using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DshTray;

internal static class Program
{
    // Win10/11 通知 toast 按 AppUserModelID 关联应用图标（即 exe 的鲸鱼图标），
    // 避免未注册进程被系统显示为通用图标。
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static void Main()
    {
        // 单实例：避免多次双击产生多个托盘图标
        using var mutex = new Mutex(true, @"Global\DshTray.SingleInstance.b7c1", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        // 开机自启场景（注册表 Run 传入 --autostart）：延迟启动 dsh，避开登录高峰
        var autoStarted = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));

        SetCurrentProcessExplicitAppUserModelID("DeepSeek.DshTray.Tray");
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(autoStarted));
    }
}
