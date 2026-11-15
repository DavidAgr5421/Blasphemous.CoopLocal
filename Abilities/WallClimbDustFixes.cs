using System.Collections.Generic;
using System.Reflection;
using Framework.Managers;
using Gameplay.GameControllers.AnimationBehaviours.Effects.Player;
using Gameplay.GameControllers.Effects.Player.Dust;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Polvo de la pared (WallClimbDust) para los jugadores invitados.
//
// Vanilla (decompilado):
//  - WallJumpContactBehaviour.OnStateEnter llama a WallClimbDust.TriggerDust() en CADA entrada al estado
//    WallClimbContact y TriggerDust no tiene enfriamiento: cada llamada reproduce la animación y
//    instancia/reutiliza un sprite de polvo. El reintento de Play(WallClimbContact) de los invitados (cada frame
//    mientras STICK_ON_WALL no se activa) provocaba una ráfaga de polvos.
//  - WallClimbDustBehaviour (en el prefab de polvo) cachea `Core.Logic.Penitent` (P1) como dueño: al terminar
//    el estado, el polvo de un invitado se devuelve al pool de P1, y el pool del invitado nunca se rellena
//    (se instancia uno nuevo cada vez).
// Arreglo: (1) enfriamiento por instancia en TriggerDust; (2) el dueño del polvo se resuelve por el Penitente más
// cercano al sprite (el polvo se crea en la posición de su dueño), de modo que el pool es el correcto.
[HarmonyPatch(typeof(WallClimbDust), "TriggerDust")]
internal static class WallClimbDust_TriggerDust_Cooldown_Patch
{
    private const float MinInterval = 0.40f;
    private static readonly Dictionary<int, float> lastTrigger = new Dictionary<int, float>();

    private static bool Prefix(WallClimbDust __instance)
    {
        int id = __instance.GetInstanceID();
        float now = Time.time;
        if (lastTrigger.TryGetValue(id, out float last) && now - last < MinInterval) return false;
        lastTrigger[id] = now;
        return true;
    }
}

[HarmonyPatch(typeof(WallClimbDustBehaviour), "OnStateEnter")]
internal static class WallClimbDustBehaviour_OnStateEnter_Owner_Patch
{
    private static readonly FieldInfo PenitentField = Reflect.Field(typeof(WallClimbDustBehaviour), "_penitent");
    private static readonly FieldInfo DustField = Reflect.Field(typeof(WallClimbDustBehaviour), "_wallClimbDust");

    private static void Prefix(WallClimbDustBehaviour __instance, Animator animator)
    {
        try
        {
            if (animator == null || PenitentField == null || DustField == null) return;
            Penitent best = null;
            float bestSqr = float.MaxValue;
            foreach (Penitent p in CoopPlayerRegistry.GetActivePlayers())
            {
                if (p == null) continue;
                float d = (p.transform.position - animator.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            if (best == null) return;
            WallClimbDust dust = best.GetComponentInChildren<WallClimbDust>();
            if (dust == null) return;
            // Se fija en cada entrada (el objeto de polvo vuelve a usarse desde el pool y puede cambiar de dueño).
            PenitentField.SetValue(__instance, best);
            DustField.SetValue(__instance, dust);
        }
        catch (System.Exception ex)
        {
            DashParryDebugLog.Log("[WallDust] Error resolviendo dueño del polvo: " + ex.Message);
        }
    }
}
