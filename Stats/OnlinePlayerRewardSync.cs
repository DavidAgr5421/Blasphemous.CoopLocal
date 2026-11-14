using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Sincroniza recompensas (lágrimas de enmienda / Purge) con jugadores online no conectados.
/// Los jugadores online en la partida que aún no se hayan conectado físicamente al mundo
/// obtienen el 60% del dinero de las bajas enemigas que consiga el equipo activo.
/// </summary>
[HarmonyPatch(typeof(Penitent), "GetPurge")]
internal static class Penitent_GetPurge_OnlinePlayerSync_Patch
{
    private static void Prefix(Penitent __instance, out float __state)
    {
        __state = (__instance != null && __instance.Stats != null && __instance.Stats.Purge != null)
            ? __instance.Stats.Purge.Current
            : -1f;
    }

    private static void Postfix(Penitent __instance, float __state)
    {
        if (__state < 0f || __instance == null || __instance.Stats == null || __instance.Stats.Purge == null) return;

        // Solo procesar una vez por baja: priorizar a P1; si P1 no existe o está muerto, procesar con el primer invitado vivo
        bool isPrimaryReceiver = (Core.Logic != null && __instance == Core.Logic.Penitent);
        if (!isPrimaryReceiver)
        {
            bool p1Dead = (Core.Logic?.Penitent == null || Core.Logic.Penitent.Status.Dead);
            if (p1Dead && __instance == CoopLocal.Player2)
            {
                isPrimaryReceiver = true;
            }
        }

        if (!isPrimaryReceiver) return;

        float gained = __instance.Stats.Purge.Current - __state;
        if (gained <= 0f) return;

        // Otorgar el 60% del monto real a cualquier jugador online de la partida que no esté conectado
        float onlineShare = gained * 0.60f;
        int activeCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 1, 4);
        int autoSlot = PersistentManager.GetAutomaticSlot();

        for (int i = 1; i < activeCount; i++)
        {
            if (CoopGameConfig.Slots[i].Device == CoopDeviceType.RemoteSteam && !CoopGameConfig.Slots[i].IsOnlineConnected)
            {
                if (autoSlot >= 0)
                {
                    var pData = CoopSaveManager.GetSlotData(autoSlot).GetPlayer(i);
                    if (pData != null)
                    {
                        pData.CurrentPurge = Mathf.Clamp(pData.CurrentPurge + onlineShare, 0f, 9999999f);
                        if (Main.CoopLocal != null)
                        {
                            Blasphemous.ModdingAPI.ModLog.Info($"[CoopReward] Jugador Online P{i + 1} ({pData.Name}) ausente recibió 60% de lágrimas por baja (+{onlineShare:F0}). Saldo: {pData.CurrentPurge:F0}", Main.CoopLocal);
                        }
                    }
                }
            }
        }
    }
}
