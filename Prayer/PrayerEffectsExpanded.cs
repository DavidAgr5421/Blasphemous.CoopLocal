using Framework.FrameworkCore;
using Framework.Inventory;
using Framework.Managers;
using Gameplay.GameControllers.Effects.Player.Protection;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Entities.Guardian;
using Gameplay.GameControllers.Entities.Guardian.AI;
using Gameplay.GameControllers.Entities.MiriamPortal;
using Gameplay.GameControllers.Entities.MiriamPortal.AI;
using Gameplay.GameControllers.Environment.Traps.FireTrap;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Prayers;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Tools.Items;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Ronda 85: Implementación completa y paridad de todas las oraciones (rezos) para P2.
// Resuelve todas las instancias donde el juego vanilla asume P1 como el único lanzador:
// - PR03 / PR101: Zarabanda del Salvador Solitario / Seguidilla al Velo (Escudos orbitales)
// - PR04: Saeta Dolorosa (Ataque pesado y aumento de nivel de ataque Mea Culpa)
// - PR10: Tientos a tus Cabellos Ensangrentados (Protección e invulnerabilidad con dama protectora)
// - PR12: Zambra al Resplandor Coronado (Efecto visual de cosecha de lágrimas de expiación)
// - PR14: Campanillero a los Hijos de la Aurora (Querubines aliados orbitando y atacando)
// - PR15: Taranto a la Hermana Mía (Círculo de rayos y onda expansiva de daño en área)
// - PR16: Aubade of the Nameless Guardian (Guardián espectral acompañante que ataca y cubre a P2)
// - PR201: Cantó de los Muertos / Miriam (Portal dimensional de Miriam atacante)
// - PR202: Regreso a Puerto (Teletransporte al reclinatorio activo con animación dual)
// - PR203: Fuego de San Telmo (Anillo de trampas eléctricas centrado y orientado en P2)

#region 1. Escudos de Protección (PR03 / PR101)

internal static class ShieldOwnerTracker
{
    private static readonly Dictionary<PenitentShieldSystem, Entity> owners = new Dictionary<PenitentShieldSystem, Entity>();

    internal static void SetOwner(PenitentShieldSystem system, Entity owner)
    {
        if (system != null)
        {
            owners[system] = owner;
        }
    }

    internal static Entity GetOwner(PenitentShieldSystem system)
    {
        if (system != null && owners.TryGetValue(system, out Entity owner))
        {
            return owner;
        }
        return null;
    }
}

[HarmonyPatch(typeof(ShieldSystemPrayer), "InstantiateShield")]
internal static class ShieldSystemPrayer_InstantiateShield_Patch
{
    private static void Postfix(ShieldSystemPrayer __instance, GameObject ____instantiatedShield)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster != null && CoopLocal.IsGuestPlayer(caster, out _) && ____instantiatedShield != null)
        {
            ____instantiatedShield.transform.position = caster.transform.position;
            PenitentShieldSystem system = ____instantiatedShield.GetComponentInChildren<PenitentShieldSystem>();
            if (system != null)
            {
                system.SetShieldsOwner(caster);
                ShieldOwnerTracker.SetOwner(system, caster);
            }
        }
    }
}

[HarmonyPatch(typeof(PenitentShieldSystem), "FollowPlayer")]
internal static class PenitentShieldSystem_FollowPlayer_Patch
{
    private static bool Prefix(PenitentShieldSystem __instance, Vector2 ___OffSetPosition, float ___FollowSpeed)
    {
        Entity owner = ShieldOwnerTracker.GetOwner(__instance);
        if (owner == null || !CoopLocal.IsGuestPlayer(owner as Penitent, out _))
        {
            return true;
        }
        Vector3 b = owner.transform.position + (Vector3)___OffSetPosition;
        __instance.transform.position = Vector3.Slerp(__instance.transform.position, b, Time.deltaTime * ___FollowSpeed);
        return false;
    }
}

#endregion

#region 2. Querubines Aliados (PR14)

internal static class CherubOwnerTracker
{
    private static readonly Dictionary<AlliedCherubSystem, Entity> owners = new Dictionary<AlliedCherubSystem, Entity>();

    internal static void SetOwner(AlliedCherubSystem system, Entity owner)
    {
        if (system != null)
        {
            owners[system] = owner;
        }
    }

    internal static Entity GetOwner(AlliedCherubSystem system)
    {
        if (system != null && owners.TryGetValue(system, out Entity owner))
        {
            return owner;
        }
        return null;
    }
}

[HarmonyPatch(typeof(AlliedCherubPrayer), "InstantiateCherubs")]
internal static class AlliedCherubPrayer_InstantiateCherubs_Patch
{
    private static void Postfix(AlliedCherubPrayer __instance, GameObject ____instantiatedAlliedCherubSystem)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster != null && CoopLocal.IsGuestPlayer(caster, out _) && ____instantiatedAlliedCherubSystem != null)
        {
            ____instantiatedAlliedCherubSystem.transform.position = caster.transform.position;
            AlliedCherubSystem system = ____instantiatedAlliedCherubSystem.GetComponentInChildren<AlliedCherubSystem>();
            if (system != null)
            {
                CherubOwnerTracker.SetOwner(system, caster);
            }
        }
    }
}

[HarmonyPatch(typeof(Gameplay.GameControllers.AlliedCherub.AlliedCherub), "Deploy")]
internal static class AlliedCherub_Deploy_Patch
{
    private static readonly PropertyInfo AllyProp = AccessTools.Property(typeof(Gameplay.GameControllers.AlliedCherub.AlliedCherub), "Ally");
    private static readonly PropertyInfo FlyingPosProp = AccessTools.Property(typeof(Gameplay.GameControllers.AlliedCherub.AlliedCherub), "FlyingPosition");

