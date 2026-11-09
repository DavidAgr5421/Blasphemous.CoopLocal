using System.Text;
using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Interfaz gráfica para que un jugador invitado ingrese el Código de Sala
/// y se una a la partida del Anfitrión a través de Steam P2P.
/// Permite configurar su nombre, color de texto y aspecto (skin) cosmético.
/// </summary>
public class CoopJoinLobbyUI : MonoBehaviour
{
    private static CoopJoinLobbyUI _instance;
    public static CoopJoinLobbyUI Instance
    {
        get
        {
            if (_instance == null)
            {
                CreateUI();
            }
            return _instance;
        }
    }

    public static bool IsOpen { get; private set; }

    private GameObject rootCanvasObj;
    private Text headerTitleText;
    private Text headerSubtitleText;

    // Campos interactivos
    private Text roomCodeValueText;
    private Text playerNameValueText;
    private Text playerColorValueText;
    private Text playerSkinValueText;
    private Image skinPreviewImage;
    private Text statusFeedbackText;
    private Text connectButtonText;
    private Image connectButtonBg;

    // Estado del modal
    private int selectedRowIndex = 0; // 0=RoomCode, 1=PlayerName, 2=Color, 3=Skin, 4=ConnectBtn, 5=CancelBtn
    private string currentRoomCode = "";
    private string currentPlayerName = "Invitado";
    private int currentColorIndex = 1;
    private int currentSkinIndex = 0;

    private bool isEditingName = false;
    private float inputCooldown = 0f;
    private float cursorBlinkTimer = 0f;
    private bool cursorVisible = true;

    private Action onJoinedSuccessCallback;
    private Action onBackCallback;

    private Font gameFont;

