using Com.LuisPedroFonseca.ProCamera2D;
using DG.Tweening;
using DG.Tweening.Core.Surrogates;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Effects.Player.GhostTrail;
using Gameplay.GameControllers.Effects.Player.Recolor;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Rewired;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tools.Level;
using Tools.Level.Interactables;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Multi-player interactables and execution system:
// Ensures execution triggers, cutscenes, camera centering, sprite visibility, and input locks
// operate independently for all players (P1..P4) without hijacking or freezing other players.

[HarmonyPatch(typeof(Interactable), "get_InteractionTriggered")]
internal static class Interactable_InteractionTriggered_Patch
{
    private static readonly FieldInfo InteractableWhileJumpingField =
        AccessTools.Field(typeof(Interactable), "interactableWhileJumping");
    private static readonly FieldInfo ExecutionPenitentField =
        AccessTools.Field(typeof(Execution), "<Penitent>k__BackingField");

    private static void Postfix(Interactable __instance, ref bool __result)
    {
        if (__result)
        {
            return;
        }
        if (__instance.OverlappedInteractor)
        {
            return;
        }

        bool interactableWhileJumping = (bool)InteractableWhileJumpingField.GetValue(__instance);
        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null || guest.Status == null || guest.Status.Dead) continue;
            if (GlobalBlockerTracker.AnyGlobalBlocker(guest)) continue;

            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot > 0 && (Player2Input.GetInteractDown(slot) || Player2Input.GetUp(slot)))
            {
                if (guest.IsJumping && !interactableWhileJumping) continue;
                if (guest.IsGrabbingCliffLede) continue;

                if (__instance is Execution exec && ExecutionPenitentField != null)
                {
                    ExecutionPenitentField.SetValue(exec, guest);
                }

                __result = true;
                return;
            }
        }
    }
}

// Directs PlayerDirection on Execution to the executing player instead of hardcoded P1
[HarmonyPatch(typeof(Interactable), nameof(Interactable.PlayerDirection), MethodType.Getter)]
internal static class Interactable_PlayerDirection_Patch
{
    private static void Postfix(Interactable __instance, ref EntityOrientation __result)
    {
        if (__instance is Execution exec && exec.Penitent != null)
        {
            __result = (exec.transform.InverseTransformPoint(exec.Penitent.transform.position).x < 0f)
                ? EntityOrientation.Left
                : EntityOrientation.Right;
        }
    }
}

// Prevents base Interactable.ShowPlayer from modifying P1 when a guest executes an enemy
[HarmonyPatch(typeof(Interactable), "ShowPlayer")]
internal static class Interactable_ShowPlayer_Patch
{
    private static bool Prefix(Interactable __instance, bool show)
    {
        if (__instance is Execution exec && exec.Penitent != null)
        {
            if (exec.Penitent.SpriteRenderer != null)
                exec.Penitent.SpriteRenderer.enabled = show;
            if (exec.Penitent.DamageArea != null)
                exec.Penitent.DamageArea.enabled = show;
            return false;
        }
        return true;
    }
}

// Ignores global INTERACTABLE blocker for Execution cutscenes so other players are not blocked
[HarmonyPatch(typeof(Interactable), "OnEventLaunched")]
internal static class Interactable_OnEventLaunched_Patch
{
    private static readonly FieldInfo BeingUsedField = AccessTools.Field(typeof(Interactable), "<BeingUsed>k__BackingField");
    private static readonly MethodInfo InteractionStartMethod = AccessTools.Method(typeof(Interactable), "InteractionStart");
    private static readonly MethodInfo InteractionEndMethod = AccessTools.Method(typeof(Execution), "InteractionEnd");

    private static bool Prefix(Interactable __instance, string id)
    {
        if (__instance is Execution exec)
        {
            if (id == "INTERACTION_START")
            {
                BeingUsedField?.SetValue(exec, true);
                InteractionStartMethod?.Invoke(exec, null);
                return false;
            }
            else if (id == "INTERACTION_END")
            {
                BeingUsedField?.SetValue(exec, false);
                InteractionEndMethod?.Invoke(exec, null);
                return false;
            }
        }
        return true;
    }
}

