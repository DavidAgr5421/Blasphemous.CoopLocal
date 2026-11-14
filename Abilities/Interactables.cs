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
// Ensures execution triggers, cutscenes, camera centering, sprite visibility, input locks,
// and item pickups (CollectibleItem, Chest, Altar, etc.) operate independently for all players (P1..P4).

[HarmonyPatch(typeof(Interactable), "get_PlayerInRange")]
internal static class Interactable_PlayerInRange_Coop_Patch
{
    private static void Postfix(Interactable __instance, ref bool __result)
    {
        if (__instance == null) return;

        if (!__result && Interactable_InteractionTriggered_Patch.IsAnyPlayerNearInteractable(__instance))
        {
            __result = true;
        }

        if (!__result && __instance is Door door && door.autoEnter)
        {
            DoorTransitionGuard.MarkSensorExited(door);
        }
    }
}

[HarmonyPatch(typeof(Interactable), "get_InteractionTriggered")]
internal static class Interactable_InteractionTriggered_Patch
{
    private static readonly FieldInfo InteractableWhileJumpingField =
        AccessTools.Field(typeof(Interactable), "interactableWhileJumping");
    private static readonly FieldInfo ExecutionPenitentField =
        AccessTools.Field(typeof(Execution), "<Penitent>k__BackingField");
    private static readonly FieldInfo FakeExecutionPenitentField =
        AccessTools.Field(typeof(FakeExecution), "<Penitent>k__BackingField");

    internal static void SetExecutionPenitent(Interactable interactable, Penitent penitent)
    {
        if (interactable == null || penitent == null) return;
        if (interactable is Execution)
        {
            ExecutionPenitentField?.SetValue(interactable, penitent);
        }
        else if (interactable is FakeExecution)
        {
            FakeExecutionPenitentField?.SetValue(interactable, penitent);
        }
    }

    public static Penitent LastInteractingPlayer { get; internal set; }

    internal static bool IsAnyPlayerNearInteractable(Interactable interactable)
    {
        if (interactable == null) return false;
        Vector3 itemPos = interactable.transform.position;

        Penitent p1 = Core.Logic?.Penitent;
        if (p1 != null && !p1.Status.Dead && !Player1DeathState.IsPendingRevive())
        {
            if (Vector2.Distance(p1.transform.position, itemPos) <= 2.8f)
                return true;
        }

        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null || guest.Status == null || guest.Status.Dead) continue;
            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot > 0 && !Player2DeathState.IsPendingRevive(slot))
            {
                if (Vector2.Distance(guest.transform.position, itemPos) <= 2.8f)
                    return true;
            }
        }
        return false;
    }

    private static void Postfix(Interactable __instance, ref bool __result)
    {
        if (__instance == null) return;

        // Auto-clear stale OverlappedInteractor if no enemy is currently being executed
        if (__instance.OverlappedInteractor)
        {
            if (Execution_OnUse_Patch.ExecutingEnemies.Count == 0)
            {
                __instance.OverlappedInteractor = false;
            }
            else
            {
                return;
            }
        }

        bool interactableWhileJumping = (bool)(InteractableWhileJumpingField?.GetValue(__instance) ?? false);
        Vector3 itemPos = __instance.transform.position;

        // 1. Check Player 1 (if within range)
        Penitent p1 = Core.Logic?.Penitent;
        if (p1 != null && !p1.Status.Dead && !Player1DeathState.IsPendingRevive())
        {
            if (Vector2.Distance(p1.transform.position, itemPos) <= 2.8f)
            {
                bool p1Pressed = false;
                if (ReInput.isReady)
                {
                    var rw = ReInput.players.GetPlayer(0);
                    if (rw != null && rw.GetButtonDown(8)) p1Pressed = true;
                }
                if (!p1Pressed)
                {
                    if (Player2Input.GetInteractDown(0) || Player2Input.GetUp(0)) p1Pressed = true;
                    if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.U)) p1Pressed = true;
                }

                if (p1Pressed)
                {
                    if (!p1.IsJumping || interactableWhileJumping)
                    {
                        if (!p1.IsGrabbingCliffLede && !GlobalBlockerTracker.AnyGlobalBlocker(p1))
                        {
                            LastInteractingPlayer = p1;
                            SetExecutionPenitent(__instance, p1);
                            __result = true;
                            return;
                        }
                    }
                }
            }
        }

        // 2. Check Extra Players (P2..P4) (if within range)
        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null || guest.Status == null || guest.Status.Dead) continue;
            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot <= 0 || Player2DeathState.IsPendingRevive(slot)) continue;
            if (GlobalBlockerTracker.AnyGlobalBlocker(guest)) continue;

            if (Vector2.Distance(guest.transform.position, itemPos) <= 2.8f)
            {
                if (Player2Input.GetInteractDown(slot) || Player2Input.GetUp(slot))
                {
                    if (guest.IsJumping && !interactableWhileJumping) continue;
                    if (guest.IsGrabbingCliffLede) continue;

                    LastInteractingPlayer = guest;
                    SetExecutionPenitent(__instance, guest);

                    __result = true;
                    return;
                }
            }
        }
    }
}

