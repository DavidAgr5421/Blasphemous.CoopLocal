using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Environment.AreaEffects;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using Gameplay.GameControllers.Penitent.Gizmos;
using Gameplay.GameControllers.Penitent.InputSystem;
using Gameplay.GameControllers.Penitent.Sensor;
using Gameplay.UI.Others.UIGameLogic;
using DG.Tweening;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// The camera (ProCamera2D, the Com.LuisPedroFonseca.ProCamera2D asset the game ships in
// Assembly-CSharp-firstpass.dll) only ever tracks Core.Logic.Penitent -
// CameraManager.UpdateNewCameraParams() (called on every level load) wipes the whole target
// list and re-adds P1 alone. ProCamera2D itself already supports multiple simultaneous targets
// natively (it tracks their combined midpoint) and ships its own purpose-built extension for
// exactly the requested "shared, beat-em-up style" behavior - ProCamera2DZoomToFitTargets
// automatically zooms the camera out to keep every current target on screen, and back in as
// they get closer together - it's just never attached to the game's camera by default, since
// vanilla never has more than one target. This adds it once and keeps P2 registered as a second
// target through every level transition (Postfixing UpdateNewCameraParams, since that method
// rebuilds the target list from scratch each time) and every P2 respawn (see
// CoopLocal.OnPlayerSpawn, for the case where a respawn doesn't also trigger a full camera
// reset). GetCameraTarget(...) guards against double-adding P2 in either path -
// AddCameraTarget itself has no such guard and would otherwise create a second, competing
// target entry for the exact same Transform.
// A stale keyboard-numpad concern from the pre-gamepad-split era (see Player2Pad's comment for
// current history) - CameraPan's own numpad-driven manual camera panning (Rewired axes 20/21,
// read directly off the shared "Player 0" the same way everything else in this family does).
// EnableCameraPan is a plain public field, never reassigned anywhere in the game's own
// code after its initial Inspector-set value (confirmed - nothing else writes to it), so forcing
// it false once per CameraPan instance is permanent for that instance's lifetime; Postfixing
// Start() (rather than a one-time find-and-set from CoopLocal) means this keeps applying correctly
// across level transitions, whenever the game creates a fresh CameraPan for the new scene.
[HarmonyPatch(typeof(CameraPan), "Start")]
internal static class CameraPan_Disable_Patch
{
    private static void Postfix(CameraPan __instance)
    {
        __instance.EnableCameraPan = false;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.UpdateNewCameraParams))]
internal static class CameraManager_UpdateNewCameraParams_Patch
{
    private static void Postfix(CameraManager __instance) => AddPlayer2AsCameraTarget(__instance.ProCamera2D);

    internal static void AddPlayer2AsCameraTarget(ProCamera2D proCamera2D)
    {
        if (proCamera2D == null)
        {
            return;
        }

        if (proCamera2D.GetComponent<ProCamera2DZoomToFitTargets>() == null)
        {
            proCamera2D.gameObject.AddComponent<ProCamera2DZoomToFitTargets>();
        }

        bool addedNow = false;
        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra != null && proCamera2D.GetCameraTarget(extra.transform) == null)
            {
                proCamera2D.AddCameraTarget(extra.transform, 1f, 1f, 0f, Vector2.zero);
                addedNow = true;
            }
        }

        if (addedNow)
        {
            proCamera2D.Reset(centerOnTargets: true);
        }

        // Round 52: re-assert whichever debug target mode (see CameraTargetDebugToggle below) was
        // picked before this reset - both UpdateNewCameraParams (level load) and
        // CoopLocal.OnPlayerSpawn (respawn) unconditionally rebuild the target list back to the
        // Coop default, which would otherwise silently discard an F10 selection on the very next
        // room change.
        CameraTargetDebugToggle.Apply();
    }
}

