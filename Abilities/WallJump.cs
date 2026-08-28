using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using DG.Tweening;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using System.Collections;
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

// WallJump (Gameplay.GameControllers.Penitent.Abilities.WallJump) - jump toward a wall and hold
// Attack to stick to it - was never audited before. Two separate bugs found reading the decompiled
// source directly:
//
//   Family 2 (Rewired compartido): OnStart() does `Rewired = ReInput.players.GetPlayer(0)` - the
//   one shared Rewired "Player 0" for the whole process, same as everywhere else in this family.
//   Every trigger in OnUpdate() (GetButton(5) = Attack held, GetButtonDown(6) = Jump pressed,
//   GetButton(65) = an early "let go" cancel gesture) reads it directly, so P2's own copy of this
//   component only ever reacts to P1's real keyboard/controller.
//
//   Family 1-style hardcode (not the lazy-null-fallback shape, the flat "always P1" shape already
//   catalogued in NOTES.md for Parry.StartParry/Penitent.Damage): inside the stick-on branch,
//   `Core.Logic.Penitent.Audio.SetParametersValuesByWall(...)` and
//   `playerStickedOrientation = Core.Logic.Penitent.Status.Orientation` both read the P1 singleton
//   instead of `base.EntityOwner` - which the rest of the method already uses correctly. The same
//   hardcode also shows up inside the private Stick() method (`Core.Logic.Penitent.SetOrientation
//   (playerStickedOrientation)`).
//
// Everything else in OnUpdate()/Stick()/Detach()/JumpOff()/ResetWallJumpStatus() already reads
// CharacterController/CharacterInput/EntityOwner, all correctly wired per-instance already (same
// prefab, same pattern as DamageArea/Stats elsewhere) - so unlike Parry/Dash, most of this class
// didn't need touching at all. Fixed the same way as those two: Prefix on OnUpdate that, for P2
// only, skips the original entirely and reimplements it with Player2Input reads and the real
// owner substituted in. Stick()/Detach()'s bodies are inlined here rather than called via
// reflection, both because Stick() needed its own fix anyway and because it keeps every private
// field access in one place.
//
// Known, deliberate gap: the early "let go of the wall" cancel gesture (Rewired button 65 in
// vanilla, CheckCancelHook()/UnHang()) has no equivalent P2 mapping yet - nothing else in this mod
// uses that button index, so rather than guess a keybind, P2 simply doesn't get that shortcut for
// now. Detach() (triggered by Jump) is still P2's normal way off the wall. Revisit if reported.
[HarmonyPatch(typeof(WallJump), "OnUpdate")]
internal static class WallJump_OnUpdate_P2_Patch
{
    private static readonly int WallClimbContactAnim = Animator.StringToHash("WallClimbContact");
    private static readonly int JumpForwardAnim = Animator.StringToHash("Jump Forward");

    private static readonly FieldInfo StickToWallField = AccessTools.Field(typeof(WallJump), "_stickToWall");
    private static readonly FieldInfo WallHitField = AccessTools.Field(typeof(WallJump), "_wallHit");
    private static readonly FieldInfo PlayerStickedOrientationField = AccessTools.Field(typeof(WallJump), "playerStickedOrientation");
    private static readonly FieldInfo JumpOffWallField = AccessTools.Field(typeof(WallJump), "_jumpOffWall");
    private static readonly FieldInfo StickCoolDownTimerField = AccessTools.Field(typeof(WallJump), "_stickCoolDownTimer");
    private static readonly FieldInfo JumpOffCoolDownTimerField = AccessTools.Field(typeof(WallJump), "_jumpOffCoolDownTimer");
    private static readonly FieldInfo IsJumpOffStackedField = AccessTools.Field(typeof(WallJump), "_isJumpOffStacked");
    private static readonly FieldInfo WallJumpTimerField = AccessTools.Field(typeof(WallJump), "_wallJumpTimer");
    private static readonly FieldInfo DefaultRayCastDistanceField = AccessTools.Field(typeof(WallJump), "_defaultRayCastDistance");
    private static readonly FieldInfo DisabledAbilityWhenUseField = AccessTools.Field(typeof(WallJump), "DisabledAbilityWhenUse");
    private static readonly MethodInfo DisableAbilityMethod = AccessTools.Method(typeof(WallJump), "DisableAbility");

