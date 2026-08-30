using Gameplay.UI.Others.MenuLogic;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

// Patch central sobre NewInventoryWidget.Show(bool p_active) para fijar la vista
// P1/P2 ANTES de que Show() renderice el primer frame. Esto evita un "flash" de datos
// de P1 cuando P2 abre el menú por primera vez.
[HarmonyPatch(typeof(NewInventoryWidget), nameof(NewInventoryWidget.Show))]
internal static class NewInventoryWidget_Show_P2View_Patch
{
    private static void Prefix(bool p_active)
    {
        if (p_active)
        {
            bool asP2 = Player2MenuView.ConsumePendingOpenAsP2();
            Player2MenuView.SkillViewPlayer = asP2 ? 1 : 0;
            Player2MenuView.InventoryViewPlayer = asP2 ? 1 : 0;
        }
        else
        {
            // Cierre - Show(false) es el único punto de cierre (no hay evento OnClose/OnDisable
            // en el juego real, confirmado por decompile). Resetear a P1 para que una apertura
            // posterior de P1 no herede una vista de P2 que quedó pegada.
            Player2MenuView.SkillViewPlayer = 0;
            Player2MenuView.InventoryViewPlayer = 0;
        }
    }
}