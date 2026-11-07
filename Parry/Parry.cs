using Com.LuisPedroFonseca.ProCamera2D;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using System;
using System.Collections.Generic;
using System.Reflection;
using Gameplay.GameControllers.AnimationBehaviours.Player.Attack;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Projectiles;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Attack;
using Gameplay.GameControllers.Penitent.Damage;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Parry and ActiveRiposte multi-player independence:
// Fixes shared Rewired reads, global PLAYER_LOGIC blockers, wrong-owner Penitent weapon references,
// enemy parry distance adjustment targeting, and Riposte / Execution mechanics across P1..P4.
[HarmonyPatch(typeof(Parry), "OnUpdate")]
internal static class Parry_OnUpdate_Patch
{
    private static readonly MethodInfo IsGroundedMethod = AccessTools.Method(typeof(Parry), "IsGrounded");
    private static readonly MethodInfo ReadyToCastMethod = AccessTools.Method(typeof(Parry), "ReadyToCast");
    private static readonly MethodInfo RaiseParryEventMethod = AccessTools.Method(typeof(Parry), "RaiseParryEvent");
    private static readonly MethodInfo CheckParryWindowMethod = AccessTools.Method(typeof(Parry), "CheckParryWindow");

    private static readonly System.Collections.Generic.Dictionary<Parry, int> CastStartFrame = new System.Collections.Generic.Dictionary<Parry, int>();
    private static readonly System.Collections.Generic.HashSet<Parry> EnteredParryAnim = new System.Collections.Generic.HashSet<Parry>();

    internal static void NotifyParryCastStarted(Parry parry)
    {
        if (parry == null) return;
        CastStartFrame[parry] = Time.frameCount;
        EnteredParryAnim.Remove(parry);
    }

    internal static void NotifyParryCastStopped(Parry parry)
    {
        if (parry == null) return;
        CastStartFrame.Remove(parry);
        EnteredParryAnim.Remove(parry);
    }

    internal static bool IsInAnyParryAnim(Animator animator)
    {
        if (animator == null) return false;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsName("ParryStart") ||
               state.IsName("ParryChance") ||
               state.IsName("ParryFailed") ||
               state.IsName("ParrySuccess") ||
               state.IsName("ParryRepost");
    }

    private static bool Prefix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null) return true;

        if (Gameplay.UI.UIController.instance != null && Gameplay.UI.UIController.instance.IsTutorialActive())
        {
            __instance.StopCast();
        }

        bool grounded = (bool)IsGroundedMethod.Invoke(__instance, null);

        // PRIMERO (si __instance.Casting es true), antes de evaluar el input de nuevo parry:
        if (__instance.Casting)
        {
            if (owner.GuardSlide != null && owner.GuardSlide.Casting)
            {
                return false;
            }

            CheckParryWindowMethod.Invoke(__instance, null);

            var state = owner.Animator.GetCurrentAnimatorStateInfo(0);
            bool inParryChance = state.IsName("ParryStart") || state.IsName("ParryChance");
            if (owner.Parry != null)
            {
                owner.Parry.IsOnParryChance = inParryChance && (__instance.ParryWindow > 0f || state.IsName("ParryStart"));
            }

            if (IsInAnyParryAnim(owner.Animator))
            {
                EnteredParryAnim.Add(__instance);
            }

            int framesSinceCast = Time.frameCount - (CastStartFrame.TryGetValue(__instance, out int cf) ? cf : -999);
            if ((EnteredParryAnim.Contains(__instance) && !IsInAnyParryAnim(owner.Animator)) ||
                (framesSinceCast > 3 && !IsInAnyParryAnim(owner.Animator) && !__instance.SuccessParry))
            {
                __instance.StopCast();
            }

            if (!owner.Status.IsGrounded || owner.Status.Dead || owner.Status.IsHurt)
            {
                __instance.StopCast();
            }
        }

        // SEGUNDO, lee el input de parry del jugador propietario:
        bool rawParryKeyDown;
        if (CoopLocal.IsGuestPlayer(owner, out int guestSlot))
        {
            rawParryKeyDown = Player2Input.GetParryDown(guestSlot);
        }
        else
        {
            rawParryKeyDown = (__instance.Rewired != null && __instance.Rewired.GetButtonDown(38)) || Player2Input.GetParryDown(0);
        }

        if (rawParryKeyDown)
        {
            bool isBlocked = PlayerLogicBlocker.IsBlocked(owner) || GlobalBlockerTracker.HasNonPlayerLogicBlocker();
            if (grounded && !__instance.IsRunningParryAnim && (bool)ReadyToCastMethod.Invoke(__instance, null) && !__instance.SuccessParry && !isBlocked)
            {
                RaiseParryEventMethod.Invoke(__instance, null);
                __instance.Cast();
            }
        }

        // Finalmente, si !__instance.Casting && owner.Parry != null, asegura owner.Parry.IsOnParryChance = false;
        if (!__instance.Casting && owner.Parry != null)
        {
            owner.Parry.IsOnParryChance = false;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Parry), "StartParry")]
