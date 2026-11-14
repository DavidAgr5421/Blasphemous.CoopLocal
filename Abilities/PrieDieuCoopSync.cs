using System;
using System.Collections.Generic;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.UI;
using HarmonyLib;
using Tools.Level;
using Tools.Level.Interactables;
using UnityEngine;
using Blasphemous.CoopLocal.MultiPlayer;

namespace Blasphemous.CoopLocal;

// Synchronizes Prie-Dieu (and Altar) interaction between all players (P1..P4):
// When any player activates a Prie-Dieu, P1 is placed in his normal kneeling position,
// while P2, P3, and P4 are teleported to distinct kneeling spots around the shrine.
// All players play the Prie-Dieu kneeling/standing animations simultaneously in full sync.
// Any player (P2..P4) or pressing Escape will trigger standing up from the Prie-Dieu.
internal static class PrieDieuCoopSync
{
    internal class GuestKneelVisual
    {
        public int SlotIndex;
        public Penitent Penitent;
        public GameObject VisualObject;
        public Animator Animator;
        public SpriteRenderer SpriteRenderer;
        public Vector3 TargetPos;
        public EntityOrientation Orientation;
        public bool FlipX;
    }

    private static readonly Dictionary<int, GuestKneelVisual> guestVisuals = new Dictionary<int, GuestKneelVisual>();

    public static bool IsKneeling { get; private set; }

    private struct PlacementInfo
    {
        public Vector3 TargetPos;
        public EntityOrientation Orientation;
        public bool FlipX;
    }

    private static PlacementInfo GetPlacementForSlot(int slot, Vector3 basePos)
    {
        switch (slot)
        {
            case 1: // P2: derecha inmediata, mirando hacia la izquierda
                return new PlacementInfo
                {
                    TargetPos = basePos + new Vector3(1.2f, 0f, 0f),
                    Orientation = EntityOrientation.Left,
                    FlipX = true
                };
            case 2: // P3: izquierda inmediata, mirando hacia la derecha
                return new PlacementInfo
                {
                    TargetPos = basePos + new Vector3(-1.2f, 0f, 0f),
                    Orientation = EntityOrientation.Right,
                    FlipX = false
                };
            case 3: // P4: derecha secundaria, mirando hacia la izquierda
                return new PlacementInfo
                {
                    TargetPos = basePos + new Vector3(2.4f, 0f, 0f),
                    Orientation = EntityOrientation.Left,
                    FlipX = true
                };
            default:
                return new PlacementInfo
                {
                    TargetPos = basePos + new Vector3(1.2f + (slot * 1.1f), 0f, 0f),
                    Orientation = EntityOrientation.Left,
                    FlipX = true
                };
        }
    }

    private static void ClearGuestVisuals()
    {
        foreach (var kvp in guestVisuals)
        {
            if (kvp.Value?.VisualObject != null)
            {
                UnityEngine.Object.Destroy(kvp.Value.VisualObject);
            }
        }
        guestVisuals.Clear();
    }

