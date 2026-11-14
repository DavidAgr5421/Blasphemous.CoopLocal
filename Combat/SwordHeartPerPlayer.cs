using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Framework.FrameworkCore;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Player.GhostTrail;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Corazones de Mea Culpa (Sword hearts) POR JUGADOR.
//
// Vanilla solo conoce UN inventario (el de P1: InventoryManager.wearSwords) y consulta los corazones de forma global:
//  - HE06 (corazón que anula los matraces): Healing.LateUpdate y PlayerFlask.RefreshFlask leen Sword.IsEquiped, el estado del
//    objeto compartido => si P1 lo equipaba, TODOS los jugadores perdían las pociones (y el HUD de pociones de P2-P4 se vaciaba).
//  - HE10: PenitentAttack.PlayerHasSwordHeart => IsSwordEquipped("HE10"), que el mod hace "cierto si lo tiene CUALQUIER invitado".
//  - Ability.FervorConsumption (reducción de coste de fervor por corazón) => igual, IsSwordEquipped global.
//  - IncreaseSpeedSwordHeartEffect / PowerSlashesSwordHeartEffect: solo actúan sobre P1 (Core.Logic.Penitent / _penitent de P1).
// Regla: el efecto de un corazón solo afecta al jugador que lo tiene equipado.
internal static class SwordHeartOwner
{
    internal const string HeartNoFlasks = "HE06";
    internal const string HeartDestroyProjectiles = "HE10";

    /// <summary>¿Tiene este penitente equipado el corazón? P1 usa el inventario vanilla; P2-P4 sus datos de co-op.</summary>
    internal static bool Has(Penitent owner, string swordId)
    {
        return CoopPlayers.Of(owner)?.HasSword(swordId) ?? false;
    }

    internal static bool HasByIndex(int idx, string swordId)
    {
        return CoopPlayers.Get(Mathf.Max(0, idx))?.HasSword(swordId) ?? false;
    }
}

// --- HE06: Healing.LateUpdate (usar matraz) -> solo bloquea al dueño del corazón ---
[HarmonyPatch(typeof(Healing), "LateUpdate")]
internal static class Healing_LateUpdate_HeartPerOwner_Patch
{
    private static readonly MethodInfo Helper = Reflect.Method(typeof(Healing_LateUpdate_HeartPerOwner_Patch), nameof(IsHealBlocked));

    // Reemplaza `swordHeart06.IsEquiped` por `IsHealBlocked(swordHeart06, this)`.
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction ins in instructions)
        {
            if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi && mi.Name == "get_IsEquiped")
            {
                var ldThis = new CodeInstruction(OpCodes.Ldarg_0);
                ldThis.labels.AddRange(ins.labels);
                ins.labels.Clear();
                yield return ldThis;
                yield return new CodeInstruction(OpCodes.Call, Helper);
                replaced++;
                continue;
            }
            yield return ins;
        }
        if (replaced != 1) DashParryDebugLog.Log($"[SwordHeart] Healing.LateUpdate transpiler: reemplazos={replaced} (esperado 1)");
    }

    public static bool IsHealBlocked(EquipableInventoryObject sword, Healing healing)
    {
        CoopPlayerContext ctx = CoopPlayers.Of(healing != null ? healing.EntityOwner as Penitent : null);
        if (ctx == null || ctx.IsHost) return sword != null && sword.IsEquiped; // P1: comportamiento vanilla
        return ctx.HasSword(SwordHeartOwner.HeartNoFlasks);
    }
}

// --- HE10: PenitentAttack.PlayerHasSwordHeart -> solo el portador destruye proyectiles ---
[HarmonyPatch(typeof(PenitentAttack), nameof(PenitentAttack.PlayerHasSwordHeart), MethodType.Getter)]
internal static class PenitentAttack_PlayerHasSwordHeart_PerOwner_Patch
{
    private static bool Prefix(PenitentAttack __instance, ref bool __result)
    {
        Penitent owner = __instance != null ? __instance.GetComponentInParent<Penitent>() : null;
        if (owner == null) return true; // sin dueño conocido: vanilla
        __result = SwordHeartOwner.Has(owner, SwordHeartOwner.HeartDestroyProjectiles);
        return false;
    }
}

