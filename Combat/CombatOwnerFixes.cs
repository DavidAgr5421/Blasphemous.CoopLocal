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

// AttackBehaviour bundles `_penitentAttackArea = _penitent.PenitentAttack.CurrentPenitentWeapon
// .AttackAreas[0];` inside the same "if (_penitent == null)" guard as _penitent itself - so the
// fix has to replicate BOTH assignments together, against the real owner, the first time (and
// only the first time, matching the original's once-only intent) this instance's _penitent is
// still unset. A plain "always overwrite _penitent" Prefix would make the original's own guard
// permanently see it as already-set and skip _penitentAttackArea forever, which is exactly what the
// generic scanner did and crashed on (see comment above).
[HarmonyPatch(typeof(AttackBehaviour), "OnStateEnter")]
internal static class AttackBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(AttackBehaviour), "_penitent");
    private static readonly FieldInfo AttackAreaField = AccessTools.Field(typeof(AttackBehaviour), "_penitentAttackArea");

    private static void Prefix(AttackBehaviour __instance, Animator animator)
    {
        if (PenitentField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentField.SetValue(__instance, owner);
        AttackAreaField.SetValue(__instance, owner.PenitentAttack.CurrentPenitentWeapon.AttackAreas[0]);
    }
}

// Same shape of fix for HurtSubStateBehaviour, which bundles
// `_throwBack = _penitent.GetComponentInChildren<ThrowBack>();` inside its own _penitent guard -
// confirmed crashing (NullReferenceException on _throwBack.Casting in OnStateEnter) under the
// generic scanner the first time either player got hurt.
[HarmonyPatch(typeof(HurtSubStateBehaviour), "OnStateEnter")]
internal static class HurtSubStateBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(HurtSubStateBehaviour), "_penitent");
    private static readonly FieldInfo ThrowBackField = AccessTools.Field(typeof(HurtSubStateBehaviour), "_throwBack");

    private static void Prefix(HurtSubStateBehaviour __instance, Animator animator)
    {
        if (PenitentField.GetValue(__instance) != null)
        {
            return;
        }

        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        PenitentField.SetValue(__instance, owner);
        ThrowBackField.SetValue(__instance, owner.GetComponentInChildren<ThrowBack>());
    }
}

// GroundHurtBehaviour and AirHurtBehaviour - the two StateMachineBehaviours actually entered when
// a hit lands (children states of the sub-state machine HurtSubStateBehaviour dispatches into,
// grounded vs airborne) - have the exact same simple "_penitent falls back to Core.Logic.Penitent"
// bug as everything else in this family, just never audited/patched until now. Neither bundles a
// second field init inside its own null-check (read in full before writing this - see the trap
// comment above), so the plain preset-in-Prefix fix is safe here. This is very likely the real
// cause of "damage/knockback still happens to P1 when P2 gets hit": the first time P2's own
// GroundHurtBehaviour/AirHurtBehaviour instance ever runs, its _penitent resolves to P1 and stays
// wrong forever - so every later hit P2 takes calls _penitent.DamageArea.HitDisplacement(...),
// sets _penitent.Status.Unattacable = true (a brief invulnerability window), stops
// _penitent.MotionLerper, etc. on *P1*, not on P2 - even though the underlying life-number
// reduction (Entity.Damage, via PenitentDamageArea.RaiseDamageEvent) is correctly per-instance and
// already only affects the player who was actually hit.
[HarmonyPatch(typeof(GroundHurtBehaviour), "OnStateEnter")]
internal static class GroundHurtBehaviour_OnStateEnter_Patch
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

[HarmonyPatch(typeof(AirHurtBehaviour), "OnStateEnter")]
internal static class AirHurtBehaviour_OnStateEnter_Patch
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

// Fervour turned out to have its own, different-shaped bug: PenitentSword.OnEnemyDamaged is
// subscribed to EnemyDamageArea.OnDamagedGlobal - a *static* event, combined via Delegate.Combine
// in OnAwake - the exact same family-3 pattern already found and fixed once for GrabLadder's
// subscription to FloorDistanceChecker.OnStepLadder (see Modding/NOTES.md). Both P1's and P2's own
// PenitentSword instances subscribe their own instance method to this one shared event, so *every*
// enemy hit - dealt by either player - invokes *both* players' OnEnemyDamaged. Each call
// unconditionally grants its own _penitent Fervour (_penitent.IncrementFervour(hit) - Fervour
// itself is genuinely per-instance, see NOTES.md) and pokes the shared
// Core.InventoryManager.OnDamageInflicted(hit) tracker - so landing one hit with P2 also grants
// Fervour to P1 (and vice versa), and the inventory/on-hit-effect tracker fires twice per hit
// instead of once. PenitentSword's own _penitent (GetComponentInParent<Penitent>() in OnAwake) is
// already correctly per-instance - the missing piece is that the callback never checks whether the
// Hit it received was actually dealt by *its own* _penitent. hit.AttackingEntity is set by
// PenitentAttack (PenitentAttack._penitent, resolved from base.EntityOwner - correctly
// per-instance) to the attacker's own gameObject, so comparing against that is enough to tell
// "my hit" from "the other player's hit" without any new tracking state.
[HarmonyPatch(typeof(PenitentSword), "OnEnemyDamaged")]
internal static class PenitentSword_OnEnemyDamaged_OwnerFilter_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentSword), "_penitent");

    private static bool Prefix(PenitentSword __instance, Gameplay.GameControllers.Entities.Hit hit)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        if (owner == null || hit.AttackingEntity == null)
        {
            return true;
        }
        return hit.AttackingEntity == owner.gameObject;
    }
}

// Found while investigating round 30/31's damage-sharing reports (not confirmed to be their
// cause, but a real bug in its own right): Penitent.OnAwake subscribes each instance's own
// OnEntityDead to the *static* Entity.Death event - same family-3 shape as PenitentSword above,
// just on a different event. Entity.Death fires for *any* entity dying, players included, so when
// P2 dies, *both* P1's and P2's own OnEntityDead handlers run with entity=P2. The enemy-death
// branch (Purge gain) is harmless either way since `entity as Enemy` is null for a dead Penitent -
// but the player-death branch runs unconditionally on `this` (EnableAbilities(false),
// EnableTraits(false), DamageArea.IncludeEnemyLayer(false)), regardless of whether `this` is the
// player who actually died. So P2 dying was also disabling P1's own abilities/traits (and vice
// versa) - a real, separate cross-talk bug, distinct from the damage/Fervour ones already fixed.
// Filtered the same way: skip the whole method when the Entity that died is a Penitent that isn't
// this instance - the enemy-death branch is untouched (still fires for every player on every
// enemy kill, matching solo-play Purge behavior, since that wasn't reported as a problem).
[HarmonyPatch(typeof(Penitent), "OnEntityDead")]
internal static class Penitent_OnEntityDead_OwnerFilter_Patch
{
    private static bool Prefix(Penitent __instance, Entity entity)
    {
        Penitent diedPenitent = entity as Penitent;
        if (diedPenitent != null && diedPenitent != __instance)
        {
            return false;
        }
        return true;
    }
}


