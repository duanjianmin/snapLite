# 极简录屏 snapLite

一个简洁的 Windows 截图和录屏工具，基于 [ShareX](https://github.com/ShareX/ShareX) 精简修改而来：只保留截图、滚动截图和屏幕录制（含摄像头画面），去掉了上传、工作流、工具、历史等其他功能。

> **非官方项目**：极简录屏（snapLite）是社区维护的 ShareX 修改版，与 ShareX 官方团队无关，也未获得其认可。遇到问题请在本仓库提 Issue，不要提交到 ShareX 官方。

*English: snapLite is an unofficial, minimal fork of [ShareX](https://github.com/ShareX/ShareX) for Windows that keeps only screenshots, scrolling capture and screen recording with a camera overlay. It is not affiliated with or endorsed by the ShareX team. Licensed under GPL v3.*

## 功能

- **截图**：区域截图、全屏截图。保存为 PNG，同时复制到剪贴板，右下角弹出带缩略图的通知（通知本身不会被截进下一张截图或录像）。
  - 区域截图：框选后可以拖动调整选区、用顶部工具栏添加标注（矩形、箭头、文字、马赛克等），然后点选区旁的"确定"保存；也可以按回车或双击选区。点"取消"或按 Esc 放弃。
  - 想要松开鼠标就立即保存，可以在设置里勾选"松开鼠标后立即保存区域截图"。
- **滚动截图**：选择一个窗口、控件或框选一个区域，程序自动向下滚动并拼接成一张长图，可以保存或复制。
  - 滚动时鼠标会临时移到区域中央，结束后放回原处。
  - 每步滚动距离按页面自动调整，并等画面停稳再截，适合浏览器里的平滑滚动页面。
  - 从页面当前位置开始往下截，截之前请先滚到想要的起点。
- **屏幕录制**：MP4（H.264 + AAC），使用 Windows Graphics Capture 和 Media Foundation，支持硬件编码。
  - 录制前的工具栏可以设置系统声音、麦克风、摄像头、光标、鼠标高亮、开始倒计时、帧率、码率和时长。
  - 摄像头画面：录制前可实时预览；形状可选矩形、圆形、圆角矩形、正方形、椭圆；可设置位置和大小。
  - 录制中屏幕上会显示可拖动的摄像头画面，视频里的摄像头跟着移动；这个窗口本身不会被录进视频。
  - 视频第一帧就带摄像头画面（录制会等摄像头准备好，最多 3 秒）。
  - 没有摄像头时，摄像头按钮变灰，照常录屏。
  - 录制时长没有限制，只受磁盘空间影响（默认码率下每小时约 4.5 GB）。
- **托盘菜单**：左键或右键单击托盘图标弹出菜单，每项后面显示快捷键。再次启动程序会打开设置窗口。
- **多语言**：界面支持 ShareX 的全部 28 种语言，跟随系统语言。

### 默认快捷键

| 操作 | 快捷键 |
|---|---|
| 区域截图 | Print Screen |
| 全屏截图 | Ctrl + Print Screen |
| 滚动截图 | Ctrl + Alt + Print Screen |
| 开始/停止录屏 | Shift + Print Screen |

沿用 Windows、Greenshot、Lightshot、ShareX 的 Print Screen 系列，不和微信（Alt + A）、QQ（Ctrl + Alt + A）、钉钉/飞书（Ctrl + Shift + A）的截图快捷键冲突。可以在设置中修改；被其他程序占用的快捷键会在设置里提示。

> Windows 11 默认用 Print Screen 打开系统的截图工具。如果按 Print Screen 没反应，请在"设置 → 辅助功能 → 键盘"里关闭"使用 Print Screen 键打开屏幕截图"；如果系统的"截图工具"正在运行，它也可能占用 Print Screen，关掉它即可。也可以在极简录屏的设置里换一个快捷键，或从托盘菜单开始截图。

## 下载和使用

从 [Releases](../../releases) 下载 zip，解压后运行 `snapLite.exe`。程序自带 .NET 运行时，**不需要安装 .NET 或其他依赖**。

系统要求：

- 64 位 Windows 11。项目按 Windows 11 22H2 的接口编译，Windows 10 尚未测试。
- 录屏需要 Windows 自带的 Media Foundation。Windows 的 N/KN 版本没有它，需要先在"设置 → 应用 → 可选功能"里安装"媒体功能包"；截图不受影响。
- 使用摄像头和麦克风，需要在"设置 → 隐私和安全性"里允许桌面应用访问。
- 程序没有代码签名，第一次运行时 Windows SmartScreen 可能提示"未知发布者"，点"更多信息 → 仍要运行"即可。

## 数据和隐私

- 设置和日志：`%APPDATA%\snapLite\`（`Settings.json`、`Logs\`）。
- 截图和录像：默认保存在 `图片\snapLite\年-月\`，可在设置中修改。
- 没有上传、自动更新或使用统计功能，截图和录像只保存在本机。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。在仓库根目录执行：

```powershell
dotnet publish snapLite\snapLite.csproj -c Release -r win-x64 --self-contained true -o publish\snapLite
```

`publish\snapLite` 就是可以直接分发的完整程序（约 140 MB，压缩成 zip 约 55 MB），许可证文件会一起复制进去。

如果希望包更小、由用户自己安装运行时，把 `--self-contained true` 改成 `false`（约 60 MB），用户需要先安装 .NET 10 Runtime（x64）。

如果构建时报 "being used by another process"（文件被杀毒软件或 IDE 占用），重试时加上 `--no-incremental`，否则 Avalonia 的 XAML 预编译可能被跳过，运行时会报 "No precompiled XAML found"。

## 项目结构

| 目录 | 说明 |
|---|---|
| `snapLite/` | 应用本身：托盘、快捷键、截图和录制流程、设置窗口 |
| `snapLite/Recording/` | 录制控制、录制前工具栏、摄像头预览和录制中可拖动的摄像头窗口 |
| `snapLite/MouseHighlighter/` | 鼠标高亮，移植自 ShareX.Tools |
| `snapLite/Localization/` | 本程序用到的界面文字，28 种语言 |
| `ShareX.ScreenRecordingLib/` | 原生录制器，包括摄像头画面、形状和拖动 |
| `ShareX.ScreenCaptureLib/` | 区域选择、截图、滚动截图、录制边框 |
| `ShareX.ImageEditor/` | 区域截图后的标注 |
| `ShareX.HelpersLib/`、`ShareX.Avalonia/` | 公共工具和界面主题 |

## 与 ShareX 的关系和修改说明

本项目基于 ShareX `develop` 分支的提交 [`254870479`](https://github.com/ShareX/ShareX/commit/254870479)（2026-10-23，即 22.0.0 预发布版之后的 18 个提交），于 2026 年 10 月修改而来。主要修改：

- **名称和图标**：改名为"极简录屏"（英文 snapLite），换用新的图标；界面、窗口标题和提示里不再显示 ShareX 的名称。
- **新写的应用**：`snapLite` 替换了 ShareX 原来的主程序，只包含托盘、快捷键、截图、录屏和设置。
- **去掉的功能**：上传和上传目标、工作流和截图后任务、工具（OCR、二维码、贴图等）、历史记录、图像效果、AI、监视文件夹、自动截图、自动更新、FFmpeg 录制、浏览器扩展和命令行等。只保留了 5 个需要的库，其中 `ShareX.ImageEditor` 删除了未使用的图像效果代码。
- **新增和修改的功能**：
  - 摄像头画面：多种形状、录制前实时预览、录制中可拖动、视频第一帧就带摄像头、没有摄像头时照常录屏。
  - 滚动截图：鼠标自动移到区域中央、等画面停稳再截、按页面自动调整每步滚动距离，适配浏览器平滑滚动的页面。
  - 托盘菜单：左右键弹出同一个菜单，显示快捷键，悬停时手形光标。
  - 默认快捷键改为 Print Screen 系列。
  - 区域截图默认框选后不立即保存，选区旁显示"取消 / 确定"按钮，标注时保留选区显示。
  - 截图通知窗口不会出现在之后的截图和录像里。
  - 只运行在 Windows 上，不再依赖 WPF/WinForms 运行时，自带运行时的发布包从约 233 MB 减到约 140 MB。

各源文件头部保留了 ShareX 的版权和许可声明。

## 已知问题

- 本项目基于 ShareX 22.0 的预发布代码，ShareX 22.0 本身也还在预发布阶段，难免还有未发现的问题。遇到问题欢迎提 Issue。
- 目前主要在 Windows 11、双显示器上测试。Windows 10、三块及以上显示器、不同缩放比例的显示器混用、没有独立显卡（只用软件编码）的电脑尚未充分测试。ShareX 22.0 有用户报告在多显示器配置下区域截图异常（[#8908](https://github.com/ShareX/ShareX/issues/8908)、[#8902](https://github.com/ShareX/ShareX/issues/8902)），本项目可能同样受影响。
- 标注工具栏固定在屏幕顶部，不能拖动；标注编辑器裁剪时会自动去掉白边（与 ShareX 22.0 相同，[#8904](https://github.com/ShareX/ShareX/issues/8904)）。

## 许可

与 ShareX 相同，使用 [GNU General Public License v3](LICENSE.txt)。你可以自由使用、修改和再发布，包括商用；分发修改版时也必须以 GPL v3 开源并提供源码。

ShareX 的版权归 ShareX Team 所有。第三方组件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 和 `Licenses` 目录。