internal static class Parry_StartParry_Patch
{
    private static readonly int StartParryAnimHash = Animator.StringToHash("ParryStart");

    private static bool Prefix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null) return true;

        PlayerLogicBlocker.SetBlocked(owner, true);
        SetActionStateWatchWindow.OpenIfPlayer2(owner);
        Parry_OnUpdate_Patch.NotifyParryCastStarted(__instance);
        Core.Metrics.CustomEvent("PARRY_USED", string.Empty);
        Core.Input.SetBlocker("PLAYER_LOGIC", blocking: true);
        if (owner.PenitentAttack != null && owner.PenitentAttack.IsRunningCombo)
        {
            owner.CancelEffect?.PlayCancelEffect();
        }
        owner.Animator.Play(StartParryAnimHash);
        __instance.ParryWindow = owner.Stats.ParryWindow.Final;
        Core.Audio.EventOneShotPanned(__instance.ParryGuardFx, owner.transform.position);
        return false;
    }
}

[HarmonyPatch(typeof(Parry), "StopParry")]
internal static class Parry_StopParry_Patch
{
    private static readonly int Parry1AnimHash = Animator.StringToHash("PARRY");

    private static bool Prefix(Parry __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null) return true;

        PlayerLogicBlocker.SetBlocked(owner, false);
        Parry_OnUpdate_Patch.NotifyParryCastStopped(__instance);
        Core.Input.SetBlocker("PLAYER_LOGIC", blocking: false);
        owner.Status.Invulnerable = false;
        __instance.SuccessParry = false;
        owner.Animator.SetBool(Parry1AnimHash, false);
        if (owner.Parry != null)
        {
            owner.Parry.IsOnParryChance = false;
        }
        owner.Audio?.StopParryFx();
        return false;
    }
}

[HarmonyPatch(typeof(ParryRepostBehaviour), "OnStateEnter")]
internal static class ParryRepostBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParryRepostBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParryRepostBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(ParryRepostBehaviour), "OnStateExit")]
internal static class ParryRepostBehaviour_OnStateExit_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParryRepostBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParryRepostBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateEnter")]
internal static class ParrySuccessBehaviour_OnStateEnter_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParrySuccessBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParrySuccessBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateUpdate")]
internal static class ParrySuccessBehaviour_OnStateUpdate_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParrySuccessBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParrySuccessBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

[HarmonyPatch(typeof(ParrySuccessBehaviour), "OnStateExit")]
internal static class ParrySuccessBehaviour_OnStateExit_Patch
{
    private static readonly FieldInfo PenitentBackingField = AccessTools.Field(typeof(ParrySuccessBehaviour), "<Penitent>k__BackingField");

    private static void Prefix(Animator animator, ParrySuccessBehaviour __instance)
    {
        Penitent owner = animator.GetComponentInParent<Penitent>();
        if (owner != null)
        {
            PenitentBackingField.SetValue(__instance, owner);
        }
    }
}

internal static class GuardSlideMultiplayerSync
{
    internal static Penitent GuardSlideLastOwner;
    private static readonly FieldInfo EntityOwnerBackingField = AccessTools.Field(typeof(Ability), "<EntityOwner>k__BackingField");
    private static readonly FieldInfo PenitentField = AccessTools.Field(typeof(GuardSlide), "_penitent");
    private static readonly MethodInfo OnPenitentReadyMethod = AccessTools.Method(typeof(GuardSlide), "OnPenitentReady");

    internal static Penitent GetPenitent(GuardSlide instance)
    {
        return instance != null ? PenitentField?.GetValue(instance) as Penitent : null;
    }

