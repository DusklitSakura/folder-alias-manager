// WFAM.ExplorerBg - Explorer 路径同步实现
//
// 思路：
//   IShellWindows 列出当前 session 内所有 explorer 窗口；每个窗口可走
//     IServiceProvider → IShellBrowser → QueryActiveShellView
//       → IFolderView2 → GetFolder(IPersistFolder2) → GetCurFolder
//       → SHGetPathFromIDListEx
//   并通过 IShellBrowser::GetWindow 拿顶层窗口 HWND（CabinetWClass）。
//
// 由于在 explorer.exe 自己进程内调用，COM 已就绪；我们仅需 STA + 周期调用。

#include "PathSync.h"
#include "Util.h"

#include <objbase.h>
#include <exdisp.h>          // IShellWindows / IWebBrowser2
#include <exdispid.h>
#include <shlobj.h>
#include <shldisp.h>         // IShellFolderViewDual / Folder / FolderItem
#include <shlguid.h>         // SID_STopLevelBrowser
#include <shlwapi.h>
#include <oleacc.h>          // AccessibleObjectFromWindow

#pragma comment(lib, "oleacc.lib")
#include <atomic>
#include <mutex>
#include <thread>
#include <unordered_map>

namespace wfam {

static std::mutex                                  s_mapMutex;
static std::unordered_map<HWND, std::wstring>      s_topPathMap; // 顶层 hwnd → 路径
static std::atomic<bool>                           s_running{false};
static std::thread                                 s_thread;
static HANDLE                                      s_quitEvent = nullptr;

// 把任意 hwnd 归一到所属 CabinetWClass 顶级窗口（找不到返回 nullptr）。
static HWND FindCabinetTop(HWND h) {
    HWND cur = h;
    HWND desk = GetDesktopWindow();
    for (int i = 0; i < 12 && cur && cur != desk; ++i) {
        wchar_t cls[64] = {};
        GetClassNameW(cur, cls, 64);
        if (_wcsicmp(cls, L"CabinetWClass") == 0) return cur;
        cur = GetAncestor(cur, GA_PARENT);
    }
    return nullptr;
}

static std::wstring PathFromUrl(const std::wstring& url) {
    const std::wstring prefix = L"file:///";
    if (url.size() < prefix.size() || _wcsnicmp(url.c_str(), prefix.c_str(), prefix.size()) != 0) {
        return {};
    }
    std::wstring s = url.substr(prefix.size());
    for (auto& c : s) if (c == L'/') c = L'\\';
    // URL decode 简易（处理 %20）
    std::wstring out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == L'%' && i + 2 < s.size()) {
            auto hex = [](wchar_t c) -> int {
                if (c >= L'0' && c <= L'9') return c - L'0';
                if (c >= L'a' && c <= L'f') return 10 + (c - L'a');
                if (c >= L'A' && c <= L'F') return 10 + (c - L'A');
                return -1;
            };
            int h = hex(s[i + 1]), l = hex(s[i + 2]);
            if (h >= 0 && l >= 0) { out.push_back((wchar_t)((h << 4) | l)); i += 2; continue; }
        }
        out.push_back(s[i]);
    }
    return out;
}

static std::wstring GetPathFromShellBrowser(IShellBrowser* psb) {
    if (!psb) return {};
    IShellView* psv = nullptr;
    if (FAILED(psb->QueryActiveShellView(&psv)) || !psv) return {};
    std::wstring result;

    IFolderView2* pfv2 = nullptr;
    if (SUCCEEDED(psv->QueryInterface(IID_PPV_ARGS(&pfv2))) && pfv2) {
        IPersistFolder2* ppf2 = nullptr;
        if (SUCCEEDED(pfv2->GetFolder(IID_PPV_ARGS(&ppf2))) && ppf2) {
            LPITEMIDLIST pidl = nullptr;
            if (SUCCEEDED(ppf2->GetCurFolder(&pidl)) && pidl) {
                wchar_t buf[MAX_PATH] = {};
                if (SHGetPathFromIDListW(pidl, buf)) result = buf;
                CoTaskMemFree(pidl);
            }
            ppf2->Release();
        }
        pfv2->Release();
    }
    psv->Release();
    return result;
}

