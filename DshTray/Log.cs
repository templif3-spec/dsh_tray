using System.Collections.Concurrent;

namespace DshTray;

/// <summary>
/// 文件日志（与 exe 同目录 dsh-tray.log）。
/// **异步写入**：调用方（含 UI 线程）只入队，写盘由独立后台线程完成 ——
/// 避免安全软件扫描文件时阻塞 UI 线程（托盘 UI 阻塞会导致 Shell
/// 与托盘窗口交互超时，进而使任务栏/鼠标点击异常）。
/// </summary>
internal static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>());
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "dsh-tray.log");

    static Log()
    {
        var writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "DshTray.LogWriter",
        };
        writer.Start();
    }

    public static void Info(string message) => Enqueue("INFO", message);

    public static void Error(string context, Exception ex) =>
        Enqueue("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>等待队列落盘（进程退出前调用，避免丢失尾部日志）。</summary>
    public static void Flush(int timeoutMs = 3000)
    {
        try
        {
            Queue.CompleteAdding();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(20);
            }
        }
        catch
        {
            // 忽略
        }
    }

    private static void Enqueue(string level, string message)
    {
        try
        {
            if (!Queue.IsAddingCompleted)
            {
                Queue.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
            }
        }
        catch
        {
            // 队列关闭（进程退出中）时忽略：日志失败不影响主流程
        }
    }

    private static void WriteLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch
            {
                // 写盘失败不影响主流程
            }
        }
    }
}
