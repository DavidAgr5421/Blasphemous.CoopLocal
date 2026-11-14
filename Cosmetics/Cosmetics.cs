using Tools.Level.Interactables;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Gestión de aspectos / skins cosméticas para los jugadores cooperativos (P2, P3, P4).
/// Permite que cada jugador invitado luzca su skin personalizada elegida desde el menú de inicio
/// o persistida en la configuración, sobreescribiendo per-instancia la textura del ColorPaletteSwapper.
/// </summary>
[HarmonyPatch(typeof(ColorPaletteSwapper), "SetMaterial")]
internal static class ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch
{
    internal static string GetPlayerPaletteId(int playerIndex)
    {
        if (playerIndex >= 0 && playerIndex < CoopGameConfig.Slots.Length)
        {
            string skinId = CoopGameConfig.Slots[playerIndex].SkinId;
            if (!string.IsNullOrEmpty(skinId)) return skinId;
        }
        string regSkin = CoopPlayerRegistry.GetSlot(playerIndex)?.SkinPaletteId;
        if (!string.IsNullOrEmpty(regSkin)) return regSkin;

        if (playerIndex == 0 && Core.ColorPaletteManager != null)
        {
            return Core.ColorPaletteManager.GetCurrentColorPaletteId();
        }
        return "PENITENT_DEFAULT";
    }

    internal static Texture2D GetPlayerPaletteTexture(int playerIndex)
    {
        string paletteId = GetPlayerPaletteId(playerIndex);
        if (string.IsNullOrEmpty(paletteId) || Core.ColorPaletteManager == null)
        {
            return null;
        }
        Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
        return paletteSprite != null ? paletteSprite.texture : null;
    }

    internal static Texture2D GetPlayer2PaletteTexture()
    {
        return GetPlayerPaletteTexture(1);
    }

    private static void Postfix(ColorPaletteSwapper __instance)
    {
        if (__instance == null) return;

        // Caso 1: Swapper en una instancia de jugador (P1, P2, P3, P4)
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            if (CoopPlayerRegistry.TryGetSlot(owner, out CoopPlayerSlot slot) && slot.PlayerIndex > 0)
            {
                string paletteId = slot.SkinPaletteId;
                if (string.IsNullOrEmpty(paletteId))
                {
                    paletteId = GetPlayerPaletteId(slot.PlayerIndex);
                }
                ApplySkinToSwapper(__instance, paletteId);
            }
            return;
        }

        // Caso 2: Swapper dentro de una ejecución (Execution)
        Execution exec = __instance.GetComponentInParent<Execution>();
        if (exec != null)
        {
            Penitent executor = exec.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
            if (executor != null)
            {
                int pIndex = CoopPlayerRegistry.GetPlayerIndex(executor);
                string paletteId = GetPlayerPaletteId(pIndex);
                if (!string.IsNullOrEmpty(paletteId))
                {
                    ApplySkinToSwapper(__instance, paletteId);
                }
            }
            return;
        }

        // Caso 3: Swapper dentro de una FakeExecution (ej. Crisanta u otras cinemáticas)
        FakeExecution fakeExec = __instance.GetComponentInParent<FakeExecution>();
        if (fakeExec != null)
        {
            Penitent executor = fakeExec.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
            if (executor != null)
            {
                int pIndex = CoopPlayerRegistry.GetPlayerIndex(executor);
                string paletteId = GetPlayerPaletteId(pIndex);
                if (!string.IsNullOrEmpty(paletteId))
                {
                    ApplySkinToSwapper(__instance, paletteId);
                }
            }
            return;
        }
    }

    internal static void ApplySkinToSwapper(ColorPaletteSwapper swapper, string paletteId)
    {
        if (swapper == null || string.IsNullOrEmpty(paletteId) || Core.ColorPaletteManager == null)
        {
            return;
        }

        Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
        if (paletteSprite == null)
        {
            return;
        }

        SpriteRenderer spriteRenderer = swapper.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            return;
        }

        Texture2D paletteTexture = paletteSprite.texture;
        if (paletteTexture == null)
        {
            return;
        }

        spriteRenderer.material.SetTexture("_PaletteTex", paletteTexture);
        if (swapper.extraMaterial != null)
        {
            swapper.extraMaterial.SetTexture("_PaletteTex", paletteTexture);
        }
    }

    internal static void RefreshAllGuestPlayerSkins()
    {
        for (int i = 1; i < CoopPlayerRegistry.MaxPlayers; i++)
        {
            var slot = CoopPlayerRegistry.GetSlot(i);
            if (slot != null && slot.Instance != null)
            {
                var swapper = slot.Instance.GetComponentInChildren<ColorPaletteSwapper>();
                if (swapper != null)
                {
                    ApplySkinToSwapper(swapper, slot.SkinPaletteId);
                }
            }
        }
    }
}
