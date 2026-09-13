#include "Accessible.h"
#include <shlobj.h>
#include <algorithm>
#include <atomic>
#include <limits>
#include <new>

namespace gallery {
AccessibleModel::~AccessibleModel() { if (typeInfo) typeInfo->Release(); }

HRESULT PrepareAccessibility(AccessibleModel& model) noexcept {
    ITypeLib* library = nullptr;
    auto hr = LoadRegTypeLib(LIBID_Accessibility, 1, 1, LOCALE_USER_DEFAULT, &library);
    if (SUCCEEDED(hr)) { hr = library->GetTypeInfoOfGuid(IID_IAccessible, &model.typeInfo); library->Release(); }
    return hr;
}

int SpatialNeighbor(const AccessibleModel& model, int index, LONG direction) noexcept {
    const int count = static_cast<int>(model.page.entries.size());
    if (count == 0) return -1;
    if (index < 0 || index >= count) return 0;
    if (direction == NAVDIR_NEXT || direction == NAVDIR_PREVIOUS) {
        for (int position = 0; position < count; ++position) if (model.order[static_cast<size_t>(position)] == static_cast<UINT>(index)) {
            const int target = position + (direction == NAVDIR_NEXT ? 1 : -1);
            return target >= 0 && target < count ? static_cast<int>(model.order[static_cast<size_t>(target)]) : -1;
        }
        return -1;
    }
    const RECT origin = model.bounds[static_cast<size_t>(index)];
    const auto x = (origin.left + origin.right) / 2, y = (origin.top + origin.bottom) / 2;
    long long best = std::numeric_limits<long long>::max(); int answer = -1;
    if (direction == NAVDIR_LEFT || direction == NAVDIR_RIGHT) {
        // Full-width navigation rows must not win a horizontal move merely
        // because their centre is closer than the next photo in this row.
        for (int candidate = 0; candidate < count; ++candidate) {
            if (candidate == index) continue;
            const RECT item = model.bounds[static_cast<size_t>(candidate)];
            if (std::max(origin.top,item.top) >= std::min(origin.bottom,item.bottom)) continue;
            const auto dx = static_cast<long long>((item.left + item.right) / 2 - x);
            if ((direction == NAVDIR_LEFT && dx >= 0) || (direction == NAVDIR_RIGHT && dx <= 0)) continue;
            const auto distance = dx < 0 ? -dx : dx;
            if (distance < best) { best = distance; answer = candidate; }
        }
        return answer >= 0 ? answer : SpatialNeighbor(model,index,direction == NAVDIR_RIGHT ? NAVDIR_NEXT : NAVDIR_PREVIOUS);
    }
    for (int candidate = 0; candidate < count; ++candidate) {
        if (candidate == index) continue;
        const RECT item = model.bounds[static_cast<size_t>(candidate)];
        const auto dx = static_cast<long long>((item.left + item.right) / 2 - x);
        const auto dy = static_cast<long long>((item.top + item.bottom) / 2 - y);
        const bool eligible = (direction == NAVDIR_UP && dy < 0) || (direction == NAVDIR_DOWN && dy > 0) ||
            (direction == NAVDIR_LEFT && dx < 0) || (direction == NAVDIR_RIGHT && dx > 0);
        const auto score = dx * dx + dy * dy;
        if (eligible && score < best) { best = score; answer = candidate; }
    }
    return answer;
}

namespace {
thread_local AccessibilityDiagnostics accessibleDiagnostics;
class SelectionEnumerator final : public IEnumVARIANT {
    std::atomic_ulong references_{1};
    std::shared_ptr<AccessibleModel> model_;
    IUnknown* owner_;
    std::uint64_t generation_;
    Selection selection_;
    UINT position_ = 0;
public:
    SelectionEnumerator(std::shared_ptr<AccessibleModel> model, Selection selection, UINT position = 0) noexcept
        : model_(std::move(model)), owner_(model_->lifetimeOwner), generation_(model_->presentation),
          selection_(selection), position_(position) { ++accessibleDiagnostics.enumerators; if (owner_) owner_->AddRef(); }
    ~SelectionEnumerator() { --accessibleDiagnostics.enumerators; model_.reset(); if (owner_) owner_->Release(); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_IEnumVARIANT) return E_NOINTERFACE;
        *result = static_cast<IEnumVARIANT*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto remaining = --references_; if (!remaining) delete this; return remaining; }
    HRESULT STDMETHODCALLTYPE Next(ULONG requested, VARIANT* values, ULONG* fetched) override {
        if (fetched) *fetched = 0;
        if (!values || (!fetched && requested != 1)) return E_POINTER;
        if (requested > snapshot::MaxItems) return E_INVALIDARG;
        ULONG taken = 0;
        if (model_->alive && generation_ == model_->presentation) {
            while (taken < requested && position_ < selection_.count) {
                VariantInit(&values[taken]); values[taken].vt = VT_I4;
                values[taken++].lVal = static_cast<LONG>(selection_.indices[position_++] + 1);
            }
        }
        if (fetched) *fetched = taken;
        return taken == requested ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {
        const UINT available = selection_.count - position_;
        position_ += std::min<ULONG>(count, available); return count <= available ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Reset() override { position_ = 0; return S_OK; }
    HRESULT STDMETHODCALLTYPE Clone(IEnumVARIANT** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        auto clone = new(std::nothrow) SelectionEnumerator(model_, selection_, position_);
        if (!clone) return E_OUTOFMEMORY; clone->generation_ = generation_; *result = clone; return S_OK;
    }
};

class Accessible final : public IAccessible {
    std::atomic_ulong references_{1};
    std::shared_ptr<AccessibleModel> model_;
    IUnknown* owner_;
    const std::uint64_t presentation_;
    bool Live() const noexcept { return model_->alive && presentation_ == model_->presentation; }
    bool Valid(VARIANT child, bool self = true) const noexcept {
        return Live() && child.vt == VT_I4 && child.lVal >= (self ? 0 : 1) &&
            static_cast<size_t>(child.lVal) <= model_->page.entries.size();
    }
    HRESULT Text(const wchar_t* text, BSTR* value) const noexcept {
        if (!value) return E_POINTER; *value = SysAllocString(text); return *value ? S_OK : E_OUTOFMEMORY;
    }
public:
    explicit Accessible(std::shared_ptr<AccessibleModel> model) noexcept : model_(std::move(model)), owner_(model_->lifetimeOwner), presentation_(model_->presentation) {
        ++accessibleDiagnostics.providers; if (owner_) owner_->AddRef();
    }
    ~Accessible() { --accessibleDiagnostics.providers; if (model_->accessible == this) model_->accessible = nullptr; model_.reset(); if (owner_) owner_->Release(); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_IDispatch && iid != IID_IAccessible) return E_NOINTERFACE;
        *result = static_cast<IAccessible*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
    ULONG STDMETHODCALLTYPE Release() override { const auto remaining = --references_; if (!remaining) delete this; return remaining; }
    HRESULT STDMETHODCALLTYPE GetTypeInfoCount(UINT* count) override { if (!count) return E_POINTER; *count = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetTypeInfo(UINT index, LCID, ITypeInfo** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (index != 0) return DISP_E_BADINDEX;
        *result = model_->typeInfo; if (!*result) return E_UNEXPECTED; (*result)->AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetIDsOfNames(REFIID iid, LPOLESTR* names, UINT count, LCID, DISPID* ids) override {
        if (iid != IID_NULL) return DISP_E_UNKNOWNINTERFACE;
        return DispGetIDsOfNames(model_->typeInfo, names, count, ids);
    }
    HRESULT STDMETHODCALLTYPE Invoke(DISPID id, REFIID iid, LCID, WORD flags, DISPPARAMS* parameters,
        VARIANT* value, EXCEPINFO* exception, UINT* argument) override {
        if (iid != IID_NULL) return DISP_E_UNKNOWNINTERFACE;
        if (!Live()) return CO_E_OBJNOTCONNECTED;
        return DispInvoke(static_cast<IAccessible*>(this), model_->typeInfo, id, flags, parameters, value, exception, argument);
    }
    HRESULT STDMETHODCALLTYPE get_accParent(IDispatch** parent) override {
        if (!parent) return E_POINTER; *parent = nullptr;
        if (!Live() || !model_->window) return CO_E_OBJNOTCONNECTED;
        return AccessibleObjectFromWindow(GetParent(model_->window), static_cast<DWORD>(OBJID_CLIENT), IID_IDispatch, reinterpret_cast<void**>(parent));
    }
    HRESULT STDMETHODCALLTYPE get_accChildCount(LONG* count) override {
        if (!count) return E_POINTER; *count = Live() ? static_cast<LONG>(model_->page.entries.size()) : 0; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_accChild(VARIANT child, IDispatch** result) override {
        if (!result) return E_POINTER; *result = nullptr; return Valid(child, false) ? S_FALSE : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE get_accName(VARIANT child, BSTR* value) override {
        if (!value) return E_POINTER; *value = nullptr;
        if (!Valid(child)) return Live() ? E_INVALIDARG : CO_E_OBJNOTCONNECTED;
        return Text(child.lVal ? model_->page.entries[static_cast<size_t>(child.lVal - 1)].name.c_str() : model_->title.c_str(), value);
    }
    HRESULT STDMETHODCALLTYPE get_accValue(VARIANT, BSTR* value) override { if (!value) return E_POINTER; *value = nullptr; return S_FALSE; }
    HRESULT STDMETHODCALLTYPE get_accDescription(VARIANT child, BSTR* value) override {
        if (!value) return E_POINTER; *value = nullptr;
        if (!Valid(child)) return E_INVALIDARG;
        if (model_->preview) return Text(model_->detail.data(),value);
        if (child.lVal && model_->page.entries[static_cast<size_t>(child.lVal - 1)].kind == snapshot::Kind::File) {
            const size_t index = static_cast<size_t>(child.lVal - 1);
            return Text(model_->thumbnailReady[index] ? L"文件，已加载缩略图" :
                model_->thumbnailUnavailable[index] ? L"文件，预览不可用" : L"文件，等待预览", value);
        }
        return Text(child.lVal ? snapshot::TypeText(model_->page.entries[static_cast<size_t>(child.lVal - 1)].kind) :
            snapshot::StatusText(model_->page.status), value);
    }
    HRESULT STDMETHODCALLTYPE get_accRole(VARIANT child, VARIANT* role) override {
        if (!role) return E_POINTER; VariantInit(role); if (!Valid(child)) return E_INVALIDARG;
        role->vt = VT_I4; role->lVal = model_->preview ? (child.lVal ? ROLE_SYSTEM_GRAPHIC : ROLE_SYSTEM_PANE) : ROLE_SYSTEM_LIST;
        if (model_->preview) return S_OK;
        if (child.lVal) role->lVal = model_->page.entries[static_cast<size_t>(child.lVal - 1)].kind == snapshot::Kind::NextPage ?
            ROLE_SYSTEM_LINK : ROLE_SYSTEM_LISTITEM;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_accState(VARIANT child, VARIANT* value) override {
        if (!value) return E_POINTER; VariantInit(value); if (!Valid(child)) return E_INVALIDARG;
        value->vt = VT_I4; value->lVal = STATE_SYSTEM_FOCUSABLE;
        if (child.lVal) {
            const size_t index = static_cast<size_t>(child.lVal - 1); if (!model_->preview) value->lVal |= STATE_SYSTEM_SELECTABLE;
            if (model_->selected[index]) value->lVal |= STATE_SYSTEM_SELECTED;
            if (GetFocus() == model_->window && model_->focus == static_cast<int>(index)) value->lVal |= STATE_SYSTEM_FOCUSED;
            RECT client{}, overlap{}; GetClientRect(model_->window, &client);
            if (!model_->visible || !IntersectRect(&overlap, &client, &model_->bounds[index])) value->lVal |= STATE_SYSTEM_OFFSCREEN;
        } else if (GetFocus() == model_->window) value->lVal |= STATE_SYSTEM_FOCUSED;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_accHelp(VARIANT, BSTR* value) override { if (!value) return E_POINTER; *value = nullptr; return S_FALSE; }
    HRESULT STDMETHODCALLTYPE get_accHelpTopic(BSTR* file, VARIANT, LONG* topic) override {
        if (!file || !topic) return E_POINTER; *file = nullptr; *topic = 0; return S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE get_accKeyboardShortcut(VARIANT, BSTR* value) override { if (!value) return E_POINTER; *value = nullptr; return S_FALSE; }
    HRESULT STDMETHODCALLTYPE get_accFocus(VARIANT* value) override {
        if (!value) return E_POINTER; VariantInit(value);
        if (!Live()) return CO_E_OBJNOTCONNECTED;
        if (GetFocus() == model_->window) { value->vt = VT_I4; value->lVal = model_->focus + 1; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_accSelection(VARIANT* value) override {
        if (!value) return E_POINTER; VariantInit(value); if (!Live()) return CO_E_OBJNOTCONNECTED;
        Selection selected;
        for (UINT index = 0; index < model_->page.entries.size(); ++index) if (model_->selected[index]) selected.indices[selected.count++] = index;
        if (selected.count == 1) { value->vt = VT_I4; value->lVal = static_cast<LONG>(selected.indices[0] + 1); }
        else if (selected.count > 1) {
            auto enumerator = new(std::nothrow) SelectionEnumerator(model_, selected);
            if (!enumerator) return E_OUTOFMEMORY; value->vt = VT_UNKNOWN; value->punkVal = enumerator;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_accDefaultAction(VARIANT child, BSTR* value) override {
        if (!value) return E_POINTER; *value = nullptr; if (!Valid(child, false)) return E_INVALIDARG;
        const auto kind = model_->page.entries[static_cast<size_t>(child.lVal - 1)].kind;
        if (model_->preview || (!snapshot::Navigable(kind) && kind != snapshot::Kind::File)) return S_FALSE;
        return Text(kind == snapshot::Kind::NextPage ? L"下一页" : L"打开", value);
    }
    HRESULT STDMETHODCALLTYPE accSelect(LONG flags, VARIANT child) override {
        if (!Valid(child, false) || !model_->select) return E_INVALIDARG;
        if (flags & ~(SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION | SELFLAG_EXTENDSELECTION | SELFLAG_ADDSELECTION | SELFLAG_REMOVESELECTION)) return E_INVALIDARG;
        const LONG operation = flags & (SELFLAG_TAKESELECTION | SELFLAG_EXTENDSELECTION | SELFLAG_ADDSELECTION | SELFLAG_REMOVESELECTION);
        if (operation && (operation & (operation - 1))) return E_INVALIDARG;
        if ((flags & SELFLAG_EXTENDSELECTION) && model_->extend) return model_->extend(model_->context, static_cast<UINT>(child.lVal - 1));
        if (flags & SELFLAG_REMOVESELECTION) {
            const auto generation = model_->generation;
            const auto hr = model_->select(model_->context, static_cast<UINT>(child.lVal - 1), SVSI_DESELECT);
            if (FAILED(hr) || !(flags & SELFLAG_TAKEFOCUS) || !Live() || model_->generation != generation || !model_->select) return hr;
            return model_->select(model_->context, static_cast<UINT>(child.lVal - 1), SVSI_FOCUSED | SVSI_ENSUREVISIBLE);
        }
        UINT selection = 0;
        if (flags & SELFLAG_TAKEFOCUS) selection |= SVSI_FOCUSED | SVSI_ENSUREVISIBLE;
        if (flags & SELFLAG_TAKESELECTION) selection |= SVSI_DESELECTOTHERS | SVSI_SELECT;
        if (flags & SELFLAG_ADDSELECTION) selection |= SVSI_SELECT;
        return model_->select(model_->context, static_cast<UINT>(child.lVal - 1), selection);
    }
    HRESULT STDMETHODCALLTYPE accLocation(LONG* left, LONG* top, LONG* width, LONG* height, VARIANT child) override {
        if (!left || !top || !width || !height) return E_POINTER; *left = *top = *width = *height = 0;
        if (!Valid(child) || !model_->window) return E_INVALIDARG;
        RECT rectangle{};
        if (child.lVal) rectangle = model_->bounds[static_cast<size_t>(child.lVal - 1)];
        else GetClientRect(model_->window, &rectangle);
        POINT origin{rectangle.left, rectangle.top}; if (!ClientToScreen(model_->window, &origin)) return HRESULT_FROM_WIN32(GetLastError());
        *left = origin.x; *top = origin.y; *width = rectangle.right - rectangle.left; *height = rectangle.bottom - rectangle.top; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE accNavigate(LONG direction, VARIANT start, VARIANT* end) override {
        if (!end) return E_POINTER; VariantInit(end); if (!Valid(start)) return E_INVALIDARG;
        int index = -1;
        if (start.lVal == 0 && !model_->page.entries.empty()) {
            if (direction == NAVDIR_FIRSTCHILD) index = static_cast<int>(model_->order[0]);
            else if (direction == NAVDIR_LASTCHILD) index = static_cast<int>(model_->order[model_->page.entries.size() - 1]);
        } else if (start.lVal) index = SpatialNeighbor(*model_, start.lVal - 1, direction);
        if (index < 0) return S_FALSE; end->vt = VT_I4; end->lVal = index + 1; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE accHitTest(LONG x, LONG y, VARIANT* result) override {
        if (!result) return E_POINTER; VariantInit(result); if (!Live() || !model_->window) return CO_E_OBJNOTCONNECTED;
        POINT point{x, y}; ScreenToClient(model_->window, &point); RECT client{}; GetClientRect(model_->window, &client);
        if (!PtInRect(&client, point)) return S_FALSE;
        result->vt = VT_I4; result->lVal = CHILDID_SELF;
        for (UINT index = 0; index < model_->page.entries.size(); ++index) if (PtInRect(&model_->bounds[index], point)) { result->lVal = static_cast<LONG>(index + 1); break; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE accDoDefaultAction(VARIANT child) override {
        if (!Valid(child, false) || !model_->activate) return E_INVALIDARG;
        const UINT index = static_cast<UINT>(child.lVal - 1);
        if (model_->preview || (!snapshot::Navigable(model_->page.entries[index].kind) && model_->page.entries[index].kind != snapshot::Kind::File)) return E_ACCESSDENIED;
        return model_->activate(model_->context, index);
    }
    HRESULT STDMETHODCALLTYPE put_accName(VARIANT, BSTR) override { return E_ACCESSDENIED; }
    HRESULT STDMETHODCALLTYPE put_accValue(VARIANT, BSTR) override { return E_ACCESSDENIED; }
};
}

HRESULT CreateAccessible(const std::shared_ptr<AccessibleModel>& model, IAccessible** result) noexcept {
    if (!result) return E_POINTER; *result = nullptr;
    if (!model || !model->alive || !model->typeInfo) return CO_E_OBJNOTCONNECTED;
    if (model->accessible) { model->accessible->AddRef(); *result = model->accessible; return S_OK; }
    auto provider = new(std::nothrow) Accessible(model);
    if (!provider) return E_OUTOFMEMORY; model->accessible = provider; *result = provider; return S_OK;
}
AccessibilityDiagnostics InspectAccessibility() noexcept { return accessibleDiagnostics; }
} // namespace gallery
