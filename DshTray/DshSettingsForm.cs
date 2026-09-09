using System.Drawing;
using System.Windows.Forms;

namespace DshTray;

/// <summary>
/// 「dsh 设置」对话框：配置 dsh Web 服务监听地址与端口、开机自启。
/// 保存后写入 config.json、生成 dsh-overlay.yml（下次启动 dsh 时经 --patch 生效）、
/// 并立即同步开机启动注册表项。
/// </summary>
internal sealed class DshSettingsForm : Form
{
    private static readonly string[] HostDisplayValues = ["127.0.0.1（仅本机可访问）", "0.0.0.0（局域网可访问）"];
    private static readonly string[] HostConfigValues = ["127.0.0.1", "0.0.0.0"];

    private readonly Config _config;
    private readonly ComboBox _hostCombo;
    private readonly NumericUpDown _portBox;
    private readonly CheckBox _autoStartBox;
    private readonly Label _warning;
    private readonly Label _currentHint;

    /// <summary>保存后的生效值（未修改时为空）。</summary>
    public string? SavedHost { get; private set; }
    public int SavedPort { get; private set; }
    /// <summary>host/port 是否发生变化（需要重启 dsh 生效）。</summary>
    public bool Changed { get; private set; }
    /// <summary>开机自启是否发生变化（立即生效，无需重启 dsh）。</summary>
    public bool AutoStartChanged { get; private set; }

    public DshSettingsForm(Config config)
    {
        _config = config;
        Text = "dsh 设置";
        Icon = IconFactory.Create();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 306);
        Font = new Font("Microsoft YaHei UI", 9F);

        var hostLabel = new Label
        {
            Text = "监听地址 (host)：",
            Location = new Point(20, 22),
            AutoSize = true,
        };

        _hostCombo = new ComboBox
        {
            Location = new Point(24, 46),
            Width = 372,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _hostCombo.Items.AddRange(HostDisplayValues);
        _hostCombo.SelectedIndex = config.Host == "0.0.0.0" ? 1 : 0;
        _hostCombo.SelectedIndexChanged += (_, _) => UpdateWarning();

        _warning = new Label
        {
            Text = "提示：仅建议在可信网络使用；\r\n开启后将允许局域网设备访问 dsh Web 界面。",
            Location = new Point(24, 76),
            Size = new Size(372, 42),
            ForeColor = Color.FromArgb(176, 90, 0),
            Visible = false,
        };

        var portLabel = new Label
        {
            Text = "监听端口：",
            Location = new Point(20, 128),
            AutoSize = true,
        };

        _portBox = new NumericUpDown
        {
            Location = new Point(24, 152),
            Width = 120,
            Minimum = 1,
            Maximum = 65535,
            Value = config.EffectivePort,
        };

        _currentHint = new Label
        {
            Text = $"当前生效：{config.Host ?? "127.0.0.1"}:{config.EffectivePort}",
            Location = new Point(160, 156),
            AutoSize = true,
            ForeColor = Color.Gray,
        };

        _autoStartBox = new CheckBox
        {
            Text = "开机自动启动（登录 Windows 后自动驻留托盘）",
            Location = new Point(24, 192),
            AutoSize = true,
            Checked = config.AutoStart == true || (config.AutoStart == null && StartupManager.IsRegistered()),
        };

        var autoStartHint = new Label
        {
            Text = "勾选后写入当前用户启动项（注册表 Run 键），\r\n无需管理员权限；登录 Windows 后自动驻留托盘。",
            Location = new Point(40, 216),
            Size = new Size(372, 34),
            ForeColor = Color.Gray,
        };

        var saveButton = new Button
        {
            Text = "保存",
            Location = new Point(24, 252),
            Size = new Size(120, 32),
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(160, 252),
            Size = new Size(90, 32),
            DialogResult = DialogResult.Cancel,
        };

        Controls.AddRange(new Control[] { hostLabel, _hostCombo, _warning, portLabel, _portBox, _currentHint, _autoStartBox, autoStartHint, saveButton, cancelButton });
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        UpdateWarning();
    }

    private void UpdateWarning()
    {
        _warning.Visible = _hostCombo.SelectedIndex == 1;
    }

    private void SaveAndClose()
    {
        var origHost = _config.Host ?? "127.0.0.1";
        var origPort = _config.EffectivePort;
        var origAutoStart = _config.AutoStart ?? StartupManager.IsRegistered();

        var host = HostConfigValues[_hostCombo.SelectedIndex];
        var port = decimal.ToInt32(_portBox.Value);
        var autoStart = _autoStartBox.Checked;

        _config.Host = host;
        _config.Port = port;
        _config.AutoStart = autoStart;
        try
        {
            _config.Save();
            SettingsStore.Apply(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存配置失败：{ex.Message}", "dsh 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            StartupManager.Apply(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"监听配置已保存，但写入开机启动项失败：{ex.Message}",
                "dsh 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SavedHost = host;
        SavedPort = port;
        Changed = host != origHost || port != origPort;
        AutoStartChanged = autoStart != origAutoStart;
        DialogResult = DialogResult.OK;
        Close();
    }
}
