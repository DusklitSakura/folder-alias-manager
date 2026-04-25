// WFAM.ExplorerBg - 主体：注入到 explorer.exe 后安装 hook，按文件夹绘制背景图
//
// 加载方式：由 WFAM.BgHost.exe 通过 SetWindowsHookEx(WH_GETMESSAGE, GetMsgProc, ...)
// 注入到 explorer 线程；DllMain 检测进程后启动 hook + 路径同步线程。

#include "Util.h"
#include "PathSync.h"

#include <MinHook.h>
#include <gdiplus.h>
#include <unordered_map>
#include <mutex>
#include <string>
#include <algorithm>
#include <thread>
#include <atomic>

#pragma comment(lib, "msimg32.lib")

HMODULE g_hModule = nullptr;
static bool s_inExplorer = false;
static bool s_hookInstalled = false;
static ULONG_PTR s_gdiplusToken = 0;

// 经典 "DesktopBackground" 节
static const wchar_t* kBgSection = L"{BE098140-A513-11D0-A3A4-00C04FD706EC}";

// ============================================================
// 全局状态：每个 DUI 容器的绘制上下文
// ============================================================
struct DuiData {
    HDC  hdc          = nullptr;
    SIZE size         = { 0, 0 };
    SIZE lastClient   = { 0, 0 }; // resize 监视用的客户区尺寸
    HWND topHwnd      = nullptr;  // 对应的 CabinetWClass 顶层窗口（用于查路径）
};

static std::unordered_map<HWND, DuiData> s_duiMap; // DUI hwnd → ctx
static std::mutex                        s_duiMutex;

// resize 监视线程
static std::thread        s_resizeThread;
static std::atomic<bool>  s_resizeQuit{false};

// path → 已加载位图（同一图片只加载一份）
static std::unordered_map<std::wstring, wfam::GdiBitmap*> s_bgCache;
static std::mutex                                          s_bgMutex;

// ============================================================
// Hook 原始函数指针
// ============================================================
typedef HWND (WINAPI* PFN_CreateWindowExW)(DWORD,LPCWSTR,LPCWSTR,DWORD,int,int,int,int,HWND,HMENU,HINSTANCE,LPVOID);
typedef BOOL (WINAPI* PFN_DestroyWindow)(HWND);
typedef HDC  (WINAPI* PFN_BeginPaint)(HWND, LPPAINTSTRUCT);
typedef int  (WINAPI* PFN_FillRect)(HDC, const RECT*, HBRUSH);
typedef HDC  (WINAPI* PFN_CreateCompatibleDC)(HDC);

static PFN_CreateWindowExW    o_CreateWindowExW    = nullptr;
static PFN_DestroyWindow      o_DestroyWindow      = nullptr;
static PFN_BeginPaint         o_BeginPaint         = nullptr;
static PFN_FillRect           o_FillRect           = nullptr;
static PFN_CreateCompatibleDC o_CreateCompatibleDC = nullptr;

// ============================================================
// 工具
// ============================================================
static HWND FindOwnerTopHwnd(HWND hwnd) {
    HWND top = hwnd;
    while (true) {
        HWND parent = GetAncestor(top, GA_PARENT);
        if (!parent || parent == GetDesktopWindow()) break;
        top = parent;
    }
    return top;
}

static wfam::GdiBitmap* GetOrLoadBitmapForFolder(const std::wstring& folderPath) {
    if (folderPath.empty()) return nullptr;
    if (folderPath.size() < 2 || folderPath[1] != L':') return nullptr;

    std::wstring iniPath = folderPath;
    if (iniPath.back() != L'\\') iniPath.push_back(L'\\');
    iniPath += L"desktop.ini";
    if (!wfam::FileExists(iniPath)) return nullptr;

    std::wstring img = wfam::GetIniString(iniPath, kBgSection, L"IconArea_Image");
    if (img.empty()) return nullptr;

    std::wstring abs = wfam::ResolveRelative(folderPath, img);
    if (!wfam::FileExists(abs)) return nullptr;

    std::wstring key = abs;
    std::transform(key.begin(), key.end(), key.begin(), ::towlower);

    std::lock_guard<std::mutex> lk(s_bgMutex);
    auto it = s_bgCache.find(key);
    if (it != s_bgCache.end()) return it->second;

    auto* b = new wfam::GdiBitmap(abs);
    if (!b->ok()) { delete b; return nullptr; }
    s_bgCache.emplace(key, b);
    return b;
}

