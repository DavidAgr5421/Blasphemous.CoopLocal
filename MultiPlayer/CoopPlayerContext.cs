using System.Collections.Generic;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Todo lo que el mod sabe de UN jugador (P1..P4) tras un único punto de acceso: identidad, penitente, stats, datos
/// persistentes, equipo y HUD. Pensado para sustituir el patrón "Core.Logic.Penitent por defecto / Player2 hardcodeado":
/// el código recibe (o resuelve) un contexto y pregunta al contexto, sin ramas "si es P1 ... si no ...".
///
/// Es una vista ligera sin estado propio: siempre consulta el estado actual (el penitente puede aparecer/desaparecer entre salas).
/// Uso:  CoopPlayers.Of(penitent)?.HasSword("HE06")   |   foreach (var c in CoopPlayers.Active) ...
/// </summary>
internal sealed class CoopPlayerContext
{
    public int Index { get; }

    internal CoopPlayerContext(int index) { Index = index; }

    // ---- Identidad ----
    public bool IsHost => Index == 0;
    public bool IsGuest => Index > 0;
    public CoopPlayerSlot Slot => CoopPlayerRegistry.GetSlot(Index);
    public string DisplayName => CoopPlayerRegistry.GetDisplayName(Index);
    public Color32 LabelColor => CoopPlayerRegistry.GetLabelColor(Index);
    public string Tag => $"P{Index + 1}";

    // ---- Estado en escena ----
    public Penitent Penitent => CoopPlayerRegistry.GetPenitent(Index);
    public bool IsPresent => Penitent != null;
    public bool IsAlive { get { Penitent p = Penitent; return p != null && p.Status != null && !p.Status.Dead; } }
    public Gameplay.GameControllers.Entities.EntityStats Stats { get { Penitent p = Penitent; return p != null ? p.Stats : null; } }

    /// <summary>Posición en el mundo (2D) del penitente; Vector2.zero si no está en escena.</summary>
    public Vector2 Position { get { Penitent p = Penitent; return p != null ? (Vector2)p.transform.position : Vector2.zero; } }

    /// <summary>Resto de jugadores presentes en escena (sin incluir a este).</summary>
    public System.Collections.Generic.IEnumerable<CoopPlayerContext> Others
    {
        get { foreach (var c in CoopPlayers.All) if (c.Index != Index && c.IsPresent) yield return c; }
    }

    // ---- Datos persistentes (nombre, color, habilidades, inventario propio de invitados) ----
    public PlayerCoopData Data => CoopSaveManager.CurrentSave != null ? CoopSaveManager.CurrentSave.GetPlayer(Index) : null;

    // ---- HUD (slots de P2-P4; P1 es el HUD original del juego y no tiene slot propio) ----
    public HealthBarSlotInfo HealthBar => IsGuest ? CoopHealthBars.GetSlot(Index) : null;
    public FervourBarSlotInfo FervourBar => IsGuest ? CoopFervourBars.GetSlot(Index) : null;
    public PurgeSlotWidget PurgeWidget => CoopPurgeHUD.GetSlot(Index);

    // ---- HUD: elementos, layout guardado y ciclo de vida (un único sitio en vez de ramas por jugador) ----
    public enum HudElement { Health = 0, Fervour = 1, HealthDeco = 2, Coins = 3 }

    /// <summary>Posición/escala/ancla de un elemento según el modo de HUD (2 jugadores = layout clásico; 3-4 = esquinas).</summary>
    public struct HudPlacement
    {
        public Vector2 Anchor;
        public Vector2 Position;
        public float Scale;
    }

    /// <summary>Entrada del layout guardado para este jugador/elemento (en modo 2P, P2 usa las entradas *_2p).</summary>
    public HudElementPosition HudConfig(HudElement element, bool multiLayout)
    {
        var cfg = HudLayoutConfig.Current;
        if (Index == 1 && !multiLayout)
        {
            switch (element)
            {
                case HudElement.Health: return cfg.p2_health_2p;
                case HudElement.Fervour: return cfg.p2_fervour_2p;
                case HudElement.Coins: return cfg.p2_coins_2p;
            }
        }
        switch (element)
        {
            case HudElement.Health: return cfg.GetHealthConfig(Index);
            case HudElement.Fervour: return cfg.GetFervourConfig(Index);
            case HudElement.HealthDeco: return cfg.GetHealthDecoConfig(Index);
            default: return cfg.GetCoinsConfig(Index);
        }
    }

