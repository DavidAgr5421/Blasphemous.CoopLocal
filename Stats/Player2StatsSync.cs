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

// Round 41: user reported P2 spawning with less max life and no damage/flask upgrades than P1 -
// this is architectural, not a per-instance-owner bug like almost everything else this session:
// CoopLocal.OnPlayerSpawn creates P2 via Object.Instantiate(Resources.Load<Penitent>("Core/Penitent"),
// ...), a completely fresh copy of the base prefab with none of P1's collected Rosary Beads/Mea
// Culpa/flask upgrades/etc ever applied.
//
// Decompiled Gameplay.GameControllers.Entities.EntityStats (real C# via ICSharpCode.Decompiler, not
// raw IL) to find the right generic API: every single stat - Life, Strength, DamageMultiplier,
// FlaskHealth, BeadSlots, CriticalChance, all of it - is a Framework.FrameworkCore.Attributes.Logic.
// Attribute with a `PermanetBonus` float (publicly gettable, privately settable - raised over time by
// Upgrade()/SetPermanentBonus(), i.e. exactly "story-earned progression", as opposed to temporary
// RawBonus/FinalBonus buffs from equipped relics/active effects which are deliberately NOT copied
// here). EntityStats.GetByType(StatsTypes) + SetPermanentBonus(float) - the *same* generic API the
// game's own GetCurrentPersistentState/SetCurrentPersistentState use for save/load - lets one loop
// over every EntityStats.StatsTypes enum value cover the whole stat surface at once, no per-stat
// special-casing needed. All of GetByType/PermanetBonus/SetPermanentBonus/SetToCurrentMax were
// confirmed public directly in the decompiled *real* Assembly-CSharp.dll (not just the NuGet
// reference stub) - unlike PrayerUse.CanUsePrayer earlier this round, there's no reflection
// workaround needed to call them directly.
//
// The user's explicit ask - clone once, "y ya luego esta copia de todo esto no se vuelva a hacer sin
// importar que" (never re-copy after that, no matter what) - can't be a simple did-this-run-before
// flag: CoopLocal.OnPlayerSpawn destroys and recreates P2 from the bare prefab on *every* respawn
// (level load, teleport, death), so P2's own EntityStats object (with all its PermanetBonus values)
// is thrown away and rebuilt from scratch far more often than "once per game". A flag alone would
// mean the correct stats get applied exactly once ever and then every later respawn reverts P2 to
// the weak prefab defaults again - worse than doing nothing. Instead this persists the actual
// baseline values (not just a yes/no marker) to a small per-save-slot text file under
// Application.persistentDataPath: the FIRST spawn for a given save slot (Framework.Managers.
// PersistentManager.GetAutomaticSlot(), the same public static int the game's own save system keys
// its files by) clones P1's current stats onto P2 and writes that snapshot to disk; every later
// spawn - same session or a future one, respawn or fresh launch - restores P2's *own* saved
// baseline onto the fresh instance instead of touching P1 again, so P2 keeps its starting power
// forever after that first sync without perpetually re-mirroring P1's own ongoing progress.
internal static class Player2StatsSync
{
    private static string MarkerDirectory =>
        System.IO.Path.Combine(Application.persistentDataPath, "CoopLocalMod");

    private static string SnapshotPath(int slot) =>
        System.IO.Path.Combine(MarkerDirectory, $"p2_stats_slot{slot}.txt");

    // Round 43/45: Purge (currency), Life, Fervour and Flask all need their *Current* value
    // persisted separately from the PermanetBonus loop - PermanetBonus only covers max-capacity
    // upgrades, not the live value itself, and (round 45) forcing these to max on every single
    // respawn turned out to be actively wrong: SpawnManager.OnPlayerSpawn fires on *ordinary room
    // transitions* too, not just death/checkpoint respawns, so P2 was silently getting fully
    // healed and refilled on every room change ("todo de P2 se resetea al cambiar de sala") while
    // a *real* Prie Dieu rest - which should heal P2 - did nothing at all (PrieDieu's own heal
    // logic never routes through OnPlayerSpawn). Keys deliberately don't match any real
    // EntityStats.StatsTypes enum name, so ApplySnapshot's normal per-stat loop skips over them.
    private const string PurgeCurrentKey = "__PurgeCurrent__";
    private const string LifeCurrentKey = "__LifeCurrent__";
    private const string FervourCurrentKey = "__FervourCurrent__";
    private const string FlaskCurrentKey = "__FlaskCurrent__";

