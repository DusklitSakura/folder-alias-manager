// WFAM.ExplorerBg - 工具函数实现
#include "Util.h"

#include <shlwapi.h>
#include <gdiplus.h>
#include <cstdio>
#include <vector>

extern HMODULE g_hModule;

namespace wfam {

void Log(const wchar_t* msg) {
    OutputDebugStringW(L"[WFAM.ExplorerBg] ");
    OutputDebugStringW(msg);
    OutputDebugStringW(L"\n");
}

std::wstring GetCurDllDir() {
    wchar_t buf[MAX_PATH] = {};
    GetModuleFileNameW(g_hModule, buf, MAX_PATH);
    std::wstring p(buf);
    auto pos = p.find_last_of(L'\\');
    return (pos == std::wstring::npos) ? std::wstring() : p.substr(0, pos);
}

std::wstring GetWindowClassName(HWND hWnd) {
    wchar_t buf[256] = {};
    int n = GetClassNameW(hWnd, buf, 256);
    return std::wstring(buf, (n > 0) ? n : 0);
}

std::wstring GetIniString(const std::wstring& iniPath, const std::wstring& section, const std::wstring& key) {
    wchar_t buf[2048] = {};
    DWORD n = GetPrivateProfileStringW(section.c_str(), key.c_str(), L"", buf, 2048, iniPath.c_str());
    return std::wstring(buf, n);
}

bool FileExists(const std::wstring& path) {
    DWORD a = GetFileAttributesW(path.c_str());
    return (a != INVALID_FILE_ATTRIBUTES) && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

std::wstring ResolveRelative(const std::wstring& baseDir, const std::wstring& maybeRelative) {
    if (maybeRelative.empty()) return {};
    // 已是绝对路径
    if (maybeRelative.size() >= 2 && maybeRelative[1] == L':') return maybeRelative;
    if (maybeRelative.size() >= 2 && maybeRelative[0] == L'\\' && maybeRelative[1] == L'\\') return maybeRelative;

    std::wstring combined = baseDir;
    if (!combined.empty() && combined.back() != L'\\') combined.push_back(L'\\');
    combined += maybeRelative;

    wchar_t full[MAX_PATH] = {};
    if (PathCanonicalizeW(full, combined.c_str())) return full;
    return combined;
}

// ---- GdiBitmap ----
GdiBitmap::GdiBitmap(const std::wstring& path) {
    // 用内存流加载，避免独占文件
    FILE* fp = nullptr;
    if (_wfopen_s(&fp, path.c_str(), L"rb") != 0 || !fp) return;

    fseek(fp, 0, SEEK_END);
    long len = ftell(fp);
    rewind(fp);
    if (len <= 0) { fclose(fp); return; }

    std::vector<BYTE> data(static_cast<size_t>(len));
    fread(data.data(), 1, static_cast<size_t>(len), fp);
    fclose(fp);

    IStream* stream = SHCreateMemStream(data.data(), static_cast<UINT>(len));
    if (!stream) return;

    src = Gdiplus::Bitmap::FromStream(stream);
    stream->Release();
    if (!src || src->GetLastStatus() != Gdiplus::Ok) {
        delete src; src = nullptr;
        return;
    }

    memDC = CreateCompatibleDC(nullptr);
    size = { (LONG)src->GetWidth(), (LONG)src->GetHeight() };
    src->GetHBITMAP(Gdiplus::Color(0, 0, 0, 0), &bmp);
    if (memDC && bmp) SelectObject(memDC, bmp);
}

GdiBitmap::~GdiBitmap() {
    if (memDC) DeleteDC(memDC);
    if (bmp)   DeleteObject(bmp);
    if (src)   delete src;
}

} // namespace wfam
