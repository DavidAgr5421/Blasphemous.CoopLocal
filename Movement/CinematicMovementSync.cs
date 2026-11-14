using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities.StateMachine;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.States;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Synchronizes cinematic and scripted player movement routines (DrivePlayer, Driven state,
/// elevators, PlayMaker teleports, and dialogue animations) across all players (P1..P4).
/// Ensures that when P1 walks or is driven on his own in cutscenes, P2..P4 replicate the
/// same movement routines in formation and don't break cutscenes or get left behind.
/// </summary>
public static class CinematicMovementSync
{
    private static bool isSyncingMoveToPosition = false;
    private static bool isSyncingStopCast = false;

    // --------------------------------------------------------------------------------------
    // 1. Driven State Patches: ensure Driven operates per-owner instead of hardcoded P1
    // --------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(Driven), "OnStateEnter")]
    internal static class Driven_OnStateEnter_Patch
    {
        private static bool Prefix(Driven __instance)
        {
            Penitent owner = __instance.Machine?.GetComponent<Penitent>() ?? Core.Logic?.Penitent;
            if (owner == null) return false;

            Ability[] abilities = owner.GetComponentsInChildren<Ability>();
            if (abilities != null)
            {
                foreach (Ability ability in abilities)
                {
                    if (ability != null && !(ability is DrivePlayer))
                    {
                        ability.StopCast();
                    }
                }
            }

            owner.Status.Unattacable = true;
            if (owner.PlatformCharacterInput != null)
            {
                owner.PlatformCharacterInput.ResetInputs();
                owner.PlatformCharacterInput.ResetActions();
            }

            PlayerLogicBlocker.SetBlocked(owner, true);
            Core.Input.SetBlocker("PLAYER_LOGIC", true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Driven), "Update")]
    internal static class Driven_Update_Patch
    {
        private static bool Prefix(Driven __instance)
        {
            Penitent owner = __instance.Machine?.GetComponent<Penitent>() ?? Core.Logic?.Penitent;
            if (owner == null) return false;

            if (owner.Status.IsGrounded)
            {
                if (owner.AnimatorInyector != null && owner.AnimatorInyector.enabled)
                {
                    owner.AnimatorInyector.enabled = false;
                }
                PlayerLogicBlocker.SetBlocked(owner, true);
                Core.Input.SetBlocker("PLAYER_LOGIC", true);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Driven), "OnStateExit")]
    internal static class Driven_OnStateExit_Patch
    {
        private static bool Prefix(Driven __instance)
        {
            Penitent owner = __instance.Machine?.GetComponent<Penitent>() ?? Core.Logic?.Penitent;
            if (owner == null) return false;

            PlayerLogicBlocker.SetBlocked(owner, false);
            if (!PlayerLogicBlocker.AnyBlocked())
            {
                Core.Input.SetBlocker("PLAYER_LOGIC", false);
            }

            if (owner.AnimatorInyector != null)
            {
                owner.AnimatorInyector.enabled = true;
            }

            owner.Status.Unattacable = false;
            return false;
        }
    }

    // --------------------------------------------------------------------------------------
    // 2. DrivePlayer Patches: replicate walk routines from P1 to P2..P4
    // --------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(DrivePlayer), "MoveToPosition")]
    internal static class DrivePlayer_MoveToPosition_CoopSync_Patch
    {
        private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(DrivePlayer), "_penitent");

        private static void Postfix(DrivePlayer __instance, Vector2 position, EntityOrientation finalOrientation)
        {
            if (isSyncingMoveToPosition) return;

            Penitent owner = (Penitent)PenitentField?.GetValue(__instance) ?? Core.Logic?.Penitent;
            // Only replicate when P1 (the leader/gameplay driver) is commanded to move
            if (owner != Core.Logic?.Penitent) return;

            isSyncingMoveToPosition = true;
            try
            {
                float offsetSign = (finalOrientation == EntityOrientation.Right) ? -1f : 1f;

                foreach (var guest in CoopLocal.GetAllExtraPlayers())
                {
                    if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                    int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
                    if (slot <= 0) continue;

                    // Stagger behind P1 in the direction of movement (e.g. 0.85m per slot)
                    float slotOffset = slot * 0.85f * offsetSign;

                    // If guest is far from P1 when the scripted walk begins, reposition nearby
                    // so the guest stays in frame, follows the walk routine, and doesn't get left behind
                    float distFromP1 = Vector2.Distance(guest.transform.position, owner.transform.position);
                    float yDiff = Mathf.Abs(guest.transform.position.y - owner.transform.position.y);
                    if (distFromP1 > 4.5f || yDiff > 2.0f)
                    {
                        guest.transform.position = new Vector3(owner.transform.position.x + slotOffset, owner.transform.position.y, owner.transform.position.z);
                        guest.PlatformCharacterController?.ResetPreviousPosition();
                    }

                    Vector2 guestTarget = new Vector2(position.x + slotOffset, position.y);
                    if (guest.DrivePlayer != null)
                    {
                        guest.DrivePlayer.MoveToPosition(guestTarget, finalOrientation);
                    }
                }
            }
            finally
            {
                isSyncingMoveToPosition = false;
            }
        }
    }

    [HarmonyPatch(typeof(DrivePlayer), "OnCastEnd")]
    internal static class DrivePlayer_OnCastEnd_CoopSync_Patch
    {
        private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(DrivePlayer), "_penitent");

        private static void Postfix(DrivePlayer __instance)
        {
            if (isSyncingStopCast) return;

            Penitent owner = (Penitent)PenitentField?.GetValue(__instance) ?? Core.Logic?.Penitent;
            if (owner != Core.Logic?.Penitent) return;

            isSyncingStopCast = true;
            try
            {
                foreach (var guest in CoopLocal.GetAllExtraPlayers())
                {
                    if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                    if (guest.DrivePlayer != null && guest.DrivePlayer.Casting)
                    {
                        guest.DrivePlayer.StopCast();
                    }
                }
            }
            finally
            {
                isSyncingStopCast = false;
            }
        }
    }

    // --------------------------------------------------------------------------------------
    // 3. PlayMaker Cinematic / Movement Actions Sync
    // --------------------------------------------------------------------------------------

    // PlayMaker DrivePlayer: when destination is null (rotation-only command), rotate guests too
    [HarmonyPatch(typeof(Tools.PlayMaker.Action.DrivePlayer), "OnEnter")]
    internal static class PlayMaker_DrivePlayer_OnEnter_Patch
    {
        private static void Postfix(Tools.PlayMaker.Action.DrivePlayer __instance)
        {
            if (__instance.Destination == null || __instance.Destination.Value == null)
            {
                foreach (var guest in CoopLocal.GetAllExtraPlayers())
                {
                    if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                    guest.SetOrientation(__instance.FinalPlayerOrientation);
                }
            }
        }
    }

    // PlayMaker PenitentOnElevator: parent and sync physics for all guests so they ride elevators together
    [HarmonyPatch(typeof(Tools.PlayMaker.Action.PenitentOnElevator), "OnEnter")]
    internal static class PenitentOnElevator_Coop_Patch
    {
        private static void Postfix(Tools.PlayMaker.Action.PenitentOnElevator __instance)
        {
            bool value = __instance.enablePenitentPhysics.Value;
            Transform parent = (!value && __instance.parentToElement != null && __instance.parentToElement.Value != null)
                ? __instance.parentToElement.Value.transform
                : null;

            foreach (var guest in CoopLocal.GetAllExtraPlayers())
            {
                if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                if (value)
                {
                    guest.transform.localPosition = new Vector3(guest.transform.localPosition.x, 0f, guest.transform.localPosition.z);
                    guest.PlatformCharacterController?.ResetPreviousPosition();
                }
                guest.transform.SetParent(parent);
                guest.Physics?.EnablePhysics(value);
            }
        }
    }

    // PlayMaker PlayerTeleport: teleport all guests along with P1 to keep the party together
    [HarmonyPatch(typeof(Tools.PlayMaker2.Action.PlayerTeleport), "OnEnter")]
    internal static class PlayerTeleport_Coop_Patch
    {
        private static void Postfix(Tools.PlayMaker2.Action.PlayerTeleport __instance)
        {
            GameObject targetGo = __instance.Fsm?.GetOwnerDefaultTarget(__instance.teleportTarget);
            if (targetGo == null) return;

            Vector3 targetPos = targetGo.transform.position;
            foreach (var guest in CoopLocal.GetAllExtraPlayers())
            {
                if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
                float offset = (slot > 0) ? (slot * 0.75f) : 0f;
                guest.transform.position = new Vector3(targetPos.x - offset, targetPos.y, targetPos.z);
                guest.PlatformCharacterController?.ResetPreviousPosition();
            }
        }
    }

    // --------------------------------------------------------------------------------------
    // 4. Cinematic / Dialog Animation & Invulnerability Sync
    // --------------------------------------------------------------------------------------

    internal static void OnGlobalBlockerChanged(string name, bool blocking)
    {
        if (name == "DIALOG")
        {
            foreach (var guest in CoopLocal.GetAllExtraPlayers())
            {
                if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                guest.Animator?.SetBool("IS_DIALOGUE_MODE", blocking);
            }
        }
        else if (name == "CINEMATIC")
        {
            foreach (var guest in CoopLocal.GetAllExtraPlayers())
            {
                if (guest == null || guest.Status == null || guest.Status.Dead) continue;
                guest.Status.Unattacable = blocking;
            }
        }
    }
}