// ============================================================
// Hooks
// ============================================================
static HWND WINAPI MyCreateWindowExW(
    DWORD dwExStyle, LPCWSTR lpClassName, LPCWSTR lpWindowName, DWORD dwStyle,
    int X, int Y, int nWidth, int nHeight,
    HWND hWndParent, HMENU hMenu, HINSTANCE hInstance, LPVOID lpParam)
{
    HWND hWnd = o_CreateWindowExW(dwExStyle, lpClassName, lpWindowName, dwStyle,
                                  X, Y, nWidth, nHeight, hWndParent, hMenu, hInstance, lpParam);
    if (!hWnd) return hWnd;

    auto cls = wfam::GetWindowClassName(hWnd);
    if (cls != L"DirectUIHWND") return hWnd;
    if (wfam::GetWindowClassName(hWndParent) != L"SHELLDLL_DefView") return hWnd;

    HWND grand = GetParent(hWndParent);
    auto gcls = wfam::GetWindowClassName(grand);
    if (gcls != L"ShellTabWindowClass" && gcls != L"#32770" && gcls != L"CabinetWClass")
        return hWnd;

    HWND top = FindOwnerTopHwnd(hWnd);
    {
        std::lock_guard<std::mutex> lk(s_duiMutex);
        DuiData d; d.topHwnd = top;
        s_duiMap[hWnd] = d;
    }
    wchar_t buf[128];
    swprintf_s(buf, L"DUI captured hwnd=%p top=%p grand=%s", hWnd, top, gcls.c_str());
    wfam::Log(buf);
    return hWnd;
}

static BOOL WINAPI MyDestroyWindow(HWND hWnd) {
    {
        std::lock_guard<std::mutex> lk(s_duiMutex);
        s_duiMap.erase(hWnd);
    }
    return o_DestroyWindow(hWnd);
}

static HDC WINAPI MyBeginPaint(HWND hWnd, LPPAINTSTRUCT lpPaint) {
    HDC hdc = o_BeginPaint(hWnd, lpPaint);
    std::lock_guard<std::mutex> lk(s_duiMutex);
    auto it = s_duiMap.find(hWnd);
    if (it != s_duiMap.end()) it->second.hdc = hdc;
    return hdc;
}

static HDC WINAPI MyCreateCompatibleDC(HDC hDC) {
    HDC ret = o_CreateCompatibleDC(hDC);
    HWND src = WindowFromDC(hDC);
    if (src) {
        std::lock_guard<std::mutex> lk(s_duiMutex);
        auto it = s_duiMap.find(src);
        if (it != s_duiMap.end()) it->second.hdc = ret;
    }
    return ret;
}

static int WINAPI MyFillRect(HDC hDC, const RECT* lprc, HBRUSH hbr) {
    int ret = o_FillRect(hDC, lprc, hbr);
    if (!lprc) return ret;

    HWND  targetDui = nullptr;
    DuiData snap;
    size_t mapSize = 0;
    {
        std::lock_guard<std::mutex> lk(s_duiMutex);
        mapSize = s_duiMap.size();
        for (auto& kv : s_duiMap) {
            if (kv.second.hdc == hDC) { targetDui = kv.first; snap = kv.second; break; }
        }
    }
    if (!targetDui) return ret;

    {
        wchar_t buf[160];
        swprintf_s(buf, L"FillRect hit dui=%p hdc=%p rc=%ld,%ld,%ld,%ld mapN=%zu",
                   targetDui, hDC, lprc->left, lprc->top, lprc->right, lprc->bottom, mapSize);
        wfam::Log(buf);
    }

    std::wstring path = wfam::QueryFolderPathForChild(snap.topHwnd ? snap.topHwnd : targetDui);
    if (path.empty()) {
        // 兜底：直接对 SHELLDLL_DefView 走 OBJID_NATIVEOM
        path = wfam::ResolvePathFromDui(targetDui);
        if (!path.empty()) {
            wchar_t buf[512];
            swprintf_s(buf, L"FillRect: ResolvePathFromDui ok dui=%p path=%.180s", targetDui, path.c_str());
            wfam::Log(buf);
        }
    }
    if (path.empty()) {
        wchar_t buf[160];
        swprintf_s(buf, L"FillRect: no path for top=%p (dui=%p)", snap.topHwnd, targetDui);
        wfam::Log(buf);
        return ret;
    }

    auto* bmp = GetOrLoadBitmapForFolder(path);
    if (!bmp || !bmp->ok()) {
        wchar_t buf[512];
        swprintf_s(buf, L"FillRect: no bitmap for path=%s", path.c_str());
        wfam::Log(buf);
        return ret;
    }

    RECT wr;
    GetWindowRect(targetDui, &wr);
    SIZE wndSize = { wr.right - wr.left, wr.bottom - wr.top };

    if ((snap.size.cx != wndSize.cx || snap.size.cy != wndSize.cy)) {
        std::lock_guard<std::mutex> lk(s_duiMutex);
        auto it = s_duiMap.find(targetDui);
        if (it != s_duiMap.end()) it->second.size = wndSize;
    }

    SaveDC(hDC);
    IntersectClipRect(hDC, lprc->left, lprc->top, lprc->right, lprc->bottom);

    int srcW = bmp->size.cx, srcH = bmp->size.cy;
    int dstW = wndSize.cx;
    int dstH = (srcW > 0) ? (int)((LONGLONG)srcH * dstW / srcW) : srcH;
    if (dstH < wndSize.cy) {
        dstH = wndSize.cy;
        dstW = (srcH > 0) ? (int)((LONGLONG)srcW * dstH / srcH) : srcW;
    }
    int dx = (wndSize.cx - dstW) / 2;
    int dy = (wndSize.cy - dstH) / 2;

    BLENDFUNCTION bf{};
    bf.BlendOp = AC_SRC_OVER;
    bf.SourceConstantAlpha = 220;
    bf.AlphaFormat = AC_SRC_ALPHA;
    AlphaBlend(hDC, dx, dy, dstW, dstH, bmp->memDC, 0, 0, srcW, srcH, bf);

    RestoreDC(hDC, -1);
    return ret;
}

