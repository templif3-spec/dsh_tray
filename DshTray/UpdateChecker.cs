using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace DshTray;

/// <summary>
/// dsh 更新检查与执行：
/// - 本地版本：读 dsh 入口所属包目录的 package.json version
/// - 最新版本：npm registry 的 @deepseek-ai/dsh/latest
/// - 更新：`npx --yes @deepseek-ai/dsh@latest --version`，把最新版取到 npx 缓存
///   （托盘启动时按"最新修改的 npx 缓存目录"探测 dsh 入口，故更新后自动指向新版本）
/// </summary>
internal sealed class UpdateChecker
{
    public const string PackageName = "@deepseek-ai/dsh";
    private const string RegistryUrl = "https://registry.npmjs.org/@deepseek-ai/dsh/latest";

    private readonly DshManager _manager;

    public UpdateChecker(DshManager manager)
    {
        _manager = manager;
    }

    /// <summary>本地已安装的 dsh 版本；读取失败返回 null。</summary>
    public string? GetInstalledVersion()
    {
        try
        {
            var entry = _manager.ResolveDshEntry(); // …\@deepseek-ai\dsh\lib\bin.js
            var packageJson = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(entry)!, "..", "package.json"));
            if (!File.Exists(packageJson))
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>npm registry 上的最新版本；网络不可用返回 null。</summary>
    public static async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DshTray");
            var json = await http.GetStringAsync(RegistryUrl);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>是否存在版本差异（不同即视为有可用更新）。</summary>
    public static bool IsNewer(string latest, string installed) =>
        !string.Equals(latest.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 执行更新：以 npx 拉取并运行最新版（--version 立即退出，仅完成下载/安装）。
    /// 输出不捕获（受限环境禁止管道），靠退出码判断；最长等待 5 分钟。
    /// </summary>
    public async Task<bool> RunUpdateAsync()
    {
        try
        {
            var nodeDir = Path.GetDirectoryName(_manager.ResolveNodeExe())!;
            var npxCmd = Path.Combine(nodeDir, "npx.cmd");
            var shell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var command = File.Exists(npxCmd) ? $"\"{npxCmd}\"" : "npx";

            Log.Info("开始更新 dsh：npx --yes " + PackageName + "@latest");
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"/c {command} --yes {PackageName}@latest --version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                },
            };
            proc.Start();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Log.Error("RunUpdate", new TimeoutException("npx 更新超时（5 分钟）。"));
                return false;
            }

            Log.Info($"更新命令退出码：{proc.ExitCode}");
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("RunUpdate", ex);
            return false;
        }
    }
}
