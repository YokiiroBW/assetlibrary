#include "gallery/Surface.h"
#include "gallery/Uia.h"
#include "TestOwner.h"
#include <oleacc.h>
#include <iostream>

namespace {
void Require(bool value, const char* message) { if (!value) throw message; }
std::shared_ptr<const gallery::Pbgra> Pixels(UINT size) {
    auto result = std::make_shared<gallery::Pbgra>(); result->width = result->height = size; result->stride = size * 4;
    result->pixels.resize(static_cast<size_t>(result->stride) * size, 255); return result;
}
}
int main() {
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    gallery_test::Owner owner; HWND parent = nullptr; gallery::Surface* surface = nullptr; IAccessible* accessible = nullptr;
    try {
        parent = CreateWindowExW(0, L"STATIC", L"test-only hidden large viewport", WS_OVERLAPPEDWINDOW, 0, 0, 1800, 10000,
            nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!parent) throw "budget parent";
        gallery::Callbacks callbacks; callbacks.lifetimeOwner = &owner; callbacks.providerLifetimeOwner = &owner; RECT bounds{0, 0, 1750, 9900};
        Require(SUCCEEDED(gallery::Surface::Create(parent, bounds, callbacks, &surface)), "budget surface");
        snapshot::Page page; page.status = snapshot::Status::Ready; page.epoch.Data1 = 1;
        for (UINT index = 0; index < snapshot::MaxItems; ++index) {
            snapshot::Entry item; item.kind = snapshot::Kind::File; item.name = L"合成预算测试" + std::to_wstring(index);
            item.node.Data1 = index + 1; item.epoch = page.epoch; page.entries.push_back(item);
        }
        surface->SetPage(page, 1);
        Require(surface->VisibleFileItems().count == snapshot::MaxItems, "all visible candidates retained");
        for (UINT index = 0; index < 29; ++index) Require(surface->SetThumbnail(index, 1, Pixels(64)) == S_OK, "29 small visible thumbnails including item 17 accepted");
        Require(surface->RetainedImageBytes() == 29u * 64u * 64u * 4u, "all 29 small images remain cached");
        surface->SetPage(page, 2);
        for (UINT index = 0; index < 16; ++index) Require(surface->SetThumbnail(index, 2, Pixels(512)) == S_OK, "byte budget fill");
        Require(surface->SetThumbnail(16, 2, Pixels(512)) == E_OUTOFMEMORY, "over-budget item rejected");
        Require(surface->RetainedImageBytes() == gallery::MaxImageBytes, "existing images survive rejection within byte limit");
        const HWND canvas = FindWindowExW(surface->Window(), nullptr, L"STATIC", nullptr); if (!canvas) throw "budget canvas";
        Require(SUCCEEDED(AccessibleObjectFromWindow(canvas, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible, reinterpret_cast<void**>(&accessible))), "budget accessibility");
        VARIANT child{}; child.vt = VT_I4; child.lVal = 17; BSTR description = nullptr;
        Require(SUCCEEDED(accessible->get_accDescription(child, &description)) && description, "budget fallback description");
        const bool honest = wcscmp(description, L"文件，预览不可用") == 0; SysFreeString(description);
        Require(honest, "budget failure does not remain waiting");
        accessible->Release(); accessible = nullptr; surface->Destroy(); delete surface; surface = nullptr;
        std::cout << "after_destroy owner=" << owner.references.load() << " providers=" << gallery::InspectUia().providers << " pending=" << gallery::InspectUia().pendingRetirements << " dispatcher=" << gallery::InspectUia().dispatcherWindows << '\n';
        const ULONGLONG drainDeadline = GetTickCount64() + 2000;
        while (owner.references > 1 && GetTickCount64() < drainDeadline) {
            MsgWaitForMultipleObjects(0,nullptr,FALSE,10,QS_ALLINPUT);
            MSG message{}; while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        }
        Require(owner.references == 1 && !owner.wrongThread, "all budget-test owner pins returned on STA");
        Require(gallery::InspectUia().providers == 0 && gallery::InspectUia().pendingRetirements == 0 && gallery::InspectUia().dispatcherWindows == 0, "retired provider/queue/dispatcher fully drained");
        DestroyWindow(parent); parent = nullptr; CoUninitialize();
        std::cout << "gallery_budget: 101 candidates, 29 small images, item17 and honest 16MiB rejection passed\n"; return 0;
    } catch (const char* error) {
        std::cerr << error << '\n'; if (accessible) accessible->Release(); if (surface) delete surface; if (parent) DestroyWindow(parent); CoUninitialize(); return 1;
    }
}
