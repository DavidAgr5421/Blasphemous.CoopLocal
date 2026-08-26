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

// Round 31 - confirmed root cause of "P2 gets hit by an enemy, and P1 takes the exact same damage
// too, even standing far away from that enemy": Gameplay.GameControllers.Entities.ContactDamage
// (the framework component behind "touch this enemy and take periodic contact damage", used by
// Fool and presumably others) exposes only a single bool IsTargetOverlapped - true while *any*
// entity on DamageableLayers is touching, with no record of *which* one. Enemy-specific attack
// scripts (confirmed for FoolAttack.OnUpdate) then read that bool and, when true, call
// EnemyAttack.ContactAttack(Core.Logic.Penitent) - hardcoded to the P1 singleton, regardless of
// who's actually in contact. So while P2 stands on Fool, P1 takes repeated contact damage every
// ~0.1s no matter how far away P1 physically is - confirmed in [DashParryDebug] logs showing P1
// hit from as far as 24 units away, at the exact frame cadence and damage amounts as P2's own hits,
// with the attacker's own position tracking P2's, never P1's.
//
// Fixed at the shared choke point instead of per-enemy: EnemyAttack.ContactAttack(IDamageable) is
// the base-class method every such enemy attack script ultimately calls into, so patching there
// once covers Fool and any other enemy with the same "IsTargetOverlapped + hardcoded
// Core.Logic.Penitent" shape, without needing to find and reimplement each one's own OnUpdate.
// ContactDamageOverlapTracker independently tracks *which* Penitent(s) are really touching each
// ContactDamage component (via that component's own OnTriggerEnter2D/OnTriggerExit2D - a real,
// per-instance, position-based signal, not the single shared bool). The redirect only ever fires
// when it's positively confirmed P1 is *not* among the real touchers and someone else is - same
// "never redirect by elimination" discipline as BlockerOverrideHelper elsewhere in this file - so
// an untracked/ambiguous case just leaves the original hardcoded call alone rather than guessing.
// Round 34: the redirect above was confirmed working most of the time (log showed the vast
// majority of hardcoded-to-P1 calls correctly redirected to P2), but occasionally still let a
// hardcoded hit through with "nobody tracked as touching", and P1 took the damage anyway - while
// the user's own report (moving P2 away while it's still in its post-hit invulnerability window,
// right after touching a second enemy) points at a timing gap in the tracking itself. Tracking by
// Penitent directly (the original approach) breaks if a Penitent has more than one collider that
// can independently enter/exit this same trigger (a plausible setup - a body collider plus the
// DamageArea's own separate collider, for instance): if one of the two exits while the other is
// still inside, removing "the Penitent" from a HashSet<Penitent> keyed by player wipes out the
// correct "still touching" state contributed by the other, still-overlapping collider. Tracking by
// the actual Collider2D instead (mirroring exactly what ContactDamage's own IsTargetOverlapped
// bool is built from) and deriving "which Penitent(s) are touching" from that set on demand avoids
// this - a Penitent only ever drops out once *all* of its own colliders have actually exited.
internal static class ContactDamageOverlapTracker
{
    private static readonly Dictionary<ContactDamage, HashSet<Collider2D>> overlapping = new Dictionary<ContactDamage, HashSet<Collider2D>>();

    internal static void Add(ContactDamage source, Collider2D collider)
    {
        if (collider == null)
        {
            return;
        }
        if (!overlapping.TryGetValue(source, out HashSet<Collider2D> set))
        {
            set = new HashSet<Collider2D>();
            overlapping[source] = set;
        }
        set.Add(collider);
    }

    internal static void Remove(ContactDamage source, Collider2D collider)
    {
        if (collider == null || !overlapping.TryGetValue(source, out HashSet<Collider2D> set))
        {
            return;
        }
        set.Remove(collider);
    }

    internal static IEnumerable<Penitent> GetOverlapping(ContactDamage source)
    {
        if (!overlapping.TryGetValue(source, out HashSet<Collider2D> set) || set.Count == 0)
        {
            return new Penitent[0];
        }
        HashSet<Penitent> penitents = new HashSet<Penitent>();
        foreach (Collider2D collider in set)
        {
            // A destroyed/disabled collider can linger in the set if its own OnTriggerExit2D never
            // fired (e.g. the GameObject was deactivated instead of physically leaving the
            // trigger) - Unity's "==" on a destroyed object correctly evaluates true against null,
            // so this skips those instead of throwing or resolving a stale Penitent.
            if (collider == null)
            {
                continue;
            }
            Penitent penitent = collider.GetComponentInParent<Penitent>();
            if (penitent != null)
            {
                penitents.Add(penitent);
            }
        }
        return penitents;
    }
}

[HarmonyPatch(typeof(ContactDamage), "OnTriggerEnter2D")]
internal static class ContactDamage_OnTriggerEnter2D_Track_Patch
{
    private static void Postfix(ContactDamage __instance, Collider2D other)
    {
        ContactDamageOverlapTracker.Add(__instance, other);
    }
}

