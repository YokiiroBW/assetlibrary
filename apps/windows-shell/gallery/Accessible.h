#pragma once
#include "Surface.h"
#include <oleacc.h>

namespace gallery {
// One current-page model shared by painting and MSAA; providers never retain an
// old page copy. Retiring it clears names before any owner callback is made.
struct AccessibleModel {
    bool alive = true, visible = true;
    HWND window = nullptr;
    IUnknown* lifetimeOwner = nullptr; // Borrowed; each exposed provider independently retains it.
    ITypeInfo* typeInfo = nullptr;
    std::uint64_t generation = 0;
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
    void (*activate)(void*, UINT) noexcept = nullptr;
    ~AccessibleModel();
};
HRESULT PrepareAccessibility(AccessibleModel& model) noexcept;
HRESULT CreateAccessible(const std::shared_ptr<AccessibleModel>& model, IAccessible** result) noexcept;
int SpatialNeighbor(const AccessibleModel& model, int index, LONG direction) noexcept;
} // namespace gallery
