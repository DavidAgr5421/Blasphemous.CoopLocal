using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Managers;
using Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

public class CoopPlayerPauseMenu : MonoBehaviour
{
    private static CoopPlayerPauseMenu _instance;
    public static bool IsOpen { get; private set; }
    public static int ActiveSlotIndex { get; private set; } = -1;

    private GameObject rootCanvasObj;
    private CanvasGroup canvasGroup;
    private Text headerTitleText;
    private Text headerDeviceText;
    private Text legendText;

    // Vista Principal
    private GameObject mainViewObj;
    private List<Text> mainOptionTexts = new List<Text>();
    private int selectedMainIndex = 0;
    private readonly string[] MainOptions = new string[]
    {
        "Reanudar Partida",
        "Ver y Configurar Controles",
        "Restablecer Controles por Defecto",
        "Salir al Menú Principal"
    };

    // Vista de Configuración de Controles
    private GameObject controlsViewObj;
    private List<GameObject> controlRows = new List<GameObject>();
    private List<Text> controlActionTexts = new List<Text>();
    private List<Text> controlValueTexts = new List<Text>();
    private int selectedControlIndex = 0;
    private bool isInControlsView = false;
    private bool isListeningForInput = false;
    private float listenCooldownTimer = 0f;

    private readonly CoopAction[] ConfigurableActions = new CoopAction[]
    {
        CoopAction.Attack,
        CoopAction.Jump,
        CoopAction.Dash,
        CoopAction.Parry,
        CoopAction.Heal,
        CoopAction.Interact,
        CoopAction.Prayer,
        CoopAction.Pause,
        CoopAction.Menu,
        CoopAction.Up,
        CoopAction.Down,
        CoopAction.Left,
        CoopAction.Right
    };

    private Font gameFont;