    private static void Postfix(Gameplay.GameControllers.AlliedCherub.AlliedCherub __instance,
        AlliedCherubSystem.AlliedCherubSlot cherubSlot,
        AlliedCherubSystem cherubSystem)
    {
        Entity owner = CherubOwnerTracker.GetOwner(cherubSystem);
        if (owner != null && CoopLocal.IsGuestPlayer(owner as Penitent, out _))
        {
            AllyProp.SetValue(__instance, owner, null);
            FlyingPosProp.SetValue(__instance, owner.transform.position + (Vector3)cherubSlot.Offset, null);
        }
    }
}

[HarmonyPatch(typeof(AlliedCherubSystem), "DistanceToEnemy")]
internal static class AlliedCherubSystem_DistanceToEnemy_Patch
{
    private static bool Prefix(AlliedCherubSystem __instance, Enemy enemy, ref float __result)
    {
        Entity owner = CherubOwnerTracker.GetOwner(__instance);
        if (owner != null && CoopLocal.IsGuestPlayer(owner as Penitent, out _) && enemy != null)
        {
            __result = Vector2.Distance(owner.transform.position, enemy.transform.position);
            return false;
        }
        return true;
    }
}

#endregion

#region 3. Taranto a la Hermana Mía (PR15)

[HarmonyPatch(typeof(PenitentAreaAttack), "OnApplyEffect")]
internal static class PenitentAreaAttack_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo RadiusField = AccessTools.Field(typeof(PenitentAreaAttack), "Radius");
    private static readonly FieldInfo DamageMaskField = AccessTools.Field(typeof(PenitentAreaAttack), "damageMask");
    private static readonly FieldInfo PrefabIntoPlayerField = AccessTools.Field(typeof(PenitentAreaAttack), "prefabIntoPlayer");
    private static readonly FieldInfo PrefabIntoEnemiesField = AccessTools.Field(typeof(PenitentAreaAttack), "prefabIntoEnemies");
    private static readonly FieldInfo IntoPlayerOffsetField = AccessTools.Field(typeof(PenitentAreaAttack), "intoPlayerOffset");
    private static readonly FieldInfo IntoEnemiesOffsetField = AccessTools.Field(typeof(PenitentAreaAttack), "intoEnemiesOffset");
    private static readonly FieldInfo DamageDelayField = AccessTools.Field(typeof(PenitentAreaAttack), "damageDelay");
    private static readonly FieldInfo DamageTypeField = AccessTools.Field(typeof(PenitentAreaAttack), "DamageType");
    private static readonly FieldInfo AmountField = AccessTools.Field(typeof(PenitentAreaAttack), "Amount");
    private static readonly FieldInfo ForceField = AccessTools.Field(typeof(PenitentAreaAttack), "Force");
    private static readonly FieldInfo UnnavoidableField = AccessTools.Field(typeof(PenitentAreaAttack), "Unnavoidable");
    private static readonly FieldInfo HitSoundField = AccessTools.Field(typeof(PenitentAreaAttack), "HitSound");

    private static bool Prefix(PenitentAreaAttack __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        float amount = (float)AmountField.GetValue(__instance);
        float mult = caster.Stats.PrayerStrengthMultiplier.Final;
        float dmg = amount * (1f + (mult - 1f) * 0.5f);

        Hit hit = new Hit
        {
            AttackingEntity = caster.gameObject,
            DamageType = (DamageArea.DamageType)DamageTypeField.GetValue(__instance),
            Force = (float)ForceField.GetValue(__instance),
            DamageAmount = dmg,
            HitSoundId = (string)HitSoundField.GetValue(__instance),
            Unnavoidable = (bool)UnnavoidableField.GetValue(__instance)
        };

        GameObject prefabIntoPlayer = (GameObject)PrefabIntoPlayerField.GetValue(__instance);
        Vector2 intoPlayerOffset = (Vector2)IntoPlayerOffsetField.GetValue(__instance);
        if (prefabIntoPlayer != null)
        {
            PoolManager.Instance.ReuseObject(prefabIntoPlayer, caster.GetPosition() + (Vector3)intoPlayerOffset, Quaternion.identity);
        }

        __instance.StartCoroutine(DamageCoroutineP2(__instance, hit, caster));
        __result = true;
        return false;
    }

    private static System.Collections.IEnumerator DamageCoroutineP2(PenitentAreaAttack instance, Hit hit, Penitent caster)
    {
        float radius = (float)RadiusField.GetValue(instance);
        LayerMask damageMask = (LayerMask)DamageMaskField.GetValue(instance);
        GameObject prefabIntoEnemies = (GameObject)PrefabIntoEnemiesField.GetValue(instance);
        Vector2 intoPlayerOffset = (Vector2)IntoPlayerOffsetField.GetValue(instance);
        Vector2 intoEnemiesOffset = (Vector2)IntoEnemiesOffsetField.GetValue(instance);
        float damageDelay = (float)DamageDelayField.GetValue(instance);

        Core.Logic.ScreenFreeze.Freeze(0.05f, instance.slowTimeDuration, 0f, instance.slowTimeCurve);
        Core.Logic.CameraManager.ShockwaveManager.Shockwave(caster.transform.position + (Vector3)intoPlayerOffset, 1.2f, 0.2f, 1.8f);
        Core.Logic.CameraManager.ProCamera2DShake.Shake(0.35f, Vector3.down * 0.5f, 12, 0.2f, 0.01f, default(Vector3), 0.01f, ignoreTimeScale: true);

        Collider2D[] hits = new Collider2D[20];
        int overlapped = Physics2D.OverlapCircleNonAlloc(caster.GetPosition() + (Vector3)intoPlayerOffset, radius, hits, damageMask);
        float currentDelay = damageDelay;

        for (int j = 0; j < overlapped; j++)
        {
            if (!hits[j] || hits[j].CompareTag("Penitent"))
            {
                continue;
            }
            EnemyDamageArea dmgArea = null;
            Enemy enemy = hits[j].GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                dmgArea = enemy.GetComponentInChildren<EnemyDamageArea>();
                if (enemy.SpriteRenderer != null && !enemy.SpriteRenderer.IsVisibleFrom(Camera.main))
                {
                    continue;
                }
            }
            if (dmgArea != null)
            {
                if (prefabIntoEnemies != null)
                {
                    PoolManager.Instance.ReuseObject(prefabIntoEnemies, hits[j].bounds.center + (Vector3)intoEnemiesOffset, Quaternion.identity);
                }
                yield return new WaitForSeconds(currentDelay);
                currentDelay *= 0.8f;
                if (dmgArea != null && dmgArea.OwnerEntity != null)
                {
                    ((IDamageable)dmgArea.OwnerEntity).Damage(hit);
                }
                continue;
            }
            IDamageable damageable = hits[j].GetComponentInChildren<IDamageable>();
            if (damageable != null && !(damageable is Gameplay.GameControllers.Enemies.Projectiles.ProjectileReaction))
            {
                if (prefabIntoEnemies != null)
                {
                    PoolManager.Instance.ReuseObject(prefabIntoEnemies, hits[j].gameObject.transform.position + (Vector3)intoEnemiesOffset, Quaternion.identity);
                }
                yield return new WaitForSeconds(currentDelay);
                currentDelay *= 0.8f;
                damageable.Damage(hit);
            }
        }
    }
}

