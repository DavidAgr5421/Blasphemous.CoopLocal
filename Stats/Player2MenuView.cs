namespace Blasphemous.CoopLocal;

// Ronda 88: Control de vista de menú multi-jugador (0 = P1, 1 = P2, 2 = P3, 3 = P4).
// Mantiene compatibilidad total con SkillViewPlayer e InventoryViewPlayer mientras prepara
// la arquitectura para P3 y P4 y gestiona el indicador "[nombre jugador] menu".
internal static class Player2MenuView
{
    // 0 = P1 ("Pan-chan"), 1 = P2 ("Baby"), 2 = P3, 3 = P4
    internal static int SkillViewPlayer = 0;
    internal static int InventoryViewPlayer = 0;

    internal static int ActivePlayerIndex => InventoryViewPlayer != 0 ? InventoryViewPlayer : SkillViewPlayer;

    internal static int PendingOpenPlayer { get; set; } = -1;
    internal static bool PendingOpenAsP2
    {
        get => PendingOpenPlayer == 1;
        set => PendingOpenPlayer = value ? 1 : -1;
    }
    internal static int LastMenuToggleFrame { get; set; } = -1;

    internal static void RequestOpenAsPlayer(int playerIndex) => PendingOpenPlayer = playerIndex;
    internal static void RequestOpenAsP2() => RequestOpenAsPlayer(1);

    internal static int ConsumePendingOpenPlayer()
    {
        int value = PendingOpenPlayer;
        PendingOpenPlayer = -1;
        return value;
    }

    internal static bool ConsumePendingOpenAsP2()
    {
        int p = ConsumePendingOpenPlayer();
        return p > 0;
    }

    internal static void SetActivePlayer(int playerIndex)
    {
        SkillViewPlayer = playerIndex;
        InventoryViewPlayer = playerIndex;
    }

    internal static bool IsShowingAnyMenu =>
        Gameplay.UI.UIController.instance != null &&
        (Gameplay.UI.UIController.instance.IsShowingMenu || Gameplay.UI.UIController.instance.IsShowingInventory);

    internal static bool IsGuestView =>
        ActivePlayerIndex > 0 &&
        Gameplay.UI.UIController.instance != null &&
        Gameplay.UI.UIController.instance.IsShowingInventory;

    internal static bool IsSkillGuestView =>
        SkillViewPlayer > 0 &&
        Gameplay.UI.UIController.instance != null &&
        Gameplay.UI.UIController.instance.IsShowingInventory;

    internal static bool IsInventoryGuestView =>
        InventoryViewPlayer > 0 &&
        Gameplay.UI.UIController.instance != null &&
        Gameplay.UI.UIController.instance.IsShowingInventory;

    internal static bool IsSkillP2View => IsSkillGuestView;
    internal static bool IsInventoryP2View => IsInventoryGuestView;

    internal static string CurrentMenuTitle =>
        CoopPlayerRegistry.GetMenuTitle(ActivePlayerIndex);

    internal static string CurrentBracketTag =>
        CoopPlayerRegistry.GetSlot(ActivePlayerIndex).BracketTag;
}