// Runtime-safe access to non-public Interactable members (compile-time publicized libs do not
// exist at runtime, so direct access throws Method/FieldAccessException every frame).
internal static class InteractableAccess
{
    private static readonly FieldInfo RepositionField =
        AccessTools.Field(typeof(Interactable), "RepositionBeforeInteract");
    private static readonly MethodInfo RepositionMethod =
        AccessTools.Method(typeof(Interactable), "PlayerReposition");

    internal static bool GetRepositionBeforeInteract(Interactable i)
    {
        try { return RepositionField != null && (bool)RepositionField.GetValue(i); }
        catch { return false; }
    }

    private static readonly FieldInfo WaypointField =
        AccessTools.Field(typeof(Interactable), "Waypoint");
    private static readonly MethodInfo OnUseMethod =
        AccessTools.Method(typeof(Interactable), "OnUse");

    internal static Transform GetWaypoint(Interactable i)
    {
        try { return WaypointField?.GetValue(i) as Transform; }
        catch { return null; }
    }

    internal static IEnumerator CallOnUse(Interactable i)
    {
        // Exceptions propagate to the caller's existing try/catch.
        return OnUseMethod?.Invoke(i, null) as IEnumerator;
    }

    internal static void CallPlayerReposition(Interactable i)
    {
        try { RepositionMethod?.Invoke(i, null); }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("Interactables.CallPlayerReposition", $"[InteractableCoop] PlayerReposition error: {ex.Message}", isWarning: true);
        }
    }
}
// Intercepts Interactable.Use() for non-executions (CollectibleItem, Chest, Altar, etc.)
// to guarantee that interactions run cleanly even if Core.Input.InputBlocked is true
// (e.g. when P1 is dead or another player is performing a dash/action).
[HarmonyPatch(typeof(Interactable), nameof(Interactable.Use))]
internal static class Interactable_Use_Coop_Patch
{
    private static readonly FieldInfo OnStartUsingField =
        AccessTools.Field(typeof(Interactable), "OnStartUsing");
    private static readonly FieldInfo SInteractionStartedField =
        AccessTools.Field(typeof(Interactable), "SInteractionStarted");
    private static readonly FieldInfo OnStopUsingField =
        AccessTools.Field(typeof(Interactable), "OnStopUsing");
    private static readonly FieldInfo SInteractionEndedField =
        AccessTools.Field(typeof(Interactable), "SInteractionEnded");
    private static readonly MethodInfo OnStopRepositionMethod =
        AccessTools.Method(typeof(Interactable), "OnStopReposition");

    private static void SafeRaiseOnStartUsing(Interactable interactable)
    {
        try
        {
            var evt = OnStartUsingField?.GetValue(interactable) as Core.SimpleEvent;
            evt?.Invoke();
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("Interactables.SafeRaiseOnStartUsing", $"[InteractableCoop] SafeRaiseOnStartUsing error: {ex.Message}", isWarning: true);
        }
    }

    private static void SafeRaiseSInteractionStarted(Interactable interactable)
    {
        try
        {
            var evt = SInteractionStartedField?.GetValue(null) as Core.InteractableEvent;
            evt?.Invoke(interactable);
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("Interactables.SafeRaiseSInteractionStarted", $"[InteractableCoop] SafeRaiseSInteractionStarted error: {ex.Message}", isWarning: true);
        }
    }

    private static void SafeRaiseOnStopUsing(Interactable interactable)
    {
        try
        {
            var evt = OnStopUsingField?.GetValue(interactable) as Core.SimpleEvent;
            evt?.Invoke();
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("Interactables.SafeRaiseOnStopUsing", $"[InteractableCoop] SafeRaiseOnStopUsing error: {ex.Message}", isWarning: true);
        }
    }

    private static void SafeRaiseSInteractionEnded(Interactable interactable)
    {
        try
        {
            var evt = SInteractionEndedField?.GetValue(null) as Core.InteractableEvent;
            evt?.Invoke(interactable);
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("Interactables.SafeRaiseSInteractionEnded", $"[InteractableCoop] SafeRaiseSInteractionEnded error: {ex.Message}", isWarning: true);
        }
    }

