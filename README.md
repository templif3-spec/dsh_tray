# DshTray — dsh 服务托盘管理

Windows 系统托盘程序：双击启动后自动隐藏到系统状态栏（托盘），用于管理 DeepSeek Harness Web 服务（dsh）。

## 功能

- **双击 `DshTray.exe`** → 进入系统托盘（无窗口）；若 dsh 未运行则自动拉起并等待就绪
- **托盘图标右键菜单**：
  - `在浏览器中打开界面` — 用默认浏览器打开 dsh 界面（双击图标同效）
  - `重启 dsh` — 停止现有 dsh 进程树并重新启动，等端口就绪后弹气泡通知
  - `关闭 dsh` — 停止 dsh 服务（托盘程序保持驻留，可随时重启）
  - `dsh 设置…` — 配置监听地址（host）与端口，保存后生成启动覆盖层，可立即重启生效
  - `退出` — 退出托盘程序（**不关闭 dsh**）
- 菜单首行实时显示 dsh 状态（运行中 PID / 未运行），每 15 秒自动刷新
- **加载动画**：dsh 启动 / 重启 / 关闭期间，托盘图标切换为「旋转弧线环 + 中心鲸鱼剪影」动画（8 帧，120ms/帧），操作完成后恢复鲸鱼图标
- 操作结果通过通知气泡提示；详细日志写入 exe 同目录 `dsh-tray.log`
- 图标为 DeepSeek 官方黑色小鲸鱼（沿用 dsh 前端官方 FishLogo 路径，多尺寸 ico）

## 图标来源与再生成

托盘/程序图标取自 **dsh 前端官方品牌组件 `FishLogo`**（侧边栏品牌鲸鱼 mark），黑色单色渲染：

- SVG path 与 viewBox 提取自 `node_modules\@deepseek-ai\dsh-web-frontend\dist\assets\index-*.js`（`FISH_LOGO_PATH` / `FISH_LOGO_VIEWBOX`，viewBox 为 `0 0 23.16 17.04`；命令集仅 M/C/L/Z）
- `assets\make-icon.ps1` 解析 path → `System.Drawing.GraphicsPath` → 1024px 超采样 → 缩放出 16/20/24/32/48/64/128/256 八尺寸 → 组装 PNG-ICO（`DshTray\deepseek.ico`，经 csproj 嵌入 exe 与程序资源）
- 再生成：`.\assets\make-icon.ps1`（输入 `assets\whale-path.txt`）

## dsh 设置（监听 host / 端口 / 开机自启）

右键菜单 → `dsh 设置…`：

- **监听地址 (host)**：`127.0.0.1`（仅本机可访问，默认）或 `0.0.0.0`（局域网可访问）
  - ⚠️ `0.0.0.0` 会向局域网开放 dsh Web 界面（dsh 自动把本机 LAN IP 加入信任列表），仅建议在可信网络使用
  - 注：dsh 命令行本身拒绝 `--host 0.0.0.0`（安全限制），本工具通过 `--patch` 覆盖层配置实现该能力
- **监听端口**：1-65535（默认 3080）
- **开机自动启动**：勾选后写入当前用户启动项（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，无需管理员权限），登录 Windows 后自动驻留托盘；取消勾选即移除

保存后设置写入 `config.json`，并生成 `dsh-overlay.yml`（exe 同目录，仅含 webserver 行的 host/port 覆盖）。托盘启动 dsh 时以 `--profile web --patch <overlay>` 加载；dsh 正在运行时若 host/port 有改动会询问是否立即重启生效（开机自启改动即时生效）。也可以手写 `config.json`：

```json
{
  "host": "127.0.0.1",
  "port": 39123,
  "autoStart": true
}
```

> 注：每台电脑同一时刻只应有一个 dsh web 实例；改端口后浏览器请通过托盘「打开界面」访问。

## 使用

