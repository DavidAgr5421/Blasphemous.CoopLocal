using System;
using System.Collections.Generic;

namespace Blasphemous.CoopLocal.MultiPlayer;

// Jugadores locales de ESTE ordenador cuando se juega como cliente online (1 a 3). Lógica pura (sin Unity) para poder
// probarla: qué dispositivo usa cada uno y el intercambio de teclado/mando con F9 solo entre ellos.

public enum OnlineLocalDevice : byte
{
    /// <summary>Teclado + primer mando juntos (comportamiento clásico de un único jugador).</summary>
    Auto = 0,
    Keyboard = 1,
    Pad1 = 2,
    Pad2 = 3,
    Pad3 = 4
}

public sealed class OnlineLocalPlayer
{
    public string Name = "";
    public int ColorIndex = 1;
    public string SkinId = "";
    public OnlineLocalDevice Device = OnlineLocalDevice.Auto;
    /// <summary>Puesto asignado por el anfitrión (-1 hasta aceptar la conexión).</summary>
    public int Slot = -1;
}

public static class OnlineLocalPlayers
{
    public const int Max = 3;

    private static readonly List<OnlineLocalPlayer> players = new List<OnlineLocalPlayer>();

    public static IList<OnlineLocalPlayer> All { get { return players; } }
    public static int Count { get { return players.Count; } }

    /// <summary>Combinaciones de dispositivos válidas para <paramref name="count"/> jugadores (para el selector del menú).</summary>
    public static OnlineLocalDevice[][] Presets(int count)
    {
        switch (count)
        {
            case 1:
                return new[]
                {
                    new[] { OnlineLocalDevice.Auto },
                    new[] { OnlineLocalDevice.Keyboard },
                    new[] { OnlineLocalDevice.Pad1 }
                };
            case 2:
                return new[]
                {
                    new[] { OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1 },
                    new[] { OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2 }
                };
            case 3:
                return new[]
                {
                    new[] { OnlineLocalDevice.Keyboard, OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2 },
                    new[] { OnlineLocalDevice.Pad1, OnlineLocalDevice.Pad2, OnlineLocalDevice.Pad3 }
                };
            default:
                return new[] { new[] { OnlineLocalDevice.Auto } };
        }
    }

    public static string DeviceName(OnlineLocalDevice d)
    {
        switch (d)
        {
            case OnlineLocalDevice.Auto: return "Teclado + Mando";
            case OnlineLocalDevice.Keyboard: return "Teclado";
            case OnlineLocalDevice.Pad1: return "Mando 1";
            case OnlineLocalDevice.Pad2: return "Mando 2";
            case OnlineLocalDevice.Pad3: return "Mando 3";
        }
        return d.ToString();
    }

    /// <summary>Índice del mando (0..2) que usa un dispositivo, o -1 si no es un mando.</summary>
    public static int PadIndexOf(OnlineLocalDevice d)
    {
        switch (d)
        {
            case OnlineLocalDevice.Pad1: return 0;
            case OnlineLocalDevice.Pad2: return 1;
            case OnlineLocalDevice.Pad3: return 2;
        }
        return -1;
    }

    /// <summary>Define los jugadores locales (los extras heredan nombre/skin del principal con otro color).</summary>
    public static void Configure(string name, int colorIndex, string skinId, OnlineLocalDevice[] devices, int paletteSize)
    {
        players.Clear();
        int n = Math.Max(1, Math.Min(devices == null ? 1 : devices.Length, Max));
        for (int i = 0; i < n; i++)
        {
            OnlineLocalPlayer p = new OnlineLocalPlayer();
            p.Name = i == 0 ? name : (name + " " + (i + 1));
            p.ColorIndex = paletteSize > 0 ? (colorIndex + i) % paletteSize : colorIndex;
            p.SkinId = skinId;
            p.Device = devices == null ? OnlineLocalDevice.Auto : devices[i];
            players.Add(p);
        }
    }

    /// <summary>Se queda solo con los primeros <paramref name="accepted"/> jugadores (el anfitrión pudo no tener sitio para todos).</summary>
    public static void TrimTo(int accepted)
    {
        if (accepted < 1) accepted = 1;
        while (players.Count > accepted) players.RemoveAt(players.Count - 1);
    }

    public static void Clear() { players.Clear(); }

    /// <summary>
    /// F9: pasa el teclado al siguiente jugador local y este le cede su mando al anterior (intercambio de dispositivos).
    /// Devuelve false si no hay nada que intercambiar (un solo jugador o nadie usa teclado). <paramref name="newHolder"/>
    /// es el índice local que queda con el teclado.
    /// </summary>
    public static bool CycleKeyboard(out int newHolder)
    {
        newHolder = -1;
        if (players.Count < 2) return false;
        int holder = -1;
        for (int i = 0; i < players.Count; i++)
            if (players[i].Device == OnlineLocalDevice.Keyboard) { holder = i; break; }
        if (holder < 0) return false;

        int next = (holder + 1) % players.Count;
        OnlineLocalDevice nextDevice = players[next].Device;
        players[next].Device = OnlineLocalDevice.Keyboard;
        players[holder].Device = nextDevice;
        newHolder = next;
        return true;
    }

    /// <summary>Jugador local que controla un puesto del anfitrión, o null.</summary>
    public static OnlineLocalPlayer BySlot(int slot)
    {
        for (int i = 0; i < players.Count; i++) if (players[i].Slot == slot) return players[i];
        return null;
    }
}