    private static bool Prefix(Interactable __instance)
    {
        if (__instance == null) return false;
        if (__instance is Execution || __instance is FakeExecution) return true; // Executions handle their own Coroutines
        if (__instance is PrieDieu || __instance is Altar) return true; // PrieDieu/Altar handle their own Use() & BeingUsed

        __instance.StartCoroutine(CoopUseRoutine(__instance));
        return false;
    }

    public static IEnumerator CoopUseRoutine(Interactable interactable)
    {
        if (interactable == null) yield break;
        if (interactable.Locked) yield break;

        Penitent user = Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
        if (user != null && GlobalBlockerTracker.AnyGlobalBlocker(user))
        {
            yield break;
        }

        if (InteractableAccess.GetRepositionBeforeInteract(interactable) && user != null)
        {
            if (user == Core.Logic?.Penitent && user.DrivePlayer != null && OnStopRepositionMethod != null)
            {
                try
                {
                    Core.SimpleEvent del = (Core.SimpleEvent)Delegate.CreateDelegate(typeof(Core.SimpleEvent), interactable, OnStopRepositionMethod);
                    user.DrivePlayer.OnStopMotion += del;
                }
                catch (Exception ex)
                {
                    DashParryDebugLog.LogOnce("Interactables.OnStopMotion", $"[InteractableCoop] OnStopMotion delegate error: {ex.Message}", isWarning: true);
                }
            }
            else
            {
                Transform wp = InteractableAccess.GetWaypoint(interactable) ?? interactable.transform;
                user.transform.position = new Vector3(wp.position.x, user.transform.position.y, user.transform.position.z);
                user.PlatformCharacterController?.ResetPreviousPosition();
            }
        }

        SafeRaiseOnStartUsing(interactable);

        if (!InteractableAccess.GetRepositionBeforeInteract(interactable))
        {
            SafeRaiseSInteractionStarted(interactable);
        }

        interactable.gameObject.SendMessage("OnUsePre", SendMessageOptions.DontRequireReceiver);

        IEnumerator onUseEnum = null;
        try
        {
            onUseEnum = InteractableAccess.CallOnUse(interactable);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[InteractableCoop] OnUse exception: {ex.Message}");
        }

        if (onUseEnum != null)
        {
            float startTime = Time.time;
            while (!interactable.Consumed && (Time.time - startTime < 3.0f))
            {
                yield return null;
            }
            if (!interactable.Consumed)
            {
                interactable.Consumed = true;
                interactable.SendMessage("InteractionEnd", SendMessageOptions.DontRequireReceiver);
            }
        }
        else
        {
            interactable.Consumed = true;
            interactable.SendMessage("InteractionEnd", SendMessageOptions.DontRequireReceiver);
        }

        // Sends OnUsePost, which triggers item addition to inventory & popup UI
        interactable.gameObject.SendMessage("OnUsePost", SendMessageOptions.DontRequireReceiver);

        SafeRaiseOnStopUsing(interactable);
        SafeRaiseSInteractionEnded(interactable);

        if (user != null)
        {
            user.IsPickingCollectibleItem = false;
            if (user.SpriteRenderer != null) user.SpriteRenderer.enabled = true;
            if (user.DamageArea != null) user.DamageArea.enabled = true;
        }
    }
}

// CollectibleItem.OnUpdate patch: ensures the interacting player is marked as picking
[HarmonyPatch(typeof(CollectibleItem), "OnUpdate")]
internal static class CollectibleItem_OnUpdate_Patch
{
    private static bool Prefix(CollectibleItem __instance)
    {
        if (!__instance.BeingUsed && __instance.PlayerInRange && !__instance.Consumed && __instance.InteractionTriggered)
        {
            Penitent user = Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
            if (user != null)
            {
                user.IsPickingCollectibleItem = true;
            }
            __instance.Use();
        }
        if (__instance.BeingUsed)
        {
            InteractableAccess.CallPlayerReposition(__instance);
        }
        return false;
    }
}