// Execution.ShowPlayer patch: operates exclusively on exec.Penitent, never hiding or freezing P1 if a guest executes
[HarmonyPatch(typeof(Execution), "ShowPlayer")]
internal static class Execution_ShowPlayer_Patch
{
    private static bool Prefix(Execution __instance, bool show)
    {
        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
        if (executor != null)
        {
            if (executor.SpriteRenderer != null)
                executor.SpriteRenderer.enabled = show;
            if (executor.DamageArea != null)
                executor.DamageArea.enabled = show;
            if (executor.Physics != null)
                executor.Physics.EnablePhysics(show);
            executor.IsOnExecution = !show;
            PlayerLogicBlocker.SetBlocked(executor, !show);
        }
        return false;
    }
}

// Execution.PlayerIsOnSameFloor patch: checks the floor relative to the executing player
[HarmonyPatch(typeof(Execution), "get_PlayerIsOnSameFloor")]
internal static class Execution_PlayerIsOnSameFloor_Patch
{
    private static bool Prefix(Execution __instance, ref bool __result)
    {
        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
        if (executor == null)
        {
            __result = false;
            return false;
        }

        RaycastHit2D hitExec = Physics2D.Raycast(__instance.transform.position, -Vector2.up, 3f, __instance.AllowedFloor);
        RaycastHit2D hitPlayer = Physics2D.Raycast(executor.GetPosition(), -Vector2.up, 1.5f, __instance.AllowedFloor);
        if (hitExec.collider == null || hitPlayer.collider == null)
        {
            __result = false;
            return false;
        }

        __result = Mathf.Abs(hitExec.collider.bounds.max.y - hitPlayer.collider.bounds.max.y) < 0.2f;
        return false;
    }
}

// Execution.CenterCamera patch: centers on the executing player
[HarmonyPatch(typeof(Execution), "CenterCamera")]
internal static class Execution_CenterCamera_Patch
{
    private static readonly FieldInfo CameraPlayerOffsetField = AccessTools.Field(typeof(Execution), "_cameraPlayerOffset");
    private static readonly FieldInfo CurrentCameraPositionField = AccessTools.Field(typeof(Execution), "_currentCameraPosition");
    private static readonly FieldInfo ZoomTimeLapseField = AccessTools.Field(typeof(Execution), "_zoomTimeLapse");

    private static bool Prefix(Execution __instance)
    {
        var cameraPlayerOffset = CameraPlayerOffsetField.GetValue(__instance);
        if (cameraPlayerOffset == null || Core.Logic?.CameraManager == null) return false;

        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
        ProCamera2D proCamera = Core.Logic.CameraManager.ProCamera2D;
        if (proCamera == null || executor == null) return false;

        Vector3 curPos = new Vector3(proCamera.transform.position.x, proCamera.transform.position.y, proCamera.transform.position.z);
        CurrentCameraPositionField.SetValue(__instance, curPos);
        Vector3 endValue = new Vector3(executor.transform.position.x, curPos.y, curPos.z);
        float zoomTime = (float)ZoomTimeLapseField.GetValue(__instance);

        DOTween.To(() => proCamera.transform.position, delegate (Vector3Wrapper x)
        {
            proCamera.transform.position = x;
        }, endValue, zoomTime).SetUpdate(isIndependentUpdate: true).OnStart(delegate
        {
            if (Core.Logic?.CameraManager?.ProCamera2D != null)
            {
                Core.Logic.CameraManager.ProCamera2D.FollowHorizontal = false;
                Core.Logic.CameraManager.ProCamera2D.FollowVertical = false;
            }
        }).SetEase(Ease.InSine);

        return false;
    }
}

