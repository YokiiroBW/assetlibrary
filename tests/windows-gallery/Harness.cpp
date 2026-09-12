#include "gallery/Surface.h"
#include "gallery/Accessible.h"
#include "gallery/Uia.h"
#include "TestOwner.h"
#include "Capture.h"
#include <commctrl.h>
#include <iostream>
#include <cwchar>

namespace {
constexpr wchar_t FrameClass[] = L"AssetLibrary.GalleryHarness.Frame";
bool RegisterFrame() noexcept {
    WNDCLASSW type{}; type.style = CS_DBLCLKS; type.lpfnWndProc = DefWindowProcW;
    type.hInstance = GetModuleHandleW(nullptr); type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    type.hbrBackground = GetSysColorBrush(COLOR_WINDOW); type.lpszClassName = FrameClass;
    return RegisterClassW(&type) != 0;
}
struct Harness {
    gallery_test::Owner owner;
    gallery::Surface* surface = nullptr;
    UINT activated = 0;
    bool populating = false, pending = false, fixtureFailed = false, retireOnAction = false, replaceOnAction = false, retiredClear = false;
    ~Harness() { if (surface) delete surface; }
};
snapshot::Page FixturePage() {
    snapshot::Page page; page.status = snapshot::Status::Ready; page.epoch.Data1 = 11;
    for (UINT index = 0; index < 30; ++index) {
        snapshot::Entry entry; entry.epoch = page.epoch; entry.node.Data1 = index + 1; entry.status = snapshot::Status::Ready;
        entry.kind = index == 0 ? snapshot::Kind::Directory : snapshot::Kind::File;
        entry.name = L"合成图库测试条目（非产品图片）" + std::to_wstring(index); page.entries.push_back(entry);
    }
    return page;
}
std::shared_ptr<const gallery::Pbgra> FixtureImage(UINT index) {
    auto image = std::make_shared<gallery::Pbgra>(); image->width = index % 3 == 0 ? 192 : 256; image->height = index % 3 == 0 ? 256 : 144;
    image->stride = image->width * 4; image->pixels.resize(static_cast<size_t>(image->stride) * image->height);
    for (UINT y = 0; y < image->height; ++y) for (UINT x = 0; x < image->width; ++x) {
        const size_t at = static_cast<size_t>(y) * image->stride + x * 4;
        image->pixels[at] = static_cast<BYTE>(x * 180 / image->width);
        image->pixels[at + 1] = static_cast<BYTE>(y * 170 / image->height);
        image->pixels[at + 2] = static_cast<BYTE>(60 + index % 6 * 24); image->pixels[at + 3] = 255;
    }
    return image;
}
void Populate(Harness& harness) noexcept {
    if (!harness.surface) return;
    if (harness.populating) { harness.pending = true; return; }
    harness.populating = true;
    try {
        for (UINT pass = 0; pass < 4; ++pass) {
            harness.pending = false; const auto visible = harness.surface->VisibleFileItems();
            for (UINT at = 0; at < visible.count; ++at) harness.surface->SetThumbnail(visible.indices[at], 1, FixtureImage(visible.indices[at]));
            if (!harness.pending) break;
        }
    } catch (const std::bad_alloc&) { harness.fixtureFailed = true; }
    harness.populating = false;
}
LRESULT CALLBACK FrameProc(HWND window, UINT message, WPARAM first, LPARAM second, UINT_PTR id, DWORD_PTR data) {
    auto* harness = reinterpret_cast<Harness*>(data);
    if (message == WM_SIZE && harness->surface && harness->surface->Window()) {
        RECT client{}; GetClientRect(window, &client); MoveWindow(harness->surface->Window(), 0, 0, client.right, client.bottom, TRUE);
    }
    if (message == WM_CLOSE) { if (harness->surface) harness->surface->Destroy(); DestroyWindow(window); return 0; }
    if (message == WM_NCDESTROY) { RemoveWindowSubclass(window, FrameProc, id); PostQuitMessage(0); }
    return DefSubclassProc(window, message, first, second);
}
DWORD RunExternal(const wchar_t* executable, HWND canvas, bool retire, const wchar_t* variant) {
    std::wstring command = L"\"" + std::wstring(executable) + L"\" " + std::to_wstring(reinterpret_cast<UINT_PTR>(canvas));
    if (retire) command += L" " + std::wstring(variant);
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE}; HANDLE outputRead = nullptr, outputWrite = nullptr;
    if (!CreatePipe(&outputRead, &outputWrite, &security, 0)) return 3;
    SetHandleInformation(outputRead, HANDLE_FLAG_INHERIT, 0);
    HANDLE input = CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &security, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    SIZE_T attributeBytes = 0; InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
    std::vector<BYTE> storage(attributeBytes); auto* attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    if (!attributes || input == INVALID_HANDLE_VALUE || !InitializeProcThreadAttributeList(attributes, 1, 0, &attributeBytes)) {
        if (input != INVALID_HANDLE_VALUE) CloseHandle(input); CloseHandle(outputRead); CloseHandle(outputWrite); return 3;
    }
    HANDLE inherited[] = {outputWrite, input};
    STARTUPINFOEXW startup{}; startup.StartupInfo.cb = sizeof(startup); startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    startup.StartupInfo.hStdOutput = startup.StartupInfo.hStdError = outputWrite; startup.StartupInfo.hStdInput = input;
    startup.lpAttributeList = attributes; PROCESS_INFORMATION process{};
    const BOOL configured = UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited), nullptr, nullptr);
    const BOOL started = configured && CreateProcessW(executable, command.data(), nullptr, nullptr, TRUE,
        CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT, nullptr, nullptr, &startup.StartupInfo, &process);
    DeleteProcThreadAttributeList(attributes); CloseHandle(outputWrite); CloseHandle(input);
    if (!started) { CloseHandle(outputRead); return 3; }
    CloseHandle(process.hThread); const ULONGLONG deadline = GetTickCount64() + 15000; DWORD exit = 3;
    for (;;) {
        const ULONGLONG now = GetTickCount64();
        if (now >= deadline) { TerminateProcess(process.hProcess, 3); WaitForSingleObject(process.hProcess, 2000); break; }
        const DWORD wait = MsgWaitForMultipleObjects(1, &process.hProcess, FALSE, static_cast<DWORD>(deadline - now), QS_ALLINPUT);
        if (wait == WAIT_OBJECT_0) { GetExitCodeProcess(process.hProcess, &exit); break; }
        if (wait != WAIT_OBJECT_0 + 1) break;
        MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    char output[4096]; DWORD available = 0, received = 0;
    for (UINT part = 0; part < 4 && PeekNamedPipe(outputRead, nullptr, 0, nullptr, &available, nullptr) && available > 0; ++part) {
        if (!ReadFile(outputRead, output, std::min<DWORD>(available, sizeof(output)), &received, nullptr)) break;
        std::cout.write(output, received);
    }
    CloseHandle(outputRead); CloseHandle(process.hProcess); return exit;
}
}

