using System.Collections.Generic;
using Blasphemous.CoopLocal.MultiPlayer;
using Xunit;

namespace Blasphemous.CoopLocal.Tests;

public class CoopHandshakeTests
{
    [Fact]
    public void Request_RoundTrip_KeepsEveryLocalPlayer()
    {
        var req = new HandshakeRequestData();
        req.Players.Add(new HandshakePlayer { Name = "Ana", ColorIndex = 2, SkinId = "PENITENT", Device = 1 });
        req.Players.Add(new HandshakePlayer { Name = "Ana 2", ColorIndex = 3, SkinId = "PENITENT_DEMAKE", Device = 2 });

        HandshakeRequestData back = CoopHandshakeCodec.DeserializeRequest(CoopHandshakeCodec.SerializeRequest(req));

        Assert.Equal(CoopHandshakeCodec.ProtocolVersion, back.ProtocolVersion);
        Assert.Equal(2, back.Players.Count);
        Assert.Equal("Ana 2", back.Players[1].Name);
        Assert.Equal(3, back.Players[1].ColorIndex);
        Assert.Equal("PENITENT_DEMAKE", back.Players[1].SkinId);
        Assert.Equal((byte)2, back.Players[1].Device);
    }

    [Fact]
    public void Request_CapsPlayersAtMax()
    {
        var req = new HandshakeRequestData();
        for (int i = 0; i < 6; i++) req.Players.Add(new HandshakePlayer { Name = "P" + i });
        var back = CoopHandshakeCodec.DeserializeRequest(CoopHandshakeCodec.SerializeRequest(req));
        Assert.Equal(CoopHandshakeCodec.MaxPlayersPerClient, back.Players.Count);
    }

    [Fact]
    public void Request_FromOtherVersion_IsReadWithoutPlayers()
    {
        // Un cliente antiguo (v2) enviaba nombre/color/skin sin lista: no debe romper la lectura, el host lo rechaza por versión.
        byte[] legacy;
        using (var ms = new System.IO.MemoryStream())
        using (var w = new System.IO.BinaryWriter(ms))
        {
            w.Write(CoopHandshakeCodec.PacketRequest);
            w.Write((byte)2);
            w.Write("Viejo");
            w.Write(1);
            w.Write("PENITENT");
            legacy = ms.ToArray();
        }
        var back = CoopHandshakeCodec.DeserializeRequest(legacy);
        Assert.Equal((byte)2, back.ProtocolVersion);
        Assert.Empty(back.Players);
    }

    [Fact]
    public void Accept_RoundTrip_ReturnsAllAssignedSlots()
    {
        var acc = new HandshakeAcceptData
        {
            AssignedSlots = new byte[] { 1, 3 },
            CurrentScene = "D01Z01S01",
            HostPlayerName = "Host",
            HostColorIndex = 4,
            HostSkinId = "PENITENT"
        };
        var back = CoopHandshakeCodec.DeserializeAccept(CoopHandshakeCodec.SerializeAccept(acc));
        Assert.Equal(new byte[] { 1, 3 }, back.AssignedSlots);
        Assert.Equal((byte)1, back.AssignedSlot);
        Assert.Equal("D01Z01S01", back.CurrentScene);
        Assert.Equal("Host", back.HostPlayerName);
    }

    [Fact]
    public void Allocator_PrefersSlotsReservedByHost()
    {
        var reserved = new[] { false, false, true, false };   // el host reservó P3 como online
        var other = new[] { false, true, false, true };       // P2 y P4 sin asignar
        List<int> slots = CoopSlotAllocator.Allocate(1, reserved, other);
        Assert.Equal(new List<int> { 2 }, slots);
    }

    [Fact]
    public void Allocator_TwoPlayers_UsesReservedThenFree()
    {
        var reserved = new[] { false, false, true, false };
        var other = new[] { false, true, false, true };
        List<int> slots = CoopSlotAllocator.Allocate(2, reserved, other);
        Assert.Equal(new List<int> { 1, 2 }, slots);
    }