// Repositions the interacting player instead of hardcoded P1
[HarmonyPatch(typeof(Interactable), "PlayerReposition")]
internal static class Interactable_PlayerReposition_Patch
{
    private static bool Prefix(Interactable __instance)
    {
        Penitent target = (__instance is Execution exec && exec.Penitent != null)
            ? exec.Penitent
            : (Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent);

        var anim = AccessTools.Field(typeof(Interactable), "interactorAnimator")?.GetValue(__instance) as Animator;
        if (target != null && anim != null)
        {
            target.transform.position = anim.transform.position;
            return false;
        }
        return true;
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

// Prevents base Interactable.ShowPlayer from modifying P1 when another player interacts or executes
[HarmonyPatch(typeof(Interactable), "ShowPlayer")]
internal static class Interactable_ShowPlayer_Patch
{
    private static bool Prefix(Interactable __instance, bool show)
    {
        Penitent target = (__instance is Execution exec && exec.Penitent != null)
            ? exec.Penitent
            : (Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent);

        if (target != null)
        {
            if (target.SpriteRenderer != null)
                target.SpriteRenderer.enabled = show;
            if (target.DamageArea != null)
                target.DamageArea.enabled = show;
            return false;
        }
        return true;
    }
}

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
        Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
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
        Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
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

        Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
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
            Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
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
        Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
        if (executor != null)
        {
            float bonus = 13.3f * executor.Stats.FervourStrength.Final;
            executor.Stats.Fervour.Current += bonus;
            executor.IsOnExecution = false;
            PlayerLogicBlocker.SetBlocked(executor, false);
        }
        if (__instance.ExecutedEntity != null)
        {
            Execution_OnUse_Patch.ExecutingEnemies.Remove(__instance.ExecutedEntity);
        }
        GhostTrailGenerator.AreGhostTrailsAllowed = true;
        return false;
    }
}

// Execution.OnDispose patch: cleans up tracked executed enemy if execution is destroyed early
[HarmonyPatch(typeof(Execution), "OnDispose")]
internal static class Execution_OnDispose_Patch
{
    private static void Prefix(Execution __instance)
    {
        if (__instance != null && __instance.ExecutedEntity != null)
        {
            Execution_OnUse_Patch.ExecutingEnemies.Remove(__instance.ExecutedEntity);
        }
    }
}

// Execution.PlayerReposition patch: repositions only the executing player
[HarmonyPatch(typeof(Execution), "PlayerReposition")]
internal static class Execution_PlayerReposition_Patch
{
    private static bool Prefix(Execution __instance)
    {
        Penitent executor = __instance.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
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
    public static readonly HashSet<Enemy> ExecutingEnemies = new HashSet<Enemy>();

    public static bool IsEnemyBeingExecuted(Enemy enemy)
    {
        if (enemy == null) return false;
        return ExecutingEnemies.Contains(enemy);
    }

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

    internal static void ApplySkinToInteractableHierarchy(Interactable interactable, Penitent executor)
    {
        if (interactable == null || executor == null) return;

        int playerIndex = CoopPlayerRegistry.GetPlayerIndex(executor);
        string paletteId = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayerPaletteId(playerIndex);
        Texture2D paletteTexture = ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.GetPlayerPaletteTexture(playerIndex);

        if (paletteTexture == null && executor.SpriteRenderer != null && executor.SpriteRenderer.material != null)
        {
            paletteTexture = executor.SpriteRenderer.material.GetTexture("_PaletteTex") as Texture2D;
        }

        if (paletteTexture == null) return;

        // 1. Recolor all ColorPaletteSwappers in the hierarchy
        List<ColorPaletteSwapper> swappers = new List<ColorPaletteSwapper>();
        interactable.GetComponentsInChildren(true, swappers);
        if (interactable.transform.root != null && interactable.transform.root != interactable.transform)
        {
            ColorPaletteSwapper[] rootSwappers = interactable.transform.root.GetComponentsInChildren<ColorPaletteSwapper>(true);
            foreach (var s in rootSwappers)
            {
                if (!swappers.Contains(s)) swappers.Add(s);
            }
        }

        foreach (var swapper in swappers)
        {
            if (swapper == null) continue;
            ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.ApplySkinToSwapper(swapper, paletteId);
            if (swapper.extraMaterial != null)
            {
                swapper.extraMaterial.SetTexture("_PaletteTex", paletteTexture);
            }
            SpriteRenderer sr = swapper.GetComponent<SpriteRenderer>();
            if (sr != null && sr.material != null)
            {
                sr.material.SetTexture("_PaletteTex", paletteTexture);
            }
        }

        // 2. Recolor all SpriteRenderers in the hierarchy belonging to Penitent
        List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        interactable.GetComponentsInChildren(true, renderers);
        if (interactable.transform.root != null && interactable.transform.root != interactable.transform)
        {
            SpriteRenderer[] rootRenderers = interactable.transform.root.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in rootRenderers)
            {
                if (!renderers.Contains(r)) renderers.Add(r);
            }
        }

        SpriteRenderer playerSr = executor.SpriteRenderer;
        foreach (var sr in renderers)
        {
            if (sr == null || sr.material == null) continue;

            bool isPenitent = sr.GetComponent<ColorPaletteSwapper>() != null
                || sr.gameObject.CompareTag("Penitent")
                || sr.gameObject.name.IndexOf("Penitent", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isPenitent || (sr.sharedMaterial != null && sr.sharedMaterial.HasProperty("_PaletteTex")))
            {
                if (playerSr != null && playerSr.sharedMaterial != null && sr.sharedMaterial != playerSr.sharedMaterial)
                {
                    sr.sharedMaterial = playerSr.sharedMaterial;
                }
                sr.material.SetTexture("_PaletteTex", paletteTexture);
            }
        }
    }