int RunCase(int argc, wchar_t** argv, Harness& harness, bool retentionDiagnostic) {
    const bool show = argc == 2 && wcscmp(argv[1], L"--show") == 0;
    const bool variant = argc == 3 && (wcscmp(argv[1], L"--msaa-retire") == 0 || wcscmp(argv[1], L"--element-retire") == 0 || wcscmp(argv[1], L"--released-retire") == 0);
    const bool replace = argc == 3 && wcscmp(argv[1], L"--external-page") == 0;
    const bool retire = argc == 3 && (wcscmp(argv[1], L"--external-retire") == 0 || variant);
    const bool external = argc == 3 && (wcscmp(argv[1], L"--external") == 0 || retire || replace);
    const bool render = argc == 4 && wcscmp(argv[1], L"--render") == 0;
    if (!show && !external && !render) { std::cerr << "test-only harness: --show, --external <probe.exe>, or --render <new.bmp> <width>\n"; return 2; }
    harness.retireOnAction = retire; harness.replaceOnAction = replace; int result = 1;
    const int width = render ? std::max(240, std::min(1920, _wtoi(argv[3]))) : 1040;
    HWND frame = CreateWindowExW(0, FrameClass, L"AssetLibrary 图库验证 — 合成图像 / 非产品客户端", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, width, 760, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (frame) {
        SetWindowSubclass(frame, FrameProc, 1, reinterpret_cast<DWORD_PTR>(&harness));
        gallery::Callbacks callbacks; callbacks.context = &harness; callbacks.lifetimeOwner = &harness.owner; callbacks.providerLifetimeOwner = &harness.owner;
        callbacks.activateItem = [](void* context, UINT) noexcept {
            auto& state = *static_cast<Harness*>(context); ++state.activated;
            if (state.retireOnAction && state.surface) state.surface->Destroy();
            else if (state.replaceOnAction && state.surface) { auto page = FixturePage(); page.entries[0].name = L"新页合成目录"; state.surface->SetPage(page, 2); }
        };
        callbacks.viewportChanged = [](void* context) noexcept { Populate(*static_cast<Harness*>(context)); };
        RECT client{}; GetClientRect(frame, &client);
        if (SUCCEEDED(gallery::Surface::Create(frame, client, callbacks, &harness.surface))) {
            harness.surface->SetPage(FixturePage(), 1);
            Populate(harness);
            if (external) {
                const HWND canvas = FindWindowExW(harness.surface->Window(), nullptr, L"STATIC", nullptr);
                result = static_cast<int>(RunExternal(argv[2], canvas, retire || replace, replace ? L"--page" : variant ? argv[1] : L"--retire"));
                if (result != 0) std::cerr << "external accessibility stage failed: " << result << '\n';
                const ULONGLONG actionDeadline = GetTickCount64() + 2000;
                while (result == 0 && harness.activated == 0 && GetTickCount64() < actionDeadline) {
                    MsgWaitForMultipleObjects(0, nullptr, FALSE, 10, QS_ALLINPUT);
                    MSG action{}; while (PeekMessageW(&action, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&action); DispatchMessageW(&action); }
                }
                if (result == 0 && harness.activated == 0) result = 4;
            } else if (render) {
                const HWND canvas = FindWindowExW(harness.surface->Window(), nullptr, L"STATIC", nullptr);
                RECT rectangle{}; GetClientRect(canvas, &rectangle);
                try { gallery_test::Capture output(rectangle.right, rectangle.bottom); output.Draw(canvas); output.Save(argv[2]); result = 0; }
                catch (const char* error) { std::cerr << error << '\n'; result = 1; }
            } else {
                ShowWindow(frame, SW_SHOW); MSG message{};
                while (GetMessageW(&message, nullptr, 0, 0) > 0) {
                    if (!harness.surface->TranslateAccelerator(message) && !IsDialogMessageW(frame, &message)) { TranslateMessage(&message); DispatchMessageW(&message); }
                }
                result = 0;
            }
            harness.surface->Destroy();
            harness.retiredClear = harness.surface->RetainedImageBytes() == 0 && harness.surface->SelectedItems().count == 0;
        }
        if (IsWindow(frame)) DestroyWindow(frame);
    }
    if (harness.surface) { delete harness.surface; harness.surface = nullptr; }
    if (harness.fixtureFailed) result = 6;
    if (external && result == 0 && !retentionDiagnostic) {
        const ULONGLONG deadline = GetTickCount64() + 2000;
        while (harness.owner.references > 1 && GetTickCount64() < deadline) {
            MsgWaitForMultipleObjects(0, nullptr, FALSE, 20, QS_ALLINPUT);
            MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        }
        if (harness.owner.wrongThread || harness.owner.references != 1 || gallery::InspectUia().dispatcherWindows != 0) {
            std::cerr << "external provider owner release did not complete on creating STA: references=" << harness.owner.references.load()
                << " wrongThread=" << harness.owner.wrongThread.load() << " providers=" << gallery::InspectAccessibility().providers
                << " nativeProviders=" << gallery::InspectUia().providers << " dispatcherWindows=" << gallery::InspectUia().dispatcherWindows << " pending=" << gallery::InspectUia().pendingRetirements << " disconnected=" << gallery::InspectUia().disconnected << " nativeResult=0x" << std::hex << static_cast<ULONG>(gallery::InspectUia().lastResult) << std::dec << '\n'; result = 5;
        }
    }
    return result;
}

void Pump(DWORD wait) {
    MsgWaitForMultipleObjects(0, nullptr, FALSE, wait, QS_ALLINPUT);
    MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
}
void PrintRetained(const std::array<Harness,3>& cases, const char* phase) {
    std::cout << phase << " owners=[" << cases[0].owner.references.load() << "," << cases[1].owner.references.load()
        << "," << cases[2].owner.references.load() << "] providers=" << gallery::InspectAccessibility().providers
        << " nativeProviders=" << gallery::InspectUia().providers << " dispatcherWindows=" << gallery::InspectUia().dispatcherWindows << " pending=" << gallery::InspectUia().pendingRetirements << " disconnected=" << gallery::InspectUia().disconnected
        << " cleared=[" << cases[0].retiredClear << "," << cases[1].retiredClear << "," << cases[2].retiredClear << "]\n" << std::flush;
}
int wmain(int argc, wchar_t** argv) {
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    if (!RegisterFrame()) { CoUninitialize(); return 3; }
    if (argc == 3 && wcscmp(argv[1], L"--retention-cycles") == 0) {
        std::array<Harness,3> cases;
        wchar_t mode[] = L"--external-retire"; wchar_t* arguments[] = {argv[0], mode, argv[2]};
        int result = 0;
        for (size_t index = 0; index < cases.size(); ++index) {
            const int current = RunCase(3, arguments, cases[index], true); if (current != 0) result = current;
            Pump(100); PrintRetained(cases, "after_case");
        }
        const ULONGLONG deadline = GetTickCount64() + 30000;
        while (GetTickCount64() < deadline && (cases[0].owner.references > 1 || cases[1].owner.references > 1 || cases[2].owner.references > 1)) Pump(50);
        PrintRetained(cases, "after_30s_max");
        if (cases[0].owner.references != 1 || cases[1].owner.references != 1 || cases[2].owner.references != 1 ||
            gallery::InspectUia().providers || gallery::InspectUia().pendingRetirements || gallery::InspectUia().dispatcherWindows ||
            gallery::InspectAccessibility().providers || gallery::InspectAccessibility().enumerators) result = 5;
        UnregisterClassW(FrameClass, GetModuleHandleW(nullptr)); CoUninitialize(); PrintRetained(cases, "after_CoUninitialize");
        return result; // Bounded synthetic regression, not the G4 duration gate.
    }
    Harness harness; const int result = RunCase(argc, argv, harness, false); UnregisterClassW(FrameClass, GetModuleHandleW(nullptr)); CoUninitialize(); return result;
}
