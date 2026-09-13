#include "PreviewViewport.h"
#include <algorithm>
#include <cmath>

namespace gallery {
namespace {
double Width(const RECT& view) noexcept { return static_cast<double>(view.right) - view.left; }
double Height(const RECT& view) noexcept { return static_cast<double>(view.bottom) - view.top; }
double Constrain(double center, double viewport, double extent) noexcept {
    if (extent <= viewport) return .5;
    const double margin = viewport / (2 * extent);
    return std::clamp(center, margin, 1 - margin);
}
double Drag(double center, double delta, double viewport, double extent) noexcept {
    if (extent <= viewport) return .5;
    const double margin = viewport / (2 * extent);
    // Bound a finite delta before division, including DBL_MAX on a subpixel image.
    const double movement = std::clamp(delta, (center - (1 - margin)) * extent, (center - margin) * extent);
    return Constrain(center - movement / extent, viewport, extent);
}
}

void PreviewViewport::Reset(UINT imageWidth, UINT imageHeight) noexcept {
    const bool valid = imageWidth >= 1 && imageWidth <= 1600 && imageHeight >= 1 && imageHeight <= 1600;
    imageWidth_ = valid ? imageWidth : 0;
    imageHeight_ = valid ? imageHeight : 0;
    fitting_ = true; scale_ = 1; centerX_ = centerY_ = .5;
}

void PreviewViewport::Resize(const RECT& viewport) noexcept {
    viewport_ = viewport;
    ClampCenter();
}

bool PreviewViewport::Ready() const noexcept {
    return imageWidth_ != 0 && imageHeight_ != 0 && viewport_.right > viewport_.left && viewport_.bottom > viewport_.top;
}

double PreviewViewport::FitScale() const noexcept {
    if (!Ready()) return 0;
    return std::min({1.0, Width(viewport_) / imageWidth_, Height(viewport_) / imageHeight_});
}

double PreviewViewport::Scale() const noexcept { return Ready() ? (fitting_ ? FitScale() : scale_) : 0; }

PreviewPlacement PreviewViewport::Placement() const noexcept {
    if (!Ready()) return {};
    const double scale = Scale();
    const double width = imageWidth_ * scale, height = imageHeight_ * scale;
    return {viewport_.left + Width(viewport_) / 2 - centerX_ * width,
        viewport_.top + Height(viewport_) / 2 - centerY_ * height, width, height};
}

void PreviewViewport::ClampCenter() noexcept {
    if (!Ready()) return; // A temporarily hidden/empty canvas must not erase explicit navigation state.
    const double scale = Scale();
    centerX_ = Constrain(centerX_, Width(viewport_), imageWidth_ * scale);
    centerY_ = Constrain(centerY_, Height(viewport_), imageHeight_ * scale);
}

bool PreviewViewport::Fit() noexcept {
    if (!Ready()) return false;
    const bool changed = !fitting_ || centerX_ != .5 || centerY_ != .5;
    fitting_ = true; scale_ = 1; centerX_ = centerY_ = .5;
    return changed;
}

bool PreviewViewport::ActualSize() noexcept {
    if (!Ready()) return false;
    const bool changed = fitting_ || scale_ != 1;
    fitting_ = false; scale_ = 1;
    ClampCenter();
    return changed;
}

bool PreviewViewport::ZoomAt(double multiplier, POINT anchor) noexcept {
    if (!Ready() || !std::isfinite(multiplier) || multiplier <= 0 || multiplier == 1) return false;
    const double prior = Scale();
    // Fit can be below 10%; a zoom-out gesture must never jump upward to the explicit minimum.
    if (multiplier < 1 && prior <= MinimumScale) return false;
    const double next = multiplier >= MaximumScale / prior ? MaximumScale :
        multiplier <= MinimumScale / prior ? MinimumScale : std::clamp(prior * multiplier, MinimumScale, MaximumScale);
    if (next == prior) return false;
    const double x = static_cast<double>(anchor.x) - (viewport_.left + Width(viewport_) / 2);
    const double y = static_cast<double>(anchor.y) - (viewport_.top + Height(viewport_) / 2);
    centerX_ += x * (1 / (imageWidth_ * prior) - 1 / (imageWidth_ * next));
    centerY_ += y * (1 / (imageHeight_ * prior) - 1 / (imageHeight_ * next));
    scale_ = next; fitting_ = false;
    ClampCenter();
    return true;
}

bool PreviewViewport::Pan(double deltaX, double deltaY) noexcept {
    if (!CanPan() || !std::isfinite(deltaX) || !std::isfinite(deltaY)) return false;
    const double priorX = centerX_, priorY = centerY_, scale = Scale();
    centerX_ = Drag(centerX_, deltaX, Width(viewport_), imageWidth_ * scale);
    centerY_ = Drag(centerY_, deltaY, Height(viewport_), imageHeight_ * scale);
    return centerX_ != priorX || centerY_ != priorY;
}

bool PreviewViewport::CanPan() const noexcept {
    return Ready() && !fitting_ && (imageWidth_ * Scale() > Width(viewport_) || imageHeight_ * Scale() > Height(viewport_));
}
bool PreviewViewport::CanZoomIn() const noexcept { return Ready() && Scale() < MaximumScale; }
bool PreviewViewport::CanZoomOut() const noexcept { return Ready() && Scale() > MinimumScale; }
} // namespace gallery
