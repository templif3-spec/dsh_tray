using Microsoft.Win32;

namespace DshTray;

/// <summary>
/// 开机自启管理：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// （仅当前用户，无需管理员权限）。
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DshTray";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定程序自身路径。");
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 按配置同步注册表：
    /// config.autoStart=true  → 注册；false → 注销；未设置（null）→ 不干预。
    /// </summary>
    public static void Apply(Config config)
    {
        if (config.AutoStart == true)
        {
            Register();
        }
        else if (config.AutoStart == false)
        {
            Unregister();
        }
    }
}
