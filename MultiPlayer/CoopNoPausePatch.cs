using HarmonyLib;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// En partida online ningún menú (pausa, inventario, mapa, corazones...) detiene el juego: pausar el tiempo en una PC
/// congelaría a los jugadores de las demás. Cada menú queda local a su jugador (ver Player2MenuView.MenuBlocksSlot).
/// </summary>
[HarmonyPatch(typeof(Framework.Managers.LogicManager), nameof(Framework.Managers.LogicManager.PauseGame))]
internal static class LogicManager_PauseGame_Online_Patch
{
    private static bool Prefix()
    {
        return !CoopNetworkManager.IsOnlineActive;
    }
}