// Execution.TriggerAwareness patch: spawns awareness effect at the executing player's elevation
[HarmonyPatch(typeof(Execution), "TriggerAwareness")]
internal static class Execution_TriggerAwareness_Patch
{
    private static bool Prefix(Execution __instance)
    {
        if (__instance.ExecutionAwareness != null && __instance.ExecutedEntity != null)
        {
            Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
            float y = (executor != null) ? executor.transform.position.y : __instance.transform.position.y;
            Vector2 pos = new Vector2(__instance.ExecutedEntity.transform.position.x, y);
            UnityEngine.Object.Instantiate(__instance.ExecutionAwareness, pos, Quaternion.identity);
        }
        return false;
    }
}

// Execution.InteractionEnd patch: awards fervour bonus to the executing player
[HarmonyPatch(typeof(Execution), "InteractionEnd")]
internal static class Execution_InteractionEnd_Patch
{
    private static bool Prefix(Execution __instance)
    {
        __instance.Consumed = true;
        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
        if (executor != null)
        {
            float bonus = 13.3f * executor.Stats.FervourStrength.Final;
            executor.Stats.Fervour.Current += bonus;
            executor.IsOnExecution = false;
            PlayerLogicBlocker.SetBlocked(executor, false);
        }
        GhostTrailGenerator.AreGhostTrailsAllowed = true;
        return false;
    }
}

// Execution.PlayerReposition patch: repositions only the executing player
[HarmonyPatch(typeof(Execution), "PlayerReposition")]
internal static class Execution_PlayerReposition_Patch
{
    private static bool Prefix(Execution __instance)
    {
        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;
        if (executor != null)
        {
            executor.transform.position = __instance.GetRootMotionPosition(__instance.PlayerDirection);
        }
        return false;
    }
}

// Execution.OnUpdate patch: detects which player (P1..P4) in range pressed interact
[HarmonyPatch(typeof(Execution), "OnUpdate")]
internal static class Execution_OnUpdate_Patch
{
    private static readonly FieldInfo AttackCoolDownField = AccessTools.Field(typeof(Execution), "attackCoolDown");
    private static readonly FieldInfo CurrentTimeThresholdField = AccessTools.Field(typeof(Execution), "_currentTimeThreshold");
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(Execution), "<Penitent>k__BackingField");

    private static bool IsPlayerOnSameFloor(Execution exec, Penitent penitent)
    {
        if (exec == null || penitent == null) return false;
        RaycastHit2D hitExec = Physics2D.Raycast(exec.transform.position, -Vector2.up, 3f, exec.AllowedFloor);
        RaycastHit2D hitPlayer = Physics2D.Raycast(penitent.GetPosition(), -Vector2.up, 1.5f, exec.AllowedFloor);
        if (hitExec.collider == null || hitPlayer.collider == null) return false;
        return Mathf.Abs(hitExec.collider.bounds.max.y - hitPlayer.collider.bounds.max.y) < 0.2f;
    }

    private static Penitent FindTriggeringPlayer(Execution exec)
    {
        Penitent bestPlayer = null;
        float bestDist = float.MaxValue;

        for (int slot = 0; slot < CoopPlayerRegistry.MaxPlayers; slot++)
        {
            Penitent p = CoopPlayerRegistry.GetPenitent(slot);
            if (p == null || p.Status == null || p.Status.Dead || p.Status.IsHurt || !p.Status.IsGrounded)
                continue;
            if (p.IsJumping || p.IsGrabbingCliffLede || p.IsOnExecution)
                continue;

            float dist = Vector2.Distance(p.transform.position, exec.transform.position);
            if (dist > 3.0f)
                continue;

            bool pressed = false;
            if (slot == 0)
            {
                pressed = (ReInput.isReady && ReInput.players.GetPlayer(0).GetButtonDown(8)) || Player2Input.GetInteractDown(0);
            }
            else
            {
                pressed = Player2Input.GetInteractDown(slot);
            }

            if (pressed && dist < bestDist)
            {
                if (IsPlayerOnSameFloor(exec, p))
                {
                    bestPlayer = p;
                    bestDist = dist;
                }
            }
        }
        return bestPlayer;
    }

    private static bool Prefix(Execution __instance)
    {
        if (__instance.Consumed) return false;

        float currentTime = (float)CurrentTimeThresholdField.GetValue(__instance) + Time.deltaTime;
        CurrentTimeThresholdField.SetValue(__instance, currentTime);

        float attackCd = (float)AttackCoolDownField.GetValue(__instance) - Time.deltaTime;

        Penitent currentPenitent = __instance.Penitent ?? Core.Logic.Penitent;
        if (currentPenitent != null && currentPenitent.PlatformCharacterInput != null && currentPenitent.PlatformCharacterInput.Attack)
        {
            attackCd = 0.5f;
        }
        AttackCoolDownField.SetValue(__instance, attackCd);

        if (__instance.BeingUsed)
        {
            if (currentPenitent != null)
            {
                currentPenitent.transform.position = __instance.GetRootMotionPosition(__instance.PlayerDirection);
            }
            return false;
        }

        if (attackCd <= 0f)
        {
            Penitent interactor = FindTriggeringPlayer(__instance);
            if (interactor != null)
            {
                PenitentBackingField.SetValue(__instance, interactor);
                interactor.IsOnExecution = true;
                PlayerLogicBlocker.SetBlocked(interactor, true);
                GhostTrailGenerator.AreGhostTrailsAllowed = false;
                __instance.Use();
                return false;
            }
        }

        return false;
    }
}

