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
        if (proCamera2D == null || CoopLocal.Player2 == null)
        {
            return;
        }

        if (proCamera2D.GetComponent<ProCamera2DZoomToFitTargets>() == null)
        {
            proCamera2D.gameObject.AddComponent<ProCamera2DZoomToFitTargets>();
        }

        // Round 55: the user reported normal Coop mode (both targets, the default - not an F10
        // debug state) sits too high vertically, not centering on the pair, cutting off the bottom
        // of vertical rooms - the exact same *symptom* ronda 53 found and fixed for F10's own
        // mid-room target switch, but that fix (proCamera2D.Reset() after a target actually
        // changes) only ever got triggered from CameraTargetDebugToggle.Apply(), called below,
        // *after* P2 has already been added directly a few lines above. By the time Apply() runs,
        // GetCameraTarget(P2) already returns non-null (just added), so its own "did anything
        // actually change" check sees no change and never calls Reset() - meaning the *original*,
        // primary code path that first turns the camera from single-target (P1 alone, vanilla) into
        // two-target coop framing has *never* re-centered/resynced the camera's internal follow
        // state once P2 joins the target list, since this method was first written (rondas
        // pre-F10) - not something the F10 feature introduced. The same mechanism ronda 53
        // diagnosed applies here: ProCamera2D.Move()'s own smoothing + boundary-clamping
        // (ProCamera2DGeometryBoundaries) then has to bridge, unassisted, whatever gap opened up
        // between "camera snapped to P1 alone" (vanilla's own CameraManager.UpdateNewCameraParams
        // body, which runs immediately before this Postfix) and "the real P1+P2 weighted midpoint"
        // the instant P2 is added - which can leave it stuck too high if that gap happens to be
        // downward (P2 lower than P1) and boundary-clamping can't fully resolve it on its own.
        // Fixed the same way as ronda 53: explicitly re-center once, right when P2 genuinely
        // becomes a target for the first time (tracked here directly, not inferred from Apply()'s
        // own already-too-late check).
        bool addedNow = false;
        if (proCamera2D.GetCameraTarget(CoopLocal.Player2.transform) == null)
        {
            // Same weight/offset the game itself uses for P1 in
            // CameraManager.UpdateNewCameraParams - keeps both players framed with identical
            // priority.
            proCamera2D.AddCameraTarget(CoopLocal.Player2.transform, 1f, 1f, 0f, new Vector2(0f, 6f));
            addedNow = true;
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
    Coop,
    P1Only,
    P2Only,
}

internal static class CameraTargetDebugToggle
{
    // Same weight/offset CameraManager.UpdateNewCameraParams()/AddPlayer2AsCameraTarget already
    // use for P1/P2 respectively - re-adding a target that was previously removed has to match
    // this exactly, or the two players would come back framed with different priority.
    private static readonly Vector2 TargetOffset = new Vector2(0f, 6f);

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
        switch (Mode)
        {
            case CameraDebugTargetMode.Coop:
                Mode = CameraDebugTargetMode.P1Only;
                break;
            case CameraDebugTargetMode.P1Only:
                Mode = CameraDebugTargetMode.P2Only;
                break;
            default:
                Mode = CameraDebugTargetMode.Coop;
                break;
        }
        Apply();
        CameraTargetModeIndicator.Show(Mode);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[CameraDebug] camera target mode -> {Mode}", Main.CoopLocal);
        }
    }

    // Re-applies the current Mode against the camera's actual target list - safe to call any
    // number of times (SetTargetPresent below only adds/removes when the current state doesn't
    // already match), including right after something else just rebuilt the target list from
    // scratch (see the Postfix above and CoopLocal.OnPlayerSpawn).
    //
    // Round 53: the user reported that switching target mid-room (or re-selecting the same target
    // again) leaves the camera "misplaced" - rises too high, doesn't center, cuts off the bottom
    // of vertical rooms - but a room entered *already* in P1-only/P2-only mode is fine. The
    // difference is that a normal room load never just mutates ProCamera2D.CameraTargets on its
    // own - CameraManager.UpdateNewCameraParams() also directly snaps ProCamera2D.transform's
    // position to the target and (for one second) zeroes Horizontal/VerticalFollowSmoothness, so
    // the follow/boundary-clamping pipeline (ProCamera2D.Move(), CreativeSpore's own smoothing +
    // ProCamera2DGeometryBoundaries' raycast-based delta clamp against level colliders) never has
    // to bridge a large one-frame jump. AddCameraTarget/RemoveCameraTarget called on their own
    // (what this class used to do) change ProCamera2D.CameraTargets - and therefore the raw
    // targets-weighted midpoint Move() computes every frame - instantly, but never touch any of
    // the camera's own internal follow/zoom state, so the very next Move() call tries to smoothly
    // (and boundary-clamp-limited) approach a target that may have jumped many world units in one
    // frame, through whatever level geometry happens to be nearby - exactly the kind of jump that
    // can leave MoveInColliderBoundaries' raycast-based clamping stuck against the wrong edge.
    // ProCamera2D.Reset(bool centerOnTargets = true) is the SDK's own built-in fix for precisely
    // this situation - it re-centers instantly on the current target list's weighted midpoint via
    // MoveCameraInstantlyToPosition (which also calls ResetMovement(), resyncing the internal
    // smoothed-position state to match instead of leaving it stale), resets zoom back to the
    // level's base size (ResetSize()), and fires the ProCamera2D.OnReset event - which
    // ProCamera2DZoomToFitTargets.OnReset() is subscribed to (via BasePC2D.Enable()) and uses to
    // reset its own _zoomVelocity/_targetCamSize/_targetCamSizeSmoothed back to _initialCamSize.
    // Only called when SetTargetPresent below actually changed something, so a normal level-load
    // call in Coop mode (where nothing needs to change) never disturbs vanilla's own positioning.
    internal static void Apply()
    {
        ProCamera2D proCamera2D = CameraManager.Instance != null ? CameraManager.Instance.ProCamera2D : null;
        Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
        Penitent p2 = CoopLocal.Player2;
        if (proCamera2D == null || p1 == null)
        {
            return;
        }

        bool changed = SetTargetPresent(proCamera2D, p1.transform, Mode != CameraDebugTargetMode.P2Only);
        if (p2 != null)
        {
            changed |= SetTargetPresent(proCamera2D, p2.transform, Mode != CameraDebugTargetMode.P1Only);
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
            // duration=0 removes immediately (no fade-out coroutine) - same as the drop-target
            // call CoopLocal.OnPlayerSpawn already makes when P2 is destroyed on respawn.
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
    }
}

// On-screen indicator for the current CameraDebugTargetMode - same Canvas/TextMeshProUGUI
// screen-space pattern Player2ModeIndicator (Input/Player2Input.cs) already established for F9's
// indicator, just anchored directly below it (Player2ModeIndicator sits at anchoredPosition
// (-16,-16) with a 40px-tall box) so the two never overlap.
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
        }
    }

    private static string ModeLabel(CameraDebugTargetMode mode)
    {
        switch (mode)
        {
            case CameraDebugTargetMode.P1Only:
                return "P1";
            case CameraDebugTargetMode.P2Only:
                return "P2";
            default:
                return "coop";
        }
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