#endregion

#region 4. Saeta Dolorosa (PR04)

[HarmonyPatch(typeof(HeavyAttackPrayerEffect), "OnApplyEffect")]
internal static class HeavyAttackPrayerEffect_OnApplyEffect_P2_Patch
{
    internal static int prevP2Level = 1;

    private static bool Prefix(HeavyAttackPrayerEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }
        caster.PenitentAttack.IsHeavyAttackPrayerEquipped = true;
        prevP2Level = caster.PenitentAttack.CurrentLevel;
        caster.PenitentAttack.CurrentLevel = 2;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(HeavyAttackPrayerEffect), "OnRemoveEffect")]
internal static class HeavyAttackPrayerEffect_OnRemoveEffect_P2_Patch
{
    private static bool Prefix(HeavyAttackPrayerEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }
        caster.PenitentAttack.IsHeavyAttackPrayerEquipped = false;
        caster.PenitentAttack.CurrentLevel = HeavyAttackPrayerEffect_OnApplyEffect_P2_Patch.prevP2Level;
        return false;
    }
}

#endregion

#region 5. Tientos a tus Cabellos Ensangrentados (PR10)

[HarmonyPatch(typeof(PenitentGuardianEffect), "OnApplyEffect")]
internal static class PenitentGuardianEffect_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(PenitentGuardianEffect), "_owner");

    private static void Prefix(PenitentGuardianEffect __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster != null && CoopLocal.IsGuestPlayer(caster, out _))
        {
            OwnerField.SetValue(__instance, caster);
        }
    }
}

[HarmonyPatch(typeof(PenitentGuardianEffect), "InstantiateGuardian")]
internal static class PenitentGuardianEffect_InstantiateGuardian_P2_Patch
{
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(PenitentGuardianEffect), "_owner");
    private static readonly FieldInfo PenitentGuardianField = AccessTools.Field(typeof(PenitentGuardianEffect), "_penitentGuardian");

    private static bool Prefix(PenitentGuardianEffect __instance)
    {
        Penitent owner = (Penitent)OwnerField.GetValue(__instance);
        if (owner == null || !CoopLocal.IsGuestPlayer(owner as Penitent, out _))
        {
            return true;
        }

        Vector3 position = owner.transform.position;
        position.y += __instance.YOffset;

        GameObject guardianObj = (GameObject)PenitentGuardianField.GetValue(__instance);
        if (guardianObj == null)
        {
            if (__instance.PenitentGuardianPrefab != null)
            {
                guardianObj = UnityEngine.Object.Instantiate(__instance.PenitentGuardianPrefab, position, Quaternion.identity);
                PenitentGuardianField.SetValue(__instance, guardianObj);
                Gameplay.GameControllers.Effects.Player.PenitentGuardian comp =
                    guardianObj.GetComponent<Gameplay.GameControllers.Effects.Player.PenitentGuardian>();
                comp.SetOrientation(__instance.currentHit);
            }
            return false;
        }

        guardianObj.SetActive(true);
        Gameplay.GameControllers.Effects.Player.PenitentGuardian comp2 =
            guardianObj.GetComponent<Gameplay.GameControllers.Effects.Player.PenitentGuardian>();
        if (!comp2.IsTriggered)
        {
            comp2.SetOrientation(__instance.currentHit);
            guardianObj.transform.position = position;
        }
        return false;
    }
}