// Round 52 (debug tool, not player-facing): F10 cycles which player(s) the camera actually
// follows - Coop (both, the normal default above), P1 only, or P2 only - to make it easy to
// isolate camera/positioning issues to a single player instead of always looking at the blended
// midpoint. Purely a dev hotkey, read directly off UnityEngine.Input the same way every other
// debug-only toggle in this codebase is (F9's device-mode toggle in Input/Player2Input.cs) -
// deliberately not routed through Player2Keys/PlayerLogicBlocker, since it isn't part of either
// player's own control scheme.
internal enum CameraDebugTargetMode
{
    Coop = -1,
    P1Only = 0,
    P2Only = 1,
    P3Only = 2,
    P4Only = 3,
}

internal static class CameraTargetDebugToggle
{
    private static readonly Vector2 TargetOffset = Vector2.zero;

    private static GameObject driverObject;

    internal static CameraDebugTargetMode Mode { get; private set; } = CameraDebugTargetMode.Coop;

    internal static void EnsureCreated()
    {
        if (driverObject != null)
        {
            return;
        }
        driverObject = new GameObject("CoopLocalCameraDebugToggle");
        UnityEngine.Object.DontDestroyOnLoad(driverObject);
        driverObject.AddComponent<CameraTargetDebugToggleDriver>();
    }

    internal static void CycleMode()
    {
        int playerCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 1, 4);

        if (Mode == CameraDebugTargetMode.Coop)
        {
            Mode = CameraDebugTargetMode.P1Only;
        }
        else if (Mode == CameraDebugTargetMode.P1Only)
        {
            Mode = (playerCount >= 2) ? CameraDebugTargetMode.P2Only : CameraDebugTargetMode.Coop;
        }
        else if (Mode == CameraDebugTargetMode.P2Only)
        {
            Mode = (playerCount >= 3) ? CameraDebugTargetMode.P3Only : CameraDebugTargetMode.Coop;
        }
        else if (Mode == CameraDebugTargetMode.P3Only)
        {
            Mode = (playerCount >= 4) ? CameraDebugTargetMode.P4Only : CameraDebugTargetMode.Coop;
        }
        else
        {
            Mode = CameraDebugTargetMode.Coop;
        }

        Apply();
        CameraTargetModeIndicator.Show(Mode);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CameraDebug] camera target mode -> {Mode} (session players: {playerCount})", Main.CoopLocal);
        }
    }

    internal static void Apply()
    {
        ProCamera2D proCamera2D = CameraManager.Instance != null ? CameraManager.Instance.ProCamera2D : null;
        Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
        if (proCamera2D == null || p1 == null)
        {
            return;
        }

        int playerCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 1, 4);

        bool[] isAlive = new bool[playerCount];
        Penitent[] players = new Penitent[playerCount];

        players[0] = p1;
        isAlive[0] = (p1 != null && !p1.Status.Dead && !Player1DeathState.IsPendingRevive());
        int livingCount = isAlive[0] ? 1 : 0;

        for (int i = 1; i < playerCount; i++)
        {
            Penitent extra = CoopLocal.GetPlayer(i);
            players[i] = extra;
            isAlive[i] = (extra != null && !extra.Status.Dead && !Player2DeathState.IsPendingRevive(i));
            if (isAlive[i]) livingCount++;
        }

        bool changed = false;

        if (Mode == CameraDebugTargetMode.Coop)
        {
            // Modo Coop: la cámara sigue a TODOS los jugadores vivos en la sesión.
            // Si un jugador muere, se retira inmediatamente del target de la cámara.
            for (int i = 0; i < playerCount; i++)
            {
                if (players[i] == null) continue;
                bool shouldInclude = (livingCount > 0) ? isAlive[i] : (i == 0);
                changed |= SetTargetPresent(proCamera2D, players[i].transform, shouldInclude);
            }
        }
        else
        {
            // Modo de target único (P1, P2, P3 o P4)
            int targetIdx = (int)Mode;
            bool targetAlive = targetIdx >= 0 && targetIdx < playerCount && isAlive[targetIdx];

            for (int i = 0; i < playerCount; i++)
            {
                if (players[i] == null) continue;
                bool shouldInclude;
                if (targetAlive)
                {
                    shouldInclude = (i == targetIdx);
                }
                else if (livingCount > 0)
                {
                    // Si el jugador objetivo murió, enfocar a los vivos
                    shouldInclude = isAlive[i];
                }
                else
                {
                    shouldInclude = (i == 0);
                }
                changed |= SetTargetPresent(proCamera2D, players[i].transform, shouldInclude);
            }
        }

        // Quitar targets de jugadores que no estén en la sesión activa
        for (int i = playerCount; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            Penitent extra = CoopLocal.GetPlayer(i);
            if (extra != null)
            {
                changed |= SetTargetPresent(proCamera2D, extra.transform, false);
            }
        }

        if (changed)
        {
            proCamera2D.Reset(centerOnTargets: true);
        }
    }

    private static bool SetTargetPresent(ProCamera2D proCamera2D, Transform target, bool present)
    {
        bool isPresent = proCamera2D.GetCameraTarget(target) != null;
        if (present && !isPresent)
        {
            proCamera2D.AddCameraTarget(target, 1f, 1f, 0f, TargetOffset);
            return true;
        }
        if (!present && isPresent)
        {
            proCamera2D.RemoveCameraTarget(target);
            return true;
        }
        return false;
    }
}

