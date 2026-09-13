#include "gallery/PreviewViewport.h"
#include <cmath>
#include <iostream>
#include <limits>

namespace {
using gallery::PreviewPlacement;
using gallery::PreviewViewport;
void Require(bool value, const char* reason) { if (!value) throw reason; }
void Near(double actual, double expected, const char* reason) {
    Require(std::isfinite(actual) && std::abs(actual - expected) <= 1e-8, reason);
}
void Place(const PreviewViewport& view, double left, double top, double width, double height) {
    const auto item = view.Placement();
    Near(item.left, left, "placement left"); Near(item.top, top, "placement top");
    Near(item.width, width, "placement width"); Near(item.height, height, "placement height");
}
void Same(const PreviewViewport& view, const PreviewPlacement& before, double scale, bool fitting) {
    Place(view, before.left, before.top, before.width, before.height);
    Require(view.Scale() == scale && view.Fitting() == fitting, "invalid operation changed presentation state");
}
void Empty() {
    PreviewViewport view;
    Require(!view.Ready() && view.Fitting(), "default empty fit state");
    Near(view.Scale(), 0, "empty scale"); Near(view.FitScale(), 0, "empty fit scale"); Place(view, 0, 0, 0, 0);
    Require(!view.Fit() && !view.ActualSize() && !view.ZoomAt(2, {}) && !view.Pan(1, 1), "empty mutations");
    Require(!view.CanPan() && !view.CanZoomIn() && !view.CanZoomOut(), "empty capabilities");
    view.Resize({10, 20, 810, 620}); view.Reset(1600, 800);
    Require(view.Ready(), "reset retains viewport"); Place(view, 10, 120, 800, 400);
    for (const auto size : {0u, 1601u, std::numeric_limits<UINT>::max()}) {
        view.Reset(size, 10); Require(!view.Ready(), "invalid width"); Place(view, 0, 0, 0, 0);
        view.Reset(10, size); Require(!view.Ready(), "invalid height");
    }
    view.Reset(100, 50); Place(view, 360, 295, 100, 50);
    Require(!view.CanPan(), "small fit is not pannable");
    Require(view.ActualSize() && !view.Fitting(), "100 percent changes mode at equal scale");
    Place(view, 360, 295, 100, 50); Require(!view.ActualSize(), "repeated 100 percent");
    Require(view.Fit() && !view.Fit(), "fit mode change then idempotence");
    view.Reset(); Require(!view.Ready() && view.Fitting(), "reset clears image and mode");
}
void Anchors() {
    PreviewViewport view; view.Resize({10, 20, 810, 620}); view.Reset(1600, 1200);
    Require(view.ZoomAt(2, {210, 170}), "anchored zoom");
    Place(view, -190, -130, 1600, 1200); Near(view.Scale(), 1, "anchored scale");
    const auto zoomed = view.Placement();
    // Pixel(400,300), independently selected before zoom, remains under the pointer.
    Near(zoomed.left + 400 * view.Scale(), 210, "horizontal source anchor");
    Near(zoomed.top + 300 * view.Scale(), 170, "vertical source anchor");
    Require(view.ZoomAt(.5, {210, 170}), "reverse zoom"); Place(view, 10, 20, 800, 600);
    Require(!view.Fitting() && view.Fit(), "explicit fit-sized image retains explicit mode");
    view.Resize({0, 0, 400, 300});
    Require(view.ZoomAt(2, {std::numeric_limits<LONG>::min(), std::numeric_limits<LONG>::max()}), "extreme anchor remains bounded");
    Place(view, 0, -300, 800, 600);
}
void DragEdges() {
    PreviewViewport view; view.Resize({100, 200, 500, 500}); view.Reset(800, 600); view.ActualSize();
    Place(view, -100, 50, 800, 600);
    Require(view.Pan(20, -30), "image follows positive x and negative y drag"); Place(view, -80, 20, 800, 600);
    Require(view.Pan(1000, 1000), "top left clamp"); Place(view, 100, 200, 800, 600);
    Require(!view.Pan(1000, 1000), "top left saturation");
    Require(view.Pan(-1000, 0), "right clamp"); Place(view, -300, 200, 800, 600);
    Require(view.Pan(0, -1000), "bottom clamp"); Place(view, -300, -100, 800, 600);
    Require(!view.Pan(-1000, -1000), "bottom right saturation");
    view.Resize({0, 0, 600, 400}); view.Reset(1000, 100); view.ActualSize();
    Place(view, -200, 150, 1000, 100); Require(view.CanPan(), "one large axis");
    Require(!view.Pan(0, 1000), "small axis cannot pan");
    Require(view.Pan(1000, 1000), "only wide axis pans"); Place(view, 0, 150, 1000, 100);
}
void CentersAndResize() {
    PreviewViewport view; view.Resize({0, 0, 800, 600}); view.Reset(1600, 1200); view.ActualSize();
    Require(view.Pan(-200, -150), "move center to pixel(1000,750)"); Place(view, -600, -450, 1600, 1200);
    Require(view.ZoomAt(2, {400, 300}), "center zoom"); Place(view, -1600, -1200, 3200, 2400);
    Require(view.ActualSize(), "actual size keeps image center"); Place(view, -600, -450, 1600, 1200);
    view.Resize({20, 30, 1020, 830}); Place(view, -480, -320, 1600, 1200);
    for (const RECT invalid : {RECT{0, 0, 0, 10}, RECT{0, 0, 10, 0}, RECT{10, 10, 0, 0}}) {
        view.Resize(invalid); Require(!view.Ready() && !view.Fitting(), "invalid resize is temporarily empty");
        Place(view, 0, 0, 0, 0);
        Require(!view.Fit() && !view.ActualSize() && !view.ZoomAt(2, {}) && !view.Pan(20, 20), "invalid viewport operations preserve state");
        view.Resize({20, 30, 1020, 830}); Place(view, -480, -320, 1600, 1200);
    }
    view.Resize({0, 0, 4000, 3000}); Place(view, 1200, 900, 1600, 1200);
    Require(!view.CanPan(), "enlarged view centers both smaller axes");
    view.Resize({0, 0, 800, 600}); Place(view, -400, -300, 1600, 1200);
    Require(view.Fit(), "fit resets the explicit center"); Place(view, 0, 0, 800, 600);
    view.Resize({0, 0, 1600, 1200}); Place(view, 0, 0, 1600, 1200);
    view.Resize({0, 0, 3200, 2400}); Place(view, 800, 600, 1600, 1200);
}
void ScaleBounds() {
    PreviewViewport view; view.Resize({0, 0, 400, 400}); view.Reset(1600, 1600);
    Require(view.ZoomAt(std::numeric_limits<double>::max(), {200, 200}), "finite overflow-size multiplier saturates");
    Near(view.Scale(), 4, "maximum scale"); Place(view, -3000, -3000, 6400, 6400);
    Require(!view.CanZoomIn(), "cannot enlarge past maximum");
    for (int index = 0; index < 1000; ++index) Require(!view.ZoomAt(1.25, {0, 0}), "maximum is stable");
    Require(view.Pan(std::numeric_limits<double>::max(), -std::numeric_limits<double>::max()), "finite huge drag clamps");
    Place(view, 0, -6000, 6400, 6400);
    for (int index = 0; index < 1000; ++index) Require(!view.Pan(std::numeric_limits<double>::max(), -std::numeric_limits<double>::max()), "drag saturation is stable");
    Require(view.ZoomAt(std::numeric_limits<double>::denorm_min(), {200, 200}), "finite underflow-size multiplier clamps");
    Near(view.Scale(), .1, "minimum scale"); Place(view, 120, 120, 160, 160);
    Require(!view.CanZoomOut() && !view.Pan(2, 3) && !view.ZoomAt(.5, {}), "minimum and small image noops");
    view.Resize({0, 0, 16, 12}); view.Reset(1600, 1200); Near(view.Scale(), .01, "fit is allowed below minimum");
    Require(!view.CanZoomOut() && !view.ZoomAt(.8, {8, 6}) && view.Fitting(), "zoom out never increases a tiny fit");
    Require(!view.ZoomAt(1, {8, 6}) && view.Fitting(), "unit multiplier keeps fit mode");
    Require(view.ZoomAt(1.25, {8, 6}), "first enlargement reaches explicit minimum");
    Place(view, -72, -54, 160, 120); Near(view.Scale(), .1, "explicit minimum after tiny fit");
}
void InvalidNumbers() {
    PreviewViewport view; view.Resize({0, 0, 400, 300}); view.Reset(800, 600); view.ActualSize();
    const auto before = view.Placement();
    for (const double invalid : {0.0, -0.0, -1.0, std::numeric_limits<double>::quiet_NaN(),
        std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity()}) {
        Require(!view.ZoomAt(invalid, {123, 456}), "invalid multiplier is rejected"); Same(view, before, 1, false);
    }
    for (const double invalid : {std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity()}) {
        Require(!view.Pan(invalid, 20) && !view.Pan(20, invalid), "invalid pan is atomic"); Same(view, before, 1, false);
    }
    Require(!view.ZoomAt(1, {}) && !view.Pan(0, 0), "neutral actions are unchanged");
}
void DevicePixelsAndExtremeRects() {
    PreviewViewport view; view.Resize({0, 0, 400, 300}); view.Reset(800, 600); Near(view.Scale(), .5, "96dpi device-pixel fit");
    view.Resize({0, 0, 600, 450}); Near(view.Scale(), .75, "144dpi canvas recomputes fit");
    view.ActualSize(); Place(view, -100, -75, 800, 600);
    view.Resize({-400, -300, 400, 300}); Place(view, -400, -300, 800, 600);
    view.Resize({std::numeric_limits<LONG>::min(), std::numeric_limits<LONG>::min(), std::numeric_limits<LONG>::max(), std::numeric_limits<LONG>::max()});
    view.Reset(1600, 1600); Place(view, -800.5, -800.5, 1600, 1600);
    Require(view.ZoomAt(4, {}), "large ordered RECT does not overflow"); Place(view, -3200.5, -3200.5, 6400, 6400);
    view.Resize({std::numeric_limits<LONG>::max(), 0, std::numeric_limits<LONG>::min(), 1});
    Require(!view.Ready(), "reversed extreme RECT is empty"); Place(view, 0, 0, 0, 0);
}
void CheckCover(const PreviewViewport& view, double width, double height) {
    const auto p = view.Placement();
    Require(std::isfinite(p.left) && std::isfinite(p.top) && p.width > 0 && p.height > 0, "finite positive placement");
    if (p.width <= width) Near(p.left + p.width / 2, width / 2, "smaller horizontal axis centered");
    else Require(p.left <= 1e-8 && p.left + p.width >= width - 1e-8, "large horizontal axis leaves no gap");
    if (p.height <= height) Near(p.top + p.height / 2, height / 2, "smaller vertical axis centered");
    else Require(p.top <= 1e-8 && p.top + p.height >= height - 1e-8, "large vertical axis leaves no gap");
}
void Matrix() {
    for (UINT imageWidth : {1u, 17u, 511u, 1600u}) for (UINT imageHeight : {1u, 23u, 800u, 1600u})
    for (LONG width : {1L, 79L, 601L, 4096L}) for (LONG height : {1L, 113L, 480L, 4096L}) {
        PreviewViewport view; view.Resize({0, 0, width, height}); view.Reset(imageWidth, imageHeight);
        Require(view.Ready() && view.Scale() <= 1 && !view.CanPan(), "fit invariant"); CheckCover(view, width, height);
        for (const double factor : {1.25, 8.0, .8, .01, 2.0}) {
            view.ZoomAt(factor, {width / 3, height / 4}); CheckCover(view, width, height);
            view.Pan(1e300, -1e300); CheckCover(view, width, height);
            view.Pan(-1e300, 1e300); CheckCover(view, width, height);
        }
    }
}
}
int main() {
    try {
        Empty(); Anchors(); DragEdges(); CentersAndResize(); ScaleBounds(); InvalidNumbers(); DevicePixelsAndExtremeRects(); Matrix();
        std::cout << "preview_viewport: 7 directed scenarios, 256 shape/view matrices, anchor/bounds/invalid/saturation passed\n";
        return 0;
    } catch (const char* reason) { std::cerr << "preview_viewport: " << reason << '\n'; return 1; }
}
