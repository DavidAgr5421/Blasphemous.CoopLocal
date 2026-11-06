using System;
using Framework.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Interfaz gráfica horizontal por columnas/tarjetas para configurar la partida antes de comenzar:
/// - Navegación horizontal (Izquierda / Derecha) entre jugadores y sus configuraciones.
/// - Navegación vertical (Arriba / Abajo) entre filas de ajustes.
/// - Cambio de estado de la configuración seleccionada mediante el BOTÓN DE ATAQUE (X en mando, K en teclado).
/// - Detección automática de mando/teclado al pulsar cualquier botón sobre la ranura.
/// </summary>
internal sealed class CoopGameSetupUI : MonoBehaviour
{
    private static CoopGameSetupUI _instance;
    public static CoopGameSetupUI Instance
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

    private GameObject canvasObj;
    private Canvas canvas;
    private Text titleText;
    private Text subtitleText;

    // Barra superior: Coop Toggle y Cantidad de Jugadores
    private GameObject coopToggleBox;
    private Outline coopToggleOutline;
    private Text coopToggleLabel;
    private Text coopToggleValue;

    private GameObject playerCountBox;
    private Outline playerCountOutline;
    private Text playerCountLabel;
    private Text playerCountValue;

    // Tarjetas de jugadores (hasta 4 columnas)
    private struct PlayerCardUI
    {
        public GameObject CardObj;
        public Image CardBg;
        public Outline CardOutline;
        public Text HeaderText;

        // 4 filas por tarjeta: 0 = Control, 1 = Nombre, 2 = Color, 3 = Skin
        public GameObject[] RowObjs;
        public Text[] RowLabels;
        public Text[] RowValues;
        public Image SkinPreview;
    }

    private readonly PlayerCardUI[] playerCards = new PlayerCardUI[4];

    // Edición de nombre con teclado
    private bool isEditingName = false;
    private int editingPlayerIndex = 0;
    private string editingBuffer = "";

    // Botones de acción inferiores
    private GameObject startButtonBox;
    private Outline startButtonOutline;
    private Text startButtonText;

    private GameObject cancelButtonBox;
    private Outline cancelButtonOutline;
    private Text cancelButtonText;

    private Text footerTipText;

    private Action onConfirmedCallback;
    private Action onCancelledCallback;

    public bool IsOpen { get; private set; }

    // Estados de navegación
    private enum MenuZone
    {
        TopGlobal = 0,
        PlayerCards = 1,
        BottomActions = 2
    }

    private MenuZone currentZone = MenuZone.PlayerCards;
    private int topBarIndex = 0;          // 0 = CoopToggle, 1 = PlayerCount
    private int selectedPlayerIndex = 0;  // 0 .. PlayerCount - 1
    private int selectedConfigRow = 0;    // 0 = Control, 1 = Nombre, 2 = Color, 3 = Skin
    private int bottomActionIndex = 0;    // 0 = StartGame, 1 = Cancel

    private float inputCooldown = 0f;

    public static void Show(Action onConfirm, Action onCancel)
    {
        Instance.Open(onConfirm, onCancel);
    }

    public static void Hide()
    {
        if (_instance != null)
        {
            _instance.Close();
        }
    }

    private static void CreateUI()
    {
        GameObject go = new GameObject("CoopGameSetupUI_Root");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopGameSetupUI>();
        _instance.BuildInterface();
    }

    private void BuildInterface()
    {
        canvasObj = new GameObject("CoopGameSetupCanvas");
        canvasObj.transform.SetParent(transform, false);

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32500;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        Font gameFont = GetGameFont();

        // 1. Fondo oscurecido con viñeta
        GameObject bgObj = CreateUiElement("DarkBackdrop", canvasObj.transform);
        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.02f, 0.03f, 0.96f);