    public static void Initialize()
    {
        if (_instance == null)
        {
            var go = new GameObject("CoopPlayerPauseMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<CoopPlayerPauseMenu>();
        }
    }

    public static void OpenMenu(int slotIndex)
    {
        Initialize();
        _instance.Show(slotIndex);
    }

    public static void CloseMenu()
    {
        if (_instance != null && IsOpen)
        {
            _instance.Hide();
        }
    }

    private void Awake()
    {
        _instance = this;
        CreateUI();
    }

    private void CreateUI()
    {
        if (rootCanvasObj != null) return;

        gameFont = CoopGameSetupUI.GetGameFont();

        rootCanvasObj = new GameObject("CoopPauseCanvas");
        rootCanvasObj.transform.SetParent(transform, false);
        Canvas canvas = rootCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = rootCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        rootCanvasObj.AddComponent<GraphicRaycaster>();
        canvasGroup = rootCanvasObj.AddComponent<CanvasGroup>();

        // Fondo oscuro semitransparente total
        GameObject darkOverlay = new GameObject("DarkOverlay");
        darkOverlay.transform.SetParent(rootCanvasObj.transform, false);
        RectTransform doRt = darkOverlay.AddComponent<RectTransform>();
        doRt.anchorMin = Vector2.zero;
        doRt.anchorMax = Vector2.one;
        doRt.sizeDelta = Vector2.zero;
        Image doImg = darkOverlay.AddComponent<Image>();
        doImg.color = new Color(0f, 0f, 0f, 0.85f);

        // Marco central
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(rootCanvasObj.transform, false);
        RectTransform panelRt = panelObj.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(900f, 650f);

        Image pnlImg = panelObj.AddComponent<Image>();
        pnlImg.color = new Color(0.08f, 0.08f, 0.09f, 0.98f);
        Outline pnlOutline = panelObj.AddComponent<Outline>();
        pnlOutline.effectColor = new Color(0.70f, 0.58f, 0.32f, 0.90f);
        pnlOutline.effectDistance = new Vector2(3f, -3f);

        // Título del Jugador (Cabecera)
        GameObject hdrObj = new GameObject("HeaderTitle");
        hdrObj.transform.SetParent(panelObj.transform, false);
        RectTransform hdrRt = hdrObj.AddComponent<RectTransform>();
        hdrRt.anchoredPosition = new Vector2(0f, 275f);
        hdrRt.sizeDelta = new Vector2(860f, 45f);
        headerTitleText = hdrObj.AddComponent<Text>();
        headerTitleText.font = gameFont;
        headerTitleText.fontSize = 28;
        headerTitleText.alignment = TextAnchor.MiddleCenter;
        headerTitleText.color = new Color(1f, 0.92f, 0.65f, 1f);
        Outline hdrOutline = hdrObj.AddComponent<Outline>();
        hdrOutline.effectColor = Color.black;
        hdrOutline.effectDistance = new Vector2(2f, -2f);

        // Subtítulo con información del dispositivo
        GameObject devObj = new GameObject("HeaderDevice");
        devObj.transform.SetParent(panelObj.transform, false);
        RectTransform devRt = devObj.AddComponent<RectTransform>();
        devRt.anchoredPosition = new Vector2(0f, 230f);
        devRt.sizeDelta = new Vector2(860f, 30f);
        headerDeviceText = devObj.AddComponent<Text>();
        headerDeviceText.font = gameFont;
        headerDeviceText.fontSize = 18;
        headerDeviceText.alignment = TextAnchor.MiddleCenter;
        headerDeviceText.color = new Color(0.80f, 0.80f, 0.80f, 1f);

        // Línea divisoria dorada
        GameObject lineObj = new GameObject("Divider");
        lineObj.transform.SetParent(panelObj.transform, false);
        RectTransform lineRt = lineObj.AddComponent<RectTransform>();
        lineRt.anchoredPosition = new Vector2(0f, 205f);
        lineRt.sizeDelta = new Vector2(820f, 2f);
        Image lineImg = lineObj.AddComponent<Image>();
        lineImg.color = new Color(0.60f, 0.50f, 0.28f, 0.80f);

        // --- Vista 1: Opciones Principales ---
        mainViewObj = new GameObject("MainView");
        mainViewObj.transform.SetParent(panelObj.transform, false);
        RectTransform mvRt = mainViewObj.AddComponent<RectTransform>();
        mvRt.anchoredPosition = new Vector2(0f, -20f);
        mvRt.sizeDelta = new Vector2(800f, 380f);

        for (int i = 0; i < MainOptions.Length; i++)
        {
            GameObject optObj = new GameObject($"Option_{i}");
            optObj.transform.SetParent(mainViewObj.transform, false);
            RectTransform optRt = optObj.AddComponent<RectTransform>();
            optRt.anchoredPosition = new Vector2(0f, 120f - (i * 70f));
            optRt.sizeDelta = new Vector2(700f, 50f);

            Text optText = optObj.AddComponent<Text>();
            optText.font = gameFont;
            optText.fontSize = 24;
            optText.alignment = TextAnchor.MiddleCenter;
            optText.color = Color.white;
            Outline optOutline = optObj.AddComponent<Outline>();
            optOutline.effectColor = Color.black;
            optOutline.effectDistance = new Vector2(1.5f, -1.5f);

            mainOptionTexts.Add(optText);
        }

        // --- Vista 2: Configuración de Controles ---
        controlsViewObj = new GameObject("ControlsView");
        controlsViewObj.transform.SetParent(panelObj.transform, false);
        RectTransform cvRt = controlsViewObj.AddComponent<RectTransform>();
        cvRt.anchoredPosition = new Vector2(0f, -30f);
        cvRt.sizeDelta = new Vector2(840f, 410f);

        // 13 filas de controles
        float rowStartY = 175f;
        float rowSpacing = 28f;
        for (int i = 0; i < ConfigurableActions.Length; i++)
        {
            GameObject rowObj = new GameObject($"ControlRow_{i}");
            rowObj.transform.SetParent(controlsViewObj.transform, false);
            RectTransform rowRt = rowObj.AddComponent<RectTransform>();
            rowRt.anchoredPosition = new Vector2(0f, rowStartY - (i * rowSpacing));
            rowRt.sizeDelta = new Vector2(800f, 28f);

            // Nombre de la Acción
            GameObject actObj = new GameObject("ActionName");
            actObj.transform.SetParent(rowObj.transform, false);
            RectTransform actRt = actObj.AddComponent<RectTransform>();
            actRt.anchoredPosition = new Vector2(-180f, 0f);
            actRt.sizeDelta = new Vector2(400f, 28f);
            Text actText = actObj.AddComponent<Text>();
            actText.font = gameFont;
            actText.fontSize = 17;
            actText.alignment = TextAnchor.MiddleLeft;
            actText.color = Color.white;
            controlActionTexts.Add(actText);

            // Valor actual asignado
            GameObject valObj = new GameObject("BindingValue");
            valObj.transform.SetParent(rowObj.transform, false);
            RectTransform valRt = valObj.AddComponent<RectTransform>();
            valRt.anchoredPosition = new Vector2(200f, 0f);
            valRt.sizeDelta = new Vector2(360f, 28f);
            Text valText = valObj.AddComponent<Text>();
            valText.font = gameFont;
            valText.fontSize = 17;
            valText.alignment = TextAnchor.MiddleRight;
            valText.color = new Color(1f, 0.88f, 0.45f, 1f);
            controlValueTexts.Add(valText);

            controlRows.Add(rowObj);
        }

        // Barra inferior de leyenda / controles del menú
        GameObject lgdObj = new GameObject("LegendText");
        lgdObj.transform.SetParent(panelObj.transform, false);
        RectTransform lgdRt = lgdObj.AddComponent<RectTransform>();
        lgdRt.anchoredPosition = new Vector2(0f, -280f);
        lgdRt.sizeDelta = new Vector2(860f, 35f);
        legendText = lgdObj.AddComponent<Text>();
        legendText.font = gameFont;
        legendText.fontSize = 15;
        legendText.alignment = TextAnchor.MiddleCenter;
        legendText.color = new Color(0.70f, 0.70f, 0.70f, 1f);

        rootCanvasObj.SetActive(false);
    }

    public void Show(int slotIndex)
    {
        CreateUI();
        ActiveSlotIndex = slotIndex;
        IsOpen = true;
        isInControlsView = false;
        isListeningForInput = false;
        selectedMainIndex = 0;
        selectedControlIndex = 0;
        listenCooldownTimer = 0.25f;

        // Pausar el juego
        if (Core.Logic != null)
        {
            Core.Logic.PauseGame();
        }

        if (Core.Audio != null)
        {
            Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
        }

        CoopGameSetupUI.ApplyGameFont(rootCanvasObj);
        rootCanvasObj.SetActive(true);
        UpdateHeaderAndViews();
    }

    public void Hide()
    {
        IsOpen = false;
        ActiveSlotIndex = -1;
        isListeningForInput = false;

        if (rootCanvasObj != null)
        {
            rootCanvasObj.SetActive(false);
        }

        // Reanudar el juego
        if (Core.Logic != null)
        {
            Core.Logic.ResumeGame();
        }

        if (Core.Audio != null)
        {
            Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
        }
    }

    private void UpdateHeaderAndViews()
    {
        if (ActiveSlotIndex < 0 || ActiveSlotIndex >= CoopGameConfig.Slots.Length) return;

        string playerName = CoopPlayerRegistry.GetDisplayName(ActiveSlotIndex);
        Color playerColor = CoopPlayerRegistry.GetLabelColor(ActiveSlotIndex);
        headerTitleText.text = $"[P{ActiveSlotIndex + 1} - {playerName}] MENÚ DE OPCIONES";
        headerTitleText.color = playerColor;

        CoopDeviceType dev = CoopGameConfig.Slots[ActiveSlotIndex].Device;
        string devName = CoopGameConfig.GetDeviceDisplayName(dev);
        string lobbyInfo = !string.IsNullOrEmpty(Blasphemous.CoopLocal.MultiPlayer.CoopLobbyManager.ActiveRoomCode)
            ? $"   |   🌐 Código de Sala: {Blasphemous.CoopLocal.MultiPlayer.CoopLobbyManager.ActiveRoomCode}"
            : "";
        headerDeviceText.text = $"Dispositivo Actual: {devName}{lobbyInfo}";

        mainViewObj.SetActive(!isInControlsView);
        controlsViewObj.SetActive(isInControlsView);

        if (!isInControlsView)
        {
            for (int i = 0; i < MainOptions.Length; i++)
            {
                bool isSel = (i == selectedMainIndex);
                mainOptionTexts[i].text = isSel ? $"►  {MainOptions[i]}  ◄" : MainOptions[i];
                mainOptionTexts[i].color = isSel ? new Color(1f, 0.90f, 0.40f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);
            }
            legendText.text = "[Arriba/Abajo] Navegar   |   [A / Enter] Seleccionar   |   [B / Esc] Cerrar";
        }
        else
        {
            var bindings = CoopControlsConfig.GetBindings(ActiveSlotIndex);
            bool isKb = (dev == CoopDeviceType.Keyboard);

            for (int i = 0; i < ConfigurableActions.Length; i++)
            {
                CoopAction act = ConfigurableActions[i];
                bool isSel = (i == selectedControlIndex);
                controlActionTexts[i].text = isSel ? $"► {CoopControlsConfig.ActionDisplayName(act)}" : $"  {CoopControlsConfig.ActionDisplayName(act)}";
                controlActionTexts[i].color = isSel ? new Color(1f, 0.92f, 0.45f, 1f) : new Color(0.80f, 0.80f, 0.80f, 1f);

                if (isListeningForInput && isSel)
                {
                    controlValueTexts[i].text = "[PRESIONA UNA TECLA/BOTÓN...]";
                    controlValueTexts[i].color = Color.green;
                }
                else
                {
                    if (isKb)
                    {
                        KeyCode k = bindings.GetKey(act);
                        controlValueTexts[i].text = $"[ {CoopControlsConfig.KeyDisplayName(k)} ]";
                    }
                    else
                    {
                        if (act == CoopAction.Up || act == CoopAction.Down || act == CoopAction.Left || act == CoopAction.Right)
                        {
                            controlValueTexts[i].text = "[ STICK IZQ / D-PAD ]";
                        }
                        else
                        {
                            string padBtn = bindings.GetPadButton(act);
                            controlValueTexts[i].text = $"[ {CoopControlsConfig.PadButtonDisplayName(padBtn)} ]";
                        }
                    }
                    controlValueTexts[i].color = isSel ? new Color(1f, 0.85f, 0.35f, 1f) : new Color(0.90f, 0.75f, 0.30f, 0.90f);
                }
            }

            if (isListeningForInput)
            {
                legendText.text = "Presiona la nueva tecla o botón deseado en tu dispositivo... [ESC / B para cancelar]";
            }
            else
            {
                legendText.text = "[Arriba/Abajo] Navegar   |   [A / Enter] Reasignar Tecla/Botón   |   [B / Esc] Volver";
            }
        }
    }

    private void Update()
    {
        if (!IsOpen || ActiveSlotIndex < 0) return;

        if (listenCooldownTimer > 0f)
        {
            listenCooldownTimer -= Time.unscaledDeltaTime;
            return;
        }

        if (isListeningForInput)
        {
            ProcessInputListening();
            return;
        }

        ProcessNavigationInput();
    }

    private void ProcessInputListening()
    {
        CoopDeviceType dev = CoopGameConfig.Slots[ActiveSlotIndex].Device;
        bool isKb = (dev == CoopDeviceType.Keyboard);
        var bindings = CoopControlsConfig.GetBindings(ActiveSlotIndex);
        CoopAction targetAction = ConfigurableActions[selectedControlIndex];

        if (isKb)
        {
            // Cancelar con Escape si la acción a configurar no es Pause
            if (Input.GetKeyDown(KeyCode.Escape) && targetAction != CoopAction.Pause)
            {
                isListeningForInput = false;
                listenCooldownTimer = 0.2f;
                UpdateHeaderAndViews();
                return;
            }

            // Escanear todas las teclas de KeyCode
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(k))
                {
                    // Ignorar clics de ratón no deseados
                    if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;

                    bindings.SetKey(targetAction, k);
                    CoopControlsConfig.Save();
                    isListeningForInput = false;
                    listenCooldownTimer = 0.2f;

                    if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/EquipItem");
                    UpdateHeaderAndViews();
                    return;
                }
            }
        }
        else
        {
            var pad = Player2Pad.GetPadForSlot(ActiveSlotIndex);
            if (pad == null)
            {
                isListeningForInput = false;
                UpdateHeaderAndViews();
                return;
            }

            // Cancelar con B/Círculo si la acción a remapear no es Parry/Cancel
            if (Player2Pad.GetCancelDown(pad) && targetAction != CoopAction.Parry)
            {
                isListeningForInput = false;
                listenCooldownTimer = 0.2f;
                UpdateHeaderAndViews();
                return;
            }

            // Escanear botones del mando
            if (pad.ButtonElementIdentifiers != null)
            {
                foreach (var ident in pad.ButtonElementIdentifiers)
                {
                    if (pad.GetButtonDownById(ident.id))
                    {
                        bindings.SetPadButton(targetAction, ident.name);
                        CoopControlsConfig.Save();
                        isListeningForInput = false;
                        listenCooldownTimer = 0.2f;

                        if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/EquipItem");
                        UpdateHeaderAndViews();
                        return;
                    }
                }
            }

            // Escanear ejes del mando (Gatillos LT / RT)
            if (pad.AxisElementIdentifiers != null)
            {
                foreach (var ident in pad.AxisElementIdentifiers)
                {
                    // No capturar stick izquierdo para acciones de combate
                    if (ident.name.IndexOf("stick", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    if (Mathf.Abs(pad.GetAxisById(ident.id)) >= 0.65f)
                    {
                        bindings.SetPadButton(targetAction, ident.name);
                        CoopControlsConfig.Save();
                        isListeningForInput = false;
                        listenCooldownTimer = 0.25f;

                        if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/EquipItem");
                        UpdateHeaderAndViews();
                        return;
                    }
                }
            }
        }
    }

    private void ProcessNavigationInput()
    {
        CoopDeviceType dev = CoopGameConfig.Slots[ActiveSlotIndex].Device;
        bool isKb = (dev == CoopDeviceType.Keyboard);
        var pad = Player2Pad.GetPadForSlot(ActiveSlotIndex);

        bool navUp = isKb ? (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) : (Player2Pad.GetUp(pad));
        bool navDown = isKb ? (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) : (Player2Pad.GetDown(pad));
        bool confirm = isKb ? (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.K)) : (Player2Pad.GetSelectDown(pad));
        bool cancel = isKb ? (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) : (Player2Pad.GetCancelDown(pad));

        if (navUp)
        {
            if (!isInControlsView)
            {
                selectedMainIndex = (selectedMainIndex - 1 + MainOptions.Length) % MainOptions.Length;
            }
            else
            {
                selectedControlIndex = (selectedControlIndex - 1 + ConfigurableActions.Length) % ConfigurableActions.Length;
            }
            listenCooldownTimer = 0.18f;
            if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateHeaderAndViews();
            return;
        }

        if (navDown)
        {
            if (!isInControlsView)
            {
                selectedMainIndex = (selectedMainIndex + 1) % MainOptions.Length;
            }
            else
            {
                selectedControlIndex = (selectedControlIndex + 1) % ConfigurableActions.Length;
            }
            listenCooldownTimer = 0.18f;
            if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
            UpdateHeaderAndViews();
            return;
        }

        if (confirm)
        {
            listenCooldownTimer = 0.2f;
            if (!isInControlsView)
            {
                ExecuteMainOption(selectedMainIndex);
            }
            else
            {
                // Iniciar escucha de input para reasignar la acción seleccionada
                CoopAction act = ConfigurableActions[selectedControlIndex];
                if (!isKb && (act == CoopAction.Up || act == CoopAction.Down || act == CoopAction.Left || act == CoopAction.Right))
                {
                    // En mando, los ejes del stick izquierdo no se reasignan manualmente
                    if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ButtonPress");
                    return;
                }
                isListeningForInput = true;
                listenCooldownTimer = 0.2f;
                if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ButtonPress");
                UpdateHeaderAndViews();
            }
            return;
        }

        if (cancel)
        {
            listenCooldownTimer = 0.2f;
            if (isInControlsView)
            {
                isInControlsView = false;
                if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateHeaderAndViews();
            }
            else
            {
                Hide();
            }
        }
    }

