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

// PrayerUse (the "activate equipped prayer" ability, distinct from Healing above) has no
// dedicated input method of its own the way Healing does - it relies entirely on the base
// Ability's generic UpdateInput() dispatcher, which Ability_UpdateInput_Patch (further down this
// file) disables outright for P2 (see that patch's own comment - "abilities we haven't
// explicitly wired for P2 yet simply won't be castable"). So P2's own PrayerUse currently never
// casts at all. Wired here the same way Dash/Parry/Healing were: a dedicated per-instance check
// reading P2's own input instead of the disabled generic path - Postfixed onto OnUpdate() (runs
// every frame per-instance already) rather than UpdateInput() itself, since that stays
// intentionally disabled for P2 by the patch further down. P1's own instance is untouched - its
// PrayerUse still casts through the normal generic dispatcher exactly as before.
[HarmonyPatch(typeof(PrayerUse), "OnUpdate")]
internal static class PrayerUse_P2Input_Patch
{
    private static readonly FieldInfo CastInformationField = AccessTools.Field(typeof(Ability), "castInformation");

    // Round 39 follow-up: __instance.CanUsePrayer compiles fine against the NuGet reference
    // assembly (which marks it public) but the REAL shipped Assembly-CSharp.dll has the getter as
    // non-public - calling it directly threw a runtime MethodAccessException ("get_CanUsePrayer is
    // inaccessible"), confirmed via LogOutput.log. AccessTools.Property + PropertyInfo.GetValue
    // bypasses the compile-time accessibility check, same trick already relied on throughout this
    // file for private fields.
    private static readonly PropertyInfo CanUsePrayerProperty = AccessTools.Property(typeof(PrayerUse), "CanUsePrayer");

    // Round 38: static analysis of Ability.Cast()/PrayerUse.OnCastStart()/StartUsingPrayer() all
    // came back correctly per-instance (EntityOwner/_penitent-based throughout, no hardcoded
    // Core.Logic.Penitent found anywhere in that chain) - yet the user reports the prayer effect
    // visibly originating from P1 and consuming *neither* player's Fervour when triggered from
    // P2. Since nothing in the code this patch can see explains that, logging Cast()'s own
    // castInformation field (a string Ability.Cast() itself sets to exactly why it
    // succeeded/failed - "SUCCESSFULLY EXECUTED", "ALREADY CASTING", "ABILITY NOT READY",
    // "CONDITION NOT MET", "ENTITY DEAD", "ABILITIES DISABLED", "INVALID OWNER" - see its
    // decompiled source) plus P2's own Fervour before/after, to find out directly which of those
    // it actually is rather than guessing further blind. The specific prayer effect classes
    // (multishotPrayer/lightBeamPrayer/shieldPrayer/cherubPrayer/etc, all boss-attack classes
    // reused for player prayers) haven't been individually audited for their own owner/position
    // logic yet - if castInformation comes back "SUCCESSFULLY EXECUTED" with Fervour genuinely
    // dropping on P2, the bug is in one of *those* classes instead, not in PrayerUse itself.
    private static void Postfix(PrayerUse __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }
        if (Player2Input.PrayerActivateDown)
        {
            // Round 39: PrayerUse.get_CanUsePrayer (non-virtual, decompiled from the real shipped
            // Assembly-CSharp.dll) is the property that actually checks fervourNeeded against the
            // per-instance _penitent.Stats.Fervour.Current before P1 is allowed to cast - it was
            // never being consulted here, so P2's Cast() fired unconditionally regardless of
            // P2's own Fervour, and with no floor P2's Fervour could go arbitrarily negative.
            // Gating on it here mirrors P1's real logic exactly ("misma logica que rezo P1").
            bool canUsePrayer = (bool)CanUsePrayerProperty.GetValue(__instance, null);
            if (!canUsePrayer)
            {
                return;
            }
            float fervourBefore = owner.Stats.Fervour.Current;
            __instance.Cast();
            string info = (string)CastInformationField.GetValue(__instance);
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[PrayerUse] P2 Cast() -> castInformation='{info}', P2 Fervour {fervourBefore:F1} -> {owner.Stats.Fervour.Current:F1}, " +
                    $"equippedPrayer={(__instance.GetEquippedPrayer() != null ? __instance.GetEquippedPrayer().name : "null")}",
                    Main.CoopLocal);
            }
        }
        if (Player2Input.PrayerActivateUp)
        {
            __instance.StopCast();
        }
    }
}

