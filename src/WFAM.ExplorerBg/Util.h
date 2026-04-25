// WFAM.ExplorerBg - 工具函数声明
#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objidl.h>     // IStream（gdiplus 依赖，但 LEAN_AND_MEAN 不会自动带入）
#include <gdiplus.h>
#include <string>

namespace wfam {

// 取当前 DLL 所在目录（不带尾斜杠）。
std::wstring GetCurDllDir();

// 取窗口类名。
std::wstring GetWindowClassName(HWND hWnd);

// 在 ini 中读字符串；找不到返回空串。
std::wstring GetIniString(const std::wstring& iniPath, const std::wstring& section, const std::wstring& key);

// 文件是否存在（普通文件，不管隐藏属性）。
bool FileExists(const std::wstring& path);

// 把相对路径基于 baseDir 解析为绝对路径；本身已是绝对路径则原样返回。
std::wstring ResolveRelative(const std::wstring& baseDir, const std::wstring& maybeRelative);

// 用文件流方式从磁盘加载位图，避免独占文件。返回 nullptr 表示失败；调用方负责 delete。
class GdiBitmap {
public:
    explicit GdiBitmap(const std::wstring& path);
    ~GdiBitmap();

    HDC     memDC = nullptr;
    HBITMAP bmp   = nullptr;
    SIZE    size  = { 0, 0 };
    Gdiplus::Bitmap* src = nullptr;

    bool ok() const { return memDC && bmp && src; }
};

void Log(const wchar_t* msg);

} // namespace wfam
