using System.Collections.Generic;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Round 57: every permanent-upgrade PlayMaker action (Life/Strength/BeadSlots/FlaskHealth/Fervour/
// MeaCulpa/Flask) hardcodes Core.Logic.Penitent (always P1) when applying its Upgrade() - confirmed
// by decompiling each Tools.Playmaker2.Action.<Name>Upgrade class directly: every one of them is
// just `Core.Logic.Penitent.Stats.<X>.Upgrade(); [Core.Logic.Penitent.Stats.<X>.SetToCurrentMax();]
// Finish();` in OnEnter. P2 has its own EntityStats but nothing ever calls Upgrade() on it. These 7
// Harmony patches redirect the upgrade to P2's own Stats when P2 (not P1) is the one who touched
// the altar/pickup, then persist the result immediately via Player2StatsSync.PersistPermanentBonus
// (P2's whole EntityStats gets recreated from scratch on every respawn/room transition - see that
// file's own header comment).
//
// BeadSlots and MeaCulpa are NOT plain int/float fields - EntityStats.BeadSlots and
// EntityStats.MeaCulpa are themselves Attribute-derived properties (same class as Life/Strength/
// etc, confirmed in the decompiled Gameplay.GameControllers.Entities.EntityStats), so they get the
// exact same `.Upgrade()` call as everything else. The real Attribute property is spelled
// `PermanetBonus` (missing the second 'n' - a genuine typo in the game's own source, confirmed in
// the decompiled Framework.FrameworkCore.Attributes.Logic.Attribute) - there is no `PermanentBonus`
// (correctly spelled) member to read.
//
// Trap avoided here (the Postfix-fires-unconditionally variant of family 1/3): the Postfix only
// applies the P2 upgrade when the matching Prefix positively identified P2 as the toucher for that
// exact Fsm instance (UpgradeCreditState.LastToucher, keyed by Fsm to support multiple altars alive
// in the same scene). Without this guard an unconditional Postfix would ALSO fire when P1
// legitimately triggers the vanilla pickup (Prefix returned true, letting
// Core.Logic.Penitent.Stats.<X>.Upgrade() run normally) - silently double-granting the same
// permanent bonus to P2 for free and calling Finish() a second time on an Fsm the vanilla code
// already finished on its own.

// ---------------------------------------------------------------------------
// Helper: UpgradeTouchResolver
// Determines which Penitent (P1 or P2) physically touched the altar/pickup's trigger, using
// per-instance Fsm data - never shared/global state (family 3).
// ---------------------------------------------------------------------------
internal static class UpgradeTouchResolver
{
    // Fsm.TriggerCollider2D is a real public per-instance property (confirmed in the decompiled
    // HutongGames.PlayMaker.Fsm), populated by OnTriggerEnter2D/Stay2D/Exit2D right before the FSM
    // event fires - i.e. exactly which specific collider (P1's or P2's) tripped THIS altar's
    // trigger. It is typed Collider2D (this is a 2D game - the earlier reflection-based version of
    // this method cast it to the unrelated 3D UnityEngine.Collider type via `as`, which always
    // silently yields null and made this resolver never actually work).
    internal static Penitent ResolveByTriggerCollider(Fsm fsm)
    {
        if (fsm == null) return null;
        Collider2D collider = fsm.TriggerCollider2D;
        return collider != null ? collider.GetComponentInParent<Penitent>() : null;
    }

    // Secondary attempt: Collision2DInfo (populated by OnCollisionEnter2D/Stay2D/Exit2D instead of
    // the trigger callbacks) - covers altar variants that use a solid collision instead of a
    // trigger. Also a real per-instance Fsm property, typed Collision2D (not the 3D Collision type).
    internal static Penitent ResolveByCollisionInfo(Fsm fsm)
    {
        if (fsm == null) return null;
        Collision2D info = fsm.Collision2DInfo;
        return (info != null && info.gameObject != null) ? info.gameObject.GetComponentInParent<Penitent>() : null;
    }

    // Fallback for cases with no physical collider/collision data at all (e.g. an action reached
    // through a dialogue/button chain). Best-effort only, used last.
    internal static Penitent ResolveByProximity(Fsm fsm)
    {
        if (fsm == null || fsm.GameObject == null) return null;
        return fsm.GameObject.GetComponentInParent<Penitent>();
    }

    internal static Penitent Resolve(Fsm fsm)
    {
        return ResolveByTriggerCollider(fsm) ?? ResolveByCollisionInfo(fsm) ?? ResolveByProximity(fsm);
    }
}

// ---------------------------------------------------------------------------
// Helper: UpgradeCreditState
// Shared Prefix/Postfix gating logic for all 7 patches below - keeps the "was this Fsm positively
// resolved as P2 THIS time" decision out of global/static booleans (family 3) by keying on the
// specific Fsm instance that fired.
// ---------------------------------------------------------------------------
internal static class UpgradeCreditState
{
    private static readonly Dictionary<Fsm, Penitent> LastToucher = new();

    // Shared Prefix body: true = let vanilla run untouched (P1, or nobody positively identified -
    // safer default than crediting P2 on ambiguous data). false = block vanilla, P2 was identified;
    // the matching Postfix will do the actual crediting.
    internal static bool TryBlockForPlayer2(Fsm fsm)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null) return true;

        Penitent toucher = UpgradeTouchResolver.Resolve(fsm);
        if (toucher == null || toucher != p2) return true;

        LastToucher[fsm] = toucher;
        return false;
    }

    // Shared Postfix guard: only returns true (and hands back p2) if the matching Prefix call for
    // this exact Fsm instance actually identified P2. Removes the entry afterward so a later,
    // ordinary P1 pickup reusing the same Fsm object never sees a stale P2 entry.
    internal static bool TryConsumeForPlayer2(Fsm fsm, out Penitent p2)
    {
        if (LastToucher.TryGetValue(fsm, out p2) && p2 != null && p2 == CoopLocal.Player2)
        {
            LastToucher.Remove(fsm);
            return true;
        }
        p2 = null;
        return false;
    }
}

