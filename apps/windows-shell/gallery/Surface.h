#pragma once

#include "../Snapshot.h"
#include <unknwn.h>
#include <array>
#include <cstdint>
#include <memory>

namespace gallery {

constexpr UINT MaxVisibleImages = 16;
constexpr size_t MaxImageBytes = 16u * 1024u * 1024u;
constexpr UINT MinimumDensityDip = 96, MaximumDensityDip = 256, DefaultDensityDip = 176;

enum class Mode { Gallery, List };

// Caller publishes immutable, validated thumbnail-v1 pixels; the surface also
// checks dimensions/stride/length. No decoder, URL, path or IPC belongs here.
struct Pbgra {
    UINT width = 0, height = 0, stride = 0;
    std::vector<BYTE> pixels;
};

struct Selection {
    std::array<UINT, snapshot::MaxItems> indices{};
    UINT count = 0;
};

struct VisibleFiles {
    std::array<UINT, MaxVisibleImages> indices{};
    UINT count = 0;
};

struct Callbacks {
    void* context = nullptr;
    // Borrowed while the HWND exists. Retained across each callback and by any
    // external accessibility provider; the View must Destroy before releasing
    // its owned surface. The surface itself does not create an owner cycle.
    IUnknown* lifetimeOwner = nullptr;
    void (*activateItem)(void*, UINT index) noexcept = nullptr;
    void (*contextMenu)(void*, int index, POINT screenPoint) noexcept = nullptr; // -1 means background.
    void (*viewportChanged)(void*) noexcept = nullptr;
    void (*selectionChanged)(void*) noexcept = nullptr;
    void (*refresh)(void*) noexcept = nullptr;
    void (*settings)(void*) noexcept = nullptr;
    void (*focusActivated)(void*) noexcept = nullptr;
    // Optional owner-defined WM_APP..0xBFFF completion message. Other messages
    // are never forwarded. The callback owns LPARAM payload disposal.
    UINT completionMessage = 0;
    LRESULT (*completion)(void*, WPARAM, LPARAM) noexcept = nullptr;
};

class Surface final {
    struct State;
    std::shared_ptr<State> state_;
    explicit Surface(std::shared_ptr<State> state) noexcept;

public:
    ~Surface();
    Surface(const Surface&) = delete;
    Surface& operator=(const Surface&) = delete;

    // All calls and callbacks are on the creating UI thread. Every callback may
    // synchronously clear/destroy the view. Failure leaves *result null.
    static HRESULT Create(HWND parent, const RECT& bounds, const Callbacks& callbacks, Surface** result) noexcept;
    void Destroy() noexcept; // Idempotent. No joining/waiting; clears pixels, names and accessible state first.
    HWND Window() const noexcept;
    bool Shown() const noexcept; // Latest WM_SHOWWINDOW/SetVisible intent; native style may update after the callback.

    HRESULT SetPage(const snapshot::Page& page, std::uint64_t generation) noexcept;
    void Clear(snapshot::Status status, std::uint64_t generation) noexcept;
    // S_FALSE means stale generation, hidden/offscreen, or non-file item.
    // nullptr is an honest unavailable placeholder, never a synthetic image.
    HRESULT SetThumbnail(UINT index, std::uint64_t generation, std::shared_ptr<const Pbgra> image) noexcept;
    VisibleFiles VisibleFileItems() const noexcept;
    size_t RetainedImageBytes() const noexcept;

    Selection SelectedItems() const noexcept;
    int FocusedItem() const noexcept; // -1 when none.
    HRESULT SelectItem(UINT index, UINT shellSelectionFlags) noexcept; // Documented SVSI_* flags.
    HRESULT ItemRect(UINT index, RECT* clientBounds) const noexcept; // Surface-window client coordinates.
    void Focus() noexcept;
    bool TranslateAccelerator(const MSG& message) noexcept; // Internal Tab only; boundary/Ctrl/Alt+Tab remain with host.

    void SetVisible(bool visible) noexcept; // Hidden means zero image candidates and immediate image release.
    void SetMode(Mode mode) noexcept;
    Mode CurrentMode() const noexcept;
    void SetDensity(UINT targetRowHeightDip) noexcept; // Clamped to [96,256]; keeps scroll anchor.
    UINT Density() const noexcept;
};

} // namespace gallery
