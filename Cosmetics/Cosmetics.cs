using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbClifLede;
using Gameplay.GameControllers.AnimationBehaviours.Player.ClimbLadder;
using Gameplay.GameControllers.AnimationBehaviours.Player.Crouch;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dash;
using Gameplay.GameControllers.AnimationBehaviours.Player.Hurt;
using Gameplay.GameControllers.AnimationBehaviours.Player.Jump;
using Gameplay.GameControllers.AnimationBehaviours.Player.Dead;
using Gameplay.GameControllers.AnimationBehaviours.Player.Prayer;
using Gameplay.GameControllers.AnimationBehaviours.Player.RangeAttack;
using Gameplay.GameControllers.AnimationBehaviours.Player.Run;
using Gameplay.GameControllers.AnimationBehaviours.Player.SubStatesBehaviours;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Environment.AreaEffects;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using Gameplay.GameControllers.Penitent.Gizmos;
using Gameplay.GameControllers.Penitent.InputSystem;
using Gameplay.GameControllers.Penitent.Sensor;
using Gameplay.UI.Others.UIGameLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// Forces P2 to always wear the "True Apostasy" ("Verdadera Apostasia") skin, independent of
// whatever skin P1 has selected from the Extras menu. ColorPaletteSwapper.SetMaterial() (on
// the same GameObject as the character's own SpriteRenderer, so it's genuinely per-instance)
// reads Core.ColorPaletteManager's single *global* current-skin id and writes the matching
// texture into the "_PaletteTex" slot on this instance's own material - since that id isn't
// per-character, P1 and P2 would otherwise always end up wearing the exact same skin. This
// Postfix runs after the original (harmless - it only overwrites the texture a second time)
// and, only for P2's own instance, re-applies the True Apostasy palette instead. Runs on every
// call rather than just the initial Start() one, so P2 stays forced even if SetMaterial() is
// ever invoked again later (menu skin change, respawn, etc).
[HarmonyPatch(typeof(ColorPaletteSwapper), "SetMaterial")]
internal static class ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch
{
    // Round 36: "PAL_Penitent_ALT2" (a community modding doc's id) was confirmed WRONG - the
    // [ColorPalette] log dump of this game's real ids came back as: PENITENT_DEFAULT,
    // PENITENT_ENDING_A, PENITENT_ENDING_B, PENITENT_OSSUARY, PENITENT_BACKER, PENITENT_DELUXE,
    // PENITENT_ALMS, PENITENT_PE01/02/03, PENITENT_BOSSRUSH(_S), PENITENT_DEMAKE,
    // PENITENT_ENDING_C, PENITENT_SIERPES, PENITENT_ISIDORA, PENITENT_GAMEBOY, PENITENT_KONAMI -
    // no "ALT2" anywhere, so the ids are clearly named per-ending, not per "ALT" slot like the
    // community doc assumed. Per external research (blasphemous.wiki.gg/wiki/Skins), True
    // Apostasy unlocks from completing Ending B ("The Path of the Unworthy") - or from Ending A
    // specifically on a first playthrough, a secondary special case - so PENITENT_ENDING_B is the
    // best-effort match for the *general* unlock path. Still not visually confirmed - if this
    // renders the wrong (but validly-existing, so no fallback/log fires) palette, it's most likely
    // actually PENITENT_ENDING_A instead; there's no way to tell which without a screenshot.
    private const string TrueApostasyPaletteId = "PENITENT_ENDING_B";

    private static bool resolveAttempted;
    private static string resolvedPaletteId;

    private static string ResolveTrueApostasyPaletteId()
    {
        if (resolveAttempted)
        {
            return resolvedPaletteId;
        }
        resolveAttempted = true;

        List<string> allIds = Core.ColorPaletteManager.GetAllColorPalettesId();
        if (allIds == null)
        {
            return null;
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[ColorPalette] all known palette ids: {string.Join(", ", allIds.ToArray())}", Main.CoopLocal);
        }

        if (allIds.Contains(TrueApostasyPaletteId))
        {
            resolvedPaletteId = TrueApostasyPaletteId;
            return resolvedPaletteId;
        }

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"[ColorPalette] could not find '{TrueApostasyPaletteId}' in the list above - " +
                "P2's skin will NOT be forced. Pick the right id from that list.",
                Main.CoopLocal);
        }
        return null;
    }

    private static void Postfix(ColorPaletteSwapper __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || owner != CoopLocal.Player2)
        {
            return;
        }

        string paletteId = ResolveTrueApostasyPaletteId();
        if (paletteId == null)
        {
            return;
        }

        Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
        if (paletteSprite == null)
        {
            return;
        }

        SpriteRenderer spriteRenderer = __instance.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            return;
        }

        Texture2D paletteTexture = paletteSprite.texture;
        spriteRenderer.material.SetTexture("_PaletteTex", paletteTexture);
        if (__instance.extraMaterial != null)
        {
            __instance.extraMaterial.SetTexture("_PaletteTex", paletteTexture);
        }
    }
}


