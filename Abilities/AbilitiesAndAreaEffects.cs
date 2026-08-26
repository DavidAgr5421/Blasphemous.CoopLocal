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

// CrouchAttackBehaviour and CrouchUpBehaviour are two more Animator states in the same crouch
// state graph as CrouchDownBehaviour (already fixed above) - both with their own separate
// _penitent field, both subject to the identical bug. CrouchAttackBehaviour is the one that
// actually matters most: it's the state CrouchDownBehaviour transitions into when the crouch-
// attack key is pressed, and its OnStateEnter/OnStateUpdate is what raises the attack event
// (_penitent.AnimatorInyector.RaiseAttackEvent()), sets the damage amount
// (_penitent.CurrentOutputDamage), and toggles _penitent.IsCrouchAttacking /
// _penitent.PlatformCharacterInput.IsAttacking. On P2's first ever crouch-attack, an unfixed
// _penitent here resolves to P1 - so P2's crouch-attack animation plays, but the actual attack
// (damage, hitbox event) fires as if P1 had done it, while P1's own IsCrouchAttacking/IsAttacking
// get set to true out of nowhere - which, since PlatformCharacterInput.IsHorizontalClamped()
// includes IsAttacking in its clamp check, would also zero out P1's own movement for the
// duration. This is almost certainly why "P2 couldn't attack while P1 was crouched" persisted
// even after the CrouchDownBehaviour fix: that fix only handles entering/staying in the Crouch
// state itself, not the separate Crouch Attack state it hands off to.
[HarmonyPatch(typeof(CrouchAttackBehaviour), "OnStateEnter")]
internal static class CrouchAttackBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

[HarmonyPatch(typeof(CrouchUpBehaviour), "OnStateEnter")]
internal static class CrouchUpBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// HangOnCliffLedeBehaviour and ClimbCliffLedeBehaviour are the two Animator states that make up
// cliff-ledge climbing ("cornisas") - both with the same unfixed _penitent-falls-back-to-P1 bug.
// On P2's first attempt to climb a ledge, HangOnCliffLedeBehaviour.OnStateEnter resolves
// _penitent to P1 and then does everything (IsClimbingCliffLede = true, canClimbCliffLede,
// disabling P1's 2D collision/physics, snapping P1's position to the ledge's root target...) to
// *P1* instead of P2 - meaning P2's own climb never actually starts (P2.IsClimbingCliffLede stays
// false) while P1 gets silently frozen/teleported. Same fix as every other case above.
[HarmonyPatch(typeof(HangOnCliffLedeBehaviour), "OnStateEnter")]
internal static class HangOnCliffLedeBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

[HarmonyPatch(typeof(ClimbCliffLedeBehaviour), "OnStateEnter")]
internal static class ClimbCliffLedeBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator, ref Penitent ____penitent)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// GrabCliffLede (the MonoBehaviour, one per Penitent, whose trigger collider actually *detects*
// a climbable ledge and starts the grab) has a variant of the same bug that's even more direct:
// its Start() does an *unconditional* `_penitent = Core.Logic.Penitent;` (no null-check guard),
// so P2's own copy of this component always points at P1, no matter what. Every ledge P2 walks
// into is evaluated against and applied to P1's state (_penitent.IsGrabbingCliffLede,
// .CliffLedeOrientation, .RootTargetPosition, .IsJumpingOff, .IsDashing, .Status.IsGrounded...) -
// P2 can never climb a ledge at all, since nothing ever sets P2's own IsGrabbingCliffLede.
// Because the assignment is unconditional, a Prefix can't just pre-set the field (the original
// method would immediately overwrite it back to Core.Logic.Penitent) - this corrects it
// afterwards instead. The one known gap: Start() also subscribes this component's damage
// handler to _penitent.DamageArea.OnDamaged *before* this Postfix runs, so on P2's instance that
// subscribes to P1's DamageArea instead of P2's - harmless for now since P2 takes no real damage
// at all (see PenitentDamageArea_TakeDamage_Patch below), but worth revisiting if P2 ever gets
// real health.
[HarmonyPatch(typeof(GrabCliffLede), "Start")]
internal static class GrabCliffLede_Start_Patch
{
    private static void Postfix(GrabCliffLede __instance, ref Penitent ____penitent)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            ____penitent = owner;
        }
    }
}