[HarmonyPatch(typeof(PenitentGuardianEffect), "Update")]
internal static class PenitentGuardianEffect_Update_P2_Patch
{
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(PenitentGuardianEffect), "_owner");
    private static readonly FieldInfo ShowLadyField = AccessTools.Field(typeof(PenitentGuardianEffect), "_showLady");

    private static bool Prefix(PenitentGuardianEffect __instance)
    {
        Penitent owner = (Penitent)OwnerField.GetValue(__instance);
        if (owner == null || !CoopLocal.IsGuestPlayer(owner as Penitent, out _))
        {
            return true;
        }

        bool showLady = (bool)ShowLadyField.GetValue(__instance);
        if (!__instance.InvulnerableEffect || !__instance.InvulnerableEffect.IsApplied)
        {
            if (showLady)
            {
                ShowLadyField.SetValue(__instance, false);
                if (owner.DamageArea != null)
                {
                    owner.DamageArea.PrayerProtectionEnabled = false;
                }
                if (owner.Status.Invulnerable)
                {
                    owner.Status.Invulnerable = false;
                }
            }
        }
        else if (!owner.Status.Invulnerable)
        {
            ShowLadyField.SetValue(__instance, true);
            if (owner.DamageArea != null)
            {
                owner.DamageArea.PrayerProtectionEnabled = true;
            }
            owner.Status.Invulnerable = true;
        }
        return false;
    }
}

#endregion

#region 6. Zambra al Resplandor Coronado (PR12)

internal static class ZambraCasterTracker
{
    internal static Penitent LastCaster;
}

[HarmonyPatch(typeof(ZambraTearsHarvestEffect), "OnApplyEffect")]
internal static class ZambraTearsHarvestEffect_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo IsRunningEffectField = AccessTools.Field(typeof(ZambraTearsHarvestEffect), "<IsRunningEffect>k__BackingField");
    private static readonly MethodInfo DisableCoroutineMethod = AccessTools.Method(typeof(ZambraTearsHarvestEffect), "DisableCoroutine");

    private static bool Prefix(ZambraTearsHarvestEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        bool isRunning = (bool)IsRunningEffectField.GetValue(__instance);
        if (!isRunning)
        {
            IsRunningEffectField.SetValue(__instance, true);
            ZambraTearsHarvestPrayer p2Prayer = caster.GetComponentInChildren<ZambraTearsHarvestPrayer>();
            if (p2Prayer != null)
            {
                p2Prayer.EnableEffect();
            }
            ZambraCasterTracker.LastCaster = caster;
            __instance.StartCoroutine((System.Collections.IEnumerator)DisableCoroutineMethod.Invoke(__instance, new object[] { __instance.Duration }));
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(TearHarvestEffect), "LateUpdate")]
internal static class TearHarvestEffect_LateUpdate_P2_Patch
{
    private static bool Prefix(TearHarvestEffect __instance)
    {
        Penitent caster = ZambraCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }
        Vector3 position = caster.GetPosition() + ((!caster.IsCrouched) ? ((Vector3)__instance.Offset) : Vector3.zero);
        __instance.transform.position = position;
        return false;
    }
}

#endregion

#region 7. Aubade of the Nameless Guardian (PR16)

[HarmonyPatch(typeof(PrayerGhostGuardian), "InstantiateGuardian")]
internal static class PrayerGhostGuardian_InstantiateGuardian_P2_Patch
{
    private static readonly FieldInfo InstantiationField = AccessTools.Field(typeof(PrayerGhostGuardian), "GhostGuardianInstantiation");

    private static bool Prefix(PrayerGhostGuardian __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        GameObject inst = (GameObject)InstantiationField.GetValue(__instance);
        if (__instance.GhostGuardianPrefab != null && inst == null)
        {
            Vector3 position = caster.transform.position;
            inst = UnityEngine.Object.Instantiate(__instance.GhostGuardianPrefab, position, Quaternion.identity);
            InstantiationField.SetValue(__instance, inst);
        }
        else if (inst != null)
        {
            inst.SetActive(true);
            inst.transform.position = caster.transform.position;
        }

        if (inst != null)
        {
            GuardianPrayer guardian = inst.GetComponentInChildren<GuardianPrayer>();
            if (guardian != null && guardian.Behaviour != null)
            {
                guardian.Behaviour.Master = caster;
                guardian.Behaviour.SetInitialOrientation();
            }
        }
        return false;
    }
}

[HarmonyPatch(typeof(GuardianPrayerBehaviour), "get_GetMasterOffSetPosition")]
internal static class GuardianPrayerBehaviour_GetMasterOffSetPosition_Patch
{
    private static bool Prefix(GuardianPrayerBehaviour __instance, ref Vector3 __result)
    {
        if (__instance.Master == null || __instance.Master != CoopLocal.Player2)
        {
            return true;
        }
        Penitent p2 = CoopLocal.Player2;
        float hSpeed = p2.PlatformCharacterController.PlatformCharacterPhysics.HSpeed;
        if (Mathf.Abs(hSpeed) > __instance.ThresholdSpeed)
        {
            __result = p2.transform.position;
            return false;
        }
        if (p2.Status.Orientation == EntityOrientation.Right)
        {
            __result = p2.transform.position - (Vector3)__instance.MasterOffsetPosition;
            return false;
        }
        __result = p2.transform.position + (Vector3)__instance.MasterOffsetPosition;
        return false;
    }
}

#endregion

#region 8. Miriam - Cantó de los Muertos (PR201)

