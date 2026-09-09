# make-icon.ps1 — 从官方 FISH_LOGO_PATH（assets/whale-path.txt）生成 DeepSeek 鲸鱼多尺寸 ICO
# 解析 SVG path（M/C/L/Z 绝对坐标）→ System.Drawing GraphicsPath → 1024px 超采样 → 缩放各尺寸 → PNG-ICO
param(
    [string]$PathFile = (Join-Path $PSScriptRoot 'whale-path.txt'),
    [string]$OutIco = 'D:\Projects\dsh_tray\DshTray\deepseek.ico',
    [string]$OutPreview = (Join-Path $PSScriptRoot 'whale-256.png')
)

Add-Type -AssemblyName System.Drawing

$pathData = [System.IO.File]::ReadAllText($PathFile)

# ---------- tokenize: 命令字母 + 数字 ----------
$tokenRe = [regex]'[A-Za-z]|-?\d*\.?\d+(?:[eE][-+]?\d+)?'
$tokens = [System.Collections.Generic.List[string]]::new()
foreach ($m in $tokenRe.Matches($pathData)) { $tokens.Add($m.Value) }

# ---------- 构建 GraphicsPath ----------
$gp = [System.Drawing.Drawing2D.GraphicsPath]::new()
$i = 0
$cx = 0.0; $cy = 0.0       # 当前点
$startX = 0.0; $startY = 0.0
$figureOpen = $false

function Read-X { $script:i++; [double]$tokens[$script:i - 1] }

while ($i -lt $tokens.Count) {
    $t = $tokens[$i]; $i++
    if ($t -match '^[A-Za-z]$') {
        if ($t -eq 'M') {
            if ($figureOpen) { $gp.CloseFigure(); $figureOpen = $false }
            $startX = Read-X; $startY = Read-X
            $cx = $startX; $cy = $startY
            $figureOpen = $true
        }
        elseif ($t -eq 'C') {
            $x1 = Read-X; $y1 = Read-X; $x2 = Read-X; $y2 = Read-X; $x = Read-X; $y = Read-X
            $gp.AddBezier([float]$cx, [float]$cy, [float]$x1, [float]$y1, [float]$x2, [float]$y2, [float]$x, [float]$y)
            $cx = $x; $cy = $y
        }
        elseif ($t -eq 'L') {
            $x = Read-X; $y = Read-X
            $gp.AddLine([float]$cx, [float]$cy, [float]$x, [float]$y)
            $cx = $x; $cy = $y
        }
        elseif ($t -eq 'Z') {
            if ($figureOpen) { $gp.CloseFigure(); $figureOpen = $false }
            $cx = $startX; $cy = $startY
        }
        else { throw "未支持的 SVG 命令: $t" }
    }
    else {
        # 数字跟随前一个坐标命令：M 后多余的坐标对按隐式 L 处理（本图标用不上，防御性忽略）
        $i--  # 回退一个数字，仅当数字跟在数字后时发生——本 path 不存在，直接抛错更安全
        throw "意外的数字 token: $t"
    }
}
if ($figureOpen) { $gp.CloseFigure() }

# ---------- 超采样渲染到 1024x1024（透明底黑色鲸鱼，居中） ----------
$viewW = 23.16; $viewH = 17.04
$big = 1024
$scale = $big / $viewW
$drawH = $viewH * $scale
$offY = ($big - $drawH) / 2

$bmp = [System.Drawing.Bitmap]::new($big, $big, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)
$g.TranslateTransform(0, $offY)
$g.ScaleTransform($scale, $scale)
$g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::Black), $gp)
$g.Dispose()

# ---------- 各尺寸 PNG 字节 ----------
$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) {
    $small = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.Clear([System.Drawing.Color]::Transparent)
    $sg.DrawImage($bmp, 0, 0, $s, $s)
    $sg.Dispose()
    $ms = [System.IO.MemoryStream]::new()
    $small.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs["$s"] = $ms.ToArray()
    $ms.Dispose()
    $small.Dispose()
}

# ---------- 预览 PNG ----------
$bmp.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png)

# ---------- 组装 PNG-ICO ----------
$n = $pngs.Count
$header = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($header)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$n)
$entries = [System.Collections.Generic.List[byte[]]]::new()
$offset = 6 + 16 * $n
foreach ($s in $sizes) {
    $bytes = $pngs["$s"]
    $entryMs = [System.IO.MemoryStream]::new()
    $ebw = [System.IO.BinaryWriter]::new($entryMs)
    $ebw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))   # width (0 = 256)
    $ebw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))   # height
    $ebw.Write([byte]0)   # colors
    $ebw.Write([byte]0)   # reserved
    $ebw.Write([uint16]1) # planes
    $ebw.Write([uint16]32) # bitcount
    $ebw.Write([uint32]$bytes.Length)
    $ebw.Write([uint32]$offset)
    $entries.Add($entryMs.ToArray())
    $offset += $bytes.Length
    $entryMs.Dispose()
}
# 拼接写文件
$final = [System.IO.MemoryStream]::new()
$header.Position = 0; $header.CopyTo($final)
foreach ($e in $entries) { $final.Write($e, 0, $e.Length) }
foreach ($s in $sizes) { $final.Write($pngs["$s"], 0, $pngs["$s"].Length) }
[System.IO.File]::WriteAllBytes($OutIco, $final.ToArray())

$bmp.Dispose()
Write-Host "生成完成: $OutIco ($($final.Length) bytes, $n 个尺寸), 预览: $OutPreview"
