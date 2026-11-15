using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Steamworks;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Datos cosméticos y de mundo que el anfitrión envía al cliente online y que no viajan en WorldState:
/// nombre / color / skin de cada puesto (para dibujar bien los títeres) y qué prie-dieus de la escena están encendidos.
/// </summary>
internal static class CoopOnlineSync
{
    // ------------------------------------------------------------------ SlotInfo (nombre, color, skin por puesto)
    internal static byte[] SerializeSlotInfo()
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write((byte)CoopPacketType.SlotInfo);
            w.Write((byte)4);
            for (int i = 0; i < 4; i++)
            {
                var s = CoopGameConfig.Slots[i];
                w.Write(s.Name ?? "");
                w.Write((byte)Mathf.Clamp(s.ColorIndex, 0, 255));
                w.Write(s.SkinId ?? "");
            }
            return ms.ToArray();
        }
    }

    internal static void SendSlotInfoToAll()
    {
        if (!CoopNetworkManager.IsOnlineHost) return;
        byte[] packet = SerializeSlotInfo();
        foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
            CoopNetworkManager.SendPacket(id, packet, 0, EP2PSend.k_EP2PSendReliable);
    }

    internal static void ApplySlotInfo(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
        {
            r.ReadByte();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadString();
                int color = r.ReadByte();
                string skin = r.ReadString();
                if (i >= 4) continue;
                // Los puestos de ESTE cliente conservan lo que él mismo eligió
                if (CoopNetworkManager.IsLocalSlot(i)) continue;
                var s = CoopGameConfig.Slots[i];
                if (!string.IsNullOrEmpty(name)) s.Name = name;
                s.ColorIndex = color;
                if (!string.IsNullOrEmpty(skin)) s.SkinId = skin;
            }
        }
        CoopClientWorldSync.Instance?.RefreshPuppetCosmetics();
    }

    // ------------------------------------------------------------------ Prie-dieus encendidos
    private static string PrieKey(PrieDieu p)
    {
        Vector3 pos = p.transform.position;
        return Mathf.RoundToInt(pos.x * 10f) + "_" + Mathf.RoundToInt(pos.y * 10f);
    }

    internal static byte[] SerializePrieDieus()
    {
        var lit = new List<string>();
        foreach (PrieDieu p in UnityEngine.Object.FindObjectsOfType<PrieDieu>())
            if (p != null && p.Ligthed) lit.Add(PrieKey(p));
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write((byte)CoopPacketType.PrieDieuState);
            w.Write((ushort)lit.Count);
            foreach (string k in lit) w.Write(k);
            return ms.ToArray();
        }
    }

    internal static void SendPrieDieusTo(CSteamID target)
    {
        if (!CoopNetworkManager.IsOnlineHost) return;
        CoopNetworkManager.SendPacket(target, SerializePrieDieus(), 0, EP2PSend.k_EP2PSendReliable);
    }

    internal static void SendPrieDieusToAll()
    {
        if (!CoopNetworkManager.IsOnlineHost) return;
        byte[] packet = SerializePrieDieus();
        foreach (CSteamID id in CoopNetworkManager.ConnectedClientIds())
            CoopNetworkManager.SendPacket(id, packet, 0, EP2PSend.k_EP2PSendReliable);
    }

    private static readonly HashSet<string> pendingLit = new HashSet<string>();
    private static float pendingUntil;

    internal static void ReceivePrieDieus(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
        {
            r.ReadByte();
            int n = r.ReadUInt16();
            pendingLit.Clear();
            for (int i = 0; i < n; i++) pendingLit.Add(r.ReadString());
        }
        pendingUntil = Time.unscaledTime + 25f;
        TryApplyPendingPrieDieus();
    }

    /// <summary>Se reintenta unos segundos: la escena del cliente puede no haber terminado de cargar cuando llega el paquete.</summary>
    internal static void TryApplyPendingPrieDieus()
    {
        if (pendingLit.Count == 0) return;
        if (Time.unscaledTime > pendingUntil) { pendingLit.Clear(); return; }
        foreach (PrieDieu p in UnityEngine.Object.FindObjectsOfType<PrieDieu>())
        {
            if (p == null || p.Ligthed) continue;
            if (pendingLit.Contains(PrieKey(p)))
            {
                try { p.Ligthed = true; }
                catch (Exception e) { SwallowedExceptions.Report(e, "CoopOnlineSync:prie"); }
            }
        }
    }
}

/// <summary>Cuando el anfitrión enciende un prie-dieu, los clientes lo ven encendido al instante.</summary>
[HarmonyPatch(typeof(PrieDieu), "ShallowActivationLogic")]
internal static class PrieDieu_Activation_OnlineSync_Patch
{
    private static void Postfix()
    {
        if (CoopNetworkManager.IsOnlineHost) CoopOnlineSync.SendPrieDieusToAll();
    }
}
