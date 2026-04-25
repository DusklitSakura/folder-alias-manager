// WFAM.ExplorerBg - Explorer 顶层窗口 → 当前路径 同步
#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

namespace wfam {

// 启动后台轮询线程；多次调用安全（只启动一次）。
void StartPathSync();
void StopPathSync();

// 给定 explorer 内任意子窗口 hwnd，向上找祖先级 CabinetWClass / ShellTabWindowClass
// 然后查路径；找不到返回空串。
std::wstring QueryFolderPathForChild(HWND childHwnd);

// 直接对某个 DUI/子 hwnd 通过 SHELLDLL_DefView + WM_GETOBJECT(OBJID_NATIVEOM)
// 同步取当前 tab 的真实路径。可处理 Win11 多 tab 中 IShellWindows 列不到的情形。
std::wstring ResolvePathFromDui(HWND duiHwnd);

} // namespace wfam
