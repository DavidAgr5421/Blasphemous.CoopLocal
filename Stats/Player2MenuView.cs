namespace Blasphemous.CoopLocal;

// Toggle explícito P1/P2 dentro del mismo menú (no inferir de dispositivo).
// Usado por Fase1 SkillTree y Fase2 Inventory Grid.
internal static class Player2MenuView
{
    // 0 = P1 (vanilla), 1 = P2
    internal static int SkillViewPlayer = 0;
    internal static int InventoryViewPlayer = 0; // para Grid (Prayers/Beads/Swords)

    internal static bool PendingOpenAsP2 { get; set; }

    internal static void RequestOpenAsP2() => PendingOpenAsP2 = true;

    internal static bool ConsumePendingOpenAsP2()
    {
        bool value = PendingOpenAsP2;
        PendingOpenAsP2 = false;
        return value;
    }

    internal static bool IsSkillP2View => SkillViewPlayer == 1;
    internal static bool IsInventoryP2View => InventoryViewPlayer == 1;
}