// Custom Execution.OnUse coroutine ensuring full player isolation, proper repositioning, and skin application
[HarmonyPatch(typeof(Execution), "OnUse")]
internal static class Execution_OnUse_Patch
{
    private static readonly FieldInfo InteractorStartPositionField =
        AccessTools.Field(typeof(Execution), "_interactorStartPosition");
    private static readonly FieldInfo InteractableAnimatorField =
        AccessTools.Field(typeof(Interactable), "interactableAnimator");
    private static readonly FieldInfo InteractorAnimatorField =
        AccessTools.Field(typeof(Interactable), "interactorAnimator");
    private static readonly FieldInfo SoundDelayField =
        AccessTools.Field(typeof(Execution), "soundDelay");

    private static readonly MethodInfo FlipAnimationMethod =
        AccessTools.Method(typeof(Execution), "FlipAnimation");
    private static readonly MethodInfo ShowPlayerMethod =
        AccessTools.Method(typeof(Execution), "ShowPlayer");
    private static readonly MethodInfo AddProgressToAC27Method =
        AccessTools.Method(typeof(Execution), "AddProgressToAC27");
    private static readonly MethodInfo ReleaseOverlappedInteractablesMethod =
        AccessTools.Method(typeof(Execution), "ReleaseOverlappedInteractables");

    private static void ApplyExecutorSkin(Execution exec)
    {
        if (exec == null) return;
        Penitent executor = exec.Penitent ?? Core.Logic.Penitent;
        if (executor == null) return;

        Animator interactorAnim = InteractorAnimatorField?.GetValue(exec) as Animator;
        if (interactorAnim == null) return;

        SpriteRenderer playerSr = executor.SpriteRenderer;
        SpriteRenderer[] allSr = interactorAnim.GetComponentsInChildren<SpriteRenderer>(true);

        int playerIndex = CoopPlayerRegistry.GetPlayerIndex(executor);
        string paletteId = playerIndex >= 0 ? CoopPlayerRegistry.GetSlot(playerIndex)?.SkinPaletteId : null;
        Texture2D paletteTexture = null;

        if (!string.IsNullOrEmpty(paletteId) && Core.ColorPaletteManager != null)
        {
            Sprite paletteSprite = Core.ColorPaletteManager.GetColorPaletteById(paletteId);
            if (paletteSprite != null)
            {
                paletteTexture = paletteSprite.texture;
            }
        }

        foreach (var sr in allSr)
        {
            if (sr == null) continue;
            if (playerSr != null && playerSr.sharedMaterial != null)
            {
                sr.sharedMaterial = playerSr.sharedMaterial;
            }
            if (paletteTexture != null && sr.material != null)
            {
                sr.material.SetTexture("_PaletteTex", paletteTexture);
            }
        }

        ColorPaletteSwapper swapper = interactorAnim.GetComponent<ColorPaletteSwapper>();
        if (swapper != null && !string.IsNullOrEmpty(paletteId))
        {
            ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.ApplySkinToSwapper(swapper, paletteId);
        }
    }

