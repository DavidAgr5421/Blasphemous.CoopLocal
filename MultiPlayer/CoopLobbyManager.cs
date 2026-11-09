using System;
using System.Text;
using Steamworks;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

public enum LobbyConnectionState
{
    Disconnected,
    CreatingLobby,
    Hosting,
    SearchingLobby,
    JoiningLobby,
    Connected
}

/// <summary>
/// Gestor de Salas P2P y Lobbies de Steamworks para el modo cooperativo en línea.
/// Permite al Anfitrión crear un lobby y generar un Código de Sala de 6 caracteres,
/// y al Cliente buscar y unirse directamente a través de dicho código.
/// </summary>
public class CoopLobbyManager : MonoBehaviour
{
    private static CoopLobbyManager _instance;
    public static CoopLobbyManager Instance
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

    public static LobbyConnectionState State { get; private set; } = LobbyConnectionState.Disconnected;
    public static string ActiveRoomCode { get; private set; } = "";
    public static CSteamID CurrentLobbyId { get; private set; } = CSteamID.Nil;
    public static CSteamID HostSteamId { get; private set; } = CSteamID.Nil;
    public static bool IsHost => State == LobbyConnectionState.Hosting;
    public static bool IsClientConnected => State == LobbyConnectionState.Connected;

    // CallResults y Callbacks de Steam
    private CallResult<LobbyCreated_t> m_LobbyCreatedCallResult;
    private CallResult<LobbyEnter_t> m_LobbyEnterCallResult;
    private CallResult<LobbyMatchList_t> m_LobbyMatchListCallResult;
    private Callback<P2PSessionRequest_t> m_P2PSessionRequestCallback;
    private Callback<P2PSessionConnectFail_t> m_P2PSessionConnectFailCallback;

    private Action<bool, string> pendingCreateCallback;
    private Action<bool, string> pendingJoinCallback;
    private string targetSearchCode = "";

