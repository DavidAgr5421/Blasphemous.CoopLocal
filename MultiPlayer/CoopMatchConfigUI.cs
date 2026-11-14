using System;
using System.Text;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Gameplay.UI.Widgets;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Interfaz para configurar una partida guardada ya existente antes de entrar a jugar.
/// Permite añadir o eliminar jugadores, alternar entre control local y online (Steam P2P),
/// verificar y copiar el Código de Sala, y cuenta con modales de confirmación para proteger al usuario.
/// </summary>
public class CoopMatchConfigUI : MonoBehaviour
{
    private static CoopMatchConfigUI _instance;
    public static CoopMatchConfigUI Instance
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

    // Callbacks y datos del slot
    private int currentSlotIndex = 0;
    private SaveSlot activeSaveSlot;
    private Action onPlayCallback;
    private Action onCancelCallback;

    // Componentes del Canvas
    private GameObject rootCanvasObj;
    private Text headerTitleText;
    private Text headerSubtitleText;

    // Sección de Código de Sala
    private Text roomCodeTitleText;
    private Text roomCodeValueText;
    private Text roomCodeCopyTipText;
    private Text roomCodeFeedbackText;
    private float feedbackTimer = 0f;

    // Tarjetas de jugadores (P1 a P4)
    private readonly PlayerCardUI[] playerCards = new PlayerCardUI[4];

    private sealed class PlayerCardUI
    {
        public GameObject cardObj;
        public Image cardBg;
        public Outline cardOutline;
        public Text headerText;

        // Filas para jugador activo
        public GameObject activeContentObj;
        public Text modeValueText;
        public Text nameValueText;
        public Text colorValueText;
        public Text skinValueText;
        public GameObject deleteButtonObj;
        public Text deleteButtonText;
        public Outline deleteButtonOutline;

        // Fila para jugador inactivo (botón Añadir)
        public GameObject addContentObj;
        public Text addButtonText;
        public Outline addButtonOutline;
    }

    // Botones de acción inferiores
    private GameObject playButtonObj;
    private Outline playButtonOutline;
    private Text playButtonText;

    private GameObject backButtonObj;
    private Outline backButtonOutline;
    private Text backButtonText;

    private Text legendText;

    // Modal de Confirmación
    private GameObject confirmModalOverlay;
    private Text confirmModalTitleText;
    private Text confirmModalDescText;
    private GameObject confirmModalYesBtn;
    private Outline confirmModalYesOutline;
    private Text confirmModalYesText;
    private GameObject confirmModalNoBtn;
    private Outline confirmModalNoOutline;
    private Text confirmModalNoText;

    private enum ConfirmationKind
    {
        None,
        DeletePlayer,
        ChangeToOnline
    }

    private bool isConfirmModalOpen = false;
    private ConfirmationKind confirmKind = ConfirmationKind.None;
    private int confirmTargetPlayer = 1;
    private int confirmButtonIndex = 0; // 0 = Confirmar, 1 = Cancelar

    // Estado de navegación
    // zone: 0 = Tarjetas de Jugadores, 1 = Botones Inferiores
    private int currentZone = 0;
    private int selectedCardIdx = 0; // 0 a 3
    private int selectedRowIdx = 0;  // 0=Modo, 1=Nombre, 2=Color, 3=Skin, 4=Eliminar/Añadir
    private int bottomButtonIdx = 0; // 0=Jugar, 1=Volver

    // Edición de nombre
    private bool isEditingName = false;
    private string nameBuffer = "";

    private float inputCooldown = 0f;
    private float cursorBlinkTimer = 0f;
    private bool cursorVisible = true;

    private Font gameFont;

    private static GameObject CreateUiElement(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void CreateUI()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("CoopMatchConfigUI", typeof(RectTransform));
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopMatchConfigUI>();
        _instance.BuildInterface();
    }

