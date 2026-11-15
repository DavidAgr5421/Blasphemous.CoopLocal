using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Blasphemous.CoopLocal.MultiPlayer;

// Lógica PURA del handshake online (sin Unity/Steam) para poder probarla en el proyecto de Tests.
// Un cliente puede entrar con 1 a 3 jugadores locales (p. ej. uno con teclado y otro con mando): el HandshakeRequest los
// describe a todos y el HandshakeAccept devuelve los puestos que el anfitrión le asignó (uno por jugador aceptado).

/// <summary>Un jugador local del cliente tal como viaja en el handshake.</summary>
public sealed class HandshakePlayer
{
    public string Name = "";
    public int ColorIndex = 1;
    public string SkinId = "";
    /// <summary>OnlineLocalDevice como byte (0 auto, 1 teclado, 2..4 mando 1..3). Informativo para el anfitrión.</summary>
    public byte Device;
}

public class HandshakeRequestData
{
    public byte ProtocolVersion = CoopHandshakeCodec.ProtocolVersion;
    public List<HandshakePlayer> Players = new List<HandshakePlayer>();
}

public class HandshakeAcceptData
{
    /// <summary>Puestos asignados, en el mismo orden que los jugadores del request (puede ser menos si no había sitio).</summary>
    public byte[] AssignedSlots = new byte[] { 1 };
    public string CurrentScene = "";
    public string HostPlayerName = "";
    public int HostColorIndex = 0;
    public string HostSkinId = "";

    /// <summary>Primer puesto asignado (el del jugador principal del cliente).</summary>
    public byte AssignedSlot { get { return AssignedSlots != null && AssignedSlots.Length > 0 ? AssignedSlots[0] : (byte)1; } }
}

public static class CoopHandshakeCodec
{
    /// <summary>v3: el handshake describe/asigna varios jugadores por cliente.</summary>
    public const byte ProtocolVersion = 5;
    public const int MaxPlayersPerClient = 3;

    // Mismos valores que CoopPacketType.HandshakeRequest / HandshakeAccept (CoopNetworkProtocol.cs).
    public const byte PacketRequest = 1;
    public const byte PacketAccept = 2;

    public static byte[] SerializeRequest(HandshakeRequestData req)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write(PacketRequest);
            w.Write(req.ProtocolVersion);
            int n = Math.Min(req.Players.Count, MaxPlayersPerClient);
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                HandshakePlayer p = req.Players[i];
                w.Write(p.Name ?? "");
                w.Write(p.ColorIndex);
                w.Write(p.SkinId ?? "");
                w.Write(p.Device);
            }
            return ms.ToArray();
        }
    }

    public static HandshakeRequestData DeserializeRequest(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
        {
            r.ReadByte(); // tipo de paquete
            HandshakeRequestData req = new HandshakeRequestData();
            req.ProtocolVersion = r.ReadByte();
            // Un cliente de otra versión puede enviar otro formato: el anfitrión ya lo rechaza por ProtocolVersion antes de usar Players.
            if (req.ProtocolVersion != ProtocolVersion) return req;
            int n = Math.Min((int)r.ReadByte(), MaxPlayersPerClient);
            for (int i = 0; i < n; i++)
            {
                HandshakePlayer p = new HandshakePlayer();
                p.Name = r.ReadString();
                p.ColorIndex = r.ReadInt32();
                p.SkinId = r.ReadString();
                p.Device = r.ReadByte();
                req.Players.Add(p);
            }
            return req;
        }
    }

    public static byte[] SerializeAccept(HandshakeAcceptData acc)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write(PacketAccept);
            byte[] slots = acc.AssignedSlots ?? new byte[0];
            w.Write((byte)slots.Length);
            for (int i = 0; i < slots.Length; i++) w.Write(slots[i]);
            w.Write(acc.CurrentScene ?? "");
            w.Write(acc.HostPlayerName ?? "");
            w.Write(acc.HostColorIndex);
            w.Write(acc.HostSkinId ?? "");
            return ms.ToArray();
        }
    }

    public static HandshakeAcceptData DeserializeAccept(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
        {
            r.ReadByte(); // tipo de paquete
            HandshakeAcceptData acc = new HandshakeAcceptData();
            int n = r.ReadByte();
            acc.AssignedSlots = new byte[n];
            for (int i = 0; i < n; i++) acc.AssignedSlots[i] = r.ReadByte();
            acc.CurrentScene = r.ReadString();
            acc.HostPlayerName = r.ReadString();
            acc.HostColorIndex = r.ReadInt32();
            acc.HostSkinId = r.ReadString();
            return acc;
        }
    }
}

/// <summary>Reparto de puestos (1..3) entre los jugadores que pide un cliente.</summary>
public static class CoopSlotAllocator
{
    /// <summary>
    /// Devuelve hasta <paramref name="requested"/> puestos: primero los que el anfitrión reservó como online
    /// (<paramref name="reservedFree"/>), después los sin asignar (<paramref name="otherFree"/>). Ambos arrays tienen 4
    /// posiciones (0 = anfitrión, nunca se asigna). Si hay menos sitio que jugadores pedidos devuelve los que haya.
    /// </summary>
    public static List<int> Allocate(int requested, bool[] reservedFree, bool[] otherFree)
    {
        List<int> result = new List<int>();
        int want = Math.Max(1, Math.Min(requested, CoopHandshakeCodec.MaxPlayersPerClient));
        for (int i = 1; i < 4 && result.Count < want; i++)
        {
            if (reservedFree != null && i < reservedFree.Length && reservedFree[i]) result.Add(i);
        }
        for (int i = 1; i < 4 && result.Count < want; i++)
        {
            if (result.Contains(i)) continue;
            if (otherFree != null && i < otherFree.Length && otherFree[i]) result.Add(i);
        }
        result.Sort();
        return result;
    }
}