    private static bool Prefix(Execution __instance, ref IEnumerator __result)
    {
        __result = CustomOnUse(__instance);
        return false;
    }

    private static IEnumerator CustomOnUse(Execution __instance)
    {
        WaitForEndOfFrame lapse = new WaitForEndOfFrame();
        Penitent executor = __instance.Penitent ?? Core.Logic.Penitent;

        if (executor != null && executor.PlatformCharacterController != null)
        {
            executor.PlatformCharacterController.PlatformCharacterPhysics.HSpeed = 0f;
            executor.PlatformCharacterController.InstantVelocity = Vector3.zero;
        }

        if (executor != null)
        {
            executor.IsOnExecution = true;
            PlayerLogicBlocker.SetBlocked(executor, true);
            InteractorStartPositionField?.SetValue(__instance, new Vector2(executor.transform.position.x, executor.transform.position.y));
        }

        Core.Input.SetBlocker("EXECUTION", blocking: true);

        FlipAnimationMethod?.Invoke(__instance, new object[] { __instance.PlayerDirection });

        Animator interactableAnim = InteractableAnimatorField?.GetValue(__instance) as Animator;
        if (interactableAnim != null)
        {
            interactableAnim.gameObject.SetActive(false);
        }

        ShowPlayerMethod?.Invoke(__instance, new object[] { false });

        if (__instance.ExecutedEntity != null)
        {
            __instance.ExecutedEntity.KillEntity();
        }

        if (__instance.PlayOnUse && !string.IsNullOrEmpty(__instance.ActivationSound))
        {
            float soundDelay = SoundDelayField != null ? (float)SoundDelayField.GetValue(__instance) : 0f;
            Core.Audio.PlaySfx(__instance.ActivationSound, soundDelay);
        }

        ApplyExecutorSkin(__instance);

        Animator interactorAnim = InteractorAnimatorField?.GetValue(__instance) as Animator;
        if (interactorAnim != null)
        {
            interactorAnim.SetTrigger("USED");
        }

        while (!__instance.Consumed)
        {
            yield return lapse;
        }

        AddProgressToAC27Method?.Invoke(__instance, null);

        if (executor != null)
        {
            executor.transform.position = __instance.GetRootMotionPosition(__instance.PlayerDirection);
        }

        if (__instance.ExecutedEntity != null)
        {
            UnityEngine.Object.Destroy(__instance.ExecutedEntity.gameObject);
        }
        UnityEngine.Object.Destroy(__instance.gameObject);

        ShowPlayerMethod?.Invoke(__instance, new object[] { true });

        ReleaseOverlappedInteractablesMethod?.Invoke(__instance, null);

        Core.Input.SetBlocker("EXECUTION", blocking: false);

        if (executor != null)
        {
            executor.IsOnExecution = false;
            PlayerLogicBlocker.SetBlocked(executor, false);
        }
    }
}


[HarmonyPatch(typeof(Door), "get_EnterTriggered")]
internal static class Door_EnterTriggered_Coop_Patch
{
    private static void Postfix(Door __instance, ref bool __result)
    {
        if (__result) return;
        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null || guest.Status == null || guest.Status.Dead) continue;
            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot > 0)
            {
                if (Player2Input.GetUp(slot) || Player2Input.GetInteractDown(slot))
                {
                    __result = true;
                    return;
                }
            }
        }
    }
}

