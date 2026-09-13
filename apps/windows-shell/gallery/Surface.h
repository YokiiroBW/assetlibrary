#pragma once

#include "../Snapshot.h"
#include <unknwn.h>
#include <array>
#include <cstdint>
#include <memory>

namespace gallery {

constexpr UINT MaxImageRequestsPerView = 16;
constexpr UINT MaxVisibleFiles = static_cast<UINT>(snapshot::MaxItems);
constexpr int StatusTextControlId = 106;
constexpr size_t MaxStatusTextChars = 256;
constexpr UINT MaxPreviewDimension = 1600;
constexpr size_t MaxPreviewPixelBytes = 10240000;
constexpr int PreviewBackControlId = 107, PreviewPreviousControlId = 108, PreviewNextControlId = 109;
constexpr int PreviewZoomOutControlId = 110, PreviewZoomInControlId = 111;
constexpr int PreviewFitControlId = 112, PreviewActualControlId = 113;
constexpr int PagePreviousControlId = 114, PageNextControlId = 115;
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
    std::array<UINT, MaxVisibleFiles> indices{};
    UINT count = 0;
};

struct Callbacks {
    void* context = nullptr;
    // Borrowed View owner, retained only while window messages/public calls
    // and event callbacks execute. The Surface does not own the View.
    IUnknown* lifetimeOwner = nullptr;
    // Independent DLL lifetime (production: Folder), never an object owning
    // the View/Surface. Accessibility providers retain this, not lifetimeOwner.
    IUnknown* providerLifetimeOwner = nullptr;
    void (*activateItem)(void*, UINT index) noexcept = nullptr;
    void (*previewStep)(void*, int delta) noexcept = nullptr; // -1 or +1; owner selects an authorized file.
    void (*previewClose)(void*) noexcept = nullptr;
    void (*pageStep)(void*, int delta) noexcept = nullptr; // -1 or +1; owner queries an opaque page token.
    void (*preferencesChanged)(void*) noexcept = nullptr; // Only an actual mode/density change.
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
    // generation is the owner's monotonic preview request serial, independent
    // of SetPage generation. Calls may reenter/retire through owner callbacks.
    // Begin releases all thumbnails/old preview, retaining browse state.
    HRESULT BeginPreview(UINT index, std::uint64_t generation, bool previousEnabled, bool nextEnabled) noexcept;
    // nullptr carries an honest no-image status. Stale/hidden/end returns S_FALSE.
    HRESULT SetPreview(UINT index, std::uint64_t generation, std::shared_ptr<const Pbgra> image, const std::wstring& statusText) noexcept;
    void EndPreview() noexcept; // Restores prior selection, focus and browse position.
    bool Previewing() const noexcept;
    VisibleFiles VisibleFileItems() const noexcept;
    size_t RetainedImageBytes() const noexcept;

    Selection SelectedItems() const noexcept;
    int FocusedItem() const noexcept; // -1 when none.
    HRESULT SelectItem(UINT index, UINT shellSelectionFlags) noexcept; // Documented SVSI_* flags.
    HRESULT ItemRect(UINT index, RECT* clientBounds) const noexcept; // Surface-window client coordinates.
    void Focus() noexcept;
    bool TranslateAccelerator(const MSG& message) noexcept; // Internal Tab only; boundary/Ctrl/Alt+Tab remain with host.

    // Presentation only: caller owns counts. Empty clears; max 256 UTF-16 units.
    void SetStatusText(const std::wstring& text) noexcept;
    // Browse-only native controls. Clear/SetPage/hidden retire these flags;
    // owner restores them after publishing the current authorized page/status.
    void SetPageNavigation(bool previousEnabled, bool nextEnabled) noexcept;
    void SetVisible(bool visible) noexcept; // Hidden means zero image candidates and immediate image release.
    void SetMode(Mode mode) noexcept;
    Mode CurrentMode() const noexcept;
    void SetDensity(UINT targetRowHeightDip) noexcept; // Clamped to [96,256]; keeps scroll anchor.
    UINT Density() const noexcept;
};

} // namespace gallery