1. 将 `dist\win-x64\`（或自构建输出）整个文件夹放到任意位置。
2. 双击 `DshTray.exe`（单实例：重复双击只保留一个托盘图标，不会多开）。
3. 开机自启：右键菜单 → `dsh 设置…` 勾选「开机自动启动」即可，无需其他操作；也可手动把 `DshTray.exe` 快捷方式放入 `shell:startup`。

## 端口与进程识别

程序通过 **TCP 端口探测**识别 dsh（默认 3080），不依赖进程命令行：

1. `GetExtendedTcpTable` 枚举监听/连接行（AF_INET + AF_INET6）；
2. 兜底解析 `netstat -ano` 输出。

停止时先校验目标进程确为 node/dsh/deno/bun，防止误杀同端口上的其他程序。

## 配置 config.json

所有字段可选，省略时自动探测（推荐直接省略，仅自定义地址/端口时才需要）：

```json
{
  "url": "http://127.0.0.1:3080",
  "node": "C:\\nvm4w\\nodejs\\node.exe",
  "dshEntry": "C:\\Users\\1\\AppData\\Local\\npm-cache\\_npx\\xxxxxxxx\\node_modules\\@deepseek-ai\\dsh\\lib\\bin.js",
  "dshArgs": "web"
}
```

| 字段 | 默认 | 说明 |
|---|---|---|
| `url` | `http://127.0.0.1:3080` | 界面地址；未显式配置 host/port 时决定浏览器打开地址与识别端口 |
| `host` | `127.0.0.1` | dsh 监听地址，仅允许 `127.0.0.1` / `0.0.0.0` |
| `port` | `3080` | dsh 监听端口 1-65535（优先于 url 中的端口） |
| `node` | 自动探测（PATH → 常见安装位置） | node.exe 路径 |
| `dshEntry` | 自动探测（PATH 的 dsh.cmd → npx 缓存） | `@deepseek-ai/dsh/lib/bin.js` 入口 |
| `dshArgs` | `web` | 启动参数；显式设置 host/port 时改用 `--patch` 覆盖层 |
| `autoStart` | 未设置 | 开机自启：`true` 写入启动项 / `false` 注销（未设置不干预，启动时自动同步注册表） |

> 本机默认路径仅供参考；`dshEntry` 建议用 `where dsh` 找到 `dsh.cmd` 后推出，或直接省略自动探测。

## 构建

要求：.NET 8 SDK（构建机），运行机需 .NET 8 桌面运行时（framework-dependent 默认；`-SelfContained` 可打包含运行时的完整 exe）。

```powershell
# 默认：framework-dependent 单文件（约 180KB，运行机需 .NET 8 / 9 / 10 桌面运行时）
.\build.ps1

# 自包含单文件（约 70MB，任何 Win10/11 机器可跑；需联网下载 runtime pack）
.\build.ps1 -SelfContained
```

输出：`dist\win-x64\DshTray.exe`。

构建脚本已做离线兼容（本机 targeting packs 随 SDK 安装时无需联网；单文件发布关闭了分析器/运行时包下载声明）。

## 故障排查

- **dsh 启动失败/超时**：查看 `dsh-tray.log` 与 dsh 自身日志（`C:\Users\1\.dsh\logs` 或 `%USERPROFILE%\.dsh`）。
- **检测不到运行中的 dsh**：本机默认端口 3080；若改了端口，请在 `config.json` 的 `url` 中同步。
- **关闭 dsh 被拒**：程序会校验端口进程类型（node/deno/bun/dsh），若 3080 端口被其他程序占用会拒绝操作以避免误杀。
- **“dsh 已在运行”但仍打不开界面**：浏览器直接访问 `url` 确认服务是否正常（返回 401 属正常，说明服务在等待登录）。

## 技术实现

- C# / .NET 8 WinForms（`NotifyIcon` + `ContextMenuStrip`），`P/Invoke GetExtendedTcpTable` + `netstat` 双路端口检测
- 进程树终止（`Process.Kill(entireProcessTree: true)`）
- 启动命令解析：`node.exe "<bin.js>" web`（`CreateNoWindow`、独立于本进程）
- 单实例互斥（`Global\DshTray.SingleInstance.b7c1`）
- 托盘图标为运行时绘制（渐变圆底 + “D”），无外部资源

源码结构：

```
DshTray/
  DshTray.csproj          项目文件（net8.0-windows / WinForms / 单文件发布）
  Program.cs              入口与单实例互斥
  TrayApplicationContext.cs  托盘图标、右键菜单、操作编排
  DshManager.cs           端口检测、启动/停止/重启、等待就绪、命令解析
  Config.cs               可选 config.json 配置
  IconFactory.cs          运行时绘制托盘图标
  Log.cs                  文件日志
build.ps1                 一键发布脚本
dist\win-x64\DshTray.exe  构建产物（含 config.json 模板）
```
