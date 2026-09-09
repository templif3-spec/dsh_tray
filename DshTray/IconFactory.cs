using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshTray;

/// <summary>
/// 图标工厂：
/// - Create()              鲸鱼图标（嵌入资源里的官方 DeepSeek FishLogo 多尺寸 ico）
/// - CreateLoadingFrames() loading 动画帧（旋转弧线环 + 矢量渲染的鲸鱼剪影，黑色主题）
/// </summary>
internal static class IconFactory
{
    private static readonly Lazy<Icon> NormalIcon = new(() =>
    {
        using var stream = typeof(IconFactory).Assembly.GetManifestResourceStream("DshTray.deepseek.ico");
        if (stream != null)
        {
            var size = SystemInformation.SmallIconSize;
            return new Icon(stream, size.Width, size.Height);
        }
        return (Icon)SystemIcons.Application.Clone();
    });

    private static readonly Lazy<GraphicsPath> WhaleShape = new(() =>
        SvgPathParser.Parse(WhalePathData.Path));

    /// <summary>鲸鱼图标（按系统小图标尺寸）。调用方持有并负责 Dispose。</summary>
    public static Icon Create()
    {
        return (Icon)NormalIcon.Value.Clone();
    }

    /// <summary>
    /// 生成 loading 动画帧（旋转 270° 弧线环 + 中心矢量鲸鱼剪影）。
    /// 帧对象由调用方持有并负责 Dispose。
    /// </summary>
    public static Icon[] CreateLoadingFrames(int frames = 8)
    {
        var icons = new Icon[frames];
        try
        {
            var size = SystemInformation.SmallIconSize;
            var ss = size.Width * 4; // 4x 超采样抗锯齿
            for (var f = 0; f < frames; f++)
            {
                using var bmp = new Bitmap(ss, ss);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // 中心鲸鱼（矢量渲染，占 62% 内圆）
                    var whaleW = ss * 0.62f;
                    var scale = whaleW / (float)WhalePathData.ViewWidth;
                    var drawH = (float)WhalePathData.ViewHeight * scale;
                    g.TranslateTransform((ss - whaleW) / 2f, (ss - drawH) / 2f);
                    g.ScaleTransform(scale, scale);
                    using var brush = new SolidBrush(Color.Black);
                    g.FillPath(brush, WhaleShape.Value);
                    g.ResetTransform();

                    // 旋转弧线环（270°，每帧转角 360/frames）
                    using var pen = new Pen(Color.Black, ss * 0.085f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                    };
                    var ring = new RectangleF(ss * 0.06f, ss * 0.06f, ss * 0.88f, ss * 0.88f);
                    var start = -90f + f * 360f / frames;
                    g.DrawArc(pen, ring, start, 270f);
                }

                using var temp = Icon.FromHandle(bmp.GetHicon());
                icons[f] = (Icon)temp.Clone();
            }
        }
        catch
        {
            foreach (var icon in icons)
            {
                icon?.Dispose();
            }
            throw;
        }
        return icons;
    }
}
