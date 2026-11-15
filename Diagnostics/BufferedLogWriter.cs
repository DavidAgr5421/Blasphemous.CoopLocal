using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Escritor de log con búfer: Enqueue() solo añade a una lista en memoria (barato, seguro desde cualquier hilo) y un hilo
/// de fondo vuelca los pendientes a disco por lotes. Sustituye al esquema anterior de "abrir/escribir/cerrar el archivo +
/// File.Exists + FileInfo en CADA línea, en el hilo principal". La rotación (archivo actual -> *_prev) se decide con el
/// tamaño acumulado en memoria, sin consultar el disco por línea. Sin dependencias de Unity (se prueba con xUnit).
/// </summary>
internal sealed class BufferedLogWriter
{
    private readonly Func<string> pathProvider;
    private readonly long maxBytes;
    private readonly int maxPending;
    private readonly object gate = new object();
    private readonly object writeGate = new object();
    private List<string> pending = new List<string>();
    private long currentBytes = -1; // -1 = aún no medido
    private int dropped;
    private Thread thread;
    private volatile bool stopping;

    internal int Dropped { get { lock (gate) return dropped; } }
    internal int PendingCount { get { lock (gate) return pending.Count; } }

    /// <param name="pathProvider">Ruta del log actual; puede devolver null mientras aún no se conozca (los pendientes esperan).</param>
    internal BufferedLogWriter(Func<string> pathProvider, long maxBytes, int maxPending = 20000)
    {
        this.pathProvider = pathProvider;
        this.maxBytes = maxBytes;
        this.maxPending = maxPending;
    }

    /// <summary>Añade una línea ya formateada (con marca de tiempo). Si el búfer está lleno se descarta y se cuenta.</summary>
    internal void Enqueue(string line)
    {
        lock (gate)
        {
            if (pending.Count >= maxPending) { dropped++; return; }
            pending.Add(line);
        }
    }

    /// <summary>Arranca el hilo de fondo que vuelca cada <paramref name="intervalMs"/> ms. Idempotente.</summary>
    internal void Start(int intervalMs)
    {
        lock (gate)
        {
            if (thread != null) return;
            stopping = false;
            thread = new Thread(() =>
            {
                while (!stopping)
                {
                    Thread.Sleep(intervalMs);
                    try { Flush(); } catch { /* el log nunca debe tumbar el juego */ }
                }
            });
            thread.IsBackground = true;
            thread.Name = "CoopLocal.LogWriter";
            thread.Start();
        }
    }

    /// <summary>Detiene el hilo y vuelca lo pendiente.</summary>
    internal void Stop()
    {
        stopping = true;
        try { Flush(); } catch { }
    }

    /// <summary>Vuelca ahora todo lo pendiente (síncrono). Devuelve las líneas escritas.</summary>
    internal int Flush()
    {
        lock (writeGate)
        {
            List<string> batch;
            int droppedNow;
            lock (gate)
            {
                if (pending.Count == 0 && dropped == 0) return 0;
                string probe = pathProvider();
                if (probe == null) return 0; // aún sin ruta: conservar pendientes
                batch = pending;
                pending = new List<string>(Math.Min(batch.Count + 16, maxPending));
                droppedNow = dropped;
                dropped = 0;
            }

            string path = pathProvider();
            if (path == null) { Requeue(batch, droppedNow); return 0; }

            var sb = new StringBuilder(batch.Count * 96);
            if (droppedNow > 0) sb.Append("[LogWriter] ").Append(droppedNow).Append(" líneas descartadas (búfer lleno)").Append(Environment.NewLine);
            foreach (string l in batch) sb.Append(l).Append(Environment.NewLine);
            string text = sb.ToString();

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (currentBytes < 0) currentBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                long incoming = Encoding.UTF8.GetByteCount(text);
                if (currentBytes > 0 && currentBytes + incoming > maxBytes)
                {
                    string prev = PrevPath(path);
                    if (File.Exists(prev)) File.Delete(prev);
                    if (File.Exists(path)) File.Move(path, prev);
                    currentBytes = 0;
                }
                File.AppendAllText(path, text);
                currentBytes += incoming;
            }
            catch
            {
                Requeue(batch, droppedNow); // disco bloqueado: reintentar en el próximo vuelco
                return 0;
            }
            return batch.Count;
        }
    }

    private void Requeue(List<string> batch, int droppedNow)
    {
        lock (gate)
        {
            batch.AddRange(pending);
            pending = batch.Count > maxPending ? batch.GetRange(batch.Count - maxPending, maxPending) : batch;
            dropped += droppedNow;
        }
    }

    internal static string PrevPath(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_prev" + Path.GetExtension(path));
    }

    /// <summary>Olvida el tamaño medido (p. ej. tras rotar el archivo desde fuera).</summary>
    internal void ResetSizeCache() { lock (writeGate) currentBytes = -1; }
}