// Round 43: found the actual cause of "el origen del rezo es en P1" - PrayerUse itself
// (Cast()/OnCastStart()/StartUsingPrayer()) is genuinely per-instance and correctly casts from
// whichever Penitent owns it (confirmed since Fervour drains correctly from P2's own pool). But
// StartUsingPrayer() ends by calling `prayer.Use()` on the equipped Prayer *item* - a single
// object shared game-wide (there's only one "equipped prayer" inventory entry, not one per
// Penitent) - which does `SendMessage("OnUseInventoryObject")`. The specific prayer-power effect
// classes that receive that message (decompiled via ICSharpCode.Decompiler from the real
// Assembly-CSharp.dll) each independently hardcode `_owner = Core.Logic.Penitent;` as their own
// first line - the exact same "wrong owner" bug class found ~50 times already this session in
// AnimationBehaviours, just living in a completely different part of the codebase
// (Framework.Inventory's ObjectEffect system) that a per-Penitent-component scan would never
// reach. Since the shared Prayer item has no way to know who actually triggered it, this patch
// tracks the real caster itself: a Prefix on PrayerUse's own (already correctly per-instance)
// StartUsingPrayer() records `_penitent` into a static field *before* prayer.Use() fires the
// SendMessage chain - by the time OnApplyEffect() runs (synchronously, same call stack), the
// tracker reliably holds the real caster.
internal static class PrayerCasterTracker
{
    internal static Penitent LastCaster;
}

[HarmonyPatch(typeof(PrayerUse), "StartUsingPrayer")]
internal static class PrayerUse_StartUsingPrayer_TrackCaster_Patch
{
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(PrayerUse), "_penitent");

    private static void Prefix(PrayerUse __instance)
    {
        PrayerCasterTracker.LastCaster = (Penitent)PenitentField.GetValue(__instance);
    }
}

// PrayerAlliedCherubEffect/PrayerShieldEffect both derive from ObjectEffect_Stat and end their
// OnApplyEffect/OnRemoveEffect with `base.OnApplyEffect()`/`base.OnRemoveEffect()` - a generic
// stat-bonus applier that *also* hardcodes Core.Logic.Penitent internally. Reflection can't safely
// invoke "just the base implementation" here (MethodInfo.Invoke on a virtual method always
// re-dispatches to the most-derived override via the CLR's normal vtable lookup, regardless of
// which declaring type's MethodInfo was used to look it up - invoking it from inside this very
// Prefix would recurse into itself). Rather than risk a broken reimplementation of
// ObjectEffect_Stat's full logic (PenitencePE02 special-casing, RawBonus tracking, etc) blind,
// this patch fixes only the part the user actually reported - the visible cherub/shield spawn
// itself - and deliberately skips (via `return false`) the inherited stat-bonus call, a known,
// narrow, documented gap rather than an attempted full fix.
[HarmonyPatch(typeof(Framework.Inventory.PrayerAlliedCherubEffect), "OnApplyEffect")]
internal static class PrayerAlliedCherubEffect_OnApplyEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerAlliedCherubEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        AlliedCherubPrayer cherubPrayer = prayerUse != null ? prayerUse.cherubPrayer : null;
        if (cherubPrayer != null)
        {
            cherubPrayer.InstantiateCherubs();
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerAlliedCherubEffect), "OnRemoveEffect")]
internal static class PrayerAlliedCherubEffect_OnRemoveEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerAlliedCherubEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        AlliedCherubPrayer cherubPrayer = prayerUse != null ? prayerUse.cherubPrayer : null;
        if (cherubPrayer != null)
        {
            cherubPrayer.DisposeCherubs();
        }
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerShieldEffect), "OnApplyEffect")]
internal static class PrayerShieldEffect_OnApplyEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerShieldEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        ShieldSystemPrayer shieldPrayer = prayerUse != null ? prayerUse.shieldPrayer : null;
        if (shieldPrayer != null)
        {
            shieldPrayer.InstantiateShield();
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Framework.Inventory.PrayerShieldEffect), "OnRemoveEffect")]
internal static class PrayerShieldEffect_OnRemoveEffect_P2_Patch
{
    private static bool Prefix(Framework.Inventory.PrayerShieldEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        ShieldSystemPrayer shieldPrayer = prayerUse != null ? prayerUse.shieldPrayer : null;
        if (shieldPrayer != null)
        {
            shieldPrayer.DisposeShield();
        }
        return false;
    }
}