// --- Reducción de coste de fervor por corazón (Ability.SwordHeart) -> solo para el portador ---
[HarmonyPatch]
internal static class Ability_FervorConsumption_HeartPerOwner_Patch
{
    private static MethodBase TargetMethod() => Reflect.PropertyGetter(typeof(Ability), "FervorConsumption");

    private static void Postfix(Ability __instance, ref float __result)
    {
        if (__instance == null || __instance.SwordHeart == null) return;
        float baseCost = __instance.MinFervourConsumption;
        Penitent owner = __instance.EntityOwner as Penitent;
        if (owner == null) return;
        bool has = SwordHeartOwner.Has(owner, __instance.SwordHeart.id);
        __result = has ? baseCost - baseCost * __instance.ReducePercentageByHeart / 100f : baseCost;
    }
}

// --- El efecto de velocidad vanilla guarda "el último penitente que spawneó": que nunca sea un invitado ---
[HarmonyPatch(typeof(IncreaseSpeedSwordHeartEffect), "OnPlayerSpawn")]
internal static class IncreaseSpeedHeart_OnPlayerSpawn_P1Only_Patch
{
    private static bool Prefix(Penitent penitent)
    {
        return CoopPlayerRegistry.GetPlayerIndex(penitent) <= 0;
    }
}

/// <summary>
/// Réplica para P2-P4 de los efectos "activos" de los corazones que vanilla solo aplica a P1:
///  - PowerSlashesSwordHeartEffect: ataque nivel 2 + color del tajo.
///  - IncreaseSpeedSwordHeartEffect: velocidad aumentada tras un dash en suelo (+ estela), hasta parar/saltar/escalar.
/// Se detecta por componente del objeto Sword (no por id), así que cubre cualquier corazón con ese efecto.
/// </summary>
public class CoopGuestHeartEffects : MonoBehaviour
{
    private static CoopGuestHeartEffects _instance;

    private class GuestState
    {
        public Penitent penitent;
        public string swordId;
        public IncreaseSpeedSwordHeartEffect speed;
        public PowerSlashesSwordHeartEffect power;
        public bool powerApplied;
        public int prevAttackLevel;
        public PenitentSword.AttackColor prevColor;
        public bool speedApplied;
        public bool removeOnLand;
        public bool subscribed;
        public Core.SimpleEvent onDash, onLunge;
    }

