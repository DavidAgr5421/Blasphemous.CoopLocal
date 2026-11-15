using Blasphemous.CoopLocal.MultiPlayer;
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
using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

// On-screen "Controller mode" / "Keyboard mode" text, top-right corner - so P2's active input
// scheme never has to be guessed from feel/trial-and-error alone. Built from a bare Canvas +
// UI.Text rather than TextMeshPro (avoids needing a font asset reference) since this mod has no
// existing Canvas/uGUI infrastructure to build on. Created once, lazily, and left alive for the
// whole session (DontDestroyOnLoad) rather than tied to either player's own lifetime - the mode
// itself isn't tied to a spawned player either.
internal static class Player2ModeIndicator
{
    // Round 50: confirmed via a one-shot dump of every font asset loaded in memory
    // (Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) that the game's real Latin-alphabet TMP
    // font is "MajesticExtended_FullLatin" - the other names found were either locale-specific
    // font swaps (Russian/Chinese/Korean/Japanese) or TMP's own LiberationSans SDF fallback. It's
    // a serialized asset reference, not something loadable by path/code, so it has to be found
    // this way rather than referenced directly.
    private const string GameFontName = "MajesticExtended_FullLatin";

    private static TMPro.TextMeshProUGUI label;

    private const float DisplaySeconds = 5f;
    private static float visibleUntil;

    internal static void SetMenuOverlayHidden(bool hidden)
    {
        if (label != null)
        {
            label.gameObject.SetActive(!hidden && Time.unscaledTime < visibleUntil);
        }
    }

    /// <summary>Aviso discreto (arriba a la derecha) de qué jugador tiene el teclado; desaparece solo a los 5 s.</summary>
    internal static void ShowPlayer(int playerIndex, string playerName)
    {
        EnsureCreated();
        if (label != null)
        {
            visibleUntil = Time.unscaledTime + DisplaySeconds;
            label.gameObject.SetActive(true);
            label.text = $"Teclado: P{playerIndex + 1} ({playerName})";
            label.color = CoopPlayerRegistry.GetLabelColor(playerIndex);
        }
    }

    internal static void Show(Player2InputMode mode)
    {
        int kbIdx = Player2Input.ActiveKeyboardPlayerIndex;
        ShowPlayer(kbIdx, CoopPlayerRegistry.GetDisplayName(kbIdx));
    }

    private sealed class Fader : MonoBehaviour
    {
        private void Update()
        {
            if (label != null && label.gameObject.activeSelf && Time.unscaledTime >= visibleUntil)
                label.gameObject.SetActive(false);
        }
    }

    private static void EnsureCreated()
    {
        if (label != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("CoopLocalModeIndicatorCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject textObject = new GameObject("ModeText");
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -16f);
        rect.sizeDelta = new Vector2(480f, 40f);

        label = textObject.AddComponent<TMPro.TextMeshProUGUI>();
        TMPro.TMP_FontAsset gameFont = Array.Find(
            Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(),
            f => f.name == GameFontName);
        if (gameFont != null)
        {
            label.font = gameFont;
        }
        label.fontSize = 22;
        label.alignment = TMPro.TextAlignmentOptions.TopRight;
        label.color = Color.white;
        label.text = "";
        label.fontSize = 20;
        canvasObject.AddComponent<Fader>();
        textObject.SetActive(false);
    }
}