[HarmonyPatch(typeof(PrayerMiriam), "InstantiatePortal")]
internal static class PrayerMiriam_InstantiatePortal_P2_Patch
{
    private static readonly FieldInfo InstantiationField = AccessTools.Field(typeof(PrayerMiriam), "PortalInstantiation");
    private static readonly FieldInfo PortalField = AccessTools.Field(typeof(PrayerMiriam), "MiriamPortal");

    private static bool Prefix(PrayerMiriam __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        Vector3 targetPos = caster.transform.position + Vector3.up * __instance.vOffset;
        targetPos.x += (caster.GetOrientation() != EntityOrientation.Right ? __instance.hOffest : -__instance.hOffest);

        GameObject inst = (GameObject)InstantiationField.GetValue(__instance);
        MiriamPortalPrayer portal = (MiriamPortalPrayer)PortalField.GetValue(__instance);

        if (__instance.PortalPrefab != null && inst == null)
        {
            inst = UnityEngine.Object.Instantiate(__instance.PortalPrefab, targetPos, Quaternion.identity);
            InstantiationField.SetValue(__instance, inst);
        }
        else if (inst != null)
        {
            if (inst.activeInHierarchy && portal != null && portal.Behaviour != null)
            {
                portal.Behaviour.ReappearFlag = true;
                portal.Behaviour.VanishFlag = false;
            }
            else
            {
                inst.SetActive(true);
                inst.transform.position = targetPos;
            }
        }

        if (portal == null && inst != null)
        {
            portal = inst.GetComponentInChildren<MiriamPortalPrayer>();
            PortalField.SetValue(__instance, portal);
        }

        if (portal != null && portal.Behaviour != null)
        {
            portal.Behaviour.Master = caster;
            portal.Behaviour.SetState(MiriamPortalPrayerBehaviour.MiriamPortalState.Follow);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PrayerMiriam), "OnUpdate")]
internal static class PrayerMiriam_OnUpdate_P2_Patch
{
    private static bool Prefix(PrayerMiriam __instance)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster != null && CoopLocal.IsGuestPlayer(caster, out int guestSlot))
        {
            var p2Prayer = Player2InventoryManager.GetEquippedPrayerObj(guestSlot);
            if (p2Prayer == null || p2Prayer.id != "PR201")
            {
                __instance.DisposePortal();
            }
            return false;
        }
        return true;
    }
}

#endregion

#region 9. Fuego de San Telmo (PR203)

[HarmonyPatch(typeof(PR203ElmFireLoopEffect), "OnApplyEffect")]
internal static class PR203ElmFireLoopEffect_OnApplyEffect_P2_Patch
{
    private static readonly MethodInfo ElmFireLoopCoroutineMethod =
        AccessTools.Method(typeof(PR203ElmFireLoopEffect), "ElmFireLoopCoroutine");

    private static bool Prefix(PR203ElmFireLoopEffect __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        Vector3 position = caster.GetPosition();
        position += ((caster.Status.Orientation != EntityOrientation.Left) ? (Vector3.right * 1.25f) : (Vector3.left * 1.25f));
        PoolManager.ObjectInstance objectInstance = PoolManager.Instance.ReuseObject(__instance.ElmFireLoop, position, Quaternion.identity, createPoolIfNeeded: true);
        int num = ((caster.Status.Orientation != EntityOrientation.Left) ? 1 : (-1));
        objectInstance.GameObject.transform.localScale = new Vector3(num, 1f, 1f);

        ElmFireTrapManager componentInChildren = objectInstance.GameObject.GetComponentInChildren<ElmFireTrapManager>();
        float final = caster.Stats.PrayerStrengthMultiplier.Final;
        componentInChildren.RecursiveSetUpTrapDamage((float)__instance.BaseLightningDamage * final);

        __instance.StartCoroutine((System.Collections.IEnumerator)ElmFireLoopCoroutineMethod.Invoke(__instance, new object[] { componentInChildren }));
        __result = true;
        return false;
    }
}

#endregion

#region 10. Regreso a Puerto (PR202)

[HarmonyPatch(typeof(PenitentTeleportToPriedieu), "OnApplyEffect")]
internal static class PenitentTeleportToPriedieu_OnApplyEffect_P2_Patch
{
    private static bool Prefix(PenitentTeleportToPriedieu __instance, ref bool __result)
    {
        Penitent caster = PrayerCasterTracker.LastCaster;
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        if (Core.Input.HasBlocker("INTERACTABLE"))
        {
            __result = false;
            return false;
        }

        CoopLocal.Player2.Animator?.Play(__instance.TPOAnimatorState, 0, 0f);
        Core.Logic.Penitent.Animator?.Play(__instance.TPOAnimatorState, 0, 0f);
        __result = true;
        return false;
    }
}

#endregion

#region 11. Següiriya a tus Ojos como Estrellas y Rezos de Estadísticas / Efectos Temporales (ObjectEffect_Stat, ItemGhostTrail, ItemTemporalEffect, PrayerUseVfx, ProtectionDomeEffect, ToxicCloudEffect)

internal static class ObjectEffectCasterTracker
{
    private static readonly Dictionary<ObjectEffect, Penitent> activeCasters = new Dictionary<ObjectEffect, Penitent>();

    internal static void SetCaster(ObjectEffect effect, Penitent caster)
    {
        if (effect != null)
        {
            activeCasters[effect] = caster;
        }
    }

    internal static Penitent GetCaster(ObjectEffect effect)
    {
        if (effect != null && activeCasters.TryGetValue(effect, out Penitent caster) && caster != null)
        {
            return caster;
        }
        return PrayerCasterTracker.LastCaster;
    }
}

