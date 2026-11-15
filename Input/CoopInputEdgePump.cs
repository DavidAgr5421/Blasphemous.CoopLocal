using UnityEngine;

namespace Blasphemous.CoopLocal;

// Evalua una vez por fotograma (aunque nadie lo consulte) los flancos por ranura, para que "pulsacion nueva" signifique
// siempre "no estaba pulsado el fotograma anterior" y no "no se consulto antes" (entrar en rango con Arriba ya pulsado).
public class CoopInputEdgePump : MonoBehaviour
{
    private static CoopInputEdgePump _instance;

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("CoopInputEdgePump");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopInputEdgePump>();
    }

    private void Update()
    {
        Player2Input.HostMenuHotkey();
        if (!CoopGameConfig.IsCoopEnabled) return;
        for (int slot = 0; slot < 4; slot++) Player2Input.GetUpDown(slot);
    }
}