    internal static void SetEntityOwner(GuardSlide instance, Penitent owner)
    {
        if (instance == null || owner == null) return;
        if (instance.EntityOwner != owner)
        {
            EntityOwnerBackingField?.SetValue(instance, owner);
        }
    }

    internal static void EnsureGuardSlideOwner(GuardSlide instance, out Penitent owner)
    {
        owner = (instance != null ? instance.EntityOwner as Penitent : null) ?? instance?.GetComponentInParent<Penitent>();
        if (instance == null || owner == null) return;

        SetEntityOwner(instance, owner);
        Penitent prevPenitent = GetPenitent(instance);
        if (prevPenitent != owner)
        {
            OnPenitentReadyMethod?.Invoke(instance, new object[] { owner });
        }
    }
}

[HarmonyPatch(typeof(GuardSlide), "OnPenitentReady")]
internal static class GuardSlide_OnPenitentReady_Patch
{
    private static bool Prefix(GuardSlide __instance, ref Penitent penitent)
    {
        Penitent actualOwner = __instance != null ? __instance.GetComponentInParent<Penitent>() : null;
        if (actualOwner != null)
        {
            penitent = actualOwner;
            GuardSlideMultiplayerSync.SetEntityOwner(__instance, actualOwner);
        }

        if (penitent == null || GuardSlideMultiplayerSync.GetPenitent(__instance) == penitent)
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(GuardSlide), nameof(GuardSlide.CastSlide))]
internal static class GuardSlide_CastSlide_Patch
{
    private static void Prefix(GuardSlide __instance, Hit hit)
    {
        GuardSlideMultiplayerSync.EnsureGuardSlideOwner(__instance, out Penitent owner);
        if (owner != null && !__instance.Casting)
        {
            PlayerLogicBlocker.SetBlocked(owner, true);
            GuardSlideMultiplayerSync.GuardSlideLastOwner = owner;
        }

        if (hit.AttackingEntity != null)
        {
            Enemy enemy = hit.AttackingEntity.GetComponentInParent<Enemy>();
            if (enemy != null && owner != null)
            {
                Combat.EnemyTargetingSystem.RecordDamageAggro(enemy, owner, 6f);
            }
            if (enemy is Gameplay.GameControllers.Enemies.LanceAngel.LanceAngel lanceAngel && hit.OnGuardCallback == null)
            {
                lanceAngel.Parry();
            }
        }
    }
}

[HarmonyPatch(typeof(GuardSlide), "OnCastStart")]
internal static class GuardSlide_OnCastStart_Patch
{
    private static void Prefix(GuardSlide __instance, out Penitent __state)
    {
        GuardSlideMultiplayerSync.EnsureGuardSlideOwner(__instance, out Penitent owner);
        GuardSlideMultiplayerSync.GuardSlideLastOwner = owner;
        __state = Core.Logic?.Penitent;
        if (owner != null && Core.Logic != null)
        {
            Core.Logic.Penitent = owner;
        }
    }

    private static void Postfix(Penitent __state)
    {
        if (Core.Logic != null && __state != null)
        {
            Core.Logic.Penitent = __state;
        }
    }
}

[HarmonyPatch(typeof(GuardSlide), "OnCastEnd")]
internal static class GuardSlide_OnCastEnd_Patch
{
    private static void Prefix(GuardSlide __instance, out Penitent __state)
    {
        GuardSlideMultiplayerSync.EnsureGuardSlideOwner(__instance, out Penitent owner);
        if (owner != null)
        {
            PlayerLogicBlocker.SetBlocked(owner, false);
        }
        __state = Core.Logic?.Penitent;
        if (owner != null && Core.Logic != null)
        {
            Core.Logic.Penitent = owner;
        }
    }

    private static void Postfix(Penitent __state)
    {
        if (Core.Logic != null && __state != null)
        {
            Core.Logic.Penitent = __state;
        }
    }
}

[HarmonyPatch(typeof(GuardSlide), "OnUpdate")]
internal static class GuardSlide_OnUpdate_Patch
{
    private static readonly FieldInfo CurrentLapseBeforeDashField = AccessTools.Field(typeof(GuardSlide), "_currentLapseBeforeDash");
    private static readonly FieldInfo TimeLerpingRemainingField = AccessTools.Field(typeof(GuardSlide), "_timeLerpingRemaining");
    private static readonly MethodInfo SetOwnerOrientationMethod = AccessTools.Method(typeof(GuardSlide), "SetOwnerOrientation");
    private static readonly MethodInfo CastDashMethod = AccessTools.Method(typeof(GuardSlide), "CastDash");