    private readonly GuestState[] states = new GuestState[CoopPlayerRegistry.MaxPlayers];
    private float pollTimer;
    private static readonly FieldInfo SlashColorField = Reflect.Field(typeof(PowerSlashesSwordHeartEffect), "slashColor");

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("CoopGuestHeartEffects");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopGuestHeartEffects>();
    }

    private void Update()
    {
        if (Core.Logic == null) return;
        pollTimer -= Time.unscaledDeltaTime;
        bool poll = pollTimer <= 0f;
        if (poll) pollTimer = 0.2f;

        for (int idx = 1; idx < CoopPlayerRegistry.MaxPlayers; idx++)
        {
            try { Tick(idx, poll); }
            catch (Exception e) { DashParryDebugLog.Log($"[SwordHeart] guest P{idx + 1} efecto: {e.Message}"); }
        }
    }

    private void Tick(int idx, bool poll)
    {
        Penitent p = CoopPlayerRegistry.GetPenitent(idx);
        GuestState st = states[idx];
        if (p == null)
        {
            states[idx] = null;
            return;
        }
        if (st == null || st.penitent != p)
        {
            st = new GuestState { penitent = p };
            states[idx] = st;
        }

        if (poll)
        {
            string id = Player2InventoryManager.GetSwordInSlot(0, idx);
            if (!string.Equals(id, st.swordId, StringComparison.Ordinal))
            {
                RemovePower(st);
                RemoveSpeed(st);
                st.swordId = id;
                Sword sw = string.IsNullOrEmpty(id) ? null : Core.InventoryManager.GetSword(id);
                st.speed = sw != null ? sw.GetComponentInChildren<IncreaseSpeedSwordHeartEffect>(true) : null;
                st.power = sw != null ? sw.GetComponentInChildren<PowerSlashesSwordHeartEffect>(true) : null;
            }

            // Poder de tajos: reafirmar nivel 2 (el nivel puede reiniciarse al morir/cambiar de sala)
            if (st.power != null && p.PenitentAttack != null)
            {
                if (!st.powerApplied)
                {
                    st.prevAttackLevel = p.PenitentAttack.CurrentLevel;
                    st.prevColor = p.PenitentAttack.AttackColor;
                    st.powerApplied = true;
                }
                if (p.PenitentAttack.CurrentLevel != 2) p.PenitentAttack.CurrentLevel = 2;
                p.PenitentAttack.AttackColor = SlashColorOf(st.power);
            }

            if (st.speed != null) EnsureSubscribed(st);
        }

        if (st.speed != null) TickSpeed(st);
    }

    private static PenitentSword.AttackColor SlashColorOf(PowerSlashesSwordHeartEffect e)
    {
        try
        {
            object v = SlashColorField != null ? SlashColorField.GetValue(e) : null;
            if (v != null && Convert.ToInt32(v) == 1) return PenitentSword.AttackColor.Red;
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "SwordHeartPerPlayer.cs:223"); }
        return PenitentSword.AttackColor.Default;
    }

    private void RemovePower(GuestState st)
    {
        if (!st.powerApplied || st.penitent == null || st.penitent.PenitentAttack == null) { st.powerApplied = false; return; }
        st.penitent.PenitentAttack.CurrentLevel = st.prevAttackLevel;
        st.penitent.PenitentAttack.AttackColor = st.prevColor;
        st.powerApplied = false;
    }

    // ---- Velocidad ----
    private void EnsureSubscribed(GuestState st)
    {
        if (st.subscribed || st.penitent == null) return;
        Penitent p = st.penitent;
        if (p.Dash == null || p.LungeAttack == null) return;
        st.onDash = () =>
        {
            if (st.speed != null && p.Status.IsGrounded) SetSpeed(st, true);
        };
        st.onLunge = () =>
        {
            if (st.speed != null) SetSpeed(st, false);
        };
        p.Dash.OnFinishDash = (Core.SimpleEvent)Delegate.Combine(p.Dash.OnFinishDash, st.onDash);
        p.LungeAttack.OnLungeAttackStart = (Core.SimpleEvent)Delegate.Combine(p.LungeAttack.OnLungeAttackStart, st.onLunge);
        st.subscribed = true;
    }

    private void Unsubscribe(GuestState st)
    {
        if (!st.subscribed || st.penitent == null) { st.subscribed = false; return; }
        Penitent p = st.penitent;
        if (p.Dash != null) p.Dash.OnFinishDash = (Core.SimpleEvent)Delegate.Remove(p.Dash.OnFinishDash, st.onDash);
        if (p.LungeAttack != null) p.LungeAttack.OnLungeAttackStart = (Core.SimpleEvent)Delegate.Remove(p.LungeAttack.OnLungeAttackStart, st.onLunge);
        st.subscribed = false;
    }

    private void TickSpeed(GuestState st)
    {
        Penitent p = st.penitent;
        if (p == null || p.Status == null || p.PlatformCharacterController == null) return;
        if (!p.Status.IsGrounded && st.speedApplied) st.removeOnLand = true;
        if (p.Status.IsGrounded && st.removeOnLand) { st.removeOnLand = false; SetSpeed(st, false); }
        if (st.speedApplied)
        {
            if (p.PlatformCharacterController.IsClimbing || p.IsGrabbingCliffLede || p.IsStickedOnWall) SetSpeed(st, false);
            else if (p.Status.IsGrounded && Mathf.Abs(p.PlatformCharacterController.PlatformCharacterPhysics.HSpeed) <= 0.1f) SetSpeed(st, false);
        }
    }

    private void SetSpeed(GuestState st, bool apply)
    {
        Penitent p = st.penitent;
        if (p == null || p.PlatformCharacterController == null || p.Dash == null) return;
        st.speedApplied = apply;
        Dash.MoveSetting m = apply && st.speed != null ? st.speed.MotionSettings : p.Dash.DefaultMoveSetting;
        p.PlatformCharacterController.MaxWalkingSpeed = m.Speed;
        p.PlatformCharacterController.WalkingDrag = m.Drag;
        GhostTrailGenerator ghost = p.GetComponentInChildren<GhostTrailGenerator>();
        if (ghost != null) ghost.EnableGhostTrail = apply;
    }

    private void RemoveSpeed(GuestState st)
    {
        if (st.speedApplied) SetSpeed(st, false);
        st.removeOnLand = false;
        Unsubscribe(st);
    }
}