    // Round 42: the first-ever sync (previous round) ran synchronously inside CoopLocal's
    // OnPlayerSpawn handler and captured every one of P1's stats as PermanetBonus=0 - confirmed by
    // reading the actual saved snapshot file, which was all zeros despite the user testing on a
    // save with real progression. Root cause: SpawnManager.OnPlayerSpawn fires as soon as P1's
    // Penitent object exists, but the save file's own EntityStats.SetCurrentPersistentState (which
    // populates the *real* PermanetBonus values from disk) evidently hasn't necessarily run yet at
    // that exact moment - reading p1.Stats synchronously in the same frame can race it. Delaying a
    // handful of frames via a coroutine (hosted on p2, since Penitent is a real MonoBehaviour) before
    // reading P1's stats avoids the race without needing to detect it - correctly delays even into a
    // second/third frame if needed, cheap and imperceptible since this only ever runs once per save
    // slot. The synchronous version below now runs from PerformSync, not directly from
    // OnPlayerSpawn - always go through EnsureSynced.
    // Round 46: the 5-frame delay only exists to dodge the race described above, which only
    // matters for the genuinely-first-ever sync (reading P1's live stats before the save file has
    // necessarily finished restoring them). Every *later* respawn goes through ApplySnapshot,
    // which never reads p1 at all - so routing it through the same delayed coroutine was pure
    // unnecessary lag, and on an ordinary room transition (which can involve a real loading pause,
    // during which yield-return-null-based frame counting can take a perceptible chunk of wall-
    // clock time to advance 5 times) that lag was long enough for the user to see P2's HUD
    // genuinely show fresh/base Life/Fervour/Purge for a moment before snapping to the restored
    // values - read by the user as "todo de P2 se resetea al cambiar de sala". Checking file
    // existence synchronously here and restoring immediately (no coroutine, no delay at all) for
    // the common case removes that window entirely; the delay now only ever applies to the
    // once-per-save first-time sync.
    internal static void EnsureSynced(Penitent p1, Penitent p2)
    {
        if (p1 == null || p2 == null)
        {
            return;
        }
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            return;
        }
        string path = SnapshotPath(slot);
        if (System.IO.File.Exists(path))
        {
            ApplySnapshot(path, p2, (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes)));
            return;
        }
        p2.StartCoroutine(DelayedFirstSync(p1, p2));
    }

    private static System.Collections.IEnumerator DelayedFirstSync(Penitent p1, Penitent p2)
    {
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }
        if (p1 == null || p2 == null)
        {
            yield break;
        }
        PerformFirstSync(p1, p2);
    }

    private static void PerformFirstSync(Penitent p1, Penitent p2)
    {
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            // No save slot active yet - shouldn't normally happen once P1 exists, but skip rather
            // than write a marker under a meaningless bucket.
            return;
        }

        EntityStats.StatsTypes[] allTypes = (EntityStats.StatsTypes[])Enum.GetValues(typeof(EntityStats.StatsTypes));
        string path = SnapshotPath(slot);

        if (System.IO.File.Exists(path))
        {
            // Another spawn's own sync (e.g. a very fast second room change) already wrote the
            // baseline while this one was mid-delay - just restore it instead of double-syncing.
            ApplySnapshot(path, p2, allTypes);
            return;
        }

        foreach (EntityStats.StatsTypes type in allTypes)
        {
            Framework.FrameworkCore.Attributes.Logic.Attribute p1Attr = p1.Stats.GetByType(type);
            Framework.FrameworkCore.Attributes.Logic.Attribute p2Attr = p2.Stats.GetByType(type);
            if (p1Attr == null || p2Attr == null)
            {
                continue;
            }
            p2Attr.SetPermanentBonus(p1Attr.PermanetBonus);
        }
        // First-ever sync only: full heal makes sense as a fresh starting point (and lets the
        // user test prayers immediately) - every *later* respawn restores the persisted current
        // values instead (see ApplySnapshot), it does not force max again.
        p2.Stats.Life.SetToCurrentMax();
        p2.Stats.Flask.SetToCurrentMax();
        p2.Stats.Fervour.SetToCurrentMax();
        // Round 43: the user explicitly asked for P1's current currency to be copied too - P2
        // previously always started at 0 since Purge.Current isn't part of the PermanetBonus
        // loop above (see PurgeCurrentKey's own comment).
        p2.Stats.Purge.Current = p1.Stats.Purge.Current;

        SaveSnapshot(path, p2, allTypes);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[P2StatsSync] first-ever sync for save slot {slot}: cloned P1's progression onto P2 and saved a baseline. " +
                $"P2.Life.Final={p2.Stats.Life.Final:F0}, P2.Strength.Final={p2.Stats.Strength.Final:F1}, P2.Flask.Final={p2.Stats.Flask.Final:F0}",
                Main.CoopLocal);
        }
    }

    private static void SaveSnapshot(string path, Penitent p2, EntityStats.StatsTypes[] allTypes)
    {
        try
        {
            System.IO.Directory.CreateDirectory(MarkerDirectory);
            List<string> lines = new List<string>();
            foreach (EntityStats.StatsTypes type in allTypes)
            {
                Framework.FrameworkCore.Attributes.Logic.Attribute attr = p2.Stats.GetByType(type);
                if (attr == null)
                {
                    continue;
                }
                lines.Add($"{type}={attr.PermanetBonus.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            lines.Add($"{PurgeCurrentKey}={p2.Stats.Purge.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{LifeCurrentKey}={p2.Stats.Life.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{FervourCurrentKey}={p2.Stats.Fervour.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add($"{FlaskCurrentKey}={p2.Stats.Flask.Current.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            System.IO.File.WriteAllLines(path, lines.ToArray());
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to save baseline: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    // Round 43/45: P2's currency/life/fervour/flasks all change continuously during gameplay, but
    // P2's whole EntityStats gets recreated from scratch on every respawn (same architectural
    // issue the PermanetBonus snapshot exists to work around) - without this, all four would
    // silently reset to a stale earlier value on every subsequent respawn. Called from
    // CoopLocal.OnPlayerSpawn right before the outgoing P2 is destroyed, so the *next* spawn's
    // ApplySnapshot picks up the freshest values rather than stale ones.
    internal static void SaveCurrentVitals(Penitent outgoingP2)
    {
        if (outgoingP2 == null)
        {
            return;
        }
        int slot = PersistentManager.GetAutomaticSlot();
        if (slot < 0)
        {
            return;
        }
        string path = SnapshotPath(slot);
        if (!System.IO.File.Exists(path))
        {
            // No baseline yet for this slot - the upcoming first-ever sync will capture P1's
            // current values directly, nothing to update here.
            return;
        }
        try
        {
            List<string> lines = new List<string>(System.IO.File.ReadAllLines(path));
            UpsertLine(lines, PurgeCurrentKey, outgoingP2.Stats.Purge.Current);
            UpsertLine(lines, LifeCurrentKey, outgoingP2.Stats.Life.Current);
            UpsertLine(lines, FervourCurrentKey, outgoingP2.Stats.Fervour.Current);
            UpsertLine(lines, FlaskCurrentKey, outgoingP2.Stats.Flask.Current);
            System.IO.File.WriteAllLines(path, lines.ToArray());
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to save vitals before respawn: {ex.Message}", Main.CoopLocal);
            }
        }
    }

    // Round 45: PrieDieu.ShallowActivationLogic (the real "resting at a shrine" heal, patched
    // separately below) calls this to give P2 the same treatment P1 gets - full life/flasks, and
    // Fervour only if the same Alms upgrade condition P1's own heal checks is met. Persists
    // immediately so the healed values survive the very next respawn correctly.
    internal static void HealAtPrieDieu(Penitent p2, bool healFervour)
    {
        if (p2 == null)
        {
            return;
        }
        p2.Stats.Life.SetToCurrentMax();
        p2.Stats.Flask.SetToCurrentMax();
        if (healFervour)
        {
            p2.Stats.Fervour.SetToCurrentMax();
        }
        SaveCurrentVitals(p2);
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[P2StatsSync] healed P2 at Prie Dieu (Life/Flask to max, Fervour healed={healFervour}).",
                Main.CoopLocal);
        }
    }

    private static void UpsertLine(List<string> lines, string key, float value)
    {
        string newLine = $"{key}={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        int existingIndex = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            lines[existingIndex] = newLine;
        }
        else
        {
            lines.Add(newLine);
        }
    }

    private static void ApplySnapshot(string path, Penitent p2, EntityStats.StatsTypes[] allTypes)
    {
        try
        {
            string[] lines = System.IO.File.ReadAllLines(path);
            int applied = 0;
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq);
                string valueText = line.Substring(eq + 1);
                float value;
                if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                if (key == PurgeCurrentKey)
                {
                    p2.Stats.Purge.Current = value;
                    applied++;
                    continue;
                }
                if (key == LifeCurrentKey)
                {
                    p2.Stats.Life.Current = value;
                    applied++;
                    continue;
                }
                if (key == FervourCurrentKey)
                {
                    p2.Stats.Fervour.Current = value;
                    applied++;
                    continue;
                }
                if (key == FlaskCurrentKey)
                {
                    p2.Stats.Flask.Current = value;
                    applied++;
                    continue;
                }
                if (!Enum.IsDefined(typeof(EntityStats.StatsTypes), key))
                {
                    continue;
                }
                EntityStats.StatsTypes type = (EntityStats.StatsTypes)Enum.Parse(typeof(EntityStats.StatsTypes), key);
                Framework.FrameworkCore.Attributes.Logic.Attribute attr = p2.Stats.GetByType(type);
                if (attr == null)
                {
                    continue;
                }
                attr.SetPermanentBonus(value);
                applied++;
            }
            // Round 45: no longer forces Life/Flask to max here - that was the actual cause of
            // "todo de P2 se resetea al cambiar de sala" (every ordinary room transition fires
            // OnPlayerSpawn, not just death/checkpoint respawns). Those two are restored from the
            // snapshot above instead; a real heal only happens via PrieDieu.ShallowActivationLogic's
            // own Postfix (HealAtPrieDieu) or the first-ever sync.
            //
            // Round 46: Fervour is a deliberate exception, per explicit user request - always
            // force it to max on spawn so prayers can be tested immediately, overriding whatever
            // FervourCurrentKey just restored above. Remove this line (and the matching one in
            // PerformFirstSync) if/when the user wants Fervour to persist like Life/Flask do.
            p2.Stats.Fervour.SetToCurrentMax();

            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info(
                    $"[P2StatsSync] restored P2's saved baseline ({applied} stats) for save slot. " +
                    $"Life={p2.Stats.Life.Current:F0}/{p2.Stats.Life.Final:F0} Fervour={p2.Stats.Fervour.Current:F0}/{p2.Stats.Fervour.CurrentMax:F0} " +
                    $"Flask={p2.Stats.Flask.Current:F0}/{p2.Stats.Flask.Final:F0} Purge={p2.Stats.Purge.Current:F0}",
                    Main.CoopLocal);
            }
        }
        catch (Exception ex)
        {
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[P2StatsSync] failed to restore baseline: {ex.Message}", Main.CoopLocal);
            }
        }
    }
}


