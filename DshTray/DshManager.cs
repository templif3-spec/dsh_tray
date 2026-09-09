using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace DshTray;

/// <summary>
/// dsh 服务管理：通过端口识别运行中的 dsh（最可靠，不依赖命令行），
/// 提供启动 / 停止 / 重启 / 等待就绪。
/// </summary>
internal sealed class DshManager
{
    public Config Config { get; }

    public DshManager(Config config)
    {
        Config = config;
    }

    // ---------- 进程识别 ----------

    /// <summary>
    /// 通过端口识别 dsh 进程 PID。两级探测：
    /// 1) GetExtendedTcpTable（AF_INET+AF_INET6，LISTEN/ESTABLISHED 等占用行，忽略 TIME_WAIT 的 pid=0）；
    /// 2) netstat -ano 解析兜底（部分受限环境下 iphlpapi 表不返回 LISTEN 行）。
    /// </summary>
    public int? FindDshPid()
    {
        foreach (var pid in QueryOwningPids(AF_INET, Config.EffectivePort).Concat(QueryOwningPids(AF_INET6, Config.EffectivePort)))
        {
            if (pid.HasValue && pid.Value > 0)
            {
                return pid.Value;
            }
        }
        return FindDshPidViaNetstat();
    }

    public bool IsDshRunning() => FindDshPid() != null;

    // ---------- 启动 / 停止 / 等待 ----------

    public Process StartDsh()
    {
        // 每次启动前把 config.json 的 host/port 同步成 --patch 覆盖层（幂等）；
        // 写入失败（如只读目录）不阻断启动，仅记录日志
        try
        {
            SettingsStore.Apply(Config);
        }
        catch (Exception ex)
        {
            Log.Error("SettingsStore.Apply", ex);
        }

        var psi = BuildStartInfo();
        Log.Info($"启动 dsh: {psi.FileName} {psi.Arguments}");
        return Process.Start(psi)!;
    }

    public void StopDsh()
    {
        var pid = FindDshPid();
        if (pid == null)
        {
            return;
        }

        using var proc = Process.GetProcessById(pid.Value);
        if (!IsLikelyDshProcess(proc))
        {
            throw new InvalidOperationException(
                $"端口 {Config.EffectivePort} 被进程 {proc.ProcessName}（PID {pid}）占用，不像 dsh 服务，已取消停止操作。");
        }
        Log.Info($"终止 dsh 进程树（PID {pid}）");
        proc.Kill(entireProcessTree: true);
    }

