#pragma once
#include <windows.h>

namespace gallery {
struct PreviewPlacement { double left = 0, top = 0, width = 0, height = 0; };

// Presentation geometry only. Coordinates are canvas device pixels; no bitmap,
// window ownership, original-file access or persisted state belongs here.
class PreviewViewport final {
    UINT imageWidth_ = 0, imageHeight_ = 0;
    RECT viewport_{};
    double scale_ = 1, centerX_ = .5, centerY_ = .5;
    bool fitting_ = true;
    void ClampCenter() noexcept;
public:
    static constexpr double MinimumScale = .1, MaximumScale = 4;
    void Reset(UINT imageWidth = 0, UINT imageHeight = 0) noexcept;
    void Resize(const RECT& viewport) noexcept;
    bool Ready() const noexcept;
    bool Fitting() const noexcept { return fitting_; }
    double FitScale() const noexcept;
    double Scale() const noexcept;
    PreviewPlacement Placement() const noexcept;
    bool Fit() noexcept;
    bool ActualSize() noexcept;
    bool ZoomAt(double multiplier, POINT anchor) noexcept;
    bool Pan(double deltaX, double deltaY) noexcept;
    bool CanPan() const noexcept;
    bool CanZoomIn() const noexcept;
    bool CanZoomOut() const noexcept;
};
}