    internal static void OnPrieDieuStart(Interactable interactable, Animator interactorAnimator)
    {
        if (IsKneeling) return;
        IsKneeling = true;

        if (Player1DeathState.IsPendingRevive())
        {
            Penitent p1 = Core.Logic.Penitent;
            if (p1 != null)
            {
                if (interactorAnimator != null)
                {
                    p1.transform.position = interactorAnimator.transform.position;
                }
                PlayerDeathFixShared.RestoreP1FromDownedHiddenState(p1);
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
        if (anyPending && Core.Logic?.Penitent != null)
        {
            CoopLocal.SpawnAllGuestPlayers(Core.Logic.Penitent);
        }

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra == null) continue;
            if (extra.Status != null && extra.Status.Dead)
            {
                extra.Status.Dead = false;
                extra.Stats?.Life?.SetToCurrentMax();
            }
            int slot = CoopPlayerRegistry.GetPlayerIndex(extra);
            Player2StatsSync.HealAtPrieDieu(extra, healFervour: true, slotIndex: slot);
        }

        ClearGuestVisuals();

        if (interactorAnimator == null) return;
        Vector3 basePos = interactorAnimator.transform.position;

        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null) continue;
            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot <= 0) continue;

            PlacementInfo placement = GetPlacementForSlot(slot, basePos);

            guest.transform.position = placement.TargetPos;
            guest.SetOrientation(placement.Orientation);
            if (guest.PlatformCharacterController != null && guest.PlatformCharacterController.PlatformCharacterPhysics != null)
            {
                guest.PlatformCharacterController.PlatformCharacterPhysics.Velocity = Vector3.zero;
            }

            PlayerLogicBlocker.SetBlocked(guest, true);
            if (guest.SpriteRenderer != null)
            {
                guest.SpriteRenderer.enabled = false;
            }
            if (guest.DamageArea != null)
            {
                guest.DamageArea.enabled = false;
            }

            try
            {
                GameObject visual = UnityEngine.Object.Instantiate(interactorAnimator.gameObject);
                visual.name = $"P{slot + 1}_PrieDieu_KneelVisual";
                visual.transform.position = placement.TargetPos;
                visual.transform.localScale = interactorAnimator.transform.localScale;

                foreach (MonoBehaviour mb in visual.GetComponentsInChildren<MonoBehaviour>())
                {
                    if (mb != null) UnityEngine.Object.Destroy(mb);
                }
                foreach (Collider2D col in visual.GetComponentsInChildren<Collider2D>())
                {
                    if (col != null) UnityEngine.Object.Destroy(col);
                }

                Animator anim = visual.GetComponent<Animator>();
                SpriteRenderer sr = visual.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    sr.flipX = placement.FlipX;
                    sr.enabled = true;

                    sr.material = new Material(sr.material);
                    Texture2D pTex = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayerPaletteTexture(slot);
                    if (pTex == null && guest.SpriteRenderer != null && guest.SpriteRenderer.material != null)
                    {
                        pTex = guest.SpriteRenderer.material.GetTexture("_PaletteTex") as Texture2D;
                    }
                    if (pTex != null)
                    {
                        sr.material.SetTexture("_PaletteTex", pTex);
                    }
                }

                guestVisuals[slot] = new GuestKneelVisual
                {
                    SlotIndex = slot,
                    Penitent = guest,
                    VisualObject = visual,
                    Animator = anim,
                    SpriteRenderer = sr,
                    TargetPos = placement.TargetPos,
                    Orientation = placement.Orientation,
                    FlipX = placement.FlipX
                };

                DashParryDebugLog.Log($"[PrieDieuCoopSync] P{slot + 1} kneel visual created at {placement.TargetPos} facing {placement.Orientation}");
            }
            catch (Exception ex)
            {
                DashParryDebugLog.Log($"[PrieDieuCoopSync] Error instantiating P{slot + 1} kneel visual: {ex.Message}");
            }
        }
    }

    internal static void OnPlayerReposition(Interactable interactable, Animator interactorAnimator)
    {
        if (interactorAnimator == null) return;
        Vector3 basePos = interactorAnimator.transform.position;

        foreach (var kvp in guestVisuals)
        {
            GuestKneelVisual visual = kvp.Value;
            if (visual == null) continue;

            PlacementInfo placement = GetPlacementForSlot(visual.SlotIndex, basePos);
            visual.TargetPos = placement.TargetPos;

            if (visual.Penitent != null)
            {
                visual.Penitent.transform.position = visual.TargetPos;
                visual.Penitent.SetOrientation(visual.Orientation);
            }

            if (visual.VisualObject != null)
            {
                visual.VisualObject.transform.position = visual.TargetPos;

                if (visual.SpriteRenderer != null)
                {
                    visual.SpriteRenderer.flipX = visual.FlipX;
                    visual.SpriteRenderer.enabled = true;
                    Texture2D pTex = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayerPaletteTexture(visual.SlotIndex);
                    if (pTex != null && visual.SpriteRenderer.material != null)
                    {
                        visual.SpriteRenderer.material.SetTexture("_PaletteTex", pTex);
                    }
                }

                if (visual.Animator != null)
                {
                    AnimatorStateInfo state = interactorAnimator.GetCurrentAnimatorStateInfo(0);
                    visual.Animator.Play(state.fullPathHash, 0, state.normalizedTime);
                    visual.Animator.speed = interactorAnimator.speed;
                }
            }
        }
    }

    internal static void OnPrieDieuEnd(Interactable interactable, Animator interactorAnimator)
    {
        IsKneeling = false;

        Vector3 basePos = interactorAnimator != null ? interactorAnimator.transform.position : Vector3.zero;

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra == null) continue;
            if (extra.SpriteRenderer != null) extra.SpriteRenderer.enabled = true;
            if (extra.DamageArea != null) extra.DamageArea.enabled = true;
            PlayerLogicBlocker.SetBlocked(extra, false);

            int slot = CoopPlayerRegistry.GetPlayerIndex(extra);
            PlacementInfo placement = GetPlacementForSlot(slot, basePos);
            if (interactorAnimator != null)
            {
                extra.transform.position = placement.TargetPos;
            }
            extra.SetOrientation(placement.Orientation);
        }

        ClearGuestVisuals();
        DashParryDebugLog.Log("[PrieDieuCoopSync] All guest kneel visuals destroyed, players restored");
    }

    internal static void Reset()
    {
        IsKneeling = false;
        ClearGuestVisuals();

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra == null) continue;
            if (extra.SpriteRenderer != null) extra.SpriteRenderer.enabled = true;
            if (extra.DamageArea != null) extra.DamageArea.enabled = true;
            PlayerLogicBlocker.SetBlocked(extra, false);
        }
    }
}

/// <summary>
/// Permite que cualquier jugador (P2..P4) o la tecla Escape hagan que los personajes se levanten del PrieDieu.
/// </summary>
[HarmonyPatch(typeof(UIController), nameof(UIController.IsStopKneelPressed))]
internal static class UIController_IsStopKneelPressed_Coop_Patch
{
    private static void Postfix(ref bool __result)
    {
        if (__result) return;

        // Si se presiona Escape en teclado físico
        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            __result = true;
            return;
        }

        // Si los jugadores 2 a 4 presionan su botón de Escape / Pausa o Interact para levantarse
        int maxPlayers = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);
        for (int slot = 1; slot < maxPlayers; slot++)
        {
            if (Player2Input.GetPauseDown(slot) || Player2Input.GetInteractDown(slot))
            {
                __result = true;
                return;
            }
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
