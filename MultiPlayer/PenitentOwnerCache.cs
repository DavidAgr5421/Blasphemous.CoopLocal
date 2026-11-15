using System.Collections.Generic;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Los parches de StateMachineBehaviour/Animator se ejecutan cada fotograma por jugador y llamaban a
// PenitentOwnerCache.Of(animator) (recorre la jerarquia) en cada uno. La relacion animador -> Penitent no cambia
// durante la vida del animador, asi que se cachea por InstanceID. Un resultado "null" (animador de otra cosa) tambien se
// cachea; un Penitent destruido se detecta con la comprobacion de Unity y se recalcula.
internal static class PenitentOwnerCache
{
    private const int PruneThreshold = 128;
    private static readonly Dictionary<int, Penitent> cache = new Dictionary<int, Penitent>();
    private static readonly List<int> pruneBuffer = new List<int>();

    internal static Penitent Of(Animator animator)
    {
        if (animator == null) return null;
        int id = animator.GetInstanceID();
        Penitent cached;
        if (cache.TryGetValue(id, out cached))
        {
            if (ReferenceEquals(cached, null)) return null;   // negativo cacheado
            if (cached != null) return cached;                 // vivo
        }
        Penitent found = PenitentOwnerCache.Of(animator);
        if (cache.Count >= PruneThreshold) Prune();
        cache[id] = found;
        return found;
    }

    private static void Prune()
    {
        pruneBuffer.Clear();
        foreach (var kv in cache)
        {
            // Quita los Penitent destruidos (null de Unity pero no null real).
            if (!ReferenceEquals(kv.Value, null) && kv.Value == null) pruneBuffer.Add(kv.Key);
        }
        for (int i = 0; i < pruneBuffer.Count; i++) cache.Remove(pruneBuffer[i]);
        if (cache.Count >= PruneThreshold) cache.Clear();
    }
}
