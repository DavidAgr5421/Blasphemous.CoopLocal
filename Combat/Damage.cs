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

// P2 used to be made invulnerable here rather than wired into the death/respawn flow (see
// Modding/NOTES.md history) - a Prefix no-op'd TakeDamage for P2 entirely. Per the user's request
// for P2 to have its own real health pool, that skip is removed: P2 now takes damage through the
// exact same code path P1 always has. This is safe to just turn on, for two reasons already
// established earlier in this file: (1) the component itself is never destroyed (the historical
// reason invulnerability was added this way instead of destroying PenitentDamageArea outright -
// ~108 places in the game's own code call methods on Penitent.DamageArea assuming it always
// exists), so nothing here changes; (2) Stats.Life is a genuinely per-instance value
// (VariableAttribute's constructor sets Current = baseValue, i.e. the prefab's own serialized
// starting-life field) - P2 already has its own separate, correctly-initialized life pool the
// moment it spawns, no extra setup needed. If P2's own Status.Dead ever becomes true,
// Penitent.OnUpdate() (completely unmodified, runs per-instance for both P1 and P2 alike) already
// calls Core.Logic.SetState(LogicStates.PlayerDead) exactly like it does when P1 dies - so either
// player dying ends the run the same way solo play always has, entirely for free.
//
// One thing this does need to guard against: PenitentDamageArea.RaiseDamageEvent unconditionally
// writes `_logicManager.PlayerCurrentLife = _penitent.Stats.Life.Current;` - a single global value
// that looked, at first glance, like what the HUD's health bar reads to decide what to display.
// With P2 now able to take real damage, its hits would stomp this with *P2's* life number. See
// PenitentDamageArea_RaiseDamageEvent_HudFix_Patch below for the fix - kept even after confirming
// (decompiling Gameplay.UI.Others.UIGameLogic.PlayerHealth) that P1's actual on-screen bar reads
// Core.Logic.Penitent.Stats.Life directly, never LogicManager.PlayerCurrentLife, so this specific
// write was never the cause of any observed HUD bug. Left in place in case something else in the
// game's own code does read PlayerCurrentLife (unconfirmed either way) - harmless either way,
// since it's just restoring the value to what it already should be.
//
// Known limitation, not fixed here: P2 starts at its own LifeBase (a fresh-save starting value),
// not P1's current (possibly upgraded) max life - the two pools aren't kept in sync with whatever
// life-upgrade items P1 has collected during the playthrough. Revisit if that turns out to matter.
// Diagnostic for the round-30 report "hitting P2 after its invulnerability window ends damages
// *both* players from what looks like one hit". Logs every real TakeDamage call that gets past
// the early-out guards (CanTakeHit/recover-time), tagged with which player's own DamageArea it
// ran on, the hit's source, and a frame number - so a genuine "one enemy swing tagging both
// players' separate, real DamageArea colliders because they're standing in the same spot"
// (expected: P1 and P2 have no collision between them, per CoopLocal.OnPlayerSpawn, so nothing
// stops them occupying the same space) can be told apart from an actual bug (e.g. two calls
// against the same instance, or a call whose owner doesn't match the DamageArea it ran on) just
// by reading the timestamps and owners next to each other in the log.
//
// Round 31: the first log confirmed each hit only reduces the correct player's own Life.Current
// (no shared/duplicated line ever appeared), and P1/P2 hits from the same enemy landed a handful
// of frames apart, not the same frame - consistent with "both standing near the same enemy, two
// separate real hits". The user then said P1 was reportedly *far* from the enemy when this
// happened, which the "standing together" theory doesn't explain - so positions (owner, the other
// player, and the attacker, when available) are now logged alongside the life numbers, to settle
// with actual distances instead of guessing further.
[HarmonyPatch(typeof(PenitentDamageArea), "TakeDamage")]
internal static class PenitentDamageArea_TakeDamage_DebugLog_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentDamageArea), "_penitent");

    private static float lifeBefore;
    private static bool unattacableBefore;
    private static bool invulnerableBefore;
    private static bool isHurtBefore;

    // Round 48: user reports P2 taking damage specifically while performing an upward/side
    // attack ("parece daño por contacto al atacar hacia arriba o al lado") - a fresh live log
    // showed 13 of 16 P2 damage events landing exactly 1-2 frames after P2 entered
    // "Player_Upward_Attack_Clamped_anim" specifically. PenitentDamageArea.TakeDamage/CanTakeHit
    // are both confirmed correctly per-instance already (no hardcoded Core.Logic.Penitent
    // anywhere in that chain), and GroundHurtBehaviour/AirHurtBehaviour's own owner-fix patches
    // (which set Status.Unattacable during the post-hit invulnerability window) were checked and
    // are structurally correct too - no code-level cause has been confirmed yet, so capturing
    // Unattacable/Invulnerable/IsHurt state *before* the hit resolves (Prefix) is the next
    // concrete thing needed: either these flags were already true and got bypassed somehow (a
    // real bug), or they were genuinely false (meaning this really is just BellGhost's own attack
    // landing at the same moment the player swings - ordinary difficulty, not a mod bug).
    private static void Prefix(PenitentDamageArea __instance)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        lifeBefore = owner != null ? owner.Stats.Life.Current : -1f;
        unattacableBefore = owner != null && owner.Status.Unattacable;
        invulnerableBefore = owner != null && owner.Status.Invulnerable;
        isHurtBefore = owner != null && owner.Status.IsHurt;
    }

    // Only logs when Life.Current actually changed - TakeDamage has several early-out guards
    // (CanTakeHit, recover-time window) that make it return without applying anything, and a
    // Postfix fires regardless of which path was taken inside. Comparing life before/after is a
    // reliable way to tell "damage genuinely landed" apart from a no-op call, without needing to
    // duplicate TakeDamage's own gating logic here.
    private static void Postfix(PenitentDamageArea __instance, Gameplay.GameControllers.Entities.Hit hit)
    {
        Penitent owner = PenitentField.GetValue(__instance) as Penitent;
        float lifeAfter = owner != null ? owner.Stats.Life.Current : -1f;
        if (Mathf.Approximately(lifeAfter, lifeBefore))
        {
            return;
        }
        string ownerLabel = DashParryDebugLog.Label(owner);
        string attackerName = hit.AttackingEntity != null ? hit.AttackingEntity.name : "null";

        Penitent p1 = Core.Logic.Penitent;
        Penitent p2 = CoopLocal.Player2;
        Penitent other = (owner == p2) ? p1 : p2;
        string ownerPos = owner != null ? owner.transform.position.ToString("F1") : "?";
        string otherLabel = DashParryDebugLog.Label(other);
        string otherPos = other != null ? other.transform.position.ToString("F1") : "?";
        float distanceToOther = (owner != null && other != null) ? Vector3.Distance(owner.transform.position, other.transform.position) : -1f;
        string attackerPos = hit.AttackingEntity != null ? hit.AttackingEntity.transform.position.ToString("F1") : "?";

        DashParryDebugLog.Log(
            $"PenitentDamageArea.TakeDamage APPLIED on {ownerLabel} (instance={__instance.GetInstanceID()}) from attacker='{attackerName}' " +
            $"damageType={hit.DamageType} lifeBefore={lifeBefore:F1} lifeAfter={lifeAfter:F1} " +
            $"unattacableBefore={unattacableBefore} invulnerableBefore={invulnerableBefore} isHurtBefore={isHurtBefore} | {ownerLabel}Pos={ownerPos} " +
            $"{otherLabel}Pos={otherPos} distanceToOther={distanceToOther:F1} attackerPos={attackerPos} (frame {Time.frameCount})");
    }
}

[HarmonyPatch(typeof(PenitentDamageArea), "RaiseDamageEvent")]
internal static class PenitentDamageArea_RaiseDamageEvent_HudFix_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PenitentDamageArea), "_penitent");
    private static readonly FieldInfo LogicManagerField = AccessTools.Field(typeof(PenitentDamageArea), "_logicManager");

    private static void Postfix(object __instance)
    {
        Penitent penitent = PenitentField.GetValue(__instance) as Penitent;
        if (penitent == null || penitent != CoopLocal.Player2)
        {
            return;
        }
        Penitent p1 = Core.Logic.Penitent;
        LogicManager logicManager = LogicManagerField.GetValue(__instance) as LogicManager;
        if (p1 != null && logicManager != null)
        {
            logicManager.PlayerCurrentLife = p1.Stats.Life.Current;
        }
    }
}