    private static void CreateUI()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("CoopJoinLobbyUI");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopJoinLobbyUI>();
        _instance.BuildInterface();
    }

    public static void Show(Action onJoined = null, Action onBack = null)
    {
        CreateUI();
        _instance.onJoinedSuccessCallback = onJoined;
        _instance.onBackCallback = onBack;
        _instance.Open();
    }

    public static void Hide()
    {
        if (_instance != null && IsOpen)
        {
            _instance.Close();
        }
    }

    private void BuildInterface()
    {
        if (rootCanvasObj != null) return;

        gameFont = Font.CreateDynamicFontFromOSFont("Trajan Pro", 18);
        if (gameFont == null)
        {
            Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
            if (fonts != null && fonts.Length > 0)
            {
                for (int i = 0; i < fonts.Length; i++)
                {
                    if (fonts[i] != null && (fonts[i].name.Contains("Trajan") || fonts[i].name.Contains("Pixel") || fonts[i].name.Contains("Blasphemous")))
                    {
                        gameFont = fonts[i];
                        break;
                    }
                }
                if (gameFont == null && fonts.Length > 0) gameFont = fonts[0];
            }
        }

        rootCanvasObj = new GameObject("JoinLobbyCanvas");
        rootCanvasObj.transform.SetParent(transform, false);
        Canvas canvas = rootCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32500;

        CanvasScaler scaler = rootCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        rootCanvasObj.AddComponent<GraphicRaycaster>();

        // Fondo oscuro total
        GameObject darkOverlay = new GameObject("DarkOverlay");
        darkOverlay.transform.SetParent(rootCanvasObj.transform, false);
        RectTransform doRt = darkOverlay.AddComponent<RectTransform>();
        doRt.anchorMin = Vector2.zero;
        doRt.anchorMax = Vector2.one;
        doRt.sizeDelta = Vector2.zero;
        Image doImg = darkOverlay.AddComponent<Image>();
        doImg.color = new Color(0.03f, 0.03f, 0.04f, 0.90f);

        // Panel Central
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(rootCanvasObj.transform, false);
        RectTransform panelRt = panelObj.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(850f, 620f);

        Image pnlImg = panelObj.AddComponent<Image>();
        pnlImg.color = new Color(0.08f, 0.08f, 0.10f, 0.98f);
        Outline pnlOut = panelObj.AddComponent<Outline>();
        pnlOut.effectColor = new Color(0.75f, 0.55f, 0.20f, 0.85f);
        pnlOut.effectDistance = new Vector2(2.5f, -2.5f);

        // Título Principal
        GameObject hdrObj = new GameObject("Title");
        hdrObj.transform.SetParent(panelObj.transform, false);
        RectTransform hdrRt = hdrObj.AddComponent<RectTransform>();
        hdrRt.anchoredPosition = new Vector2(0f, 255f);
        hdrRt.sizeDelta = new Vector2(800f, 40f);
        headerTitleText = hdrObj.AddComponent<Text>();
        headerTitleText.font = gameFont;
        headerTitleText.fontSize = 24;
        headerTitleText.alignment = TextAnchor.MiddleCenter;
        headerTitleText.color = new Color(1f, 0.88f, 0.45f, 1f);
        headerTitleText.text = "=== UNIRSE A SALA CO-OP (STEAM P2P) ===";

        // Subtítulo
        GameObject subObj = new GameObject("Subtitle");
        subObj.transform.SetParent(panelObj.transform, false);
        RectTransform subRt = subObj.AddComponent<RectTransform>();
        subRt.anchoredPosition = new Vector2(0f, 220f);
        subRt.sizeDelta = new Vector2(800f, 30f);
        headerSubtitleText = subObj.AddComponent<Text>();
        headerSubtitleText.font = gameFont;
        headerSubtitleText.fontSize = 15;
        headerSubtitleText.alignment = TextAnchor.MiddleCenter;
        headerSubtitleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);
        headerSubtitleText.text = "Introduce el Código de Sala proporcionado por el Anfitrión para entrar en su mundo";

        // Fila 0: Código de Sala
        roomCodeValueText = CreateConfigRow(panelObj.transform, 0, 150f, "CÓDIGO DE SALA:", "[ HAZ CLICK O ESCRIBE AQUÍ ]");

        // Fila 1: Nombre de Jugador
        playerNameValueText = CreateConfigRow(panelObj.transform, 1, 80f, "TU NOMBRE:", currentPlayerName);

        // Fila 2: Color de Texto
        playerColorValueText = CreateConfigRow(panelObj.transform, 2, 10f, "COLOR DE TEXTO:", "◄ Verde ►");

        // Fila 3: Aspecto / Skin
        playerSkinValueText = CreateConfigRow(panelObj.transform, 3, -60f, "ASPECTO / SKIN:", "◄ Por Defecto ►");

        // Mini preview de Skin
        GameObject prevObj = new GameObject("SkinPreview");
        prevObj.transform.SetParent(panelObj.transform, false);
        RectTransform prRt = prevObj.AddComponent<RectTransform>();
        prRt.anchoredPosition = new Vector2(250f, -60f);
        prRt.sizeDelta = new Vector2(50f, 50f);
        skinPreviewImage = prevObj.AddComponent<Image>();
        skinPreviewImage.preserveAspect = true;

        // Estado / Feedback
        GameObject stObj = new GameObject("StatusFeedback");
        stObj.transform.SetParent(panelObj.transform, false);
        RectTransform stRt = stObj.AddComponent<RectTransform>();
        stRt.anchoredPosition = new Vector2(0f, -145f);
        stRt.sizeDelta = new Vector2(760f, 40f);
        statusFeedbackText = stObj.AddComponent<Text>();
        statusFeedbackText.font = gameFont;
        statusFeedbackText.fontSize = 16;
        statusFeedbackText.alignment = TextAnchor.MiddleCenter;
        statusFeedbackText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        statusFeedbackText.text = "[ Ingresa el código y presiona CONECTARSE ]";

        // Botón Conectarse
        GameObject connBtnObj = new GameObject("ConnectButton");
        connBtnObj.transform.SetParent(panelObj.transform, false);
        RectTransform cbRt = connBtnObj.AddComponent<RectTransform>();
        cbRt.anchoredPosition = new Vector2(0f, -210f);
        cbRt.sizeDelta = new Vector2(480f, 48f);
        connectButtonBg = connBtnObj.AddComponent<Image>();
        connectButtonBg.color = new Color(0.12f, 0.28f, 0.16f, 0.95f);
        Outline cbOut = connBtnObj.AddComponent<Outline>();
        cbOut.effectColor = new Color(0.35f, 0.85f, 0.45f, 0.8f);

        GameObject cbTxtObj = new GameObject("Text");
        cbTxtObj.transform.SetParent(connBtnObj.transform, false);
        RectTransform cbtRt = cbTxtObj.AddComponent<RectTransform>();
        cbtRt.sizeDelta = new Vector2(460f, 45f);
        connectButtonText = cbTxtObj.AddComponent<Text>();
        connectButtonText.font = gameFont;
        connectButtonText.fontSize = 20;
        connectButtonText.alignment = TextAnchor.MiddleCenter;
        connectButtonText.color = Color.white;
        connectButtonText.text = "CONECTARSE (ENTER)";

        // Barra inferior de atajos
        GameObject lgdObj = new GameObject("Legend");
        lgdObj.transform.SetParent(panelObj.transform, false);
        RectTransform lgdRt = lgdObj.AddComponent<RectTransform>();
        lgdRt.anchoredPosition = new Vector2(0f, -275f);
        lgdRt.sizeDelta = new Vector2(800f, 30f);
        Text lgdText = lgdObj.AddComponent<Text>();
        lgdText.font = gameFont;
        lgdText.fontSize = 14;
        lgdText.alignment = TextAnchor.MiddleCenter;
        lgdText.color = new Color(0.65f, 0.65f, 0.65f, 1f);
        lgdText.text = "[Arriba/Abajo] Seleccionar   |   [Ctrl+V] Pegar Código   |   [Esc] Cancelar";

        rootCanvasObj.SetActive(false);
    }

    private Text CreateConfigRow(Transform parent, int rowIndex, float posY, string label, string defaultValue)
    {
        GameObject rowObj = new GameObject($"Row_{rowIndex}");
        rowObj.transform.SetParent(parent, false);
        RectTransform rRt = rowObj.AddComponent<RectTransform>();
        rRt.anchoredPosition = new Vector2(0f, posY);
        rRt.sizeDelta = new Vector2(760f, 50f);

        // Etiqueta
        GameObject lblObj = new GameObject("Label");
        lblObj.transform.SetParent(rowObj.transform, false);
        RectTransform lRt = lblObj.AddComponent<RectTransform>();
        lRt.anchoredPosition = new Vector2(-180f, 0f);
        lRt.sizeDelta = new Vector2(360f, 40f);
        Text lTxt = lblObj.AddComponent<Text>();
        lTxt.font = gameFont;
        lTxt.fontSize = 18;
        lTxt.alignment = TextAnchor.MiddleRight;
        lTxt.color = new Color(0.80f, 0.80f, 0.80f, 1f);
        lTxt.text = label;

        // Valor / Entrada
        GameObject valObj = new GameObject("Value");
        valObj.transform.SetParent(rowObj.transform, false);
        RectTransform vRt = valObj.AddComponent<RectTransform>();
        vRt.anchoredPosition = new Vector2(160f, 0f);
        vRt.sizeDelta = new Vector2(340f, 40f);
        Text vTxt = valObj.AddComponent<Text>();
        vTxt.font = gameFont;
        vTxt.fontSize = 18;
        vTxt.alignment = TextAnchor.MiddleLeft;
        vTxt.color = new Color(1f, 0.90f, 0.45f, 1f);
        vTxt.text = defaultValue;

        return vTxt;
    }

    private void Open()
    {
        BuildInterface();
        IsOpen = true;
        selectedRowIndex = 0;
        isEditingName = false;
        inputCooldown = 0.25f;

        // Cargar nombre por defecto desde Steam si está disponible
        if (SteamManager.Initialized)
        {
            try
            {
                string steamName = SteamFriends.GetPersonaName();
                if (!string.IsNullOrEmpty(steamName)) currentPlayerName = steamName;
            }
            catch { }
        }

        UpdateDisplay();
        rootCanvasObj.SetActive(true);
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
    }

    private void Close()
    {
        IsOpen = false;
        isEditingName = false;
        if (rootCanvasObj != null)
        {
            rootCanvasObj.SetActive(false);
        }
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        onBackCallback?.Invoke();
    }

    private void Update()
    {
        if (!IsOpen) return;

        float dt = Time.unscaledDeltaTime;

        // Parpadeo del cursor
        cursorBlinkTimer += dt;
        if (cursorBlinkTimer >= 0.45f)
        {
            cursorBlinkTimer = 0f;
            cursorVisible = !cursorVisible;
            UpdateCursorVisuals();
        }

        if (inputCooldown > 0f)
        {
            inputCooldown -= dt;
            return;
        }

        // Atajo global Ctrl+V para pegar código
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
        {
            PasteRoomCodeFromClipboard();
            return;
        }

        // Cancelar con Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isEditingName)
            {
                isEditingName = false;
                UpdateDisplay();
            }
            else
            {
                Close();
            }
            return;
        }

        // Procesar edición de texto en Fila 0 (Room Code) o Fila 1 (Nombre)
        if (selectedRowIndex == 0)
        {
            HandleRoomCodeTyping();
        }
        else if (selectedRowIndex == 1 && isEditingName)
        {
            HandlePlayerNameTyping();
        }

        // Navegación con teclado / mando
        HandleNavigation();
    }

    private void HandleRoomCodeTyping()
    {
        foreach (char c in Input.inputString)
        {
            if (c == '\b') // Backspace
            {
                if (currentRoomCode.Length > 0)
                {
                    currentRoomCode = currentRoomCode.Substring(0, currentRoomCode.Length - 1);
                    Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
                    UpdateDisplay();
                }
            }
            else if (c == '\n' || c == '\r') // Enter
            {
                StartConnectionProcess();
                return;
            }
            else if (char.IsLetterOrDigit(c) && currentRoomCode.Length < 8)
            {
                currentRoomCode += char.ToUpperInvariant(c);
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
                UpdateDisplay();
            }
        }
    }

    private void HandlePlayerNameTyping()
    {
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (currentPlayerName.Length > 0)
                {
                    currentPlayerName = currentPlayerName.Substring(0, currentPlayerName.Length - 1);
                    UpdateDisplay();
                }
            }
            else if (c == '\n' || c == '\r')
            {
                isEditingName = false;
                UpdateDisplay();
                return;
            }
            else if (!char.IsControl(c) && currentPlayerName.Length < 16)
            {
                currentPlayerName += c;
                UpdateDisplay();
            }
        }
    }

    private void HandleNavigation()
    {
        bool up = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
        bool down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
        bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
        bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        if (up && !isEditingName)
        {
            selectedRowIndex = (selectedRowIndex - 1 + 5) % 5;
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateDisplay();
            return;
        }

        if (down && !isEditingName)
        {
            selectedRowIndex = (selectedRowIndex + 1) % 5;
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateDisplay();
            return;
        }

        if (left)
        {
            if (selectedRowIndex == 2) CycleColor(-1);
            else if (selectedRowIndex == 3) CycleSkin(-1);
        }
        else if (right)
        {
            if (selectedRowIndex == 2) CycleColor(1);
            else if (selectedRowIndex == 3) CycleSkin(1);
        }

        if (enter)
        {
            if (selectedRowIndex == 0 || selectedRowIndex == 4)
            {
                StartConnectionProcess();
            }
            else if (selectedRowIndex == 1)
            {
                isEditingName = !isEditingName;
                UpdateDisplay();
            }
        }
    }

    private void CycleColor(int dir)
    {
        currentColorIndex = (currentColorIndex + dir + CoopGameConfig.Palette.Length) % CoopGameConfig.Palette.Length;
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        UpdateDisplay();
    }

    private void CycleSkin(int dir)
    {
        currentSkinIndex = (currentSkinIndex + dir + CoopGameConfig.AvailableSkins.Length) % CoopGameConfig.AvailableSkins.Length;
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        UpdateDisplay();
    }

    private void PasteRoomCodeFromClipboard()
    {
        string clip = GUIUtility.systemCopyBuffer;
        if (!string.IsNullOrEmpty(clip))
        {
            clip = clip.Trim().ToUpperInvariant();
            // Mantener solo letras y números
            StringBuilder clean = new StringBuilder();
            foreach (char c in clip)
            {
                if (char.IsLetterOrDigit(c) && clean.Length < 8) clean.Append(c);
            }
            currentRoomCode = clean.ToString();
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
            statusFeedbackText.color = Color.green;
            statusFeedbackText.text = "✔ ¡Código pegado desde el portapapeles!";
            UpdateDisplay();
        }
    }

    private void StartConnectionProcess()
    {
        if (string.IsNullOrEmpty(currentRoomCode) || currentRoomCode.Length < 4)
        {
            statusFeedbackText.color = new Color(1f, 0.40f, 0.40f, 1f);
            statusFeedbackText.text = "⚠ El código de sala debe contener al menos 4 caracteres.";
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            return;
        }

        statusFeedbackText.color = new Color(0.35f, 0.85f, 1f, 1f);
        statusFeedbackText.text = $"⏳ Conectando con la sala '{currentRoomCode}' vía Steam P2P...";
        inputCooldown = 0.5f;

        // Iniciar búsqueda y conexión mediante CoopLobbyManager
        CoopLobbyManager.Instance.JoinLobbyByCode(currentRoomCode, (success, message) =>
        {
            if (success)
            {
                statusFeedbackText.color = new Color(0.35f, 1f, 0.45f, 1f);
                statusFeedbackText.text = "✔ ¡Sala encontrada! Conectando y sincronizando con el anfitrión...";
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");

                // Configurar datos cosméticos de este cliente
                CoopGameConfig.Slots[1].Name = currentPlayerName;
                CoopGameConfig.Slots[1].ColorIndex = currentColorIndex;
                CoopGameConfig.Slots[1].SkinId = CoopGameConfig.AvailableSkins[currentSkinIndex].Id;

                // Iniciar handshake P2P con el anfitrión
                CoopNetworkManager.Instance.SendHandshakeRequest();

                onJoinedSuccessCallback?.Invoke();
            }
            else
            {
                statusFeedbackText.color = new Color(1f, 0.40f, 0.40f, 1f);
                statusFeedbackText.text = $"❌ {message}";
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            }
        });
    }

    private void UpdateDisplay()
    {
        // 0. Código de Sala
        bool isSel0 = (selectedRowIndex == 0);
        string codeCursor = (isSel0 && cursorVisible) ? "|" : "";
        string displayCode = string.IsNullOrEmpty(currentRoomCode)
            ? (isSel0 ? $"[ Escribe el código...{codeCursor} ]" : "[ Click o selecciona para escribir ]")
            : $"[  {currentRoomCode}{codeCursor}  ]";
        roomCodeValueText.text = displayCode;
        roomCodeValueText.color = isSel0 ? new Color(1f, 0.95f, 0.35f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);

        // 1. Nombre
        bool isSel1 = (selectedRowIndex == 1);
        string nameCursor = (isEditingName && cursorVisible) ? "|" : "";
        playerNameValueText.text = isEditingName ? $"[ {currentPlayerName}{nameCursor} ] (Presiona Enter)" : $"[ {currentPlayerName} ]";
        playerNameValueText.color = isSel1 ? new Color(1f, 0.92f, 0.45f, 1f) : Color.white;

        // 2. Color
        bool isSel2 = (selectedRowIndex == 2);
        var pal = CoopGameConfig.Palette[currentColorIndex];
        playerColorValueText.text = isSel2 ? $"◄  {pal.Name}  ►" : pal.Name;
        playerColorValueText.color = pal.Color;

        // 3. Skin
        bool isSel3 = (selectedRowIndex == 3);
        var skin = CoopGameConfig.AvailableSkins[currentSkinIndex];
        playerSkinValueText.text = isSel3 ? $"◄  {skin.Name}  ►" : skin.Name;
        playerSkinValueText.color = isSel3 ? new Color(1f, 0.90f, 0.40f, 1f) : Color.white;

        // 4. Botón Conectar
        bool isSel4 = (selectedRowIndex == 4);
        connectButtonBg.color = isSel4 ? new Color(0.20f, 0.55f, 0.28f, 1f) : new Color(0.12f, 0.28f, 0.16f, 0.95f);
        connectButtonText.color = isSel4 ? Color.yellow : Color.white;
    }

    private void UpdateCursorVisuals()
    {
        if (selectedRowIndex == 0 || isEditingName)
        {
            UpdateDisplay();
        }
    }
}
