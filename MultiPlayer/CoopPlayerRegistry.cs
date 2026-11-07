using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Ronda 88: Arquitectura base multi-jugador (P1..P4) para identidades, nombres, colores,
// skins, menús y escalabilidad futura hacia P3 y P4 sin romper la compatibilidad con P2.
internal sealed class CoopPlayerSlot
{
    public int PlayerIndex { get; } // 0 = P1, 1 = P2, 2 = P3, 3 = P4
    public string DisplayName { get; set; }
    public Color32 LabelColor { get; set; }
    public Color32 OutlineColor { get; set; }
    public string SkinPaletteId { get; set; }
    public Vector2 DefaultHudPosition { get; set; }
    public Penitent Instance { get; internal set; }

    public CoopPlayerSlot(
        int playerIndex,
        string displayName,
        Color32 labelColor,
        Color32 outlineColor,
        string skinPaletteId,
        Vector2 defaultHudPosition)
    {
        PlayerIndex = playerIndex;
        DisplayName = displayName;
        LabelColor = labelColor;
        OutlineColor = outlineColor;
        SkinPaletteId = skinPaletteId;
        DefaultHudPosition = defaultHudPosition;
    }

    public string MenuTitle => $"{DisplayName} menu";
    public string BracketTag => $"[{DisplayName}]";
}

internal static class CoopPlayerRegistry
{
    internal const int MaxPlayers = 4;

    private static readonly CoopPlayerSlot[] slots = new CoopPlayerSlot[MaxPlayers]
    {
        // Slot 0: P1 (Anfitrión / Host)
        new CoopPlayerSlot(
            playerIndex: 0,
            displayName: "Pan-chan",
            labelColor: new Color32(235, 36, 10, 255),
            outlineColor: new Color32(140, 14, 0, 190),
            skinPaletteId: "PENITENT_DEFAULT",
            defaultHudPosition: new Vector2(0f, 0f)),

        // Slot 1: P2
        new CoopPlayerSlot(
            playerIndex: 1,
            displayName: "Baby",
            labelColor: new Color32(55, 247, 70, 255),
            outlineColor: new Color32(39, 94, 73, 190),
            skinPaletteId: "PENITENT_ENDING_B",
            defaultHudPosition: new Vector2(-119f, -20f)),

        // Slot 2: P3 (Preparado para futura expansión)
        new CoopPlayerSlot(
            playerIndex: 2,
            displayName: "P3",
            labelColor: new Color32(65, 185, 255, 255),
            outlineColor: new Color32(20, 60, 120, 190),
            skinPaletteId: "PENITENT_ENDING_A",
            defaultHudPosition: new Vector2(-119f, -110f)),

        // Slot 3: P4 (Preparado para futura expansión)
        new CoopPlayerSlot(
            playerIndex: 3,
            displayName: "P4",
            labelColor: new Color32(255, 215, 60, 255),
            outlineColor: new Color32(120, 85, 15, 190),
            skinPaletteId: "PENITENT_OSSUARY",
            defaultHudPosition: new Vector2(-119f, -200f)),
    };

    private static readonly Dictionary<Penitent, CoopPlayerSlot> instanceMap = new Dictionary<Penitent, CoopPlayerSlot>();

    internal static CoopPlayerSlot GetSlot(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayers)
        {
            return slots[0];
        }
        return slots[playerIndex];
    }

    internal static void RegisterInstance(int playerIndex, Penitent penitent)
    {
        CoopPlayerSlot slot = GetSlot(playerIndex);
        if (slot.Instance != null && instanceMap.ContainsKey(slot.Instance))
        {
            instanceMap.Remove(slot.Instance);
        }
        slot.Instance = penitent;
        if (penitent != null)
        {
            instanceMap[penitent] = slot;
        }
    }

    internal static bool TryGetSlot(Penitent penitent, out CoopPlayerSlot slot)
    {
        if (penitent == null)
        {
            slot = null;
            return false;
        }
        if (instanceMap.TryGetValue(penitent, out slot))
        {
            return true;
        }
        if (penitent == CoopLocal.Player2)
        {
            slot = slots[1];
            return true;
        }
        if (Core.Logic != null && penitent == Core.Logic.Penitent)
        {
            slot = slots[0];
            return true;
        }
        slot = null;
        return false;
    }

    internal static bool IsGuestPlayer(Penitent penitent, out CoopPlayerSlot slot)
    {
        if (TryGetSlot(penitent, out slot) && slot.PlayerIndex > 0)
        {
            return true;
        }
        slot = null;
        return false;
    }

    internal static int GetPlayerIndex(Penitent penitent)
    {
        if (penitent == null) return -1;
        if (TryGetSlot(penitent, out CoopPlayerSlot slot)) return slot.PlayerIndex;
        if (penitent == CoopLocal.Player2) return 1;
        if (penitent == CoopLocal.Player3) return 2;
        if (penitent == CoopLocal.Player4) return 3;
        if (Core.Logic != null && penitent == Core.Logic.Penitent) return 0;
        return -1;
    }

    internal static Penitent GetPenitent(int playerIndex)
    {
        if (playerIndex == 0) return Core.Logic != null ? Core.Logic.Penitent : null;
        if (playerIndex == 1) return CoopLocal.Player2;
        if (playerIndex == 2) return CoopLocal.Player3;
        if (playerIndex == 3) return CoopLocal.Player4;
        return GetSlot(playerIndex)?.Instance;
    }

    internal static string GetDisplayName(int playerIndex) => GetSlot(playerIndex).DisplayName;
    internal static string GetMenuTitle(int playerIndex) => GetSlot(playerIndex).MenuTitle;
    internal static Color32 GetLabelColor(int playerIndex) => GetSlot(playerIndex).LabelColor;
    internal static Color32 GetOutlineColor(int playerIndex) => GetSlot(playerIndex).OutlineColor;
}