internal class CameraTargetDebugToggleDriver : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F10))
        {
            CameraTargetDebugToggle.CycleMode();
        }

        CameraTargetDebugToggle.Apply();
    }
}

internal static class CameraTargetModeIndicator
{
    private const string GameFontName = "MajesticExtended_FullLatin";

    private static TMPro.TextMeshProUGUI label;

    internal static void Show(CameraDebugTargetMode mode)
    {
        EnsureCreated();
        if (label != null)
        {
            label.text = "target: " + ModeLabel(mode);
            int idx = (int)mode;
            if (idx >= 0 && idx < CoopPlayerRegistry.MaxPlayers)
            {
                label.color = CoopPlayerRegistry.GetLabelColor(idx);
            }
            else
            {
                label.color = Color.white;
            }
        }
    }

    private static string ModeLabel(CameraDebugTargetMode mode)
    {
        int idx = (int)mode;
        if (idx >= 0 && idx < CoopPlayerRegistry.MaxPlayers)
        {
            return $"P{idx + 1} ({CoopPlayerRegistry.GetDisplayName(idx)})";
        }
        return "coop";
    }

    private static void EnsureCreated()
    {
        if (label != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("CoopLocalCameraModeIndicatorCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject textObject = new GameObject("CameraModeText");
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -60f);
        rect.sizeDelta = new Vector2(320f, 40f);

        label = textObject.AddComponent<TMPro.TextMeshProUGUI>();
        TMPro.TMP_FontAsset gameFont = Array.Find(
            Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(),
            f => f.name == GameFontName);
        if (gameFont != null)
        {
            label.font = gameFont;
        }
        label.fontSize = 22;
        label.alignment = TMPro.TextAlignmentOptions.TopRight;
        label.color = Color.white;
        label.text = "";
    }
}

[HarmonyPatch(typeof(CameraPlayerOffset), nameof(CameraPlayerOffset.UpdateNewParams))]
internal static class CameraPlayerOffset_UpdateNewParams_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(CameraPlayerOffset), "_penitent");
    private static readonly FieldInfo ProCameraField = AccessTools.Field(typeof(CameraPlayerOffset), "_proCamera2D");
    private static readonly FieldInfo PlayerCurrentOrientationField = AccessTools.Field(typeof(CameraPlayerOffset), "_playerCurrentOrientation");
    private static readonly FieldInfo PlayerLastOrientationField = AccessTools.Field(typeof(CameraPlayerOffset), "_playerLastOrientation");

    private static void Postfix(CameraPlayerOffset __instance)
    {
        if (PenitentField == null || ProCameraField == null)
        {
            return;
        }

        CameraDebugTargetMode mode = CameraTargetDebugToggle.Mode;
        ProCamera2D proCamera2D = __instance.GetComponent<ProCamera2D>();
        if (proCamera2D == null)
        {
            proCamera2D = ProCameraField.GetValue(__instance) as ProCamera2D;
        }
        if (proCamera2D == null)
        {
            return;
        }

        int targetIdx = (int)mode;
        if (targetIdx >= 0)
        {
            Penitent targetPenitent = CoopLocal.GetPlayer(targetIdx);
            if (targetPenitent != null)
            {
                PenitentField.SetValue(__instance, targetPenitent);
                EntityOrientation orientation = targetPenitent.Status.Orientation;
                if (PlayerCurrentOrientationField != null)
                {
                    PlayerCurrentOrientationField.SetValue(__instance, orientation);
                }
                if (PlayerLastOrientationField != null)
                {
                    PlayerLastOrientationField.SetValue(__instance, orientation);
                }
                if (DOTween.IsTweening("ForwardFocus"))
                {
                    DOTween.Kill("ForwardFocus");
                }
                float targetX = (orientation == EntityOrientation.Left) ? -__instance.XOffset : __instance.XOffset;
                if (__instance.PlayerTarget != null)
                {
                    DOTween.To(() => proCamera2D.OverallOffset.x, x => proCamera2D.OverallOffset.x = x, targetX, 0f)
                        .SetEase(Ease.OutSine).SetId("ForwardFocus");
                    Vector2 offset = proCamera2D.OverallOffset;
                    offset.x = targetX;
                    proCamera2D.OverallOffset = offset;
                }
                var prop = AccessTools.Property(typeof(CameraPlayerOffset), "DefaultTargetOffset");
                if (prop != null)
                {
                    prop.SetValue(__instance, proCamera2D.OverallOffset, null);
                }
            }
        }
        else if (mode == CameraDebugTargetMode.Coop)
        {
            if (DOTween.IsTweening("ForwardFocus"))
            {
                DOTween.Kill("ForwardFocus");
            }
            if (Mathf.Abs(proCamera2D.OverallOffset.x) > 0.001f)
            {
                DOTween.To(() => proCamera2D.OverallOffset.x, x => proCamera2D.OverallOffset.x = x, 0f, 0f)
                    .SetEase(Ease.OutSine).SetId("ForwardFocus");
                Vector2 offset = proCamera2D.OverallOffset;
                offset.x = 0f;
                proCamera2D.OverallOffset = offset;
            }
            var prop = AccessTools.Property(typeof(CameraPlayerOffset), "DefaultTargetOffset");
            if (prop != null)
            {
                prop.SetValue(__instance, proCamera2D.OverallOffset, null);
            }
            PenitentField.SetValue(__instance, null);
        }
    }
}