    private static bool Prefix(GuardSlide __instance)
    {
        GuardSlideMultiplayerSync.EnsureGuardSlideOwner(__instance, out Penitent owner);
        if (owner != null && CoopLocal.IsGuestPlayer(owner, out int guestSlot))
        {
            if (owner.Status.IsGrounded && __instance.Casting)
            {
                float curLapse = (float)(CurrentLapseBeforeDashField?.GetValue(__instance) ?? 0f) + Time.deltaTime;
                CurrentLapseBeforeDashField?.SetValue(__instance, curLapse);
                float timeRemaining = (float)(TimeLerpingRemainingField?.GetValue(__instance) ?? 0f) - Time.deltaTime;
                TimeLerpingRemainingField?.SetValue(__instance, timeRemaining);

                if (owner.MotionLerper != null && owner.MotionLerper.IsLerping)
                {
                    SetOwnerOrientationMethod?.Invoke(__instance, null);
                }

                if (!(!owner.FloorChecker.IsSideBlocked && owner.FloorChecker.IsGrounded))
                {
                    owner.MotionLerper?.StopLerping();
                }

                if (timeRemaining <= 0.35f && __instance.IsGuard && owner.MotionLerper != null && owner.MotionLerper.IsLerping)
                {
                    __instance.IsGuard = false;
                    owner.Animator.Play(Animator.StringToHash("GuardToIdle"));
                }

                if ((Player2Input.GetDashHeld(guestSlot) || Player2Input.GetDashDown(guestSlot)) && owner.Animator.GetCurrentAnimatorStateInfo(0).IsName("GuardToIdle"))
                {
                    CastDashMethod?.Invoke(__instance, null);
                    owner.MotionLerper?.StopLerping();
                }
            }
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(GuardToIdleBehaviour), nameof(GuardToIdleBehaviour.OnStateEnter))]
internal static class GuardToIdleBehaviour_OnStateEnter_Patch
{
    private static void Prefix(Animator animator)
    {
        Penitent owner = animator != null ? animator.GetComponentInParent<Penitent>() : null;
        if (owner != null)
        {
            PlayerLogicBlocker.SetBlocked(owner, true);
        }
    }
}

[HarmonyPatch(typeof(GuardToIdleBehaviour), nameof(GuardToIdleBehaviour.OnStateExit))]
internal static class GuardToIdleBehaviour_OnStateExit_Patch
{
    private static void Prefix(Animator animator)
    {
        Penitent owner = animator != null ? animator.GetComponentInParent<Penitent>() : null;
        if (owner != null)
        {
            PlayerLogicBlocker.SetBlocked(owner, false);
        }
    }
}

[HarmonyPatch(typeof(Gameplay.GameControllers.AnimationBehaviours.Effects.Player.GuardSlideImpactAnimationBehaviour), nameof(Gameplay.GameControllers.AnimationBehaviours.Effects.Player.GuardSlideImpactAnimationBehaviour.OnStateUpdate))]
internal static class GuardSlideImpactAnimationBehaviour_OnStateUpdate_Patch
{
    private static void Postfix(Animator animator)
    {
        if (GuardSlideMultiplayerSync.GuardSlideLastOwner != null && animator != null)
        {
            animator.transform.position = GuardSlideMultiplayerSync.GuardSlideLastOwner.transform.position;
        }
    }
}

internal static class ParryDirectionHelper
{
    internal static bool IsFacingEnemyOrSameDirection(PenitentSword sword, Penitent defender, Entity enemy)
    {
        if (sword != null && !sword.CheckParryEnemyDirection) return true;
        if (enemy == null) return false;
        if (sword != null && sword.IsEnemySameDirection(enemy)) return true;
        if (defender != null)
        {
            float dx = enemy.transform.position.x - defender.transform.position.x;
            if (Mathf.Abs(dx) <= 0.25f) return true;
            return defender.Status.Orientation == EntityOrientation.Right ? dx >= 0f : dx <= 0f;
        }
        return false;
    }
}

// Penitent.Damage patch: uses the actual receiving Penitent's weapon and resolves Enemy via GetComponentInParent
[HarmonyPatch(typeof(Penitent), nameof(Penitent.Damage))]
internal static class Penitent_Damage_Patch
{
    private static bool Prefix(Penitent __instance, Hit hit)
    {
        if (!__instance.DamageArea || (__instance.LungeAttack != null && __instance.LungeAttack.Casting && hit.DamageElement == DamageArea.DamageElement.Contact) || __instance.IsOnExecution || __instance.IsPickingCollectibleItem || (__instance.GuardSlide != null && __instance.GuardSlide.Casting))
        {
            return false;
        }

        if (__instance.Parry != null && __instance.Parry.IsOnParryChance && hit.DamageType == DamageArea.DamageType.Normal && !hit.forceGuardslide)
        {
            __instance.Parry.IsOnParryChance = false;
            if (!__instance.Parry.CheckParry(hit))
            {
                __instance.DamageArea.TakeDamage(hit);
            }
            return false;
        }
        else if (__instance.Parry != null && __instance.Parry.IsOnParryChance && !hit.Unblockable && (hit.DamageType == DamageArea.DamageType.Heavy || hit.forceGuardslide))
        {
            PenitentSword penitentSword = (PenitentSword)__instance.PenitentAttack.CurrentPenitentWeapon;
            Enemy enemy = (hit.AttackingEntity != null) ? hit.AttackingEntity.GetComponentInParent<Enemy>() : null;
            if (!hit.CheckOrientationsForGuardslide || ParryDirectionHelper.IsFacingEnemyOrSameDirection(penitentSword, __instance, enemy))
            {
                __instance.GuardSlide?.CastSlide(hit);
            }
            else
            {
                __instance.DamageArea.TakeDamage(hit);
            }
            return false;
        }

        return true;
    }
}

// PenitentSword.SuccessParryChance patch:
// Resolves Enemy and ParriableProjectile using GetComponentInParent, ensures WeaponOwner,
// registers aggro towards the parrying player, and directs enemy parry logic (Acolyte, Crisanta, etc.)
// towards the parrying player.
[HarmonyPatch(typeof(PenitentSword), nameof(PenitentSword.SuccessParryChance))]
internal static class PenitentSword_SuccessParryChance_Patch
{
    private static readonly FieldInfo OnParryField = AccessTools.Field(typeof(PenitentSword), "OnParry");