static void RefreshOnce() {
    IShellWindows* psw = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER,
                                  IID_PPV_ARGS(&psw));
    if (FAILED(hr) || !psw) {
        static bool s_logged = false;
        if (!s_logged) {
            s_logged = true;
            wchar_t buf[128];
            swprintf_s(buf, L"PathSync: CoCreate IShellWindows failed hr=0x%08X", hr);
            Log(buf);
        }
        return;
    }
    long n = 0;
    psw->get_Count(&n);

    std::unordered_map<HWND, std::wstring> latest;
    latest.reserve((size_t)n);

    static bool s_dumpedOnce = false;
    static long s_lastDumpCount = -1;
    bool dumpThis = !s_dumpedOnce || (n != s_lastDumpCount);

    for (long i = 0; i < n; ++i) {
        VARIANT v; VariantInit(&v); v.vt = VT_I4; v.lVal = i;
        IDispatch* pdisp = nullptr;
        if (FAILED(psw->Item(v, &pdisp)) || !pdisp) { VariantClear(&v); continue; }
        VariantClear(&v);

        HWND hwnd = nullptr;
        std::wstring path;
        std::wstring src;

        // 路径方式 A：IShellBrowser
        IServiceProvider* psp = nullptr;
        if (SUCCEEDED(pdisp->QueryInterface(IID_PPV_ARGS(&psp))) && psp) {
            IShellBrowser* psb = nullptr;
            if (SUCCEEDED(psp->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&psb))) && psb) {
                psb->GetWindow(&hwnd);
                path = GetPathFromShellBrowser(psb);
                if (!path.empty()) src = L"SB";
                psb->Release();
            }
            psp->Release();
        }

        // 路径方式 B：IWebBrowser2.LocationURL（同时也用来兜底拿 hwnd）
        IWebBrowser2* pwb = nullptr;
        if (SUCCEEDED(pdisp->QueryInterface(IID_PPV_ARGS(&pwb))) && pwb) {
            if (!hwnd) {
                SHANDLE_PTR h = 0;
                if (SUCCEEDED(pwb->get_HWND(&h))) hwnd = (HWND)h;
            }
            if (path.empty()) {
                BSTR bs = nullptr;
                if (SUCCEEDED(pwb->get_LocationURL(&bs)) && bs) {
                    std::wstring url(bs, SysStringLen(bs));
                    SysFreeString(bs);
                    path = PathFromUrl(url);
                    if (!path.empty()) src = L"WB";
                }
            }

            // 路径方式 C：Document(IShellFolderViewDual) -> Folder -> Self -> Path
            // 这条对非活动 tab 也有效
            if (path.empty()) {
                IDispatch* pdocDisp = nullptr;
                if (SUCCEEDED(pwb->get_Document(&pdocDisp)) && pdocDisp) {
                    IShellFolderViewDual* psfvd = nullptr;
                    if (SUCCEEDED(pdocDisp->QueryInterface(IID_PPV_ARGS(&psfvd))) && psfvd) {
                        Folder* pf = nullptr;
                        if (SUCCEEDED(psfvd->get_Folder(&pf)) && pf) {
                            Folder2* pf2 = nullptr;
                            FolderItem* pfi = nullptr;
                            if (SUCCEEDED(pf->QueryInterface(IID_PPV_ARGS(&pf2))) && pf2 &&
                                SUCCEEDED(pf2->get_Self(&pfi)) && pfi) {
                                BSTR bsp = nullptr;
                                if (SUCCEEDED(pfi->get_Path(&bsp)) && bsp) {
                                    std::wstring sp(bsp, SysStringLen(bsp));
                                    SysFreeString(bsp);
                                    // 可能是 file:/// 也可能就是 C:\..
                                    if (!sp.empty()) {
                                        if (_wcsnicmp(sp.c_str(), L"file:///", 8) == 0) {
                                            path = PathFromUrl(sp);
                                        } else {
                                            path = sp;
                                        }
                                        if (!path.empty()) src = L"FV";
                                    }
                                }
                                pfi->Release();
                            }
                            if (pf2) pf2->Release();
                            pf->Release();
                        }
                        psfvd->Release();
                    }
                    pdocDisp->Release();
                }
            }
            pwb->Release();
        }

        if (dumpThis) {
            wchar_t cls[64] = {};
            if (hwnd) GetClassNameW(hwnd, cls, 64);
            HWND cab = FindCabinetTop(hwnd);
            wchar_t buf[320];
            swprintf_s(buf, L"  IShellWindows[%ld] hwnd=%p cls=%s cab=%p src=%s path=%.150s",
                       i, hwnd, cls[0] ? cls : L"-", cab,
                       src.empty() ? L"-" : src.c_str(), path.c_str());
            Log(buf);
        }

        if (hwnd && !path.empty()) {
            // key 归一到 CabinetWClass top（左侧导航切换时同一 cab 下不同 ShellTabWindowClass
            // 共享同一窗口，避免父链匹配失败）
            HWND key = FindCabinetTop(hwnd);
            if (!key) key = hwnd;
            latest[key] = path;
        }
        pdisp->Release();
    }
    psw->Release();

    if (dumpThis) { s_dumpedOnce = true; s_lastDumpCount = n; }

    {
        static long s_lastCount = -1;
        if (n != s_lastCount) {
            s_lastCount = n;
            wchar_t buf[128];
            swprintf_s(buf, L"PathSync: IShellWindows count=%ld, mapped=%zu", n, latest.size());
            Log(buf);
        }
    }

    std::lock_guard<std::mutex> lk(s_mapMutex);
    s_topPathMap.swap(latest);
}