    private static void ApplyExecutorSkin(Execution exec)
    {
        if (exec == null) return;
        Penitent executor = exec.Penitent ?? Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? Core.Logic?.Penitent;
        if (executor == null) return;

        ApplySkinToInteractableHierarchy(exec, executor);
    }

    private static bool Prefix(Execution __instance, ref IEnumerator __result)
    {
        __result = CustomOnUse(__instance);
        return false;
    }

    private static IEnumerator CustomOnUse(Execution __instance)
    {
        WaitForEndOfFrame lapse = new WaitForEndOfFrame();
        Penitent executor = Interactable_InteractionTriggered_Patch.LastInteractingPlayer ?? __instance.Penitent ?? Core.Logic?.Penitent;
        if (executor != null)
        {
            Interactable_InteractionTriggered_Patch.SetExecutionPenitent(__instance, executor);
        }
        Interactable_InteractionTriggered_Patch.LastInteractingPlayer = null;
        Enemy executedEnemy = __instance.ExecutedEntity;

        if (executedEnemy != null)
        {
            ExecutingEnemies.Add(executedEnemy);
            var statusCtrl = executedEnemy.GetComponent<EnemyElementalStatusController>();
            if (statusCtrl != null)
            {
                statusCtrl.ResetAllStatuses();
            }
        }

        try
        {
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

            yield return lapse;
            ApplyExecutorSkin(__instance);

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
        finally
        {
            if (executedEnemy != null)
            {
                ExecutingEnemies.Remove(executedEnemy);
            }
        }
    }
}


internal static class DoorTransitionGuard
{
    private static readonly HashSet<int> exitedAutoEnterDoorIds = new HashSet<int>();

    internal static void ResetOnLevelLoaded()
    {
        exitedAutoEnterDoorIds.Clear();
    }

    internal static void MarkSensorExited(Door door)
    {
        if (door != null)
        {
            exitedAutoEnterDoorIds.Add(door.GetInstanceID());
        }
    }

    internal static bool HasExitedArrivalSensor(Door door)
    {
        return door != null && exitedAutoEnterDoorIds.Contains(door.GetInstanceID());
    }
}

[HarmonyPatch(typeof(Door), "get_EnterTriggered")]
internal static class Door_EnterTriggered_Coop_Patch
{
    private static void Postfix(Door __instance, ref bool __result)
    {
        if (__result) return;
        if (__instance == null || !__instance.PlayerInRange) return;

        foreach (var guest in CoopLocal.GetAllExtraPlayers())
        {
            if (guest == null || guest.Status == null || guest.Status.Dead) continue;
            if (GlobalBlockerTracker.AnyGlobalBlocker(guest)) continue;
            if (Vector2.Distance(guest.transform.position, __instance.transform.position) > 2.8f) continue;

            int slot = CoopPlayerRegistry.GetPlayerIndex(guest);
            if (slot > 0)
            {
                if (Player2Input.GetUp(slot) || Player2Input.GetInteractDown(slot))
                {
                    __result = true;
                    DashParryDebugLog.Log($"[DoorTrigger] Door='{__instance.name}' triggered by P{slot + 1} (autoEnter={__instance.autoEnter})");
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
        if (Core.LevelManager != null && Core.LevelManager.InsideChangeLevel)
        {
            return false;
        }

        // Anti-infinite loop: prevent autoEnter doors from re-triggering for 1.2s after entering a room
        if (__instance.autoEnter && UnityEngine.Time.time < CoopLocal.LastDoorTransitionTime + 1.2f)
        {
            return false;
        }

        // Fix B: for autoEnter doors after a transition, require PlayerInRange to have been false at least once
        if (__instance.autoEnter && !DoorTransitionGuard.HasExitedArrivalSensor(__instance))
        {
            return false;
        }

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

        DashParryDebugLog.Log($"[DoorTrigger] EnterDoor allowed: Door='{__instance.name}' autoEnter={__instance.autoEnter}");
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
