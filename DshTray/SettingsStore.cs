using System.Text;

namespace DshTray;

/// <summary>
/// 生成/删除 dsh 的 --patch 覆盖层（exe 同目录 dsh-overlay.yml）。
/// 覆盖层按 id 替换 web-app bundle 的 webserver 行 config（整段替换），
/// 因此 host/port 两个字段必须同时写出（webserver 的 schema 要求两者均必填）。
/// 该文件只随 DshTray 配置存在，不触碰 $DSH_HOME/profiles/web/cordis.patch.yml。
/// </summary>
internal static class SettingsStore
{
    public static string PatchPath => Path.Combine(AppContext.BaseDirectory, "dsh-overlay.yml");

    /// <summary>按配置生成（或删除）覆盖层。host/port 都未显式设置时删除文件，恢复 dsh 默认。</summary>
    public static void Apply(Config config)
    {
        if (config.Host == null && config.Port == null)
        {
            if (File.Exists(PatchPath))
            {
                File.Delete(PatchPath);
            }
            return;
        }

        var host = config.Host ?? "127.0.0.1";
        var port = config.Port ?? 3080;
        if (host is not ("127.0.0.1" or "0.0.0.0") || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"无效的监听设置：host={host} port={port}");
        }

        var sb = new StringBuilder();
        sb.AppendLine("# DshTray — dsh web 监听设置覆盖层（文本由 DshTray 生成，请勿手改）");
        sb.AppendLine("# 在 dsh 启动时以 --patch 加载，按 id 覆盖 webserver 行的 host/port");
        sb.AppendLine("- id: webserver");
        sb.AppendLine("  config:");
        sb.AppendLine($"    host: '{host}'");
        sb.AppendLine($"    port: {port}");
        File.WriteAllText(PatchPath, sb.ToString(), new UTF8Encoding(false));
    }
}