// PenitentLightBeamEffect derives straight from ObjectEffect (not ObjectEffect_Stat) - its
// OnApplyEffect is fully self-contained with no base-call recursion risk, so this is a complete
// reimplementation rather than a partial one.
[HarmonyPatch(typeof(Tools.Items.PenitentLightBeamEffect), "OnApplyEffect")]
internal static class PenitentLightBeamEffect_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "_owner");
    private static readonly FieldInfo AreaSummonAttackField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "_areaSummonAttack");
    private static readonly FieldInfo DamageAmountField = AccessTools.Field(typeof(Tools.Items.PenitentLightBeamEffect), "DamageAmount");
    private static readonly MethodInfo PushPlayerColorMethod = AccessTools.Method(typeof(Tools.Items.PenitentLightBeamEffect), "PushPlayerColor");
    private static readonly MethodInfo PopPlayerColorMethod = AccessTools.Method(typeof(Tools.Items.PenitentLightBeamEffect), "PopPlayerColor");

    private static bool Prefix(Tools.Items.PenitentLightBeamEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || caster != CoopLocal.Player2)
        {
            return true;
        }
        OwnerField.SetValue(__instance, caster);
        PrayerUse prayerUse = caster.GetComponentInChildren<PrayerUse>();
        Gameplay.GameControllers.Bosses.Quirce.Attack.BossAreaSummonAttack areaSummonAttack =
            prayerUse != null ? prayerUse.lightBeamPrayer : null;
        if (areaSummonAttack == null)
        {
            __result = false;
            return false;
        }
        AreaSummonAttackField.SetValue(__instance, areaSummonAttack);
        if (Core.Logic.CameraManager != null && Core.Logic.CameraManager.ProCamera2DShake != null)
        {
            Core.Logic.CameraManager.ProCamera2DShake.ShakeUsingPreset("SimpleHit");
        }
        Vector3 position = areaSummonAttack.transform.position;
        float strengthFinal = caster.Stats.PrayerStrengthMultiplier.Final;
        GameObject spawned = areaSummonAttack.SummonAreaOnPoint(position, 0f, strengthFinal);
        int damageAmount = (int)DamageAmountField.GetValue(__instance);
        Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack spawnedAttack =
            spawned.GetComponent<Gameplay.GameControllers.Bosses.Quirce.Attack.BossSpawnedAreaAttack>();
        if (spawnedAttack != null)
        {
            spawnedAttack.SetDamage(damageAmount);
        }
        __instance.StartCoroutine(VerticalBeamCoroutine(__instance));
        __result = true;
        return false;
    }

    private static System.Collections.IEnumerator VerticalBeamCoroutine(Tools.Items.PenitentLightBeamEffect instance)
    {
        yield return new WaitForSeconds(0.4f);
        PushPlayerColorMethod.Invoke(instance, null);
        yield return new WaitForSeconds(0.8f);
        PopPlayerColorMethod.Invoke(instance, null);
    }
}