static void Worker() {
    // 自己的 STA
    HRESULT hrInit = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    while (s_running.load()) {
        try { RefreshOnce(); } catch (...) {}
        if (s_quitEvent && WaitForSingleObject(s_quitEvent, 300) == WAIT_OBJECT_0) break;
        if (!s_quitEvent) Sleep(300);
    }
    if (SUCCEEDED(hrInit)) CoUninitialize();
}

void StartPathSync() {
    bool expected = false;
    if (!s_running.compare_exchange_strong(expected, true)) return;
    s_quitEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    s_thread = std::thread(Worker);
}

void StopPathSync() {
    s_running.store(false);
    if (s_quitEvent) SetEvent(s_quitEvent);
    if (s_thread.joinable()) s_thread.join();
    if (s_quitEvent) { CloseHandle(s_quitEvent); s_quitEvent = nullptr; }
}

std::wstring QueryFolderPathForChild(HWND childHwnd) {
    if (!childHwnd) return {};
    HWND cab = FindCabinetTop(childHwnd);
    std::lock_guard<std::mutex> lk(s_mapMutex);
    if (cab) {
        auto it = s_topPathMap.find(cab);
        if (it != s_topPathMap.end()) return it->second;
    }
    HWND cur = childHwnd;
    HWND desktop = GetDesktopWindow();
    for (int i = 0; i < 16 && cur && cur != desktop; ++i) {
        auto it = s_topPathMap.find(cur);
        if (it != s_topPathMap.end()) return it->second;
        cur = GetAncestor(cur, GA_PARENT);
    }
    // 调试：dump map（节流：每 2 秒一次）
    {
        static DWORD s_lastDump = 0;
        DWORD now = GetTickCount();
        if (now - s_lastDump > 2000) {
            s_lastDump = now;
            wchar_t buf[256];
            swprintf_s(buf, L"PathSync map size=%zu (querying child=%p cab=%p)",
                       s_topPathMap.size(), childHwnd, cab);
            Log(buf);
            int idx = 0;
            for (auto& kv : s_topPathMap) {
                swprintf_s(buf, L"  map[%d] hwnd=%p path=%.180s", idx++, kv.first, kv.second.c_str());
                Log(buf);
                if (idx >= 8) break;
            }
        }
    }
    return {};
}

