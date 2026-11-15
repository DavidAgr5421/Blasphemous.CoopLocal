using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Framework.FrameworkCore;
using Framework.Managers;
using FullSerializer;
using Steamworks;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Estado persistente de los objetos de la escena (prie-dieus, cofres, puertas, palancas, rompibles, coleccionables, pasadizos...)
/// que cada PC guarda en SU partida local. El anfitrión lo envía al cliente al entrar y cada vez que cambia, y el cliente lo
/// aplica con el mismo método que usa el juego al cargar una partida (SetCurrentPersistentState).
/// </summary>
internal static class CoopSceneStateSync
{
    private static readonly fsSerializer serializer = new fsSerializer();

    // El estado de estos se sincroniza por otras vías o es personal de cada jugador.
    private static bool IsExcluded(PersistentObject po)
    {
        string n = po.GetType().Name;
        return n == "PersistentEnemy" || n == "GuiltDropCollectibleItem" || n == "MiriamPortal" || n == "MiriamExit" || n == "MiriamStart";
    }

    // ------------------------------------------------------------------ anfitrión
    private static string hostScene = "";
    private static readonly Dictionary<string, string> hostSent = new Dictionary<string, string>();
    private static float nextScan;

    private static string CurrentScene()
    {
        try { return Core.LevelManager != null && Core.LevelManager.currentLevel != null ? Core.LevelManager.currentLevel.LevelName : ""; }
        catch (Exception) { return ""; }
    }

    private static bool Transitioning()
    {
        try { return Core.LevelManager == null || Core.LevelManager.InsideChangeLevel; }
        catch (Exception) { return true; }
    }

    private static Dictionary<string, string> Capture()
    {
        var result = new Dictionary<string, string>();
        foreach (PersistentObject po in UnityEngine.Object.FindObjectsOfType<PersistentObject>())
        {
            if (po == null || IsExcluded(po)) continue;
            try
            {
                if (po.IsIgnoringPersistence()) continue;
                string id = po.GetPersistenID();
                if (string.IsNullOrEmpty(id)) continue;
                PersistentManager.PersistentData data = po.GetCurrentPersistentState("", false);
                if (data == null) continue;
                fsData fs;
                if (serializer.TrySerialize(typeof(PersistentManager.PersistentData), data, out fs).Failed) continue;
                result[id] = fsJsonPrinter.CompressedJson(fs);
            }
            catch (Exception e) { SwallowedExceptions.Report(e, "CoopSceneStateSync:capture"); }
        }
        return result;
    }

    /// <summary>Cada ~2 s revisa la escena y envía a los clientes solo lo que cambió.</summary>
    internal static void HostTick()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 2f;
        if (Transitioning()) return;
        string scene = CurrentScene();
        if (scene.Length == 0) return;
        if (scene != hostScene) { hostScene = scene; hostSent.Clear(); nextScan = Time.unscaledTime + 3f; return; } // deja cargar la escena

        var now = Capture();
        var changed = new Dictionary<string, string>();
        foreach (var kv in now)
        {
            string old;
            if (!hostSent.TryGetValue(kv.Key, out old) || old != kv.Value) changed[kv.Key] = kv.Value;
        }
        if (changed.Count == 0) return;
        foreach (var kv in changed) hostSent[kv.Key] = kv.Value;
        Broadcast(scene, changed);
    }

    private static void Broadcast(string scene, Dictionary<string, string> entries)
    {
        byte[] packet = Serialize(scene, entries);
        foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
            CoopNetworkManager.SendPacket(id, packet, 0, EP2PSend.k_EP2PSendReliable);
    }

    /// <summary>Estado completo de la escena actual para un cliente que acaba de entrar / cargar la escena.</summary>
    internal static void SendFullTo(CSteamID target)
    {
        if (!CoopNetworkManager.IsOnlineHost || Transitioning()) return;
        string scene = CurrentScene();
        if (scene.Length == 0) return;
        var now = Capture();
        hostScene = scene;
        foreach (var kv in now) hostSent[kv.Key] = kv.Value;
        CoopNetworkManager.SendPacket(target, Serialize(scene, now), 0, EP2PSend.k_EP2PSendReliable);
    }

    private static byte[] Serialize(string scene, Dictionary<string, string> entries)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write((byte)CoopPacketType.SceneState);
            w.Write(scene);
            w.Write(entries.Count);
            foreach (var kv in entries) { w.Write(kv.Key); w.Write(kv.Value); }
            return ms.ToArray();
        }
    }

    // ------------------------------------------------------------------ cliente
    private static string pendingScene = "";
    private static readonly Dictionary<string, string> pending = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> applied = new Dictionary<string, string>();
    private static string appliedScene = "";
    private static float pendingUntil;

    internal static void Receive(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
        {
            r.ReadByte();
            string scene = r.ReadString();
            int n = r.ReadInt32();
            if (!string.Equals(scene, pendingScene, StringComparison.OrdinalIgnoreCase)) { pending.Clear(); pendingScene = scene; }
            for (int i = 0; i < n; i++) { string id = r.ReadString(); pending[id] = r.ReadString(); }
        }
        pendingUntil = Time.unscaledTime + 25f;
        TryApply();
    }

    internal static void TryApply()
    {
        if (pending.Count == 0) return;
        if (Time.unscaledTime > pendingUntil) { pending.Clear(); return; }
        if (Transitioning()) return;
        string scene = CurrentScene();
        if (!string.Equals(scene, pendingScene, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(scene, appliedScene, StringComparison.OrdinalIgnoreCase)) { applied.Clear(); appliedScene = scene; }

        var done = new List<string>();
        foreach (PersistentObject po in UnityEngine.Object.FindObjectsOfType<PersistentObject>())
        {
            if (po == null || IsExcluded(po)) continue;
            try
            {
                if (po.IsIgnoringPersistence()) continue;
                string id = po.GetPersistenID();
                string json;
                if (string.IsNullOrEmpty(id) || !pending.TryGetValue(id, out json)) continue;
                string prev;
                if (applied.TryGetValue(id, out prev) && prev == json) { done.Add(id); continue; }

                fsData fs;
                if (fsJsonParser.Parse(json, out fs).Failed) { done.Add(id); continue; }
                object obj = null;
                if (serializer.TryDeserialize(fs, typeof(PersistentManager.PersistentData), ref obj).Failed) { done.Add(id); continue; }
                PersistentManager.PersistentData pd = obj as PersistentManager.PersistentData;
                if (pd != null) po.SetCurrentPersistentState(pd, false, "");
                applied[id] = json;
                done.Add(id);
            }
            catch (Exception e) { SwallowedExceptions.Report(e, "CoopSceneStateSync:apply"); }
        }
        // Lo que no se encontró se reintenta (el objeto puede no haberse creado aún); lo aplicado sale de la cola.
        for (int i = 0; i < done.Count; i++) pending.Remove(done[i]);
    }
}