    private static void RaiseOnParry(PenitentSword sword, Enemy enemy)
    {
        if (sword == null || OnParryField == null) return;
        var del = OnParryField.GetValue(sword) as Core.SimpleEventParam;
        del?.Invoke(enemy);
    }

    private static bool Prefix(PenitentSword __instance, Hit hit, ref bool __result)
    {
        bool result = false;
        Enemy component = hit.AttackingEntity != null ? hit.AttackingEntity.GetComponentInParent<Enemy>() : null;
        ParriableProjectile component2 = hit.AttackingEntity != null ? hit.AttackingEntity.GetComponentInParent<ParriableProjectile>() : null;

        Penitent ownerPenitent = __instance.WeaponOwner as Penitent ?? __instance.GetComponentInParent<Penitent>();
        if (ownerPenitent != null && __instance.WeaponOwner == null)
        {
            __instance.WeaponOwner = ownerPenitent;
        }

        if (component != null)
        {
            if ((component.IsParryable || component is Gameplay.GameControllers.Enemies.LanceAngel.LanceAngel) && !hit.Unparriable && ParryDirectionHelper.IsFacingEnemyOrSameDirection(__instance, ownerPenitent, component))
            {
                result = true;
                if (ownerPenitent != null)
                {
                    Combat.EnemyTargetingSystem.RecordDamageAggro(component, ownerPenitent, 6f);
                }

                Penitent oldPenitent = Core.Logic != null ? Core.Logic.Penitent : null;
                try
                {
                    if (ownerPenitent != null && Core.Logic != null)
                    {
                        Core.Logic.Penitent = ownerPenitent;
                    }
                    component.Parry();
                }
                finally
                {
                    if (Core.Logic != null && oldPenitent != null)
                    {
                        Core.Logic.Penitent = oldPenitent;
                    }
                }

                if (component is Gameplay.GameControllers.Enemies.ViciousDasher.ViciousDasher vd)
                {
                    vd.MotionLerper?.StopLerping();
                    if (vd.DamageArea?.DamageAreaCollider != null) vd.DamageArea.DamageAreaCollider.enabled = true;
                    vd.StateMachine?.SwitchState<Gameplay.GameControllers.Enemies.ViciousDasher.AI.ViciousDasherDeathState>();
                }

                RaiseOnParry(__instance, component);
            }
        }
        else if (component2 != null && !hit.Unparriable)
        {
            bool facingProj = !__instance.CheckParryEnemyDirection || (ownerPenitent == null) ||
                (ownerPenitent.Status.Orientation == EntityOrientation.Right ? component2.transform.position.x >= ownerPenitent.transform.position.x : component2.transform.position.x <= ownerPenitent.transform.position.x);

            if (facingProj)
            {
                result = true;
                component2.OnParry();
                RaiseOnParry(__instance, component);
            }
        }

        __result = result;
        return false;
    }
}

[HarmonyPatch(typeof(Gameplay.GameControllers.AnimationBehaviours.Enemies.Stunt.ParryBehaviour), nameof(Gameplay.GameControllers.AnimationBehaviours.Enemies.Stunt.ParryBehaviour.OnStateUpdate))]
internal static class Enemies_Stunt_ParryBehaviour_OnStateUpdate_Patch
{
    private static void Prefix(out Penitent __state)
    {
        __state = null;
        Penitent executingPlayer = null;
        if (Core.Logic?.Penitent != null && Core.Logic.Penitent.IsOnExecution)
        {
            executingPlayer = Core.Logic.Penitent;
        }
        else
        {
            var activePlayers = CoopPlayerRegistry.GetActivePlayers();
            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (activePlayers[i] != null && activePlayers[i].IsOnExecution)
                {
                    executingPlayer = activePlayers[i];
                    break;
                }
            }
        }

