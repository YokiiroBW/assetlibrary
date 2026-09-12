#include "Layout.h"
#include <algorithm>
#include <cmath>

namespace gallery {
namespace {
int Scale(int value, UINT dpi) noexcept { return MulDiv(value, static_cast<int>(std::clamp(dpi, 48u, 768u)), 96); }
double Aspect(double value) noexcept { return std::isfinite(value) && value > 0 ? std::clamp(value, 0.25, 4.0) : 4.0 / 3.0; }
void Row(Layout& result, UINT index, int left, int width, int top, int height, int icon, int gap) {
    Placement item; item.index = index; item.bounds = {left, top, left + width, top + height};
    const int padding = std::min(gap, width / 4);
    item.image = {left + padding, top + padding, std::min(left + padding + icon, left + width), top + height - padding};
    item.caption = {std::min<LONG>(item.image.right + padding, left + width), top, left + width - padding, top + height};
    result.items.push_back(item);
}
}

Layout Arrange(const std::vector<LayoutItem>& items, int width, UINT densityDip, UINT dpi, Mode mode) {
    Layout result;
    if (items.size() > snapshot::MaxItems) return result;
    width = std::clamp(width, 1, 32768);
    const int margin = std::min(Scale(12, dpi), width / 8);
    const int available = std::max(1, width - margin * 2);
    const int gap = std::max(1, Scale(8, dpi)), caption = Scale(26, dpi);
    const int target = std::max(1, Scale(static_cast<int>(std::clamp(densityDip, MinimumDensityDip, MaximumDensityDip)), dpi));
    int top = margin;
    std::vector<UINT> files, navigation;
    for (UINT index = 0; index < static_cast<UINT>(items.size()); ++index) {
        if (items[index].kind == snapshot::Kind::File) files.push_back(index);
        else if (items[index].kind == snapshot::Kind::NextPage) navigation.push_back(index);
        else {
            const int height = Scale(44, dpi);
            Row(result, index, margin, available, top, height, Scale(28, dpi), gap);
            top += height + gap;
        }
    }
    if (!result.items.empty() && !files.empty()) top += gap;
    size_t start = 0;
    while (start < files.size()) {
        if (mode == Mode::List) {
            const int height = Scale(42, dpi);
            Row(result, files[start++], margin, available, top, height, Scale(26, dpi), gap);
            top += height + gap;
            continue;
        }
        size_t end = start;
        double sum = 0;
        while (end < files.size()) {
            sum += Aspect(items[files[end]].aspect);
            ++end;
            if (sum * target + static_cast<double>(end - start - 1) * gap >= available) break;
        }
        const int spaces = static_cast<int>(end - start - 1) * gap;
        const double justified = std::max(1.0, (available - spaces) / sum);
        // Keep an incomplete final row readable instead of enlarging one photo to a whole screen.
        const bool filled = end < files.size() || justified <= target;
        const int pictureHeight = std::max(1, static_cast<int>(std::floor(filled ? justified : target)));
        int left = margin;
        for (size_t at = start; at < end; ++at) {
            const int remaining = margin + available - left;
            const int imageWidth = at + 1 == end && filled ? remaining :
                std::min(remaining, std::max(1, static_cast<int>(std::floor(pictureHeight * Aspect(items[files[at]].aspect)))));
            Placement item; item.index = files[at];
            item.image = {left, top, left + std::max(1, imageWidth), top + pictureHeight};
            item.caption = {left, item.image.bottom, item.image.right, item.image.bottom + caption};
            item.bounds = {left, top, item.image.right, item.caption.bottom};
            result.items.push_back(item);
            left = item.image.right + gap;
        }
        top += pictureHeight + caption + gap;
        start = end;
    }
    for (UINT index : navigation) {
        const int height = Scale(44, dpi);
        Row(result, index, margin, available, top, height, Scale(28, dpi), gap);
        top += height + gap;
    }
    result.height = result.items.empty() ? 0 : top + margin;
    return result;
}

int HitTest(const Layout& layout, POINT point) noexcept {
    for (const auto& item : layout.items) if (PtInRect(&item.bounds, point)) return static_cast<int>(item.index);
    return -1;
}

VisibleFiles Visible(const Layout& layout, const std::vector<LayoutItem>& items, int top, int height) noexcept {
    VisibleFiles result;
    if (height <= 0) return result;
    for (const auto& item : layout.items) {
        if (item.index < items.size() && items[item.index].kind == snapshot::Kind::File &&
            item.bounds.bottom > top && item.bounds.top < top + height) {
            result.indices[result.count++] = item.index;
            if (result.count == MaxVisibleFiles) break;
        }
    }
    return result;
}
} // namespace gallery