// ============================================================
// 安装 / 卸载
// ============================================================
// ============================================================
// resize 监视：发现某个 DUI 客户区尺寸变化 → InvalidateRect 强制整体重绘。
// 解决两个问题：
//   1) 拉伸窗口时背景不会跟随窗口实时缩放（Explorer 只增量重绘新增条带）。
//   2) 改变窗口大小后旧画面残留在标题栏 / 工具栏附近。
// ============================================================
static void ResizeWatcher() {
    while (!s_resizeQuit.load()) {
        std::vector<std::pair<HWND, SIZE>> toInvalidate;
        {
            std::lock_guard<std::mutex> lk(s_duiMutex);
            for (auto& kv : s_duiMap) {
                HWND dui = kv.first;
                if (!IsWindow(dui)) continue;
                RECT rc{};
                if (!GetClientRect(dui, &rc)) continue;
                SIZE cur = { rc.right - rc.left, rc.bottom - rc.top };
                if (cur.cx <= 0 || cur.cy <= 0) continue;
                if (cur.cx != kv.second.lastClient.cx || cur.cy != kv.second.lastClient.cy) {
                    kv.second.lastClient = cur;
                    toInvalidate.emplace_back(dui, cur);
                }
            }
        }
        for (auto& p : toInvalidate) {
            // FALSE：不擦背景（避免闪烁），让 Explorer 自身的 FillRect 触发我们的绘制。
            InvalidateRect(p.first, nullptr, FALSE);
        }
        Sleep(80);
    }
}

static void InstallHooks() {
    if (s_hookInstalled) return;
    s_hookInstalled = true;

    Gdiplus::GdiplusStartupInput si;
    Gdiplus::GdiplusStartup(&s_gdiplusToken, &si, nullptr);

    if (MH_Initialize() != MH_OK) return;
    MH_CreateHook((LPVOID)&CreateWindowExW,    (LPVOID)&MyCreateWindowExW,    (LPVOID*)&o_CreateWindowExW);
    MH_CreateHook((LPVOID)&DestroyWindow,      (LPVOID)&MyDestroyWindow,      (LPVOID*)&o_DestroyWindow);
    MH_CreateHook((LPVOID)&BeginPaint,         (LPVOID)&MyBeginPaint,         (LPVOID*)&o_BeginPaint);
    MH_CreateHook((LPVOID)&FillRect,           (LPVOID)&MyFillRect,           (LPVOID*)&o_FillRect);
    MH_CreateHook((LPVOID)&CreateCompatibleDC, (LPVOID)&MyCreateCompatibleDC, (LPVOID*)&o_CreateCompatibleDC);
    MH_EnableHook(MH_ALL_HOOKS);

    wfam::StartPathSync();
    s_resizeQuit.store(false);
    s_resizeThread = std::thread(ResizeWatcher);
    wfam::Log(L"hooks installed");
}

// ============================================================
// 进程过滤：只在 explorer.exe 内安装 hook
// ============================================================
static bool IsExplorerProcess() {
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring name(path);
    auto pos = name.find_last_of(L'\\');
    if (pos != std::wstring::npos) name = name.substr(pos + 1);
    std::transform(name.begin(), name.end(), name.begin(), ::towlower);
    return name == L"explorer.exe";
}

// ============================================================
// 导出：WH_GETMESSAGE 回调（仅作为 SetWindowsHookEx 的 hookProc，
// 触发 LoadLibrary 加载本 DLL，从而执行 DllMain → InstallHooks）。
// ============================================================
extern "C" __declspec(dllexport) LRESULT CALLBACK GetMsgProc(int code, WPARAM wParam, LPARAM lParam) {
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        if (!g_hModule) g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
        s_inExplorer = IsExplorerProcess();
        if (s_inExplorer) {
            InstallHooks();
        }
    } else if (reason == DLL_PROCESS_DETACH) {
        if (s_inExplorer) {
            s_resizeQuit.store(true);
            if (s_resizeThread.joinable()) s_resizeThread.detach();
            wfam::StopPathSync();
        }
        std::lock_guard<std::mutex> lk(s_bgMutex);
        for (auto& kv : s_bgCache) delete kv.second;
        s_bgCache.clear();
    }
    return TRUE;
}