    /// <summary>Esquina de anclaje de las barras de P2-P4 (P2 en 2P: abajo-derecha; P3: arriba-izquierda; P2/P4 en 3-4P: arriba-derecha).</summary>
    public Vector2 HudAnchor(bool is3Plus)
    {
        if (Index == 1 && !is3Plus) return new Vector2(1f, 0f);
        return Index == 2 ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
    }

    /// <summary>Colocación inicial de la barra de vida o fervor de un invitado.</summary>
    public HudPlacement GetHudPlacement(HudElement element, bool is3Plus)
    {
        var placement = new HudPlacement { Anchor = HudAnchor(is3Plus) };
        var cfg = HudLayoutConfig.Current;
        HudElementPosition health = HudConfig(HudElement.Health, is3Plus);

        if (element == HudElement.Fervour)
        {
            HudElementPosition fervour = HudConfig(HudElement.Fervour, is3Plus);
            bool twoPlayerClassic = Index == 1 && !is3Plus; // el layout clásico de 2P usa siempre su entrada propia
            if (twoPlayerClassic || (cfg.isCustomSaved && fervour != null))
            {
                placement.Scale = fervour.scale;
                placement.Position = fervour.Position;
            }
            else
            {
                // Sin layout guardado: fervor pegado a la barra de vida
                placement.Scale = health.scale;
                placement.Position = health.Position + new Vector2(44f * health.scale, 27f * health.scale);
            }
            return placement;
        }

        placement.Scale = health.scale;
        placement.Position = health.Position;
        return placement;
    }

    /// <summary>RectTransform vivo de un elemento del HUD de este jugador (null si no existe en escena).</summary>
    public RectTransform HudRect(HudElement element, bool multiLayout)
    {
        switch (element)
        {
            case HudElement.Health: return IsHost ? CoopHealthBars.P1HealthRect : HealthBar?.CloneRect;
            case HudElement.Fervour: return IsHost ? CoopFervourBars.P1FervourRect : FervourBar?.CloneRect;
            case HudElement.HealthDeco: return CoopHealthBars.GetDecorationRect(Index);
            default:
                if (Index == 1 && !multiLayout) return Player2PurgePoints.CloneRect;
                return CoopPurgeHUD.GetSlotRect(Index);
        }
    }

    /// <summary>
    /// Guarda posición/escala de un elemento en el layout y, si el elemento es un clon vivo, la aplica al instante.
    /// (P1 salvo monedas y las decoraciones solo se guardan: el HUD original de P1 lo mueve el editor directamente.)
    /// </summary>
    public void ApplyHudTransform(HudElement element, Vector2 pos, float scale, bool multiLayout)
    {
        HudElementPosition entry = HudConfig(element, multiLayout);
        if (entry != null) { entry.x = pos.x; entry.y = pos.y; entry.scale = scale; }

        switch (element)
        {
            case HudElement.Health:
                if (IsGuest && HealthBar != null)
                {
                    HealthBar.AnchoredPosition = pos; HealthBar.Scale = scale;
                    if (HealthBar.CloneRect != null) { HealthBar.CloneRect.anchoredPosition = pos; HealthBar.CloneRect.localScale = Vector3.one * scale; }
                }
                break;
            case HudElement.Fervour:
                if (IsGuest && FervourBar != null)
                {
                    FervourBar.AnchoredPosition = pos; FervourBar.Scale = scale;
                    if (FervourBar.CloneRect != null) { FervourBar.CloneRect.anchoredPosition = pos; FervourBar.CloneRect.localScale = Vector3.one * scale; }
                }
                break;
            case HudElement.Coins:
                if (Index == 1 && !multiLayout)
                {
                    Player2PurgePoints.AnchoredPosition = pos;
                    Player2PurgePoints.TextScale = scale;
                }
                else
                {
                    PurgeSlotWidget w = PurgeWidget;
                    if (w != null)
                    {
                        w.AnchoredPosition = pos; w.Scale = scale;
                        if (w.RootRect != null) { w.RootRect.anchoredPosition = pos; w.RootRect.localScale = Vector3.one * scale; }
                    }
                }
                break;
        }
    }

    /// <summary>Crea las barras de vida y fervor de este invitado (P2-P4).</summary>
    public void EnsureGuestBars()
    {
        Penitent p = Penitent;
        if (!IsGuest || p == null) return;
        CoopHealthBars.EnsureCreated(Index, p);
        CoopFervourBars.EnsureCreated(Index, p);
    }

