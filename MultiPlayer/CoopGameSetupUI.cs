using System;
using System.Text;
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

    // Barra superior: Coop Toggle, Cantidad de Jugadores y Senda Elemental
    private GameObject coopToggleBox;
    private Outline coopToggleOutline;
    private Text coopToggleLabel;
    private Text coopToggleValue;

    private GameObject playerCountBox;
    private Outline playerCountOutline;
    private Text playerCountLabel;
    private Text playerCountValue;

    private GameObject elementalToggleBox;
    private Outline elementalToggleOutline;
    private Text elementalToggleLabel;
    private Text elementalToggleValue;

    // Tarjetas de jugadores (hasta 4 columnas)
    private struct PlayerCardUI
    {
        public GameObject CardObj;
        public Image CardBg;
        public Outline CardOutline;
        public Text HeaderText;

        // 3 filas por tarjeta: 0 = Nombre, 1 = Color, 2 = Skin
        public GameObject[] RowObjs;
        public Text[] RowLabels;
        public Text[] RowValues;
        public Image SkinPreview;
    }

    private readonly PlayerCardUI[] playerCards = new PlayerCardUI[4];

    // Modal de asignación secuencial de controles al pulsar Comenzar
    private GameObject deviceAssignModal;
    private Text deviceAssignPlayerHeader;
    private Text deviceAssignPromptText;
    private Text deviceAssignStatusText;
    private Text deviceAssignSummaryText;
    private Text deviceAssignCancelText;
    private bool isAssigningDevices = false;
    private int currentAssignSlotIndex = 0;
    private float assignSuccessTimer = 0f;
    private float assignInputCooldown = 0f;

    // Modal de dificultad y configuración de enemigos (aparece tras asignar mandos)
    private GameObject enemyDifficultyModal;
    private Text enemyDiffTitleText;
    private Text enemyDiffSubtitleText;
    private readonly Text[] enemyDiffRowLabels = new Text[5];
    private readonly Text[] enemyDiffRowValues = new Text[5];
    private GameObject enemyDiffStartButton;
    private Outline enemyDiffStartOutline;
    private Text enemyDiffStartText;
    private GameObject enemyDiffBackButton;
    private Outline enemyDiffBackOutline;
    private Text enemyDiffBackText;
    private bool isConfiguringEnemyDifficulty = false;
    private int selectedEnemyDiffRow = 0; // 0 = Cantidad, 1 = Salud, 2 = Resistencia, 3 = Senda Elemental, 4 = Preajuste
    private int enemyDiffBottomAction = 0; // 0 = Iniciar Partida, 1 = Volver
    private bool enemyDiffOnBottomActions = false;

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

    public static bool IsOpen { get; private set; }

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
    private int selectedConfigRow = 0;    // 0 = Nombre, 1 = Color, 2 = Skin
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

        // 5. Barra superior (Modo Coop, Cantidad de Jugadores y Senda Elemental)
        GameObject topBarContainer = CreateUiElement("TopBarContainer", panelObj.transform);
        RectTransform tbRt = topBarContainer.GetComponent<RectTransform>();
        tbRt.anchoredPosition = new Vector2(0f, 325f);
        tbRt.sizeDelta = new Vector2(1280f, 55f);

        // 5.1 Botón Modo Cooperativo
        coopToggleBox = CreateUiElement("CoopToggleBox", topBarContainer.transform);
        RectTransform ctbRt = coopToggleBox.GetComponent<RectTransform>();
        ctbRt.anchoredPosition = new Vector2(-425f, 0f);
        ctbRt.sizeDelta = new Vector2(410f, 50f);
        Image ctbImg = coopToggleBox.AddComponent<Image>();
        ctbImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        coopToggleOutline = coopToggleBox.AddComponent<Outline>();
        coopToggleOutline.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.70f);
        coopToggleOutline.effectDistance = new Vector2(2f, -2f);

        GameObject ctbLblObj = CreateUiElement("Label", coopToggleBox.transform);
        RectTransform ctbLblRt = ctbLblObj.GetComponent<RectTransform>();
        ctbLblRt.anchoredPosition = new Vector2(-75f, 0f);
        ctbLblRt.sizeDelta = new Vector2(230f, 40f);
        coopToggleLabel = ctbLblObj.AddComponent<Text>();
        coopToggleLabel.font = gameFont;
        coopToggleLabel.fontSize = 17;
        coopToggleLabel.alignment = TextAnchor.MiddleLeft;
        coopToggleLabel.color = new Color(0.90f, 0.90f, 0.90f, 1f);
        coopToggleLabel.text = "Modo de Juego:";

        GameObject ctbValObj = CreateUiElement("Value", coopToggleBox.transform);
        RectTransform ctbValRt = ctbValObj.GetComponent<RectTransform>();
        ctbValRt.anchoredPosition = new Vector2(90f, 0f);
        ctbValRt.sizeDelta = new Vector2(210f, 40f);
        coopToggleValue = ctbValObj.AddComponent<Text>();
        coopToggleValue.font = gameFont;
        coopToggleValue.fontSize = 17;
        coopToggleValue.alignment = TextAnchor.MiddleRight;

        // 5.2 Botón Cantidad de Jugadores
        playerCountBox = CreateUiElement("PlayerCountBox", topBarContainer.transform);
        RectTransform pcbRt = playerCountBox.GetComponent<RectTransform>();
        pcbRt.anchoredPosition = new Vector2(0f, 0f);
        pcbRt.sizeDelta = new Vector2(410f, 50f);
        Image pcbImg = playerCountBox.AddComponent<Image>();
        pcbImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        playerCountOutline = playerCountBox.AddComponent<Outline>();
        playerCountOutline.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.70f);
        playerCountOutline.effectDistance = new Vector2(2f, -2f);

        GameObject pcbLblObj = CreateUiElement("Label", playerCountBox.transform);
        RectTransform pcbLblRt = pcbLblObj.GetComponent<RectTransform>();
        pcbLblRt.anchoredPosition = new Vector2(-75f, 0f);
        pcbLblRt.sizeDelta = new Vector2(230f, 40f);
        playerCountLabel = pcbLblObj.AddComponent<Text>();
        playerCountLabel.font = gameFont;
        playerCountLabel.fontSize = 17;
        playerCountLabel.alignment = TextAnchor.MiddleLeft;
        playerCountLabel.color = new Color(0.90f, 0.90f, 0.90f, 1f);
        playerCountLabel.text = "Jugadores:";

        GameObject pcbValObj = CreateUiElement("Value", playerCountBox.transform);
        RectTransform pcbValRt = pcbValObj.GetComponent<RectTransform>();
        pcbValRt.anchoredPosition = new Vector2(90f, 0f);
        pcbValRt.sizeDelta = new Vector2(210f, 40f);
        playerCountValue = pcbValObj.AddComponent<Text>();
        playerCountValue.font = gameFont;
        playerCountValue.fontSize = 17;
        playerCountValue.alignment = TextAnchor.MiddleRight;

        // 5.3 Botón Mod Veneno y Sangrado / Senda Elemental
        elementalToggleBox = CreateUiElement("ElementalToggleBox", topBarContainer.transform);
        RectTransform etbRt = elementalToggleBox.GetComponent<RectTransform>();
        etbRt.anchoredPosition = new Vector2(425f, 0f);
        etbRt.sizeDelta = new Vector2(410f, 50f);
        Image etbImg = elementalToggleBox.AddComponent<Image>();
        etbImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        elementalToggleOutline = elementalToggleBox.AddComponent<Outline>();
        elementalToggleOutline.effectColor = new Color(0.40f, 0.35f, 0.25f, 0.70f);
        elementalToggleOutline.effectDistance = new Vector2(2f, -2f);

        GameObject etbLblObj = CreateUiElement("Label", elementalToggleBox.transform);
        RectTransform etbLblRt = etbLblObj.GetComponent<RectTransform>();
        etbLblRt.anchoredPosition = new Vector2(-75f, 0f);
        etbLblRt.sizeDelta = new Vector2(230f, 40f);
        elementalToggleLabel = etbLblObj.AddComponent<Text>();
        elementalToggleLabel.font = gameFont;
        elementalToggleLabel.fontSize = 17;
        elementalToggleLabel.alignment = TextAnchor.MiddleLeft;
        elementalToggleLabel.color = new Color(0.90f, 0.90f, 0.90f, 1f);
        elementalToggleLabel.text = "Senda Elemental:";

        GameObject etbValObj = CreateUiElement("Value", elementalToggleBox.transform);
        RectTransform etbValRt = etbValObj.GetComponent<RectTransform>();
        etbValRt.anchoredPosition = new Vector2(90f, 0f);
        etbValRt.sizeDelta = new Vector2(210f, 40f);
        elementalToggleValue = etbValObj.AddComponent<Text>();
        elementalToggleValue.font = gameFont;
        elementalToggleValue.fontSize = 17;
        elementalToggleValue.alignment = TextAnchor.MiddleRight;

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

            // 3 Filas de ajustes
            GameObject[] rowObjs = new GameObject[3];
            Text[] rowLbls = new Text[3];
            Text[] rowVals = new Text[3];

            string[] defaultRowLabels = new string[] { "NOMBRE", "COLOR DE TEXTO", "ASPECTO / SKIN" };
            float[] rowY = new float[] { 145f, 75f, 5f };

            for (int r = 0; r < 3; r++)
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
                vRt.anchoredPosition = new Vector2(0f, -14f);
                vRt.sizeDelta = new Vector2(260f, 32f);
                Text vTxt = valObj.AddComponent<Text>();
                vTxt.font = gameFont;
                vTxt.fontSize = 18;
                vTxt.alignment = TextAnchor.MiddleCenter;

                Image skinPrev = null;
                if (r == 2)
                {
                    GameObject spObj = CreateUiElement("SkinPreview", rowObj.transform);
                    RectTransform spRt = spObj.GetComponent<RectTransform>();
                    spRt.anchoredPosition = new Vector2(0f, -115f);
                    spRt.sizeDelta = new Vector2(140f, 140f);
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
                SkinPreview = rowObjs[2].transform.Find("SkinPreview")?.GetComponent<Image>()
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
        footerTipText.text = "Pulsa [ F2 ] para omitir y comenzar directamente sin configurar mandos.";

        // 9. Modal de asignación secuencial de controles
        deviceAssignModal = CreateUiElement("DeviceAssignModal", panelObj.transform);
        RectTransform damRt = deviceAssignModal.GetComponent<RectTransform>();
        damRt.anchorMin = Vector2.zero;
        damRt.anchorMax = Vector2.one;
        damRt.sizeDelta = Vector2.zero;
        Image damBg = deviceAssignModal.AddComponent<Image>();
        damBg.color = new Color(0.02f, 0.02f, 0.04f, 0.96f);

        GameObject modalBox = CreateUiElement("ModalBox", deviceAssignModal.transform);
        RectTransform mbRt = modalBox.GetComponent<RectTransform>();
        mbRt.sizeDelta = new Vector2(780f, 440f);
        mbRt.anchoredPosition = Vector2.zero;
        Image mbBg = modalBox.AddComponent<Image>();
        mbBg.color = new Color(0.08f, 0.08f, 0.11f, 0.98f);
        Outline mbOut = modalBox.AddComponent<Outline>();
        mbOut.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.90f);
        mbOut.effectDistance = new Vector2(3f, -3f);

        GameObject daTitleObj = CreateUiElement("Title", modalBox.transform);
        RectTransform daTitleRt = daTitleObj.GetComponent<RectTransform>();
        daTitleRt.anchoredPosition = new Vector2(0f, 160f);
        daTitleRt.sizeDelta = new Vector2(720f, 40f);
        Text daTitleTxt = daTitleObj.AddComponent<Text>();
        daTitleTxt.font = gameFont;
        daTitleTxt.fontSize = 20;
        daTitleTxt.alignment = TextAnchor.MiddleCenter;
        daTitleTxt.color = new Color(0.85f, 0.70f, 0.30f, 1f);
        daTitleTxt.text = "ASIGNACIÓN DE CONTROL";

        GameObject daHdrObj = CreateUiElement("PlayerHeader", modalBox.transform);
        RectTransform daHdrRt = daHdrObj.GetComponent<RectTransform>();
        daHdrRt.anchoredPosition = new Vector2(0f, 95f);
        daHdrRt.sizeDelta = new Vector2(720f, 50f);
        deviceAssignPlayerHeader = daHdrObj.AddComponent<Text>();
        deviceAssignPlayerHeader.font = gameFont;
        deviceAssignPlayerHeader.fontSize = 28;
        deviceAssignPlayerHeader.alignment = TextAnchor.MiddleCenter;

        GameObject daPrmObj = CreateUiElement("Prompt", modalBox.transform);
        RectTransform daPrmRt = daPrmObj.GetComponent<RectTransform>();
        daPrmRt.anchoredPosition = new Vector2(0f, 30f);
        daPrmRt.sizeDelta = new Vector2(720f, 45f);
        deviceAssignPromptText = daPrmObj.AddComponent<Text>();
        deviceAssignPromptText.font = gameFont;
        deviceAssignPromptText.fontSize = 17;
        deviceAssignPromptText.alignment = TextAnchor.MiddleCenter;
        deviceAssignPromptText.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        deviceAssignPromptText.text = "Presiona el botón del control que vas a usar o teclado.";

        GameObject daStsObj = CreateUiElement("Status", modalBox.transform);
        RectTransform daStsRt = daStsObj.GetComponent<RectTransform>();
        daStsRt.anchoredPosition = new Vector2(0f, -40f);
        daStsRt.sizeDelta = new Vector2(720f, 40f);
        deviceAssignStatusText = daStsObj.AddComponent<Text>();
        deviceAssignStatusText.font = gameFont;
        deviceAssignStatusText.fontSize = 19;
        deviceAssignStatusText.alignment = TextAnchor.MiddleCenter;

        GameObject daSumObj = CreateUiElement("Summary", modalBox.transform);
        RectTransform daSumRt = daSumObj.GetComponent<RectTransform>();
        daSumRt.anchoredPosition = new Vector2(0f, -110f);
        daSumRt.sizeDelta = new Vector2(720f, 35f);
        deviceAssignSummaryText = daSumObj.AddComponent<Text>();
        deviceAssignSummaryText.font = gameFont;
        deviceAssignSummaryText.fontSize = 14;
        deviceAssignSummaryText.alignment = TextAnchor.MiddleCenter;
        deviceAssignSummaryText.color = new Color(0.65f, 0.65f, 0.65f, 1f);

        GameObject daCnlObj = CreateUiElement("CancelHint", modalBox.transform);
        RectTransform daCnlRt = daCnlObj.GetComponent<RectTransform>();
        daCnlRt.anchoredPosition = new Vector2(0f, -165f);
        daCnlRt.sizeDelta = new Vector2(720f, 25f);
        deviceAssignCancelText = daCnlObj.AddComponent<Text>();
        deviceAssignCancelText.font = gameFont;
        deviceAssignCancelText.fontSize = 13;
        deviceAssignCancelText.alignment = TextAnchor.MiddleCenter;
        deviceAssignCancelText.color = new Color(0.55f, 0.55f, 0.55f, 1f);
        deviceAssignCancelText.text = "[ ESC / B ]: Volver al menú   •   [ F2 ]: Pasar directamente";

        deviceAssignModal.SetActive(false);

        // 10. Modal de dificultad y configuración de enemigos
        enemyDifficultyModal = CreateUiElement("EnemyDifficultyModal", panelObj.transform);
        RectTransform edmRt = enemyDifficultyModal.GetComponent<RectTransform>();
        edmRt.anchorMin = Vector2.zero;
        edmRt.anchorMax = Vector2.one;
        edmRt.sizeDelta = Vector2.zero;
        Image edmBg = enemyDifficultyModal.AddComponent<Image>();
        edmBg.color = new Color(0.02f, 0.02f, 0.04f, 0.96f);

        GameObject edBox = CreateUiElement("ModalBox", enemyDifficultyModal.transform);
        RectTransform edbRt = edBox.GetComponent<RectTransform>();
        edbRt.sizeDelta = new Vector2(920f, 580f);
        edbRt.anchoredPosition = Vector2.zero;
        Image edbBg = edBox.AddComponent<Image>();
        edbBg.color = new Color(0.08f, 0.08f, 0.11f, 0.98f);
        Outline edbOut = edBox.AddComponent<Outline>();
        edbOut.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.90f);
        edbOut.effectDistance = new Vector2(3f, -3f);

        GameObject edTitleObj = CreateUiElement("Title", edBox.transform);
        RectTransform edTitleRt = edTitleObj.GetComponent<RectTransform>();
        edTitleRt.anchoredPosition = new Vector2(0f, 235f);
        edTitleRt.sizeDelta = new Vector2(860f, 38f);
        enemyDiffTitleText = edTitleObj.AddComponent<Text>();
        enemyDiffTitleText.font = gameFont;
        enemyDiffTitleText.fontSize = 22;
        enemyDiffTitleText.alignment = TextAnchor.MiddleCenter;
        enemyDiffTitleText.color = new Color(1f, 0.88f, 0.40f, 1f);
        enemyDiffTitleText.text = "CONFIGURACIÓN DE ENEMIGOS Y DIFICULTAD COOPERATIVA";

        GameObject edSubObj = CreateUiElement("Subtitle", edBox.transform);
        RectTransform edSubRt = edSubObj.GetComponent<RectTransform>();
        edSubRt.anchoredPosition = new Vector2(0f, 195f);
        edSubRt.sizeDelta = new Vector2(860f, 30f);
        enemyDiffSubtitleText = edSubObj.AddComponent<Text>();
        enemyDiffSubtitleText.font = gameFont;
        enemyDiffSubtitleText.fontSize = 15;
        enemyDiffSubtitleText.alignment = TextAnchor.MiddleCenter;
        enemyDiffSubtitleText.color = new Color(0.80f, 0.80f, 0.80f, 1f);
        enemyDiffSubtitleText.text = "Escala la cantidad de enemigos, salud, resistencia y senda elemental";

        // 5 Filas de ajustes de dificultad y senda elemental
        string[] diffRowNames = new string[]
        {
            "CANTIDAD DE ENEMIGOS POR SALA",
            "SALUD / VIDA DE ENEMIGOS Y JEFES",
            "RESISTENCIA DE JEFES A VENENO Y SANGRADO",
            "SENDA ELEMENTAL (VENENO / SANGRADO)",
            "PREAJUSTE DE DIFICULTAD"
        };
        float[] diffRowY = new float[] { 135f, 75f, 15f, -45f, -105f };

        for (int r = 0; r < 5; r++)
        {
            GameObject rObj = CreateUiElement($"DiffRow_{r}", edBox.transform);
            RectTransform rRt = rObj.GetComponent<RectTransform>();
            rRt.anchoredPosition = new Vector2(0f, diffRowY[r]);
            rRt.sizeDelta = new Vector2(840f, 56f);

            GameObject lblObj = CreateUiElement("Label", rObj.transform);
            RectTransform lRt = lblObj.GetComponent<RectTransform>();
            lRt.anchoredPosition = new Vector2(0f, 13f);
            lRt.sizeDelta = new Vector2(820f, 24f);
            Text lTxt = lblObj.AddComponent<Text>();
            lTxt.font = gameFont;
            lTxt.fontSize = 15;
            lTxt.alignment = TextAnchor.MiddleCenter;
            lTxt.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            lTxt.text = diffRowNames[r];

            GameObject valObj = CreateUiElement("Value", rObj.transform);
            RectTransform vRt = valObj.GetComponent<RectTransform>();
            vRt.anchoredPosition = new Vector2(0f, -13f);
            vRt.sizeDelta = new Vector2(820f, 30f);
            Text vTxt = valObj.AddComponent<Text>();
            vTxt.font = gameFont;
            vTxt.fontSize = 18;
            vTxt.alignment = TextAnchor.MiddleCenter;

            enemyDiffRowLabels[r] = lTxt;
            enemyDiffRowValues[r] = vTxt;
        }

        // Botones inferiores del modal
        GameObject edBottomBar = CreateUiElement("BottomBar", edBox.transform);
        RectTransform edbBarRt = edBottomBar.GetComponent<RectTransform>();
        edbBarRt.anchoredPosition = new Vector2(0f, -185f);
        edbBarRt.sizeDelta = new Vector2(840f, 50f);

        // Botón Iniciar Partida
        enemyDiffStartButton = CreateUiElement("StartButton", edBottomBar.transform);
        RectTransform edsbRt = enemyDiffStartButton.GetComponent<RectTransform>();
        edsbRt.anchoredPosition = new Vector2(-190f, 0f);
        edsbRt.sizeDelta = new Vector2(360f, 48f);
        Image edsbImg = enemyDiffStartButton.AddComponent<Image>();
        edsbImg.color = new Color(0.10f, 0.16f, 0.12f, 0.95f);
        enemyDiffStartOutline = enemyDiffStartButton.AddComponent<Outline>();
        enemyDiffStartOutline.effectColor = new Color(0.20f, 0.60f, 0.30f, 0.70f);
        enemyDiffStartOutline.effectDistance = new Vector2(2f, -2f);

        GameObject edsbTxtObj = CreateUiElement("Text", enemyDiffStartButton.transform);
        RectTransform edsbTxtRt = edsbTxtObj.GetComponent<RectTransform>();
        edsbTxtRt.sizeDelta = new Vector2(340f, 40f);
        edsbTxtRt.anchoredPosition = Vector2.zero;
        enemyDiffStartText = edsbTxtObj.AddComponent<Text>();
        enemyDiffStartText.font = gameFont;
        enemyDiffStartText.fontSize = 19;
        enemyDiffStartText.alignment = TextAnchor.MiddleCenter;
        enemyDiffStartText.color = new Color(0.40f, 1f, 0.50f, 1f);
        enemyDiffStartText.text = "► ¡INICIAR PARTIDA!";

        // Botón Volver a Mandos
        enemyDiffBackButton = CreateUiElement("BackButton", edBottomBar.transform);
        RectTransform edbbRt = enemyDiffBackButton.GetComponent<RectTransform>();
        edbbRt.anchoredPosition = new Vector2(190f, 0f);
        edbbRt.sizeDelta = new Vector2(360f, 48f);
        Image edbbImg = enemyDiffBackButton.AddComponent<Image>();
        edbbImg.color = new Color(0.16f, 0.10f, 0.10f, 0.95f);
        enemyDiffBackOutline = enemyDiffBackButton.AddComponent<Outline>();
        enemyDiffBackOutline.effectColor = new Color(0.60f, 0.20f, 0.20f, 0.70f);
        enemyDiffBackOutline.effectDistance = new Vector2(2f, -2f);

        GameObject edbbTxtObj = CreateUiElement("Text", enemyDiffBackButton.transform);
        RectTransform edbbTxtRt = edbbTxtObj.GetComponent<RectTransform>();
        edbbTxtRt.sizeDelta = new Vector2(340f, 40f);
        edbbTxtRt.anchoredPosition = Vector2.zero;
        enemyDiffBackText = edbbTxtObj.AddComponent<Text>();
        enemyDiffBackText.font = gameFont;
        enemyDiffBackText.fontSize = 19;
        enemyDiffBackText.alignment = TextAnchor.MiddleCenter;
        enemyDiffBackText.color = new Color(1f, 0.45f, 0.45f, 1f);
        enemyDiffBackText.text = "VOLVER A MANDOS";

        GameObject edTipObj = CreateUiElement("Tip", edBox.transform);
        RectTransform edTipRt = edTipObj.GetComponent<RectTransform>();
        edTipRt.anchoredPosition = new Vector2(0f, -235f);
        edTipRt.sizeDelta = new Vector2(860f, 24f);
        Text edTipTxt = edTipObj.AddComponent<Text>();
        edTipTxt.font = gameFont;
        edTipTxt.fontSize = 13;
        edTipTxt.alignment = TextAnchor.MiddleCenter;
        edTipTxt.color = new Color(0.70f, 0.70f, 0.70f, 0.90f);
        edTipTxt.text = "[▲ / ▼] Seleccionar ajuste  |  [◄ / ► / ATAQUE] Cambiar valor  |  [ESC]: Volver";

        enemyDifficultyModal.SetActive(false);

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
        isAssigningDevices = false;
        isConfiguringEnemyDifficulty = false;
        if (deviceAssignModal != null) deviceAssignModal.SetActive(false);
        if (enemyDifficultyModal != null) enemyDifficultyModal.SetActive(false);

        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        canvasObj.SetActive(true);
        RefreshDisplay();
    }

    private void Close()
    {
        IsOpen = false;
        isAssigningDevices = false;
        isConfiguringEnemyDifficulty = false;
        if (deviceAssignModal != null) deviceAssignModal.SetActive(false);
        if (enemyDifficultyModal != null) enemyDifficultyModal.SetActive(false);
        if (canvasObj != null)
        {
            canvasObj.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsOpen) return;

        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        // Atajo global F2: pasar directamente la asignación o iniciar
        if (Input.GetKeyDown(KeyCode.F2))
        {
            if (isConfiguringEnemyDifficulty)
            {
                FinishDifficultyAndStartGame();
            }
            else
            {
                BypassDeviceAssignment();
            }
            return;
        }

        // Modal de asignación secuencial de controles para cada jugador
        if (isAssigningDevices)
        {
            UpdateDeviceAssignInput();
            return;
        }

        if (isConfiguringEnemyDifficulty)
        {
            UpdateEnemyDifficultyInput();
            return;
        }

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




        // 2. Navegación direccional (Izquierda / Derecha / Arriba / Abajo)
        bool navigated = false;
        if (inputCooldown <= 0f)
        {
            navigated = HandleNavigationInput();
        }

        // 3. Modificación de la configuración mediante BOTÓN DE ATAQUE O SALTO ('X' o 'A' en mando, K/J/X o Espacio/Enter en teclado)
        if (!navigated && inputCooldown <= 0f && IsAttackOrJumpButtonPressed())
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

    private bool HandleNavigationInput()
    {
        bool left = Input.GetKeyDown(KeyCode.LeftArrow) || Player2Pad.Left;
        bool right = Input.GetKeyDown(KeyCode.RightArrow) || Player2Pad.Right;
        bool up = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Player2Pad.Up;
        bool down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) || Player2Pad.Down;

        if (Input.GetKeyDown(KeyCode.A)) left = true;
        if (Input.GetKeyDown(KeyCode.D)) right = true;

        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;

        if (left)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    topBarIndex = (topBarIndex == 0) ? 2 : topBarIndex - 1;
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
            return true;
        }
        else if (right)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    topBarIndex = (topBarIndex == 2) ? 0 : topBarIndex + 1;
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
            return true;
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
                        topBarIndex = Mathf.Clamp(selectedPlayerIndex * 2 / Mathf.Max(1, activePlayers - 1), 0, 2);
                    }
                    break;
                case MenuZone.BottomActions:
                    currentZone = MenuZone.PlayerCards;
                    selectedConfigRow = 2; // Fila de Skin
                    break;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            RefreshDisplay();
            return true;
        }
        else if (down)
        {
            switch (currentZone)
            {
                case MenuZone.TopGlobal:
                    currentZone = MenuZone.PlayerCards;
                    selectedConfigRow = 0;
                    selectedPlayerIndex = Mathf.Clamp(topBarIndex * (activePlayers - 1) / 2, 0, activePlayers - 1);
                    break;
                case MenuZone.PlayerCards:
                    if (selectedConfigRow < 2)
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
            return true;
        }
        return false;
    }

    /// <summary>
    /// Comprueba si se ha pulsado el botón de ataque o salto/confirmación
    /// ('X' o 'A' en mando, K/J/X/Z o Espacio/Enter en teclado).
    /// </summary>
    private bool IsAttackOrJumpButtonPressed()
    {
        // 1. Teclas de ataque en teclado (K, J, X, Z)
        if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.J) ||
            Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Z))
        {
            return true;
        }

        // 2. Teclas de salto / confirmación en teclado (Espacio, Enter, KeypadEnter)
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            return true;
        }

        // 3. Botones de ataque ('X') y salto / selección ('A') en mando P2/General
        // NOTA CRÍTICA: Solo ButtonDown (edge), NUNCA ButtonHeld
        if (Player2Pad.AttackDown || Player2Pad.SelectDown)
        {
            return true;
        }

        // 4. Ataque o Salto de P1 en Rewired (si está listo)
        try
        {
            if (Rewired.ReInput.isReady && Rewired.ReInput.players != null && Rewired.ReInput.players.playerCount > 0)
            {
                var p0 = Rewired.ReInput.players.GetPlayer(0);
                if (p0 != null)
                {
                    if (p0.GetButtonDown(5) || p0.GetButtonDown(6) ||
                        p0.GetButtonDown("Attack") || p0.GetButtonDown("Jump"))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        // 5. Detección directa en mandos de Unity (Botón 2 = X, Botón 0 = A)
        for (int j = 1; j <= 4; j++)
        {
            try
            {
                if (Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button2"))) return true;
                if (Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), $"Joystick{j}Button0"))) return true;
            }
            catch { }
        }

        return false;
    }

    private bool IsAttackButtonPressed() => IsAttackOrJumpButtonPressed();

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
                else if (topBarIndex == 1)
                {
                    // Cambiar cantidad de jugadores (2 -> 3 -> 4 -> 2)
                    int count = CoopGameConfig.PlayerCount + 1;
                    if (count > 4) count = 2;
                    CoopGameConfig.PlayerCount = count;
                    CoopGameConfig.Save();
                    PlayToggleSound();
                }
                else if (topBarIndex == 2)
                {
                    // Alternar Senda Elemental (Veneno / Sangrado)
                    CoopGameConfig.EnableElementalUpgrades = !CoopGameConfig.EnableElementalUpgrades;
                    CoopGameConfig.Save();
                    PlayToggleSound();
                }
                break;

            case MenuZone.PlayerCards:
                switch (selectedConfigRow)
                {
                    case 0: // Nombre (Escribir con teclado)
                        isEditingName = true;
                        editingPlayerIndex = selectedPlayerIndex;
                        editingBuffer = CoopGameConfig.Slots[selectedPlayerIndex].Name ?? "";
                        PlayToggleSound();
                        inputCooldown = 0.20f;
                        break;
                    case 1: // Color de Texto
                        CycleColor(selectedPlayerIndex, 1);
                        PlayToggleSound();
                        break;
                    case 2: // Aspecto / Skin
                        CycleSkin(selectedPlayerIndex, 1);
                        PlayToggleSound();
                        break;
                }
                break;

            case MenuZone.BottomActions:
                if (bottomActionIndex == 0)
                {
                    StartDeviceAssignment();
                }
                else
                {
                    CancelAndReturn();
                }
                break;
        }

        RefreshDisplay();
    }

    private void StartDeviceAssignment()
    {
        isAssigningDevices = true;
        currentAssignSlotIndex = 0;
        assignSuccessTimer = 0f;
        assignInputCooldown = 0.35f;

        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        for (int i = 0; i < activePlayers; i++)
        {
            CoopGameConfig.Slots[i].Device = CoopDeviceType.Unassigned;
        }

        if (deviceAssignModal != null)
        {
            deviceAssignModal.SetActive(true);
        }
        UpdateDeviceAssignModalDisplay();
    }

    private void UpdateDeviceAssignModalDisplay()
    {
        if (deviceAssignModal == null || !deviceAssignModal.activeSelf) return;

        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        if (currentAssignSlotIndex >= activePlayers) return;

        string pName = CoopGameConfig.Slots[currentAssignSlotIndex].Name;
        if (string.IsNullOrEmpty(pName)) pName = $"Jugador {currentAssignSlotIndex + 1}";

        int colorIdx = Mathf.Clamp(CoopGameConfig.Slots[currentAssignSlotIndex].ColorIndex, 0, CoopGameConfig.Palette.Length - 1);
        Color32 pColor = CoopGameConfig.Palette[colorIdx].Color;

        if (deviceAssignPlayerHeader != null)
        {
            deviceAssignPlayerHeader.color = pColor;
            deviceAssignPlayerHeader.text = $"[ {pName} ]   •   P{currentAssignSlotIndex + 1}";
        }

        if (deviceAssignPromptText != null)
        {
            deviceAssignPromptText.text = "Presiona el botón del control que vas a usar o teclado.";
        }

        if (deviceAssignStatusText != null)
        {
            deviceAssignStatusText.color = new Color(0.80f, 0.80f, 0.80f, 1f);
            deviceAssignStatusText.text = $"[ Esperando que {pName} (P{currentAssignSlotIndex + 1}) presione un botón... ]";
        }

        if (deviceAssignSummaryText != null)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < currentAssignSlotIndex; i++)
            {
                if (i > 0) sb.Append("   |   ");
                sb.Append($"P{i + 1} ({CoopGameConfig.Slots[i].Name}): {CoopGameConfig.GetDeviceDisplayName(CoopGameConfig.Slots[i].Device)}");
            }
            deviceAssignSummaryText.text = sb.ToString();
        }
    }

    private void UpdateDeviceAssignInput()
    {
        float dt = Time.unscaledDeltaTime;

        if (assignSuccessTimer > 0f)
        {
            assignSuccessTimer -= dt;
            if (assignSuccessTimer <= 0f)
            {
                currentAssignSlotIndex++;
                int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
                if (currentAssignSlotIndex >= activePlayers)
                {
                    FinishDeviceAssignmentAndStart();
                    return;
                }
                else
                {
                    assignInputCooldown = 0.30f;
                    UpdateDeviceAssignModalDisplay();
                }
            }
            return;
        }

        if (assignInputCooldown > 0f)
        {
            assignInputCooldown -= dt;
            return;
        }

        // Pasar directamente sin seleccionar controles con F2
        if (Input.GetKeyDown(KeyCode.F2))
        {
            BypassDeviceAssignment();
            return;
        }

        // Cancelar con Escape o Botón B / Círculo
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            CancelDeviceAssignment();
            return;
        }

        // 1. Detectar mandos (Joystick 1..4)
        int detectedJoy = -1;
        for (int j = 1; j <= 4; j++)
        {
            KeyCode start = KeyCode.Joystick1Button0 + ((j - 1) * 20);
            for (int b = 0; b < 20; b++)
            {
                if (Input.GetKeyDown((KeyCode)((int)start + b)))
                {
                    detectedJoy = j;
                    break;
                }
            }
            if (detectedJoy != -1) break;
        }

        if (detectedJoy != -1)
        {
            CoopDeviceType dev = (CoopDeviceType)(detectedJoy + 1); // Gamepad1..4

            // Verificar si este mando ya fue tomado por un jugador anterior
            bool taken = false;
            string takenBy = "";
            for (int p = 0; p < currentAssignSlotIndex; p++)
            {
                if (CoopGameConfig.Slots[p].Device == dev)
                {
                    taken = true;
                    takenBy = CoopGameConfig.Slots[p].Name;
                    break;
                }
            }

            if (taken)
            {
                if (deviceAssignStatusText != null)
                {
                    deviceAssignStatusText.color = new Color(1f, 0.40f, 0.40f, 1f);
                    deviceAssignStatusText.text = $"¡Mando {detectedJoy} ya asignado a {takenBy}! Usa otro control o teclado.";
                }
                Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                assignInputCooldown = 0.35f;
                return;
            }

            // Asignación de mando confirmada
            CoopGameConfig.AssignDevice(currentAssignSlotIndex, dev);
            Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
            if (deviceAssignStatusText != null)
            {
                deviceAssignStatusText.color = new Color(0.35f, 1f, 0.45f, 1f);
                deviceAssignStatusText.text = $"✔ ¡Mando {detectedJoy} asignado a {CoopGameConfig.Slots[currentAssignSlotIndex].Name}!";
            }
            assignSuccessTimer = 0.50f;
            return;
        }

        // 2. Detectar Teclado si no se pulsó ningún botón de mando
        if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.Escape) && !IsAnyJoystickButtonHeld())
        {
            // Verificar si el teclado ya fue asignado
            bool taken = false;
            string takenBy = "";
            for (int p = 0; p < currentAssignSlotIndex; p++)
            {
                if (CoopGameConfig.Slots[p].Device == CoopDeviceType.Keyboard)
                {
                    taken = true;
                    takenBy = CoopGameConfig.Slots[p].Name;
                    break;
                }
            }

            if (taken)
            {
                if (deviceAssignStatusText != null)
                {
                    deviceAssignStatusText.color = new Color(1f, 0.40f, 0.40f, 1f);
                    deviceAssignStatusText.text = $"¡El teclado ya fue asignado a {takenBy}! Conecta o usa un mando.";
                }
                Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
                assignInputCooldown = 0.35f;
                return;
            }

            // Asignación de teclado confirmada
            CoopGameConfig.AssignDevice(currentAssignSlotIndex, CoopDeviceType.Keyboard);
            Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
            if (deviceAssignStatusText != null)
            {
                deviceAssignStatusText.color = new Color(0.35f, 1f, 0.45f, 1f);
                deviceAssignStatusText.text = $"✔ ¡Teclado asignado a {CoopGameConfig.Slots[currentAssignSlotIndex].Name}!";
            }
            assignSuccessTimer = 0.50f;
            return;
        }
    }

    private void BypassDeviceAssignment()
    {
        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        for (int i = 0; i < activePlayers; i++)
        {
            if (i == 0)
                CoopGameConfig.AssignDevice(i, CoopDeviceType.Keyboard);
            else
                CoopGameConfig.AssignDevice(i, (CoopDeviceType)(i + 1));
        }

        if (deviceAssignModal != null)
        {
            deviceAssignModal.SetActive(false);
        }
        isAssigningDevices = false;

        Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info("[CoopSetupUI] F2 presionado -> Asignación de controles omitida directamente (P1=Teclado, P2..P4=Mandos).", Main.CoopLocal);
        }

        OpenEnemyDifficultyModal();
    }

    private void CancelDeviceAssignment()
    {
        isAssigningDevices = false;
        if (deviceAssignModal != null)
        {
            deviceAssignModal.SetActive(false);
        }
        inputCooldown = 0.25f;
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        RefreshDisplay();
    }

    private void FinishDeviceAssignmentAndStart()
    {
        isAssigningDevices = false;
        if (deviceAssignModal != null)
        {
            deviceAssignModal.SetActive(false);
        }
        OpenEnemyDifficultyModal();
    }

    private void OpenEnemyDifficultyModal()
    {
        isConfiguringEnemyDifficulty = true;
        selectedEnemyDiffRow = 0;
        enemyDiffOnBottomActions = false;
        enemyDiffBottomAction = 0;
        inputCooldown = 0.25f;

        // Establecer valores recomendados por defecto según la cantidad de jugadores si no se han personalizado
        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
        Combat.CoopEnemyScaling.SetDefaultsForPlayerCount(activePlayers);

        if (enemyDifficultyModal != null)
        {
            enemyDifficultyModal.SetActive(true);
        }
        UpdateEnemyDifficultyDisplay();
    }

    private void UpdateEnemyDifficultyDisplay()
    {
        if (enemyDifficultyModal == null || !enemyDifficultyModal.activeSelf) return;

        int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;

        // Fila 0: Multiplicador de Enemigos
        float curEm = Combat.CoopEnemyScaling.ActiveEnemyMultiplier;
        string emLabel = "1.0x (Normal)";
        for (int i = 0; i < Combat.CoopEnemyScaling.EnemyCountMultipliers.Length; i++)
        {
            if (Mathf.Approximately(curEm, Combat.CoopEnemyScaling.EnemyCountMultipliers[i]))
            {
                emLabel = Combat.CoopEnemyScaling.EnemyCountLabels[i];
                break;
            }
        }

        // Fila 1: Multiplicador de Salud
        float curHm = Combat.CoopEnemyScaling.ActiveHealthMultiplier;
        string hmLabel = "1.0x (Normal)";
        for (int i = 0; i < Combat.CoopEnemyScaling.HealthMultipliers.Length; i++)
        {
            if (Mathf.Approximately(curHm, Combat.CoopEnemyScaling.HealthMultipliers[i]))
            {
                hmLabel = Combat.CoopEnemyScaling.HealthLabels[i];
                break;
            }
        }

        // Fila 2: Resistencia de Jefes
        float curBr = Combat.CoopEnemyScaling.ActiveBossResistanceFactor;
        string brLabel = "Media (Recomendada)";
        for (int i = 0; i < Combat.CoopEnemyScaling.BossResistanceLevels.Length; i++)
        {
            if (Mathf.Approximately(curBr, Combat.CoopEnemyScaling.BossResistanceLevels[i]))
            {
                brLabel = Combat.CoopEnemyScaling.BossResistanceLabels[i];
                break;
            }
        }

        // Fila 3: Senda Elemental (Veneno / Sangrado)
        bool elemOn = CoopGameConfig.EnableElementalUpgrades;
        string elemLabel = elemOn ? "ACTIVADO" : "DESACTIVADO";

        // Fila 4: Preajuste
        string presetLabel = $"Recomendado ({activePlayers} Jugadores)";
        if (Mathf.Approximately(curEm, 1.0f) && Mathf.Approximately(curHm, 1.0f) && Mathf.Approximately(curBr, 0.0f))
        {
            presetLabel = "Modo Clásico Vanilla (1.0x / 1.0x / Sin Resistencia)";
        }
        else if (Mathf.Approximately(curEm, 2.5f) && Mathf.Approximately(curHm, 2.5f) && Mathf.Approximately(curBr, 1.70f))
        {
            presetLabel = "Modo Pesadilla (x2.5 Enemigos / x2.5 Vida / Resistencia Extrema)";
        }
        else if (Mathf.Approximately(curEm, 3.0f) && Mathf.Approximately(curHm, 2.0f))
        {
            presetLabel = "Caos Total (x3.0 Enemigos / x2.0 Vida)";
        }

        string[] values = new string[] { emLabel, hmLabel, brLabel, elemLabel, presetLabel };

        for (int r = 0; r < 5; r++)
        {
            bool isSelected = (!enemyDiffOnBottomActions && selectedEnemyDiffRow == r);
            string prefix = isSelected ? "► " : "";
            enemyDiffRowLabels[r].color = isSelected ? new Color(1f, 0.95f, 0.40f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);

            if (r == 3)
            {
                if (isSelected)
                {
                    enemyDiffRowValues[r].color = elemOn ? new Color(0.40f, 1f, 0.50f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
                }
                else
                {
                    enemyDiffRowValues[r].color = elemOn ? new Color(0.35f, 0.85f, 0.45f, 1f) : new Color(0.70f, 0.35f, 0.35f, 1f);
                }
            }
            else
            {
                enemyDiffRowValues[r].color = isSelected ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f);
            }

            enemyDiffRowValues[r].text = prefix + $"[ ◄  {values[r]}  ► ]";
        }

        // Botones inferiores
        bool isStart = enemyDiffOnBottomActions && (enemyDiffBottomAction == 0);
        bool isBack = enemyDiffOnBottomActions && (enemyDiffBottomAction == 1);

        enemyDiffStartOutline.effectColor = isStart ? new Color(0.40f, 1f, 0.50f, 1f) : new Color(0.20f, 0.50f, 0.30f, 0.60f);
        enemyDiffStartOutline.effectDistance = isStart ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        enemyDiffStartText.text = (isStart ? "► " : "") + "¡INICIAR PARTIDA!";

        enemyDiffBackOutline.effectColor = isBack ? new Color(1f, 0.45f, 0.45f, 1f) : new Color(0.50f, 0.20f, 0.20f, 0.60f);
        enemyDiffBackOutline.effectDistance = isBack ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        enemyDiffBackText.text = (isBack ? "► " : "") + "VOLVER A MANDOS";
    }

    private void UpdateEnemyDifficultyInput()
    {
        if (Input.GetKeyDown(KeyCode.F2))
        {
            FinishDifficultyAndStartGame();
            return;
        }

        float dt = Time.unscaledDeltaTime;
        if (inputCooldown > 0f)
        {
            inputCooldown -= dt;
            return;
        }

        bool left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || Player2Pad.Left;
        bool right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Player2Pad.Right;
        bool up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || Player2Pad.Up;
        bool down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || Player2Pad.Down;

        if (up)
        {
            if (enemyDiffOnBottomActions)
            {
                enemyDiffOnBottomActions = false;
                selectedEnemyDiffRow = 4;
            }
            else if (selectedEnemyDiffRow > 0)
            {
                selectedEnemyDiffRow--;
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            UpdateEnemyDifficultyDisplay();
            return;
        }
        else if (down)
        {
            if (!enemyDiffOnBottomActions)
            {
                if (selectedEnemyDiffRow < 4)
                {
                    selectedEnemyDiffRow++;
                }
                else
                {
                    enemyDiffOnBottomActions = true;
                }
            }
            PlayNavSound();
            inputCooldown = 0.16f;
            UpdateEnemyDifficultyDisplay();
            return;
        }

        if (left)
        {
            if (enemyDiffOnBottomActions)
            {
                enemyDiffBottomAction = (enemyDiffBottomAction == 0) ? 1 : 0;
                PlayNavSound();
            }
            else
            {
                CycleEnemyDifficultyValue(-1);
                PlayToggleSound();
            }
            inputCooldown = 0.18f;
            UpdateEnemyDifficultyDisplay();
            return;
        }
        else if (right)
        {
            if (enemyDiffOnBottomActions)
            {
                enemyDiffBottomAction = (enemyDiffBottomAction == 0) ? 1 : 0;
                PlayNavSound();
            }
            else
            {
                CycleEnemyDifficultyValue(1);
                PlayToggleSound();
            }
            inputCooldown = 0.18f;
            UpdateEnemyDifficultyDisplay();
            return;
        }

        if (IsAttackButtonPressed())
        {
            if (enemyDiffOnBottomActions)
            {
                if (enemyDiffBottomAction == 0)
                {
                    FinishDifficultyAndStartGame();
                }
                else
                {
                    ReturnToDeviceAssignment();
                }
            }
            else
            {
                CycleEnemyDifficultyValue(1);
                PlayToggleSound();
                inputCooldown = 0.18f;
                UpdateEnemyDifficultyDisplay();
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Player2Pad.CancelDown)
        {
            ReturnToDeviceAssignment();
        }
    }

    private void CycleEnemyDifficultyValue(int dir)
    {
        switch (selectedEnemyDiffRow)
        {
            case 0: // Cantidad de Enemigos
                int curEmIdx = 0;
                for (int i = 0; i < Combat.CoopEnemyScaling.EnemyCountMultipliers.Length; i++)
                {
                    if (Mathf.Approximately(Combat.CoopEnemyScaling.ActiveEnemyMultiplier, Combat.CoopEnemyScaling.EnemyCountMultipliers[i]))
                    {
                        curEmIdx = i;
                        break;
                    }
                }
                int nextEm = (curEmIdx + dir + Combat.CoopEnemyScaling.EnemyCountMultipliers.Length) % Combat.CoopEnemyScaling.EnemyCountMultipliers.Length;
                Combat.CoopEnemyScaling.ActiveEnemyMultiplier = Combat.CoopEnemyScaling.EnemyCountMultipliers[nextEm];
                break;

            case 1: // Salud de Enemigos
                int curHmIdx = 0;
                for (int i = 0; i < Combat.CoopEnemyScaling.HealthMultipliers.Length; i++)
                {
                    if (Mathf.Approximately(Combat.CoopEnemyScaling.ActiveHealthMultiplier, Combat.CoopEnemyScaling.HealthMultipliers[i]))
                    {
                        curHmIdx = i;
                        break;
                    }
                }
                int nextHm = (curHmIdx + dir + Combat.CoopEnemyScaling.HealthMultipliers.Length) % Combat.CoopEnemyScaling.HealthMultipliers.Length;
                Combat.CoopEnemyScaling.ActiveHealthMultiplier = Combat.CoopEnemyScaling.HealthMultipliers[nextHm];
                break;

            case 2: // Resistencia de Jefes
                int curBrIdx = 0;
                for (int i = 0; i < Combat.CoopEnemyScaling.BossResistanceLevels.Length; i++)
                {
                    if (Mathf.Approximately(Combat.CoopEnemyScaling.ActiveBossResistanceFactor, Combat.CoopEnemyScaling.BossResistanceLevels[i]))
                    {
                        curBrIdx = i;
                        break;
                    }
                }
                int nextBr = (curBrIdx + dir + Combat.CoopEnemyScaling.BossResistanceLevels.Length) % Combat.CoopEnemyScaling.BossResistanceLevels.Length;
                Combat.CoopEnemyScaling.ActiveBossResistanceFactor = Combat.CoopEnemyScaling.BossResistanceLevels[nextBr];
                break;

            case 3: // Senda Elemental (Veneno / Sangrado)
                CoopGameConfig.EnableElementalUpgrades = !CoopGameConfig.EnableElementalUpgrades;
                CoopGameConfig.Save();
                break;

            case 4: // Preajuste
                int activePlayers = CoopGameConfig.IsCoopEnabled ? Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4) : 1;
                if (dir > 0)
                {
                    if (Mathf.Approximately(Combat.CoopEnemyScaling.ActiveEnemyMultiplier, 1.0f))
                    {
                        // Pasar a recomendado
                        Combat.CoopEnemyScaling.SetDefaultsForPlayerCount(activePlayers);
                    }
                    else if (Mathf.Approximately(Combat.CoopEnemyScaling.ActiveEnemyMultiplier, 2.5f))
                    {
                        // Caos total
                        Combat.CoopEnemyScaling.ActiveEnemyMultiplier = 3.0f;
                        Combat.CoopEnemyScaling.ActiveHealthMultiplier = 2.0f;
                        Combat.CoopEnemyScaling.ActiveBossResistanceFactor = 1.0f;
                    }
                    else
                    {
                        // Pesadilla
                        Combat.CoopEnemyScaling.ActiveEnemyMultiplier = 2.5f;
                        Combat.CoopEnemyScaling.ActiveHealthMultiplier = 2.5f;
                        Combat.CoopEnemyScaling.ActiveBossResistanceFactor = 1.70f;
                    }
                }
                else
                {
                    Combat.CoopEnemyScaling.ActiveEnemyMultiplier = 1.0f;
                    Combat.CoopEnemyScaling.ActiveHealthMultiplier = 1.0f;
                    Combat.CoopEnemyScaling.ActiveBossResistanceFactor = 0.0f;
                }
                break;
        }
    }

    private void ReturnToDeviceAssignment()
    {
        isConfiguringEnemyDifficulty = false;
        if (enemyDifficultyModal != null)
        {
            enemyDifficultyModal.SetActive(false);
        }
        Core.Audio?.PlayOneShot("event:/SFX/UI/ChangeTab");
        StartDeviceAssignment();
    }

    private void FinishDifficultyAndStartGame()
    {
        isConfiguringEnemyDifficulty = false;
        if (enemyDifficultyModal != null)
        {
            enemyDifficultyModal.SetActive(false);
        }
        ConfirmAndStart();
    }

    private static bool IsAnyJoystickButtonHeld()
    {
        for (int j = 1; j <= 4; j++)
        {
            KeyCode start = KeyCode.Joystick1Button0 + ((j - 1) * 20);
            for (int b = 0; b < 20; b++)
            {
                if (Input.GetKey((KeyCode)((int)start + b))) return true;
            }
        }
        return false;
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
        bool isTop2 = isTopZone && (topBarIndex == 2);

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

        bool elemOn = CoopGameConfig.EnableElementalUpgrades;
        elementalToggleOutline.effectColor = isTop2 ? new Color(1f, 0.88f, 0.35f, 1f) : new Color(0.40f, 0.35f, 0.25f, 0.60f);
        elementalToggleOutline.effectDistance = isTop2 ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
        elementalToggleLabel.color = isTop2 ? new Color(1f, 0.95f, 0.50f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
        elementalToggleValue.color = elemOn ? new Color(0.40f, 1f, 0.50f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
        elementalToggleValue.text = (isTop2 ? "► " : "") + (elemOn ? "[ ACTIVADO ]" : "[ DESACTIVADO ]");

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
                cRt.sizeDelta = new Vector2(cardWidth, 490f);

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
                for (int r = 0; r < 3; r++)
                {
                    bool isSelectedRow = (isSelectedCard && selectedConfigRow == r);
                    Text lTxt = card.RowLabels[r];
                    Text vTxt = card.RowValues[r];

                    string prefix = isSelectedRow ? "► " : "";
                    lTxt.color = isSelectedRow ? new Color(1f, 0.95f, 0.40f, 1f) : new Color(0.70f, 0.70f, 0.70f, 1f);

                    switch (r)
                    {
                        case 0: // Nombre
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

                        case 1: // Color
                            vTxt.color = playerColor;
                            vTxt.text = prefix + $"■ {CoopGameConfig.Palette[colorIdx].Name}";
                            break;

                        case 2: // Aspecto / Skin
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
