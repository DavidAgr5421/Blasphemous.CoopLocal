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

// Round 40: temporary dev tool, NOT meant to ship long-term - every position value in
// Player2HealthBar/Player2FervourBar/Player2PurgePoints so far has been a guess, since this
// environment can't screenshot the live HUD to check placement. Lets the user interactively
// reposition each cloned widget in-game instead: arrow keys nudge whichever one is currently
// selected, "+" (Keypad or the plain top-row key, either works) cycles Life -> Fervour ->
// Currency -> Life, an on-screen label shows which one is selected, and every move is logged as
// "[HudTuner] <Target> position now: (x, y)" - once a widget looks right, copy that line's
// coordinates into the matching AnchoredPosition field above (Player2HealthBar.AnchoredPosition
// etc.) to make it permanent, and this whole class can be deleted afterwards. Caveat: arrow keys
// may double as P1's own alternate movement binding in Rewired (same keyboard-map overlap this
// file's history is full of) - fine for a one-off tuning session, just don't expect to also
// actively play P1 with arrows at the same time as tuning.
internal static class Player2HudPositionTuner
{
    private enum Target
    {
        Life,
        Fervour,
        Currency,
        CurrencyIcon,
    }

    private const float MoveStep = 1f;

    // Round 44: "." grows, "-" shrinks the currently selected widget by 5% per press - lets the
    // user compare sizes live instead of guessing a fixed scale blind.
    private const float ScaleStep = 0.05f;

    private static Target current = Target.Life;
    private static Text label;

    internal static void Tick()
    {
        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
        {
            current = current == Target.CurrencyIcon ? Target.Life : current + 1;
            ShowLabel();
        }

        EnsureLabelShown();

        if (Input.GetKeyDown(KeyCode.Period))
        {
            AdjustScale(1f + ScaleStep);
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            AdjustScale(1f - ScaleStep);
        }

        RectTransform rect = GetTargetRect();
        if (rect == null)
        {
            return;
        }

        Vector2 move = Vector2.zero;
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            move.y += MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            move.y -= MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            move.x -= MoveStep;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            move.x += MoveStep;
        }
        if (move == Vector2.zero)
        {
            return;
        }

        rect.anchoredPosition += move;
        SaveAndLog(rect.anchoredPosition);
    }

    private static void AdjustScale(float factor)
    {
        RectTransform rect = GetTargetRect();
        if (rect == null)
        {
            return;
        }
        rect.localScale *= factor;
        float newScale;
        switch (current)
        {
            case Target.Life:
                Player2HealthBar.Scale *= factor;
                newScale = Player2HealthBar.Scale;
                break;
            case Target.Fervour:
                Player2FervourBar.Scale *= factor;
                newScale = Player2FervourBar.Scale;
                break;
            case Target.CurrencyIcon:
                Player2PurgePoints.IconScale *= factor;
                newScale = Player2PurgePoints.IconScale;
                break;
            default:
                Player2PurgePoints.TextScale *= factor;
                newScale = Player2PurgePoints.TextScale;
                break;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[HudTuner] {current} scale now: {newScale:F3}", Main.CoopLocal);
        }
    }

    private static RectTransform GetTargetRect()
    {
        switch (current)
        {
            case Target.Life:
                return Player2HealthBar.CloneRect;
            case Target.Fervour:
                return Player2FervourBar.CloneRect;
            case Target.CurrencyIcon:
                return Player2PurgePoints.IconRect;
            default:
                return Player2PurgePoints.CloneRect;
        }
    }

    private static void SaveAndLog(Vector2 position)
    {
        switch (current)
        {
            case Target.Life:
                Player2HealthBar.AnchoredPosition = position;
                break;
            case Target.Fervour:
                Player2FervourBar.AnchoredPosition = position;
                break;
            case Target.CurrencyIcon:
                Player2PurgePoints.IconAnchoredPosition = position;
                break;
            default:
                Player2PurgePoints.AnchoredPosition = position;
                break;
        }
        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[HudTuner] {current} position now: ({position.x:F0}, {position.y:F0})", Main.CoopLocal);
        }
    }

    private static void EnsureLabelShown()
    {
        if (label != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("HudTunerLabelCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject textObject = new GameObject("HudTunerLabel");
        textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -16f);
        rect.sizeDelta = new Vector2(500f, 40f);

        label = textObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 22;
        label.alignment = TextAnchor.UpperCenter;
        label.color = Color.yellow;
        ShowLabel();
    }

    private static void ShowLabel()
    {
        if (label == null)
        {
            return;
        }
        label.text = $"HUD Tuner: {current} Mode  (arrows = move, + = switch, . / - = scale)";
    }
}