// 从 DUI 出发：parent = SHELLDLL_DefView，AccessibleObjectFromWindow(OBJID_NATIVEOM)
// 返回的 IDispatch 可以 QI 成 IShellFolderViewDual，再链路到 Folder/Self/Path。
// 这条对 Win11 的每个 tab（含非活动）都有效。
std::wstring ResolvePathFromDui(HWND duiHwnd) {
    if (!duiHwnd) return {};

    static DWORD s_lastDiag = 0;
    DWORD now = GetTickCount();
    bool diag = (now - s_lastDiag > 2000);
    if (diag) s_lastDiag = now;

    // 找 SHELLDLL_DefView：通常就是 dui 的 parent
    HWND defView = nullptr;
    HWND probe = GetAncestor(duiHwnd, GA_PARENT);
    HWND desk  = GetDesktopWindow();
    for (int i = 0; i < 6 && probe && probe != desk; ++i) {
        wchar_t cls[64] = {};
        GetClassNameW(probe, cls, 64);
        if (diag) {
            wchar_t buf[160];
            swprintf_s(buf, L"  ResolvePathFromDui anc[%d] hwnd=%p cls=%s", i, probe, cls);
            Log(buf);
        }
        if (_wcsicmp(cls, L"SHELLDLL_DefView") == 0) { defView = probe; break; }
        probe = GetAncestor(probe, GA_PARENT);
    }
    if (!defView) {
        if (diag) Log(L"  ResolvePathFromDui: no SHELLDLL_DefView");
        return {};
    }

    // 通过手动 WM_GETOBJECT(OBJID_NATIVEOM) + ObjectFromLresult 获取 IDispatch。
    // AccessibleObjectFromWindow 在新版 Win11 多 tab 场景下常返回 E_FAIL。
    auto tryNativeOM = [&](HWND target) -> IDispatch* {
        LRESULT lr = 0;
        SendMessageTimeoutW(target, WM_GETOBJECT, 0, (LPARAM)OBJID_NATIVEOM,
                            SMTO_ABORTIFHUNG | SMTO_BLOCK, 1000, (PDWORD_PTR)&lr);
        if (diag) {
            wchar_t buf[160];
            swprintf_s(buf, L"  ResolvePathFromDui: WM_GETOBJECT(NATIVEOM) on %p -> lr=0x%I64x",
                       target, (long long)lr);
            Log(buf);
        }
        if (lr == 0) return nullptr;
        IDispatch* p = nullptr;
        HRESULT hr = ObjectFromLresult(lr, IID_IDispatch, 0, (void**)&p);
        if (FAILED(hr)) {
            if (diag) {
                wchar_t buf[128];
                swprintf_s(buf, L"  ResolvePathFromDui: ObjectFromLresult hr=0x%08lx", (long)hr);
                Log(buf);
            }
            return nullptr;
        }
        return p;
    };

    IDispatch* pdisp = tryNativeOM(defView);

    // 兜底：尝试 SHELLDLL_DefView 的直接子窗口
    if (!pdisp) {
        HWND child = GetWindow(defView, GW_CHILD);
        for (int i = 0; i < 8 && child && !pdisp; ++i) {
            wchar_t cls[64] = {};
            GetClassNameW(child, cls, 64);
            if (diag) {
                wchar_t buf[160];
                swprintf_s(buf, L"  ResolvePathFromDui: try defView child[%d] hwnd=%p cls=%s", i, child, cls);
                Log(buf);
            }
            pdisp = tryNativeOM(child);
            child = GetWindow(child, GW_HWNDNEXT);
        }
    }

    if (!pdisp) return {};

    std::wstring path;
    IShellFolderViewDual* psfvd = nullptr;
    HRESULT hr2 = pdisp->QueryInterface(IID_PPV_ARGS(&psfvd));
    if (diag) {
        wchar_t buf[128];
        swprintf_s(buf, L"  ResolvePathFromDui: QI(IShellFolderViewDual)=0x%08lx", (long)hr2);
        Log(buf);
    }
    if (SUCCEEDED(hr2) && psfvd) {
        Folder* pf = nullptr;
        if (SUCCEEDED(psfvd->get_Folder(&pf)) && pf) {
            Folder2* pf2 = nullptr;
            FolderItem* pfi = nullptr;
            if (SUCCEEDED(pf->QueryInterface(IID_PPV_ARGS(&pf2))) && pf2 &&
                SUCCEEDED(pf2->get_Self(&pfi)) && pfi) {
                BSTR bsp = nullptr;
                if (SUCCEEDED(pfi->get_Path(&bsp)) && bsp) {
                    std::wstring sp(bsp, SysStringLen(bsp));
                    SysFreeString(bsp);
                    if (!sp.empty()) {
                        if (_wcsnicmp(sp.c_str(), L"file:///", 8) == 0) {
                            path = PathFromUrl(sp);
                        } else {
                            path = sp;
                        }
                    }
                }
                pfi->Release();
            }
            if (pf2) pf2->Release();
            pf->Release();
        }
        psfvd->Release();
    }
    pdisp->Release();
    return path;
}

} // namespace wfam
