#include "Uia.h"
#include "Accessible.h"
#include <shlobj.h>
#include <commctrl.h>
#include <algorithm>
#include <atomic>
#include <new>
#include <cmath>
#include <limits>

namespace gallery {
namespace {
thread_local UiaDiagnostics diagnostics;
constexpr UINT MaxProviders = 512;
constexpr UINT RetireMessage = WM_APP + 0x619;
constexpr WPARAM RetireCookie = 0x55494131;
class Element;
HRESULT Make(const std::shared_ptr<AccessibleModel>&, int, Element**) noexcept;
HRESULT PrepareScheduler(IUnknown* owner) noexcept;
void CleanupScheduler() noexcept;

class Element final : public IRawElementProviderSimple, public IRawElementProviderFragmentRoot, public IRawElementProviderFragment,
    public ISelectionProvider, public ISelectionItemProvider, public IInvokeProvider,
    public IScrollProvider, public IScrollItemProvider {
    std::atomic_ulong references_{1};
    std::shared_ptr<AccessibleModel> model_;
    IUnknown* owner_;
    const std::uint64_t presentation_;
    const int index_;
    const bool invokable_;
    bool retired_ = false;
    bool Live() const noexcept { return !retired_ && model_->alive && presentation_ == model_->presentation &&
        (index_ < 0 || static_cast<size_t>(index_) < model_->page.entries.size()); }
    HRESULT Related(int index, IRawElementProviderFragment** result) noexcept {
        Element* element = nullptr; const auto hr = Make(model_, index, &element);
        if (SUCCEEDED(hr)) *result = static_cast<IRawElementProviderFragment*>(element); return hr;
    }
    SCROLLINFO ScrollInfo() const noexcept {
        SCROLLINFO info{sizeof(info), SIF_ALL}; if (model_->window) GetScrollInfo(model_->window, SB_VERT, &info); return info;
    }
    HRESULT ChangeSelection(UINT flags) noexcept {
        if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        return index_ >= 0 && model_->select ? model_->select(model_->context, static_cast<UINT>(index_), flags) : E_INVALIDARG;
    }
public:
    Element(std::shared_ptr<AccessibleModel> model, int index) noexcept : model_(std::move(model)), owner_(model_->lifetimeOwner),
        presentation_(model_->presentation), index_(index), invokable_(index >= 0 && snapshot::Navigable(model_->page.entries[static_cast<size_t>(index)].kind)) { owner_->AddRef(); ++diagnostics.providers; }
    ~Element() {
        const auto slot = static_cast<size_t>(index_ + 1);
        if (model_->uia[slot] == static_cast<IRawElementProviderSimple*>(this)) model_->uia[slot] = nullptr;
        --diagnostics.providers; CleanupScheduler(); model_.reset(); owner_->Release();
    }
    void Retire() noexcept { retired_ = true; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER; *value = nullptr;
        if (iid == IID_IUnknown || iid == __uuidof(IRawElementProviderSimple)) *value = static_cast<IRawElementProviderSimple*>(this);
        else if (iid == __uuidof(IRawElementProviderFragment)) *value = static_cast<IRawElementProviderFragment*>(this);
        else if (iid == __uuidof(IRawElementProviderFragmentRoot) && index_ < 0) *value = static_cast<IRawElementProviderFragmentRoot*>(this);
        else if (iid == __uuidof(ISelectionProvider) && index_ < 0) *value = static_cast<ISelectionProvider*>(this);
        else if (iid == __uuidof(IScrollProvider) && index_ < 0) *value = static_cast<IScrollProvider*>(this);
        else if (iid == __uuidof(ISelectionItemProvider) && index_ >= 0) *value = static_cast<ISelectionItemProvider*>(this);
        else if (iid == __uuidof(IScrollItemProvider) && index_ >= 0) *value = static_cast<IScrollItemProvider*>(this);
        else if (iid == __uuidof(IInvokeProvider) && invokable_)
            *value = static_cast<IInvokeProvider*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto count = --references_; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE get_ProviderOptions(ProviderOptions* value) override {
        if (!value) return E_POINTER; *value = static_cast<ProviderOptions>(ProviderOptions_ServerSideProvider | ProviderOptions_UseComThreading); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetPatternProvider(PATTERNID pattern, IUnknown** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (index_ < 0 && pattern == UIA_SelectionPatternId) *value = static_cast<ISelectionProvider*>(this);
        else if (index_ < 0 && pattern == UIA_ScrollPatternId) *value = static_cast<IScrollProvider*>(this);
        else if (index_ >= 0 && pattern == UIA_SelectionItemPatternId) *value = static_cast<ISelectionItemProvider*>(this);
        else if (index_ >= 0 && pattern == UIA_ScrollItemPatternId) *value = static_cast<IScrollItemProvider*>(this);
        else if (index_ >= 0 && pattern == UIA_InvokePatternId && snapshot::Navigable(model_->page.entries[static_cast<size_t>(index_)].kind)) *value = static_cast<IInvokeProvider*>(this);
        if (*value) AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetPropertyValue(PROPERTYID property, VARIANT* value) override {
        if (!value) return E_POINTER; VariantInit(value); if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (property == UIA_ControlTypePropertyId) { value->vt = VT_I4; value->lVal = index_ < 0 ? UIA_ListControlTypeId : UIA_ListItemControlTypeId; }
        else if (property == UIA_NamePropertyId) {
            value->vt = VT_BSTR; value->bstrVal = SysAllocString(index_ < 0 ? model_->title.c_str() : model_->page.entries[static_cast<size_t>(index_)].name.c_str());
            if (!value->bstrVal) return E_OUTOFMEMORY;
        } else if (property == UIA_IsControlElementPropertyId || property == UIA_IsContentElementPropertyId || property == UIA_IsKeyboardFocusablePropertyId || property == UIA_IsEnabledPropertyId) {
            value->vt = VT_BOOL; value->boolVal = VARIANT_TRUE;
        } else if (property == UIA_HasKeyboardFocusPropertyId) {
            value->vt = VT_BOOL; value->boolVal = ::GetFocus() == model_->window && (index_ < 0 || model_->focus == index_) ? VARIANT_TRUE : VARIANT_FALSE;
        } else if (property == UIA_IsOffscreenPropertyId) {
            RECT client{}, overlap{}; GetClientRect(model_->window, &client);
            const bool visible = model_->visible && IsWindowVisible(model_->window) &&
                (index_ < 0 || IntersectRect(&overlap, &client, &model_->bounds[static_cast<size_t>(index_)]));
            value->vt = VT_BOOL; value->boolVal = visible ? VARIANT_FALSE : VARIANT_TRUE;
        } else if (property == UIA_AutomationIdPropertyId && index_ >= 0) {
            wchar_t identity[40]{}; if (StringFromGUID2(model_->page.entries[static_cast<size_t>(index_)].node, identity, 40) == 0) return E_FAIL;
            value->vt = VT_BSTR; value->bstrVal = SysAllocString(identity); if (!value->bstrVal) return E_OUTOFMEMORY;
        } else if (property == UIA_ItemStatusPropertyId && index_ >= 0) {
            const auto at = static_cast<size_t>(index_); const wchar_t* status = model_->thumbnailReady[at] ? L"缩略图已就绪" : model_->thumbnailUnavailable[at] ? L"预览不可用" : L"";
            value->vt = VT_BSTR; value->bstrVal = SysAllocString(status); if (!value->bstrVal) return E_OUTOFMEMORY;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_HostRawElementProvider(IRawElementProviderSimple** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        return index_ < 0 ? UiaHostProviderFromHwnd(model_->window, value) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE Navigate(NavigateDirection direction, IRawElementProviderFragment** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (index_ < 0) {
            if (model_->page.entries.empty()) return S_OK;
            if (direction == NavigateDirection_FirstChild) return Related(static_cast<int>(model_->order[0]), value);
            if (direction == NavigateDirection_LastChild) return Related(static_cast<int>(model_->order[model_->page.entries.size() - 1]), value);
        } else {
            if (direction == NavigateDirection_Parent) return Related(-1, value);
            int next = -1;
            if (direction == NavigateDirection_NextSibling) next = SpatialNeighbor(*model_, index_, NAVDIR_NEXT);
            if (direction == NavigateDirection_PreviousSibling) next = SpatialNeighbor(*model_, index_, NAVDIR_PREVIOUS);
            if (next >= 0) return Related(next, value);
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeId(SAFEARRAY** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (index_ < 0) return S_OK;
        LONG ids[] = {UiaAppendRuntimeId, static_cast<LONG>(presentation_ & 0xffffffff), static_cast<LONG>(presentation_ >> 32), index_ + 1};
        auto* array = SafeArrayCreateVector(VT_I4, 0, 4); if (!array) return E_OUTOFMEMORY;
        for (LONG at = 0; at < 4; ++at) { const auto hr = SafeArrayPutElement(array, &at, &ids[at]); if (FAILED(hr)) { SafeArrayDestroy(array); return hr; } }
        *value = array; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_BoundingRectangle(UiaRect* value) override {
        if (!value) return E_POINTER; *value = {}; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        RECT client{}; GetClientRect(model_->window, &client); RECT bounds = client;
        if (index_ >= 0 && !IntersectRect(&bounds, &client, &model_->bounds[static_cast<size_t>(index_)])) return S_OK;
        POINT point{bounds.left, bounds.top}; ClientToScreen(model_->window, &point);
        value->left = point.x; value->top = point.y; value->width = bounds.right - bounds.left; value->height = bounds.bottom - bounds.top; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetEmbeddedFragmentRoots(SAFEARRAY** value) override { if (!value) return E_POINTER; *value = nullptr; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE SetFocus() override {
        if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        return index_ >= 0 ? ChangeSelection(SVSI_FOCUSED | SVSI_ENSUREVISIBLE) : model_->focusControl ? model_->focusControl(model_->context) : E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE get_FragmentRoot(IRawElementProviderFragmentRoot** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        Element* root = nullptr; const auto hr = Make(model_, -1, &root); if (SUCCEEDED(hr)) *value = static_cast<IRawElementProviderFragmentRoot*>(root); return hr;
    }
    HRESULT STDMETHODCALLTYPE ElementProviderFromPoint(double x, double y, IRawElementProviderFragment** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!std::isfinite(x) || !std::isfinite(y) || x < LONG_MIN || x > LONG_MAX || y < LONG_MIN || y > LONG_MAX) return E_INVALIDARG;
        POINT point{static_cast<LONG>(x), static_cast<LONG>(y)}; ScreenToClient(model_->window, &point);
        for (UINT index = 0; index < model_->page.entries.size(); ++index) if (PtInRect(&model_->bounds[index], point)) return Related(static_cast<int>(index), value);
        return Related(-1, value);
    }
    HRESULT STDMETHODCALLTYPE GetFocus(IRawElementProviderFragment** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        return ::GetFocus() == model_->window ? Related(model_->focus, value) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSelection(SAFEARRAY** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        ULONG count = 0; for (size_t at = 0; at < model_->page.entries.size(); ++at) if (model_->selected[at]) ++count;
        auto* array = SafeArrayCreateVector(VT_UNKNOWN, 0, count); if (!array) return E_OUTOFMEMORY;
        IUnknown** entries = nullptr; auto hr = SafeArrayAccessData(array, reinterpret_cast<void**>(&entries));
        if (FAILED(hr)) { SafeArrayDestroy(array); return hr; }
        ULONG output = 0;
        for (UINT at = 0; at < model_->page.entries.size(); ++at) if (model_->selected[at]) {
            Element* item = nullptr; hr = Make(model_, static_cast<int>(at), &item); if (FAILED(hr)) break;
            entries[output++] = static_cast<IRawElementProviderSimple*>(item);
        }
        SafeArrayUnaccessData(array); if (FAILED(hr)) { SafeArrayDestroy(array); return hr; } *value = array; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_CanSelectMultiple(BOOL* value) override { if (!value) return E_POINTER; *value = TRUE; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE get_IsSelectionRequired(BOOL* value) override { if (!value) return E_POINTER; *value = FALSE; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE Select() override { return ChangeSelection(SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_FOCUSED | SVSI_ENSUREVISIBLE); }
    HRESULT STDMETHODCALLTYPE AddToSelection() override { return ChangeSelection(SVSI_SELECT); }
    HRESULT STDMETHODCALLTYPE RemoveFromSelection() override { return ChangeSelection(SVSI_DESELECT); }
    HRESULT STDMETHODCALLTYPE get_IsSelected(BOOL* value) override {
        if (!value) return E_POINTER; *value = FALSE; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        *value = index_ >= 0 && model_->selected[static_cast<size_t>(index_)]; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_SelectionContainer(IRawElementProviderSimple** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        Element* root = nullptr; const auto hr = Make(model_, -1, &root); if (SUCCEEDED(hr)) *value = static_cast<IRawElementProviderSimple*>(root); return hr;
    }
    HRESULT STDMETHODCALLTYPE Invoke() override {
        if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        return index_ >= 0 && snapshot::Navigable(model_->page.entries[static_cast<size_t>(index_)].kind) && model_->activate ?
            model_->activate(model_->context, static_cast<UINT>(index_)) : UIA_E_NOTSUPPORTED;
    }
    HRESULT STDMETHODCALLTYPE ScrollIntoView() override { return ChangeSelection(SVSI_ENSUREVISIBLE | SVSI_NOTAKEFOCUS); }
    HRESULT STDMETHODCALLTYPE Scroll(ScrollAmount horizontal, ScrollAmount vertical) override {
        if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (horizontal != ScrollAmount_NoAmount || !model_->scrollTo) return UIA_E_NOTSUPPORTED;
        const auto info = ScrollInfo(); int delta = 0;
        if (vertical == ScrollAmount_LargeIncrement) delta = static_cast<int>(info.nPage);
        else if (vertical == ScrollAmount_LargeDecrement) delta = -static_cast<int>(info.nPage);
        else if (vertical == ScrollAmount_SmallIncrement) delta = 32;
        else if (vertical == ScrollAmount_SmallDecrement) delta = -32;
        else if (vertical != ScrollAmount_NoAmount) return E_INVALIDARG;
        return model_->scrollTo(model_->context, info.nPos + delta);
    }
    HRESULT STDMETHODCALLTYPE SetScrollPercent(double horizontal, double vertical) override {
        if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (horizontal == UIA_ScrollPatternNoScroll && vertical == UIA_ScrollPatternNoScroll) return S_OK;
        if (horizontal != UIA_ScrollPatternNoScroll || !std::isfinite(vertical) || vertical < 0 || vertical > 100 || !model_->scrollTo) return E_INVALIDARG;
        const auto info = ScrollInfo(); return model_->scrollTo(model_->context, static_cast<int>(vertical * std::max(0, info.nMax + 1 - static_cast<int>(info.nPage)) / 100.0));
    }
    HRESULT STDMETHODCALLTYPE get_HorizontalScrollPercent(double* value) override { if (!value) return E_POINTER; *value = UIA_ScrollPatternNoScroll; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE get_HorizontalViewSize(double* value) override { if (!value) return E_POINTER; *value = 100; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE get_HorizontallyScrollable(BOOL* value) override { if (!value) return E_POINTER; *value = FALSE; return Live() ? S_OK : UIA_E_ELEMENTNOTAVAILABLE; }
    HRESULT STDMETHODCALLTYPE get_VerticallyScrollable(BOOL* value) override {
        if (!value) return E_POINTER; *value = FALSE; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto info = ScrollInfo(); *value = info.nMax + 1 > static_cast<int>(info.nPage); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_VerticalScrollPercent(double* value) override {
        if (!value) return E_POINTER; *value = UIA_ScrollPatternNoScroll; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto info = ScrollInfo(); const int range = info.nMax + 1 - static_cast<int>(info.nPage);
        if (range > 0) *value = 100.0 * info.nPos / range; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_VerticalViewSize(double* value) override {
        if (!value) return E_POINTER; *value = 100; if (!Live()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto info = ScrollInfo(); if (info.nMax >= 0) *value = std::min(100.0, 100.0 * info.nPage / (info.nMax + 1)); return S_OK;
    }
};

HRESULT Make(const std::shared_ptr<AccessibleModel>& model, int index, Element** value) noexcept {
    *value = nullptr;
    if (!model || !model->lifetimeOwner || !model->alive || model->uiaBlocked || index < -1 || (index >= 0 && static_cast<size_t>(index) >= model->page.entries.size())) return UIA_E_ELEMENTNOTAVAILABLE;
    const size_t slot = static_cast<size_t>(index + 1);
    if (model->uia[slot]) { auto* existing = static_cast<Element*>(model->uia[slot]); existing->AddRef(); *value = existing; return S_OK; }
    if (diagnostics.providers >= MaxProviders) return E_OUTOFMEMORY;
    const auto prepared = PrepareScheduler(model->lifetimeOwner); if (FAILED(prepared)) return prepared;
    auto* created = new(std::nothrow) Element(model, index); if (!created) { CleanupScheduler(); return E_OUTOFMEMORY; }
    model->uia[slot] = static_cast<IRawElementProviderSimple*>(created); *value = created; return S_OK;
}

// The scheduler is prepared before the first provider is exposed. Retirement
// needs no allocation and every live provider has a place in this fixed queue.
struct Scheduler {
    HWND window = nullptr;
    IUnknown* owner = nullptr; // Independent DLL lease also covers scheduler failure.
    std::array<IRawElementProviderSimple*, MaxProviders> queue{};
    UINT count = 0, tries = 0;
    bool processing = false, scheduled = false, failed = false;
};
thread_local Scheduler scheduler;
LRESULT CALLBACK RetireProc(HWND,UINT,WPARAM,LPARAM,UINT_PTR,DWORD_PTR) noexcept;
void CleanupScheduler() noexcept {
    if (diagnostics.providers || scheduler.processing || scheduler.count || !scheduler.window) return;
    const HWND window = scheduler.window;
    if (!RemoveWindowSubclass(window,RetireProc,RetireCookie)) { diagnostics.lastResult = E_FAIL; return; }
    const auto owner = scheduler.owner; scheduler.window = nullptr; KillTimer(window,1); DestroyWindow(window); scheduler = {};
    if (owner) owner->Release();
}
HRESULT PrepareScheduler(IUnknown* owner) noexcept {
    if (scheduler.failed) return diagnostics.lastResult;
    if (scheduler.window) return S_OK;
    const HWND window = CreateWindowExW(0,L"STATIC",L"AssetLibrary.UiaRetirement",0,0,0,0,0,HWND_MESSAGE,nullptr,GetModuleHandleW(nullptr),nullptr);
    if (!window) return HRESULT_FROM_WIN32(GetLastError());
    if (!SetWindowSubclass(window,RetireProc,RetireCookie,0)) { DestroyWindow(window); return E_FAIL; }
    scheduler.window = window; scheduler.owner = owner; owner->AddRef(); return S_OK;
}
HRESULT Schedule() noexcept {
    if (scheduler.scheduled || scheduler.processing || scheduler.count == 0) return S_OK;
    if (PostMessageW(scheduler.window,RetireMessage,RetireCookie,0) || SetTimer(scheduler.window,1,50,nullptr)) {
        scheduler.scheduled = true; return S_OK;
    }
    // Both OS scheduling mechanisms failed. Keep the bounded queued references
    // and DLL pins, deny new providers, and expose the failure diagnostically.
    scheduler.failed = true; diagnostics.lastResult = HRESULT_FROM_WIN32(GetLastError());
    if (SUCCEEDED(diagnostics.lastResult)) diagnostics.lastResult = E_FAIL;
    return diagnostics.lastResult;
}
LRESULT CALLBACK RetireProc(HWND window, UINT message, WPARAM first, LPARAM second, UINT_PTR, DWORD_PTR) noexcept {
    if (!((message == RetireMessage && first == RetireCookie && second == 0) || (message == WM_TIMER && first == 1))) return DefSubclassProc(window,message,first,second);
    KillTimer(window,1); scheduler.scheduled = false;
    if (InSendMessageEx(nullptr) != ISMEX_NOSEND) {
        if (++scheduler.tries < 8 && SetTimer(window,1,50,nullptr)) { scheduler.scheduled = true; return 0; }
        scheduler.failed = true; diagnostics.lastResult = RPC_E_CANTCALLOUT_ININPUTSYNCCALL; return 0;
    }
    scheduler.tries = 0; scheduler.processing = true; HRESULT result = S_OK;
    // Reentrant retirement may append, but the live-provider cap also bounds
    // this drain. The queued AddRef remains through the precise disconnect.
    UINT drained = 0;
    while (scheduler.count && drained++ < MaxProviders) {
        auto* provider = scheduler.queue[--scheduler.count]; scheduler.queue[scheduler.count] = nullptr;
        const auto disconnected = UiaDisconnectProvider(provider);
        if (SUCCEEDED(disconnected) || disconnected == UIA_E_ELEMENTNOTAVAILABLE) ++diagnostics.disconnected;
        else result = disconnected;
        provider->Release();
    }
    scheduler.processing = false; diagnostics.pendingRetirements = scheduler.count; diagnostics.lastResult = result;
    if (scheduler.count) Schedule(); else CleanupScheduler(); return 0;
}

}

HRESULT CreateUiaRoot(const std::shared_ptr<AccessibleModel>& model, IRawElementProviderSimple** value) noexcept {
    if (!value) return E_POINTER; *value = nullptr;
    if (model && model->uiaBlocked) RetireUia(model);
    Element* root = nullptr; const auto hr = Make(model,-1,&root); if (SUCCEEDED(hr)) *value = static_cast<IRawElementProviderSimple*>(root); return hr;
}
HRESULT RetireUia(const std::shared_ptr<AccessibleModel>& model) noexcept {
    if (!model) return E_INVALIDARG;
    // Mark the entire old presentation before any OS call can reenter it.
    for (auto* provider : model->uia) if (provider) static_cast<Element*>(provider)->Retire();
    for (auto*& provider : model->uia) if (provider) {
        if (scheduler.count >= MaxProviders) { model->uiaBlocked = true; diagnostics.lastResult = E_UNEXPECTED; return E_UNEXPECTED; }
        provider->AddRef(); scheduler.queue[scheduler.count++] = provider; provider = nullptr;
    }
    model->uiaBlocked = false; diagnostics.pendingRetirements = scheduler.count;
    return Schedule();
}
UiaDiagnostics InspectUia() noexcept { return diagnostics; }
void RaiseUiaEvent(const std::shared_ptr<AccessibleModel>& model, EVENTID event, int index) noexcept {
    if (!UiaClientsAreListening()) return;
    Element* element = nullptr; if (FAILED(Make(model,index,&element))) return;
    UiaRaiseAutomationEvent(static_cast<IRawElementProviderSimple*>(element),event); element->Release();
}
} // namespace gallery
