using System.Collections.Generic;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Red de seguridad contra Penitents "huerfanos": instancias vivas de Penitent que no son P1 ni un invitado registrado
// (p. ej. el clon de P4 al revivir en el Prie-Dieu). Un huerfano no tiene ranura, asi que el input vanilla de P1 lo mueve.
// Cada ~1 s cuenta los Penitent de la escena; si uno sigue sin pertenecer a nadie en 3 comprobaciones seguidas, avisa y lo
// destruye (las 3 pasadas evitan tocar uno legitimo que todavia se esta registrando). Coste: un FindObjectsOfType por segundo.
public class PenitentOrphanGuard : MonoBehaviour
{
    private const float CheckInterval = 1f;
    private const int StrikesToDestroy = 3;

    private static PenitentOrphanGuard _instance;
    private readonly Dictionary<int, int> strikes = new Dictionary<int, int>();
    private readonly List<int> staleKeys = new List<int>();
    private float timer;

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("PenitentOrphanGuard");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PenitentOrphanGuard>();
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < CheckInterval) return;
        timer = 0f;

        if (!CoopGameConfig.IsCoopEnabled || Core.Logic == null || Core.Logic.Penitent == null)
        {
            strikes.Clear();
            return;
        }

        Penitent p1 = Core.Logic.Penitent;
        Penitent[] all = FindObjectsOfType<Penitent>();
        staleKeys.Clear();
        foreach (int key in strikes.Keys) staleKeys.Add(key);

        for (int i = 0; i < all.Length; i++)
        {
            Penitent p = all[i];
            if (p == null || p == p1 || !p.gameObject.scene.IsValid()) continue;
            if (CoopLocal.IsGuestPlayer(p, out _)) continue;

            int id = p.GetInstanceID();
            staleKeys.Remove(id);
            int count;
            strikes.TryGetValue(id, out count);
            count++;
            strikes[id] = count;

            if (count == 1)
            {
                DashParryDebugLog.Log($"[OrphanGuard] Penitent no registrado detectado: '{p.name}' id={id} pos={p.transform.position}");
            }
            if (count >= StrikesToDestroy)
            {
                DashParryDebugLog.Log($"[OrphanGuard] Destruyendo Penitent huerfano '{p.name}' id={id}");
                strikes.Remove(id);
                Destroy(p.gameObject);
            }
        }

        // Los que ya no existen o ya se registraron dejan de contar.
        for (int i = 0; i < staleKeys.Count; i++) strikes.Remove(staleKeys[i]);
    }
}
