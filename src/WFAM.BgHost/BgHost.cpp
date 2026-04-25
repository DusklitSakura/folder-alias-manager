// WFAM.BgHost - 常驻注入器
//
// 用法：
//   WFAM.BgHost.exe              （默认 = 启动）
//   WFAM.BgHost.exe --stop       （触发已运行实例退出）
//
// 行为：
//   1. 通过命名互斥保证同 session 只跑一份；
//   2. LoadLibrary("WFAM.ExplorerBg.dll")（同目录）；
//   3. SetWindowsHookEx(WH_GETMESSAGE, GetMsgProc, hModule, 0)；
//      → 内核会把 DLL 注入到当前桌面所有 GUI 进程；
//      → DLL 的 DllMain 检测到非 explorer 时直接静默返回；
//   4. 等待命名事件 "WFAM.BgHost.Quit"，收到后 UnhookWindowsHookEx + 退出。

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

static const wchar_t* kQuitEventName = L"Local\\WFAM.BgHost.Quit";
static const wchar_t* kSingletonName = L"Local\\WFAM.BgHost.Singleton";

static std::wstring DllPathBesideSelf() {
    wchar_t buf[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, buf, MAX_PATH);
    std::wstring p = buf;
    auto pos = p.find_last_of(L'\\');
    if (pos != std::wstring::npos) p.resize(pos + 1);
    p += L"WFAM.ExplorerBg.dll";
    return p;
}

static int CmdStop() {
    HANDLE h = OpenEventW(EVENT_MODIFY_STATE | SYNCHRONIZE, FALSE, kQuitEventName);
    if (!h) return 0;            // 没在跑就视作成功
    SetEvent(h);
    CloseHandle(h);
    return 0;
}

static int CmdStart() {
    // 单实例
    HANDLE hMutex = CreateMutexW(nullptr, TRUE, kSingletonName);
    if (!hMutex || GetLastError() == ERROR_ALREADY_EXISTS) {
        if (hMutex) CloseHandle(hMutex);
        return 0;                // 已经在跑
    }

    auto dllPath = DllPathBesideSelf();
    HMODULE hDll = LoadLibraryW(dllPath.c_str());
    if (!hDll) return 2;

    auto pProc = (HOOKPROC)GetProcAddress(hDll, "GetMsgProc");
    if (!pProc) { FreeLibrary(hDll); return 3; }

    HHOOK hHook = SetWindowsHookExW(WH_GETMESSAGE, pProc, hDll, 0);
    if (!hHook) { FreeLibrary(hDll); return 4; }

    HANDLE hQuit = CreateEventW(nullptr, TRUE, FALSE, kQuitEventName);

    // 给所有线程发一次 NULL 消息，刺激 hook 触发，加速 DLL 加载
    PostMessageW(HWND_BROADCAST, WM_NULL, 0, 0);

    // 主消息循环：等待 quit 事件 或 WM_QUIT
    MSG msg;
    while (true) {
        DWORD wait = MsgWaitForMultipleObjectsEx(
            1, &hQuit, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        if (wait == WAIT_OBJECT_0) break;
        if (wait == WAIT_OBJECT_0 + 1) {
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                if (msg.message == WM_QUIT) goto done;
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        }
    }
done:
    UnhookWindowsHookEx(hHook);
    if (hQuit) CloseHandle(hQuit);
    // 不调用 FreeLibrary：保证已注入到其它进程的 DLL 持续存活，
    // 由各进程在退出时自然卸载。本进程结束后内核会做清理。
    if (hMutex) { ReleaseMutex(hMutex); CloseHandle(hMutex); }
    return 0;
}

int APIENTRY wWinMain(HINSTANCE, HINSTANCE, LPWSTR lpCmdLine, int) {
    std::wstring cmd = lpCmdLine ? lpCmdLine : L"";
    if (cmd.find(L"--stop") != std::wstring::npos) return CmdStop();
    return CmdStart();
}
