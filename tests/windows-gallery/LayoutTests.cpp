#include "gallery/Layout.h"
#include "gallery/Accessible.h"
#include <cmath>
#include <iostream>
#include <limits>

namespace {
void Require(bool value, const char* message) { if (!value) throw message; }
void Check(const gallery::Layout& layout, int width, size_t count) {
    Require(layout.items.size() == count, "item count");
    for (size_t index = 0; index < layout.items.size(); ++index) {
        const auto& item = layout.items[index];
        Require(item.bounds.left >= 0 && item.bounds.right <= width, "horizontal bounds");
        Require(item.bounds.top >= 0 && item.bounds.bottom <= layout.height, "vertical bounds");
        Require(item.bounds.right > item.bounds.left && item.bounds.bottom > item.bounds.top, "positive size");
        for (size_t other = index + 1; other < layout.items.size(); ++other) {
            RECT overlap{}; Require(!IntersectRect(&overlap, &item.bounds, &layout.items[other].bounds), "nonoverlap");
        }
        POINT point{item.bounds.left, item.bounds.top};
        Require(gallery::HitTest(layout, point) == static_cast<int>(item.index), "hit test mapping");
    }
}
}

int main() {
    try {
        std::vector<gallery::LayoutItem> items;
        items.push_back({snapshot::Kind::Directory, 1});
        for (UINT index = 0; index < 99; ++index) items.push_back({snapshot::Kind::File, index % 3 == 0 ? 2.0 : index % 3 == 1 ? 0.5 : 1.0});
        items.push_back({snapshot::Kind::NextPage, 1});
        for (int width : {1, 120, 320, 640, 1100, 3840}) for (UINT dpi : {96u, 144u, 192u}) for (auto mode : {gallery::Mode::Gallery, gallery::Mode::List}) {
            const auto layout = gallery::Arrange(items, width, 176, dpi, mode); Check(layout, width, items.size());
            const auto visible = gallery::Visible(layout, items, 0, 10000);
            Require(visible.count <= gallery::MaxVisibleFiles, "complete visible-file set remains page bounded");
            for (UINT at = 0; at < visible.count; ++at) Require(items[visible.indices[at]].kind == snapshot::Kind::File, "only ordinary files");
        }
        auto empty = gallery::Arrange({}, 640, 176, 96, gallery::Mode::Gallery); Require(empty.height == 0 && empty.items.empty(), "empty layout");
        auto invalid = items; invalid.push_back({}); Require(gallery::Arrange(invalid, 640, 176, 96, gallery::Mode::Gallery).items.empty(), "page bound");
        std::vector<gallery::LayoutItem> bad{{snapshot::Kind::File, 0}, {snapshot::Kind::File, std::numeric_limits<double>::infinity()}, {snapshot::Kind::File, -1}};
        Check(gallery::Arrange(bad, 640, 176, 96, gallery::Mode::Gallery), 640, bad.size());
        std::vector<gallery::LayoutItem> extreme{{snapshot::Kind::File, 512}, {snapshot::Kind::File, 1.0 / 512.0}};
        Check(gallery::Arrange(extreme, 320, 176, 96, gallery::Mode::Gallery), 320, extreme.size());
        std::vector<gallery::LayoutItem> ratios(4, {snapshot::Kind::File, 2});
        const auto row = gallery::Arrange(ratios, 640, 176, 96, gallery::Mode::Gallery);
        for (const auto& item : row.items) {
            const double ratio = static_cast<double>(item.image.right - item.image.left) / (item.image.bottom - item.image.top);
            Require(std::abs(ratio - 2.0) < 0.04, "preserved aspect ratio rounding");
        }
        Require(gallery::Visible(row, ratios, row.height + 1, 100).count == 0, "outside viewport");
        std::vector<gallery::LayoutItem> all(snapshot::MaxItems, {snapshot::Kind::File, 1});
        const auto complete = gallery::Arrange(all, 1800, 96, 96, gallery::Mode::Gallery);
        Require(gallery::Visible(complete, all, 0, complete.height + 1).count == snapshot::MaxItems, "all 101 visible files are candidates");
        gallery::AccessibleModel navigation; navigation.page.entries.resize(5);
        navigation.bounds[0] = {0,0,600,44}; navigation.bounds[1] = {0,60,200,150}; navigation.bounds[2] = {300,60,500,150};
        navigation.bounds[3] = {0,160,200,250}; navigation.bounds[4] = {300,160,500,250};
        for (UINT at=0; at<5; ++at) navigation.order[at] = at;
        Require(gallery::SpatialNeighbor(navigation,1,NAVDIR_RIGHT) == 2,"right prefers same row over closer wide folder above");
        Require(gallery::SpatialNeighbor(navigation,2,NAVDIR_LEFT) == 1,"left prefers same row over closer wide folder above");
        Require(gallery::SpatialNeighbor(navigation,2,NAVDIR_RIGHT) == 3 && gallery::SpatialNeighbor(navigation,3,NAVDIR_LEFT) == 2,"horizontal row boundary follows displayed order");
        Require(gallery::SpatialNeighbor(navigation,0,NAVDIR_RIGHT) == 1 && gallery::SpatialNeighbor(navigation,4,NAVDIR_RIGHT) == -1,"navigation row and final page boundary");
        Require(gallery::SpatialNeighbor(navigation,1,NAVDIR_UP) == 0,"vertical geometry remains unchanged");
        for (UINT at=0; at<5; ++at) OffsetRect(&navigation.bounds[at],0,-100);
        Require(gallery::SpatialNeighbor(navigation,1,NAVDIR_RIGHT) == 2,"same-row preference survives negative viewport offset");
        std::cout << "gallery_layout: 36 viewport/dpi/mode matrices plus bounds, ratios and visibility passed\n";
        return 0;
    } catch (const char* error) { std::cerr << error << '\n'; return 1; }
}