// ---------------------------------------------------------------------------
// 7 Harmony patches, one per permanent-upgrade PlayMaker action. Each mirrors the exact vanilla
// OnEnter body (decompiled above) but targeting p2.Stats instead of Core.Logic.Penitent.Stats.
// ---------------------------------------------------------------------------

// 1. LifeUpgrade
[HarmonyPatch(typeof(Tools.Playmaker2.Action.LifeUpgrade), "OnEnter")]
internal static class LifeUpgrade_CreditP2_Patch
{
    // OnEnter() takes no parameters - "fsm" only exists as the private field `fsm` on the base
    // class HutongGames.PlayMaker.FsmStateAction (confirmed via ilspycmd). Harmony's reversed-field
    // convention needs the 3-underscore prefix (___fsm) to inject it; a bare "fsm" parameter name
    // doesn't match any real method parameter, which is exactly the collateral finding from Round
    // 66's playtest log ("Parameter "fsm" not found in method ... LifeUpgrade::OnEnter()").
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.LifeUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.Life.Upgrade();
        p2.Stats.Life.SetToCurrentMax(); // mirrors vanilla LifeUpgrade.OnEnter
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish(); // critical: vanilla never ran, so the altar's Fsm must be finished manually
    }
}

// 2. StrengthUpgrade
[HarmonyPatch(typeof(Tools.Playmaker2.Action.StrengthUpgrade), "OnEnter")]
internal static class StrengthUpgrade_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.StrengthUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.Strength.Upgrade();
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}

// 3. BeadUpgrade (Rosary Beads slots)
[HarmonyPatch(typeof(Tools.Playmaker2.Action.BeadUpgrade), "OnEnter")]
internal static class BeadUpgrade_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.BeadUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.BeadSlots.Upgrade(); // BeadSlots is an Attribute-derived property, not a plain int
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}

// 4. FlaskHealthUpgrade (flask healing capacity)
[HarmonyPatch(typeof(Tools.Playmaker2.Action.FlaskHealthUpgrade), "OnEnter")]
internal static class FlaskHealthUpgrade_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.FlaskHealthUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.FlaskHealth.Upgrade();
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}

// 5. FervourUpgrade
[HarmonyPatch(typeof(Tools.Playmaker2.Action.FervourUpgrade), "OnEnter")]
internal static class FervourUpgrade_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.FervourUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.Fervour.Upgrade();
        p2.Stats.Fervour.SetToCurrentMax(); // mirrors vanilla FervourUpgrade.OnEnter
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}

// 6. MeaCulpaUpgrade
[HarmonyPatch(typeof(Tools.Playmaker2.Action.MeaCulpaUpgrade), "OnEnter")]
internal static class MeaCulpaUpgrade_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.MeaCulpaUpgrade __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.MeaCulpa.Upgrade(); // MeaCulpa is an Attribute-derived property, not a plain float
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}

// 7. FlaskAdd (extra flask capacity)
[HarmonyPatch(typeof(Tools.Playmaker2.Action.FlaskAdd), "OnEnter")]
internal static class FlaskAdd_CreditP2_Patch
{
    private static bool Prefix(Fsm ___fsm) => UpgradeCreditState.TryBlockForPlayer2(___fsm);

    private static void Postfix(Fsm ___fsm, Tools.Playmaker2.Action.FlaskAdd __instance)
    {
        if (!UpgradeCreditState.TryConsumeForPlayer2(___fsm, out Penitent p2)) return;
        p2.Stats.Flask.Upgrade(); // mirrors vanilla FlaskAdd.OnEnter (Stats.Flask.Upgrade())
        Player2StatsSync.PersistPermanentBonus(p2);
        __instance.Finish();
    }
}