    private void ExecuteMainOption(int index)
    {
        switch (index)
        {
            case 0: // Reanudar Partida
                Hide();
                break;

            case 1: // Ver y Configurar Controles
                isInControlsView = true;
                selectedControlIndex = 0;
                if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/ChangeTab");
                UpdateHeaderAndViews();
                break;

            case 2: // Restablecer Controles por Defecto
                CoopControlsConfig.ResetBindingsToDefault(ActiveSlotIndex);
                if (Core.Audio != null) Core.Audio.PlayOneShot("event:/SFX/UI/UnequipItem");
                UpdateHeaderAndViews();
                break;

            case 3: // Salir al Menú Principal
                ExitToMainMenu();
                break;
        }
    }

    private void ExitToMainMenu()
    {
        Hide();

        try
        {
            // Guardado integral de persistencia vanilla y cooperativo
            if (Core.Persistence != null)
            {
                Core.Persistence.SaveGame();
            }
            CoopSaveManager.SaveCurrentSlot();

            if (Core.Logic != null)
            {
                Core.Logic.ResumeGame();
            }

            if (UIController.instance != null)
            {
                UIController.instance.HidePauseMenu();
                UIController.instance.HideMiriamTimer();
            }

            // Restaurar teclado a P1 para navegar el menú principal
            Player2Input.ResetToMainMenuInput();

            if (Core.Logic != null)
            {
                Core.Logic.LoadMenuScene();
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log($"[CoopPlayerPauseMenu] Error saliendo al menú principal: {ex.Message}");
        }
    }
}

[HarmonyLib.HarmonyPatch(typeof(Gameplay.UI.UIController), "Update")]
internal static class UIController_Update_CoopPause_Patch
{
    private static bool Prefix()
    {
        if (CoopPlayerPauseMenu.IsOpen)
        {
            return false;
        }
        return true;
    }
}
