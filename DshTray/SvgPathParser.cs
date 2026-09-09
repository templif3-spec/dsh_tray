using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace DshTray;

/// <summary>
/// 最小 SVG path 解析器：支持绝对坐标命令 M / C / L / Z
/// （官方 FishLogo path 仅含这些命令），输出 System.Drawing GraphicsPath。
/// </summary>
internal static partial class SvgPathParser
{
    [GeneratedRegex(@"[A-Za-z]|-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    public static GraphicsPath Parse(string pathData)
    {
        var gp = new GraphicsPath();
        var tokens = TokenRegex().Matches(pathData);
        var i = 0;

        double cx = 0, cy = 0, startX = 0, startY = 0;
        var figureOpen = false;

        double ReadX()
        {
            return double.Parse(tokens[i++].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        while (i < tokens.Count)
        {
            var token = tokens[i].Value;
            i++;
            if (char.IsLetter(token[0]))
            {
                switch (token)
                {
                    case "M":
                        if (figureOpen)
                        {
                            gp.CloseFigure();
                            figureOpen = false;
                        }
                        startX = ReadX();
                        startY = ReadX();
                        cx = startX;
                        cy = startY;
                        figureOpen = true;
                        break;
                    case "C":
                        var x1 = ReadX(); var y1 = ReadX();
                        var x2 = ReadX(); var y2 = ReadX();
                        var x = ReadX(); var y = ReadX();
                        gp.AddBezier((float)cx, (float)cy, (float)x1, (float)y1, (float)x2, (float)y2, (float)x, (float)y);
                        cx = x;
                        cy = y;
                        break;
                    case "L":
                        var lx = ReadX();
                        var ly = ReadX();
                        gp.AddLine((float)cx, (float)cy, (float)lx, (float)ly);
                        cx = lx;
                        cy = ly;
                        break;
                    case "Z":
                        if (figureOpen)
                        {
                            gp.CloseFigure();
                            figureOpen = false;
                        }
                        cx = startX;
                        cy = startY;
                        break;
                    default:
                        gp.Dispose();
                        throw new NotSupportedException($"SVG path 命令 {token} 不受支持：{pathData[..120]}…");
                }
            }
            else
            {
                // 命令之外的孤立数字（官方 path 中不存在，防御性报错）
                gp.Dispose();
                throw new NotSupportedException("SVG path 中出现意外的数字 token。");
            }
        }
        if (figureOpen)
        {
            gp.CloseFigure();
        }
        return gp;
    }
}
