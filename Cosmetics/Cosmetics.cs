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
        return CoopPlayerRegistry.GetSlot(playerIndex)?.SkinPaletteId ?? "PENITENT_ENDING_B";
    }

    internal static Texture2D GetPlayer2PaletteTexture()
    {
        string paletteId = GetPlayerPaletteId(1);
        if (string.IsNullOrEmpty(paletteId) || Core.ColorPaletteManager == null)
        {
            return null;
        }
        Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
        return paletteSprite != null ? paletteSprite.texture : null;
    }

    private static void Postfix(ColorPaletteSwapper __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null)
        {
            return;
        }

        // P1 mantiene su skin vanilla seleccionada en el menú Extras.
        // Para P2, P3 y P4 (o cualquier invitado registrado):
        if (CoopPlayerRegistry.TryGetSlot(owner, out CoopPlayerSlot slot) && slot.PlayerIndex > 0)
        {
            string paletteId = slot.SkinPaletteId;
            if (string.IsNullOrEmpty(paletteId))
            {
                paletteId = GetPlayerPaletteId(slot.PlayerIndex);
            }

            ApplySkinToSwapper(__instance, paletteId);
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
