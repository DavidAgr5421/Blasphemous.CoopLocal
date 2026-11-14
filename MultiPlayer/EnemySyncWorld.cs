using System;
using System.Collections.Generic;
using System.Reflection;
using Framework.Managers;
using Gameplay.GameControllers.Bosses;
using Gameplay.GameControllers.Bosses.BossFight;
using Gameplay.GameControllers.Entities;
using Gameplay.UI;
using HarmonyLib;
using Steamworks;
using Tools.Level.Actionables;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Step D1 of the enemy sync: flags / gates / boss events (host -> clients), host discovery logging and the
/// client stuck-input safety net. See Modding/ENEMY_SYNC_SPEC.md (Phase 1b).
/// Applying a remote change never rebroadcasts (_applying) and clients never broadcast.
/// </summary>
internal static class EnemySyncWorld
{
    private const float StuckInputSeconds = 20f;
    private const int FlagInfoLogLimit = 50;

    private static bool _applying;
    private static int _flagLogCount;

    private static EventManager _subscribedTo;
    private static bool _bossActive;

    internal static bool Applying => _applying;
    internal static bool BossActive => _bossActive;

    // ------------------------------------------------------------------ common

    private static void SendToAll(byte[] packet)
    {
        foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
            CoopNetworkManager.SendPacket(id, packet, CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
    }

    private static void Err(string what, Exception ex)
    {
        Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] {what} failed: {ex}", Main.CoopLocal);
    }

    internal static void OnSceneChanged()
    {
        _gateCache = null;
        _gateCacheScene = null;
        _bossActive = false;
    }

    // ------------------------------------------------------------------ host: subscriptions

    internal static void HostTick()
    {
        try
        {
            EventManager ev = Core.Events;
            if (ev == null || ReferenceEquals(ev, _subscribedTo)) return;
            ev.OnFlagChanged += OnFlagChanged;
            ev.OnEventLaunched += OnEventLaunched;
            _subscribedTo = ev;
            Blasphemous.ModdingAPI.ModLog.Info("[EnemySync] host suscrito a OnFlagChanged/OnEventLaunched", Main.CoopLocal);
        }
        catch (Exception ex) { Err("HostTick", ex); }
    }

    private static void LogFlag(string msg)
    {
        if (_flagLogCount++ < FlagInfoLogLimit)
            Blasphemous.ModdingAPI.ModLog.Info("[EnemySync][Flag] " + msg, Main.CoopLocal);
        else
            DashParryDebugLog.LogVerbose("[EnemySync][Flag] " + msg);
    }

