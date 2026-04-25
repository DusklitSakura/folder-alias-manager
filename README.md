# WFAM · Windows Folder Alias Manager

Fork for thx114's https://github.com/thx114/folder-alias-manager

二次修改于 thx114 的https://github.com/thx114/folder-alias-manager

[简体中文](#简体中文) | [English](#english)

---

## 简体中文

WFAM 是一款基于 .NET 10 + WPF / WPF-UI 的现代化 Windows 桌面工具，用于**批量管理文件夹的本地化别名（`LocalizedResourceName`）和自定义图标（`IconResource`）**，并支持为 U 盘 / 移动硬盘写入 `autorun.inf` 自定义显示名与图标。

### 主要特性

- **文件夹别名 & 图标管理**
  - 拖放或导入任意文件夹，批量编辑别名 + 图标
  - 自动写入 `desktop.ini`（`+h+s`）并设置 `+r` 让资源管理器立即识别
  - 受保护目录（如 `Program Files`）自动通过 UAC 提权，由独立的 `DesktopIniHelper.exe` 完成写入
  - 资源管理器右键菜单一键集成（"使用 WFAM 管理别名"）
  - 历史记录 + 一键还原

- **U 盘 / 驱动器 autorun.inf 自定义**
  - 为可移动盘 / 移动硬盘写入 `autorun.inf`，自定义在资源管理器中显示的名称与图标
  - 图标支持 `.ico` / `.exe` / `.dll`（自动从 PE 资源中提取并合成 BMP/DIB 编码的 .ico）
  - 同样走 UAC 提权流水线，文件名严格白名单校验（`SecurityGuard`）

- **现代化界面**
  - WPF-UI 4.x · FluentWindow · NavigationView · Card · Snackbar
  - 浅色 / 深色 / 高对比度 / 跟随系统 主题
  - 简体中文 / English 即时切换（自定义 `{loc:Tr Key}` 标记扩展）

- **自动更新**
  - 启动时可选自动检查 GitHub Release（`DusklitSakura/folder-alias-manager`）
  - 发现新版弹出对话框，含 release notes，三个选项：立即更新 / 稍后更新 / 忽略此版本
  - "立即更新" 自动下载 zip → 退出当前进程 → 由 PowerShell 脚本覆盖文件并重启

### 项目结构

```
WFAM/
├─ Directory.Build.props          统一版本号（<Version> / <AssemblyVersion>）
├─ WFAM.sln
└─ src/
   ├─ WFAM.App/                   主程序（WPF, AssemblyName=WFAM）
   │  ├─ Helpers/                 转换器、TranslateExtension、DragDrop、NativeMethods
   │  ├─ Models/                  AppSettings、HistoryEntry、UpdateInfo …
   │  ├─ Services/                业务服务层（DI 注入）
   │  ├─ ViewModels/              MVVM
   │  └─ Views/                   主窗口 + Pages（Folders / UsbDrives / History / Settings / About）
   └─ WFAM.Helper/                提权小程序（AssemblyName=DesktopIniHelper）
      ├─ Program.cs               接收主程序通过 JSON IPC 发来的请求
      ├─ DesktopIniWriter.cs      写 desktop.ini
      ├─ AutorunInfWriter.cs      写 autorun.inf + 复制 .ico 到盘符根
      └─ SecurityGuard.cs         路径 / 文件名白名单校验
```

### 技术栈

- .NET 10（target `net10.0-windows10.0.19041.0`）
- WPF + WPF-UI 4.2.1
- CommunityToolkit.Mvvm 8.3.2
- Microsoft.Extensions.Hosting / DependencyInjection / Logging
- 双进程提权架构：主程序 → `ShellExecuteEx + "runas"` → DesktopIniHelper.exe

### 构建与运行

需要：**.NET 10 SDK**（含 Windows Desktop 工作负载）和 Windows 10/11。

```powershell
git clone https://github.com/DusklitSakura/folder-alias-manager.git
cd folder-alias-manager
dotnet build .\WFAM.sln -c Debug
dotnet run --project .\src\WFAM.App\WFAM.App.csproj
```

### 打包发布

| 文件 | 说明 | 大小 |
| --- | --- | --- |
| `WFAM-<ver>-win-x64.zip` | 依赖 .NET 10 桌面运行时 | ~9 MB |
| `WFAM-<ver>-win-x64-self-contained.zip` | 内置运行时，开箱即用 | ~70 MB |

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

### 版本号管理

集中在 [`Directory.Build.props`](Directory.Build.props) 的 `<Version>` / `<AssemblyVersion>` / `<FileVersion>`。发版步骤：

1. 修改 `Directory.Build.props` 中的版本号
2. `git tag v1.0.1 && git push --tags`
3. 在 GitHub 创建 Release，标题用同一 tag，附上 `WFAM-1.0.1-win-x64.zip` 等资产
4. 应用内的"自动更新"会自动比对 tag 并提示用户

### 安全性

- 提权进程接收的 JSON 请求通过严格的白名单校验：路径必须以 `\\?\` 形式存在、盘符根判定、文件名 regex `^[A-Za-z0-9_\-]{1,32}\.ico$` 等
- IPC 文件名 regex 限定（`wfam_in_<pid>_<32hex>.json`），避免被劫持

### 许可证

本项目以 MIT 许可证发布。

---

## English

**WFAM** (Windows Folder Alias Manager) is a modern .NET 10 / WPF · WPF-UI desktop tool for **batch-managing Windows folder localized names (`LocalizedResourceName`) and custom icons (`IconResource`)**. It also supports writing `autorun.inf` to USB / removable drives so you can customize the label and icon Explorer shows for them.

### Features

- **Folder alias & icon management**
  - Drag-and-drop or import folders; edit alias + icon in batch
  - Writes `desktop.ini` (with `+h+s` attributes) and sets `+r` on the parent folder so Explorer picks the change up immediately
  - Protected directories (e.g. `Program Files`) are written through a separate UAC-elevated `DesktopIniHelper.exe`
  - One-click Explorer context-menu integration ("Manage aliases with WFAM")
  - Per-change history with one-click revert

- **USB / drive autorun.inf customization**
  - Write `autorun.inf` to removable / fixed drives to customize the name & icon shown in Explorer
  - Icon source can be `.ico`, `.exe`, or `.dll` (automatically extracted from PE resources and re-encoded as a BMP/DIB-based .ico)
  - Same UAC pipeline; strict whitelist validation by `SecurityGuard`

- **Modern UI**
  - WPF-UI 4.x · FluentWindow · NavigationView · Card · Snackbar
  - Light / Dark / High Contrast / System theme
  - Live-switch between Simplified Chinese and English via a custom `{loc:Tr Key}` markup extension

- **Auto-update**
  - Optional startup check against GitHub Releases (`DusklitSakura/folder-alias-manager`)
  - When a new version is found, a dialog shows release notes with three actions: **Update now**, **Remind me later**, **Skip this version**
  - "Update now" downloads the zip, exits the running process, and a PowerShell script overwrites the install folder and relaunches the app

### Project layout

```
WFAM/
├─ Directory.Build.props          Centralised version (<Version> / <AssemblyVersion>)
├─ WFAM.sln
└─ src/
   ├─ WFAM.App/                   Main app (WPF, AssemblyName=WFAM)
   │  ├─ Helpers/                 Converters, TranslateExtension, DragDrop, NativeMethods
   │  ├─ Models/                  AppSettings, HistoryEntry, UpdateInfo …
   │  ├─ Services/                Business services (DI)
   │  ├─ ViewModels/              MVVM
   │  └─ Views/                   Main window + pages (Folders / UsbDrives / History / Settings / About)
   └─ WFAM.Helper/                Elevated helper (AssemblyName=DesktopIniHelper)
      ├─ Program.cs               JSON-IPC entry point
      ├─ DesktopIniWriter.cs      Writes desktop.ini
      ├─ AutorunInfWriter.cs      Writes autorun.inf + copies .ico to drive root
      └─ SecurityGuard.cs         Path / filename whitelist validation
```

### Tech stack

- .NET 10 (`net10.0-windows10.0.19041.0`)
- WPF + WPF-UI 4.2.1
- CommunityToolkit.Mvvm 8.3.2
- Microsoft.Extensions.Hosting / DependencyInjection / Logging
- Two-process elevation: main app → `ShellExecuteEx + "runas"` → `DesktopIniHelper.exe`

### Build & run

Requires **.NET 10 SDK** (with the Windows Desktop workload) and Windows 10 / 11.

```powershell
git clone https://github.com/DusklitSakura/folder-alias-manager.git
cd folder-alias-manager
dotnet build .\WFAM.sln -c Debug
dotnet run --project .\src\WFAM.App\WFAM.App.csproj
```

### Packaging

| File | Description | Size |
| --- | --- | --- |
| `WFAM-<ver>-win-x64.zip` | Framework-dependent (needs the .NET 10 Desktop Runtime) | ~9 MB |
| `WFAM-<ver>-win-x64-self-contained.zip` | Self-contained, runtime included | ~70 MB |


### Versioning

Centralised in [`Directory.Build.props`](Directory.Build.props) (`<Version>` / `<AssemblyVersion>` / `<FileVersion>`). To ship a new release:

1. Bump the version in `Directory.Build.props`
2. `git tag v1.0.1 && git push --tags`
3. Create a GitHub Release using the same tag and attach `WFAM-1.0.1-win-x64.zip` etc.
4. The in-app auto-updater compares against this tag and prompts users automatically

### Security

- Every JSON request the elevated helper receives is whitelist-validated: paths normalized through `\\?\`, drive-root checks, filename regex `^[A-Za-z0-9_\-]{1,32}\.ico$`, etc.
- IPC files are constrained by a regex name pattern (`wfam_in_<pid>_<32hex>.json`) to prevent hijacking

### License

Released under the MIT License.