[HarmonyPatch(typeof(CameraPlayerOffset), "LateUpdate")]
internal static class CameraPlayerOffset_LateUpdate_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(CameraPlayerOffset), "_penitent");

    private static bool Prefix(CameraPlayerOffset __instance)
    {
        CameraDebugTargetMode mode = CameraTargetDebugToggle.Mode;

        int targetIdx = (int)mode;
        if (targetIdx >= 0)
        {
            Penitent targetPenitent = CoopLocal.GetPlayer(targetIdx);
            if (targetPenitent != null)
            {
                if (PenitentField != null)
                {
                    object current = PenitentField.GetValue(__instance);
                    if (!ReferenceEquals(current, targetPenitent))
                    {
                        PenitentField.SetValue(__instance, targetPenitent);
                    }
                }
                return true;
            }
        }

        if (mode == CameraDebugTargetMode.Coop)
        {
            ProCamera2D proCamera2D = __instance.GetComponent<ProCamera2D>();
            if (proCamera2D != null && Mathf.Abs(proCamera2D.OverallOffset.x) > 0.001f)
            {
                if (DOTween.IsTweening("ForwardFocus"))
                {
                    DOTween.Kill("ForwardFocus");
                }
                DOTween.To(() => proCamera2D.OverallOffset.x, x => proCamera2D.OverallOffset.x = x, 0f, __instance.ElapsedTime)
                    .SetEase(Ease.OutSine).SetId("ForwardFocus");
            }
            return false;
        }

        return true;
    }
}
