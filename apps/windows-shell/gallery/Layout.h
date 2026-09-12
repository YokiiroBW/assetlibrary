#pragma once
#include "Surface.h"

namespace gallery {
struct LayoutItem { snapshot::Kind kind = snapshot::Kind::File; double aspect = 4.0 / 3.0; };
struct Placement { UINT index = 0; RECT bounds{}, image{}, caption{}; };
struct Layout {
    std::vector<Placement> items;
    int height = 0;
};
Layout Arrange(const std::vector<LayoutItem>& items, int width, UINT densityDip, UINT dpi, Mode mode);
int HitTest(const Layout& layout, POINT contentPoint) noexcept;
VisibleFiles Visible(const Layout& layout, const std::vector<LayoutItem>& items, int top, int height) noexcept;
} // namespace gallery