    public async Task<bool> WaitForPortAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (FindDshPid() != null && await TryHttpProbeAsync(800))
            {
                return true;
            }
            await Task.Delay(400);
        }
        return false;
    }

    public async Task<bool> WaitForPortFreeAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (FindDshPid() == null)
            {
                return true;
            }
            await Task.Delay(300);
        }
        return false;
    }

    private static bool IsLikelyDshProcess(Process p)
    {
        return p.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Equals("dsh", StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Equals("deno", StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Equals("bun", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryHttpProbeAsync(int timeoutMs)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = await client.GetAsync(Config.BrowserUrl);
            var code = (int)resp.StatusCode;
            // 200/401/403 等都说明 HTTP 服务已就绪
            return resp.IsSuccessStatusCode || code == 401 || code == 403;
        }
        catch
        {
            return false;
        }
    }

    // ---------- 启动命令解析 ----------

    private ProcessStartInfo BuildStartInfo()
    {
        var node = ResolveNodeExe();
        var entry = ResolveDshEntry();
        return new ProcessStartInfo
        {
            FileName = node,
            Arguments = $"\"{entry}\" {ResolveLaunchArgs()}",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    /// <summary>
    /// 启动参数：存在 dsh-overlay.yml（托盘「dsh 设置」生成的监听覆盖层）时用
    /// `--profile web --patch &lt;overlay&gt;` 加载；否则用默认 dshArgs（"web"）。
    /// </summary>
    private static string ResolveLaunchArgs()
    {
        if (File.Exists(SettingsStore.PatchPath))
        {
            return $"--profile web --patch \"{SettingsStore.PatchPath}\"";
        }
        return "web";
    }

    private string ResolveNodeExe()
    {
        if (!string.IsNullOrWhiteSpace(Config.NodeExe) && File.Exists(Config.NodeExe))
        {
            return Config.NodeExe!;
        }

        foreach (var dir in PathDirectories())
        {
            var candidate = Path.Combine(dir, "node.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var fallback in new[] { @"C:\Program Files\nodejs\node.exe", @"C:\nvm4w\nodejs\node.exe" })
        {
            if (File.Exists(fallback))
            {
                return fallback;
            }
        }

        throw new FileNotFoundException("未找到 node.exe：请在 config.json 中设置 \"node\" 路径。");
    }

    private string ResolveDshEntry()
    {
        if (!string.IsNullOrWhiteSpace(Config.DshEntry) && File.Exists(Config.DshEntry))
        {
            return Config.DshEntry!;
        }

        // 方案一：PATH 中有 dsh.cmd → 推出 @deepseek-ai/dsh/lib/bin.js
        foreach (var dir in PathDirectories())
        {
            var dshCmd = Path.Combine(dir, "dsh.cmd");
            if (File.Exists(dshCmd))
            {
                // dsh.cmd 位于 <root>\node_modules\.bin\
                var binJs = Path.Combine(Path.GetDirectoryName(dshCmd)!, "..", "@deepseek-ai", "dsh", "lib", "bin.js");
                binJs = Path.GetFullPath(binJs);
                if (File.Exists(binJs))
                {
                    return binJs;
                }
            }
        }

        // 方案二：npx 缓存中搜索（按修改时间取最新）
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache", "_npx");
        if (Directory.Exists(cacheRoot))
        {
            var best = Directory.EnumerateDirectories(cacheRoot)
                .Select(d => Path.Combine(d, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (best != null)
            {
                return best;
            }
        }

        throw new FileNotFoundException(
            "未找到 dsh 入口（@deepseek-ai/dsh/lib/bin.js）：请在 config.json 中设置 \"dshEntry\" 路径。");
    }

    private static IEnumerable<string> PathDirectories()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        return pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => d.Length > 0);
    }

    // ---------- 端口 → PID（P/Invoke GetExtendedTcpTable） ----------

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 4;
    private const uint MIB_TCP_STATE_TIME_WAIT = 11;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, int Reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    private static IEnumerable<int?> QueryOwningPids(int family, int targetPort)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0)
        {
            yield break;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
            {
                yield break;
            }

            var count = Marshal.ReadInt32(buffer);
            if (family == AF_INET)
            {
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buffer + 4 + i * rowSize);
                    if (row.OwningPid > 0 && row.State != MIB_TCP_STATE_TIME_WAIT
                        && NetworkToHost((ushort)row.LocalPort) == targetPort)
                    {
                        yield return (int)row.OwningPid;
                    }
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(buffer + 4 + i * rowSize);
                    if (row.OwningPid > 0 && row.State != MIB_TCP_STATE_TIME_WAIT
                        && NetworkToHost((ushort)row.LocalPort) == targetPort)
                    {
                        yield return (int)row.OwningPid;
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ushort NetworkToHost(ushort networkOrder)
    {
        return (ushort)IPAddress.NetworkToHostOrder((short)networkOrder);
    }

    // ---------- netstat 兜底 ----------

    private int? FindDshPidViaNetstat()
    {
        try
        {
            // 经 cmd 把输出重定向到临时文件：部分受限环境禁止子进程管道捕获
            // （RedirectStandardOutput 会 EPERM），文件重定向不受影响。
            var outputFile = Path.Combine(Path.GetTempPath(), $"dsh-tray-netstat-{Environment.ProcessId}.txt");
            File.Delete(outputFile);
            using (var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = $"/c netstat -ano > \"{outputFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            })
            {
                proc.Start();
                if (!proc.WaitForExit(8000))
                {
                    try { proc.Kill(); } catch { }
                }
            }

            if (!File.Exists(outputFile))
            {
                return null;
            }

            string output;
            try
            {
                output = File.ReadAllText(outputFile);
            }
            finally
            {
                try { File.Delete(outputFile); } catch { }
            }

            var expected = Config.EffectivePort.ToString();
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var localEnd = parts[1];
                var colon = localEnd.LastIndexOf(':');
                if (colon < 0 || localEnd[(colon + 1)..] != expected)
                {
                    continue;
                }

                // parts = [TCP, 127.0.0.1:3080, 0.0.0.0:0, LISTENING, 12424]
                if (int.TryParse(parts[^1], out var pid) && pid > 0)
                {
                    return pid;
                }
            }
        }
        catch
        {
            // netstat 不可用时静默回退
        }
        return null;
    }
}
