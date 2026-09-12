#pragma once
#include "Surface.h"
#include <oleacc.h>
#include <UIAutomation.h>

namespace gallery {
// One current-page model shared by painting and MSAA; providers never retain an
// old page copy. Retiring it clears names before any owner callback is made.
struct AccessibleModel {
    bool alive = true, visible = true;
    HWND window = nullptr;
    IUnknown* lifetimeOwner = nullptr; // Borrowed; each exposed provider independently retains it.
    ITypeInfo* typeInfo = nullptr;
    IAccessible* accessible = nullptr; // Weak, cleared by the provider destructor; no owner cycle.
    std::uint64_t generation = 0;
    std::uint64_t presentation = 0;
    std::array<IRawElementProviderSimple*, snapshot::MaxItems + 1> uia{}; // Weak; providers clear their own entry.
    bool uiaBlocked = false, uiaUnavailable = false;
    snapshot::Page page;
    std::array<RECT, snapshot::MaxItems> bounds{};
    std::array<UINT, snapshot::MaxItems> order{};
    std::array<bool, snapshot::MaxItems> selected{};
    std::array<bool, snapshot::MaxItems> thumbnailReady{}, thumbnailUnavailable{};
    int focus = -1;
    std::wstring title = L"资产库图库";
    void* context = nullptr;
    HRESULT (*select)(void*, UINT, UINT) noexcept = nullptr;
    HRESULT (*extend)(void*, UINT) noexcept = nullptr;
    HRESULT (*activate)(void*, UINT) noexcept = nullptr;
    HRESULT (*focusControl)(void*) noexcept = nullptr;
    HRESULT (*scrollTo)(void*, int) noexcept = nullptr;
    ~AccessibleModel();
};
HRESULT PrepareAccessibility(AccessibleModel& model) noexcept;
HRESULT CreateAccessible(const std::shared_ptr<AccessibleModel>& model, IAccessible** result) noexcept;
struct AccessibilityDiagnostics { UINT providers = 0, enumerators = 0; };
AccessibilityDiagnostics InspectAccessibility() noexcept; // Current-STA COM resource diagnostics only.
int SpatialNeighbor(const AccessibleModel& model, int index, LONG direction) noexcept;
} // namespace gallery