        // 2. Contenedor del panel central
        GameObject panelObj = CreateUiElement("CenterPanel", canvasObj.transform);
        RectTransform panelRt = panelObj.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(1360f, 940f);
        panelRt.anchoredPosition = Vector2.zero;
        Image panelImg = panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
        Outline panelOutline = panelObj.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.85f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        // 3. Título principal
        GameObject titleObj = CreateUiElement("TitleText", panelObj.transform);
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0f, 425f);
        titleRt.sizeDelta = new Vector2(1300f, 45f);
        titleText = titleObj.AddComponent<Text>();
        titleText.font = gameFont;
        titleText.fontSize = 30;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(1f, 0.88f, 0.40f, 1f);
        titleText.text = "CONFIGURACIÓN DE PARTIDA COOPERATIVA";

        // 4. Subtítulo con instrucciones claras de controles
        GameObject subObj = CreateUiElement("SubtitleText", panelObj.transform);
        RectTransform subRt = subObj.GetComponent<RectTransform>();
        subRt.anchoredPosition = new Vector2(0f, 382f);
        subRt.sizeDelta = new Vector2(1300f, 32f);
        subtitleText = subObj.AddComponent<Text>();
        subtitleText.font = gameFont;
        subtitleText.fontSize = 17;
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        subtitleText.text = "[◄ / ►] Mover entre Jugadores y Ajustes  |  [▲ / ▼] Seleccionar Fila  |  [BOTÓN ATAQUE (X/K)]: Cambiar Valor";

        // 5. Barra superior (Modo Coop y Cantidad de Jugadores)
        GameObject topBarContainer = CreateUiElement("TopBarContainer", panelObj.transform);
        RectTransform tbRt = topBarContainer.GetComponent<RectTransform>();
        tbRt.anchoredPosition = new Vector2(0f, 325f);
        tbRt.sizeDelta = new Vector2(1280f, 55f);

        // 5.1 Botón Modo Cooperativo
        coopToggleBox = CreateUiElement("CoopToggleBox", topBarContainer.transform);
        RectTransform ctbRt = coopToggleBox.GetComponent<RectTransform>();
        ctbRt.anchoredPosition = new Vector2(-320f, 0f);
        ctbRt.sizeDelta = new Vector2(610f, 50f);
        Image ctbImg = coopToggleBox.AddComponent<Image>();
        ctbImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        coopToggleOutline = coopToggleBox.AddComponent<Outline>();
        coopToggleOutline.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.70f);
        coopToggleOutline.effectDistance = new Vector2(2f, -2f);

        GameObject ctbLblObj = CreateUiElement("Label", coopToggleBox.transform);
        RectTransform ctbLblRt = ctbLblObj.GetComponent<RectTransform>();
        ctbLblRt.anchoredPosition = new Vector2(-120f, 0f);
        ctbLblRt.sizeDelta = new Vector2(320f, 40f);
        coopToggleLabel = ctbLblObj.AddComponent<Text>();
        coopToggleLabel.font = gameFont;
        coopToggleLabel.fontSize = 18;
        coopToggleLabel.alignment = TextAnchor.MiddleLeft;
        coopToggleLabel.color = new Color(0.90f, 0.90f, 0.90f, 1f);
        coopToggleLabel.text = "Modo de Juego:";

        GameObject ctbValObj = CreateUiElement("Value", coopToggleBox.transform);
        RectTransform ctbValRt = ctbValObj.GetComponent<RectTransform>();
        ctbValRt.anchoredPosition = new Vector2(130f, 0f);
        ctbValRt.sizeDelta = new Vector2(300f, 40f);
        coopToggleValue = ctbValObj.AddComponent<Text>();
        coopToggleValue.font = gameFont;
        coopToggleValue.fontSize = 19;
        coopToggleValue.alignment = TextAnchor.MiddleRight;

        // 5.2 Botón Cantidad de Jugadores
        playerCountBox = CreateUiElement("PlayerCountBox", topBarContainer.transform);
        RectTransform pcbRt = playerCountBox.GetComponent<RectTransform>();
        pcbRt.anchoredPosition = new Vector2(320f, 0f);
        pcbRt.sizeDelta = new Vector2(610f, 50f);
        Image pcbImg = playerCountBox.AddComponent<Image>();
        pcbImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        playerCountOutline = playerCountBox.AddComponent<Outline>();
        playerCountOutline.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.70f);
        playerCountOutline.effectDistance = new Vector2(2f, -2f);

        GameObject pcbLblObj = CreateUiElement("Label", playerCountBox.transform);
        RectTransform pcbLblRt = pcbLblObj.GetComponent<RectTransform>();
        pcbLblRt.anchoredPosition = new Vector2(-120f, 0f);
        pcbLblRt.sizeDelta = new Vector2(320f, 40f);
        playerCountLabel = pcbLblObj.AddComponent<Text>();
        playerCountLabel.font = gameFont;
        playerCountLabel.fontSize = 18;
        playerCountLabel.alignment = TextAnchor.MiddleLeft;
        playerCountLabel.color = new Color(0.90f, 0.90f, 0.90f, 1f);
        playerCountLabel.text = "Cantidad Jugadores:";

        GameObject pcbValObj = CreateUiElement("Value", playerCountBox.transform);
        RectTransform pcbValRt = pcbValObj.GetComponent<RectTransform>();
        pcbValRt.anchoredPosition = new Vector2(130f, 0f);
        pcbValRt.sizeDelta = new Vector2(300f, 40f);
        playerCountValue = pcbValObj.AddComponent<Text>();
        playerCountValue.font = gameFont;
        playerCountValue.fontSize = 19;
        playerCountValue.alignment = TextAnchor.MiddleRight;

        // 6. Contenedor de Tarjetas de Jugadores (Columnas)
        GameObject cardsContainer = CreateUiElement("PlayerCardsContainer", panelObj.transform);
        RectTransform ccRt = cardsContainer.GetComponent<RectTransform>();
        ccRt.anchoredPosition = new Vector2(0f, 20f);
        ccRt.sizeDelta = new Vector2(1280f, 500f);

        for (int p = 0; p < 4; p++)
        {
            GameObject cardObj = CreateUiElement($"PlayerCard_{p}", cardsContainer.transform);
            Image cImg = cardObj.AddComponent<Image>();
            cImg.color = new Color(0.10f, 0.10f, 0.13f, 0.94f);
            Outline cOut = cardObj.AddComponent<Outline>();
            cOut.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.60f);
            cOut.effectDistance = new Vector2(2f, -2f);

            // Cabecera de la tarjeta
            GameObject hdrObj = CreateUiElement("Header", cardObj.transform);
            RectTransform hRt = hdrObj.GetComponent<RectTransform>();
            hRt.anchoredPosition = new Vector2(0f, 215f);
            hRt.sizeDelta = new Vector2(280f, 42f);
            Text hTxt = hdrObj.AddComponent<Text>();
            hTxt.font = gameFont;
            hTxt.fontSize = 21;
            hTxt.alignment = TextAnchor.MiddleCenter;

            // 4 Filas de ajustes
            GameObject[] rowObjs = new GameObject[4];
            Text[] rowLbls = new Text[4];
            Text[] rowVals = new Text[4];

            string[] defaultRowLabels = new string[] { "CONTROL / DISPOSITIVO", "NOMBRE", "COLOR DE TEXTO", "ASPECTO / SKIN" };
            float[] rowY = new float[] { 135f, 50f, -35f, -120f };

            for (int r = 0; r < 4; r++)
            {
                GameObject rowObj = CreateUiElement($"Row_{r}", cardObj.transform);
                RectTransform rRt = rowObj.GetComponent<RectTransform>();
                rRt.anchoredPosition = new Vector2(0f, rowY[r]);
                rRt.sizeDelta = new Vector2(270f, 65f);

                // Etiqueta del ajuste
                GameObject lblObj = CreateUiElement("Label", rowObj.transform);
                RectTransform lRt = lblObj.GetComponent<RectTransform>();
                lRt.anchoredPosition = new Vector2(0f, 16f);
                lRt.sizeDelta = new Vector2(260f, 26f);
                Text lTxt = lblObj.AddComponent<Text>();
                lTxt.font = gameFont;
                lTxt.fontSize = 15;
                lTxt.alignment = TextAnchor.MiddleCenter;
                lTxt.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                lTxt.text = defaultRowLabels[r];

                // Valor del ajuste
                GameObject valObj = CreateUiElement("Value", rowObj.transform);
                RectTransform vRt = valObj.GetComponent<RectTransform>();
                vRt.anchoredPosition = (r == 3) ? new Vector2(-16f, -14f) : new Vector2(0f, -14f);
                vRt.sizeDelta = (r == 3) ? new Vector2(200f, 32f) : new Vector2(260f, 32f);
                Text vTxt = valObj.AddComponent<Text>();
                vTxt.font = gameFont;
                vTxt.fontSize = 18;
                vTxt.alignment = (r == 3) ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;

                Image skinPrev = null;
                if (r == 3)
                {
                    GameObject spObj = CreateUiElement("SkinPreview", rowObj.transform);
                    RectTransform spRt = spObj.GetComponent<RectTransform>();
                    spRt.anchoredPosition = new Vector2(100f, -14f);
                    spRt.sizeDelta = new Vector2(28f, 28f);
                    skinPrev = spObj.AddComponent<Image>();
                    skinPrev.preserveAspect = true;
                }

                rowObjs[r] = rowObj;
                rowLbls[r] = lTxt;
                rowVals[r] = vTxt;
            }

            playerCards[p] = new PlayerCardUI
            {
                CardObj = cardObj,
                CardBg = cImg,
                CardOutline = cOut,
                HeaderText = hTxt,
                RowObjs = rowObjs,
                RowLabels = rowLbls,
                RowValues = rowVals,
                SkinPreview = rowObjs[3].transform.Find("SkinPreview")?.GetComponent<Image>()
            };
        }

        // 7. Botones de acción inferiores
        GameObject bottomBarContainer = CreateUiElement("BottomBarContainer", panelObj.transform);
        RectTransform bbcRt = bottomBarContainer.GetComponent<RectTransform>();
        bbcRt.anchoredPosition = new Vector2(0f, -315f);
        bbcRt.sizeDelta = new Vector2(1280f, 55f);

        // 7.1 Botón Comenzar Partida
        startButtonBox = CreateUiElement("StartButtonBox", bottomBarContainer.transform);
        RectTransform sbbRt = startButtonBox.GetComponent<RectTransform>();
        sbbRt.anchoredPosition = new Vector2(-280f, 0f);
        sbbRt.sizeDelta = new Vector2(520f, 52f);
        Image sbbImg = startButtonBox.AddComponent<Image>();
        sbbImg.color = new Color(0.10f, 0.16f, 0.12f, 0.95f);
        startButtonOutline = startButtonBox.AddComponent<Outline>();
        startButtonOutline.effectColor = new Color(0.20f, 0.60f, 0.30f, 0.70f);
        startButtonOutline.effectDistance = new Vector2(2f, -2f);

        GameObject sbbTxtObj = CreateUiElement("Text", startButtonBox.transform);
        RectTransform sbbTxtRt = sbbTxtObj.GetComponent<RectTransform>();
        sbbTxtRt.sizeDelta = new Vector2(500f, 44f);
        sbbTxtRt.anchoredPosition = Vector2.zero;
        startButtonText = sbbTxtObj.AddComponent<Text>();
        startButtonText.font = gameFont;
        startButtonText.fontSize = 20;
        startButtonText.alignment = TextAnchor.MiddleCenter;
        startButtonText.color = new Color(0.40f, 1f, 0.50f, 1f);
        startButtonText.text = "¡COMENZAR PARTIDA!";

        // 7.2 Botón Cancelar / Volver
        cancelButtonBox = CreateUiElement("CancelButtonBox", bottomBarContainer.transform);
        RectTransform cbbRt = cancelButtonBox.GetComponent<RectTransform>();
        cbbRt.anchoredPosition = new Vector2(280f, 0f);
        cbbRt.sizeDelta = new Vector2(520f, 52f);
        Image cbbImg = cancelButtonBox.AddComponent<Image>();
        cbbImg.color = new Color(0.16f, 0.10f, 0.10f, 0.95f);
        cancelButtonOutline = cancelButtonBox.AddComponent<Outline>();
        cancelButtonOutline.effectColor = new Color(0.60f, 0.20f, 0.20f, 0.70f);
        cancelButtonOutline.effectDistance = new Vector2(2f, -2f);

        GameObject cbbTxtObj = CreateUiElement("Text", cancelButtonBox.transform);
        RectTransform cbbTxtRt = cbbTxtObj.GetComponent<RectTransform>();
        cbbTxtRt.sizeDelta = new Vector2(500f, 44f);
        cbbTxtRt.anchoredPosition = Vector2.zero;
        cancelButtonText = cbbTxtObj.AddComponent<Text>();
        cancelButtonText.font = gameFont;
        cancelButtonText.fontSize = 20;
        cancelButtonText.alignment = TextAnchor.MiddleCenter;
        cancelButtonText.color = new Color(1f, 0.45f, 0.45f, 1f);
        cancelButtonText.text = "VOLVER / CANCELAR";

        // 8. Consejo inferior
        GameObject tipObj = CreateUiElement("FooterTip", panelObj.transform);
        RectTransform tipRt = tipObj.GetComponent<RectTransform>();
        tipRt.anchoredPosition = new Vector2(0f, -385f);
        tipRt.sizeDelta = new Vector2(1280f, 30f);
        footerTipText = tipObj.AddComponent<Text>();
        footerTipText.font = gameFont;
        footerTipText.fontSize = 15;
        footerTipText.alignment = TextAnchor.MiddleCenter;
        footerTipText.color = new Color(0.85f, 0.75f, 0.40f, 0.95f);
        footerTipText.text = "Para asignar mando o teclado: sitúate en 'Control' y pulsa cualquier botón en tu dispositivo";

        canvasObj.SetActive(false);
    }

    private void Open(Action onConfirm, Action onCancel)
    {
        onConfirmedCallback = onConfirm;
        onCancelledCallback = onCancel;
        IsOpen = true;
        currentZone = MenuZone.PlayerCards;
        selectedPlayerIndex = 0;
        selectedConfigRow = 0;
        topBarIndex = 0;
        bottomActionIndex = 0;
        inputCooldown = 0.25f;

        canvasObj.SetActive(true);
        RefreshDisplay();
    }

    private void Close()
    {
        IsOpen = false;
        if (canvasObj != null)
        {
            canvasObj.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsOpen) return;

        float dt = Time.unscaledDeltaTime;
        if (inputCooldown > 0f)
        {
            inputCooldown -= dt;
        }

        // Modo interactivo de edición de nombre con teclado
        if (isEditingName)
        {
            HandleNameEditingInput();
            return;
        }

        // 1. Detección automática de mando o teclado si se presiona cualquier botón
        DetectControllerOrKeyboardPress();

        // 2. Navegación direccional (Izquierda / Derecha / Arriba / Abajo)
        if (inputCooldown <= 0f)
        {
            HandleNavigationInput();
        }

        // 3. Modificación de la configuración mediante el BOTÓN DE ATAQUE (o confirmación)
        if (inputCooldown <= 0f && IsAttackButtonPressed())
        {
            HandleAttackButtonPress();
            inputCooldown = 0.22f;
        }

        // 4. Cancelar / Volver (Escape, Botón B / Back)
        if (Input.GetKeyDown(KeyCode.Escape) || Player2Pad.CancelDown)
        {
            CancelAndReturn();
        }
    }

    private void HandleNameEditingInput()
    {
        // Cancelar con Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            isEditingName = false;
            PlayNavSound();
            RefreshDisplay();
            return;
        }

        // Confirmar con Enter, KeypadEnter o botón de confirmación / ataque
        bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                       Player2Pad.SelectDown;

        string typed = Input.inputString;
        if (!string.IsNullOrEmpty(typed))
        {
            foreach (char c in typed)
            {
                if (c == '\b') // Backspace
                {
                    if (editingBuffer.Length > 0)
                    {
                        editingBuffer = editingBuffer.Substring(0, editingBuffer.Length - 1);
                        PlayNavSound();
                    }
                }
                else if (c == '\n' || c == '\r') // Enter
                {
                    confirm = true;
                }
                else if (c >= 32 && c <= 126 && editingBuffer.Length < 14) // Carácter imprimible
                {
                    editingBuffer += c;
                }
            }
        }

        if (confirm)
        {
            string finalName = editingBuffer.Trim();
            if (string.IsNullOrEmpty(finalName))
            {
                finalName = $"Jugador {editingPlayerIndex + 1}";
            }
            CoopGameConfig.Slots[editingPlayerIndex].Name = finalName;
            CoopGameConfig.ApplyToRegistry();
            CoopGameConfig.Save();
            isEditingName = false;
            PlayToggleSound();
            inputCooldown = 0.20f;
        }

        RefreshDisplay();
    }

    private void HandleNavigationInput()
    {
        bool left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || Player2Pad.Left;
        bool right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Player2Pad.Right;
        bool up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || Player2Pad.Up;
        bool down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || Player2Pad.Down;

        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;

        if (left)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    topBarIndex = (topBarIndex == 0) ? 1 : 0;
                    break;
                case MenuZone.PlayerCards:
                    selectedPlayerIndex--;
                    if (selectedPlayerIndex < 0) selectedPlayerIndex = activePlayers - 1;
                    break;
                case MenuZone.BottomActions:
                    bottomActionIndex = (bottomActionIndex == 0) ? 1 : 0;
                    break;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            RefreshDisplay();
        }
        else if (right)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    topBarIndex = (topBarIndex == 0) ? 1 : 0;
                    break;
                case MenuZone.PlayerCards:
                    selectedPlayerIndex++;
                    if (selectedPlayerIndex >= activePlayers) selectedPlayerIndex = 0;
                    break;
                case MenuZone.BottomActions:
                    bottomActionIndex = (bottomActionIndex == 0) ? 1 : 0;
                    break;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            RefreshDisplay();
        }
        else if (up)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    // Ya en el tope superior
                    break;
                case MenuZone.PlayerCards:
                    if (selectedConfigRow > 0)
                    {
                        selectedConfigRow--;
                    }
                    else
                    {
                        currentZone = MenuZone.TopGlobal;
                        topBarIndex = (selectedPlayerIndex >= activePlayers / 2) ? 1 : 0;
                    }
                    break;
                case MenuZone.BottomActions:
                    currentZone = MenuZone.PlayerCards;
                    selectedConfigRow = 3; // Fila de Skin
                    break;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            RefreshDisplay();
        }
        else if (down)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    currentZone = MenuZone.PlayerCards;
                    selectedConfigRow = 0;
                    break;
                case MenuZone.PlayerCards:
                    if (selectedConfigRow < 3)
                    {
                        selectedConfigRow++;
                    }
                    else
                    {
                        currentZone = MenuZone.BottomActions;
                        bottomActionIndex = (selectedPlayerIndex >= activePlayers / 2) ? 1 : 0;
                    }
                    break;
                case MenuZone.BottomActions:
                    // Ya en la base inferior
                    break;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            RefreshDisplay();
        }
    }

    /// <summary>
    /// Comprueba si se ha pulsado el botón de atacar (X en mando, K o J en teclado, etc.)
    /// </summary>
    private bool IsAttackButtonPressed()
    {
        // 1. Teclas clásicas de ataque en teclado (K del mod, J de vanilla, X, Z)
        if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.J) ||
            Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Z))
        {
            return true;
        }

        // 2. Mando de P2 / General: Botón de ataque ("X Button" en Xbox / Cuadrado en PlayStation)
        if (Player2Pad.AttackDown)
        {
            return true;
        }

        // 3. Ataque de P1 en Rewired (si está listo)
        try
        {
            if (Rewired.ReInput.isReady)
            {
                var p0 = Rewired.ReInput.players.GetPlayer(0);
                if (p0 != null && p0.GetButtonDown("Attack"))
                {
                    return true;
                }
            }
        }
        catch { }

        // 4. Detección directa en mandos de Unity (Botón 2 = X en Xbox)
        for (int j = 1; j <= 4; j++)
        {
            try
            {
                if (Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button2"))) return true;
                if (Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button0"))) return true;
            }
            catch { }
        }

        // 5. Botones de confirmación y selección (Enter, Espacio, Botón A)
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Space) || Player2Pad.JumpHeld || Player2Pad.SelectDown)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Aplica el cambio de valor / estado a la opción seleccionada al presionar el botón de ataque.
    /// </summary>
    private void HandleAttackButtonPress()
    {
        switch (currentZone)
        {
            case MenuZone.TopGlobal:
                if (topBarIndex == 0)
                {
                    // Alternar Cooperativo (Sí / No)
                    CoopGameConfig.IsCoopEnabled = !CoopGameConfig.IsCoopEnabled;
                    CoopGameConfig.Save();
                    int maxP = CoopGameConfig.IsCoopEnabled ? CoopGameConfig.PlayerCount : 1;
                    if (selectedPlayerIndex >= maxP) selectedPlayerIndex = maxP - 1;
                    PlayToggleSound();
                }
                else
                {
                    // Cambiar cantidad de jugadores (2 -> 3 -> 4 -> 2)
                    int count = CoopGameConfig.PlayerCount + 1;
                    if (count > 4) count = 2;
                    CoopGameConfig.PlayerCount = count;
                    CoopGameConfig.Save();
                    PlayToggleSound();
                }
                break;

            case MenuZone.PlayerCards:
                switch (selectedConfigRow)
                {
                    case 0: // Control / Dispositivo
                        CycleDevice(selectedPlayerIndex, 1);
                        PlayToggleSound();
                        break;
                    case 1: // Nombre (Escribir con teclado)
                        isEditingName = true;
                        editingPlayerIndex = selectedPlayerIndex;
                        editingBuffer = CoopGameConfig.Slots[selectedPlayerIndex].Name ?? "";
                        PlayToggleSound();
                        inputCooldown = 0.20f;
                        break;
                    case 2: // Color de Texto
                        CycleColor(selectedPlayerIndex, 1);
                        PlayToggleSound();
                        break;
                    case 3: // Aspecto / Skin
                        CycleSkin(selectedPlayerIndex, 1);
                        PlayToggleSound();
                        break;
                }
                break;

            case MenuZone.BottomActions:
                if (bottomActionIndex == 0)
                {
                    ConfirmAndStart();
                }
                else
                {
                    CancelAndReturn();
                }
                break;
        }

        RefreshDisplay();
    }

    private void DetectControllerOrKeyboardPress()
    {
        int targetPlayer = (currentZone == MenuZone.PlayerCards) ? selectedPlayerIndex : -1;

        // Mandos (Joystick 1..4)
        for (int j = 1; j <= 4; j++)
        {
            bool joyPressed = false;
            for (int b = 0; b < 16; b++)
            {
                try
                {
                    KeyCode kc = (KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button{b}");
                    if (Input.GetKeyDown(kc))
                    {
                        joyPressed = true;
                        break;
                    }
                }
                catch { }
            }

            if (joyPressed)
            {
                CoopDeviceType dev = (CoopDeviceType)(j + 1); // Gamepad1..4
                int assignTo = targetPlayer >= 0 ? targetPlayer : FindFirstUnassignedSlot(dev);
                if (assignTo >= 0 && currentZone == MenuZone.PlayerCards && selectedConfigRow == 0)
                {
                    CoopGameConfig.AssignDevice(assignTo, dev);
                    Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
                    RefreshDisplay();
                    return;
                }
            }
        }

        // Teclado
        if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.Escape) && !Input.GetKeyDown(KeyCode.Return) &&
            !Input.GetKeyDown(KeyCode.UpArrow) && !Input.GetKeyDown(KeyCode.DownArrow) &&
            !Input.GetKeyDown(KeyCode.LeftArrow) && !Input.GetKeyDown(KeyCode.RightArrow) &&
            !Input.GetKeyDown(KeyCode.W) && !Input.GetKeyDown(KeyCode.S) &&
            !Input.GetKeyDown(KeyCode.A) && !Input.GetKeyDown(KeyCode.D) &&
            !Input.GetKeyDown(KeyCode.K) && !Input.GetKeyDown(KeyCode.J) &&
            !IsAnyJoystickButtonHeld())
        {
            if (currentZone == MenuZone.PlayerCards && selectedConfigRow == 0)
            {
                CoopGameConfig.AssignDevice(selectedPlayerIndex, CoopDeviceType.Keyboard);
                Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
                RefreshDisplay();
            }
        }
    }

    private static bool IsAnyJoystickButtonHeld()
    {
        for (int j = 1; j <= 4; j++)
        {
            for (int b = 0; b < 16; b++)
            {
                try
                {
                    KeyCode kc = (KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button{b}");
                    if (Input.GetKey(kc)) return true;
                }
                catch { }
            }
        }
        return false;
    }

    private int FindFirstUnassignedSlot(CoopDeviceType dev)
    {
        int max = CoopGameConfig.IsCoopEnabled ? CoopGameConfig.PlayerCount : 1;
        for (int i = 0; i < max; i++)
        {
            if (CoopGameConfig.Slots[i].Device == CoopDeviceType.Unassigned)
            {
                return i;
            }
        }
        return -1;
    }

    private void CycleDevice(int playerIndex, int dir)
    {
        CoopDeviceType current = CoopGameConfig.Slots[playerIndex].Device;
        int next = ((int)current + dir);
        if (next < 0) next = 5;
        if (next > 5) next = 0;
        CoopGameConfig.AssignDevice(playerIndex, (CoopDeviceType)next);
    }

    private void CycleSuggestedName(int playerIndex, int dir)
    {
        string[] suggestions = CoopGameConfig.SuggestedNames[playerIndex % CoopGameConfig.SuggestedNames.Length];
        string curName = CoopGameConfig.Slots[playerIndex].Name;
        int idx = Array.IndexOf(suggestions, curName);
        if (idx < 0) idx = 0;
        int nextIdx = (idx + dir + suggestions.Length) % suggestions.Length;
        CoopGameConfig.Slots[playerIndex].Name = suggestions[nextIdx];
        CoopGameConfig.ApplyToRegistry();
        CoopGameConfig.Save();
    }

    private void CycleColor(int playerIndex, int dir)
    {
        int cur = CoopGameConfig.Slots[playerIndex].ColorIndex;
        int len = CoopGameConfig.Palette.Length;
        int next = (cur + dir + len) % len;
        CoopGameConfig.Slots[playerIndex].ColorIndex = next;
        CoopGameConfig.ApplyToRegistry();
        CoopGameConfig.Save();
    }

    private void CycleSkin(int playerIndex, int dir)
    {
        string curSkin = CoopGameConfig.Slots[playerIndex].SkinId;
        int idx = CoopGameConfig.GetSkinIndex(curSkin);
        int total = CoopGameConfig.AvailableSkins.Length;
        int nextIdx = (idx + dir + total) % total;
        CoopGameConfig.Slots[playerIndex].SkinId = CoopGameConfig.AvailableSkins[nextIdx].Id;

        // Si es P1, actualizar la skin activa en ColorPaletteManager
        if (playerIndex == 0 && Core.ColorPaletteManager != null)
        {
            Core.ColorPaletteManager.SetCurrentColorPaletteId(CoopGameConfig.Slots[playerIndex].SkinId);
        }

        CoopGameConfig.ApplyToRegistry();
        CoopGameConfig.Save();
    }

    private void RefreshDisplay()
    {
        bool coopOn = CoopGameConfig.IsCoopEnabled;
        int activePlayers = coopOn ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;

        if (subtitleText != null)
        {
            subtitleText.text = isEditingName
                ? "[ESC] Cancelar  |  [ENTER] Guardar Nombre  |  Escribe el nombre con el teclado..."
                : "[◄ / ►] Mover entre Jugadores y Ajustes  |  [▲ / ▼] Seleccionar Fila  |  [BOTÓN ATAQUE (X/K)]: Cambiar / Escribir";
        }

        // 1. Barra superior
        bool isTopZone = (currentZone == MenuZone.TopGlobal);
        bool isTop0 = isTopZone && (topBarIndex == 0);
        bool isTop1 = isTopZone && (topBarIndex == 1);

        coopToggleOutline.effectColor = isTop0 ? new Color(1f, 0.88f, 0.35f, 1f) : new Color(0.40f, 0.35f, 0.25f, 0.60f);
        coopToggleOutline.effectDistance = isTop0 ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        coopToggleLabel.color = isTop0 ? new Color(1f, 0.95f, 0.50f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
        coopToggleValue.color = coopOn ? new Color(0.40f, 1f, 0.50f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
        coopToggleValue.text = (isTop0 ? "► " : "") + (coopOn ? "[ COOPERATIVO (SÍ) ]" : "[ UN JUGADOR (NO) ]");

        playerCountOutline.effectColor = isTop1 ? new Color(1f, 0.88f, 0.35f, 1f) : new Color(0.40f, 0.35f, 0.25f, 0.60f);
        playerCountOutline.effectDistance = isTop1 ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        playerCountLabel.color = isTop1 ? new Color(1f, 0.95f, 0.50f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
        playerCountValue.color = coopOn ? Color.white : new Color(0.50f, 0.50f, 0.50f, 1f);
        playerCountValue.text = (isTop1 ? "► " : "") + (coopOn ? $"[ {activePlayers} JUGADORES ]" : "[ 1 JUGADOR ]");

        // 2. Posicionamiento dinámico de tarjetas de jugadores según cantidad
        float totalWidth = 1260f;
        float cardSpacing = 16f;
        float cardWidth = (totalWidth - (cardSpacing * (activePlayers - 1))) / activePlayers;
        float startX = -((totalWidth - cardWidth) * 0.5f);

        for (int p = 0; p < 4; p++)
        {
            PlayerCardUI card = playerCards[p];
            if (p < activePlayers)
            {
                card.CardObj.SetActive(true);
                RectTransform cRt = card.CardObj.GetComponent<RectTransform>();
                cRt.anchoredPosition = new Vector2(startX + (p * (cardWidth + cardSpacing)), 0f);
                cRt.sizeDelta = new Vector2(cardWidth, 470f);

                bool isSelectedCard = (currentZone == MenuZone.PlayerCards && selectedPlayerIndex == p);

                card.CardBg.color = isSelectedCard
                    ? new Color(0.14f, 0.13f, 0.11f, 0.98f)
                    : new Color(0.09f, 0.09f, 0.12f, 0.90f);

                card.CardOutline.effectColor = isSelectedCard
                    ? new Color(1f, 0.85f, 0.30f, 1f)
                    : new Color(0.35f, 0.32f, 0.25f, 0.60f);
                card.CardOutline.effectDistance = isSelectedCard ? new Vector2(3f, -3f) : new Vector2(2f, -2f);

                int colorIdx = Mathf.Clamp(CoopGameConfig.Slots[p].ColorIndex, 0, CoopGameConfig.Palette.Length - 1);
                Color32 playerColor = CoopGameConfig.Palette[colorIdx].Color;

                card.HeaderText.color = playerColor;
                string roleTag = (p == 0) ? " (Host)" : "";
                card.HeaderText.text = $"[ JUGADOR {p + 1}{roleTag} ]";

                // Renderizar las 4 filas de la tarjeta
                for (int r = 0; r < 4; r++)
                {
                    bool isSelectedRow = (isSelectedCard && selectedConfigRow == r);
                    Text lTxt = card.RowLabels[r];
                    Text vTxt = card.RowValues[r];

                    string prefix = isSelectedRow ? "► " : "";
                    lTxt.color = isSelectedRow ? new Color(1f, 0.95f, 0.40f, 1f) : new Color(0.70f, 0.70f, 0.70f, 1f);

                    switch (r)
                    {
                        case 0: // Control
                            CoopDeviceType dev = CoopGameConfig.Slots[p].Device;
                            vTxt.color = dev == CoopDeviceType.Unassigned ? new Color(1f, 0.80f, 0.30f, 1f) : new Color(0.40f, 0.90f, 1f, 1f);
                            vTxt.text = prefix + CoopGameConfig.GetDeviceDisplayName(dev);
                            break;

                        case 1: // Nombre
                            if (isEditingName && p == editingPlayerIndex)
                            {
                                string cursor = (Time.unscaledTime % 0.8f < 0.4f) ? "_" : " ";
                                vTxt.color = new Color(1f, 0.95f, 0.40f, 1f);
                                vTxt.text = "> " + editingBuffer + cursor;
                            }
                            else
                            {
                                vTxt.color = Color.white;
                                vTxt.text = prefix + CoopGameConfig.Slots[p].Name;
                            }
                            break;

                        case 2: // Color
                            vTxt.color = playerColor;
                            vTxt.text = prefix + $"■ {CoopGameConfig.Palette[colorIdx].Name}";
                            break;

                        case 3: // Aspecto / Skin
                            string sId = CoopGameConfig.Slots[p].SkinId;
                            string sName = CoopGameConfig.GetSkinDisplayName(sId);
                            vTxt.color = new Color(1f, 0.88f, 0.45f, 1f);
                            vTxt.text = prefix + $"◈ {sName}";

                            if (card.SkinPreview != null)
                            {
                                Sprite prevSpr = Core.ColorPaletteManager?.GetPaletteSpritePreview(sId);
                                if (prevSpr != null)
                                {
                                    card.SkinPreview.sprite = prevSpr;
                                    card.SkinPreview.gameObject.SetActive(true);
                                }
                                else
                                {
                                    card.SkinPreview.gameObject.SetActive(false);
                                }
                            }
                            break;
                    }
                }
            }
            else
            {
                card.CardObj.SetActive(false);
            }
        }

        // 3. Botones inferiores
        bool isBottomZone = (currentZone == MenuZone.BottomActions);
        bool isStart = isBottomZone && (bottomActionIndex == 0);
        bool isCancel = isBottomZone && (bottomActionIndex == 1);

        startButtonOutline.effectColor = isStart ? new Color(0.40f, 1f, 0.50f, 1f) : new Color(0.20f, 0.50f, 0.30f, 0.60f);
        startButtonOutline.effectDistance = isStart ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        startButtonText.text = (isStart ? "► " : "") + "¡COMENZAR PARTIDA!";

        cancelButtonOutline.effectColor = isCancel ? new Color(1f, 0.45f, 0.45f, 1f) : new Color(0.50f, 0.20f, 0.20f, 0.60f);
        cancelButtonOutline.effectDistance = isCancel ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        cancelButtonText.text = (isCancel ? "► " : "") + "VOLVER / CANCELAR";
    }

    private void ConfirmAndStart()
    {
        CoopGameConfig.ApplyToRegistry();
        CoopGameConfig.Save();
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        Close();
        onConfirmedCallback?.Invoke();
    }

    private void CancelAndReturn()
    {
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        Close();
        onCancelledCallback?.Invoke();
    }

    private static void PlayNavSound()
    {
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
    }

    private static void PlayToggleSound()
    {
        Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
    }

    private static GameObject CreateUiElement(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Font GetGameFont()
    {
        foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
        {
            if (f != null && (f.name.Contains("Majestic") || f.name.Contains("FullLatin")))
            {
                return f;
            }
        }
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
