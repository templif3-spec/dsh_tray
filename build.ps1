param(
    # 自包含发布（目标机器无需安装 .NET 运行时，exe 体积较大；离线环境不可用）
    [switch]$SelfContained,
    # 保留 PDB 调试符号（默认关闭，发布产物不含 .pdb）
    [switch]$KeepPdb,
    [string]$Runtime = "win-x64",
    [string]$Output = (Join-Path $PSScriptRoot "dist")
)

# 首次使用时把 dotnet 的配置目录放到本地临时区，避免权限问题
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $env:TEMP "dotnet-cli-home"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_NOLOGO = "1"
}

$project = Join-Path $PSScriptRoot "DshTray\DshTray.csproj"
$outDir = Join-Path $Output $Runtime

Write-Host "==> dotnet publish ($(if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }))"

# 离线构建支持：targeting packs 已随 SDK 安装，无需从 NuGet 下载
$args = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $SelfContained,
    "-p:PublishSingleFile=true",
    "-p:SelfContained=false",
    # 单文件不裁剪：关闭 single-file 分析器，避免恢复阶段要求 Microsoft.NET.ILLink.Tasks
    "-p:EnableSingleFileAnalyzer=false",
    "-p:EnableRuntimePackDownload=false",
    "-p:EnableTargetingPackDownload=false",
    "-p:RestoreIgnoreFailedSources=true",
    "-p:NuGetAudit=false",
    "-o", $outDir
)

# Release 产物默认不带 PDB（调试符号关闭；需要调试时用 -KeepPdb）
if (-not $KeepPdb) {
    $args += @(
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )
}

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    Write-Error "发布失败：$outDir"
    exit $LASTEXITCODE
}

# 清理可能残留的符号文件，确保产物目录只有 exe 等运行文件
if (-not $KeepPdb) {
    Get-ChildItem $outDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "构建完成: $outDir\DshTray.exe" -ForegroundColor Green
Write-Host "双击 DshTray.exe 即进驻系统托盘；可在同目录放置 config.json 自定义配置。" -ForegroundColor DarkGray