    private static bool Prefix(WallJump __instance)
    {
        Penitent owner = __instance.EntityOwner as Penitent;
        if (owner == null || owner != CoopLocal.Player2)
        {
            return true;
        }

        PlatformCharacterController controller = __instance.CharacterController;
        bool stickToWall = (bool)StickToWallField.GetValue(__instance);
        float stickCoolDownTimer = (float)StickCoolDownTimerField.GetValue(__instance);
        float jumpOffCoolDownTimer = (float)JumpOffCoolDownTimerField.GetValue(__instance);
        bool jumpOffWall = (bool)JumpOffWallField.GetValue(__instance);

        if (owner.Status.IsGrounded)
        {
            // ResetWallJumpStatus(), inlined - no owner-hardcode issue in the original, just moved
            // here so every field write for P2 lives in one place.
            JumpOffWallField.SetValue(__instance, false);
            IsJumpOffStackedField.SetValue(__instance, false);
            StickToWallField.SetValue(__instance, false);
            StickCoolDownTimerField.SetValue(__instance, -1f);
            controller.PlatformCharacterPhysics.Gravity = new Vector3(0f, -9.8f, 0f);
            stickToWall = false;
            stickCoolDownTimer = -1f;
            jumpOffWall = false;
            if (__instance.Distance <= 0f)
            {
                __instance.Distance = (float)DefaultRayCastDistanceField.GetValue(__instance);
            }
        }

        Vector3 rayOrigin = new Vector3(__instance.transform.position.x, __instance.transform.position.y + __instance.HookHeightFromPivotPoint, __instance.transform.position.z);
        float dir = owner.Status.Orientation != EntityOrientation.Right ? -1f : 1f;
        RaycastHit2D wallHit = Physics2D.Raycast(rayOrigin, Vector2.right * dir, __instance.Distance, __instance.WallLayerMask);
        WallHitField.SetValue(__instance, wallHit);

        bool endStickCoolDown = stickCoolDownTimer < 0f;
        if (Player2Input.AttackHeld && !controller.IsGrounded && wallHit.collider != null && !stickToWall && endStickCoolDown)
        {
            owner.Audio.SetParametersValuesByWall(wallHit.collider);
            stickToWall = true;
            StickToWallField.SetValue(__instance, true);
            PlayerStickedOrientationField.SetValue(__instance, owner.Status.Orientation);
            owner.Animator.ResetTrigger("AIR_ATTACK");
            owner.Animator.Play(WallClimbContactAnim);
            owner.transform.position = GetClimbPosition(__instance, owner, wallHit.collider);
            PlayerLogicBlocker.SetBlocked(owner, true);
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: true);
            owner.transform.DOMoveY(owner.transform.position.y - __instance.GravityDragDistance, __instance.GravityDragLapse).SetEase(Ease.OutSine).OnUpdate(() =>
            {
                RaycastHit2D currentHit = (RaycastHit2D)WallHitField.GetValue(__instance);
                if (currentHit.collider == null)
                {
                    DOTween.Kill(owner.transform);
                }
            });
        }