[HarmonyPatch(typeof(ContactDamage), "OnTriggerExit2D")]
internal static class ContactDamage_OnTriggerExit2D_Track_Patch
{
    private static void Postfix(ContactDamage __instance, Collider2D other)
    {
        ContactDamageOverlapTracker.Remove(__instance, other);
    }
}

[HarmonyPatch(typeof(EnemyAttack), nameof(EnemyAttack.ContactAttack))]
internal static class EnemyAttack_ContactAttack_OwnerRedirect_Patch
{
    private static void Prefix(EnemyAttack __instance, ref IDamageable damageable)
    {
        Penitent p1 = Core.Logic.Penitent;
        if (p1 == null || !(damageable is Penitent target) || target != p1)
        {
            // Only ever intervenes on the exact bug shape - a call hardcoded to P1. Any other
            // target (P2, an enemy, anything else IDamageable) is left completely alone.
            return;
        }

        // NOT __instance.GetComponentInChildren<ContactDamage>() - confirmed by testing to find
        // nothing and silently no-op the whole patch. FoolAttack.OnStart() resolves its own
        // ContactDamage reference via Fool.GetComponentInChildren<ContactDamage>() (Fool being
        // base.EntityOwner, the shared Entity root), not from FoolAttack's own transform - meaning
        // ContactDamage lives as a *sibling* component under the enemy's root, not a descendant of
        // the Attack component's own GameObject. Mirroring that exact resolution path here instead.
        if (__instance.EntityOwner == null)
        {
            DashParryDebugLog.Log($"ContactAttack redirect: no EntityOwner on {__instance.GetType().Name} (frame {Time.frameCount})");
            return;
        }
        ContactDamage contactDamage = __instance.EntityOwner.GetComponentInChildren<ContactDamage>();
        if (contactDamage == null)
        {
            DashParryDebugLog.Log($"ContactAttack redirect: no ContactDamage found under {__instance.EntityOwner.name} (frame {Time.frameCount})");
            return;
        }

        bool p1Touching = false;
        Penitent otherTouching = null;
        foreach (Penitent touching in ContactDamageOverlapTracker.GetOverlapping(contactDamage))
        {
            if (touching == p1)
            {
                p1Touching = true;
            }
            else
            {
                otherTouching = touching;
            }
        }

        if (otherTouching == null)
        {
            // Nobody else tracked as touching (including the untracked/ambiguous case) - leave the
            // original call alone, matching vanilla/solo-play behavior exactly.
            DashParryDebugLog.Log(
                $"ContactAttack redirect: hardcoded-to-P1 call from {__instance.EntityOwner.name}, but nobody tracked as " +
                $"touching {contactDamage.gameObject.name} - leaving as-is (frame {Time.frameCount})");
            return;
        }
        if (p1Touching)
        {
            // Both are genuinely touching at once - let the original call through for P1 as-is,
            // and separately attack the other player for real instead of dropping their hit.
            DashParryDebugLog.Log($"ContactAttack redirect: both P1 and {DashParryDebugLog.Label(otherTouching)} touching {contactDamage.gameObject.name} - hitting both (frame {Time.frameCount})");
            __instance.ContactAttack(otherTouching);
            return;
        }
        DashParryDebugLog.Log($"ContactAttack redirect: P1 NOT touching {contactDamage.gameObject.name}, redirecting hardcoded hit to {DashParryDebugLog.Label(otherTouching)} (frame {Time.frameCount})");
        damageable = otherTouching;
    }
}

// Ability (the base class behind Dash, Parry, Combo, VerticalAttack, and every other
// cast-based skill) has its own *generic* input dispatcher, completely separate from anything
// PlatformCharacterInput does:
//
//   private void UpdateInput()
//   {
//       if ((bool)EntityOwner && Rewired != null && EntityOwner.CompareTag("Penitent"))
//       {
//           if (Rewired.GetButtonDown(triggerCode)) Cast();
//           if (Rewired.GetButtonUp(triggerCode)) StopCast();
//       }
//   }
//
// This runs every frame for every Ability component on every Penitent - including P2's, since
// `Rewired` here is (as always) just ReInput.players.GetPlayer(0), the same shared Player 0.
// So whenever P1 presses their own real Dash/Parry/etc. button, this fires Cast()/StopCast()
// on P2's *own* Dash/Parry ability too, racing against whatever we triggered for P2 through
// PlatformCharacterInput.Dash / the ParryInput patch. That race is what produced the "both
// dash at once -> P2 crouches instead, both get stuck" bug: two independent paths both calling
// Cast()/StopCast() on the same ability in the same window, leaving castTime/animator state
// half-updated.
//
// P2's own casting is already fully covered elsewhere (Dash via AnimatorInyector reading
// PlatformCharacterInput.Dash, Parry via the ParryInput patch above), so this dispatcher adds
// nothing for P2 except cross-talk from P1's buttons - safe to disable outright for any Ability
// living on P2. Abilities we haven't explicitly wired for P2 yet simply won't be castable
// through this path either, which is a gap to close later, not a new bug.
[HarmonyPatch(typeof(Ability), "UpdateInput")]
internal static class Ability_UpdateInput_Patch
{
    private static bool Prefix(Ability __instance)
    {
        return __instance.EntityOwner != CoopLocal.Player2;
    }
}


