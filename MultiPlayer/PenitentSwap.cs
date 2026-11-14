using Framework.Managers;
using Gameplay.GameControllers.Penitent;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Cambio temporal de Core.Logic.Penitent (el singleton de "el jugador" de vanilla) al penitente real que actúa.
/// Vanilla hardcodea ese singleton (siempre P1) en muchísimos métodos; la única forma de ejecutarlos para P2-P4 sin
/// reescribirlos es cambiarlo durante la llamada. Centralizado aquí para que TODOS los parches lo hagan igual:
///   Prefix:    __state = PenitentSwap.Begin(owner);
///   Finalizer: PenitentSwap.End(__state);      // Finalizer (no Postfix): se restaura aunque vanilla lance una excepción.
/// Si el restaurado se omitiera, P1 quedaría convertido en otro jugador para el resto de la sesión.
/// </summary>
internal static class PenitentSwap
{
    /// <summary>Cambia el singleton a <paramref name="owner"/>. Devuelve el penitente a restaurar, o null si no hubo cambio.</summary>
    internal static Penitent Begin(Penitent owner)
    {
        if (owner == null || Core.Logic == null) return null;
        Penitent previous = Core.Logic.Penitent;
        if (previous == owner) return null;
        Core.Logic.Penitent = owner;
        return previous;
    }

    /// <summary>Restaura el singleton con el valor devuelto por Begin (null = no había cambio que deshacer).</summary>
    internal static void End(Penitent previous)
    {
        if (previous != null && Core.Logic != null) Core.Logic.Penitent = previous;
    }
}