// Ronda 89: Redirigir todos los efectos basados en ObjectEffect_Stat (como Següiriya a tus Ojos como Estrellas,
// Soleá de la Excomunión, Romance a la Bruma Carmesí, etc.) hacia las estadísticas de P2 cuando P2 es quien
// lanza el rezo (o equipa la cuenta/corazón en su vista).
[HarmonyPatch(typeof(ObjectEffect_Stat), "OnApplyEffect")]
internal static class ObjectEffect_Stat_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo CurrentValueField = AccessTools.Field(typeof(ObjectEffect_Stat), "currentValue");
    private static readonly FieldInfo BonusField = AccessTools.Field(typeof(ObjectEffect_Stat), "bonus");
    private static readonly FieldInfo CurrentHitField = AccessTools.Field(typeof(ObjectEffect), "currentHit");

    private static bool Prefix(ObjectEffect_Stat __instance, ref bool __result)
    {
        Penitent caster = ResolveEffectTarget(__instance);
        ObjectEffectCasterTracker.SetCaster(__instance, caster);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        if (__instance.statType == EntityStats.StatsTypes.Life &&
            __instance.effectMode == ObjectEffect_Stat.EffectMode.Current &&
            __instance.value > 1f &&
            Core.PenitenceManager.GetCurrentPenitence() is Framework.Penitences.PenitencePE02)
        {
            return true;
        }

        EntityStats stats = caster.Stats;
        Framework.FrameworkCore.Attributes.Logic.Attribute byType = stats.GetByType(__instance.statType);
        if (byType == null)
        {
            __result = false;
            return false;
        }

        float curVal = __instance.value;
        bool checkOnHit = __instance.effectType == ObjectEffect.EffectType.OnHitEnemy || __instance.effectType == ObjectEffect.EffectType.OnHitReceived;
        if (checkOnHit && __instance.UseHitAsBaseValue && CurrentHitField != null)
        {
            Hit hit = (Hit)CurrentHitField.GetValue(__instance);
            curVal += hit.DamageAmount;
        }

        if (__instance.valueType == ObjectEffect_Stat.ValueType.BasedOnCurrentStat ||
            __instance.valueType == ObjectEffect_Stat.ValueType.BasedOnMaxStat)
        {
            Framework.FrameworkCore.Attributes.Logic.VariableAttribute varAttr =
                stats.GetByType(__instance.statValueType) as Framework.FrameworkCore.Attributes.Logic.VariableAttribute;
            if (varAttr != null)
            {
                curVal += (__instance.valueType == ObjectEffect_Stat.ValueType.BasedOnCurrentStat)
                    ? varAttr.Current
                    : varAttr.CurrentMax;
            }
        }

        if (__instance.effectMode == ObjectEffect_Stat.EffectMode.Bonus)
        {
            var rawBonus = new Framework.FrameworkCore.Attributes.Logic.RawBonus(curVal, __instance.multiplier);
            BonusField?.SetValue(__instance, rawBonus);
            byType.AddRawBonus(rawBonus);
        }
        else
        {
            curVal *= __instance.multiplier;
            if (byType.IsVariable())
            {
                var varAttr2 = byType as Framework.FrameworkCore.Attributes.Logic.VariableAttribute;
                if (varAttr2 != null)
                {
                    varAttr2.Current += curVal;
                }
            }
        }

        CurrentValueField?.SetValue(__instance, curVal);
        __result = true;
        return false;
    }

    internal static Penitent ResolveEffectTarget(ObjectEffect instance)
    {
        if (instance != null && instance.effectType == ObjectEffect.EffectType.OnUse)
        {
            return PrayerCasterTracker.LastCaster;
        }
        if (Player2MenuView.IsInventoryP2View)
        {
            int activeIdx = Player2MenuView.ActivePlayerIndex;
            return CoopPlayerRegistry.GetPenitent(activeIdx) ?? CoopLocal.Player2;
        }
        return PrayerCasterTracker.LastCaster;
    }
}

[HarmonyPatch(typeof(ObjectEffect_Stat), "OnRemoveEffect")]
internal static class ObjectEffect_Stat_OnRemoveEffect_P2_Patch
{
    private static readonly FieldInfo CurrentValueField = AccessTools.Field(typeof(ObjectEffect_Stat), "currentValue");
    private static readonly FieldInfo BonusField = AccessTools.Field(typeof(ObjectEffect_Stat), "bonus");

    private static bool Prefix(ObjectEffect_Stat __instance)
    {
        Penitent caster = ObjectEffectCasterTracker.GetCaster(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        EntityStats stats = caster.Stats;
        Framework.FrameworkCore.Attributes.Logic.Attribute byType = stats.GetByType(__instance.statType);
        if (byType == null)
        {
            return false;
        }

        if (__instance.effectMode == ObjectEffect_Stat.EffectMode.Bonus)
        {
            var rawBonus = BonusField?.GetValue(__instance) as Framework.FrameworkCore.Attributes.Logic.RawBonus;
            if (rawBonus != null)
            {
                byType.RemoveRawBonus(rawBonus);
            }
        }
        else if (byType.IsVariable())
        {
            float curVal = CurrentValueField != null ? (float)CurrentValueField.GetValue(__instance) : 0f;
            ((Framework.FrameworkCore.Attributes.Logic.VariableAttribute)byType).Current -= curVal;
        }

        return false;
    }
}

// Ronda 89: Redirigir la estela visual (ItemGhostTrail) de Següiriya y otros rezos hacia el GhostTrailGenerator de P2
[HarmonyPatch(typeof(ItemGhostTrail), "OnApplyEffect")]
internal static class ItemGhostTrail_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo TrailField = AccessTools.Field(typeof(ItemGhostTrail), "trail");
    private static readonly FieldInfo PrevColorField = AccessTools.Field(typeof(ItemGhostTrail), "prevColor");
    private static readonly FieldInfo StoppedField = AccessTools.Field(typeof(ItemGhostTrail), "stopped");

