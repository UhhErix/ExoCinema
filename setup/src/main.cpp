// Exo Cinema Setup: the window that installs, updates and uninstalls Exo Cinema, as Exo Launcher's Setup does (ported
// from Exo Browser's port of it).
//
// The screens are Exo's look (Exo Cinema's glass.css, Inter) drawn by WebView2, the engine Exo Cinema itself runs on,
// from a page packed into this exe (setup\page, packed by setup\build.ps1). The work is an NSIS engine
// (tools\ExoCinema.nsi) appended to this exe, run silently: it writes each step to a status file this window shows, and
// the staging folder it fills gives the real progress. /S, or EXO_CINEMA_SILENT_INSTALL (the in-app updater), shows no
// window at all. Without WebView2 the engine's own plain screens run instead (and it installs WebView2 for Exo Cinema).
//
//   ExoSetup.exe [/S [/NOLAUNCH] [/DESKTOP=0|1]]          install or update
//   ExoSetup.exe --uninstall [/S [/REMOVEDATA]]           uninstall (the copy in app\ is Windows' uninstaller)
// EXO_CINEMA_SETUP_TEST_ROOT: the folder a walk-through build installs under (build.ps1 -TestRoot builds it in).
// EXO_CINEMA_SETUP_DEVTOOLS: DevTools for the page, on port 9557.
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <dwmapi.h>
#include <wrl.h>
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <fstream>
#include <map>
#include <string>
#include <thread>
#include <vector>
#include "WebView2.h"
#include "WebView2EnvironmentOptions.h"
#include "setup-config.h" // written by build.ps1: PRODUCT_VERSION, and TEST_ROOT for a walk-through build

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

#ifndef PRODUCT_VERSION
#define PRODUCT_VERSION L"0.0.0"
#endif

namespace {
constexpr wchar_t kOrigin[] = L"https://setup.exo-cinema.local/";
constexpr wchar_t kExe[] = L"ExoCinema.exe";
constexpr char kMagic[8] = {'E', 'X', 'O', 'S', 'E', 'T', 'U', 'P'};
constexpr UINT WM_APP_POST = WM_APP + 1; // a JSON message for the page, posted from a worker thread

HWND g_window = nullptr;
ComPtr<ICoreWebView2Controller> g_controller;
ComPtr<ICoreWebView2> g_view;
std::map<std::wstring, std::string> g_files; // the page, from the packed resource
std::atomic<bool> g_busy{false};
bool g_uninstall = false, g_test = false;
uint64_t g_engineOffset = 0, g_engineSize = 0, g_appBytes = 0;
std::wstring g_self, g_temp, g_root, g_app, g_linkName = L"Exo Cinema";

std::wstring Known(REFKNOWNFOLDERID id) {
    PWSTR path = nullptr;
    std::wstring out;
    if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &path))) out = path;
    CoTaskMemFree(path);
    return out;
}

bool Exists(const std::wstring& path) { return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES; }

std::string Utf8(const std::wstring& text) {
    if (text.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, text.data(), (int)text.size(), nullptr, 0, nullptr, nullptr);
    std::string out(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), (int)text.size(), out.data(), n, nullptr, nullptr);
    return out;
}

std::wstring Wide(const std::string& text) {
    if (text.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), (int)text.size(), nullptr, 0);
    std::wstring out(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), (int)text.size(), out.data(), n);
    return out;
}

// A JSON string literal: quotes, backslashes and control characters escaped.
std::string Json(const std::wstring& text) {
    std::string in = Utf8(text), out = "\"";
    for (unsigned char c : in) {
        if (c == '"' || c == '\\') { out += '\\'; out += (char)c; }
        else if (c < 0x20) { char buf[8]; sprintf_s(buf, "\\u%04x", c); out += buf; }
        else out += (char)c;
    }
    return out + "\"";
}

// Posts to the page from any thread (WebView2 calls belong to the window's thread). A silent run has no page.
void Post(const std::string& json) {
    if (!g_window) return;
    auto copy = new std::wstring(Wide(json));
    if (!PostMessageW(g_window, WM_APP_POST, 0, reinterpret_cast<LPARAM>(copy))) delete copy;
}

