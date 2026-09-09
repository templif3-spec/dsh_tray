using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshTray;

/// <summary>
/// 可选配置文件（与 DshTray.exe 同目录的 config.json）。
/// 所有字段均可省略，省略时自动探测：
///   url      界面地址，决定浏览器打开与默认端口（默认 http://127.0.0.1:3080）
///   host     dsh 监听 host，仅允许 127.0.0.1 / 0.0.0.0（null=127.0.0.1）
///   port     dsh 监听端口 1-65535（null=3080）
///   node     node.exe 路径（默认从 PATH 探测，回退常见安装位置）
///   dshEntry dsh 入口 bin.js 路径（默认从 PATH 的 dsh.cmd 或 npx 缓存探测）
///   dshArgs  启动参数（默认 "web"；显式设置 Host/Port 时改用 --patch 覆盖层）
///   autoStart 是否写入开机启动项（true=注册；false=注销；未设置不干预）
/// </summary>
internal sealed class Config
{
    public string Url { get; set; } = "http://127.0.0.1:3080";
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? NodeExe { get; set; }
    public string? DshEntry { get; set; }
    public string DshArgs { get; set; } = "web";
    public bool? AutoStart { get; set; }

    /// <summary>生效端口：Port 字段优先，回退 url 中的端口。</summary>
    public int EffectivePort
    {
        get
        {
            if (Port is > 0 and <= 65535)
            {
                return Port.Value;
            }
            try
            {
                var p = new Uri(Url).Port;
                return p > 0 ? p : 3080;
            }
            catch
            {
                return 3080;
            }
        }
    }

    /// <summary>浏览器打开的地址：host 为 0.0.0.0 时用 127.0.0.1（0.0.0.0 无法在浏览器访问）。</summary>
    public string BrowserUrl
    {
        get
        {
            if (Host == null)
            {
                return Url;
            }
            var host = Host == "0.0.0.0" ? "127.0.0.1" : Host;
            return $"http://{host}:{EffectivePort}";
        }
    }

    public static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config.json");
        Config config;
        if (File.Exists(path))
        {
            try
            {
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path)) ?? new Config();
            }
            catch
            {
                config = new Config();
            }
        }
        else
        {
            config = new Config();
        }

        // config 未显式设置监听但覆盖层存在（UI 保存产物、config.json 被移动/删除）时，
        // 以覆盖层为准反读，保证端口识别与浏览器地址与 dsh 实际监听一致。
        if (config.Host == null && config.Port == null && SettingsStore.TryReadPatch(out var h, out var p))
        {
            config.Host = h;
            config.Port = p;
        }
        return config;
    }

    /// <summary>保存到 exe 同目录 config.json（null 字段不写出，保持文件干净）。</summary>
    public void Save()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var path = Path.Combine(AppContext.BaseDirectory, "config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, options), new System.Text.UTF8Encoding(false));
    }
}
