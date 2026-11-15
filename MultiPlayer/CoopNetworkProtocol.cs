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
    EnemyState = 32,
    EnemyEvent = 33,
    EnemyFullSync = 34,
    SceneReady = 35,
    FlagChange = 36,
    GateState = 37,
    BossEvent = 38,
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
    Menu    = 1 << 12,
    Range   = 1 << 13
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

[Flags]
public enum EnemyStateFlags : byte
{
    None       = 0,
    Alive      = 1 << 0,
    FacingLeft = 1 << 1,
    Visible    = 1 << 2
}

public struct EnemyStateEntry
{
    public uint NetId;
    public float X;
    public float Y;
    public EnemyStateFlags Flags;
    public int AnimShortHash;
    public byte AnimNormTime; // 0-255
    public ushort Hp;         // clamped
}

public class EnemyStateData
{
    public ushort Sequence;
    public ushort SceneHash;
    public EnemyStateEntry[] Enemies;
}

public class EnemyEventData
{
    public const byte KIND_DEATH = 1;
    public const byte KIND_RESPAWN_ALL = 2; // netId=0; KillerSlot field reused: 1 = RespawnDeadEnemies, 0 = Reset only
    public byte Kind;
    public uint NetId;
    public ushort SceneHash;
    public sbyte KillerSlot;
}

public class SceneReadyData
{
    public ushort SceneHash;
    public uint[] NetIds;
}

public struct EnemyFullSyncEntry
{
    public uint NetId;
    public bool Alive;
    public ushort Hp;
    public float X;
    public float Y;
}

public class EnemyFullSyncData
{
    public ushort SceneHash;
    public EnemyFullSyncEntry[] Enemies;
}

public struct FlagEntry
{
    public string Flag;
    public bool Value;
}

/// <summary>One or more flag changes (single change = 1 entry; bulk = up to MAX_FLAGS_PER_PACKET).</summary>
public class FlagChangeData
{
    public FlagEntry[] Flags;
}

public class GateStateData
{
    public uint GateId;
    public bool Open;
    public ushort SceneHash;
}

public class BossEventData
{
    public const byte KIND_FIGHT_START = 1;
    public const byte KIND_FIGHT_END = 2;
    public byte Kind;
    public uint BossNetId; // 0 = unknown
    public ushort SceneHash;
}

/// <summary>
/// Protocolo y serializador binario de alto rendimiento para el cooperativo en línea P2P.
/// </summary>
public static class CoopNetworkProtocol
{
    public const byte CURRENT_PROTOCOL_VERSION = CoopHandshakeCodec.ProtocolVersion;

    // --- Serializadores ---

    public static byte[] SerializeHandshakeRequest(HandshakeRequestData req)
    {
        return CoopHandshakeCodec.SerializeRequest(req);
    }

    public static HandshakeRequestData DeserializeHandshakeRequest(byte[] data)
    {
        return CoopHandshakeCodec.DeserializeRequest(data);
    }

    public static byte[] SerializeHandshakeAccept(HandshakeAcceptData acc)
    {
        return CoopHandshakeCodec.SerializeAccept(acc);
    }

