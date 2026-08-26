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

// Interactable (level props: doors, levers, item pickups, NPC dialogue triggers) is a per-OBJECT
// input check, not per-Penitent like the Ability classes above - InteractionTriggered reads
// button 8 off the shared Rewired Player 0 directly, plus hardcodes Core.Logic.Penitent (always
// P1) for its own "not currently jumping/grabbing a cliff ledge" gates, then returns
// !OverlappedInteractor as its final result. OverlappedInteractor is NOT "a player is in range"
// (that's the separate PlayerInRange property, set correctly for both P1 and P2 via a generic
// CompareTag("Penitent") check in OnEntityEnter/Exit - no owner bug there) - it's only ever
// written by the narrow Execution/GuiltDropCollectibleItem subsystems (finishers/guilt drops),
// meaning it's false for every ordinary door/lever/chest, and vanilla's own logic *requires* it
// to be false to succeed. (Round 36 fix: an earlier version of this patch had that inverted -
// checking `!OverlappedInteractor` as if it were a required-true gate - which meant this Postfix
// bailed out on almost every ordinary interactable and Interact silently never worked for P2.)
// PlayerInRange itself doesn't need rechecking here since Door/Lever/etc.'s own OnUpdate() (the
// caller) already ANDs InteractionTriggered together with its own PlayerInRange check.
[HarmonyPatch(typeof(Tools.Level.Interactable), "get_InteractionTriggered")]
internal static class Interactable_InteractionTriggered_Patch
{
    private static readonly FieldInfo InteractableWhileJumpingField =
        AccessTools.Field(typeof(Tools.Level.Interactable), "interactableWhileJumping");

    private static void Postfix(Tools.Level.Interactable __instance, ref bool __result)
    {
        if (__result || CoopLocal.Player2 == null || !Player2Input.InteractDown)
        {
            return;
        }
        if (__instance.OverlappedInteractor || Core.Input.InputBlocked)
        {
            return;
        }
        bool interactableWhileJumping = (bool)InteractableWhileJumpingField.GetValue(__instance);
        if (CoopLocal.Player2.IsJumping && !interactableWhileJumping)
        {
            return;
        }
        if (CoopLocal.Player2.IsGrabbingCliffLede)
        {
            return;
        }
        __result = true;
    }
}