// MudAreaEffect (the slow-down trigger zone for mud/swamp terrain) caches the *last entity
// that entered* in single shared fields (Controller/Dash/Animator, plus the "default" values
// it read from them) instead of tracking each entity in the zone separately. With two players
// able to be in the mud at once, whichever one entered last "wins" that cache; ApplyMudEffects
// (every frame anyone stays in the zone) only ever touches that one cached controller, and
// OnExitAreaEffect's reset-to-default step does the same regardless of which entity (`other`)
// is actually the one leaving - so leaving the mud can silently reset the wrong player, or
// reset the right one using stale/wrong "default" values, leaving them stuck with the mud's
// reduced jump/walk speed permanently.
//
// There's a second, independent bug on top of that one, in the *base* AreaEffect class: its
// OnTriggerExit2D sets the whole zone's IsPopulated = false the instant ANY ONE occupant leaves,
// and OnUpdate() only calls OnStayAreaEffect (the periodic mud re-apply) while IsPopulated is
// true - so the moment either player leaves a mud patch the other player is still standing in,
// the periodic re-application stops firing for them entirely. Combined with the single-entity
// cache above, this is what produces the reported "sometimes the horizontal slowdown just
// disappears, sometimes you can suddenly jump normally again" - it happens whenever the OTHER
// player crosses the mud's edge (leaving, or re-entering and re-winning the cache), not from
// dashing specifically; dashing just makes reaching that edge far more likely; a normal walk
// across the same boundary would trigger it too.
//
// Population (the list of GameObjects currently inside, on the base AreaEffect class) - unlike
// Controller/Dash/Animator/IsPopulated - IS tracked correctly per-entity by AddEntityToArea
// Population/RemoveEntityToAreaPopulation, so all three fixes below key off that list directly
// instead of trusting the single-entity cache or the zone-wide IsPopulated flag.
[HarmonyPatch(typeof(AreaEffect), "OnTriggerExit2D")]
internal static class AreaEffect_OnTriggerExit2D_Patch
{
    // Scoped to MudAreaEffect only - other AreaEffect subclasses (poison, wind, etc.) haven't
    // been reported broken and haven't been audited for the same two-occupant issue.
    private static void Postfix(AreaEffect __instance, List<GameObject> ___Population)
    {
        if (__instance is MudAreaEffect && ___Population.Count > 0)
        {
            __instance.IsPopulated = true;
        }
    }
}

// Replaces the single-cache periodic mud application with one that walks every entity actually
// in Population and applies this zone's mud values directly to each of them, every tick -
// completely independent of whichever entity OnEnterAreaEffect's shared cache last happened to
// point at.
[HarmonyPatch(typeof(MudAreaEffect), "OnStayAreaEffect")]
internal static class MudAreaEffect_OnStayAreaEffect_Patch
{
    private static bool Prefix(MudAreaEffect __instance, List<GameObject> ___Population)
    {
        foreach (GameObject populant in ___Population)
        {
            Entity entity = populant.GetComponentInParent<Entity>();
            if (entity == null)
            {
                continue;
            }

            PlatformCharacterController controller = entity.GetComponentInChildren<PlatformCharacterController>();
            if (controller == null)
            {
                continue;
            }

            controller.JumpingSpeed = __instance.JumpingSpeed;
            controller.WalkingDrag = __instance.WalkingDrag;
            controller.WalkingAcc = __instance.WalkingAcceleration;
            controller.MaxWalkingSpeed = __instance.MaxWalkingSpeed;

            Dash dash = entity.GetComponentInChildren<Dash>();
            if (dash != null)
            {
                dash.DashMoveSetting.Speed = __instance.DashSettings.Speed;
                dash.DashMoveSetting.Drag = __instance.DashSettings.Drag;
                if (entity.Animator != null)
                {
                    entity.Animator.speed = entity.Animator.GetCurrentAnimatorStateInfo(0).IsName("Run") ? 0.7f : 1f;
                }
            }
        }

        return false;
    }
}

