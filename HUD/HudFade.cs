using DG.Tweening;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Round 57 - user asked for P2's HUD (health/fervour/currency) to fade in/out smoothly instead of
// the round-56 binary SetActive pop, both for room-transition sync (Player2HudFadeSync) and for
// the widgets' own first-time appearance.
//
// Vanilla's own screen fade (Gameplay.UI.Widgets.FadeWidget, decompiled) animates via DOTween too
// (FadeAfterDelay: `black.DOColor(target, duration)`) - matched here for a consistent feel.
// Duration: FadeWidget.Fade()/FadeCoroutine()'s own *default* parameter is 1.5s, but that default
// is never actually used for a normal room transition - the real call sites are
// Tools.Level.Interactables.Door.OnUse (`FadeWidget.instance.Fade(toBlack: true, 0.2f, ...)`) and
// Gameplay.UI.UIController.ShowLoad's own fade-back-to-visible (also 0.2f) - both decompiled and
// confirmed 0.2s, which is the timing actually visible to players during a room load. Used here
// instead of the unused 1.5s default so P2's HUD fade genuinely matches what's on screen.
//
// Tweening mechanism: this game's bundled DOTween.dll has no DOTweenModuleUI (no
// CanvasGroup.DOFade shortcut extension - confirmed absent by decompiling DOTween.dll itself,
// unlike the SpriteRenderer/Image.DOFade calls found throughout the game's own code, which come
// from the Sprite/UI modules that evidently weren't included in this stripped build). A plain
// `DOTween.To(getter, setter, ...)` generic float tween drives CanvasGroup.alpha directly instead
// - same tweening engine/easing DOTween.Kill/etc. everywhere else in this mod already uses
// (WallJump.cs, DashAndInputBlockers.cs), just without the convenience extension method.
internal static class HudFade
{
    internal const float Duration = 0.2f;

    internal static CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        if (root == null)
        {
            return null;
        }
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = root.AddComponent<CanvasGroup>();
        }
        return group;
    }

    // Called right after a widget's root is (re)created (EnsureCreated) - starts it fully
    // transparent but still active (DOTween needs the GameObject active to actually animate it)
    // instead of Instantiate's default alpha-1/pop-visible. The caller (Player2HudFadeSync.
    // ApplyCurrentFadeState, run right after every EnsureCreated in CoopLocal.OnPlayerSpawn)
    // decides afterward whether to fade it in immediately (no screen fade in progress right now)
    // or leave it at 0 until the next FadeWidget.OnFadeHidedEnd (screen fade already covering
    // things, e.g. spawning mid room-load).
    internal static void PrepareHidden(GameObject root)
    {
        if (root == null)
        {
            return;
        }
        CanvasGroup group = EnsureCanvasGroup(root);
        DOTween.Kill(group);
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    // `instant`: skips the tween entirely - only for the "a screen fade is already covering
    // things right now" correction in Player2HudFadeSync.ApplyCurrentFadeState, which is a
    // defensive snap (the widget was already primed at alpha 0 by PrepareHidden above; this just
    // also fully deactivates the GameObject) rather than a transition anyone is meant to see.
    internal static void SetVisible(GameObject root, bool visible, bool instant = false)
    {
        if (root == null)
        {
            return;
        }
        CanvasGroup group = EnsureCanvasGroup(root);

        // Kill whatever tween is already running on this CanvasGroup before starting a new one -
        // handles rapid toggling (e.g. two fades back to back) by restarting cleanly from
        // whatever alpha the in-flight tween had reached, instead of two tweens fighting over the
        // same value. Deliberately NOT `complete: true`: killing a fade-OUT tween that's still
        // mid-flight must NOT fire its "deactivate the GameObject" OnComplete after we've just
        // been asked to show it again.
        DOTween.Kill(group);

        if (instant)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
            root.SetActive(visible);
            return;
        }

        if (visible)
        {
            root.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;
            DOTween.To(() => group.alpha, x => group.alpha = x, 1f, Duration).SetTarget(group);
        }
        else
        {
            if (!root.activeSelf)
            {
                // Already hidden - nothing to animate.
                return;
            }
            group.blocksRaycasts = false;
            group.interactable = false;
            DOTween.To(() => group.alpha, x => group.alpha = x, 0f, Duration)
                .SetTarget(group)
                .OnComplete(() =>
                {
                    if (root != null)
                    {
                        root.SetActive(false);
                    }
                });
        }
    }
}