        if (stickToWall)
        {
            // Stick(), inlined and fixed - the one line that mattered:
            // Core.Logic.Penitent.SetOrientation(...) -> owner.SetOrientation(...).
            JumpOffWallField.SetValue(__instance, false);
            __instance.ToogleAbilities(false);
            jumpOffCoolDownTimer -= Time.deltaTime;
            JumpOffCoolDownTimerField.SetValue(__instance, jumpOffCoolDownTimer);
            stickCoolDownTimer = __instance.StickCoolDown;
            StickCoolDownTimerField.SetValue(__instance, stickCoolDownTimer);
            IsJumpOffStackedField.SetValue(__instance, false);
            controller.PlatformCharacterPhysics.Velocity = Vector3.zero;
            controller.PlatformCharacterPhysics.VSpeed = 0f;
            controller.PlatformCharacterPhysics.Gravity = Vector3.zero;
            controller.PlatformCharacterPhysics.Acceleration = Vector3.zero;
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: true);
            EntityOrientation stickedOrientation = (EntityOrientation)PlayerStickedOrientationField.GetValue(__instance);
            owner.SetOrientation(stickedOrientation);
        }

        if (Player2Input.JumpDown && stickToWall && jumpOffCoolDownTimer < 0f && owner.Animator.GetBool("STICK_ON_WALL") && !Gameplay.UI.UIController.instance.IsShowingMenu)
        {
            DOTween.Kill(owner.transform);
            __instance.ToogleAbilities(true);

            // Detach(), inlined - already owner-safe in the original, no bug here.
            StickToWallField.SetValue(__instance, false);
            JumpOffWallField.SetValue(__instance, true);
            jumpOffWall = true;
            controller.PlatformCharacterPhysics.Gravity = new Vector3(0f, -9.8f, 0f);
            PlayerLogicBlocker.SetBlocked(owner, false);
            Core.Input.SetBlocker("PLAYER_LOGIC", blocking: false);
            controller.PlatformCharacterPhysics.Velocity = new Vector2(__instance.WallJumpSpeed * __instance.CharacterInput.FHorAxis, __instance.WallJumpSpeed);
            owner.Animator.SetBool("STICK_ON_WALL", false);
            GrabCliffLede disabledAbility = (GrabCliffLede)DisabledAbilityWhenUseField.GetValue(__instance);
            if (disabledAbility != null)
            {
                IEnumerator routine = (IEnumerator)DisableAbilityMethod.Invoke(__instance, null);
                __instance.StartCoroutine(routine);
            }
        }

        if (jumpOffWall)
        {
            // JumpOff(), inlined - already owner-safe in the original, no bug here.
            JumpOffCoolDownTimerField.SetValue(__instance, __instance.JumpOffCoolDown);
            stickCoolDownTimer -= Time.deltaTime;
            StickCoolDownTimerField.SetValue(__instance, stickCoolDownTimer);
            bool isJumpOffStacked = (bool)IsJumpOffStackedField.GetValue(__instance);
            if (!isJumpOffStacked)
            {
                IsJumpOffStackedField.SetValue(__instance, true);
                EntityOrientation stickedOrientation = (EntityOrientation)PlayerStickedOrientationField.GetValue(__instance);
                float x = stickedOrientation != EntityOrientation.Right ? 1f : -1f;
                __instance.CharacterInput.Move(x, 0.1f);
                owner.Animator.Play(JumpForwardAnim);
                owner.SetOrientation(stickedOrientation != EntityOrientation.Left ? EntityOrientation.Left : EntityOrientation.Right);
            }
            float wallJumpTimer = (float)WallJumpTimerField.GetValue(__instance);
            if (wallJumpTimer > 0f)
            {
                wallJumpTimer -= Time.deltaTime;
                WallJumpTimerField.SetValue(__instance, wallJumpTimer);
                controller.PlatformCharacterPhysics.Acceleration += __instance.transform.up * __instance.WallJumpAcc;
            }
        }
        else
        {
            WallJumpTimerField.SetValue(__instance, controller.JumpingAccTime);
        }

        return false;
    }

    private static Vector3 GetClimbPosition(WallJump instance, Penitent owner, Collider2D climbCollider)
    {
        float x = owner.Status.Orientation != EntityOrientation.Right
            ? climbCollider.bounds.max.x + instance.StickDistanceToWall
            : climbCollider.bounds.min.x - instance.StickDistanceToWall;
        return new Vector2(x, owner.transform.position.y);
    }
}

// P1's own vanilla Stick()/Detach() calls (unaffected by the Prefix above, which only intercepts
// P2's OnUpdate) never register with PlayerLogicBlocker - only the global Core.Input.SetBlocker.
// Same gap already flagged in NOTES.md for every other still-unaudited PLAYER_LOGIC user; closing
// it here the same way Dash/Parry/GrabLadderDown already are, so BlockerOverrideHelper can
// correctly un-freeze the other player while either one is wall-clinging.
[HarmonyPatch(typeof(WallJump), "Stick")]
internal static class WallJump_Stick_BlockerTracking_Patch
{
    private static void Postfix(WallJump __instance)
    {
        PlayerLogicBlocker.SetBlocked(__instance.EntityOwner as Penitent, true);
    }
}

[HarmonyPatch(typeof(WallJump), "Detach")]
internal static class WallJump_Detach_BlockerTracking_Patch
{
    private static void Postfix(WallJump __instance)
    {
        PlayerLogicBlocker.SetBlocked(__instance.EntityOwner as Penitent, false);
    }
}