    [Fact]
    public void Allocator_GrantsOnlyWhatIsAvailable()
    {
        var reserved = new[] { false, false, true, false };
        var other = new[] { false, false, false, false };
        Assert.Equal(new List<int> { 2 }, CoopSlotAllocator.Allocate(3, reserved, other));
    }

    [Fact]
    public void Allocator_NoRoom_ReturnsEmpty_AndNeverGivesHostSlot()
    {
        var none = new[] { true, false, false, false };   // aunque el array marque el puesto 0, el anfitrión nunca se asigna
        Assert.Empty(CoopSlotAllocator.Allocate(2, none, none));
    }
}

public class OnlineLocalPlayersTests
{
    private static void Setup(params OnlineLocalDevice[] devices)
    {
        OnlineLocalPlayers.Configure("Ana", 2, "PENITENT", devices, 8);
    }

    [Fact]
    public void Configure_ExtraPlayersGetDerivedNameAndColor()
    {
        Setup(OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2);
        Assert.Equal(3, OnlineLocalPlayers.Count);
        Assert.Equal("Ana", OnlineLocalPlayers.All[0].Name);
        Assert.Equal("Ana 2", OnlineLocalPlayers.All[1].Name);
        Assert.Equal(4, OnlineLocalPlayers.All[2].ColorIndex);
        Assert.Equal("PENITENT", OnlineLocalPlayers.All[2].SkinId);
    }

    [Fact]
    public void CycleKeyboard_SwapsKeyboardAndPadBetweenLocalPlayersOnly()
    {
        Setup(OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1);
        Assert.True(OnlineLocalPlayers.CycleKeyboard(out int holder));
        Assert.Equal(1, holder);
        Assert.Equal(OnlineLocalDevice.Keyboard, OnlineLocalPlayers.All[1].Device);
        Assert.Equal(OnlineLocalDevice.Pad1, OnlineLocalPlayers.All[0].Device);

        Assert.True(OnlineLocalPlayers.CycleKeyboard(out holder));
        Assert.Equal(0, holder);
        Assert.Equal(OnlineLocalDevice.Keyboard, OnlineLocalPlayers.All[0].Device);
        Assert.Equal(OnlineLocalDevice.Pad1, OnlineLocalPlayers.All[1].Device);
    }

    [Fact]
    public void CycleKeyboard_ThreePlayers_RotatesAndKeepsEveryPadUnique()
    {
        Setup(OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2);
        for (int i = 0; i < 6; i++)
        {
            Assert.True(OnlineLocalPlayers.CycleKeyboard(out _));
            var seen = new HashSet<OnlineLocalDevice>();
            foreach (var p in OnlineLocalPlayers.All) Assert.True(seen.Add(p.Device));
            Assert.Contains(OnlineLocalDevice.Keyboard, seen);
        }
    }

    [Fact]
    public void CycleKeyboard_SinglePlayer_DoesNothing()
    {
        Setup(OnlineLocalDevice.Auto);
        Assert.False(OnlineLocalPlayers.CycleKeyboard(out int holder));
        Assert.Equal(-1, holder);
        Assert.Equal(OnlineLocalDevice.Auto, OnlineLocalPlayers.All[0].Device);
    }

    [Fact]
    public void CycleKeyboard_NobodyHasKeyboard_DoesNothing()
    {
        Setup(OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2);
        Assert.False(OnlineLocalPlayers.CycleKeyboard(out _));
    }

    [Fact]
    public void TrimTo_KeepsOnlyAcceptedPlayers_AndBySlotFindsThem()
    {
        Setup(OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2);
        OnlineLocalPlayers.All[0].Slot = 1;
        OnlineLocalPlayers.All[1].Slot = 3;
        OnlineLocalPlayers.TrimTo(2);
        Assert.Equal(2, OnlineLocalPlayers.Count);
        Assert.Equal("Ana 2", OnlineLocalPlayers.BySlot(3).Name);
        Assert.Null(OnlineLocalPlayers.BySlot(2));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Presets_HaveExpectedShape(int count, int presetCount)
    {
        var presets = OnlineLocalPlayers.Presets(count);
        Assert.Equal(presetCount, presets.Length);
        foreach (var preset in presets) Assert.Equal(count, preset.Length);
    }
}