[HarmonyPatch(typeof(Door), "EnterDoor")]
internal static class Door_EnterDoor_Coop_Patch
{
    private static readonly FieldInfo LadderRequiredField = AccessTools.Field(typeof(Door), "ladderRequired");

    private static bool Prefix(Door __instance)
    {
        bool ladderRequired = (bool)LadderRequiredField.GetValue(__instance);
        bool canClimb = false;
        if (!ladderRequired)
        {
            canClimb = true;
        }
        else
        {
            if (Core.Logic?.Penitent != null && Core.Logic.Penitent.IsClimbingLadder)
            {
                canClimb = true;
            }
            else
            {
                foreach (var guest in CoopLocal.GetAllExtraPlayers())
                {
                    if (guest != null && !guest.Status.Dead && guest.IsClimbingLadder)
                    {
                        canClimb = true;
                        break;
                    }
                }
            }
        }

        if (!canClimb)
        {
            return false;
        }

        // Si P1 esta muerto (PendingRevive), revivirlo temporalmente para que la transicion guarde datos validos
        if (Player1DeathState.IsPendingRevive() && Core.Logic?.Penitent != null)
        {
            PlayerDeathFixShared.TemporarilyReviveP1ForTransition(Core.Logic.Penitent);
        }

        // Congelar fisicas y dano de todos los invitados durante el cambio de sala
        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra != null)
            {
                if (extra.Physics != null)
                {
                    extra.Physics.EnablePhysics(false);
                    extra.Physics.EnableColliders(false);
                }
                if (extra.DamageArea?.DamageAreaCollider != null)
                {
                    extra.DamageArea.DamageAreaCollider.enabled = false;
                }
                if (extra.PlatformCharacterController != null)
                {
                    extra.PlatformCharacterController.InstantVelocity = Vector3.zero;
                    extra.PlatformCharacterController.enabled = false;
                }
            }
        }

        return true;
    }
}

[HarmonyPatch(typeof(Interactable), "OnEntityExit")]
internal static class Interactable_OnEntityExit_Coop_Patch
{
    private static void Postfix(Interactable __instance, Entity entity)
    {
        if (entity != null && entity.CompareTag("Penitent"))
        {
            if (IsAnyLivingPlayerInSensors(__instance))
            {
                __instance.PlayerInRange = true;
            }
        }
    }

    private static bool IsAnyLivingPlayerInSensors(Interactable interactable)
    {
        if (interactable == null) return false;
        var sensorsField = AccessTools.Field(typeof(Interactable), "sensors");
        var sensors = sensorsField?.GetValue(interactable) as CollisionSensor[];
        if (sensors == null || sensors.Length == 0) return false;

        Penitent p1 = Core.Logic?.Penitent;
        if (p1 != null && !p1.Status.Dead && !Player1DeathState.IsPendingRevive())
        {
            if (IsPenitentInSensors(p1, sensors)) return true;
        }

        foreach (var extra in CoopLocal.GetAllExtraPlayers())
        {
            if (extra != null && !extra.Status.Dead)
            {
                int slot = CoopPlayerRegistry.GetPlayerIndex(extra);
                if (slot > 0 && !Player2DeathState.IsPendingRevive(slot))
                {
                    if (IsPenitentInSensors(extra, sensors)) return true;
                }
            }
        }
        return false;
    }

    private static bool IsPenitentInSensors(Penitent p, CollisionSensor[] sensors)
    {
        Collider2D pCol = p.GetComponentInChildren<Collider2D>();
        if (pCol == null) return false;
        for (int i = 0; i < sensors.Length; i++)
        {
            if (sensors[i] == null) continue;
            Collider2D sCol = sensors[i].GetComponent<Collider2D>();
            if (sCol != null && sCol.bounds.Intersects(pCol.bounds))
            {
                return true;
            }
        }
        return false;
    }
}