    private static void OnFlagChanged(string flag, bool active)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost || _applying) return;
            if (_bossActive) DashParryDebugLog.Log($"[EnemySync][Boss] flag {flag}={active}");
            LogFlag($"host envia {flag}={active}");
            SendToAll(CoopNetworkProtocol.SerializeFlagChange(new FlagChangeData
            {
                Flags = new[] { new FlagEntry { Flag = flag, Value = active } }
            }));
        }
        catch (Exception ex) { Err("OnFlagChanged", ex); }
    }

    private static void OnEventLaunched(string id, string parameter)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost || !_bossActive) return;
            DashParryDebugLog.Log($"[EnemySync][Boss] LaunchEvent id={id} param={parameter}");
        }
        catch (Exception ex) { Err("OnEventLaunched", ex); }
    }

    // ------------------------------------------------------------------ host: bulk state

    /// <summary>Host: send every currently TRUE flag to one client (chunked).</summary>
    internal static void SendAllFlagsTo(CSteamID target)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost) return;
            var dict = Traverse.Create(Core.Events).Field("flags").GetValue<Dictionary<string, FlagObject>>();
            if (dict == null)
            {
                Blasphemous.ModdingAPI.ModLog.Warn("[EnemySync][Flag] no se pudo leer EventManager.flags (Traverse)", Main.CoopLocal);
                return;
            }

            var chunk = new List<FlagEntry>();
            int total = 0, packets = 0;
            foreach (var kv in dict)
            {
                if (kv.Value == null || !kv.Value.value) continue;
                chunk.Add(new FlagEntry { Flag = kv.Key, Value = true });
                total++;
                if (chunk.Count >= CoopNetworkProtocol.MAX_FLAGS_PER_PACKET)
                {
                    CoopNetworkManager.SendPacket(target, CoopNetworkProtocol.SerializeFlagChange(new FlagChangeData { Flags = chunk.ToArray() }),
                        CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                    chunk.Clear();
                    packets++;
                }
            }
            if (chunk.Count > 0)
            {
                CoopNetworkManager.SendPacket(target, CoopNetworkProtocol.SerializeFlagChange(new FlagChangeData { Flags = chunk.ToArray() }),
                    CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                packets++;
            }
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync][Flag] bulk enviado: {total} flags TRUE en {packets} paquete(s)", Main.CoopLocal);
        }
        catch (Exception ex) { Err("SendAllFlagsTo", ex); }
    }

    /// <summary>Host, on a matching SceneReady: flags + state of every gate in the current scene.</summary>
    internal static void SendSceneStateTo(CSteamID target)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost) return;
            SendAllFlagsTo(target);

            ushort hash = EnemySyncRegistry.SceneHash16();
            Gate[] gates = UnityEngine.Object.FindObjectsOfType<Gate>();
            int sent = 0;
            foreach (Gate g in gates)
            {
                if (g == null) continue;
                CoopNetworkManager.SendPacket(target, CoopNetworkProtocol.SerializeGateState(new GateStateData
                {
                    GateId = ComputeGateId(g),
                    Open = g.IsOpenOrActivated(),
                    SceneHash = hash
                }), CoopNetworkManager.CHANNEL_RELIABLE, EP2PSend.k_EP2PSendReliable);
                sent++;
            }
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync][Gate] estado de escena enviado: {sent} gates", Main.CoopLocal);
        }
        catch (Exception ex) { Err("SendSceneStateTo", ex); }
    }

    // ------------------------------------------------------------------ gates

    private static Dictionary<uint, Gate> _gateCache;
    private static string _gateCacheScene;

    private static uint Fnv1a(string s)
    {
        uint hash = 0x811C9DC5u;
        for (int i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 16777619u;
        }
        return hash;
    }

    private static string HierarchyPath(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    internal static uint ComputeGateId(Gate g)
    {
        Vector3 p = g.transform.position;
        string key = EnemySyncRegistry.GetCurrentSceneName() + "|" + HierarchyPath(g.transform) + "|"
            + Mathf.RoundToInt(p.x * 100f) + "," + Mathf.RoundToInt(p.y * 100f);
        return Fnv1a(key);
    }

    private static Dictionary<uint, Gate> BuildGateCache()
    {
        var map = new Dictionary<uint, Gate>();
        foreach (Gate g in UnityEngine.Object.FindObjectsOfType<Gate>())
        {
            if (g == null) continue;
            uint id = ComputeGateId(g);
            if (map.ContainsKey(id))
                Blasphemous.ModdingAPI.ModLog.Warn($"[EnemySync][Gate] id duplicado {id} ('{HierarchyPath(g.transform)}'), se ignora el segundo", Main.CoopLocal);
            else
                map[id] = g;
        }
        _gateCache = map;
        _gateCacheScene = EnemySyncRegistry.GetCurrentSceneName();
        return map;
    }

    private static Gate FindGate(uint id)
    {
        if (_gateCache == null || _gateCacheScene != EnemySyncRegistry.GetCurrentSceneName()) BuildGateCache();
        if (_gateCache.TryGetValue(id, out Gate g) && g != null) return g;
        BuildGateCache(); // stale / late-created gate: one retry
        return _gateCache.TryGetValue(id, out g) && g != null ? g : null;
    }

    internal static void HostOnGateUse(Gate gate)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost || _applying) return;
            uint id = ComputeGateId(gate);
            bool open = gate.IsOpenOrActivated();
            DashParryDebugLog.Log($"[EnemySync][Boss] gate toggle '{HierarchyPath(gate.transform)}' id={id} open={open} bossActive={_bossActive}");
            SendToAll(CoopNetworkProtocol.SerializeGateState(new GateStateData
            {
                GateId = id,
                Open = open,
                SceneHash = EnemySyncRegistry.SceneHash16()
            }));
        }
        catch (Exception ex) { Err("HostOnGateUse", ex); }
    }

    // ------------------------------------------------------------------ client: apply

    internal static void ApplyFlagChange(FlagChangeData d)
    {
        try
        {
            if (d.Flags == null) return;
            _applying = true;
            int applied = 0;
            foreach (FlagEntry f in d.Flags)
            {
                if (string.IsNullOrEmpty(f.Flag)) continue;
                if (Core.Events.GetFlag(f.Flag) == f.Value) continue;
                Core.Events.SetFlag(f.Flag, f.Value);
                applied++;
                LogFlag($"cliente aplica {f.Flag}={f.Value}");
            }
            if (d.Flags.Length > 1)
                Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync][Flag] bulk recibido: {d.Flags.Length} flags, {applied} cambiados", Main.CoopLocal);
        }
        catch (Exception ex) { Err("ApplyFlagChange", ex); }
        finally { _applying = false; }
    }

    internal static void ApplyGateState(GateStateData d)
    {
        try
        {
            if (d.SceneHash != EnemySyncRegistry.SceneHash16()) return;
            Gate g = FindGate(d.GateId);
            if (g == null)
            {
                DashParryDebugLog.LogOnce("EnemySync.gate.missing." + d.GateId, $"[EnemySync][Gate] gate id={d.GateId} no encontrado en el cliente (escena '{EnemySyncRegistry.GetCurrentSceneName()}')", true);
                return;
            }
            if (g.IsOpenOrActivated() == d.Open) return;
            _applying = true;
            g.Use();
            Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync][Gate] cliente aplica '{HierarchyPath(g.transform)}' open={d.Open}", Main.CoopLocal);
        }
        catch (Exception ex) { Err("ApplyGateState", ex); }
        finally { _applying = false; }
    }

    internal static void ApplyBossEvent(BossEventData d)
    {
        try
        {
            if (d.SceneHash != EnemySyncRegistry.SceneHash16()) return;
            if (d.Kind == BossEventData.KIND_FIGHT_START)
            {
                if (d.BossNetId == 0 || !EnemySyncRegistry.TryGet(d.BossNetId, out EnemySyncRegistry.Entry reg) || reg.Enemy == null)
                {
                    Blasphemous.ModdingAPI.ModLog.Warn($"[EnemySync][Boss] FightStart sin puppet local (netId={d.BossNetId})", Main.CoopLocal);
                    return;
                }
                UIController.instance.ShowBossHealth(reg.Enemy);
                Blasphemous.ModdingAPI.ModLog.Info($"[EnemySync][Boss] FightStart: barra de vida del boss netId={d.BossNetId}", Main.CoopLocal);
            }
            else if (d.Kind == BossEventData.KIND_FIGHT_END)
            {
                UIController.instance.HideBossHealth();
                Blasphemous.ModdingAPI.ModLog.Info("[EnemySync][Boss] FightEnd: barra de vida oculta", Main.CoopLocal);
            }
        }
        catch (Exception ex) { Err("ApplyBossEvent", ex); }
    }

    // ------------------------------------------------------------------ host: boss events

    internal static void HostBossEvent(byte kind, BossFightManager mgr)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost) return;
            _bossActive = kind == BossEventData.KIND_FIGHT_START;
            uint netId = 0;
            Enemy boss = mgr != null ? mgr.Boss : null;
            if (boss != null) EnemySyncRegistry.TryGetNetId(boss, out netId);
            DashParryDebugLog.Log($"[EnemySync][Boss] BossFightManager kind={kind} boss='{(boss != null ? boss.name : "null")}' netId={netId}");
            SendToAll(CoopNetworkProtocol.SerializeBossEvent(new BossEventData
            {
                Kind = kind,
                BossNetId = netId,
                SceneHash = EnemySyncRegistry.SceneHash16()
            }));
        }
        catch (Exception ex) { Err("HostBossEvent", ex); }
    }

    internal static void HostBossTrigger()
    {
        if (!CoopNetworkManager.IsOnlineHost || _bossActive) return;
        _bossActive = true;
        DashParryDebugLog.Log("[EnemySync][Boss] BossScripting trigger en el host -> discovery activo");
    }

    // ------------------------------------------------------------------ client: stuck-input safety

    private static float _blockedSince = -1f;

    internal static void ClientSafetyTick()
    {
        try
        {
            var input = Core.Input;
            if (input == null || !input.InputBlocked) { _blockedSince = -1f; return; }
            if (_blockedSince < 0f) { _blockedSince = Time.time; return; }
            if (Time.time - _blockedSince < StuckInputSeconds) return;

            bool cinematic = false;
            try { cinematic = Traverse.Create(Core.Cinematics).Field("cinematicRunning").GetValue<bool>(); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncWorld.cs:358"); }
            if (cinematic) { _blockedSince = Time.time; return; }

            bool freeCam = false;
            try { freeCam = Traverse.Create(Core.Cinematics).Field("lastCameraValid").GetValue<bool>(); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncWorld.cs:362"); }
            if (freeCam)
            {
                try { Core.Cinematics.SetFreeCamera(false); }
                catch (Exception ex) { Blasphemous.ModdingAPI.ModLog.Error($"[EnemySync] safety SetFreeCamera failed: {ex.Message}", Main.CoopLocal); }
            }

            input.ResetManager();
            // ResetManager does not recompute InputBlocked until the next SetBlocker call.
            input.SetBlocker("COOP_SAFETY_TMP", true);
            input.SetBlocker("COOP_SAFETY_TMP", false);
            _blockedSince = -1f;
            Blasphemous.ModdingAPI.ModLog.Warn($"[EnemySync] safety unblock (input bloqueado > {StuckInputSeconds}s, freeCamera={freeCam}), InputBlocked ahora={input.InputBlocked}", Main.CoopLocal);
        }
        catch (Exception ex) { Err("ClientSafetyTick", ex); }
    }
}

// ====================================================================== patches

[HarmonyPatch(typeof(Gate), nameof(Gate.Use))]
internal static class Gate_Use_EnemySync_Patch
{
    private static bool Prefix(Gate __instance)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineClient || EnemySyncWorld.Applying) return true;
            DashParryDebugLog.LogOnce("EnemySync.gate.suppressed." + __instance.GetInstanceID(),
                $"[EnemySync][Gate] Gate.Use suprimido en cliente: '{__instance.name}' (escena '{EnemySyncRegistry.GetCurrentSceneName()}')");
            return false;
        }
        catch { return true; }
    }

    private static void Postfix(Gate __instance) { EnemySyncWorld.HostOnGateUse(__instance); }
}

