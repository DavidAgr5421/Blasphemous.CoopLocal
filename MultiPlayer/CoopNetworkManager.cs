using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using Framework.Managers;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Gestor principal de red P2P a través de Steamworks.
/// Controla el ciclo de vida de los paquetes (Canales 0, 1 y 2), el handshake de conexión,
/// la transmisión continua de inputs a 60Hz y la replicación del estado del mundo a 30Hz.
/// </summary>
public class CoopNetworkManager : MonoBehaviour
{
    private static CoopNetworkManager _instance;
    public static CoopNetworkManager Instance
    {
        get
        {
            if (_instance == null)
            {
                Initialize();
            }
            return _instance;
        }
    }

    public const int CHANNEL_RELIABLE = 0; // Handshake, Cambio de Escena, Desconexión
    public const int CHANNEL_INPUT    = 1; // Transmisión de Inputs de Clientes (60Hz)
    public const int CHANNEL_STATE    = 2; // Replicación de Estado del Mundo (30Hz)

    public static int LocalAssignedSlot { get; set; } = 1;
    public static bool IsOnlineClient => CoopLobbyManager.State == LobbyConnectionState.Connected && !CoopLobbyManager.IsHost;
    public static bool IsOnlineHost => CoopLobbyManager.State == LobbyConnectionState.Hosting;

    // --- Sesiones de Clientes Conectados (Solo Anfitrión) ---
    public class RemoteClientSession
    {
        public CSteamID SteamId;
        public int SlotIndex;
        public float LastPacketTime;
        public ushort LastSequence;
        public CoopButtonFlags PrevButtons;
        public CoopButtonFlags CurrentButtons;
        public CoopButtonFlags ButtonsDown;
        public CoopButtonFlags ButtonsUp;
        public float AxisX;
        public float AxisY;
    }

    private static readonly Dictionary<ulong, RemoteClientSession> connectedClients = new Dictionary<ulong, RemoteClientSession>();
    private static readonly RemoteClientSession[] slotClients = new RemoteClientSession[4];

    // Buffer reusable para recibir paquetes de Steam
    private readonly byte[] receiveBuffer = new byte[4096];

    // Temporizadores
    private ushort localInputSequence = 0;
    private ushort hostWorldSequence = 0;
    private float nextWorldBroadcastTime = 0f;
    private float nextClientPingTime = 0f;
    private float lastClientReceivedTime = 0f;

    public static void Initialize()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("CoopNetworkManager");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopNetworkManager>();
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Update()
    {
        if (!SteamManager.Initialized) return;

        // 1. Procesar paquetes entrantes en todos los canales
        ProcessIncomingPackets();

        // 2. Lógica del Anfitrión: actualizar transiciones de botones y difundir estado
        if (IsOnlineHost)
        {
            UpdateHostInputTransitions();
            CheckClientTimeouts();

            if (Time.time >= nextWorldBroadcastTime && connectedClients.Count > 0)
            {
                nextWorldBroadcastTime = Time.time + (1f / 30f); // 30Hz
                BroadcastWorldState();
            }
        }
        // 3. Lógica del Cliente: enviar inputs locales hacia el anfitrión a 60Hz
        else if (IsOnlineClient)
        {
            StreamLocalClientInput();

            if (Time.time >= nextClientPingTime)
            {
                nextClientPingTime = Time.time + 2.0f;
                SendPingToHost();
            }
        }
    }

    // =========================================================================
    // PROCESAMIENTO DE PAQUETES (RECEPCIÓN)
    // =========================================================================

    private void ProcessIncomingPackets()
    {
        // Leer paquetes en los tres canales
        ReadPacketsOnChannel(CHANNEL_RELIABLE);
        ReadPacketsOnChannel(CHANNEL_INPUT);
        ReadPacketsOnChannel(CHANNEL_STATE);
    }

