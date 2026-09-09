using System.Threading;
using System.Windows.Forms;

namespace DshTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 单实例：避免多次双击产生多个托盘图标
        using var mutex = new Mutex(true, @"Global\DshTray.SingleInstance.b7c1", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
