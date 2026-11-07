using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Tools.Level;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Synchronizes Prie-Dieu (and Altar) interaction between P1 and P2:
// When either player activates a Prie-Dieu, P1 is placed in his normal kneeling position,
// while P2 is positioned slightly to the right (X + 1.2f), facing left towards P1.
// Both players play the Prie-Dieu kneeling/standing animations simultaneously in full sync.
internal static class PrieDieuCoopSync
{
    private static GameObject p2KneelVisual;
    private static Animator p2KneelAnimator;
    private static SpriteRenderer p2KneelSpriteRenderer;

    internal static void OnPrieDieuStart(Interactable interactable, Animator interactorAnimator)
    {
        if (Player1DeathState.IsPendingRevive())
        {
            Player1DeathState.ClearPendingRevive();
            Penitent p1 = Core.Logic.Penitent;
            if (p1 != null)
            {
                p1.Status.Dead = false;
                p1.DeathEventLaunched = false;
                p1.Stats.Life.SetToCurrentMax();
                if (interactorAnimator != null)
                {
                    p1.transform.position = interactorAnimator.transform.position;
                }
                PlayerDeathFixShared.EnableAbilitiesMethod?.Invoke(p1, new object[] { true });
                PlayerDeathFixShared.EnableTraitsMethod?.Invoke(p1, new object[] { true });
                if (p1.DamageArea != null)
                {
                    p1.DamageArea.IncludeEnemyLayer(true);
                }
                p1.Animator.Play("Idle");
            }
        }

        bool anyPending = false;
        for (int p = 1; p < CoopPlayerRegistry.MaxPlayers; p++)
        {
            if (Player2DeathState.IsPendingRevive(p))
            {
                Player2DeathState.ClearPendingRevive(p);
                anyPending = true;
            }
        }
        if (anyPending && CoopLocal.Player2 == null && Core.Logic?.Penitent != null)
        {
            CoopLocal.SpawnPlayer2(Core.Logic.Penitent, Core.Logic.Penitent.transform.position);
        }

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra == null) continue;
            if (extra.Status.Dead)
            {
                extra.Status.Dead = false;
                extra.Stats.Life.SetToCurrentMax();
            }
            int slot = CoopPlayerRegistry.GetPlayerIndex(extra);
            Player2StatsSync.HealAtPrieDieu(extra, healFervour: true, slotIndex: slot);
        }

        Penitent p2 = CoopLocal.Player2;
        if (p2 == null || interactorAnimator == null)
        {
            return;
        }

        Vector3 targetPos = interactorAnimator.transform.position + new Vector3(1.2f, 0f, 0f);
        p2.transform.position = targetPos;
        p2.SetOrientation(EntityOrientation.Left);
        p2.PlatformCharacterController.PlatformCharacterPhysics.Velocity = Vector3.zero;

        PlayerLogicBlocker.SetBlocked(p2, true);
        p2.SpriteRenderer.enabled = false;
        if (p2.DamageArea != null)
        {
            p2.DamageArea.enabled = false;
        }

        if (p2KneelVisual != null)
        {
            UnityEngine.Object.Destroy(p2KneelVisual);
            p2KneelVisual = null;
        }

        try
        {
            p2KneelVisual = UnityEngine.Object.Instantiate(interactorAnimator.gameObject);
            p2KneelVisual.name = "P2_PrieDieu_KneelVisual";
            p2KneelVisual.transform.position = targetPos;
            p2KneelVisual.transform.localScale = interactorAnimator.transform.localScale;

            foreach (MonoBehaviour mb in p2KneelVisual.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mb != null)
                {
                    UnityEngine.Object.Destroy(mb);
                }
            }
            foreach (Collider2D col in p2KneelVisual.GetComponentsInChildren<Collider2D>())
            {
                if (col != null)
                {
                    UnityEngine.Object.Destroy(col);
                }
            }

            p2KneelAnimator = p2KneelVisual.GetComponent<Animator>();
            p2KneelSpriteRenderer = p2KneelVisual.GetComponentInChildren<SpriteRenderer>();
            if (p2KneelSpriteRenderer != null)
            {
                p2KneelSpriteRenderer.flipX = true; // Looking left towards P1
                p2KneelSpriteRenderer.enabled = true;

                // Aplicar la skin oscura (Verdadera Apostasía) de P2 al sprite del reclinatorio
                p2KneelSpriteRenderer.material = new Material(p2KneelSpriteRenderer.material);
                Texture2D p2Tex = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayer2PaletteTexture();
                if (p2Tex == null && p2.SpriteRenderer != null && p2.SpriteRenderer.material != null)
                {
                    p2Tex = p2.SpriteRenderer.material.GetTexture("_PaletteTex") as Texture2D;
                }
                if (p2Tex != null)
                {
                    p2KneelSpriteRenderer.material.SetTexture("_PaletteTex", p2Tex);
                }
            }

            DashParryDebugLog.Log($"[PrieDieuCoopSync] P2 kneel visual created at {targetPos} facing left");
        }
        catch (System.Exception ex)
        {
            DashParryDebugLog.Log($"[PrieDieuCoopSync] Error instantiating P2 kneel visual: {ex.Message}");
        }
    }

    internal static void OnPlayerReposition(Interactable interactable, Animator interactorAnimator)
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null || interactorAnimator == null)
        {
            return;
        }

        Vector3 targetPos = interactorAnimator.transform.position + new Vector3(1.2f, 0f, 0f);
        p2.transform.position = targetPos;
        p2.SetOrientation(EntityOrientation.Left);

        if (p2KneelVisual != null)
        {
            p2KneelVisual.transform.position = targetPos;

            if (p2KneelSpriteRenderer != null)
            {
                p2KneelSpriteRenderer.flipX = true;
                p2KneelSpriteRenderer.enabled = true;
                Texture2D p2Tex = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayer2PaletteTexture();
                if (p2Tex != null && p2KneelSpriteRenderer.material != null)
                {
                    p2KneelSpriteRenderer.material.SetTexture("_PaletteTex", p2Tex);
                }
            }

            if (p2KneelAnimator != null)
            {
                AnimatorStateInfo state = interactorAnimator.GetCurrentAnimatorStateInfo(0);
                p2KneelAnimator.Play(state.fullPathHash, 0, state.normalizedTime);
                p2KneelAnimator.speed = interactorAnimator.speed;
            }
        }
    }

    internal static void OnPrieDieuEnd(Interactable interactable, Animator interactorAnimator)
    {
        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra == null) continue;
            extra.SpriteRenderer.enabled = true;
            if (extra.DamageArea != null)
            {
                extra.DamageArea.enabled = true;
            }
            PlayerLogicBlocker.SetBlocked(extra, false);
            if (interactorAnimator != null)
            {
                extra.transform.position = interactorAnimator.transform.position + new Vector3(1.2f, 0f, 0f);
            }
            extra.SetOrientation(EntityOrientation.Left);
        }

        if (p2KneelVisual != null)
        {
            UnityEngine.Object.Destroy(p2KneelVisual);
            p2KneelVisual = null;
            p2KneelAnimator = null;
            p2KneelSpriteRenderer = null;
            DashParryDebugLog.Log("[PrieDieuCoopSync] P2 kneel visual destroyed, P2 restored");
        }
    }

    internal static void Reset()
    {
        if (p2KneelVisual != null)
        {
            UnityEngine.Object.Destroy(p2KneelVisual);
            p2KneelVisual = null;
            p2KneelAnimator = null;
            p2KneelSpriteRenderer = null;
        }
        if (CoopLocal.Player2 != null)
        {
            CoopLocal.Player2.SpriteRenderer.enabled = true;
            if (CoopLocal.Player2.DamageArea != null)
            {
                CoopLocal.Player2.DamageArea.enabled = true;
            }
            PlayerLogicBlocker.SetBlocked(CoopLocal.Player2, false);
        }
    }
}