    /// <summary>Destruye todo el HUD propio de este invitado (vida, fervor y monedas).</summary>
    public void DestroyHud()
    {
        if (!IsGuest) return;
        CoopHealthBars.DestroySlot(Index);
        CoopFervourBars.DestroySlot(Index);
        CoopPurgeHUD.DestroySlot(Index);
    }

    // ---- Equipo: P1 = inventario vanilla; P2-P4 = inventario propio del mod. Misma pregunta para todos. ----
    public string EquippedSwordId
    {
        get
        {
            if (IsGuest) return Data?.EquippedSword;
            var s = Core.InventoryManager != null ? Core.InventoryManager.GetSwordInSlot(0) : null;
            return s != null ? s.id : null;
        }
    }

    public bool HasSword(string id) =>
        !string.IsNullOrEmpty(id) && string.Equals(EquippedSwordId, id, System.StringComparison.OrdinalIgnoreCase);

    public bool HasBead(string id) => IsGuest ? Player2InventoryManager.IsBeadEquipped(id, Index) : Core.InventoryManager.IsRosaryBeadEquipped(id);
    public bool HasPrayer(string id) => IsGuest ? Player2InventoryManager.IsPrayerEquipped(id, Index) : Core.InventoryManager.IsPrayerEquipped(id);
    public bool HasRelic(string id) => IsGuest ? Player2InventoryManager.IsRelicEquipped(id, Index) : Core.InventoryManager.IsRelicEquipped(id);

    public override string ToString() => $"{Tag} ({DisplayName})";
}

/// <summary>Acceso a los contextos de jugador.</summary>
internal static class CoopPlayers
{
    private static readonly CoopPlayerContext[] all = CreateAll();

    private static CoopPlayerContext[] CreateAll()
    {
        var a = new CoopPlayerContext[CoopPlayerRegistry.MaxPlayers];
        for (int i = 0; i < a.Length; i++) a[i] = new CoopPlayerContext(i);
        return a;
    }

    /// <summary>Contexto del jugador por índice (0..3); null fuera de rango.</summary>
    internal static CoopPlayerContext Get(int index) => index >= 0 && index < all.Length ? all[index] : null;

    /// <summary>Contexto de un penitente, o null si no pertenece a ningún jugador conocido.</summary>
    internal static CoopPlayerContext Of(Penitent penitent)
    {
        int idx = CoopPlayerRegistry.GetPlayerIndex(penitent);
        return idx >= 0 ? Get(idx) : null;
    }

    /// <summary>Los 4 contextos (estén o no en escena).</summary>
    internal static CoopPlayerContext[] All => all;

    /// <summary>Solo los jugadores con penitente en escena.</summary>
    internal static IEnumerable<CoopPlayerContext> Active
    {
        get { foreach (var c in all) if (c.IsPresent) yield return c; }
    }

    /// <summary>Contexto de P1 (anfitrión).</summary>
    internal static CoopPlayerContext Host => all[0];

    /// <summary>Contexto del jugador al que pertenece cualquier componente/entidad (busca el Penitent en su jerarquía), o null.</summary>
    internal static CoopPlayerContext Of(Component component)
    {
        if (component == null) return null;
        return Of(component as Penitent ?? component.GetComponentInParent<Penitent>());
    }

    /// <summary>Número de jugadores presentes en escena (sin asignar memoria).</summary>
    internal static int PresentCount
    {
        get { int n = 0; for (int i = 0; i < all.Length; i++) if (all[i].IsPresent) n++; return n; }
    }

    /// <summary>Jugador presente más cercano a un punto (null si no hay). Con <paramref name="aliveOnly"/> ignora a los muertos.</summary>
    internal static CoopPlayerContext Nearest(Vector2 point, bool aliveOnly = true)
    {
        CoopPlayerContext best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < all.Length; i++)
        {
            CoopPlayerContext c = all[i];
            if (!c.IsPresent || (aliveOnly && !c.IsAlive)) continue;
            float d = (c.Position - point).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = c; }
        }
        return best;
    }

    /// <summary>Solo los invitados (P2-P4) presentes en escena.</summary>
    internal static IEnumerable<CoopPlayerContext> ActiveGuests
    {
        get { foreach (var c in all) if (c.IsGuest && c.IsPresent) yield return c; }
    }
}
