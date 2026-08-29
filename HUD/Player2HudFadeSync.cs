using Gameplay.UI.Widgets;

namespace Blasphemous.CoopLocal;

// Round 56 - bug report: during a room transition the game fades the whole screen to black; P1's
// real HUD (health/fervour/currency) correctly disappears along with it, P2's cloned HUD does not.
//
// Root cause, confirmed via decompile (Gameplay.UI.Others.UIGameLogic.PlayerHealth,
// Gameplay.UI.Widgets.GameplayWidget, Framework.Managers.UIManager, Gameplay.UI.UIController):
// P1's HUD has *no* fade-awareness of its own at all - PlayerHealth/PlayerFervour never subscribe
// to any fade/level event, and GameplayWidget's own CanvasGroup.alpha toggle (UIController.cs's
// Update(), gated on Core.UI.MustShowGamePlayUI()/IsMenuScene()/IsAttrackScene()) is never touched
// during a normal room transition (confirmed: nothing in the decompiled project sets
// Core.UI.ShowGamePlayUI = false outside of the PontiffHusk boss fight). P1's HUD only *looks*
// like it fades because it lives on the same UI canvas as UIController's own load/fade overlay
// (UIController.ShowLoad -> loadWidget.SetActive(true), a full-screen opaque Image, plus
// FadeWidget's own tween) - it's physically covered, not actually hidden.
//
// P2's HUD clones (Player2HealthBar/Player2FervourBar/Player2PurgePoints) are plain runtime
// Instantiate() copies parented at that same canvas's root (see each class's own EnsureCreated) -
// but every one of them is (re)created fresh on every CoopLocal.OnPlayerSpawn, which fires *during*
// the black-screen part of a room load, and Unity defaults a freshly instantiated GameObject to
// active - so the clone pops up fully visible with no equivalent "something is covering me"
// mechanism, regardless of whatever sibling/z-order quirk does or doesn't put it above the vanilla
// overlay.
//
// Fix: hook FadeWidget's own public events (the real vanilla signal for "a fade to/from black is
// in progress", fired for every fade regardless of cause - room transitions, death respawns,
// cutscenes - not just level-load-specific ones) instead of re-deriving fade timing by hand.
// OnFadeShowStart fires the instant a fade-to-black begins; OnFadeHidedEnd fires once a fade back
// to visible has fully completed - mirroring those onto P2's own HUD roots reproduces the same
// "hidden while the screen is black" behavior P1 gets for free.
internal static class Player2HudFadeSync
{
    private static bool subscribed;

    internal static void Initialize()
    {
        if (subscribed)
        {
            return;
        }
        FadeWidget.OnFadeShowStart += HideAll;
        FadeWidget.OnFadeHidedEnd += ShowAll;
        subscribed = true;
    }

    internal static void Dispose()
    {
        if (!subscribed)
        {
            return;
        }
        FadeWidget.OnFadeShowStart -= HideAll;
        FadeWidget.OnFadeHidedEnd -= ShowAll;
        subscribed = false;
    }

    // Round 57: these now fade (via HudFade, matching the vanilla screen fade's own DOTween
    // timing) instead of a binary SetActive pop - see each widget's own SetVisible comment.
    private static void HideAll()
    {
        DashParryDebugLog.Log("Player2HudFadeSync: FadeWidget.OnFadeShowStart -> hiding P2 HUD");
        Player2HealthBar.SetVisible(false);
        Player2FervourBar.SetVisible(false);
        Player2PurgePoints.SetVisible(false);
    }

    private static void ShowAll()
    {
        DashParryDebugLog.Log("Player2HudFadeSync: FadeWidget.OnFadeHidedEnd -> showing P2 HUD");
        Player2HealthBar.SetVisible(true);
        Player2FervourBar.SetVisible(true);
        Player2PurgePoints.SetVisible(true);
    }

    // Round 57: instant (non-animated) hide, only for the defensive correction below - a brand
    // new widget was already primed at alpha 0 by its own EnsureCreated (HudFade.PrepareHidden),
    // so this just also fully deactivates the GameObject rather than playing a pointless 0->0
    // fade nobody would see anyway.
    private static void HideAllInstant()
    {
        Player2HealthBar.SetVisible(false, instant: true);
        Player2FervourBar.SetVisible(false, instant: true);
        Player2PurgePoints.SetVisible(false, instant: true);
    }

    // CoopLocal.OnPlayerSpawn (re)creates all three HUD clones fresh every time, each already
    // primed hidden (alpha 0, still active - see HudFade.PrepareHidden, called from each widget's
    // own EnsureCreated) rather than Instantiate's normal fully-visible pop. Called right after
    // those EnsureCreated calls to decide what happens next: if a screen fade is already covering
    // things right now (this whole method can run *during* a room transition's fade to black,
    // since OnPlayerSpawn fires mid-load), snap the correction home and leave the widgets hidden
    // until the next FadeWidget.OnFadeHidedEnd fades them in for real; otherwise there's no fade
    // in progress to wait for, so reveal them immediately with the same natural fade-in a real
    // OnFadeHidedEnd would trigger - covers both the "first ever P2 spawn" and "P2 respawns
    // outside of any room transition" cases from the user's own request. FadeWidget.IsActive is
    // the widget's own "am I currently covering the screen" check (black.color.a > 0f).
    internal static void ApplyCurrentFadeState()
    {
        if (FadeWidget.instance != null && FadeWidget.instance.IsActive)
        {
            HideAllInstant();
            return;
        }
        ShowAll();
    }
}
