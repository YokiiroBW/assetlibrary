#include "gallery/Accessible.h"
#include "gallery/Uia.h"
#include "TestOwner.h"
#include <iostream>
#include <limits>

namespace {
bool unpinnedDispatcher = false;
void Require(bool condition, const char* message) { if (!condition) throw message; }
void Pump() {
    MSG message{}; while (PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
}
std::shared_ptr<gallery::AccessibleModel> Model(gallery_test::Owner& owner, HWND window) {
    auto model = std::make_shared<gallery::AccessibleModel>(); model->lifetimeOwner = &owner; model->window = window;
    model->page.status = snapshot::Status::Ready;
    for (UINT at=0; at<snapshot::MaxItems; ++at) {
        snapshot::Entry entry; entry.kind = at == 0 ? snapshot::Kind::Directory : snapshot::Kind::File;
        entry.node.Data1 = at + 1; entry.name = L"current synthetic item " + std::to_wstring(at);
        model->page.entries.push_back(entry); model->order[at] = at; model->bounds[at] = {0,static_cast<LONG>(at*30),100,static_cast<LONG>(at*30+25)};
    }
    return model;
}
}
int wmain() {
    if (FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED))) return 2;
    gallery_test::Owner providerOwner, viewOwner;
    providerOwner.releaseObserved = [](ULONG count) noexcept { if (count == 1 && gallery::InspectUia().dispatcherWindows) unpinnedDispatcher = true; };
    HWND window = CreateWindowExW(0,L"STATIC",L"test",WS_OVERLAPPEDWINDOW,0,0,900,700,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    std::vector<IRawElementProviderSimple*> retained;
    std::vector<std::shared_ptr<gallery::AccessibleModel>> models;
    gallery::Surface* surface = nullptr;
    int result = 1;
    try {
        Require(window != nullptr,"test parent");
        auto model = Model(providerOwner,window); models.push_back(model);
        IRawElementProviderSimple* root = nullptr; Require(SUCCEEDED(gallery::CreateUiaRoot(model,&root)),"native root"); retained.push_back(root);
        IRawElementProviderFragment* tree = nullptr; root->QueryInterface(IID_PPV_ARGS(&tree)); Require(tree != nullptr,"root fragment");
        IRawElementProviderFragment* child = nullptr; tree->Navigate(NavigateDirection_FirstChild,&child); tree->Release(); Require(child != nullptr,"native first child");
        IRawElementProviderSimple* item = nullptr; child->QueryInterface(IID_PPV_ARGS(&item)); retained.push_back(item);
        IInvokeProvider* invoke = nullptr; Require(SUCCEEDED(child->QueryInterface(IID_PPV_ARGS(&invoke))),"directory immutable Invoke capability"); invoke->Release();
        SAFEARRAY* identity = nullptr; Require(SUCCEEDED(child->GetRuntimeId(&identity)) && identity,"item runtime identity"); SafeArrayDestroy(identity);
        Require(SUCCEEDED(gallery::RetireUia(model)),"retire presentation"); ++model->presentation;
        model->page.entries[0].name = L"replacement name must not escape";
        VARIANT name{}; Require(item->GetPropertyValue(UIA_NamePropertyId,&name) == UIA_E_ELEMENTNOTAVAILABLE && name.vt == VT_EMPTY,"old provider refuses new name");
        Require(SUCCEEDED(child->QueryInterface(IID_PPV_ARGS(&invoke))),"retired QI capability remains static");
        Require(invoke->Invoke() == UIA_E_ELEMENTNOTAVAILABLE,"retired invocation denied"); invoke->Release(); child->Release();
        ISelectionProvider* selection = nullptr; root->QueryInterface(IID_PPV_ARGS(&selection)); SAFEARRAY* stale = nullptr;
        Require(selection && selection->GetSelection(&stale) == UIA_E_ELEMENTNOTAVAILABLE && !stale,"provider directly refuses retired selection"); selection->Release();
        Pump(); for (auto* value : retained) value->Release(); retained.clear(); models.clear(); model.reset();
        Require(gallery::InspectUia().providers == 0 && gallery::InspectUia().dispatcherWindows == 0 && !unpinnedDispatcher && providerOwner.references == 1,"retired direct provider cleanup");

        // Fill every reserved retirement slot without pumping. Five full pages
        // exceed the rejected four-batch design; the final partial page hits 512.
        while (retained.size() < 512) {
            model = Model(providerOwner,window); models.push_back(model);
            Require(SUCCEEDED(gallery::CreateUiaRoot(model,&root)),"quota root"); retained.push_back(root);
            root->QueryInterface(IID_PPV_ARGS(&tree)); child = nullptr; tree->Navigate(NavigateDirection_FirstChild,&child); tree->Release();
            while (child && retained.size() < 512) {
                item = nullptr; child->QueryInterface(IID_PPV_ARGS(&item)); retained.push_back(item);
                IRawElementProviderFragment* next = nullptr;
                if (retained.size() < 512) Require(SUCCEEDED(child->Navigate(NavigateDirection_NextSibling,&next)),"bounded next child");
                child->Release(); child = next;
            }
            if (child) child->Release();
        }
        Require(gallery::InspectUia().providers == 512,"provider quota reached exactly");
        auto excess = Model(providerOwner,window); root = nullptr;
        Require(gallery::CreateUiaRoot(excess,&root) == E_OUTOFMEMORY && !root,"provider 513 refused");
        gallery::Callbacks callbacks; callbacks.lifetimeOwner = &viewOwner; callbacks.providerLifetimeOwner = &providerOwner;
        RECT bounds{0,0,800,600}; Require(SUCCEEDED(gallery::Surface::Create(window,bounds,callbacks,&surface)),"quota fallback surface");
        surface->SetPage(excess->page,1); const HWND canvas = FindWindowExW(surface->Window(),nullptr,L"STATIC",nullptr);
        Require(SendMessageW(canvas,WM_GETOBJECT,0,static_cast<LPARAM>(UiaRootObjectId)) == 0,"unavailable native provider");
        Require(SendMessageW(canvas,WM_GETOBJECT,0,OBJID_CLIENT) == 0,"quota failure cannot expose custom MSAA bridge fallback");
        surface->Destroy(); delete surface; surface = nullptr; Require(viewOwner.references == 1,"provider never retains View");
        for (auto& old : models) { Require(SUCCEEDED(gallery::RetireUia(old)),"all bounded retirements admitted"); old->alive = false; old->page.entries.clear(); }
        Require(gallery::InspectUia().pendingRetirements == 512,"all 512 objects queued without allocation");
        Pump(); Require(gallery::InspectUia().pendingRetirements == 0,"last destroyed page also drained");
        for (auto* value : retained) value->Release(); retained.clear(); models.clear(); model.reset();
        Require(gallery::InspectUia().providers == 0 && gallery::InspectUia().dispatcherWindows == 0 && !unpinnedDispatcher && providerOwner.references == 1 && !providerOwner.wrongThread,"all independent DLL leases returned on STA");
        std::cout << "gallery_uia: immutable interfaces, stale queries, 512 admission/retirement and no legacy fallback passed\n"; result = 0;
    } catch (const char* message) { std::cerr << message << '\n'; }
    if (surface) delete surface;
    for (auto& model : models) { gallery::RetireUia(model); model->alive = false; }
    Pump(); for (auto* value : retained) value->Release(); retained.clear(); models.clear();
    if (window) DestroyWindow(window); CoUninitialize(); return result;
}