// The NSIS engine and its sizes, appended after this exe: [engine][u64 engine size][u64 app bytes]["EXOSETUP"].
bool ReadTrailer() {
    std::ifstream file(g_self, std::ios::binary | std::ios::ate);
    if (!file) return false;
    auto total = (uint64_t)file.tellg();
    if (total < 24) return false;
    char magic[8];
    file.seekg(total - 8);
    file.read(magic, 8);
    if (memcmp(magic, kMagic, 8) != 0) return false;
    file.seekg(total - 24);
    file.read(reinterpret_cast<char*>(&g_engineSize), 8);
    file.read(reinterpret_cast<char*>(&g_appBytes), 8);
    if (g_engineSize == 0 || g_engineSize > total - 24) return false;
    g_engineOffset = total - 24 - g_engineSize;
    return true;
}

std::wstring ExtractEngine() {
    std::wstring path = g_temp + L"\\engine.exe";
    std::ifstream in(g_self, std::ios::binary);
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!in || !out) return {};
    in.seekg((std::streamoff)g_engineOffset);
    std::vector<char> buffer(1 << 20);
    for (uint64_t left = g_engineSize; left > 0;) {
        auto chunk = (std::streamsize)std::min<uint64_t>(left, buffer.size());
        in.read(buffer.data(), chunk);
        if (in.gcount() != chunk) return {};
        out.write(buffer.data(), chunk);
        left -= (uint64_t)chunk;
    }
    out.close();
    return out.good() ? path : std::wstring{};
}

DWORD Run(const std::wstring& exe, const std::wstring& args, HANDLE* process = nullptr, DWORD* pid = nullptr) {
    std::wstring line = L"\"" + exe + L"\" " + args;
    STARTUPINFOW si{sizeof(si)};
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(exe.c_str(), line.data(), nullptr, nullptr, FALSE, 0, nullptr, g_temp.c_str(), &si, &pi)) return GetLastError();
    CloseHandle(pi.hThread);
    if (pid) *pid = pi.dwProcessId;
    if (process) { *process = pi.hProcess; return 0; }
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hProcess);
    return code;
}

uint64_t FolderBytes(const std::wstring& dir) {
    uint64_t total = 0;
    WIN32_FIND_DATAW data;
    HANDLE find = FindFirstFileExW((dir + L"\\*").c_str(), FindExInfoBasic, &data, FindExSearchNameMatch, nullptr, FIND_FIRST_EX_LARGE_FETCH);
    if (find == INVALID_HANDLE_VALUE) return 0;
    do {
        if (!wcscmp(data.cFileName, L".") || !wcscmp(data.cFileName, L"..")) continue;
        if (data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) continue;
        if (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) total += FolderBytes(dir + L"\\" + data.cFileName);
        else total += ((uint64_t)data.nFileSizeHigh << 32) | data.nFileSizeLow;
    } while (FindNextFileW(find, &data));
    FindClose(find);
    return total;
}