[HarmonyPatch(typeof(PrieDieu), "OnUse")]
internal static class PrieDieu_OnUse_Patch
{
    private static void Prefix(PrieDieu __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPrieDieuStart(__instance, ___interactorAnimator);
    }
}

[HarmonyPatch(typeof(PrieDieu), "PlayerReposition")]
internal static class PrieDieu_PlayerReposition_Patch
{
    private static void Postfix(PrieDieu __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPlayerReposition(__instance, ___interactorAnimator);
    }
}

[HarmonyPatch(typeof(PrieDieu), "InteractionEnd")]
internal static class PrieDieu_InteractionEnd_Patch
{
    private static void Postfix(PrieDieu __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPrieDieuEnd(__instance, ___interactorAnimator);
    }
}

[HarmonyPatch(typeof(Altar), "OnUse")]
internal static class Altar_OnUse_Patch
{
    private static void Prefix(Altar __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPrieDieuStart(__instance, ___interactorAnimator);
    }
}

[HarmonyPatch(typeof(Altar), "PlayerReposition")]
internal static class Altar_PlayerReposition_Patch
{
    private static void Postfix(Altar __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPlayerReposition(__instance, ___interactorAnimator);
    }
}

[HarmonyPatch(typeof(Altar), "InteractionEnd")]
internal static class Altar_InteractionEnd_Patch
{
    private static void Postfix(Altar __instance, Animator ___interactorAnimator)
    {
        PrieDieuCoopSync.OnPrieDieuEnd(__instance, ___interactorAnimator);
    }
}