    private static bool Prefix(ItemGhostTrail __instance, ref bool __result)
    {
        Penitent caster = ObjectEffect_Stat_OnApplyEffect_P2_Patch.ResolveEffectTarget(__instance);
        ObjectEffectCasterTracker.SetCaster(__instance, caster);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        var trail = caster.GetComponentInChildren<Gameplay.GameControllers.Effects.Player.GhostTrail.GhostTrailGenerator>();
        if (trail == null)
        {
            __result = false;
            return false;
        }

        TrailField?.SetValue(__instance, trail);
        PrevColorField?.SetValue(__instance, trail.TrailColor);
        StoppedField?.SetValue(__instance, false);
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ItemGhostTrail), "OnUpdate")]
internal static class ItemGhostTrail_OnUpdate_P2_Patch
{
    private static readonly FieldInfo TrailField = AccessTools.Field(typeof(ItemGhostTrail), "trail");
    private static readonly FieldInfo ColorField = AccessTools.Field(typeof(ItemGhostTrail), "color");
    private static readonly FieldInfo StoppedField = AccessTools.Field(typeof(ItemGhostTrail), "stopped");

    private static bool Prefix(ItemGhostTrail __instance)
    {
        Penitent caster = ObjectEffectCasterTracker.GetCaster(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        var trail = TrailField?.GetValue(__instance) as Gameplay.GameControllers.Effects.Player.GhostTrail.GhostTrailGenerator;
        bool stopped = StoppedField != null && (bool)StoppedField.GetValue(__instance);
        if (trail != null && !stopped && !caster.IsOnExecution && !trail.EnableGhostTrail)
        {
            trail.EnableGhostTrail = true;
            if (ColorField != null)
            {
                trail.TrailColor = (Color)ColorField.GetValue(__instance);
            }
        }
        return false;
    }
}

// Ronda 89: Redirigir ItemTemporalEffect (ataques rojos, nivel 2, invulnerabilidad, freno de fervor) hacia P2
[HarmonyPatch(typeof(ItemTemporalEffect), "OnApplyEffect")]
internal static class ItemTemporalEffect_OnApplyEffect_P2_Patch
{
    private static readonly FieldInfo PrevColorField = AccessTools.Field(typeof(ItemTemporalEffect), "previousColor");
    private static readonly FieldInfo PrevLevelField = AccessTools.Field(typeof(ItemTemporalEffect), "previousAttackLevel");

    private static bool Prefix(ItemTemporalEffect __instance, ref bool __result)
    {
        Penitent caster = ObjectEffect_Stat_OnApplyEffect_P2_Patch.ResolveEffectTarget(__instance);
        ObjectEffectCasterTracker.SetCaster(__instance, caster);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        var effects = __instance.Effects;
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.StopFervourRecolection))
        {
            caster.obtainsFervour = false;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.Invulnerable))
        {
            caster.Status.Invulnerable = true;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.Level2Attack))
        {
            PrevLevelField?.SetValue(__instance, caster.PenitentAttack.CurrentLevel);
            caster.PenitentAttack.CurrentLevel = 2;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.RedAttack))
        {
            PrevColorField?.SetValue(__instance, caster.PenitentAttack.AttackColor);
            caster.PenitentAttack.AttackColor = Gameplay.GameControllers.Penitent.Attack.PenitentSword.AttackColor.Red;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.StopGuiltDrop))
        {
            caster.GuiltDrop = false;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.DisableUnEquipSword))
        {
            caster.AllowEquipSwords = false;
        }
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ItemTemporalEffect), "OnRemoveEffect")]
internal static class ItemTemporalEffect_OnRemoveEffect_P2_Patch
{
    private static readonly FieldInfo PrevColorField = AccessTools.Field(typeof(ItemTemporalEffect), "previousColor");
    private static readonly FieldInfo PrevLevelField = AccessTools.Field(typeof(ItemTemporalEffect), "previousAttackLevel");

    private static bool Prefix(ItemTemporalEffect __instance)
    {
        Penitent caster = ObjectEffectCasterTracker.GetCaster(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        var effects = __instance.Effects;
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.StopFervourRecolection))
        {
            caster.obtainsFervour = true;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.Invulnerable))
        {
            caster.Status.Invulnerable = false;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.Level2Attack) && PrevLevelField != null)
        {
            caster.PenitentAttack.CurrentLevel = (int)PrevLevelField.GetValue(__instance);
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.RedAttack) && PrevColorField != null)
        {
            caster.PenitentAttack.AttackColor = (Gameplay.GameControllers.Penitent.Attack.PenitentSword.AttackColor)PrevColorField.GetValue(__instance);
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.StopGuiltDrop))
        {
            caster.GuiltDrop = true;
        }
        if (effects.Contains(ItemTemporalEffect.PenitentEffects.DisableUnEquipSword))
        {
            caster.AllowEquipSwords = true;
        }
        return false;
    }
}

