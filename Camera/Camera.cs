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

        if (proCamera2D.GetCameraTarget(CoopLocal.Player2.transform) == null)
        {
            // Same weight/offset the game itself uses for P1 in
            // CameraManager.UpdateNewCameraParams - keeps both players framed with identical
            // priority.
            proCamera2D.AddCameraTarget(CoopLocal.Player2.transform, 1f, 1f, 0f, new Vector2(0f, 6f));
        }
    }
}