    private void ReadPacketsOnChannel(int channel)
    {
        uint msgSize;
        while (SteamNetworking.IsP2PPacketAvailable(out msgSize, channel))
        {
            if (msgSize == 0) break;

            byte[] buffer = (msgSize <= receiveBuffer.Length) ? receiveBuffer : new byte[msgSize];
            uint bytesRead;
            CSteamID remoteSteamId;

            if (SteamNetworking.ReadP2PPacket(buffer, (uint)buffer.Length, out bytesRead, out remoteSteamId, channel))
            {
                if (bytesRead > 0)
                {
                    byte[] packetData = new byte[bytesRead];
                    Array.Copy(buffer, 0, packetData, 0, bytesRead);
                    HandlePacket(remoteSteamId, packetData, channel);
                }
            }
        }
    }

    private void HandlePacket(CSteamID sender, byte[] data, int channel)
    {
        if (data == null || data.Length == 0) return;
        CoopPacketType packetType = (CoopPacketType)data[0];

        switch (packetType)
        {
            case CoopPacketType.HandshakeRequest:
                if (IsOnlineHost) HandleHandshakeRequest(sender, data);
                break;

            case CoopPacketType.HandshakeAccept:
                if (IsOnlineClient) HandleHandshakeAccept(sender, data);
                break;

            case CoopPacketType.HandshakeReject:
                if (IsOnlineClient) HandleHandshakeReject(sender, data);
                break;

            case CoopPacketType.PlayerInput:
                if (IsOnlineHost) HandlePlayerInput(sender, data);
                break;

            case CoopPacketType.SceneChange:
                if (IsOnlineClient) HandleSceneChange(sender, data);
                break;

            case CoopPacketType.WorldState:
                if (IsOnlineClient) HandleWorldState(sender, data);
                break;

            case CoopPacketType.Ping:
                // Responder Pong en canal confiable
                SendPacket(sender, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Pong), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                break;

            case CoopPacketType.Pong:
                lastClientReceivedTime = Time.time;
                break;

            case CoopPacketType.Disconnect:
                if (IsOnlineHost)
                {
                    HandleClientDisconnect(sender);
                }
                else
                {
                    HandleHostDisconnect();
                }
                break;
        }
    }

    // =========================================================================
    // HANDSHAKE Y GESTIÓN DE SESIONES
    // =========================================================================

    public void SendHandshakeRequest()
    {
        if (!SteamManager.Initialized || CoopLobbyManager.HostSteamId == CSteamID.Nil) return;

        HandshakeRequestData req = new HandshakeRequestData
        {
            ProtocolVersion = CoopNetworkProtocol.CURRENT_PROTOCOL_VERSION,
            PlayerName = CoopGameConfig.Slots[1].Name,
            ColorIndex = CoopGameConfig.Slots[1].ColorIndex,
            SkinId = CoopGameConfig.Slots[1].SkinId
        };

        byte[] packet = CoopNetworkProtocol.SerializeHandshakeRequest(req);
        SendPacket(CoopLobbyManager.HostSteamId, packet, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Enviada solicitud de Handshake al anfitrión {CoopLobbyManager.HostSteamId}", Main.CoopLocal);
        }
    }

