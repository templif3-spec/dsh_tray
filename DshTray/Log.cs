namespace DshTray;

/// <summary>简单文件日志（与 exe 同目录 dsh-tray.log），便于排查。</summary>
internal static class Log
{
    private static readonly object Sync = new();

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "dsh-tray.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