[HarmonyPatch(typeof(BossFightManager), nameof(BossFightManager.StartBossFight), new[] { typeof(bool) })]
internal static class BossFightManager_StartBossFight_EnemySync_Patch
{
    private static bool Prefix()
    {
        if (!CoopNetworkManager.IsOnlineClient) return true;
        DashParryDebugLog.LogOnce("EnemySync.boss.startsuppressed", "[EnemySync][Boss] BossFightManager.StartBossFight suprimido en cliente");
        return false;
    }

    private static void Postfix(BossFightManager __instance)
    {
        EnemySyncWorld.HostBossEvent(BossEventData.KIND_FIGHT_START, __instance);
    }
}

[HarmonyPatch(typeof(BossFightManager), "OnDeathBoss")]
internal static class BossFightManager_OnDeathBoss_EnemySync_Patch
{
    private static void Postfix(BossFightManager __instance)
    {
        EnemySyncWorld.HostBossEvent(BossEventData.KIND_FIGHT_END, __instance);
    }
}

[HarmonyPatch(typeof(BossScripting), "OnTriggerEnter2D")]
internal static class BossScripting_OnTriggerEnter2D_EnemySync_Patch
{
    private static bool Prefix()
    {
        if (!CoopNetworkManager.IsOnlineClient) return true;
        DashParryDebugLog.LogOnce("EnemySync.boss.scriptingsuppressed", "[EnemySync][Boss] BossScripting.OnTriggerEnter2D suprimido en cliente");
        return false;
    }

    private static void Postfix() { EnemySyncWorld.HostBossTrigger(); }
}

/// <summary>Host discovery: log PlayMakerFSM.BroadcastEvent(string) names while a boss fight is active.</summary>
[HarmonyPatch]
internal static class PlayMakerFSM_BroadcastEvent_EnemySync_Patch
{
    private static MethodBase _target;

    private static bool Prepare()
    {
        _target = Reflect.Method(typeof(PlayMakerFSM), "BroadcastEvent", new[] { typeof(string) });
        return _target != null;
    }

    private static MethodBase TargetMethod() => _target;

    private static void Prefix(string __0)
    {
        try
        {
            if (!CoopNetworkManager.IsOnlineHost || !EnemySyncWorld.BossActive) return;
            DashParryDebugLog.Log($"[EnemySync][Boss] PlayMakerFSM.BroadcastEvent '{__0}'");
        }
        catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "EnemySyncWorld.cs:459"); }
    }
}