    private void HandleHandshakeRequest(CSteamID sender, byte[] data)
    {
        HandshakeRequestData req = CoopNetworkProtocol.DeserializeHandshakeRequest(data);

        // 1. Verificar versión del protocolo
        if (req.ProtocolVersion != CoopNetworkProtocol.CURRENT_PROTOCOL_VERSION)
        {
            byte[] reject = CoopNetworkProtocol.SerializeHandshakeReject($"Versión de mod incompatible (Host: v{CoopNetworkProtocol.CURRENT_PROTOCOL_VERSION}, Cliente: v{req.ProtocolVersion})");
            SendPacket(sender, reject, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            return;
        }

        // 2. Buscar una ranura disponible para el invitado
        int targetSlot = -1;

        // Primero buscar si ya tenía una ranura asignada
        if (connectedClients.TryGetValue(sender.m_SteamID, out RemoteClientSession existing))
        {
            targetSlot = existing.SlotIndex;
        }
        else
        {
            // Buscar una ranura configurada como RemoteSteam disponible, o una vacía P2..P4
            for (int i = 1; i < 4; i++)
            {
                if (CoopGameConfig.Slots[i].Device == CoopDeviceType.RemoteSteam && !CoopGameConfig.Slots[i].IsOnlineConnected)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot == -1)
            {
                // Si ninguna estaba pre-configurada como RemoteSteam, tomar la primera no conectada
                for (int i = 1; i < 4; i++)
                {
                    if (slotClients[i] == null && !CoopGameConfig.Slots[i].IsOnlineConnected)
                    {
                        targetSlot = i;
                        break;
                    }
                }
            }
        }

        if (targetSlot == -1)
        {
            byte[] reject = CoopNetworkProtocol.SerializeHandshakeReject("La partida está llena o no tiene ranuras cooperativas disponibles.");
            SendPacket(sender, reject, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            return;
        }

        // 3. Asignar y configurar la ranura
        CoopGameConfig.Slots[targetSlot].Name = string.IsNullOrEmpty(req.PlayerName) ? $"Jugador {targetSlot + 1}" : req.PlayerName;
        CoopGameConfig.Slots[targetSlot].ColorIndex = req.ColorIndex;
        CoopGameConfig.Slots[targetSlot].SkinId = string.IsNullOrEmpty(req.SkinId) ? "PENITENT" : req.SkinId;
        CoopGameConfig.Slots[targetSlot].Device = CoopDeviceType.RemoteSteam;
        CoopGameConfig.Slots[targetSlot].IsOnlineConnected = true;

        RemoteClientSession session = new RemoteClientSession
        {
            SteamId = sender,
            SlotIndex = targetSlot,
            LastPacketTime = Time.time
        };
        connectedClients[sender.m_SteamID] = session;
        slotClients[targetSlot] = session;

        // 4. Instanciar dinámicamente al Penitente del jugador en el mundo del Anfitrión
        CoopLocal.SpawnGuestPlayerDynamically(targetSlot);

        // 5. Responder HandshakeAccept con la ranura asignada y la escena actual
        string currentScene = Core.LevelManager?.currentLevel?.LevelName ?? "";
        HandshakeAcceptData acc = new HandshakeAcceptData
        {
            AssignedSlot = (byte)targetSlot,
            CurrentScene = currentScene,
            HostPlayerName = CoopGameConfig.Slots[0].Name,
            HostColorIndex = CoopGameConfig.Slots[0].ColorIndex,
            HostSkinId = CoopGameConfig.Slots[0].SkinId
        };

        byte[] acceptPacket = CoopNetworkProtocol.SerializeHandshakeAccept(acc);
        SendPacket(sender, acceptPacket, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Cliente {sender} aceptado en Ranura {targetSlot + 1} ({req.PlayerName}) en escena '{currentScene}'", Main.CoopLocal);
        }
    }

    private void HandleHandshakeAccept(CSteamID sender, byte[] data)
    {
        HandshakeAcceptData acc = CoopNetworkProtocol.DeserializeHandshakeAccept(data);
        LocalAssignedSlot = acc.AssignedSlot;
        lastClientReceivedTime = Time.time;

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] ¡Handshake Aceptado! Asignado a Ranura P{LocalAssignedSlot + 1}. Escena anfitrión: '{acc.CurrentScene}'", Main.CoopLocal);
        }

        // Cargar la escena del anfitrión si el cliente no está en ella
        string myScene = Core.LevelManager?.currentLevel?.LevelName ?? "";
        if (!string.IsNullOrEmpty(acc.CurrentScene) && myScene != acc.CurrentScene)
        {
            Core.SpawnManager?.PrepareForCommandSpawn(acc.CurrentScene);
            Core.LevelManager?.ChangeLevel(acc.CurrentScene, false, true, true);
        }

        // Cerrar la interfaz modal de unión
        CoopJoinLobbyUI.Hide();
    }