        if (executingPlayer != null && Core.Logic != null && Core.Logic.Penitent != executingPlayer)
        {
            __state = Core.Logic.Penitent;
            Core.Logic.Penitent = executingPlayer;
        }
    }

    private static void Finalizer(Penitent __state)
    {
        if (__state != null && Core.Logic != null)
        {
            Core.Logic.Penitent = __state;
        }
    }
}

// ActiveRiposte.get_AttackTrigger patch:
// Directs riposte attack trigger to the specific player's attack key (P1..P4)
[HarmonyPatch(typeof(ActiveRiposte), "get_AttackTrigger")]
internal static class ActiveRiposte_AttackTrigger_Patch
{
    private static bool Prefix(ActiveRiposte __instance, ref bool __result)
    {
        Penitent owner = __instance.EntityOwner as Penitent ?? __instance.GetComponentInParent<Penitent>();
        if (owner == null) return true;

        if (CoopLocal.IsGuestPlayer(owner, out int guestSlot))
        {
            __result = Player2Input.GetAttackDown(guestSlot);
            return false;
        }

        if (owner == Core.Logic.Penitent)
        {
            bool p1Rewired = Core.Logic?.Penitent?.PlatformCharacterInput?.Rewired != null && Core.Logic.Penitent.PlatformCharacterInput.Rewired.GetButtonDown(5);
            bool p1Custom = Player2Input.GetAttackDown(0);
            __result = p1Rewired || p1Custom;
            return false;
        }

        return true;
    }
}

// Enemy.Execution patch:
// Swaps Core.Logic.Penitent to the real attacker so ActiveRiposte.MakeRiposte, LungeAttack,
// and ReleaseChargedAttack are evaluated on the attacker entity.
[HarmonyPatch(typeof(Gameplay.GameControllers.Entities.Enemy), nameof(Gameplay.GameControllers.Entities.Enemy.Execution))]
internal static class Enemy_Execution_Patch
{
    private static void Prefix(Hit hit, out Penitent __state)
    {
        __state = null;
        Penitent attacker = hit.AttackingEntity != null ? hit.AttackingEntity.GetComponentInParent<Penitent>() : null;
        if (attacker != null && Core.Logic != null && attacker != Core.Logic.Penitent)
        {
            __state = Core.Logic.Penitent;
            Core.Logic.Penitent = attacker;
        }
    }

    private static void Finalizer(Penitent __state)
    {
        if (__state != null && Core.Logic != null)
        {
            Core.Logic.Penitent = __state;
        }
    }
}
