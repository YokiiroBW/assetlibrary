#include "gallery/Surface.h"
#include "TestOwner.h"
#include <shlobj.h>
#include <oleacc.h>
#include <iostream>

namespace {
void Require(bool value, const char* message) { if (!value) throw message; }
void Drain(gallery_test::Owner& owner) {
    const ULONGLONG deadline = GetTickCount64() + 2000;
    while (owner.references > 1 && GetTickCount64() < deadline) {
        MsgWaitForMultipleObjects(0, nullptr, FALSE, 10, QS_ALLINPUT);
        MSG message{}; while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
}
struct Scenario {
    gallery::Surface* surface = nullptr;
    IAccessible* accessible = nullptr;
    UINT viewportChanges = 0, activations = 0, refreshes = 0, menus = 0;
    bool destroyOnViewport = false, deleteOnViewport = false, verifyEmpty = false, observedEmpty = false;
    WPARAM forwardedFirst = 0; LPARAM forwardedSecond = 0;
};
snapshot::Page Page() {
    snapshot::Page page; page.status = snapshot::Status::Ready; page.epoch.Data1 = 7;
    for (UINT index = 0; index < 30; ++index) {
        snapshot::Entry item; item.epoch = page.epoch; item.node.Data1 = index + 1; item.status = snapshot::Status::Ready;
        item.kind = index == 0 ? snapshot::Kind::Directory : snapshot::Kind::File;
        item.name = index == 0 ? L"真实目录" : L"合成图库测试条目，长名称必须保持完整可访问" + std::to_wstring(index);
        page.entries.push_back(item);
    }
    return page;
}
std::shared_ptr<const gallery::Pbgra> Image() {
    auto image = std::make_shared<gallery::Pbgra>(); image->width = 256; image->height = 128; image->stride = 1024;
    image->pixels.resize(1024 * 128);
    for (size_t at = 0; at < image->pixels.size(); at += 4) { image->pixels[at] = 64; image->pixels[at + 1] = 96; image->pixels[at + 2] = 128; image->pixels[at + 3] = 128; }
    return image;
}
}

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) return 2;
    HWND parent = nullptr; gallery::Surface* surface = nullptr; IAccessible* accessible = nullptr;
    gallery_test::Owner owner; Scenario scenario;
    try {
        parent = CreateWindowExW(0, L"STATIC", L"AssetLibrary test-only hidden harness", WS_OVERLAPPEDWINDOW, 0, 0, 1000, 700,
            nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!parent) throw "parent creation";
        gallery::Callbacks callbacks; callbacks.context = &scenario; callbacks.lifetimeOwner = &owner;
        callbacks.viewportChanged = [](void* context) noexcept {
            auto& state = *static_cast<Scenario*>(context); ++state.viewportChanges;
            if (state.verifyEmpty && state.surface && state.accessible) {
                LONG count = -1;
                state.observedEmpty = state.surface->RetainedImageBytes() == 0 && state.surface->SelectedItems().count == 0 &&
                    SUCCEEDED(state.accessible->get_accChildCount(&count)) && count == 0;
            }
            if (state.destroyOnViewport && state.surface) state.surface->Destroy();
            if (state.deleteOnViewport && state.surface) { auto* victim = state.surface; state.surface = nullptr; delete victim; }
        };
        callbacks.activateItem = [](void* context, UINT) noexcept { ++static_cast<Scenario*>(context)->activations; };
        callbacks.refresh = [](void* context) noexcept { ++static_cast<Scenario*>(context)->refreshes; };
        callbacks.contextMenu = [](void* context, int, POINT) noexcept { ++static_cast<Scenario*>(context)->menus; };
        callbacks.completionMessage = WM_APP + 91;
        callbacks.completion = [](void* context, WPARAM first, LPARAM second) noexcept -> LRESULT {
            auto& state = *static_cast<Scenario*>(context); state.forwardedFirst = first; state.forwardedSecond = second; return 42;
        };
        RECT bounds{0, 0, 1000, 700};
        Require(SUCCEEDED(gallery::Surface::Create(parent, bounds, callbacks, &surface)), "surface creation"); scenario.surface = surface;
        const auto page = Page(); Require(SUCCEEDED(surface->SetPage(page, 1)), "page commit");
        auto files = surface->VisibleFileItems(); Require(files.count > 0 && files.count <= 16, "visible files");
        Require(SUCCEEDED(surface->SetThumbnail(files.indices[0], 1, Image())), "thumbnail accepted");
        Require(surface->RetainedImageBytes() > 0 && surface->RetainedImageBytes() <= gallery::MaxImageBytes, "image budget");
        Require(surface->SetThumbnail(files.indices[0], 0, Image()) == S_FALSE, "late image rejected");
        Require(surface->SetThumbnail(0, 1, Image()) == S_FALSE, "directory image rejected");
        auto malformed = std::make_shared<gallery::Pbgra>(); malformed->width = 513; malformed->height = 1;
        Require(surface->SetThumbnail(files.indices[0], 1, malformed) == E_INVALIDARG, "malformed dimensions rejected");
        auto reserved = std::make_shared<gallery::Pbgra>(); reserved->width = reserved->height = 1; reserved->stride = 4;
        reserved->pixels.resize(4); reserved->pixels.reserve(gallery::MaxImageBytes + 1);
        Require(surface->SetThumbnail(files.indices[0], 1, reserved) == E_OUTOFMEMORY, "persistent byte budget counts vector capacity");
        Require(SUCCEEDED(surface->SelectItem(1, SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_FOCUSED)), "selection");
        Require(surface->FocusedItem() == 1 && surface->SelectedItems().count == 1, "selection readback");
        surface->SetDensity(1); Require(surface->Density() == 96, "density lower bound");
        surface->SetDensity(900); Require(surface->Density() == 256, "density upper bound");
        surface->SetMode(gallery::Mode::List); Require(surface->CurrentMode() == gallery::Mode::List, "list mode");
        const HWND canvas = FindWindowExW(surface->Window(), nullptr, L"STATIC", nullptr);
        if (!canvas) throw "canvas window";
        Require(SUCCEEDED(AccessibleObjectFromWindow(canvas, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible, reinterpret_cast<void**>(&accessible))), "MSAA exposure");
        scenario.accessible = accessible;
        LONG count = 0; Require(SUCCEEDED(accessible->get_accChildCount(&count)) && count == 30, "MSAA child count");
        VARIANT child{}; child.vt = VT_I4; child.lVal = 2; BSTR name = nullptr;
        Require(SUCCEEDED(accessible->get_accName(child, &name)) && name && page.entries[1].name == name, "full accessible name"); SysFreeString(name);
        Require(SendMessageW(surface->Window(), callbacks.completionMessage, 17, 19) == 42 && scenario.forwardedFirst == 17 && scenario.forwardedSecond == 19, "owner message opaque forwarding");
        MSG key{}; key.hwnd = canvas; key.message = WM_KEYDOWN; key.wParam = VK_TAB;
        Require(surface->TranslateAccelerator(key), "internal Tab reaches native toolbar");
        key.hwnd = GetDlgItem(surface->Window(), 105);
        Require(!surface->TranslateAccelerator(key), "boundary Tab remains owned by Shell"); key.hwnd = canvas;
        key.wParam = VK_F5; Require(surface->TranslateAccelerator(key) && scenario.refreshes == 1, "F5 owner callback");
        key.wParam = VK_HOME; Require(surface->TranslateAccelerator(key) && surface->FocusedItem() == 0, "Home selects first displayed item");
        key.wParam = VK_RETURN; Require(surface->TranslateAccelerator(key) && scenario.activations == 1, "Enter activates existing navigation callback");
        key.wParam = VK_APPS; Require(surface->TranslateAccelerator(key) && scenario.menus == 1, "keyboard context menu");
        surface->SetPage(page, 1); files = surface->VisibleFileItems();
        Require(files.count > 0, "scroll test visible candidates");
        surface->SetThumbnail(files.indices[0], 1, Image());
        const UINT beforeScroll = scenario.viewportChanges;
        key.wParam = VK_END; Require(surface->TranslateAccelerator(key) && surface->FocusedItem() == 29, "End focuses final page item");
        Require(scenario.viewportChanges > beforeScroll && surface->RetainedImageBytes() == 0, "scroll cancels old viewport and releases its pixels");
        key.wParam = VK_HOME; surface->TranslateAccelerator(key);
        child.lVal = 2; Require(SUCCEEDED(accessible->accSelect(SELFLAG_TAKESELECTION, child)), "MSAA selection replace");
        child.lVal = 5; Require(SUCCEEDED(accessible->accSelect(SELFLAG_EXTENDSELECTION, child)) && surface->SelectedItems().count >= 4, "MSAA range selection");
        surface->SetVisible(false); Require(surface->VisibleFileItems().count == 0 && surface->RetainedImageBytes() == 0, "hidden releases images");
        scenario.verifyEmpty = true;
        surface->Clear(snapshot::Status::AccessDenied, 2);
        Require(scenario.observedEmpty, "clear names and images before callback"); scenario.verifyEmpty = false;
        Require(surface->SelectedItems().count == 0 && surface->FocusedItem() == -1, "clear selection");
        Require(SUCCEEDED(accessible->get_accChildCount(&count)) && count == 0, "clear accessible names");
        Require(surface->SetPage(page, 1) == S_FALSE, "late page cannot republish old names");
        surface->SetVisible(true); surface->SetPage(page, 3);
        scenario.destroyOnViewport = true; surface->SetVisible(false);
        Require(surface->Window() == nullptr && surface->RetainedImageBytes() == 0, "callback destruction");
        Require(SUCCEEDED(accessible->get_accChildCount(&count)) && count == 0, "retired provider empty");
        accessible->Release(); accessible = nullptr; delete surface; surface = nullptr;
        Drain(owner);
        Require(owner.references == 1, "accessibility and HWND owner references released");
        Require(!owner.wrongThread, "MSAA owner remains on its creating STA");
        scenario = {};
        Require(SUCCEEDED(gallery::Surface::Create(parent, bounds, callbacks, &surface)), "second surface creation"); scenario.surface = surface;
        surface->SetPage(page, 1);
        const HWND secondCanvas = FindWindowExW(surface->Window(), nullptr, L"STATIC", nullptr);
        if (!secondCanvas) throw "second canvas";
        Require(SUCCEEDED(AccessibleObjectFromWindow(secondCanvas, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible, reinterpret_cast<void**>(&accessible))), "retained provider for external destruction");
        const HWND retiredWindow = surface->Window(); DestroyWindow(retiredWindow);
        Require(surface->Window() == nullptr && surface->RetainedImageBytes() == 0, "external DestroyWindow retires the surface");
        delete surface; surface = nullptr;
        Require(owner.references > 1 && SUCCEEDED(accessible->get_accChildCount(&count)) && count == 0, "retained provider keeps owner alive without old names");
        accessible->Release(); accessible = nullptr; Drain(owner); Require(owner.references == 1, "external destruction owner references reclaimed");
        scenario = {};
        Require(SUCCEEDED(gallery::Surface::Create(parent, bounds, callbacks, &surface)), "third surface creation"); scenario.surface = surface;
        surface->Clear(snapshot::Status::Loading, 0);
        UINT visibilityChanges = scenario.viewportChanges;
        surface->SetVisible(false); Require(scenario.viewportChanges == visibilityChanges + 1, "Loading empty candidates still notify hide exactly once");
        surface->SetVisible(true); Require(scenario.viewportChanges == visibilityChanges + 2, "Loading empty candidates still notify show exactly once");
        snapshot::Page empty; empty.status = snapshot::Status::Ready; surface->SetPage(empty, 0);
        visibilityChanges = scenario.viewportChanges;
        ShowWindow(surface->Window(), SW_HIDE); Require(scenario.viewportChanges == visibilityChanges + 1, "native empty-page hide notification");
        ShowWindow(surface->Window(), SW_SHOWNA); Require(scenario.viewportChanges == visibilityChanges + 2, "native empty-page show notification");
        surface->SetPage(page, 1); scenario.deleteOnViewport = true;
        surface->SetVisible(false); surface = scenario.surface;
        Require(surface == nullptr && owner.references == 1, "callback may delete the C++ Surface while its public method is running");
        DestroyWindow(parent); parent = nullptr;
        std::cout << "gallery_surface: hidden HWND/page/image/selection/MSAA/retirement checks passed\n";
        CoUninitialize(); return 0;
    } catch (const char* error) {
        std::cerr << error << '\n'; if (accessible) accessible->Release(); if (surface) delete surface; if (parent) DestroyWindow(parent);
        CoUninitialize(); return 1;
    }
}