    public static void Initialize()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("CoopLobbyManager");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopLobbyManager>();
    }

    private void Awake()
    {
        _instance = this;
        SetupCallbacks();
    }

    private void SetupCallbacks()
    {
        try
        {
            if (SteamManager.Initialized)
            {
                m_LobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                m_LobbyEnterCallResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
                m_LobbyMatchListCallResult = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);

                m_P2PSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                m_P2PSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Error($"[CoopLobbyManager] Error configurando Steam Callbacks: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    /// <summary>
    /// Genera un código de sala alfanumérico limpio de 6 caracteres sin caracteres ambiguos (0/O, 1/I).
    /// </summary>
    public static string GenerateFriendlyRoomCode()
    {
        const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        System.Random rnd = new System.Random();
        char[] code = new char[6];
        for (int i = 0; i < 6; i++)
        {
            code[i] = chars[rnd.Next(chars.Length)];
        }
        return new string(code);
    }

    /// <summary>
    /// Crea un nuevo lobby de Steam para hospedar una partida cooperativa.
    /// </summary>
    public void CreateHostLobby(Action<bool, string> onComplete = null)
    {
        pendingCreateCallback = onComplete;

        if (!SteamManager.Initialized)
        {
            // Modo offline / fallback para pruebas locales sin Steam
            ActiveRoomCode = GenerateFriendlyRoomCode();
            State = LobbyConnectionState.Hosting;
            pendingCreateCallback?.Invoke(true, ActiveRoomCode);
            pendingCreateCallback = null;
            return;
        }

        State = LobbyConnectionState.CreatingLobby;
        SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);
        m_LobbyCreatedCallResult.Set(call);
    }

    private void OnLobbyCreated(LobbyCreated_t param, bool bIOFailure)
    {
        if (bIOFailure || param.m_eResult != EResult.k_EResultOK)
        {
            State = LobbyConnectionState.Disconnected;
            pendingCreateCallback?.Invoke(false, "Fallo al crear la sala de Steam.");
            pendingCreateCallback = null;
            return;
        }

        CurrentLobbyId = new CSteamID(param.m_ulSteamIDLobby);
        HostSteamId = SteamUser.GetSteamID();
        ActiveRoomCode = GenerateFriendlyRoomCode();

        // Configurar metadatos en el lobby de Steam
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "code", ActiveRoomCode);
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "game", "Blasphemous.CoopLocal");
        SteamMatchmaking.SetLobbyData(CurrentLobbyId, "host_name", SteamFriends.GetPersonaName());

        State = LobbyConnectionState.Hosting;

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopLobbyManager] Lobby creado exitosamente. ID: {CurrentLobbyId}, Código de Sala: {ActiveRoomCode}", Main.CoopLocal);
        }

        pendingCreateCallback?.Invoke(true, ActiveRoomCode);
        pendingCreateCallback = null;
    }

    /// <summary>
    /// Busca un lobby en Steam que coincida con el código de sala proporcionado y se une a él.
    /// </summary>
    public void JoinLobbyByCode(string roomCode, Action<bool, string> onComplete = null)
    {
        pendingJoinCallback = onComplete;
        targetSearchCode = roomCode != null ? roomCode.Trim().ToUpperInvariant() : "";

        if (string.IsNullOrEmpty(targetSearchCode) || targetSearchCode.Length < 4)
        {
            pendingJoinCallback?.Invoke(false, "El código de sala debe tener al menos 4 caracteres.");
            pendingJoinCallback = null;
            return;
        }

        if (!SteamManager.Initialized)
        {
            // Modo offline / fallback
            if (targetSearchCode == ActiveRoomCode || targetSearchCode == "TEST" || targetSearchCode == "LOCAL")
            {
                State = LobbyConnectionState.Connected;
                pendingJoinCallback?.Invoke(true, "¡Conectado exitosamente (Modo Local)!");
            }
            else
            {
                pendingJoinCallback?.Invoke(false, "Steam no está activo y la sala no coincide.");
            }
            pendingJoinCallback = null;
            return;
        }

        State = LobbyConnectionState.SearchingLobby;

        // Filtrar lobbies por código de sala
        SteamMatchmaking.AddRequestLobbyListStringFilter("code", targetSearchCode, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", "Blasphemous.CoopLocal", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(1);

        SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
        m_LobbyMatchListCallResult.Set(call);
    }

    private void OnLobbyMatchList(LobbyMatchList_t param, bool bIOFailure)
    {
        if (bIOFailure || param.m_nLobbiesMatching == 0)
        {
            State = LobbyConnectionState.Disconnected;
            pendingJoinCallback?.Invoke(false, $"No se encontró ninguna sala con el código '{targetSearchCode}'.");
            pendingJoinCallback = null;
            return;
        }

        CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
        State = LobbyConnectionState.JoiningLobby;

        SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobbyId);
        m_LobbyEnterCallResult.Set(call);
    }

    private void OnLobbyEntered(LobbyEnter_t param, bool bIOFailure)
    {
        if (bIOFailure || param.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            State = LobbyConnectionState.Disconnected;
            pendingJoinCallback?.Invoke(false, "No se pudo acceder a la sala de Steam (acceso denegado o llena).");
            pendingJoinCallback = null;
            return;
        }

        CurrentLobbyId = new CSteamID(param.m_ulSteamIDLobby);
        HostSteamId = SteamMatchmaking.GetLobbyOwner(CurrentLobbyId);
        ActiveRoomCode = targetSearchCode;
        State = LobbyConnectionState.Connected;

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopLobbyManager] ¡Unido a la sala con éxito! Host ID: {HostSteamId}", Main.CoopLocal);
        }

        pendingJoinCallback?.Invoke(true, "¡Conexión establecida con el anfitrión!");
        pendingJoinCallback = null;
    }

    private void OnP2PSessionRequest(P2PSessionRequest_t param)
    {
        // Aceptar conexiones entrantes de miembros de la sala
        SteamNetworking.AcceptP2PSessionWithUser(param.m_steamIDRemote);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopLobbyManager] Sesión P2P aceptada con {param.m_steamIDRemote}", Main.CoopLocal);
        }
    }

    private void OnP2PSessionConnectFail(P2PSessionConnectFail_t param)
    {
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Error($"[CoopLobbyManager] Error de conexión P2P con {param.m_steamIDRemote}: error {param.m_eP2PSessionError}", Main.CoopLocal);
        }
    }

    /// <summary>
    /// Sale de la sala actual y restablece el estado a Desconectado.
    /// </summary>
    public void LeaveLobby()
    {
        if (CurrentLobbyId != CSteamID.Nil && SteamManager.Initialized)
        {
            SteamMatchmaking.LeaveLobby(CurrentLobbyId);
        }

        CurrentLobbyId = CSteamID.Nil;
        HostSteamId = CSteamID.Nil;
        ActiveRoomCode = "";
        State = LobbyConnectionState.Disconnected;
    }
}