std::wstring LastLine(const std::wstring& path) {
    std::ifstream in(path, std::ios::binary);
    std::string text((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    while (!text.empty() && (text.back() == '\n' || text.back() == '\r')) text.pop_back();
    auto at = text.find_last_of('\n');
    return Wide(at == std::string::npos ? text : text.substr(at + 1));
}

void DeleteTree(const std::wstring& dir) {
    std::wstring from = dir + L'\0';
    SHFILEOPSTRUCTW op{};
    op.wFunc = FO_DELETE;
    op.pFrom = from.c_str();
    op.fFlags = FOF_NO_UI;
    SHFileOperationW(&op);
}

std::wstring DesktopLink() { return Known(FOLDERID_Desktop) + L"\\" + g_linkName + L".lnk"; }

std::string Progress(double value, const std::wstring& status) {
    char buf[48] = "null";
    if (value >= 0) sprintf_s(buf, "%.4f", value);
    return std::string(R"({"type":"progress","value":)") + buf + R"(,"status":)" + Json(status) + "}";
}

// Install or update: the engine silently, its staging folder's growth as the progress, its status file as the step.
// The bar glides until the staging folder appears.
void Install(bool desktop) {
    std::wstring engine = ExtractEngine();
    if (engine.empty()) { Post(R"({"type":"failed","message":"Setup couldn't unpack itself. Download it again and retry."})"); g_busy = false; return; }
    std::wstring status = g_temp + L"\\status.txt";
    DeleteFileW(status.c_str());
    DeleteFileW((g_root + L"\\update-error.log").c_str());
    HANDLE process = nullptr;
    DWORD pid = 0;
    std::wstring args = L"/S /NOLAUNCH /DESKTOP=" + std::wstring(desktop ? L"1" : L"0") + L" /STATUSFILE=" + status;
    if (Run(engine, args, &process, &pid) != 0) { Post(R"({"type":"failed","message":"Setup couldn't start its installer."})"); g_busy = false; return; }
    std::wstring incoming = g_root + L"\\app.incoming-" + std::to_wstring(pid);
    std::wstring shown;
    double last = -2, copied = -1;
    while (WaitForSingleObject(process, 120) == WAIT_TIMEOUT) {
        double value = copied;
        if (Exists(incoming)) value = copied = g_appBytes ? std::min(0.88, 0.88 * (double)FolderBytes(incoming) / (double)g_appBytes) : 0.5;
        else if (copied >= 0) value = std::max(copied, 0.9);
        std::wstring step = LastLine(status);
        if (step != shown || value - last > 0.004) {
            shown = step;
            last = value;
            Post(Progress(value, step));
        }
    }
    DWORD code = 1;
    GetExitCodeProcess(process, &code);
    CloseHandle(process);
    if (code == 0 && Exists(g_app + L"\\" + kExe)) Post(R"({"type":"done"})");
    else {
        std::wstring why = LastLine(g_root + L"\\update-error.log");
        if (why.empty()) why = L"Setup stopped before it finished. Nothing installed was changed.";
        Post(std::string(R"({"type":"failed","message":)") + Json(why) + "}");
    }
    g_busy = false;
}

// A walk-through build removes "your data" only from a test folder, never from wherever its variable points.
bool MayRemoveData() {
    if (!g_test) return true;
    std::wstring name = PathFindFileNameW(g_root.c_str());
    CharLowerBuffW(name.data(), (DWORD)name.size());
    return name.find(L"test") != std::wstring::npos && PathIsRootW(g_root.c_str()) == FALSE;
}

// Uninstall: NSIS's uninstaller silently, in place (it waits), then what it couldn't remove while running.
// With "Also remove my sign-in and settings", the whole Exo Cinema folder goes: sign-in, settings, caches, its WebView2 profile.
int Uninstall(bool removeData) {
    std::wstring engine = g_app + L"\\Uninstall.exe";
    Post(Progress(0.3, L"Closing Exo Cinema"));
    if (!Exists(engine) && Exists(g_app + L"\\" + kExe)) {
        Post(R"({"type":"failed","message":"Exo Cinema's uninstaller is missing. Install Exo Cinema again, then uninstall it."})");
        g_busy = false;
        return 1;
    }
    DWORD code = Exists(engine) ? Run(engine, L"/S _?=" + g_app) : 0;
    if (code != 0) {
        Post(R"({"type":"failed","message":"Exo Cinema is still running. Close it and try again."})");
        g_busy = false;
        return 1;
    }
    Post(Progress(0.8, L"Removing files"));
    DeleteFileW(engine.c_str());
    DeleteFileW((g_app + L"\\ExoSetup.exe").c_str());
    if (!Exists(g_app + L"\\" + kExe)) DeleteTree(g_app);
    DeleteFileW(DesktopLink().c_str());
    int result = 0;
    if (removeData && MayRemoveData()) {
        Post(Progress(0.9, L"Removing your sign-in and settings"));
        // WebView2's own processes let go of the profile a moment after Exo Cinema closes.
        for (int i = 0; i < 50 && Exists(g_root); i++) { DeleteTree(g_root); if (Exists(g_root)) Sleep(200); }
        if (Exists(g_root)) {
            Post(R"({"type":"failed","message":"Exo Cinema was removed, but some of its data is still in use. Restart Windows, then delete the ExoCinema folder in %LOCALAPPDATA%."})");
            result = 1;
        }
    }
    if (result == 0) Post(R"({"type":"done"})");
    g_busy = false;
    return result;
}

void SendState() {
    bool updating = Exists(g_app + L"\\" + kExe);
    bool desktop = updating ? Exists(DesktopLink()) : true;
    std::string json = std::string(R"({"type":"state","mode":)") + (g_uninstall ? "\"uninstall\"" : "\"install\"") +
        R"(,"updating":)" + (updating ? "true" : "false") + R"(,"desktop":)" + (desktop ? "true" : "false") +
        R"(,"version":)" + Json(PRODUCT_VERSION) + "}";
    g_view->PostWebMessageAsJson(Wide(json).c_str());
}

bool Has(const std::wstring& message, const wchar_t* needle) { return message.find(needle) != std::wstring::npos; }

void OnMessage(const std::wstring& message) {
    if (Has(message, L"\"type\":\"hello\"")) SendState();
    else if (Has(message, L"\"type\":\"install\"") && !g_uninstall && !g_busy.exchange(true)) {
        bool desktop = Has(message, L"\"desktop\":true");
        std::thread([desktop] { Install(desktop); }).detach();
    } else if (Has(message, L"\"type\":\"uninstall\"") && g_uninstall && !g_busy.exchange(true)) {
        bool removeData = Has(message, L"\"removeData\":true");
        std::thread([removeData] { Uninstall(removeData); }).detach();
    } else if (Has(message, L"\"type\":\"launch\"") && !g_busy) {
        std::wstring exe = g_app + L"\\" + kExe;
        STARTUPINFOW si{sizeof(si)};
        PROCESS_INFORMATION pi{};
        std::wstring line = L"\"" + exe + L"\"";
        if (CreateProcessW(exe.c_str(), line.data(), nullptr, nullptr, FALSE, 0, nullptr, g_app.c_str(), &si, &pi)) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        PostMessageW(g_window, WM_CLOSE, 0, 0);
    } else if (Has(message, L"\"type\":\"close\"") && !g_busy) PostMessageW(g_window, WM_CLOSE, 0, 0);
    else if (Has(message, L"\"type\":\"minimize\"")) ShowWindow(g_window, SW_MINIMIZE);
    else if (Has(message, L"\"type\":\"drag\"")) { ReleaseCapture(); SendMessageW(g_window, WM_NCLBUTTONDOWN, HTCAPTION, 0); }
    else if (Has(message, L"\"type\":\"shown\"")) ShowWindow(g_window, SW_SHOW);
}

const wchar_t* TypeOf(const std::wstring& path) {
    auto ext = PathFindExtensionW(path.c_str());
    if (!_wcsicmp(ext, L".html")) return L"text/html; charset=utf-8";
    if (!_wcsicmp(ext, L".js")) return L"text/javascript; charset=utf-8";
    if (!_wcsicmp(ext, L".css")) return L"text/css; charset=utf-8";
    if (!_wcsicmp(ext, L".svg")) return L"image/svg+xml";
    if (!_wcsicmp(ext, L".png")) return L"image/png";
    if (!_wcsicmp(ext, L".woff2")) return L"font/woff2";
    if (!_wcsicmp(ext, L".txt")) return L"text/plain; charset=utf-8";
    return L"application/octet-stream";
}

// The packed page: [u32 count] then per file [u16 name length][name, UTF-8][u32 size][bytes].
void LoadPage() {
    HRSRC res = FindResourceW(nullptr, MAKEINTRESOURCEW(101), RT_RCDATA);
    if (!res) return;
    auto data = static_cast<const uint8_t*>(LockResource(LoadResource(nullptr, res)));
    DWORD size = SizeofResource(nullptr, res), at = 4;
    if (!data || size < 4) return;
    uint32_t count = *reinterpret_cast<const uint32_t*>(data);
    for (uint32_t i = 0; i < count && at + 6 <= size; i++) {
        uint16_t nameLength = *reinterpret_cast<const uint16_t*>(data + at); at += 2;
        if (at + nameLength + 4 > size) break;
        std::string name(reinterpret_cast<const char*>(data + at), nameLength); at += nameLength;
        uint32_t length = *reinterpret_cast<const uint32_t*>(data + at); at += 4;
        if (at + length > size) break;
        g_files[L"/" + Wide(name)] = std::string(reinterpret_cast<const char*>(data + at), length);
        at += length;
    }
}

void Serve(ICoreWebView2Environment* env, ICoreWebView2WebResourceRequestedEventArgs* args) {
    ComPtr<ICoreWebView2WebResourceRequest> request;
    args->get_Request(&request);
    LPWSTR raw = nullptr;
    request->get_Uri(&raw);
    std::wstring uri = raw ? raw : L"";
    CoTaskMemFree(raw);
    const size_t origin = wcslen(kOrigin);
    std::wstring path = uri.compare(0, origin, kOrigin) == 0 ? uri.substr(origin - 1) : L"";
    if (auto q = path.find_first_of(L"?#"); q != std::wstring::npos) path.resize(q);
    if (path == L"/") path = L"/index.html";
    ComPtr<ICoreWebView2WebResourceResponse> response;
    auto found = g_files.find(path);
    if (found == g_files.end()) {
        env->CreateWebResourceResponse(nullptr, 404, L"Not Found", L"", &response);
    } else {
        ComPtr<IStream> stream;
        stream.Attach(SHCreateMemStream(reinterpret_cast<const BYTE*>(found->second.data()), (UINT)found->second.size()));
        std::wstring headers = std::wstring(L"Content-Type: ") + TypeOf(path) + L"\r\nCache-Control: no-store";
        env->CreateWebResourceResponse(stream.Get(), 200, L"OK", headers.c_str(), &response);
    }
    args->put_Response(response.Get());
}

void Resize() {
    if (!g_controller) return;
    RECT r;
    GetClientRect(g_window, &r);
    g_controller->put_Bounds(r);
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_SIZE:
        Resize();
        // WebView2 doesn't follow its window being shown or minimized by itself: the page would stay hidden (no drawing, no motion).
        if (g_controller) g_controller->put_IsVisible(wp != SIZE_MINIMIZED && IsWindowVisible(hwnd));
        return 0;
    case WM_SHOWWINDOW:
        if (g_controller) g_controller->put_IsVisible(wp ? TRUE : FALSE);
        break;
    case WM_APP_POST: {
        auto json = reinterpret_cast<std::wstring*>(lp);
        if (g_view) g_view->PostWebMessageAsJson(json->c_str());
        delete json;
        return 0;
    }
    case WM_CLOSE:
        if (g_busy) return 0; // never in the middle of an install
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        if (g_controller) g_controller->Close();
        g_window = nullptr;
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

// No WebView2: the engine's own screens (they install WebView2 for Exo Cinema too).
int Fallback() {
    if (g_uninstall) return (int)Run(g_app + L"\\Uninstall.exe", L"");
    std::wstring engine = ExtractEngine();
    return engine.empty() ? 1 : (int)Run(engine, L"");
}

bool Arg(int argc, wchar_t** argv, const wchar_t* name) {
    for (int i = 1; i < argc; i++) if (!_wcsicmp(argv[i], name)) return true;
    return false;
}

// "/DESKTOP=1": the value after the name, or "".
std::wstring Option(int argc, wchar_t** argv, const wchar_t* name) {
    size_t n = wcslen(name);
    for (int i = 1; i < argc; i++) if (!_wcsnicmp(argv[i], name, n)) return argv[i] + n;
    return {};
}

bool Env(const wchar_t* name) { return GetEnvironmentVariableW(name, nullptr, 0) > 0; }

// The temporary copy an uninstall ran from goes a moment after it exits.
void DeleteSelfLater() {
    wchar_t cmd[MAX_PATH];
    if (!GetEnvironmentVariableW(L"ComSpec", cmd, MAX_PATH)) return;
    std::wstring line = L"\"" + std::wstring(cmd) + L"\" /d /c ping -n 3 127.0.0.1 >nul & del /f /q \"" + g_self + L"\"";
    STARTUPINFOW si{sizeof(si)};
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION pi{};
    if (CreateProcessW(cmd, line.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
}
} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    wchar_t self[MAX_PATH * 4];
    GetModuleFileNameW(nullptr, self, ARRAYSIZE(self));
    g_self = self;
    g_root = Known(FOLDERID_LocalAppData) + L"\\ExoCinema";
    // A walk-through build's engine (/DEXO_TEST_ROOT) installs under its own folder: the window follows it there.
#ifdef TEST_ROOT
    g_root = TEST_ROOT;
    g_test = true;
#endif
    wchar_t testRoot[MAX_PATH];
    if (GetEnvironmentVariableW(L"EXO_CINEMA_SETUP_TEST_ROOT", testRoot, MAX_PATH) > 0) { g_root = testRoot; g_test = true; }
    if (g_test) g_linkName = L"Exo Cinema (test)";
    g_app = g_root + L"\\app";
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    g_uninstall = Arg(argc, argv, L"--uninstall");
    bool silent = Arg(argc, argv, L"/S") || (!g_uninstall && Env(L"EXO_CINEMA_SILENT_INSTALL"));
    bool removeData = Arg(argc, argv, L"/REMOVEDATA");
    bool hasEngine = ReadTrailer();

    // Uninstalling from inside the app folder: run from a temporary copy, so the folder can go.
    if (g_uninstall && !Arg(argc, argv, L"--from-temp")) {
        wchar_t tempDir[MAX_PATH];
        GetTempPathW(MAX_PATH, tempDir);
        std::wstring copy = std::wstring(tempDir) + L"ExoCinemaSetup-uninstall-" + std::to_wstring(GetCurrentProcessId()) + L".exe";
        if (CopyFileW(g_self.c_str(), copy.c_str(), FALSE)) {
            STARTUPINFOW si{sizeof(si)};
            PROCESS_INFORMATION pi{};
            std::wstring line = L"\"" + copy + L"\" --uninstall --from-temp" + (silent ? L" /S" : L"") + (removeData ? L" /REMOVEDATA" : L"");
            if (CreateProcessW(copy.c_str(), line.data(), nullptr, nullptr, FALSE, 0, nullptr, tempDir, &si, &pi)) {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                LocalFree(argv);
                return 0;
            }
            DeleteFileW(copy.c_str());
        }
    }
    if (!g_uninstall && !hasEngine) {
        if (!silent) MessageBoxW(nullptr, L"This copy of Exo Cinema Setup is incomplete. Download it again.", L"Exo Cinema Setup", MB_ICONSTOP);
        LocalFree(argv);
        return 1;
    }

    wchar_t tempDir[MAX_PATH];
    GetTempPathW(MAX_PATH, tempDir);
    g_temp = std::wstring(tempDir) + L"ExoCinemaSetup-" + std::to_wstring(GetCurrentProcessId());
    CreateDirectoryW(g_temp.c_str(), nullptr);

    int result = 0;
    if (silent) {
        // /S: no window. Install: the engine's own silent install (it opens Exo Cinema after, unless /NOLAUNCH) and its
        // exit code. Uninstall: the same steps as the window's, with /REMOVEDATA for "Also remove my sign-in and settings".
        if (g_uninstall) result = Uninstall(removeData);
        else {
            std::wstring args = L"/S";
            if (Arg(argc, argv, L"/NOLAUNCH")) args += L" /NOLAUNCH";
            std::wstring desktop = Option(argc, argv, L"/DESKTOP=");
            if (desktop == L"0" || desktop == L"1") args += L" /DESKTOP=" + desktop;
            std::wstring engine = ExtractEngine();
            result = engine.empty() ? 1 : (int)Run(engine, args);
        }
        LocalFree(argv);
        for (int i = 0; i < 30 && Exists(g_temp); i++) { DeleteTree(g_temp); if (Exists(g_temp)) Sleep(100); }
        if (g_uninstall && g_self.find(L"ExoCinemaSetup-uninstall-") != std::wstring::npos) DeleteSelfLater();
        return result;
    }
    LocalFree(argv);

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    LoadPage();
    WNDCLASSEXW wc{sizeof(wc)};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = instance;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1));
    wc.lpszClassName = L"ExoCinemaSetup";
    RegisterClassExW(&wc);
    UINT dpi = GetDpiForSystem();
    int w = MulDiv(460, dpi, 96), h = MulDiv(520, dpi, 96);
    RECT work;
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    g_window = CreateWindowExW(WS_EX_APPWINDOW, wc.lpszClassName, g_uninstall ? L"Uninstall Exo Cinema" : L"Exo Cinema Setup",
        WS_POPUP | WS_MINIMIZEBOX | WS_SYSMENU | WS_CLIPCHILDREN, work.left + (work.right - work.left - w) / 2,
        work.top + (work.bottom - work.top - h) / 2, w, h, nullptr, nullptr, instance, nullptr);
    // Windows 11's rounded corners, a dark frame and Exo's hairline border.
    DWORD round = 2, dark = 1, border = 0x001F1F1F;
    DwmSetWindowAttribute(g_window, 33, &round, sizeof(round));
    DwmSetWindowAttribute(g_window, 20, &dark, sizeof(dark));
    DwmSetWindowAttribute(g_window, 34, &border, sizeof(border));

    std::wstring data = g_temp + L"\\webview";
    // EXO_CINEMA_SETUP_DEVTOOLS: a debugging port (9557) to inspect the page while working on it.
    bool devtools = Env(L"EXO_CINEMA_SETUP_DEVTOOLS");
    auto options = Microsoft::WRL::Make<CoreWebView2EnvironmentOptions>();
    if (devtools) options->put_AdditionalBrowserArguments(L"--remote-debugging-port=9557");
    HRESULT created = CreateCoreWebView2EnvironmentWithOptions(nullptr, data.c_str(), options.Get(),
        Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>([devtools](HRESULT hr, ICoreWebView2Environment* env) -> HRESULT {
            if (FAILED(hr) || !env) { DestroyWindow(g_window); return S_OK; }
            ComPtr<ICoreWebView2Environment> keep(env);
            env->CreateCoreWebView2Controller(g_window, Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                [keep, devtools](HRESULT hr, ICoreWebView2Controller* controller) -> HRESULT {
                    if (FAILED(hr) || !controller) { DestroyWindow(g_window); return S_OK; }
                    g_controller = controller;
                    g_controller->get_CoreWebView2(&g_view);
                    ComPtr<ICoreWebView2Controller2> c2;
                    if (SUCCEEDED(g_controller.As(&c2))) c2->put_DefaultBackgroundColor({255, 0, 0, 0});
                    ComPtr<ICoreWebView2Settings> settings;
                    g_view->get_Settings(&settings);
                    settings->put_AreDefaultContextMenusEnabled(FALSE);
                    settings->put_IsStatusBarEnabled(FALSE);
                    settings->put_IsZoomControlEnabled(FALSE);
                    settings->put_AreDevToolsEnabled(devtools);
                    ComPtr<ICoreWebView2Settings3> s3;
                    if (SUCCEEDED(settings.As(&s3))) s3->put_AreBrowserAcceleratorKeysEnabled(FALSE);
                    g_view->AddWebResourceRequestedFilter((std::wstring(kOrigin) + L"*").c_str(), COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
                    g_view->add_WebResourceRequested(Callback<ICoreWebView2WebResourceRequestedEventHandler>(
                        [keep](ICoreWebView2*, ICoreWebView2WebResourceRequestedEventArgs* args) -> HRESULT { Serve(keep.Get(), args); return S_OK; }).Get(), nullptr);
                    g_view->add_WebMessageReceived(Callback<ICoreWebView2WebMessageReceivedEventHandler>(
                        [](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                            LPWSTR text = nullptr;
                            if (SUCCEEDED(args->TryGetWebMessageAsString(&text)) && text) OnMessage(text);
                            CoTaskMemFree(text);
                            return S_OK;
                        }).Get(), nullptr);
                    // Only Setup's own page: anything else opens nowhere.
                    g_view->add_NavigationStarting(Callback<ICoreWebView2NavigationStartingEventHandler>(
                        [](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                            LPWSTR uri = nullptr;
                            args->get_Uri(&uri);
                            if (!uri || wcsncmp(uri, kOrigin, wcslen(kOrigin)) != 0) args->put_Cancel(TRUE);
                            CoTaskMemFree(uri);
                            return S_OK;
                        }).Get(), nullptr);
                    g_view->add_NewWindowRequested(Callback<ICoreWebView2NewWindowRequestedEventHandler>(
                        [](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT { args->put_Handled(TRUE); return S_OK; }).Get(), nullptr);
                    Resize();
                    g_view->Navigate((std::wstring(kOrigin) + L"index.html").c_str());
                    // Shown once the page has drawn ("shown"), or after a moment at the latest.
                    SetTimer(g_window, 1, 1500, [](HWND hwnd, UINT, UINT_PTR id, DWORD) { KillTimer(hwnd, id); ShowWindow(hwnd, SW_SHOW); });
                    return S_OK;
                }).Get());
            return S_OK;
        }).Get());
    if (FAILED(created)) {
        DestroyWindow(g_window);
        result = Fallback();
    } else {
        MSG msg;
        while (GetMessageW(&msg, nullptr, 0, 0)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        if (!g_view) result = Fallback(); // WebView2 present but would not start
    }
    g_controller.Reset();
    g_view.Reset();
    // Setup leaves nothing behind: its unpacked engine, status file and page data (once WebView2 lets go).
    for (int i = 0; i < 30 && Exists(g_temp); i++) { DeleteTree(g_temp); if (Exists(g_temp)) Sleep(100); }
    if (g_uninstall && g_self.find(L"ExoCinemaSetup-uninstall-") != std::wstring::npos) DeleteSelfLater();
    CoUninitialize();
    return result;
}
