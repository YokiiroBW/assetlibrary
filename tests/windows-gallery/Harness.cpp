#include "gallery/Surface.h"
#include "TestOwner.h"
#include "Capture.h"
#include <commctrl.h>
#include <iostream>
#include <cwchar>

namespace {
struct Harness {
    gallery_test::Owner owner;
    gallery::Surface* surface = nullptr;
    UINT activated = 0;
    bool populating = false, pending = false, fixtureFailed = false;
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
DWORD RunExternal(const wchar_t* executable, HWND canvas) {
    std::wstring command = L"\"" + std::wstring(executable) + L"\" " + std::to_wstring(reinterpret_cast<UINT_PTR>(canvas));
    STARTUPINFOW startup{sizeof(startup)}; PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) return 3;
    CloseHandle(process.hThread); const ULONGLONG deadline = GetTickCount64() + 15000; DWORD exit = 3;
    for (;;) {
        const ULONGLONG now = GetTickCount64();
        if (now >= deadline) { TerminateProcess(process.hProcess, 3); WaitForSingleObject(process.hProcess, 2000); break; }
        const DWORD wait = MsgWaitForMultipleObjects(1, &process.hProcess, FALSE, static_cast<DWORD>(deadline - now), QS_ALLINPUT);
        if (wait == WAIT_OBJECT_0) { GetExitCodeProcess(process.hProcess, &exit); break; }
        if (wait != WAIT_OBJECT_0 + 1) break;
        MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    CloseHandle(process.hProcess); return exit;
}
}

int wmain(int argc, wchar_t** argv) {
    const bool show = argc == 2 && wcscmp(argv[1], L"--show") == 0;
    const bool external = argc == 3 && wcscmp(argv[1], L"--external") == 0;
    const bool render = argc == 4 && wcscmp(argv[1], L"--render") == 0;
    if (!show && !external && !render) { std::cerr << "test-only harness: --show, --external <probe.exe>, or --render <new.bmp> <width>\n"; return 2; }
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    Harness harness; int result = 1;
    const int width = render ? std::max(240, std::min(1920, _wtoi(argv[3]))) : 1040;
    HWND frame = CreateWindowExW(0, L"STATIC", L"AssetLibrary 图库验证 — 合成图像 / 非产品客户端", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, width, 760, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (frame) {
        SetWindowSubclass(frame, FrameProc, 1, reinterpret_cast<DWORD_PTR>(&harness));
        gallery::Callbacks callbacks; callbacks.context = &harness; callbacks.lifetimeOwner = &harness.owner;
        callbacks.activateItem = [](void* context, UINT) noexcept { ++static_cast<Harness*>(context)->activated; };
        callbacks.viewportChanged = [](void* context) noexcept { Populate(*static_cast<Harness*>(context)); };
        RECT client{}; GetClientRect(frame, &client);
        if (SUCCEEDED(gallery::Surface::Create(frame, client, callbacks, &harness.surface))) {
            harness.surface->SetPage(FixturePage(), 1);
            Populate(harness);
            if (external) {
                const HWND canvas = FindWindowExW(harness.surface->Window(), nullptr, L"STATIC", nullptr);
                result = static_cast<int>(RunExternal(argv[2], canvas));
                if (result != 0) std::cerr << "external accessibility stage failed: " << result << '\n';
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
        }
        if (IsWindow(frame)) DestroyWindow(frame);
    }
    if (harness.surface) { delete harness.surface; harness.surface = nullptr; }
    if (harness.fixtureFailed) result = 6;
    if (external && result == 0) {
        const ULONGLONG deadline = GetTickCount64() + 2000;
        while (harness.owner.references > 1 && GetTickCount64() < deadline) {
            MsgWaitForMultipleObjects(0, nullptr, FALSE, 20, QS_ALLINPUT);
            MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        }
        if (harness.owner.wrongThread || harness.owner.references != 1) { std::cerr << "external provider owner release did not complete on creating STA\n"; result = 5; }
    }
    CoUninitialize(); return result;
}