// Ronda 89: Redirigir VFX de activación de rezos (PrayerUseVfx), cúpula protectora (ProtectionDomeEffect) y nube tóxica (ToxicCloudEffect) hacia P2
[HarmonyPatch(typeof(PrayerUseVfx), "InstantiateEffect")]
internal static class PrayerUseVfx_InstantiateEffect_P2_Patch
{
    private static bool Prefix(PrayerUseVfx __instance)
    {
        Penitent caster = ObjectEffect_Stat_OnApplyEffect_P2_Patch.ResolveEffectTarget(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }
        if (__instance.Vfx != null)
        {
            PoolManager.Instance.ReuseObject(__instance.Vfx, caster.transform.position, Quaternion.identity);
        }
        return false;
    }
}

[HarmonyPatch(typeof(ProtectionDomeEffect), "InstantiateProtectionDome")]
internal static class ProtectionDomeEffect_InstantiateProtectionDome_P2_Patch
{
    private static readonly FieldInfo DomeInstanceField = AccessTools.Field(typeof(ProtectionDomeEffect), "protectionDomeInstance");

    private static bool Prefix(ProtectionDomeEffect __instance)
    {
        Penitent caster = ObjectEffect_Stat_OnApplyEffect_P2_Patch.ResolveEffectTarget(__instance);
        ObjectEffectCasterTracker.SetCaster(__instance, caster);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }
        if (__instance.ProtectionDomePrefab != null)
        {
            Vector3 position = caster.transform.position + Vector3.up * 1.5f;
            GameObject dome = PoolManager.Instance.ReuseObject(__instance.ProtectionDomePrefab, position, Quaternion.identity).GameObject;
            DomeInstanceField?.SetValue(__instance, dome);
            if (dome != null && !string.IsNullOrEmpty(__instance.InstantiateProtectionDomeFx))
            {
                Core.Audio.PlaySfx(__instance.InstantiateProtectionDomeFx);
            }
        }
        return false;
    }
}

[HarmonyPatch(typeof(ProtectionDomeEffect), "OnUpdate")]
internal static class ProtectionDomeEffect_OnUpdate_P2_Patch
{
    private static readonly FieldInfo DomeInstanceField = AccessTools.Field(typeof(ProtectionDomeEffect), "protectionDomeInstance");
    private static readonly FieldInfo SetInvulnerableField = AccessTools.Field(typeof(ProtectionDomeEffect), "setInvulnerable");

    private static bool Prefix(ProtectionDomeEffect __instance)
    {
        Penitent caster = ObjectEffectCasterTracker.GetCaster(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        GameObject dome = DomeInstanceField?.GetValue(__instance) as GameObject;
        if (dome == null)
        {
            return false;
        }

        bool setInvulnerable = SetInvulnerableField != null && (bool)SetInvulnerableField.GetValue(__instance);
        if (!dome.activeInHierarchy)
        {
            if (setInvulnerable)
            {
                setInvulnerable = false;
                SetInvulnerableField?.SetValue(__instance, false);
                caster.Status.Invulnerable = false;
            }
        }
        else
        {
            setInvulnerable = Vector2.Distance(caster.GetPosition(), dome.transform.position) < __instance.MaxDistanceFromDome;
            SetInvulnerableField?.SetValue(__instance, setInvulnerable);
            caster.Status.Invulnerable = setInvulnerable;
        }
        return false;
    }
}

[HarmonyPatch(typeof(ToxicCloudEffect), "InstantiateToxicCloud")]
internal static class ToxicCloudEffect_InstantiateToxicCloud_P2_Patch
{
    private static bool Prefix(ToxicCloudEffect __instance)
    {
        Penitent caster = ObjectEffectCasterTracker.GetCaster(__instance);
        if (caster == null || !CoopLocal.IsGuestPlayer(caster, out _))
        {
            return true;
        }

        if (__instance.ToxicCloudPrefab != null)
        {
            Vector3 position = caster.transform.position + (Vector3)__instance.OffsetInstantiationPosition;
            PoolManager.ObjectInstance objectInstance = PoolManager.Instance.ReuseObject(__instance.ToxicCloudPrefab, position, Quaternion.identity);
            if (objectInstance != null)
            {
                var poison = objectInstance.GameObject.GetComponentInChildren<Gameplay.GameControllers.Environment.AreaEffects.PoisonAreaEffect>();
                if (poison != null)
                {
                    poison.DamageAmount = caster.Stats.PrayerStrengthMultiplier.Final * __instance.DamageAmount;
                }
                if (!string.IsNullOrEmpty(__instance.InstantiateToxicCloudFx))
                {
                    Core.Audio.PlaySfx(__instance.InstantiateToxicCloudFx);
                }
            }
        }
        return false;
    }
}

[HarmonyPatch(typeof(ZambraTearsHarvestPrayer), "InstantiateEffect")]
internal static class ZambraTearsHarvestPrayer_InstantiateEffect_P2_Patch
{
    private static readonly FieldInfo ZambraEffectField = AccessTools.Field(typeof(ZambraTearsHarvestPrayer), "zambraEffect");

    private static bool Prefix(ZambraTearsHarvestPrayer __instance)
    {
        Penitent owner = __instance.GetComponentInParent<Penitent>();
        if (owner == null || !CoopLocal.IsGuestPlayer(owner as Penitent, out _))
        {
            return true;
        }
        if (__instance.ZambraEffectPrefab != null)
        {
            Vector3 position = owner.GetPosition();
            GameObject go = PoolManager.Instance.ReuseObject(__instance.ZambraEffectPrefab, position, Quaternion.identity).GameObject;
            ZambraEffectField?.SetValue(__instance, go);
        }
        return false;
    }
}

#endregion
