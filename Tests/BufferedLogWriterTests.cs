using System;
using System.IO;
using System.Threading;
using Xunit;

namespace Blasphemous.CoopLocal.Tests;

public class BufferedLogWriterTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "coop_log_tests_" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(dir, "debug_log.txt");

    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Fact]
    public void Enqueue_DoesNotTouchDisk_UntilFlush()
    {
        var w = new BufferedLogWriter(() => LogPath, 1 << 20);
        w.Enqueue("a");
        w.Enqueue("b");
        Assert.False(File.Exists(LogPath));
        Assert.Equal(2, w.PendingCount);

        Assert.Equal(2, w.Flush());
        string[] lines = File.ReadAllLines(LogPath);
        Assert.Equal(new[] { "a", "b" }, lines);
        Assert.Equal(0, w.PendingCount);
    }

    [Fact]
    public void Flush_Appends_InOrder_AcrossBatches()
    {
        var w = new BufferedLogWriter(() => LogPath, 1 << 20);
        w.Enqueue("1"); w.Flush();
        w.Enqueue("2"); w.Enqueue("3"); w.Flush();
        Assert.Equal(new[] { "1", "2", "3" }, File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Flush_Rotates_ToPrev_WhenMaxBytesExceeded()
    {
        var w = new BufferedLogWriter(() => LogPath, 100);
        w.Enqueue(new string('x', 60)); w.Flush();
        w.Enqueue(new string('y', 60)); w.Flush(); // 60 + 62 > 100 -> rota

        string prev = BufferedLogWriter.PrevPath(LogPath);
        Assert.True(File.Exists(prev));
        Assert.Contains("xxxx", File.ReadAllText(prev));
        Assert.Contains("yyyy", File.ReadAllText(LogPath));
        Assert.DoesNotContain("xxxx", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Enqueue_DropsAndReports_WhenBufferFull()
    {
        var w = new BufferedLogWriter(() => LogPath, 1 << 20, maxPending: 3);
        for (int i = 0; i < 10; i++) w.Enqueue("l" + i);
        Assert.Equal(3, w.PendingCount);
        Assert.Equal(7, w.Dropped);

        w.Flush();
        string text = File.ReadAllText(LogPath);
        Assert.Contains("7 líneas descartadas", text);
        Assert.Contains("l0", text);
        Assert.DoesNotContain("l9", text);
    }

    [Fact]
    public void Flush_WithoutPath_KeepsPending_UntilPathKnown()
    {
        string path = null;
        var w = new BufferedLogWriter(() => path, 1 << 20);
        w.Enqueue("kept");
        Assert.Equal(0, w.Flush());
        Assert.Equal(1, w.PendingCount);

        path = LogPath;
        Assert.Equal(1, w.Flush());
        Assert.Equal(new[] { "kept" }, File.ReadAllLines(LogPath));
    }

    [Fact]
    public void BackgroundThread_FlushesWithoutExplicitCall()
    {
        var w = new BufferedLogWriter(() => LogPath, 1 << 20);
        w.Start(20);
        w.Enqueue("auto");
        for (int i = 0; i < 100 && !File.Exists(LogPath); i++) Thread.Sleep(20);
        w.Stop();
        Assert.Equal(new[] { "auto" }, File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Enqueue_IsThreadSafe()
    {
        var w = new BufferedLogWriter(() => LogPath, 1 << 24);
        var threads = new Thread[4];
        for (int t = 0; t < threads.Length; t++)
        {
            int id = t;
            threads[t] = new Thread(() => { for (int i = 0; i < 500; i++) w.Enqueue($"t{id}-{i}"); });
            threads[t].Start();
        }
        foreach (var th in threads) th.Join();
        w.Flush();
        Assert.Equal(2000, File.ReadAllLines(LogPath).Length);
    }
}