    public static void Show(int slotIndex, SaveSlot slot, Action onPlay, Action onCancel)
    {
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopMatchConfigUI] Show invocado para ranura {slotIndex}", Main.CoopLocal);
        }
        CreateUI();
        _instance.currentSlotIndex = slotIndex;
        _instance.activeSaveSlot = slot;
        _instance.onPlayCallback = onPlay;
        _instance.onCancelCallback = onCancel;
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

        gameFont = CoopGameSetupUI.GetGameFont();

        rootCanvasObj = CreateUiElement("CoopMatchConfigCanvas", transform);
        Canvas canvas = rootCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32500;

        CanvasScaler scaler = rootCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        rootCanvasObj.AddComponent<GraphicRaycaster>();

        // Fondo oscuro
        GameObject darkOverlay = CreateUiElement("DarkOverlay", rootCanvasObj.transform);

        RectTransform doRt = darkOverlay.GetComponent<RectTransform>();
        doRt.anchorMin = Vector2.zero;
        doRt.anchorMax = Vector2.one;
        doRt.sizeDelta = Vector2.zero;
        Image doImg = darkOverlay.AddComponent<Image>();
        doImg.color = new Color(0.03f, 0.03f, 0.04f, 0.94f);

        // Panel Principal
        GameObject mainPanel = CreateUiElement("MainPanel", rootCanvasObj.transform);

        RectTransform mpRt = mainPanel.GetComponent<RectTransform>();
        mpRt.anchorMin = new Vector2(0.5f, 0.5f);
        mpRt.anchorMax = new Vector2(0.5f, 0.5f);
        mpRt.pivot = new Vector2(0.5f, 0.5f);
        mpRt.sizeDelta = new Vector2(1480f, 840f);

        Image mpImg = mainPanel.AddComponent<Image>();
        mpImg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
        Outline mpOut = mainPanel.AddComponent<Outline>();
        mpOut.effectColor = new Color(0.75f, 0.55f, 0.20f, 0.85f);
        mpOut.effectDistance = new Vector2(3f, -3f);

        // Título
        GameObject titleObj = CreateUiElement("Title", mainPanel.transform);

        RectTransform tRt = titleObj.GetComponent<RectTransform>();
        tRt.anchoredPosition = new Vector2(0f, 375f);
        tRt.sizeDelta = new Vector2(1300f, 40f);
        headerTitleText = titleObj.AddComponent<Text>();
        headerTitleText.font = gameFont;
        headerTitleText.fontSize = 26;
        headerTitleText.alignment = TextAnchor.MiddleCenter;
        headerTitleText.color = new Color(1f, 0.88f, 0.45f, 1f);
        headerTitleText.text = "=== CONFIGURACIÓN DE PARTIDA GUARDADA ===";

        // Subtítulo con información de la ranura
        GameObject subObj = CreateUiElement("Subtitle", mainPanel.transform);

        RectTransform subRt = subObj.GetComponent<RectTransform>();
        subRt.anchoredPosition = new Vector2(0f, 335f);
        subRt.sizeDelta = new Vector2(1300f, 30f);
        headerSubtitleText = subObj.AddComponent<Text>();
        headerSubtitleText.font = gameFont;
        headerSubtitleText.fontSize = 16;
        headerSubtitleText.alignment = TextAnchor.MiddleCenter;
        headerSubtitleText.color = new Color(0.80f, 0.80f, 0.80f, 1f);
        headerSubtitleText.text = "RANURA 1";

        // Banner de Código de Sala
        BuildRoomCodeBanner(mainPanel.transform);

        // Tarjetas de los 4 Jugadores
        BuildPlayerCards(mainPanel.transform);

        // Botones de acción inferiores
        BuildBottomActions(mainPanel.transform);

        // Modal de Confirmación
        BuildConfirmationModal(rootCanvasObj.transform);

        rootCanvasObj.SetActive(false);
    }

    private void BuildRoomCodeBanner(Transform parent)
    {
        GameObject bannerObj = CreateUiElement("RoomCodeBanner", parent);

        RectTransform bnRt = bannerObj.GetComponent<RectTransform>();
        bnRt.anchoredPosition = new Vector2(0f, 250f);
        bnRt.sizeDelta = new Vector2(1000f, 68f);

        Image bnImg = bannerObj.AddComponent<Image>();
        bnImg.color = new Color(0.10f, 0.12f, 0.16f, 0.95f);
        Outline bnOut = bannerObj.AddComponent<Outline>();
        bnOut.effectColor = new Color(0.20f, 0.70f, 0.85f, 0.75f);
        bnOut.effectDistance = new Vector2(2f, -2f);

        // Etiqueta del código
        GameObject lblObj = CreateUiElement("Label", bannerObj.transform);

        RectTransform lRt = lblObj.GetComponent<RectTransform>();
        lRt.anchoredPosition = new Vector2(-220f, 12f);
        lRt.sizeDelta = new Vector2(480f, 30f);
        roomCodeTitleText = lblObj.AddComponent<Text>();
        roomCodeTitleText.font = gameFont;
        roomCodeTitleText.fontSize = 16;
        roomCodeTitleText.alignment = TextAnchor.MiddleRight;
        roomCodeTitleText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        roomCodeTitleText.text = "CÓDIGO DE SALA ONLINE (STEAM):";

        // Valor del código
        GameObject valObj = CreateUiElement("Value", bannerObj.transform);

        RectTransform vRt = valObj.GetComponent<RectTransform>();
        vRt.anchoredPosition = new Vector2(120f, 12f);
        vRt.sizeDelta = new Vector2(300f, 34f);
        roomCodeValueText = valObj.AddComponent<Text>();
        roomCodeValueText.font = gameFont;
        roomCodeValueText.fontSize = 22;
        roomCodeValueText.alignment = TextAnchor.MiddleLeft;
        roomCodeValueText.color = new Color(0f, 0.95f, 1f, 1f);
        roomCodeValueText.text = "[ SIN CÓDIGO ]";

        // Subtexto para copiar código
        GameObject tipObj = CreateUiElement("CopyTip", bannerObj.transform);

        RectTransform tipRt = tipObj.GetComponent<RectTransform>();
        tipRt.anchoredPosition = new Vector2(0f, -16f);
        tipRt.sizeDelta = new Vector2(960f, 24f);
        roomCodeCopyTipText = tipObj.AddComponent<Text>();
        roomCodeCopyTipText.font = gameFont;
        roomCodeCopyTipText.fontSize = 13;
        roomCodeCopyTipText.alignment = TextAnchor.MiddleCenter;
        roomCodeCopyTipText.color = new Color(0.70f, 0.70f, 0.70f, 1f);
        roomCodeCopyTipText.text = "Presiona [ C ] para copiar el código y compartirlo con tus amigos";

        // Feedback al copiar
        GameObject fbObj = CreateUiElement("Feedback", bannerObj.transform);

        RectTransform fbRt = fbObj.GetComponent<RectTransform>();
        fbRt.anchoredPosition = new Vector2(380f, 12f);
        fbRt.sizeDelta = new Vector2(200f, 30f);
        roomCodeFeedbackText = fbObj.AddComponent<Text>();
        roomCodeFeedbackText.font = gameFont;
        roomCodeFeedbackText.fontSize = 13;
        roomCodeFeedbackText.alignment = TextAnchor.MiddleLeft;
        roomCodeFeedbackText.color = Color.green;
        roomCodeFeedbackText.text = "";
    }

    private void BuildPlayerCards(Transform parent)
    {
        float startX = -510f;
        float spacingX = 340f;
        float posY = 20f;
        float cardW = 310f;
        float cardH = 360f;

        for (int i = 0; i < 4; i++)
        {
            playerCards[i] = new PlayerCardUI();

            GameObject cObj = CreateUiElement($"Card_{i}", parent);


            RectTransform cRt = cObj.GetComponent<RectTransform>();
            cRt.anchoredPosition = new Vector2(startX + i * spacingX, posY);
            cRt.sizeDelta = new Vector2(cardW, cardH);

            playerCards[i].cardObj = cObj;
            playerCards[i].cardBg = cObj.AddComponent<Image>();
            playerCards[i].cardBg.color = new Color(0.11f, 0.11f, 0.14f, 0.95f);

            playerCards[i].cardOutline = cObj.AddComponent<Outline>();
            playerCards[i].cardOutline.effectDistance = new Vector2(2f, -2f);
            playerCards[i].cardOutline.effectColor = new Color(0.35f, 0.35f, 0.38f, 0.7f);

            // Cabecera Jugador
            GameObject hdrObj = CreateUiElement("Header", cObj.transform);

            RectTransform hRt = hdrObj.GetComponent<RectTransform>();
            hRt.anchoredPosition = new Vector2(0f, 150f);
            hRt.sizeDelta = new Vector2(cardW - 20f, 35f);
            playerCards[i].headerText = hdrObj.AddComponent<Text>();
            playerCards[i].headerText.font = gameFont;
            playerCards[i].headerText.fontSize = 18;
            playerCards[i].headerText.alignment = TextAnchor.MiddleCenter;
            playerCards[i].headerText.color = Color.white;
            playerCards[i].headerText.text = i == 0 ? "JUGADOR 1 (HOST)" : $"JUGADOR {i + 1}";

            // Contenedor para Jugador Activo
            GameObject activeObj = CreateUiElement("ActiveContent", cObj.transform);

            RectTransform aRt = activeObj.GetComponent<RectTransform>();
            aRt.anchoredPosition = Vector2.zero;
            aRt.sizeDelta = new Vector2(cardW, cardH);
            playerCards[i].activeContentObj = activeObj;

            // Filas
            playerCards[i].modeValueText = CreateCardRow(activeObj.transform, "CONTROL:", 95f);
            playerCards[i].nameValueText = CreateCardRow(activeObj.transform, "NOMBRE:", 45f);
            playerCards[i].colorValueText = CreateCardRow(activeObj.transform, "COLOR:", -5f);
            playerCards[i].skinValueText = CreateCardRow(activeObj.transform, "ASPECTO:", -55f);

            // Botón Eliminar (solo para P2, P3, P4)
            if (i > 0)
            {
                GameObject delObj = CreateUiElement("DeleteBtn", activeObj.transform);

                RectTransform dRt = delObj.GetComponent<RectTransform>();
                dRt.anchoredPosition = new Vector2(0f, -125f);
                dRt.sizeDelta = new Vector2(cardW - 40f, 38f);
                Image dImg = delObj.AddComponent<Image>();
                dImg.color = new Color(0.30f, 0.10f, 0.10f, 0.95f);
                playerCards[i].deleteButtonOutline = delObj.AddComponent<Outline>();
                playerCards[i].deleteButtonOutline.effectColor = new Color(0.85f, 0.25f, 0.25f, 0.8f);

                GameObject dtObj = CreateUiElement("Text", delObj.transform);


                RectTransform dtRt = dtObj.GetComponent<RectTransform>();
                dtRt.sizeDelta = new Vector2(cardW - 50f, 35f);
                playerCards[i].deleteButtonText = dtObj.AddComponent<Text>();
                playerCards[i].deleteButtonText.font = gameFont;
                playerCards[i].deleteButtonText.fontSize = 14;
                playerCards[i].deleteButtonText.alignment = TextAnchor.MiddleCenter;
                playerCards[i].deleteButtonText.color = new Color(1f, 0.70f, 0.70f, 1f);
                playerCards[i].deleteButtonText.text = "✖ ELIMINAR JUGADOR";
                playerCards[i].deleteButtonObj = delObj;
            }

            // Contenedor para Jugador Inactivo (Añadir Jugador)
            GameObject addObj = CreateUiElement("AddContent", cObj.transform);

            RectTransform addRt = addObj.GetComponent<RectTransform>();
            addRt.anchoredPosition = Vector2.zero;
            addRt.sizeDelta = new Vector2(cardW, cardH);
            playerCards[i].addContentObj = addObj;

            GameObject addBtn = CreateUiElement("AddBtn", addObj.transform);


            RectTransform abRt = addBtn.GetComponent<RectTransform>();
            abRt.anchoredPosition = new Vector2(0f, 0f);
            abRt.sizeDelta = new Vector2(cardW - 40f, 70f);
            Image abImg = addBtn.AddComponent<Image>();
            abImg.color = new Color(0.12f, 0.25f, 0.16f, 0.95f);
            playerCards[i].addButtonOutline = addBtn.AddComponent<Outline>();
            playerCards[i].addButtonOutline.effectColor = new Color(0.35f, 0.80f, 0.45f, 0.8f);

            GameObject abTxtObj = CreateUiElement("Text", addBtn.transform);


            RectTransform abtRt = abTxtObj.GetComponent<RectTransform>();
            abtRt.sizeDelta = new Vector2(cardW - 50f, 65f);
            playerCards[i].addButtonText = abTxtObj.AddComponent<Text>();
            playerCards[i].addButtonText.font = gameFont;
            playerCards[i].addButtonText.fontSize = 16;
            playerCards[i].addButtonText.alignment = TextAnchor.MiddleCenter;
            playerCards[i].addButtonText.color = Color.white;
            playerCards[i].addButtonText.text = $"➕ AÑADIR JUGADOR {i + 1}";
        }
    }

    private Text CreateCardRow(Transform parent, string label, float posY)
    {
        GameObject row = CreateUiElement("Row", parent);

        RectTransform rRt = row.GetComponent<RectTransform>();
        rRt.anchoredPosition = new Vector2(0f, posY);
        rRt.sizeDelta = new Vector2(280f, 35f);

        GameObject lObj = CreateUiElement("Label", row.transform);


        RectTransform lRt = lObj.GetComponent<RectTransform>();
        lRt.anchoredPosition = new Vector2(-60f, 0f);
        lRt.sizeDelta = new Vector2(140f, 30f);
        Text lTxt = lObj.AddComponent<Text>();
        lTxt.font = gameFont;
        lTxt.fontSize = 13;
        lTxt.alignment = TextAnchor.MiddleLeft;
        lTxt.color = new Color(0.70f, 0.70f, 0.70f, 1f);
        lTxt.text = label;

        GameObject vObj = CreateUiElement("Value", row.transform);


        RectTransform vRt = vObj.GetComponent<RectTransform>();
        vRt.anchoredPosition = new Vector2(40f, 0f);
        vRt.sizeDelta = new Vector2(170f, 30f);
        Text vTxt = vObj.AddComponent<Text>();
        vTxt.font = gameFont;
        vTxt.fontSize = 13;
        vTxt.alignment = TextAnchor.MiddleRight;
        vTxt.color = Color.white;
        vTxt.text = "-";

        return vTxt;
    }

    private void BuildBottomActions(Transform parent)
    {
        // Botón Jugar
        playButtonObj = CreateUiElement("PlayButton", parent);

        RectTransform pRt = playButtonObj.GetComponent<RectTransform>();
        pRt.anchoredPosition = new Vector2(-180f, -330f);
        pRt.sizeDelta = new Vector2(340f, 52f);
        Image pImg = playButtonObj.AddComponent<Image>();
        pImg.color = new Color(0.12f, 0.32f, 0.16f, 0.95f);
        playButtonOutline = playButtonObj.AddComponent<Outline>();
        playButtonOutline.effectColor = new Color(0.35f, 0.85f, 0.45f, 0.85f);

        GameObject ptObj = CreateUiElement("Text", playButtonObj.transform);


        RectTransform ptRt = ptObj.GetComponent<RectTransform>();
        ptRt.sizeDelta = new Vector2(320f, 48f);
        playButtonText = ptObj.AddComponent<Text>();
        playButtonText.font = gameFont;
        playButtonText.fontSize = 18;
        playButtonText.alignment = TextAnchor.MiddleCenter;
        playButtonText.color = Color.white;
        playButtonText.text = "▶ JUGAR / CONTINUAR (ENTER)";

        // Botón Volver
        backButtonObj = CreateUiElement("BackButton", parent);

        RectTransform bRt = backButtonObj.GetComponent<RectTransform>();
        bRt.anchoredPosition = new Vector2(220f, -330f);
        bRt.sizeDelta = new Vector2(260f, 52f);
        Image bImg = backButtonObj.AddComponent<Image>();
        bImg.color = new Color(0.24f, 0.12f, 0.12f, 0.95f);
        backButtonOutline = backButtonObj.AddComponent<Outline>();
        backButtonOutline.effectColor = new Color(0.75f, 0.30f, 0.30f, 0.85f);

        GameObject btObj = CreateUiElement("Text", backButtonObj.transform);


        RectTransform btRt = btObj.GetComponent<RectTransform>();
        btRt.sizeDelta = new Vector2(240f, 48f);
        backButtonText = btObj.AddComponent<Text>();
        backButtonText.font = gameFont;
        backButtonText.fontSize = 18;
        backButtonText.alignment = TextAnchor.MiddleCenter;
        backButtonText.color = Color.white;
        backButtonText.text = "⬅ VOLVER (ESC)";

        // Leyenda
        GameObject lgdObj = CreateUiElement("Legend", parent);

        RectTransform lgdRt = lgdObj.GetComponent<RectTransform>();
        lgdRt.anchoredPosition = new Vector2(0f, -385f);
        lgdRt.sizeDelta = new Vector2(1300f, 30f);
        legendText = lgdObj.AddComponent<Text>();
        legendText.font = gameFont;
        legendText.fontSize = 13;
        legendText.alignment = TextAnchor.MiddleCenter;
        legendText.color = new Color(0.65f, 0.65f, 0.65f, 1f);
        legendText.text = "[W/A/S/D / Flechas] Navegar   |   [ENTER] Seleccionar/Editar   |   [C] Copiar Código de Sala   |   [ESC] Volver";
    }

    private void BuildConfirmationModal(Transform parent)
    {
        confirmModalOverlay = CreateUiElement("ConfirmModalOverlay", parent);

        RectTransform moRt = confirmModalOverlay.GetComponent<RectTransform>();
        moRt.anchorMin = Vector2.zero;
        moRt.anchorMax = Vector2.one;
        moRt.sizeDelta = Vector2.zero;

        Image moImg = confirmModalOverlay.AddComponent<Image>();
        moImg.color = new Color(0.02f, 0.02f, 0.03f, 0.88f);

        // Diálogo central
        GameObject dialogObj = CreateUiElement("Dialog", confirmModalOverlay.transform);

        RectTransform dRt = dialogObj.GetComponent<RectTransform>();
        dRt.anchorMin = new Vector2(0.5f, 0.5f);
        dRt.anchorMax = new Vector2(0.5f, 0.5f);
        dRt.pivot = new Vector2(0.5f, 0.5f);
        dRt.sizeDelta = new Vector2(650f, 320f);

        Image dImg = dialogObj.AddComponent<Image>();
        dImg.color = new Color(0.08f, 0.08f, 0.11f, 0.98f);
        Outline dOut = dialogObj.AddComponent<Outline>();
        dOut.effectColor = new Color(0.85f, 0.65f, 0.20f, 0.9f);
        dOut.effectDistance = new Vector2(2.5f, -2.5f);

        // Título del modal
        GameObject mtObj = CreateUiElement("Title", dialogObj.transform);

        RectTransform mtRt = mtObj.GetComponent<RectTransform>();
        mtRt.anchoredPosition = new Vector2(0f, 105f);
        mtRt.sizeDelta = new Vector2(600f, 35f);
        confirmModalTitleText = mtObj.AddComponent<Text>();
        confirmModalTitleText.font = gameFont;
        confirmModalTitleText.fontSize = 20;
        confirmModalTitleText.alignment = TextAnchor.MiddleCenter;
        confirmModalTitleText.color = new Color(1f, 0.85f, 0.40f, 1f);
        confirmModalTitleText.text = "¿ESTÁS SEGURO?";

        // Descripción
        GameObject mdObj = CreateUiElement("Description", dialogObj.transform);

        RectTransform mdRt = mdObj.GetComponent<RectTransform>();
        mdRt.anchoredPosition = new Vector2(0f, 25f);
        mdRt.sizeDelta = new Vector2(580f, 90f);
        confirmModalDescText = mdObj.AddComponent<Text>();
        confirmModalDescText.font = gameFont;
        confirmModalDescText.fontSize = 15;
        confirmModalDescText.alignment = TextAnchor.MiddleCenter;
        confirmModalDescText.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        confirmModalDescText.text = "Mensaje de advertencia...";

        // Botón Confirmar
        confirmModalYesBtn = CreateUiElement("YesButton", dialogObj.transform);

        RectTransform yRt = confirmModalYesBtn.GetComponent<RectTransform>();
        yRt.anchoredPosition = new Vector2(-130f, -85f);
        yRt.sizeDelta = new Vector2(220f, 46f);
        Image yImg = confirmModalYesBtn.AddComponent<Image>();
        yImg.color = new Color(0.12f, 0.32f, 0.16f, 0.95f);
        confirmModalYesOutline = confirmModalYesBtn.AddComponent<Outline>();
        confirmModalYesOutline.effectColor = new Color(0.40f, 0.90f, 0.50f, 0.8f);

        GameObject ytObj = CreateUiElement("Text", confirmModalYesBtn.transform);


        RectTransform ytRt = ytObj.GetComponent<RectTransform>();
        ytRt.sizeDelta = new Vector2(200f, 40f);
        confirmModalYesText = ytObj.AddComponent<Text>();
        confirmModalYesText.font = gameFont;
        confirmModalYesText.fontSize = 15;
        confirmModalYesText.alignment = TextAnchor.MiddleCenter;
        confirmModalYesText.color = Color.white;
        confirmModalYesText.text = "CONFIRMAR (ENTER)";

        // Botón Cancelar
        confirmModalNoBtn = CreateUiElement("NoButton", dialogObj.transform);

        RectTransform nRt = confirmModalNoBtn.GetComponent<RectTransform>();
        nRt.anchoredPosition = new Vector2(130f, -85f);
        nRt.sizeDelta = new Vector2(200f, 46f);
        Image nImg = confirmModalNoBtn.AddComponent<Image>();
        nImg.color = new Color(0.25f, 0.12f, 0.12f, 0.95f);
        confirmModalNoOutline = confirmModalNoBtn.AddComponent<Outline>();
        confirmModalNoOutline.effectColor = new Color(0.80f, 0.35f, 0.35f, 0.8f);

        GameObject ntObj = CreateUiElement("Text", confirmModalNoBtn.transform);


        RectTransform ntRt = ntObj.GetComponent<RectTransform>();
        ntRt.sizeDelta = new Vector2(180f, 40f);
        confirmModalNoText = ntObj.AddComponent<Text>();
        confirmModalNoText.font = gameFont;
        confirmModalNoText.fontSize = 15;
        confirmModalNoText.alignment = TextAnchor.MiddleCenter;
        confirmModalNoText.color = Color.white;
        confirmModalNoText.text = "CANCELAR (ESC)";

        confirmModalOverlay.SetActive(false);
    }

    private void Open()
    {
        try
        {
            BuildInterface();
            IsOpen = true;
            currentZone = 0;
            selectedCardIdx = 0;
            selectedRowIdx = 0;
            bottomButtonIdx = 0;
            isEditingName = false;
            isConfirmModalOpen = false;
            inputCooldown = 0.25f;

            // Deshabilitar navegación vanilla de Unity UI
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = false;
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }

            // Cargar datos de la ranura
            CoopSaveManager.TargetSetupSlot = currentSlotIndex;
            var slotData = CoopSaveManager.GetSlotData(currentSlotIndex);
            CoopSaveManager.ApplyConfigToGame(slotData);

            // Si hay algún jugador configurado como online, asegurar que exista el código de sala
            EnsureRoomCodeIfOnline();

            // Aplicar fuente auténtica del juego a toda la ventana
            CoopGameSetupUI.ApplyGameFont(rootCanvasObj);

            UpdateHeaderInfo();
            UpdateDisplay();

            if (rootCanvasObj != null)
            {
                rootCanvasObj.SetActive(true);
            }
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[CoopMatchConfigUI] Ventana abierta exitosamente para ranura {currentSlotIndex}", Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CoopMatchConfigUI] Error abriendo interfaz: {ex}");
            IsOpen = false;
            onPlayCallback?.Invoke();
        }
    }

    private void Close()
    {
        IsOpen = false;
        isEditingName = false;
        isConfirmModalOpen = false;

        if (rootCanvasObj != null)
        {
            rootCanvasObj.SetActive(false);
        }

        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = true;
        }

        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        SelectSaveSlots_OnAcceptSlots_CoopSetup_Patch.SuppressVanillaSaveSlotsFrames = 5;
        onCancelCallback?.Invoke();
    }

    private void EnsureRoomCodeIfOnline()
    {
        bool hasOnline = false;
        for (int i = 1; i < CoopGameConfig.PlayerCount; i++)
        {
            if (CoopGameConfig.Slots[i].Device == CoopDeviceType.RemoteSteam)
            {
                hasOnline = true;
                break;
            }
        }

        if (hasOnline)
        {
            if (string.IsNullOrEmpty(CoopLobbyManager.ActiveRoomCode))
            {
                CoopLobbyManager.Instance.CreateHostLobby((success, code) =>
                {
                    UpdateRoomCodeDisplay();
                });
            }
        }
    }

    private static readonly System.Reflection.FieldInfo ZoneTextField = HarmonyLib.AccessTools.Field(typeof(SaveSlot), "ZoneText");
    private static readonly System.Reflection.FieldInfo PurgeTextField = HarmonyLib.AccessTools.Field(typeof(SaveSlot), "PurgeText");

    private void UpdateHeaderInfo()
    {
        string zoneStr = "Desconocido";
        string tearsStr = "0";

        try
        {
            if (activeSaveSlot != null)
            {
                var zTxt = ZoneTextField?.GetValue(activeSaveSlot) as Text;
                if (zTxt != null && !string.IsNullOrEmpty(zTxt.text))
                    zoneStr = zTxt.text;

                var pTxt = PurgeTextField?.GetValue(activeSaveSlot) as Text;
                if (pTxt != null && !string.IsNullOrEmpty(pTxt.text))
                    tearsStr = pTxt.text;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CoopMatchConfigUI] Error leyendo info de ranura: {ex.Message}");
        }

        if (headerSubtitleText != null)
        {
            headerSubtitleText.text = $"RANURA {currentSlotIndex + 1}   |   ZONA: {zoneStr}   |   LÁGRIMAS: {tearsStr}";
        }
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Deseleccionar cualquier objeto ajeno
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = false;
            if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
                rootCanvasObj != null &&
                !UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.transform.IsChildOf(rootCanvasObj.transform))
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
        }

        float dt = Time.unscaledDeltaTime;

        // Temporizador de feedback
        if (feedbackTimer > 0f)
        {
            feedbackTimer -= dt;
            if (feedbackTimer <= 0f && roomCodeFeedbackText != null)
            {
                roomCodeFeedbackText.text = "";
            }
        }

        // Parpadeo de cursor
        cursorBlinkTimer += dt;
        if (cursorBlinkTimer >= 0.45f)
        {
            cursorBlinkTimer = 0f;
            cursorVisible = !cursorVisible;
            if (isEditingName) UpdateDisplay();
        }

        if (inputCooldown > 0f)
        {
            inputCooldown -= dt;
            return;
        }

        // Atajo 'C' para copiar código de sala
        if (!isEditingName && Input.GetKeyDown(KeyCode.C))
        {
            CopyRoomCode();
            return;
        }

        // Si el Modal de Confirmación está abierto, manejar su entrada exclusivamente
        if (isConfirmModalOpen)
        {
            HandleConfirmationModalInput();
            return;
        }

        // Si se está editando nombre
        if (isEditingName)
        {
            HandleNameTyping();
            return;
        }

        bool cancel = Input.GetKeyDown(KeyCode.Escape)
            || Input.GetKeyDown(KeyCode.JoystickButton1)
            || (Player2Pad.P1Pad != null && Player2Pad.ButtonDown(Player2Pad.P1Pad, "B Button", "Button B", "Circle", "Button 1", "B"))
            || (Player2Pad.Pad != null && Player2Pad.ButtonDown(Player2Pad.Pad, "B Button", "Button B", "Circle", "Button 1", "B"));

        if (!isEditingName && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.JoystickButton2) || (Player2Pad.P1Pad != null && Player2Pad.ButtonDown(Player2Pad.P1Pad, "X Button", "Button X", "Square", "Button 2", "X"))))
        {
            CopyRoomCode();
            return;
        }

        if (cancel)
        {
            Close();
            return;
        }

        // Atajo directo para iniciar partida (Espacio / Intro del teclado numérico / Start de mando)
        if (!isEditingName && !isConfirmModalOpen && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton7)))
        {
            StartGame();
            return;
        }

        // Navegación principal
        HandleMainNavigation();
    }

    private void HandleMainNavigation()
    {
        bool padUp = (Player2Pad.P1Pad != null && Player2Pad.GetUp(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetUp(Player2Pad.Pad));
        bool padDown = (Player2Pad.P1Pad != null && Player2Pad.GetDown(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetDown(Player2Pad.Pad));
        bool padLeft = (Player2Pad.P1Pad != null && Player2Pad.GetLeft(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetLeft(Player2Pad.Pad));
        bool padRight = (Player2Pad.P1Pad != null && Player2Pad.GetRight(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetRight(Player2Pad.Pad));

        bool up = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || padUp;
        bool down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || padDown;
        bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) || padLeft;
        bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) || padRight;
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0)
            || (Player2Pad.P1Pad != null && Player2Pad.GetSelectDown(Player2Pad.P1Pad))
            || (Player2Pad.Pad != null && Player2Pad.GetSelectDown(Player2Pad.Pad));

        if (padUp || padDown || padLeft || padRight)
        {
            inputCooldown = 0.20f;
        }

        if (currentZone == 0) // Zona de Tarjetas
        {
            bool isCardActive = selectedCardIdx < CoopGameConfig.PlayerCount;

            if (left)
            {
                selectedCardIdx = (selectedCardIdx - 1 + 4) % 4;
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateDisplay();
                return;
            }
            if (right)
            {
                selectedCardIdx = (selectedCardIdx + 1) % 4;
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateDisplay();
                return;
            }

            if (isCardActive)
            {
                int maxRows = selectedCardIdx == 0 ? 4 : 5; // P1 no tiene fila de Eliminar

                if (up)
                {
                    selectedRowIdx--;
                    if (selectedRowIdx < 0) selectedRowIdx = maxRows - 1;
                    Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                    UpdateDisplay();
                    return;
                }
                if (down)
                {
                    selectedRowIdx++;
                    if (selectedRowIdx >= maxRows)
                    {
                        // Bajar a botones inferiores
                        currentZone = 1;
                        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                        UpdateDisplay();
                        return;
                    }
                    Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                    UpdateDisplay();
                    return;
                }

                // Interacciones en filas de tarjeta activa
                if (selectedRowIdx == 0) // Fila de Modo de Control (Local / Online)
                {
                    if (selectedCardIdx > 0 && (enter || left || right))
                    {
                        TogglePlayerMode(selectedCardIdx);
                        return;
                    }
                }
                else if (selectedRowIdx == 1) // Nombre
                {
                    if (enter)
                    {
                        StartEditingName(selectedCardIdx);
                        return;
                    }
                }
                else if (selectedRowIdx == 2) // Color
                {
                    if (left) CycleColor(selectedCardIdx, -1);
                    else if (right || enter) CycleColor(selectedCardIdx, 1);
                }
                else if (selectedRowIdx == 3) // Skin
                {
                    if (left) CycleSkin(selectedCardIdx, -1);
                    else if (right || enter) CycleSkin(selectedCardIdx, 1);
                }
                else if (selectedRowIdx == 4) // Botón Eliminar Jugador
                {
                    if (enter)
                    {
                        RequestDeletePlayer(selectedCardIdx);
                        return;
                    }
                }
            }
            else // Tarjeta Inactiva (Botón Añadir Jugador)
            {
                if (down)
                {
                    currentZone = 1;
                    Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                    UpdateDisplay();
                    return;
                }

                if (enter)
                {
                    AddPlayer(selectedCardIdx);
                    return;
                }
            }
        }
        else // Zona 1: Botones Inferiores (Jugar / Volver)
        {
            if (up)
            {
                currentZone = 0;
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateDisplay();
                return;
            }

            if (left || right)
            {
                bottomButtonIdx = 1 - bottomButtonIdx;
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateDisplay();
                return;
            }

            if (enter)
            {
                if (bottomButtonIdx == 0)
                {
                    // Confirmar e Iniciar Partida
                    StartGame();
                }
                else
                {
                    // Volver
                    Close();
                }
            }
        }
    }

    private void TogglePlayerMode(int playerIdx)
    {
        if (playerIdx <= 0 || playerIdx >= 4) return;

        var currentDevice = CoopGameConfig.Slots[playerIdx].Device;
        if (currentDevice != CoopDeviceType.RemoteSteam)
        {
            // Requiere Confirmación para cambiar a Online (Requirement 3)
            OpenConfirmationModal(ConfirmationKind.ChangeToOnline, playerIdx);
        }
        else
        {
            // Cambiar de Online a Local directamente
            CoopGameConfig.Slots[playerIdx].Device = (playerIdx == 1) ? CoopDeviceType.Keyboard : CoopDeviceType.Gamepad2;
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
            EnsureRoomCodeIfOnline();
            UpdateDisplay();
        }
    }

    private void RequestDeletePlayer(int playerIdx)
    {
        if (playerIdx <= 0 || playerIdx >= 4) return;

        // Requiere Confirmación para eliminar jugador (Requirement 3)
        OpenConfirmationModal(ConfirmationKind.DeletePlayer, playerIdx);
    }

    private void AddPlayer(int playerIdx)
    {
        if (playerIdx < CoopGameConfig.PlayerCount) return;

        CoopGameConfig.PlayerCount = playerIdx + 1;
        var slot = CoopGameConfig.Slots[playerIdx];
        if (slot.Device == CoopDeviceType.Unassigned || slot.Device == CoopDeviceType.RemoteSteam)
        {
            slot.Device = (playerIdx == 1) ? CoopDeviceType.Keyboard : (CoopDeviceType)(2 + (playerIdx - 1));
        }

        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
        selectedRowIdx = 0;
        UpdateDisplay();
    }

    private void OpenConfirmationModal(ConfirmationKind kind, int targetPlayer)
    {
        isConfirmModalOpen = true;
        confirmKind = kind;
        confirmTargetPlayer = targetPlayer;
        confirmButtonIndex = 0;

        if (kind == ConfirmationKind.DeletePlayer)
        {
            confirmModalTitleText.text = $"<color=#ff4444>⚠ ¿ELIMINAR AL JUGADOR {targetPlayer + 1}?</color>";
            confirmModalDescText.text = $"¿Estás seguro de que deseas eliminar al Jugador {targetPlayer + 1} de esta partida?\n" +
                                        "Se removerá este personaje y se restablecerán sus datos y ranura cooperativa.";
            confirmModalYesText.text = "CONFIRMAR ELIMINACIÓN";
        }
        else if (kind == ConfirmationKind.ChangeToOnline)
        {
            confirmModalTitleText.text = $"<color=#00e5ff>🌐 ¿CAMBIAR A JUGADOR ONLINE?</color>";
            confirmModalDescText.text = $"El Jugador {targetPlayer + 1} pasará a ser controlado por un amigo remoto\n" +
                                        "a través de Steam P2P usando tu Código de Sala en lugar de un mando local.";
            confirmModalYesText.text = "CONFIRMAR ONLINE";
        }

        confirmModalOverlay.SetActive(true);
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        UpdateConfirmModalVisuals();
    }

    private void CloseConfirmationModal()
    {
        isConfirmModalOpen = false;
        confirmKind = ConfirmationKind.None;
        if (confirmModalOverlay != null)
        {
            confirmModalOverlay.SetActive(false);
        }
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
    }

    private void HandleConfirmationModalInput()
    {
        bool padLeft = (Player2Pad.P1Pad != null && Player2Pad.GetLeft(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetLeft(Player2Pad.Pad));
        bool padRight = (Player2Pad.P1Pad != null && Player2Pad.GetRight(Player2Pad.P1Pad)) || (Player2Pad.Pad != null && Player2Pad.GetRight(Player2Pad.Pad));

        bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) || padLeft;
        bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) || padRight;
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0)
            || (Player2Pad.P1Pad != null && Player2Pad.GetSelectDown(Player2Pad.P1Pad))
            || (Player2Pad.Pad != null && Player2Pad.GetSelectDown(Player2Pad.Pad));
        bool esc = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)
            || (Player2Pad.P1Pad != null && Player2Pad.ButtonDown(Player2Pad.P1Pad, "B Button", "Button B", "Circle", "Button 1", "B"))
            || (Player2Pad.Pad != null && Player2Pad.ButtonDown(Player2Pad.Pad, "B Button", "Button B", "Circle", "Button 1", "B"));

        if (padLeft || padRight)
        {
            inputCooldown = 0.20f;
        }

        if (left || right)
        {
            confirmButtonIndex = 1 - confirmButtonIndex;
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateConfirmModalVisuals();
            return;
        }

        if (esc)
        {
            CloseConfirmationModal();
            return;
        }

        if (enter)
        {
            if (confirmButtonIndex == 0) // Confirmar
            {
                ExecuteConfirmedAction();
            }
            else // Cancelar
            {
                CloseConfirmationModal();
            }
        }
    }

    private void ExecuteConfirmedAction()
    {
        if (confirmKind == ConfirmationKind.DeletePlayer)
        {
            // Reset de datos guardados del jugador
            var slotData = CoopSaveManager.GetSlotData(currentSlotIndex);
            slotData.GetPlayer(confirmTargetPlayer)?.ResetToDefaults();

            // Decrementar o reajustar cantidad de jugadores
            if (confirmTargetPlayer == CoopGameConfig.PlayerCount - 1)
            {
                CoopGameConfig.PlayerCount = Mathf.Max(1, CoopGameConfig.PlayerCount - 1);
            }
            else
            {
                // Si borra un jugador del medio, desactivarlo o marcarlo como no asignado
                CoopGameConfig.Slots[confirmTargetPlayer].Device = CoopDeviceType.Unassigned;
            }

            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        }
        else if (confirmKind == ConfirmationKind.ChangeToOnline)
        {
            CoopGameConfig.Slots[confirmTargetPlayer].Device = CoopDeviceType.RemoteSteam;
            EnsureRoomCodeIfOnline();
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
        }

        CloseConfirmationModal();
        UpdateDisplay();
    }

    private void UpdateConfirmModalVisuals()
    {
        confirmModalYesOutline.effectColor = confirmButtonIndex == 0 ? Color.yellow : new Color(0.40f, 0.90f, 0.50f, 0.6f);
        confirmModalYesText.color = confirmButtonIndex == 0 ? Color.yellow : Color.white;

        confirmModalNoOutline.effectColor = confirmButtonIndex == 1 ? Color.yellow : new Color(0.80f, 0.35f, 0.35f, 0.6f);
        confirmModalNoText.color = confirmButtonIndex == 1 ? Color.yellow : Color.white;
    }

    private void StartEditingName(int playerIdx)
    {
        isEditingName = true;
        nameBuffer = CoopGameConfig.Slots[playerIdx].Name;
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ButtonPress");
        UpdateDisplay();
    }

    private void HandleNameTyping()
    {
        foreach (char c in Input.inputString)
        {
            if (c == '\b')
            {
                if (nameBuffer.Length > 0)
                {
                    nameBuffer = nameBuffer.Substring(0, nameBuffer.Length - 1);
                    UpdateDisplay();
                }
            }
            else if (c == '\n' || c == '\r')
            {
                if (!string.IsNullOrEmpty(nameBuffer))
                {
                    CoopGameConfig.Slots[selectedCardIdx].Name = nameBuffer;
                }
                isEditingName = false;
                Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateDisplay();
                return;
            }
            else if (!char.IsControl(c) && nameBuffer.Length < 16)
            {
                nameBuffer += c;
                UpdateDisplay();
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            isEditingName = false;
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateDisplay();
        }
    }

    private void CycleColor(int playerIdx, int dir)
    {
        var slot = CoopGameConfig.Slots[playerIdx];
        slot.ColorIndex = (slot.ColorIndex + dir + CoopGameConfig.Palette.Length) % CoopGameConfig.Palette.Length;
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        UpdateDisplay();
    }

    private void CycleSkin(int playerIdx, int dir)
    {
        var skins = CoopGameConfig.AvailableSkins;
        int curIdx = 0;
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i].Id == CoopGameConfig.Slots[playerIdx].SkinId)
            {
                curIdx = i;
                break;
            }
        }
        curIdx = (curIdx + dir + skins.Length) % skins.Length;
        CoopGameConfig.Slots[playerIdx].SkinId = skins[curIdx].Id;
        Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        UpdateDisplay();
    }

    private void CopyRoomCode()
    {
        string code = CoopLobbyManager.ActiveRoomCode;
        if (!string.IsNullOrEmpty(code))
        {
            GUIUtility.systemCopyBuffer = code;
            if (roomCodeFeedbackText != null)
            {
                roomCodeFeedbackText.color = Color.green;
                roomCodeFeedbackText.text = "✔ ¡Código copiado!";
                feedbackTimer = 2.5f;
            }
            Framework.Managers.Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
        }
        else
        {
            if (roomCodeFeedbackText != null)
            {
                roomCodeFeedbackText.color = Color.yellow;
                roomCodeFeedbackText.text = "⚠ Sin sala online activa";
                feedbackTimer = 2.0f;
            }
        }
    }

    private void StartGame()
    {
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CoopMatchConfigUI] Iniciando partida para ranura {currentSlotIndex}", Main.CoopLocal);
        }
        // Guardar configuración modificada en la ranura
        var slotData = CoopSaveManager.GetSlotData(currentSlotIndex);
        CoopSaveManager.SyncConfigFromGame(slotData);
        slotData.HasCompletedSetup = true;
        CoopSaveManager.SaveSlot(currentSlotIndex);

        IsOpen = false;
        if (rootCanvasObj != null) rootCanvasObj.SetActive(false);

        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = true;
        }

        onPlayCallback?.Invoke();
    }

    private void UpdateDisplay()
    {
        UpdateRoomCodeDisplay();

        // Actualizar las 4 tarjetas
        for (int i = 0; i < 4; i++)
        {
            var card = playerCards[i];
            bool isCardActive = (i < CoopGameConfig.PlayerCount);
            bool isCardSelected = (currentZone == 0 && selectedCardIdx == i);

            // Resaltar tarjeta seleccionada
            card.cardOutline.effectColor = isCardSelected ? Color.yellow : new Color(0.35f, 0.35f, 0.38f, 0.7f);
            card.cardBg.color = isCardSelected ? new Color(0.14f, 0.14f, 0.18f, 0.98f) : new Color(0.10f, 0.10f, 0.12f, 0.95f);

            card.activeContentObj.SetActive(isCardActive);
            card.addContentObj.SetActive(!isCardActive);

            if (isCardActive)
            {
                var slot = CoopGameConfig.Slots[i];
                var pal = CoopGameConfig.Palette[Mathf.Clamp(slot.ColorIndex, 0, CoopGameConfig.Palette.Length - 1)];

                card.headerText.color = pal.Color;
                card.headerText.text = (i == 0) ? "JUGADOR 1 (HOST)" : $"JUGADOR {i + 1}";

                // Fila 0: Modo
                bool isSelRow0 = (isCardSelected && selectedRowIdx == 0);
                if (i == 0)
                {
                    card.modeValueText.text = "[ 🎮 LOCAL ]";
                    card.modeValueText.color = Color.white;
                }
                else
                {
                    bool isOnline = (slot.Device == CoopDeviceType.RemoteSteam);
                    string modeStr = isOnline ? "[ 🌐 ONLINE ]" : "[ 🎮 LOCAL ]";
                    card.modeValueText.text = isSelRow0 ? $"◄ {modeStr} ►" : modeStr;
                    card.modeValueText.color = isOnline ? new Color(0f, 0.95f, 1f, 1f) : new Color(0.40f, 0.90f, 0.50f, 1f);
                }

                // Fila 1: Nombre
                bool isSelRow1 = (isCardSelected && selectedRowIdx == 1);
                if (isEditingName && selectedCardIdx == i)
                {
                    string cur = cursorVisible ? "_" : " ";
                    card.nameValueText.text = $"[ {nameBuffer}{cur} ]";
                    card.nameValueText.color = Color.yellow;
                }
                else
                {
                    card.nameValueText.text = isSelRow1 ? $"[ {slot.Name} ]" : slot.Name;
                    card.nameValueText.color = isSelRow1 ? Color.yellow : Color.white;
                }

                // Fila 2: Color
                bool isSelRow2 = (isCardSelected && selectedRowIdx == 2);
                card.colorValueText.text = isSelRow2 ? $"◄ {pal.Name} ►" : pal.Name;
                card.colorValueText.color = pal.Color;

                // Fila 3: Skin
                bool isSelRow3 = (isCardSelected && selectedRowIdx == 3);
                string skinName = GetSkinDisplayName(slot.SkinId);
                card.skinValueText.text = isSelRow3 ? $"◄ {skinName} ►" : skinName;
                card.skinValueText.color = isSelRow3 ? Color.yellow : Color.white;

                // Fila 4: Botón Eliminar
                if (i > 0 && card.deleteButtonObj != null)
                {
                    bool isSelRow4 = (isCardSelected && selectedRowIdx == 4);
                    card.deleteButtonOutline.effectColor = isSelRow4 ? Color.yellow : new Color(0.85f, 0.25f, 0.25f, 0.8f);
                    card.deleteButtonText.color = isSelRow4 ? Color.yellow : new Color(1f, 0.70f, 0.70f, 1f);
                }
            }
            else
            {
                // Botón Añadir
                bool isAddSelected = isCardSelected;
                card.addButtonOutline.effectColor = isAddSelected ? Color.yellow : new Color(0.35f, 0.80f, 0.45f, 0.8f);
                card.addButtonText.color = isAddSelected ? Color.yellow : Color.white;
            }
        }

        // Botones inferiores
        bool isPlaySel = (currentZone == 1 && bottomButtonIdx == 0);
        playButtonOutline.effectColor = isPlaySel ? Color.yellow : new Color(0.35f, 0.85f, 0.45f, 0.85f);
        playButtonText.color = isPlaySel ? Color.yellow : Color.white;

        bool isBackSel = (currentZone == 1 && bottomButtonIdx == 1);
        backButtonOutline.effectColor = isBackSel ? Color.yellow : new Color(0.75f, 0.30f, 0.30f, 0.85f);
        backButtonText.color = isBackSel ? Color.yellow : Color.white;
    }

    private void UpdateRoomCodeDisplay()
    {
        bool hasOnline = false;
        for (int i = 1; i < CoopGameConfig.PlayerCount; i++)
        {
            if (CoopGameConfig.Slots[i].Device == CoopDeviceType.RemoteSteam)
            {
                hasOnline = true;
                break;
            }
        }

        if (hasOnline)
        {
            string code = CoopLobbyManager.ActiveRoomCode;
            if (string.IsNullOrEmpty(code))
            {
                roomCodeValueText.text = "[ GENERANDO CÓDIGO... ]";
                roomCodeValueText.color = Color.yellow;
            }
            else
            {
                roomCodeValueText.text = $"[  {code}  ]";
                roomCodeValueText.color = new Color(0f, 0.95f, 1f, 1f);
            }
            roomCodeCopyTipText.text = "Presiona [ C ] para copiar el código y compartirlo con tus amigos";
        }
        else
        {
            roomCodeValueText.text = "[ NINGUNO (MODO LOCAL) ]";
            roomCodeValueText.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            roomCodeCopyTipText.text = "Configura un jugador en [ 🌐 ONLINE ] para generar una sala de Steam P2P";
        }
    }

    private string GetSkinDisplayName(string skinId)
    {
        foreach (var s in CoopGameConfig.AvailableSkins)
        {
            if (s.Id == skinId) return s.Name;
        }
        return "Por Defecto";
    }
}
