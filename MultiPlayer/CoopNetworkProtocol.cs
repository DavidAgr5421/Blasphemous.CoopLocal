using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

public enum CoopPacketType : byte
{
    HandshakeRequest = 1,
    HandshakeAccept = 2,
    HandshakeReject = 3,
    Ping = 10,
    Pong = 11,
    PlayerInput = 20,
    SceneChange = 30,
    WorldState = 31,
    Disconnect = 99
}

[Flags]
public enum CoopButtonFlags : ushort
{
    None    = 0,
    Up      = 1 << 0,
    Down    = 1 << 1,
    Left    = 1 << 2,
    Right   = 1 << 3,
    Jump    = 1 << 4,
    Attack  = 1 << 5,
    Dash    = 1 << 6,
    Parry   = 1 << 7,
    Heal    = 1 << 8,
    Interact= 1 << 9,
    Prayer  = 1 << 10,
    Pause   = 1 << 11,
    Menu    = 1 << 12
}

public class HandshakeRequestData
{
    public byte ProtocolVersion = 1;
    public string PlayerName = "";
    public int ColorIndex = 1;
    public string SkinId = "";
}

public class HandshakeAcceptData
{
    public byte AssignedSlot = 1;
    public string CurrentScene = "";
    public string HostPlayerName = "";
    public int HostColorIndex = 0;
    public string HostSkinId = "";
}

public struct PlayerInputData
{
    public byte SlotIndex;
    public ushort Sequence;
    public CoopButtonFlags Buttons;
    public short AxisX; // -1000..1000
    public short AxisY; // -1000..1000
}

public struct PlayerStateSync
{
    public byte SlotIndex;
    public float PosX;
    public float PosY;
    public bool FacingRight;
    public byte AnimIndex;
    public float CurrentHp;
    public float MaxHp;
    public float CurrentFervour;
    public float MaxFervour;
    public int Purge;
    public byte Flasks;
    public bool IsDead;
}

public class WorldStateData
{
    public ushort Sequence;
    public PlayerStateSync[] Players;
}

/// <summary>
/// Protocolo y serializador binario de alto rendimiento para el cooperativo en línea P2P.
/// </summary>
public static class CoopNetworkProtocol
{
    public const byte CURRENT_PROTOCOL_VERSION = 1;

    // --- Serializadores ---

    public static byte[] SerializeHandshakeRequest(HandshakeRequestData req)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)CoopPacketType.HandshakeRequest);
            writer.Write(req.ProtocolVersion);
            writer.Write(req.PlayerName ?? "");
            writer.Write(req.ColorIndex);
            writer.Write(req.SkinId ?? "");
            return ms.ToArray();
        }
    }

    public static HandshakeRequestData DeserializeHandshakeRequest(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            reader.ReadByte(); // packet type
            HandshakeRequestData req = new HandshakeRequestData();
            req.ProtocolVersion = reader.ReadByte();
            req.PlayerName = reader.ReadString();
            req.ColorIndex = reader.ReadInt32();
            req.SkinId = reader.ReadString();
            return req;
        }
    }

    public static byte[] SerializeHandshakeAccept(HandshakeAcceptData acc)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)CoopPacketType.HandshakeAccept);
            writer.Write(acc.AssignedSlot);
            writer.Write(acc.CurrentScene ?? "");
            writer.Write(acc.HostPlayerName ?? "");
            writer.Write(acc.HostColorIndex);
            writer.Write(acc.HostSkinId ?? "");
            return ms.ToArray();
        }
    }

    public static HandshakeAcceptData DeserializeHandshakeAccept(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            reader.ReadByte(); // packet type
            HandshakeAcceptData acc = new HandshakeAcceptData();
            acc.AssignedSlot = reader.ReadByte();
            acc.CurrentScene = reader.ReadString();
            acc.HostPlayerName = reader.ReadString();
            acc.HostColorIndex = reader.ReadInt32();
            acc.HostSkinId = reader.ReadString();
            return acc;
        }
    }

    public static byte[] SerializeHandshakeReject(string reason)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)CoopPacketType.HandshakeReject);
            writer.Write(reason ?? "Conexión rechazada");
            return ms.ToArray();
        }
    }

    public static string DeserializeHandshakeReject(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            reader.ReadByte(); // packet type
            return reader.ReadString();
        }
    }

    public static byte[] SerializePlayerInput(PlayerInputData input)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.PlayerInput);
            writer.Write(input.SlotIndex);
            writer.Write(input.Sequence);
            writer.Write((ushort)input.Buttons);
            writer.Write(input.AxisX);
            writer.Write(input.AxisY);
            return ms.ToArray();
        }
    }

    public static PlayerInputData DeserializePlayerInput(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            PlayerInputData input = new PlayerInputData();
            input.SlotIndex = reader.ReadByte();
            input.Sequence = reader.ReadUInt16();
            input.Buttons = (CoopButtonFlags)reader.ReadUInt16();
            input.AxisX = reader.ReadInt16();
            input.AxisY = reader.ReadInt16();
            return input;
        }
    }

    public static byte[] SerializeSceneChange(string sceneName)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)CoopPacketType.SceneChange);
            writer.Write(sceneName ?? "");
            return ms.ToArray();
        }
    }

    public static string DeserializeSceneChange(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            reader.ReadByte(); // packet type
            return reader.ReadString();
        }
    }

    public static byte[] SerializeWorldState(WorldStateData state)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.WorldState);
            writer.Write(state.Sequence);
            byte count = (byte)(state.Players != null ? state.Players.Length : 0);
            writer.Write(count);
            for (int i = 0; i < count; i++)
            {
                var p = state.Players[i];
                writer.Write(p.SlotIndex);
                writer.Write(p.PosX);
                writer.Write(p.PosY);
                writer.Write(p.FacingRight);
                writer.Write(p.AnimIndex);
                writer.Write(p.CurrentHp);
                writer.Write(p.MaxHp);
                writer.Write(p.CurrentFervour);
                writer.Write(p.MaxFervour);
                writer.Write(p.Purge);
                writer.Write(p.Flasks);
                writer.Write(p.IsDead);
            }
            return ms.ToArray();
        }
    }

    public static WorldStateData DeserializeWorldState(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            WorldStateData state = new WorldStateData();
            state.Sequence = reader.ReadUInt16();
            byte count = reader.ReadByte();
            state.Players = new PlayerStateSync[count];
            for (int i = 0; i < count; i++)
            {
                PlayerStateSync p = new PlayerStateSync();
                p.SlotIndex = reader.ReadByte();
                p.PosX = reader.ReadSingle();
                p.PosY = reader.ReadSingle();
                p.FacingRight = reader.ReadBoolean();
                p.AnimIndex = reader.ReadByte();
                p.CurrentHp = reader.ReadSingle();
                p.MaxHp = reader.ReadSingle();
                p.CurrentFervour = reader.ReadSingle();
                p.MaxFervour = reader.ReadSingle();
                p.Purge = reader.ReadInt32();
                p.Flasks = reader.ReadByte();
                p.IsDead = reader.ReadBoolean();
                state.Players[i] = p;
            }
            return state;
        }
    }

    public static byte[] SerializeSimple(CoopPacketType type)
    {
        return new byte[] { (byte)type };
    }
}