    private void HandleHandshakeReject(CSteamID sender, byte[] data)
    {
        string reason = CoopNetworkProtocol.DeserializeHandshakeReject(data);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Error($"[CoopNetwork] Conexión rechazada por el anfitrión: {reason}", Main.CoopLocal);
        }
        CoopLobbyManager.Instance.LeaveLobby();
    }

    private void HandleClientDisconnect(CSteamID sender)
    {
        if (connectedClients.TryGetValue(sender.m_SteamID, out RemoteClientSession session))
        {
            int slot = session.SlotIndex;
            connectedClients.Remove(sender.m_SteamID);
            if (slot >= 0 && slot < 4) slotClients[slot] = null;

            CoopLocal.DespawnGuestPlayerDynamically(slot);
            SteamNetworking.CloseP2PSessionWithUser(sender);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Jugador en Ranura {slot + 1} se ha desconectado limpiamente.", Main.CoopLocal);
            }
        }
    }

    private void HandleHostDisconnect()
    {
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info("[CoopNetwork] El Anfitrión ha cerrado la partida.", Main.CoopLocal);
        }
        CoopLobbyManager.Instance.LeaveLobby();
    }

    private void CheckClientTimeouts()
    {
        List<ulong> toRemove = null;
        foreach (var kvp in connectedClients)
        {
            if (Time.time - kvp.Value.LastPacketTime > 6.0f) // 6 segundos sin paquetes
            {
                if (toRemove == null) toRemove = new List<ulong>();
                toRemove.Add(kvp.Key);
            }
        }

        if (toRemove != null)
        {
            foreach (ulong id in toRemove)
            {
                HandleClientDisconnect(new CSteamID(id));
            }
        }
    }

    // =========================================================================
    // STREAMING DE INPUTS (FASE 4 - CLIENTE -> HOST)
    // =========================================================================

    private void StreamLocalClientInput()
    {
        if (CoopLobbyManager.HostSteamId == CSteamID.Nil) return;

        // 1. Leer teclado o mando local del cliente
        CoopButtonFlags buttons = CoopButtonFlags.None;
        float ax = 0f;
        float ay = 0f;

        // Comprobación de teclado
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) { buttons |= CoopButtonFlags.Left; ax -= 1f; }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) { buttons |= CoopButtonFlags.Right; ax += 1f; }
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) { buttons |= CoopButtonFlags.Up; ay += 1f; }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) { buttons |= CoopButtonFlags.Down; ay -= 1f; }

        if (Input.GetKey(KeyCode.Space)) buttons |= CoopButtonFlags.Jump;
        if (Input.GetKey(KeyCode.K)) buttons |= CoopButtonFlags.Attack;
        if (Input.GetKey(KeyCode.LeftShift)) buttons |= CoopButtonFlags.Dash;
        if (Input.GetKey(KeyCode.J)) buttons |= CoopButtonFlags.Parry;
        if (Input.GetKey(KeyCode.R)) buttons |= CoopButtonFlags.Heal;
        if (Input.GetKey(KeyCode.E)) buttons |= CoopButtonFlags.Interact;
        if (Input.GetKey(KeyCode.Q)) buttons |= CoopButtonFlags.Prayer;
        if (Input.GetKey(KeyCode.Escape)) buttons |= CoopButtonFlags.Pause;
        if (Input.GetKey(KeyCode.I)) buttons |= CoopButtonFlags.Menu;

        // Comprobación opcional de mando Rewired si está conectado
        var pad = Player2Pad.GetPadForSlot(0) ?? Player2Pad.Pad;
        if (pad != null)
        {
            float padX = pad.GetAxis(Player2Pad.AxisLeftStickX);
            float padY = pad.GetAxis(Player2Pad.AxisLeftStickY);
            if (Mathf.Abs(padX) > 0.3f) { ax = padX; if (padX > 0) buttons |= CoopButtonFlags.Right; else buttons |= CoopButtonFlags.Left; }
            if (Mathf.Abs(padY) > 0.3f) { ay = padY; if (padY > 0) buttons |= CoopButtonFlags.Up; else buttons |= CoopButtonFlags.Down; }

            if (Player2Pad.GetJumpHeld(pad)) buttons |= CoopButtonFlags.Jump;
            if (Player2Pad.GetAttackHeld(pad)) buttons |= CoopButtonFlags.Attack;
            if (Player2Pad.GetDashHeld(pad)) buttons |= CoopButtonFlags.Dash;
            if (Player2Pad.GetParryDown(pad)) buttons |= CoopButtonFlags.Parry;
            if (Player2Pad.GetHealDown(pad)) buttons |= CoopButtonFlags.Heal;
            if (Player2Pad.GetInteractDown(pad)) buttons |= CoopButtonFlags.Interact;
            if (Player2Pad.GetStartDown(pad)) buttons |= CoopButtonFlags.Pause;
        }

        // 2. Empaquetar y enviar a 60Hz sin retraso (UnreliableNoDelay)
        PlayerInputData inputData = new PlayerInputData
        {
            SlotIndex = (byte)LocalAssignedSlot,
            Sequence = ++localInputSequence,
            Buttons = buttons,
            AxisX = (short)Mathf.Clamp((int)(ax * 1000f), -1000, 1000),
            AxisY = (short)Mathf.Clamp((int)(ay * 1000f), -1000, 1000)
        };

        byte[] packet = CoopNetworkProtocol.SerializePlayerInput(inputData);
        SendPacket(CoopLobbyManager.HostSteamId, packet, CHANNEL_INPUT, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    private void HandlePlayerInput(CSteamID sender, byte[] data)
    {
        if (!connectedClients.TryGetValue(sender.m_SteamID, out RemoteClientSession session)) return;

        PlayerInputData input = CoopNetworkProtocol.DeserializePlayerInput(data);

        // Aceptar solo secuencias recientes para evitar desorden de paquetes UDP
        if (input.Sequence >= session.LastSequence || (session.LastSequence - input.Sequence > 30000))
        {
            session.LastSequence = input.Sequence;
            session.CurrentButtons = input.Buttons;
            session.AxisX = input.AxisX / 1000f;
            session.AxisY = input.AxisY / 1000f;
            session.LastPacketTime = Time.time;
        }
    }

    private void UpdateHostInputTransitions()
    {
        for (int i = 1; i < 4; i++)
        {
            RemoteClientSession session = slotClients[i];
            if (session != null)
            {
                session.ButtonsDown = session.CurrentButtons & ~session.PrevButtons;
                session.ButtonsUp = ~session.CurrentButtons & session.PrevButtons;
                session.PrevButtons = session.CurrentButtons;
            }
        }
    }

    // =========================================================================
    // CONSULTA DE INPUT REMOTO (Para Player2Input en el Host)
    // =========================================================================

    public static bool GetRemoteButton(int slot, CoopButtonFlags button)
    {
        if (slot < 0 || slot >= 4) return false;
        RemoteClientSession session = slotClients[slot];
        return session != null && (session.CurrentButtons & button) != 0;
    }

    public static bool GetRemoteButtonDown(int slot, CoopButtonFlags button)
    {
        if (slot < 0 || slot >= 4) return false;
        RemoteClientSession session = slotClients[slot];
        return session != null && (session.ButtonsDown & button) != 0;
    }

    public static bool GetRemoteButtonUp(int slot, CoopButtonFlags button)
    {
        if (slot < 0 || slot >= 4) return false;
        RemoteClientSession session = slotClients[slot];
        return session != null && (session.ButtonsUp & button) != 0;
    }

    public static float GetRemoteAxisX(int slot)
    {
        if (slot < 0 || slot >= 4) return 0f;
        RemoteClientSession session = slotClients[slot];
        return session != null ? session.AxisX : 0f;
    }

    public static float GetRemoteAxisY(int slot)
    {
        if (slot < 0 || slot >= 4) return 0f;
        RemoteClientSession session = slotClients[slot];
        return session != null ? session.AxisY : 0f;
    }

    public static bool HasRemoteInput(int slot)
    {
        if (slot < 0 || slot >= 4) return false;
        RemoteClientSession session = slotClients[slot];
        return session != null && (session.CurrentButtons != CoopButtonFlags.None || Mathf.Abs(session.AxisX) > 0.2f || Mathf.Abs(session.AxisY) > 0.2f);
    }

    // =========================================================================
    // REPLICACIÓN DE ESTADO DEL MUNDO (HOST -> CLIENTES)
    // =========================================================================

    public void BroadcastSceneChange(string sceneName)
    {
        if (!IsOnlineHost || connectedClients.Count == 0) return;

        byte[] packet = CoopNetworkProtocol.SerializeSceneChange(sceneName);
        foreach (var client in connectedClients.Values)
        {
            SendPacket(client.SteamId, packet, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Notificado cambio de escena a clientes: '{sceneName}'", Main.CoopLocal);
        }
    }

    private void HandleSceneChange(CSteamID sender, byte[] data)
    {
        string sceneName = CoopNetworkProtocol.DeserializeSceneChange(data);
        string current = Core.LevelManager?.currentLevel?.LevelName ?? "";
        if (!string.IsNullOrEmpty(sceneName) && current != sceneName)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Recibido cambio de escena del Host: '{sceneName}', cargando...", Main.CoopLocal);
            }
            Core.SpawnManager?.PrepareForCommandSpawn(sceneName);
            Core.LevelManager?.ChangeLevel(sceneName, false, true, true);
        }
    }

    private void BroadcastWorldState()
    {
        List<PlayerStateSync> activeList = new List<PlayerStateSync>();
        for (int i = 0; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            var slot = CoopPlayerRegistry.GetSlot(i);
            var pen = slot?.Instance;
            if (pen == null && i == 0) pen = Core.Logic?.Penitent;
            if (pen == null) continue;

            PlayerStateSync sync = new PlayerStateSync
            {
                SlotIndex = (byte)i,
                PosX = pen.transform.position.x,
                PosY = pen.transform.position.y,
                FacingRight = (pen.SpriteRenderer != null) ? !pen.SpriteRenderer.flipX : true,
                AnimIndex = (pen.Animator != null) ? CoopAnimationStates.GetAnimationIndex(pen.Animator) : (byte)0,
                CurrentHp = (pen.Stats?.Life != null) ? pen.Stats.Life.Current : 100f,
                MaxHp = (pen.Stats?.Life != null) ? pen.Stats.Life.CurrentMax : 100f,
                CurrentFervour = (pen.Stats?.Fervour != null) ? pen.Stats.Fervour.Current : 0f,
                MaxFervour = (pen.Stats?.Fervour != null) ? pen.Stats.Fervour.CurrentMax : 100f,
                Purge = (pen.Stats?.Purge != null) ? (int)pen.Stats.Purge.Current : 0,
                Flasks = (byte)((pen.Stats?.Flask != null) ? pen.Stats.Flask.Current : 0),
                IsDead = (pen.Status != null) ? pen.Status.Dead : false
            };
            activeList.Add(sync);
        }

        if (activeList.Count == 0) return;

        WorldStateData state = new WorldStateData
        {
            Sequence = ++hostWorldSequence,
            Players = activeList.ToArray()
        };

        byte[] packet = CoopNetworkProtocol.SerializeWorldState(state);
        foreach (var client in connectedClients.Values)
        {
            SendPacket(client.SteamId, packet, CHANNEL_STATE, EP2PSend.k_EP2PSendUnreliableNoDelay);
        }
    }

    private void HandleWorldState(CSteamID sender, byte[] data)
    {
        WorldStateData state = CoopNetworkProtocol.DeserializeWorldState(data);
        CoopClientWorldSync.Instance.ApplyWorldState(state);
    }

    private void SendPingToHost()
    {
        if (CoopLobbyManager.HostSteamId != CSteamID.Nil)
        {
            SendPacket(CoopLobbyManager.HostSteamId, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Ping), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
        }
    }

    // =========================================================================
    // UTILIDADES DE ENVÍO STEAMWORKS P2P
    // =========================================================================

    public static bool SendPacket(CSteamID target, byte[] data, int channel, EP2PSend sendType)
    {
        if (!SteamManager.Initialized || target == CSteamID.Nil || data == null || data.Length == 0) return false;
        return SteamNetworking.SendP2PPacket(target, data, (uint)data.Length, sendType, channel);
    }

    private void OnDestroy()
    {
        if (IsOnlineHost)
        {
            foreach (var client in connectedClients.Values)
            {
                SendPacket(client.SteamId, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Disconnect), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                SteamNetworking.CloseP2PSessionWithUser(client.SteamId);
            }
            connectedClients.Clear();
        }
        else if (IsOnlineClient && CoopLobbyManager.HostSteamId != CSteamID.Nil)
        {
            SendPacket(CoopLobbyManager.HostSteamId, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Disconnect), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            SteamNetworking.CloseP2PSessionWithUser(CoopLobbyManager.HostSteamId);
        }
    }
}
