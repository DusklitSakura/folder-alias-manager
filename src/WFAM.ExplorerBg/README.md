# WFAM.ExplorerBg

WFAM 的文件资源管理器背景扩展。在 explorer.exe 进程内合法注册为 BHO（Browser Helper Object），用 MinHook 内联 hook GDI 绘制函数，按当前文件夹的 `desktop.ini` 中 `[{BE098140-A513-11D0-A3A4-00C04FD706EC}] IconArea_Image=` 字段绘制背景图。

## 构建前置

1. **VS C++ 工作负载**：Visual Studio 2022 + "使用 C++ 的桌面开发" + Windows 10/11 SDK。
2. **MinHook 源码**（必须，未随仓库提交）：

   ```powershell
   cd src\WFAM.ExplorerBg
   git clone --depth 1 https://github.com/TsudaKageyu/minhook.git third_party\minhook
   ```

   完成后应存在 `third_party\minhook\include\MinHook.h` 与 `third_party\minhook\src\hook.c` 等。

## 构建

```powershell
msbuild src\WFAM.ExplorerBg\WFAM.ExplorerBg.vcxproj /p:Configuration=Release /p:Platform=x64
```

输出位置：`src\WFAM.App\bin\Release\net10.0-windows10.0.19041.0\WFAM.ExplorerBg.dll`（与主程序同目录，便于 WFAM.App 直接定位）。

## 注册 / 卸载

注册（需管理员）：

```powershell
regsvr32 /s "<WFAM 安装目录>\WFAM.ExplorerBg.dll"
```

卸载：

```powershell
regsvr32 /u /s "<WFAM 安装目录>\WFAM.ExplorerBg.dll"
```

WFAM.App 会通过 WFAM.Helper 自动调用以上两个命令，无需手动执行。

## 工作原理

- COM 注册：`HKCR\CLSID\{D5C5F9A4-...}\InProcServer32` + `HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects\{D5C5F9A4-...}`（含 `NoExplorer=1`）。
- Explorer 创建新选项卡时按 BHO 协议加载本 DLL；DllMain 中 `ShouldLoad` 限制只有 `explorer.exe` 与 `regsvr32.exe` 才返回 TRUE。
- `IObjectWithSite::SetSite` 收到 `IWebBrowser2`，监听 `DISPID_DOCUMENTCOMPLETE` 拿到当前文件夹路径。
- `MH_Initialize` 后 hook 5 个 user32/gdi32 函数：`CreateWindowExW / DestroyWindow / BeginPaint / FillRect / CreateCompatibleDC`。
- `MyCreateWindowExW` 识别类名为 `DirectUIHWND`、父级 `SHELLDLL_DefView`、祖父 `ShellTabWindowClass`/`#32770`/`CabinetWClass` 的窗口并登记。
- `MyFillRect` 中先调原 `FillRect`，再读当前文件夹的 `desktop.ini`，加载图片缓存后用 `AlphaBlend` 叠加到目标 DC。

## 注意

- 仅 x64。32 位 Explorer 不支持。
- 修改 `desktop.ini` 后需要切换文件夹（或刷新）才会重新读图。
- 卸载 DLL 后老的 explorer.exe 进程仍在内存中保留 hook，重启 explorer 或注销/登录即可彻底卸载。