// Rather than rewrite MudAreaEffect's whole caching scheme, this keeps its own reliable
// per-controller baseline (captured once, right at spawn, before either player could ever
// have touched mud) and reapplies it directly to whichever Penitent actually triggered
// OnExitAreaEffect, overriding whatever the buggy shared-cache logic just did. It also
// re-applies this zone's mud values to every player still left in Population right afterwards,
// undoing any collateral damage the original method's reset-to-default step may have just done
// to whichever player its stale single-entity cache happened to be pointing at.
[HarmonyPatch(typeof(MudAreaEffect), "OnExitAreaEffect")]
internal static class MudAreaEffect_OnExitAreaEffect_Patch
{
    private readonly struct Baseline(float jumpingSpeed, float walkingDrag, float walkingAcc, float maxWalkingSpeed, float dashSpeed, float dashDrag)
    {
        public readonly float JumpingSpeed = jumpingSpeed;
        public readonly float WalkingDrag = walkingDrag;
        public readonly float WalkingAcc = walkingAcc;
        public readonly float MaxWalkingSpeed = maxWalkingSpeed;
        public readonly float DashSpeed = dashSpeed;
        public readonly float DashDrag = dashDrag;
    }

    private static readonly Dictionary<Penitent, Baseline> Baselines = new Dictionary<Penitent, Baseline>();

    // Called from CoopLocal right after a Penitent spawns, before it could possibly have
    // touched any mud yet, so these values are guaranteed clean.
    internal static void RememberBaseline(Penitent penitent)
    {
        PlatformCharacterController controller = penitent.PlatformCharacterController;
        Dash dash = penitent.GetComponentInChildren<Dash>();
        Baselines[penitent] = new Baseline(
            controller.JumpingSpeed,
            controller.WalkingDrag,
            controller.WalkingAcc,
            controller.MaxWalkingSpeed,
            dash != null ? dash.DashMoveSetting.Speed : 0f,
            dash != null ? dash.DashMoveSetting.Drag : 0f);
    }

    private static void Postfix(MudAreaEffect __instance, Collider2D other, List<GameObject> ___Population)
    {
        Penitent owner = other.GetComponentInParent<Penitent>();
        if (owner != null && Baselines.TryGetValue(owner, out Baseline baseline))
        {
            PlatformCharacterController controller = owner.PlatformCharacterController;
            controller.JumpingSpeed = baseline.JumpingSpeed;
            controller.WalkingDrag = baseline.WalkingDrag;
            controller.WalkingAcc = baseline.WalkingAcc;
            controller.MaxWalkingSpeed = baseline.MaxWalkingSpeed;

            Dash dash = owner.GetComponentInChildren<Dash>();
            if (dash != null)
            {
                dash.DashMoveSetting.Speed = baseline.DashSpeed;
                dash.DashMoveSetting.Drag = baseline.DashDrag;
            }
        }

        // Population no longer contains the exiting entity by this point (AreaEffect.
        // OnTriggerExit2D removes it before calling OnExitAreaEffect) - whoever's left here is
        // still physically standing in the mud and must keep their debuff, regardless of what
        // the original method's single-entity cache just reset.
        foreach (GameObject populant in ___Population)
        {
            Entity remaining = populant.GetComponentInParent<Entity>();
            if (remaining == null)
            {
                continue;
            }

            PlatformCharacterController remainingController = remaining.GetComponentInChildren<PlatformCharacterController>();
            if (remainingController == null)
            {
                continue;
            }

            remainingController.JumpingSpeed = __instance.JumpingSpeed;
            remainingController.WalkingDrag = __instance.WalkingDrag;
            remainingController.WalkingAcc = __instance.WalkingAcceleration;
            remainingController.MaxWalkingSpeed = __instance.MaxWalkingSpeed;

            Dash remainingDash = remaining.GetComponentInChildren<Dash>();
            if (remainingDash != null)
            {
                remainingDash.DashMoveSetting.Speed = __instance.DashSettings.Speed;
                remainingDash.DashMoveSetting.Drag = __instance.DashSettings.Drag;
            }
        }
    }
}


