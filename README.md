# WFAM · Windows Folder Alias Manager

一个用于 **批量管理 Windows 文件夹别名、图标和驱动器显示信息** 的桌面工具。  
基于 **.NET 10 + WPF + WPF-UI** 开发，支持文件夹 `desktop.ini` 编辑、U 盘 / 驱动器 `autorun.inf` 定制，以及更多资源管理器外观相关功能。

> This project is forked and extended from `thx114/folder-alias-manager` (MIT License).

[简体中文](#简体中文) | [English](#english)

---

## 简体中文

WFAM 是一个面向 Windows 的文件夹外观管理工具，适合用来统一整理常用目录、项目目录、资源目录或移动存储设备的显示名称与图标。

### 主要功能

- **文件夹别名与图标管理**
  - 批量导入文件夹
  - 编辑文件夹别名与图标
  - 支持拖放添加
  - 支持从 `.ico` / `.exe` / `.dll` 提取图标
  - 支持恢复默认设置

- **文件夹背景图**
  - 支持写入 `desktop.ini` 中的背景图字段
  - 可为文件夹设置或清除背景图片路径

- **U 盘 / 驱动器定制**
  - 读取 / 写入驱动器根目录 `autorun.inf`
  - 自定义驱动器显示名称与图标
  - 支持恢复默认状态

- **文件夹伪装**
  - 可将普通文件夹伪装为系统对象
  - 支持常见 CLSID 预设
  - 支持恢复普通文件夹状态

- **历史记录与恢复**
  - 自动记录修改历史
  - 支持查看、删除、清空和一键恢复

- **系统集成**
  - 资源管理器右键菜单集成
  - 受保护目录自动走 UAC 提权写入
  - 自动检查 GitHub Releases 更新

- **Explorer 背景扩展**
  - 提供额外的 Explorer 背景扩展组件
  - 相关实现使用了第三方项目 `TsudaKageyu/minhook` 的技术方案
  - 具体第三方许可与归属请以对应项目仓库声明为准

- **现代化界面**
  - 支持浅色 / 深色 / 高对比度 / 跟随系统
  - 支持简体中文 / English 即时切换

### 项目结构

```text
src/
├─ WFAM.App/         主程序（WPF）
├─ WFAM.Helper/      提权辅助进程
├─ WFAM.ExplorerBg/  Explorer 背景扩展
└─ WFAM.BgHost/      Explorer 背景扩展宿主
```

### 技术栈

- .NET 10
- WPF
- WPF-UI
- CommunityToolkit.Mvvm
- MinHook（用于 Explorer 背景扩展相关实现）

### 致谢与声明

- 本项目基于 `thx114/folder-alias-manager` 二次开发，原项目使用 **MIT License**
- `WFAM.ExplorerBg` 相关能力使用了 `TsudaKageyu/minhook` 的技术方案；相关第三方代码与许可归原作者所有

### 构建

```powershell
git clone https://github.com/DusklitSakura/folder-alias-manager.git
cd folder-alias-manager
dotnet build .\WFAM.sln -c Debug
dotnet run --project .\src\WFAM.App\WFAM.App.csproj
```

### 许可证

本项目以 **MIT License** 发布。  
第三方依赖与组件的版权和许可证归各自作者所有。

---

## English

WFAM is a Windows desktop tool for **batch-managing folder aliases, icons, and drive display metadata**.  
It helps customize how folders and removable drives appear in Explorer, with support for `desktop.ini`, `autorun.inf`, folder disguise, history restore, and more.

### Features

- **Folder alias and icon management**
  - Batch import folders
  - Edit folder aliases and icons
  - Drag-and-drop support
  - Extract icons from `.ico`, `.exe`, and `.dll`
  - Restore defaults

- **Folder background image**
  - Write background image metadata into `desktop.ini`
  - Set or clear folder background image paths

- **USB / drive customization**
  - Read and write root `autorun.inf`
  - Customize drive label and icon
  - Restore defaults

- **Folder disguise**
  - Disguise folders as Windows shell objects
  - Built-in CLSID presets
  - Restore normal folder state

- **History and restore**
  - Track recent changes automatically
  - View, remove, clear, and restore history entries

- **System integration**
  - Explorer context menu integration
  - UAC elevation for protected locations
  - GitHub Releases auto-update

- **Explorer background extension**
  - Includes an Explorer background extension component
  - Its implementation uses techniques based on the third-party project `TsudaKageyu/minhook`
  - Please refer to the corresponding upstream project for its license and attribution terms

- **Modern UI**
  - Light / Dark / High Contrast / System theme
  - Live language switching between Simplified Chinese and English

### Project structure

```text
src/
├─ WFAM.App/         Main WPF application
├─ WFAM.Helper/      Elevated helper process
├─ WFAM.ExplorerBg/  Explorer background extension
└─ WFAM.BgHost/      Host for Explorer background extension
```

### Tech stack

- .NET 10
- WPF
- WPF-UI
- CommunityToolkit.Mvvm
- MinHook (used by the Explorer background extension related implementation)

### Credits and attribution

- This project is forked and extended from `thx114/folder-alias-manager`, which is published under the **MIT License**
- `WFAM.ExplorerBg` uses technology from `TsudaKageyu/minhook`; copyrights and license terms for third-party code remain with their respective authors

### Build

```powershell
git clone https://github.com/DusklitSakura/folder-alias-manager.git
cd folder-alias-manager
dotnet build .\WFAM.sln -c Debug
dotnet run --project .\src\WFAM.App\WFAM.App.csproj
```

### License

This project is released under the **MIT License**.  
Third-party dependencies and components remain under their own respective licenses.
