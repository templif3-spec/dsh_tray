using System.Diagnostics;
using System.Windows.Forms;

namespace DshTray;

/// <summary>
/// 托盘应用主体：NotifyIcon + 右键菜单（打开界面 / 重启 dsh / 关闭 dsh / 退出）。
/// 启动时若 dsh 未运行则自动拉起，全程无可见窗口。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Config _config;
    private readonly DshManager _manager;
    private readonly UpdateChecker _updateChecker;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // loading 图标动画状态
    private static readonly int LoadingFrameCount = 8;
    private readonly System.Windows.Forms.Timer _loadingTimer;
    private Icon? _normalIcon;
    private Icon[]? _loadingFrames;
    private int _loadingFrame;
    private bool _busy;

    public TrayApplicationContext()
    {
        _config = Config.Load();
        _manager = new DshManager(_config);
        _updateChecker = new UpdateChecker(_manager);

        var menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("dsh：检测中…") { Enabled = false };

        var openItem = new ToolStripMenuItem("在浏览器中打开界面");
        openItem.Click += (_, _) => OpenBrowser();

        var restartItem = new ToolStripMenuItem("重启 dsh");
        restartItem.Click += async (_, _) => await RestartDshAsync();

        var stopItem = new ToolStripMenuItem("关闭 dsh");
        stopItem.Click += async (_, _) => await StopDshAsync();

        var settingsItem = new ToolStripMenuItem("dsh 设置…");
        settingsItem.Click += (_, _) => OpenSettings();

        var updateItem = new ToolStripMenuItem("检查 dsh 更新…");
        updateItem.Click += async (_, _) => await CheckUpdatesAsync(silent: false);

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication();

        var versionItem = new ToolStripMenuItem($"v{Application.ProductVersion}") { Enabled = false };

        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            openItem,
            restartItem,
            stopItem,
            settingsItem,
            updateItem,
            new ToolStripSeparator(),
            exitItem,
            versionItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Text = "DshTray — dsh 服务托盘管理",
            Icon = _normalIcon = IconFactory.Create(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenBrowser();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();

        _loadingTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _loadingTimer.Tick += (_, _) => AdvanceLoadingFrame();

        // 启动时同步 config.json 的开机自启设置（支持手改配置生效、修复注册表被外部改动）
        try
        {
            StartupManager.Apply(_config);
        }
        catch (Exception ex)
        {
            Log.Error("StartupManager.Apply", ex);
        }

        _ = BootstrapAsync();

        // 开启自动更新时：启动后延迟静默检查（有新版则自动更新）
        if (_config.AutoUpdate == true)
        {
            _ = AutoUpdateCheckAsync();
        }
    }

    // ---------- dsh 更新检查 ----------

    /// <summary>启动后的静默自动检查（延迟避开启动高峰）。</summary>
    private async Task AutoUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            Log.Info("自动更新检查开始（autoUpdate=true）。");
            await CheckUpdatesAsync(silent: true);
        }
        catch (Exception ex)
        {
            Log.Error("AutoUpdateCheck", ex);
        }
    }

    /// <summary>
    /// 检查 dsh 更新：比对本地版本与 npm registry 最新版本。
    /// silent=true（自动模式）时发现新版直接更新；false（手动）时先询问。
    /// </summary>
    private async Task CheckUpdatesAsync(bool silent)
    {
        if (_busy)
        {
            return;
        }

        if (!silent)
        {
            SetBusy(true, "正在检查 dsh 更新…");
        }

        try
        {
            var installed = _updateChecker.GetInstalledVersion();
            var latest = await UpdateChecker.GetLatestVersionAsync();

            if (latest == null)
            {
                if (!silent)
                {
                    Notify("检查更新失败", "无法访问 npm registry（网络不可用？）。", ToolTipIcon.Warning);
                }
                return;
            }

            if (installed == null)
            {
                if (!silent)
                {
                    Notify("检查更新", $"npm 上最新版本：{latest}（未能读取本地版本）。", ToolTipIcon.Info);
                }
                return;
            }

            if (!UpdateChecker.IsNewer(latest, installed))
            {
                Log.Info($"dsh 已是最新：{installed}");
                if (!silent)
                {
                    Notify("dsh 已是最新", $"当前版本 {installed}。", ToolTipIcon.Info);
                }
                return;
            }

            Log.Info($"发现 dsh 新版本：{latest}（当前 {installed}）");
            if (silent)
            {
                await RunUpdateAsync(latest, installed);
                return;
            }

            if (!silent)
            {
                SetBusy(false, null); // 先恢复以便弹出询问框
            }
            var answer = MessageBox.Show(
                $"发现新版本 dsh {latest}（当前 {installed}）。\n\n是否立即更新？（更新后需重启 dsh 生效）",
                "dsh 更新",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                await RunUpdateAsync(latest, installed);
            }
        }
        catch (Exception ex)
        {
            Log.Error("CheckUpdates", ex);
            if (!silent)
            {
                Notify("检查更新失败", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            if (!silent)
            {
                SetBusy(false, null);
            }
            RefreshStatus();
        }
    }

    /// <summary>执行更新并在成功后询问是否重启 dsh。</summary>
    private async Task RunUpdateAsync(string latest, string installed)
    {
        try
        {
            SetBusy(true, $"正在更新 dsh 到 {latest}…");
            var ok = await _updateChecker.RunUpdateAsync();
            if (!ok)
            {
                Notify("dsh 更新失败",
                    "npx 更新未成功，请检查网络或手动执行：npx @deepseek-ai/dsh@latest",
                    ToolTipIcon.Warning);
                return;
            }

            Log.Info($"dsh 更新完成：{installed} → {latest}");
            Notify("dsh 更新完成", $"已更新到 {latest}（原 {installed}），重启 dsh 后生效。", ToolTipIcon.Info);

            var answer = MessageBox.Show(
                $"dsh 已更新到 {latest}。\n\n是否立即重启 dsh 使其生效？",
                "dsh 更新",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                await RestartDshAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("RunUpdate", ex);
            Notify("dsh 更新失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    // ---------- loading 图标动画 ----------

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            try
            {
                _loadingFrames ??= IconFactory.CreateLoadingFrames(LoadingFrameCount);
            }
            catch (Exception ex)
            {
                // 帧生成失败（罕见）：保持普通图标，仅记录
                Log.Error("CreateLoadingFrames", ex);
                _loadingTimer.Stop();
                return;
            }
            _loadingFrame = 0;
            _notifyIcon.Icon = _loadingFrames[0];
            _notifyIcon.Text = "DshTray — 正在重启/启动 dsh…";
            _loadingTimer.Start();
        }
        else
        {
            _loadingTimer.Stop();
            _normalIcon ??= IconFactory.Create();
            _notifyIcon.Icon = _normalIcon;
            _notifyIcon.Text = "DshTray — dsh 服务托盘管理";
        }
    }

    private void AdvanceLoadingFrame()
    {
        if (_loadingFrames == null)
        {
            return;
        }
        _loadingFrame = (_loadingFrame + 1) % _loadingFrames.Length;
        _notifyIcon.Icon = _loadingFrames[_loadingFrame];
    }

    /// <summary>启动流程：dsh 已在运行则静默驻留；否则拉起并等待就绪。</summary>
    private async Task BootstrapAsync()
    {
        try
        {
            var pid = _manager.FindDshPid();
            if (pid != null)
            {
                Log.Info($"dsh 已在运行（PID {pid}），进入托盘。");
                return;
            }

            Log.Info("dsh 未运行，准备启动…");
            SetBusy(true, "正在启动 dsh…");
            using var proc = _manager.StartDsh();

            if (await _manager.WaitForPortAsync(90_000))
            {
                Log.Info("dsh 启动成功。");
                Notify("dsh 已启动", $"界面：{_config.BrowserUrl}", ToolTipIcon.Info);
            }
            else
            {
                Log.Error("Bootstrap", new TimeoutException("dsh 未在 90 秒内就绪。"));
                Notify("dsh 启动超时", "服务未在预期时间内就绪，请查看 dsh-tray.log 或 dsh 日志。", ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Bootstrap", ex);
            Notify("dsh 启动失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private async Task RestartDshAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            SetBusy(true, "正在重启 dsh…");
            Log.Info("开始重启 dsh。");

            if (_manager.IsDshRunning())
            {
                _manager.StopDsh();
                await _manager.WaitForPortFreeAsync(20_000);
            }

            using var proc = _manager.StartDsh();
            if (await _manager.WaitForPortAsync(90_000))
            {
                Log.Info("dsh 重启成功。");
                Notify("dsh 已重启", $"界面：{_config.BrowserUrl}", ToolTipIcon.Info);
            }
            else
            {
                Notify("重启超时", "新进程已拉起但服务未在 90 秒内就绪，请查看日志。", ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("RestartDsh", ex);
            Notify("重启 dsh 失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private async Task StopDshAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            if (!_manager.IsDshRunning())
            {
                Notify("dsh", "dsh 当前未运行。", ToolTipIcon.Info);
                return;
            }

            SetBusy(true, "正在关闭 dsh…");
            Log.Info("开始关闭 dsh。");
            _manager.StopDsh();
            var released = await _manager.WaitForPortFreeAsync(20_000);
            Log.Info(released ? "dsh 已关闭。" : "dsh 进程已终止，但端口未完全释放。");
            Notify("dsh 已关闭", released ? "服务已停止。" : "服务已停止，端口仍在释放中。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error("StopDsh", ex);
            Notify("关闭 dsh 失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
            RefreshStatus();
        }
    }

    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_config.BrowserUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("OpenBrowser", ex);
            Notify("打开界面失败", ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// 「dsh 设置」：编辑监听 host/port，保存后生成 dsh-overlay.yml；
    /// 若 dsh 正在运行则询问是否立即重启使其生效。
    /// </summary>
    private void OpenSettings()
    {
        var form = new DshSettingsForm(_config);
        if (form.ShowDialog() != DialogResult.OK
            || (!form.Changed && !form.AutoStartChanged && !form.AutoUpdateChanged))
        {
            return;
        }

        Log.Info($"设置已保存：host={form.SavedHost} port={form.SavedPort} autoStart={_config.AutoStart} autoUpdate={_config.AutoUpdate}");

        if (form.AutoStartChanged)
        {
            Notify("开机自启", _config.AutoStart == true
                ? "已启用：登录 Windows 后自动驻留托盘。"
                : "已关闭：下次登录不再自动启动。", ToolTipIcon.Info);
        }

        if (form.AutoUpdateChanged)
        {
            Notify("dsh 自动更新", _config.AutoUpdate == true
                ? "已启用：启动时自动检查并更新 DeepSeek Harness。"
                : "已关闭：不再自动检查更新（仍可手动检查）。", ToolTipIcon.Info);
        }

        if (!form.Changed)
        {
            return;
        }

        if (!_manager.IsDshRunning())
        {
            Notify("dsh 设置已保存", $"新配置将在下次启动 dsh 时生效（界面：{_config.BrowserUrl}）。", ToolTipIcon.Info);
            return;
        }

        var answer = MessageBox.Show(
            $"dsh 正在运行，新的监听配置需重启 dsh 才能生效。\n\n是否立即重启？",
            "dsh 设置",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            _ = RestartDshAsync();
        }
        else
        {
            Notify("dsh 设置已保存", $"配置已保存，将在下次重启 dsh 时生效（界面：{_config.BrowserUrl}）。", ToolTipIcon.Info);
        }
    }

    private void SetBusy(bool busy, string? statusText)
    {
        _busy = busy;
        if (busy)
        {
            if (statusText != null)
            {
                _statusItem.Text = statusText;
            }
            SetLoading(true);
            _notifyIcon.ContextMenuStrip!.Items.Cast<ToolStripItem>()
                .Where(i => i.Enabled && i != _statusItem)
                .ToList()
                .ForEach(i => i.Enabled = false);
        }
        else
        {
            SetLoading(false);
            RefreshStatus();
            _notifyIcon.ContextMenuStrip!.Items.Cast<ToolStripItem>()
                .Where(i => i.Enabled == false && i != _statusItem)
                .ToList()
                .ForEach(i => i.Enabled = true);
        }
    }

    private void RefreshStatus()
    {
        var pid = _manager.FindDshPid();
        _statusItem.Text = pid != null
            ? $"dsh：运行中（PID {pid}）"
            : "dsh：未运行（右键可启动）";
        _statusItem.ToolTipText = $"界面地址：{_config.BrowserUrl}";
    }

    private void Notify(string title, string message, ToolTipIcon icon)
    {
        if (!_notifyIcon.Visible)
        {
            return;
        }

        // 气泡图标取 NotifyIcon.Icon：loading 动画期间弹通知时先切回鲸鱼图标，
        // 保证通知信息与主图标一致（动画将在 SetBusy(false) 时一并复位）。
        if (_loadingTimer.Enabled)
        {
            _loadingTimer.Stop();
            _normalIcon ??= IconFactory.Create();
            _notifyIcon.Icon = _normalIcon;
            _notifyIcon.Text = "DshTray — dsh 服务托盘管理";
        }
        _notifyIcon.ShowBalloonTip(4000, title, message, icon);
    }

    private void ExitApplication()
    {
        _refreshTimer.Stop();
        _loadingTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon?.Dispose();
        if (_loadingFrames != null)
        {
            foreach (var icon in _loadingFrames)
            {
                icon.Dispose();
            }
        }
        Log.Info("托盘程序退出（dsh 服务保持运行）。");
        ExitThread();
    }
}
