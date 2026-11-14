using System;
using System.Collections.Generic;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Registro de excepciones "tragadas" (catch intencionalmente silenciosos). Antes eran `catch { }` mudos que ocultaban
/// fallos reales; ahora cada sitio se registra, limitado por frecuencia: se escribe la 1ª vez y luego en la 10ª, 100ª, 1000ª...
/// de cada ubicación, de modo que un catch en un bucle por fotograma no inunda el log pero tampoco pasa desapercibido.
/// Nunca lanza excepciones.
/// </summary>
internal static class SwallowedExceptions
{
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    private static readonly object gate = new object();
    private static bool reentrant;

    /// <param name="where">Ubicación del catch ("Archivo.cs:123"), usada como clave de limitación.</param>
    internal static void Report(Exception ex, string where)
    {
        try
        {
            int n;
            lock (gate)
            {
                if (reentrant) return; // el propio logger falló: no recursar
                counts.TryGetValue(where, out n);
                n++;
                counts[where] = n;
                if (!ShouldLog(n)) return;
                reentrant = true;
            }
            try
            {
                string msg = $"[Swallowed] {where} x{n}: {ex?.GetType().Name}: {ex?.Message}";
                DashParryDebugLog.Log(msg);
            }
            finally
            {
                lock (gate) { reentrant = false; }
            }
        }
        catch
        {
            // Último recurso: el registro de errores no puede ser fuente de errores.
        }
    }

    /// <summary>true en n = 1, 10, 100, 1000... (potencias de 10).</summary>
    internal static bool ShouldLog(int n)
    {
        if (n < 1) return false;
        while (n % 10 == 0) n /= 10;
        return n == 1;
    }

    /// <summary>Resumen (ubicación -> veces) para diagnóstico/depuración.</summary>
    internal static KeyValuePair<string, int>[] Snapshot()
    {
        lock (gate)
        {
            var arr = new KeyValuePair<string, int>[counts.Count];
            int i = 0;
            foreach (var kv in counts) arr[i++] = kv;
            return arr;
        }
    }
}
