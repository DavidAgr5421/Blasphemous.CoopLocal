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

    /// <summary>Puestos del anfitrión que controla ESTE cliente (uno por jugador local; 1 a 3).</summary>
    public static int[] LocalAssignedSlots = new int[] { 1 };
    /// <summary>Primer puesto asignado a este cliente (jugador principal / el que sigue la cámara y el HUD).</summary>
    public static int LocalAssignedSlot
    {
        get { return LocalAssignedSlots != null && LocalAssignedSlots.Length > 0 ? LocalAssignedSlots[0] : 1; }
        set { LocalAssignedSlots = new int[] { value }; }
    }
    public static bool IsLocalSlot(int slot)
    {
        if (LocalAssignedSlots == null) return false;
        for (int i = 0; i < LocalAssignedSlots.Length; i++) if (LocalAssignedSlots[i] == slot) return true;
        return false;
    }
    public static bool IsOnlineClient => CoopLobbyManager.State == LobbyConnectionState.Connected && !CoopLobbyManager.IsHost;
    public static bool IsOnlineHost => CoopLobbyManager.State == LobbyConnectionState.Hosting;

    // --- Sesiones de Clientes Conectados (Solo Anfitrión) ---
    /// <summary>Un cliente conectado. Puede controlar varios puestos (<see cref="Slots"/>), uno por jugador local suyo.</summary>
    public class RemoteClientSession
    {
        public CSteamID SteamId;
        public List<int> Slots = new List<int>();
        public float LastPacketTime;
        /// <summary>Primer puesto del cliente (compatibilidad con código que asume un puesto por cliente).</summary>
        public int SlotIndex { get { return Slots.Count > 0 ? Slots[0] : -1; } }
    }

    /// <summary>Estado de entrada de UN puesto remoto (cada jugador de un cliente tiene el suyo).</summary>
    public class RemoteSlotInput
    {
        public ushort LastSequence;
        public float LastPacketTime;
        public CoopButtonFlags PrevButtons;
        public CoopButtonFlags CurrentButtons;
        public CoopButtonFlags ButtonsDown;
        // Pulsaciones recibidas desde el ultimo fotograma del host (el cliente envia a 60 Hz sin fiabilidad: si llegan dos
        // paquetes entre dos fotogramas, un pulso de 1 fotograma se perdia al sobrescribir CurrentButtons).
        public CoopButtonFlags PendingDown;
        public CoopButtonFlags ButtonsUp;
        public float AxisX;
        public float AxisY;
    }

    private static readonly Dictionary<ulong, RemoteClientSession> connectedClients = new Dictionary<ulong, RemoteClientSession>();
    private static readonly RemoteClientSession[] slotClients = new RemoteClientSession[4];
    private static readonly RemoteSlotInput[] slotInputs = new RemoteSlotInput[4];

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

            if (connectedClients.Count > 0) { EnemySyncHost.Tick(); EnemySyncWorld.HostTick(); }
        }
        // 3. Lógica del Cliente: enviar inputs locales hacia el anfitrión a 60Hz
        else if (IsOnlineClient)
        {
            // Timeout de host: sin ningún Pong/WorldState/SceneChange durante 8s
            if (lastClientReceivedTime > 0f && Time.time - lastClientReceivedTime > 8f)
            {
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Error("[CoopNetwork] Sin respuesta del anfitrión durante 8s; cerrando sesión local.", Main.CoopLocal);
                }
                HandleHostDisconnect();
                return;
            }

            EnemySyncWorld.ClientSafetyTick();
            if (Input.GetKeyDown(KeyCode.F9)) CycleLocalOnlineDevices();
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

        try
        {
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

                case CoopPacketType.EnemyState:
                    if (IsOnlineClient) HandleEnemyState(sender, data);
                    break;

                case CoopPacketType.EnemyEvent:
                    if (IsOnlineClient) HandleEnemyEvent(sender, data);
                    break;

                case CoopPacketType.EnemyFullSync:
                    if (IsOnlineClient) HandleEnemyFullSync(sender, data);
                    break;

                case CoopPacketType.SceneReady:
                    if (IsOnlineHost) HandleSceneReady(sender, data);
                    break;

                case CoopPacketType.FlagChange:
                    if (IsOnlineClient) { lastClientReceivedTime = Time.time; EnemySyncWorld.ApplyFlagChange(CoopNetworkProtocol.DeserializeFlagChange(data)); }
                    break;

                case CoopPacketType.GateState:
                    if (IsOnlineClient) { lastClientReceivedTime = Time.time; EnemySyncWorld.ApplyGateState(CoopNetworkProtocol.DeserializeGateState(data)); }
                    break;

                case CoopPacketType.BossEvent:
                    if (IsOnlineClient) { lastClientReceivedTime = Time.time; EnemySyncWorld.ApplyBossEvent(CoopNetworkProtocol.DeserializeBossEvent(data)); }
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
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[CoopNetwork] Error procesando paquete tipo {packetType} de {sender}: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    // =========================================================================
    // ENEMY SYNC (stubs del paso A; el paso B los rellena)
    // =========================================================================

    private void HandleEnemyState(CSteamID sender, byte[] data)
    {
        lastClientReceivedTime = Time.time;
        EnemySyncClient.ApplyState(CoopNetworkProtocol.DeserializeEnemyState(data));
    }

    private void HandleEnemyEvent(CSteamID sender, byte[] data)
    {
        lastClientReceivedTime = Time.time;
        EnemySyncClient.ApplyEvent(CoopNetworkProtocol.DeserializeEnemyEvent(data));
    }

    private void HandleEnemyFullSync(CSteamID sender, byte[] data)
    {
        lastClientReceivedTime = Time.time;
        EnemySyncClient.ApplyFullSync(CoopNetworkProtocol.DeserializeEnemyFullSync(data));
    }

    private void HandleSceneReady(CSteamID sender, byte[] data)
    {
        EnemySyncHost.HandleSceneReady(sender, data);
    }

    internal static IEnumerable<CSteamID> ConnectedClientIds()
    {
        foreach (var c in connectedClients.Values) yield return c.SteamId;
    }

    internal static bool TryGetClientSlot(CSteamID id, out int slot)
    {
        if (connectedClients.TryGetValue(id.m_SteamID, out RemoteClientSession s)) { slot = s.SlotIndex; return true; }
        slot = -1;
        return false;
    }

    // =========================================================================
    // HANDSHAKE Y GESTIÓN DE SESIONES
    // =========================================================================

    public void SendHandshakeRequest()
    {
        if (!SteamManager.Initialized || CoopLobbyManager.HostSteamId == CSteamID.Nil) return;

        HandshakeRequestData req = new HandshakeRequestData
        {
            ProtocolVersion = CoopNetworkProtocol.CURRENT_PROTOCOL_VERSION
        };

        // Un jugador por cada jugador local de este PC (configurados en el menú de unirse). Sin configuración: uno solo.
        IList<OnlineLocalPlayer> locals = OnlineLocalPlayers.All;
        if (locals.Count == 0)
        {
            req.Players.Add(new HandshakePlayer
            {
                Name = CoopGameConfig.Slots[1].Name,
                ColorIndex = CoopGameConfig.Slots[1].ColorIndex,
                SkinId = CoopGameConfig.Slots[1].SkinId,
                Device = (byte)OnlineLocalDevice.Auto
            });
        }
        else
        {
            for (int i = 0; i < locals.Count; i++)
            {
                req.Players.Add(new HandshakePlayer
                {
                    Name = locals[i].Name,
                    ColorIndex = locals[i].ColorIndex,
                    SkinId = locals[i].SkinId,
                    Device = (byte)locals[i].Device
                });
            }
        }

        byte[] packet = CoopNetworkProtocol.SerializeHandshakeRequest(req);
        SendPacket(CoopLobbyManager.HostSteamId, packet, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Enviada solicitud de Handshake al anfitrión {CoopLobbyManager.HostSteamId} ({req.Players.Count} jugador(es) locales)", Main.CoopLocal);
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
        if (req.Players.Count == 0) req.Players.Add(new HandshakePlayer());

        // 2. Puestos para el cliente: si ya estaba conectado conserva los suyos; si no, se reparten (primero los que el
        //    anfitrión reservó como online, luego los sin asignar). Si no hay sitio para todos sus jugadores recibe los que haya.
        RemoteClientSession session;
        List<int> slots;
        bool reconnect = connectedClients.TryGetValue(sender.m_SteamID, out session);
        if (reconnect)
        {
            slots = new List<int>(session.Slots);
        }
        else
        {
            bool[] reservedFree = new bool[4];
            bool[] otherFree = new bool[4];
            for (int i = 1; i < 4; i++)
            {
                bool free = slotClients[i] == null && !CoopGameConfig.Slots[i].IsOnlineConnected;
                reservedFree[i] = free && CoopGameConfig.Slots[i].Device == CoopDeviceType.RemoteSteam;
                otherFree[i] = free && CoopGameConfig.Slots[i].Device == CoopDeviceType.Unassigned;
            }
            slots = CoopSlotAllocator.Allocate(req.Players.Count, reservedFree, otherFree);
        }

        if (slots.Count == 0)
        {
            byte[] reject = CoopNetworkProtocol.SerializeHandshakeReject("La partida está llena o no tiene ranuras cooperativas disponibles.");
            SendPacket(sender, reject, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            return;
        }

        // 3. Asignar y configurar cada puesto con los datos de su jugador
        if (!reconnect) session = new RemoteClientSession { SteamId = sender };
        session.Slots = slots;
        session.LastPacketTime = Time.time;

        int maxSlot = 0;
        for (int k = 0; k < slots.Count; k++)
        {
            int slot = slots[k];
            HandshakePlayer pl = req.Players[Math.Min(k, req.Players.Count - 1)];
            PlayerSlotConfig cfg = CoopGameConfig.Slots[slot];

            // El color del texto lo decide el host si ya configuró este puesto como online; nombre y skin los decide el jugador online.
            bool presetByHost = cfg.Device == CoopDeviceType.RemoteSteam;
            cfg.Name = string.IsNullOrEmpty(pl.Name) ? $"Jugador {slot + 1}" : pl.Name;
            if (!presetByHost) cfg.ColorIndex = pl.ColorIndex;
            cfg.SkinId = string.IsNullOrEmpty(pl.SkinId) ? "PENITENT" : pl.SkinId;
            cfg.Device = CoopDeviceType.RemoteSteam;
            cfg.IsOnlineConnected = true;

            slotClients[slot] = session;
            slotInputs[slot] = new RemoteSlotInput { LastPacketTime = Time.time };
            if (slot > maxSlot) maxSlot = slot;
        }
        connectedClients[sender.m_SteamID] = session;

        // Los puestos online deben contar dentro de la partida (al cambiar de escena se instancian según PlayerCount)
        CoopGameConfig.IsCoopEnabled = true;
        CoopGameConfig.PlayerCount = Mathf.Max(CoopGameConfig.PlayerCount, maxSlot + 1);

        // 4. Instanciar dinámicamente a los Penitentes en el mundo del Anfitrión.
        //    Si el anfitrión aún está en los menús (configurando la partida) NO se instancia nada: los invitados quedan registrados
        //    y se crean con el resto de jugadores cuando empiece la partida (SpawnAllGuestPlayers), momento en que
        //    BroadcastSceneChange los lleva a la escena.
        bool hostInGame = IsHostInGameplay();
        if (hostInGame)
        {
            for (int k = 0; k < slots.Count; k++) CoopLocal.SpawnGuestPlayerDynamically(slots[k]);
        }

        // 5. Responder HandshakeAccept con los puestos asignados y la escena actual (vacía si el anfitrión sigue en menús)
        string currentScene = hostInGame ? (Core.LevelManager?.currentLevel?.LevelName ?? "") : "";
        byte[] assigned = new byte[slots.Count];
        for (int k = 0; k < slots.Count; k++) assigned[k] = (byte)slots[k];
        HandshakeAcceptData acc = new HandshakeAcceptData
        {
            AssignedSlots = assigned,
            CurrentScene = currentScene,
            HostPlayerName = CoopGameConfig.Slots[0].Name,
            HostColorIndex = CoopGameConfig.Slots[0].ColorIndex,
            HostSkinId = CoopGameConfig.Slots[0].SkinId
        };

        byte[] acceptPacket = CoopNetworkProtocol.SerializeHandshakeAccept(acc);
        SendPacket(sender, acceptPacket, CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
        EnemySyncWorld.SendAllFlagsTo(sender);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Cliente {sender} aceptado con {slots.Count} jugador(es) en puestos [{string.Join(",", slots.ConvertAll(x => (x + 1).ToString()).ToArray())}] en escena '{currentScene}' (pidió {req.Players.Count})", Main.CoopLocal);
        }
    }

    /// <summary>true si el anfitrión ya está dentro de una partida (hay Penitente P1 y no está en el menú principal).</summary>
    internal static bool IsHostInGameplay()
    {
        try
        {
            if (Core.Logic == null || Core.Logic.Penitent == null) return false;
            if (Core.GameModeManager != null && Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU)) return false;
            return Core.LevelManager != null && Core.LevelManager.currentLevel != null;
        }
        catch { return false; }
    }

    private void HandleHandshakeAccept(CSteamID sender, byte[] data)
    {
        HandshakeAcceptData acc = CoopNetworkProtocol.DeserializeHandshakeAccept(data);
        int requested = Math.Max(1, OnlineLocalPlayers.Count);

        int[] slots = new int[acc.AssignedSlots.Length];
        for (int i = 0; i < slots.Length; i++) slots[i] = acc.AssignedSlots[i];
        LocalAssignedSlots = slots.Length > 0 ? slots : new int[] { 1 };
        lastClientReceivedTime = Time.time;

        // Si el anfitrión no tenía sitio para todos los jugadores locales, se conservan solo los aceptados
        if (OnlineLocalPlayers.Count > 0)
        {
            OnlineLocalPlayers.TrimTo(LocalAssignedSlots.Length);
            for (int i = 0; i < OnlineLocalPlayers.Count && i < LocalAssignedSlots.Length; i++)
                OnlineLocalPlayers.All[i].Slot = LocalAssignedSlots[i];
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] ¡Handshake Aceptado! Puestos asignados: {LocalAssignedSlots.Length} de {requested} (primero: P{LocalAssignedSlot + 1}). Escena anfitrión: '{acc.CurrentScene}'", Main.CoopLocal);
        }

        // Cargar la escena del anfitrión si el cliente no está en ella
        string myScene = Core.LevelManager?.currentLevel?.LevelName ?? "";
        if (!string.IsNullOrEmpty(acc.CurrentScene) && myScene != acc.CurrentScene)
        {
            CoopMainMenuCloser.CloseIfOpen();
            Core.SpawnManager?.PrepareForCommandSpawn(acc.CurrentScene);
            Core.LevelManager?.ChangeLevel(acc.CurrentScene, false, true, true);
        }

        // Si el anfitrión aún no empezó la partida, el invitado espera con el modal abierto (se cierra al llegar el cambio de escena)
        if (string.IsNullOrEmpty(acc.CurrentScene))
        {
            CoopJoinLobbyUI.ShowWaitingForHost();
            return;
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
            connectedClients.Remove(sender.m_SteamID);
            List<int> slots = new List<int>(session.Slots);
            for (int i = 0; i < slots.Count; i++)
            {
                int slot = slots[i];
                if (slot < 0 || slot >= 4) continue;
                slotClients[slot] = null;
                slotInputs[slot] = null;
                CoopLocal.DespawnGuestPlayerDynamically(slot);
                CoopGameConfig.Slots[slot].IsOnlineConnected = false;
            }
            SteamNetworking.CloseP2PSessionWithUser(sender);

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Cliente {sender} desconectado limpiamente ({slots.Count} puesto(s) liberados).", Main.CoopLocal);
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

        IList<OnlineLocalPlayer> locals = OnlineLocalPlayers.All;
        if (locals.Count == 0)
        {
            // Sin configuración de jugadores locales: un solo jugador con teclado + mando (comportamiento clásico)
            SendLocalInput(LocalAssignedSlot, OnlineLocalDevice.Auto);
            return;
        }

        for (int k = 0; k < locals.Count; k++)
        {
            int slot = locals[k].Slot >= 0 ? locals[k].Slot : (k < LocalAssignedSlots.Length ? LocalAssignedSlots[k] : -1);
            if (slot < 0) continue;
            SendLocalInput(slot, locals[k].Device);
        }
    }

    /// <summary>Lee el dispositivo de UN jugador local y envía su input al anfitrión para su puesto.</summary>
    private void SendLocalInput(int slot, OnlineLocalDevice device)
    {
        CoopButtonFlags buttons = CoopButtonFlags.None;
        float ax = 0f;
        float ay = 0f;

        bool useKeyboard = device == OnlineLocalDevice.Auto || device == OnlineLocalDevice.Keyboard;
        Rewired.Joystick pad = null;
        if (device == OnlineLocalDevice.Auto)
        {
            pad = Player2Pad.GetPadForSlot(0) ?? Player2Pad.Pad;
        }
        else
        {
            int padIdx = OnlineLocalPlayers.PadIndexOf(device);
            if (padIdx >= 0) pad = Player2Pad.GetJoystickByIndex(padIdx);
        }

        if (useKeyboard)
        {
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
            if (Input.GetKey(KeyCode.I) || Input.GetKey(KeyCode.Tab)) buttons |= CoopButtonFlags.Menu;
            if (Input.GetKey(CoopControlsConfig.GetBindings(0).KeyRange)) buttons |= CoopButtonFlags.Range;
        }

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
            if (Player2Pad.GetMenuDown(pad)) buttons |= CoopButtonFlags.Menu;
            if (Player2Pad.GetRangeHeld(pad, 0)) buttons |= CoopButtonFlags.Range;
        }

        // Empaquetar y enviar a 60Hz sin retraso (UnreliableNoDelay); un paquete por puesto
        PlayerInputData inputData = new PlayerInputData
        {
            SlotIndex = (byte)slot,
            Sequence = ++localInputSequence,
            Buttons = buttons,
            AxisX = (short)Mathf.Clamp((int)(ax * 1000f), -1000, 1000),
            AxisY = (short)Mathf.Clamp((int)(ay * 1000f), -1000, 1000)
        };

        byte[] packet = CoopNetworkProtocol.SerializePlayerInput(inputData);
        SendPacket(CoopLobbyManager.HostSteamId, packet, CHANNEL_INPUT, EP2PSend.k_EP2PSendUnreliableNoDelay);
    }

    /// <summary>F9 en un cliente online: intercambia teclado/mando solo entre SUS jugadores locales.</summary>
    private void CycleLocalOnlineDevices()
    {
        int holder;
        if (!OnlineLocalPlayers.CycleKeyboard(out holder)) return;
        OnlineLocalPlayer p = OnlineLocalPlayers.All[holder];
        Player2ModeIndicator.ShowPlayer(p.Slot >= 0 ? p.Slot : holder, p.Name);
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
    }

    private void HandlePlayerInput(CSteamID sender, byte[] data)
    {
        if (!connectedClients.TryGetValue(sender.m_SteamID, out RemoteClientSession session)) return;

        PlayerInputData input = CoopNetworkProtocol.DeserializePlayerInput(data);

        // El puesto del paquete debe pertenecer a este cliente (un cliente no puede manejar puestos ajenos)
        int slot = input.SlotIndex;
        if (slot < 1 || slot > 3 || !session.Slots.Contains(slot)) return;
        RemoteSlotInput si = slotInputs[slot];
        if (si == null) { si = new RemoteSlotInput(); slotInputs[slot] = si; }

        // Aceptar solo secuencias recientes para evitar desorden de paquetes UDP
        if (input.Sequence >= si.LastSequence || (si.LastSequence - input.Sequence > 30000))
        {
            si.LastSequence = input.Sequence;
            si.PendingDown |= input.Buttons & ~si.CurrentButtons;
            si.CurrentButtons = input.Buttons;
            si.AxisX = input.AxisX / 1000f;
            si.AxisY = input.AxisY / 1000f;
            si.LastPacketTime = Time.time;
            session.LastPacketTime = Time.time;
        }
    }

    private const float StaleInputSeconds = 0.5f;

    private void UpdateHostInputTransitions()
    {
        for (int i = 1; i < 4; i++)
        {
            RemoteSlotInput si = slotInputs[i];
            if (slotClients[i] == null || si == null) continue;

            // El cliente transmite siempre (60 Hz): si no llega nada en 0.5 s el ultimo estado esta obsoleto y un boton
            // "mantenido" (p. ej. Arriba) se quedaria pulsado para siempre. Se libera todo.
            if (Time.time - si.LastPacketTime > StaleInputSeconds)
            {
                si.CurrentButtons = CoopButtonFlags.None;
                si.AxisX = 0f;
                si.AxisY = 0f;
                si.PendingDown = CoopButtonFlags.None;
            }
            si.ButtonsDown = (si.CurrentButtons & ~si.PrevButtons) | si.PendingDown;
            si.PendingDown = CoopButtonFlags.None;
            si.ButtonsUp = ~si.CurrentButtons & si.PrevButtons;
            si.PrevButtons = si.CurrentButtons;
        }
    }

    // =========================================================================
    // CONSULTA DE INPUT REMOTO (Para Player2Input en el Host)
    // =========================================================================

    private static RemoteSlotInput RemoteInput(int slot)
    {
        if (slot < 0 || slot >= 4) return null;
        return slotClients[slot] != null ? slotInputs[slot] : null;
    }

    public static bool GetRemoteButton(int slot, CoopButtonFlags button)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null && (si.CurrentButtons & button) != 0;
    }

    public static bool GetRemoteButtonDown(int slot, CoopButtonFlags button)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null && (si.ButtonsDown & button) != 0;
    }

    public static bool GetRemoteButtonUp(int slot, CoopButtonFlags button)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null && (si.ButtonsUp & button) != 0;
    }

    public static float GetRemoteAxisX(int slot)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null ? si.AxisX : 0f;
    }

    public static float GetRemoteAxisY(int slot)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null ? si.AxisY : 0f;
    }

    public static bool HasRemoteInput(int slot)
    {
        RemoteSlotInput si = RemoteInput(slot);
        return si != null && (si.CurrentButtons != CoopButtonFlags.None || Mathf.Abs(si.AxisX) > 0.2f || Mathf.Abs(si.AxisY) > 0.2f);
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
        lastClientReceivedTime = Time.time;
        string sceneName = CoopNetworkProtocol.DeserializeSceneChange(data);
        string current = Core.LevelManager?.currentLevel?.LevelName ?? "";
        if (!string.IsNullOrEmpty(sceneName) && current != sceneName)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopNetwork] Recibido cambio de escena del Host: '{sceneName}', cargando...", Main.CoopLocal);
            }
            CoopJoinLobbyUI.Hide();
            CoopMainMenuCloser.CloseIfOpen();
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
        lastClientReceivedTime = Time.time;
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

    /// <summary>
    /// Limpia el estado de sesión (puppets, ranuras P2-P4, sesiones Steam). Debe llamarse
    /// antes de que LobbyConnectionState pase a Disconnected, porque depende de IsOnlineHost/IsOnlineClient.
    /// </summary>
    public static void ResetSessionState()
    {
        if (IsOnlineHost)
        {
            // Copia previa: DespawnGuestPlayerDynamically/HandleClientDisconnect no deben mutar la colección iterada
            List<RemoteClientSession> sessions = new List<RemoteClientSession>(connectedClients.Values);
            foreach (RemoteClientSession session in sessions)
            {
                try
                {
                    foreach (int slot in session.Slots)
                    {
                        CoopLocal.DespawnGuestPlayerDynamically(slot);
                        if (slot >= 0 && slot < 4) CoopGameConfig.Slots[slot].IsOnlineConnected = false;
                    }
                    if (SteamManager.Initialized)
                    {
                        SteamNetworking.CloseP2PSessionWithUser(session.SteamId);
                    }
                }
                catch (Exception ex)
                {
                    if (Main.CoopLocal != null)
                    {
                        Blasphemous.ModdingAPI.ModLog.Error($"[CoopNetwork] Error limpiando sesión de cliente {session.SteamId}: {ex.Message}", Main.CoopLocal);
                    }
                }
            }
            connectedClients.Clear();
            Array.Clear(slotClients, 0, slotClients.Length);
            Array.Clear(slotInputs, 0, slotInputs.Length);
        }
        else if (IsOnlineClient)
        {
            try
            {
                CoopClientWorldSync.Instance.ClearPuppets();
            }
            catch (Exception ex)
            {
                if (Main.CoopLocal != null)
                {
                    Blasphemous.ModdingAPI.ModLog.Error($"[CoopNetwork] Error limpiando títeres de cliente: {ex.Message}", Main.CoopLocal);
                }
            }
            LocalAssignedSlots = new int[] { 1 };
            OnlineLocalPlayers.Clear();
            if (_instance != null)
            {
                _instance.localInputSequence = 0;
            }
        }

        if (_instance != null)
        {
            _instance.lastClientReceivedTime = 0f;
        }
    }

    private void OnDestroy()
    {
        if (IsOnlineHost)
        {
            // Copia previa: evita reenviar Disconnect a sesiones ya cerradas por ResetSessionState
            List<RemoteClientSession> sessions = new List<RemoteClientSession>(connectedClients.Values);
            foreach (RemoteClientSession client in sessions)
            {
                SendPacket(client.SteamId, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Disconnect), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                SteamNetworking.CloseP2PSessionWithUser(client.SteamId);
            }
            connectedClients.Clear();
            Array.Clear(slotClients, 0, slotClients.Length);
            Array.Clear(slotInputs, 0, slotInputs.Length);
        }
        else if (IsOnlineClient && CoopLobbyManager.HostSteamId != CSteamID.Nil)
        {
            SendPacket(CoopLobbyManager.HostSteamId, CoopNetworkProtocol.SerializeSimple(CoopPacketType.Disconnect), CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
            SteamNetworking.CloseP2PSessionWithUser(CoopLobbyManager.HostSteamId);
        }
    }
}