    public static HandshakeAcceptData DeserializeHandshakeAccept(byte[] data)
    {
        return CoopHandshakeCodec.DeserializeAccept(data);
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

    // --- Enemy sync (v2) ---

    public const int MAX_ENEMIES_PER_STATE_PACKET = 60;

    /// <summary>Splits a list of entries into chunks of at most MAX_ENEMIES_PER_STATE_PACKET.</summary>
    public static System.Collections.Generic.List<EnemyStateEntry[]> ChunkEnemyStates(System.Collections.Generic.IList<EnemyStateEntry> all)
    {
        var result = new System.Collections.Generic.List<EnemyStateEntry[]>();
        if (all == null) return result;
        for (int i = 0; i < all.Count; i += MAX_ENEMIES_PER_STATE_PACKET)
        {
            int n = Math.Min(MAX_ENEMIES_PER_STATE_PACKET, all.Count - i);
            var chunk = new EnemyStateEntry[n];
            for (int j = 0; j < n; j++) chunk[j] = all[i + j];
            result.Add(chunk);
        }
        return result;
    }

    public static byte[] SerializeEnemyState(EnemyStateData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.EnemyState);
            writer.Write(d.Sequence);
            writer.Write(d.SceneHash);
            int count = d.Enemies != null ? Math.Min(d.Enemies.Length, MAX_ENEMIES_PER_STATE_PACKET) : 0;
            writer.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                var e = d.Enemies[i];
                writer.Write(e.NetId);
                writer.Write(e.X);
                writer.Write(e.Y);
                writer.Write((byte)e.Flags);
                writer.Write(e.AnimShortHash);
                writer.Write(e.AnimNormTime);
                writer.Write(e.Hp);
            }
            return ms.ToArray();
        }
    }

    public static EnemyStateData DeserializeEnemyState(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            EnemyStateData d = new EnemyStateData();
            d.Sequence = reader.ReadUInt16();
            d.SceneHash = reader.ReadUInt16();
            byte count = reader.ReadByte();
            d.Enemies = new EnemyStateEntry[count];
            for (int i = 0; i < count; i++)
            {
                EnemyStateEntry e = new EnemyStateEntry();
                e.NetId = reader.ReadUInt32();
                e.X = reader.ReadSingle();
                e.Y = reader.ReadSingle();
                e.Flags = (EnemyStateFlags)reader.ReadByte();
                e.AnimShortHash = reader.ReadInt32();
                e.AnimNormTime = reader.ReadByte();
                e.Hp = reader.ReadUInt16();
                d.Enemies[i] = e;
            }
            return d;
        }
    }

    public static byte[] SerializeEnemyEvent(EnemyEventData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.EnemyEvent);
            writer.Write(d.Kind);
            writer.Write(d.NetId);
            writer.Write(d.SceneHash);
            writer.Write(d.KillerSlot);
            return ms.ToArray();
        }
    }

    public static EnemyEventData DeserializeEnemyEvent(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            EnemyEventData d = new EnemyEventData();
            d.Kind = reader.ReadByte();
            d.NetId = reader.ReadUInt32();
            d.SceneHash = reader.ReadUInt16();
            d.KillerSlot = reader.ReadSByte();
            return d;
        }
    }

    public static byte[] SerializeSceneReady(SceneReadyData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.SceneReady);
            writer.Write(d.SceneHash);
            int count = d.NetIds != null ? Math.Min(d.NetIds.Length, ushort.MaxValue) : 0;
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++) writer.Write(d.NetIds[i]);
            return ms.ToArray();
        }
    }

    public static SceneReadyData DeserializeSceneReady(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            SceneReadyData d = new SceneReadyData();
            d.SceneHash = reader.ReadUInt16();
            ushort count = reader.ReadUInt16();
            d.NetIds = new uint[count];
            for (int i = 0; i < count; i++) d.NetIds[i] = reader.ReadUInt32();
            return d;
        }
    }

    public static byte[] SerializeEnemyFullSync(EnemyFullSyncData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.EnemyFullSync);
            writer.Write(d.SceneHash);
            int count = d.Enemies != null ? Math.Min(d.Enemies.Length, ushort.MaxValue) : 0;
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var e = d.Enemies[i];
                writer.Write(e.NetId);
                writer.Write(e.Alive);
                writer.Write(e.Hp);
                writer.Write(e.X);
                writer.Write(e.Y);
            }
            return ms.ToArray();
        }
    }

    public static EnemyFullSyncData DeserializeEnemyFullSync(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            EnemyFullSyncData d = new EnemyFullSyncData();
            d.SceneHash = reader.ReadUInt16();
            ushort count = reader.ReadUInt16();
            d.Enemies = new EnemyFullSyncEntry[count];
            for (int i = 0; i < count; i++)
            {
                EnemyFullSyncEntry e = new EnemyFullSyncEntry();
                e.NetId = reader.ReadUInt32();
                e.Alive = reader.ReadBoolean();
                e.Hp = reader.ReadUInt16();
                e.X = reader.ReadSingle();
                e.Y = reader.ReadSingle();
                d.Enemies[i] = e;
            }
            return d;
        }
    }

    // --- World sync (D1): flags / gates / boss events ---

    public const int MAX_FLAGS_PER_PACKET = 150;

    public static byte[] SerializeFlagChange(FlagChangeData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
        {
            writer.Write((byte)CoopPacketType.FlagChange);
            int count = d.Flags != null ? Math.Min(d.Flags.Length, ushort.MaxValue) : 0;
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(d.Flags[i].Flag ?? "");
                writer.Write(d.Flags[i].Value);
            }
            return ms.ToArray();
        }
    }

    public static FlagChangeData DeserializeFlagChange(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
        {
            reader.ReadByte(); // packet type
            FlagChangeData d = new FlagChangeData();
            ushort count = reader.ReadUInt16();
            d.Flags = new FlagEntry[count];
            for (int i = 0; i < count; i++)
            {
                d.Flags[i].Flag = reader.ReadString();
                d.Flags[i].Value = reader.ReadBoolean();
            }
            return d;
        }
    }

    public static byte[] SerializeGateState(GateStateData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.GateState);
            writer.Write(d.GateId);
            writer.Write(d.Open);
            writer.Write(d.SceneHash);
            return ms.ToArray();
        }
    }

    public static GateStateData DeserializeGateState(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            GateStateData d = new GateStateData();
            d.GateId = reader.ReadUInt32();
            d.Open = reader.ReadBoolean();
            d.SceneHash = reader.ReadUInt16();
            return d;
        }
    }

    public static byte[] SerializeBossEvent(BossEventData d)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            writer.Write((byte)CoopPacketType.BossEvent);
            writer.Write(d.Kind);
            writer.Write(d.BossNetId);
            writer.Write(d.SceneHash);
            return ms.ToArray();
        }
    }

    public static BossEventData DeserializeBossEvent(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // packet type
            BossEventData d = new BossEventData();
            d.Kind = reader.ReadByte();
            d.BossNetId = reader.ReadUInt32();
            d.SceneHash = reader.ReadUInt16();
            return d;
        }
    }

    public static byte[] SerializeSimple(CoopPacketType type)
    {
        return new byte[] { (byte)type };
    }
}
