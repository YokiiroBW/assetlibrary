#include "gallery/Surface.h"
#include "TestOwner.h"
#include "Capture.h"
#include <iostream>
#include <fstream>
#include <string>
#include <algorithm>

namespace {
void Require(bool value, const char* message) { if (!value) throw message; }
using ImageBuffer = gallery_test::Capture;
std::shared_ptr<const gallery::Pbgra> Red(UINT width, UINT height, BYTE alpha = 255) {
    auto result = std::make_shared<gallery::Pbgra>(); result->width = width; result->height = height; result->stride = width * 4;
    result->pixels.resize(static_cast<size_t>(result->stride) * height);
    for (size_t at = 0; at < result->pixels.size(); at += 4) { result->pixels[at + 2] = alpha; result->pixels[at + 3] = alpha; }
    return result;
}
void ThinShape(const ImageBuffer& image, bool horizontal) {
    int minX = image.width, minY = image.height, maxX = -1, maxY = -1;
    for (int y = 0; y < image.height; ++y) for (int x = 0; x < image.width; ++x) {
        const auto at = static_cast<size_t>((y * image.width + x) * 4);
        if (image.pixels[at] == 0 && image.pixels[at + 1] == 0 && image.pixels[at + 2] == 255) {
            minX = std::min(minX, x); maxX = std::max(maxX, x); minY = std::min(minY, y); maxY = std::max(maxY, y);
        }
    }
    Require(maxX >= minX && maxY >= minY, "actual PBGRA was drawn");
    Require(horizontal ? maxY - minY <= 1 && maxX - minX > 100 : maxX - minX <= 1 && maxY - minY > 100,
        "extreme aspect remains contained rather than stretched to tile");
}
}

int wmain(int argc, wchar_t** argv) {
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    gallery_test::Owner owner; HWND parent = nullptr; gallery::Surface* surface = nullptr;
    try {
        parent = CreateWindowExW(0, L"STATIC", L"test-only hidden rendering", WS_OVERLAPPEDWINDOW, 0, 0, 1000, 700,
            nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!parent) throw "rendering parent";
        gallery::Callbacks callbacks; callbacks.lifetimeOwner = &owner; RECT bounds{0, 0, 1000, 700};
        Require(SUCCEEDED(gallery::Surface::Create(parent, bounds, callbacks, &surface)), "rendering surface");
        const HWND canvas = FindWindowExW(surface->Window(), nullptr, L"STATIC", nullptr); if (!canvas) throw "rendering canvas";
        snapshot::Page page; page.status = snapshot::Status::Ready; page.epoch.Data1 = 1;
        snapshot::Entry item; item.kind = snapshot::Kind::File; item.name = L"极端宽高比合成验证，不是产品图片"; item.epoch = page.epoch; item.node.Data1 = 2;
        page.entries.push_back(item); surface->SetPage(page, 1);
        RECT client{}; GetClientRect(canvas, &client); ImageBuffer image(client.right, client.bottom);
        Require(SUCCEEDED(surface->SetThumbnail(0, 1, Red(512, 1))), "horizontal thumbnail"); image.Draw(canvas); ThinShape(image, true);
        Require(SUCCEEDED(surface->SetThumbnail(0, 1, Red(1, 512))), "vertical thumbnail"); image.Draw(canvas); ThinShape(image, false);
        Require(SUCCEEDED(surface->SetThumbnail(0, 1, Red(64, 64, 128))), "premultiplied alpha thumbnail"); image.Draw(canvas);
        RECT card{}; surface->ItemRect(0, &card); MapWindowPoints(surface->Window(), canvas, reinterpret_cast<POINT*>(&card), 2);
        const COLORREF blended = GetPixel(image.dc, (card.left + card.right) / 2, card.top + 20);
        Require(GetRValue(blended) > GetGValue(blended) && GetGValue(blended) > 0, "AlphaBlend composites transparency");
        const DWORD gdi = GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS), user = GetGuiResources(GetCurrentProcess(), GR_USEROBJECTS);
        for (UINT attempt = 0; attempt < 32; ++attempt) image.Draw(canvas);
        Require(GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS) == gdi, "temporary DIB/DC resources reclaimed after every paint");
        Require(GetGuiResources(GetCurrentProcess(), GR_USEROBJECTS) == user, "painting creates no persistent USER objects");
        if (argc == 2) image.Save(argv[1]);
        for (UINT index = 1; index < 30; ++index) page.entries.push_back(item);
        surface->SetPage(page, 2);
        auto visible = surface->VisibleFileItems();
        for (UINT at = 0; at < visible.count; ++at) surface->SetThumbnail(visible.indices[at], 2, Red(128, 64));
        SendMessageW(canvas, WM_VSCROLL, SB_LINEDOWN, 0);
        GetClientRect(canvas, &client);
        ImageBuffer sentinel(client.right + 40, client.bottom + 100);
        RECT entire{0, 0, sentinel.width, sentinel.height}; HBRUSH marker = CreateSolidBrush(RGB(255, 0, 255));
        if (!marker) throw "sentinel brush"; FillRect(sentinel.dc, &entire, marker); DeleteObject(marker);
        const int saved = SaveDC(sentinel.dc); Require(saved != 0, "sentinel DC state"); SetViewportOrgEx(sentinel.dc, 20, 80, nullptr);
        sentinel.Draw(canvas); RestoreDC(sentinel.dc, saved);
        for (int y = 0; y < sentinel.height; ++y) for (int x = 0; x < sentinel.width; ++x) {
            if (x >= 20 && x < 20 + client.right && y >= 80 && y < 80 + client.bottom) continue;
            const auto at = static_cast<size_t>((y * sentinel.width + x) * 4);
            Require(sentinel.pixels[at] == 255 && sentinel.pixels[at + 1] == 0 && sentinel.pixels[at + 2] == 255,
                "negative-scroll printing stays inside canvas and preserves toolbar/outside sentinel");
        }
        surface->Destroy(); delete surface; surface = nullptr; Require(owner.references == 1, "rendering owner references");
        DestroyWindow(parent); parent = nullptr; CoUninitialize();
        std::cout << "gallery_rendering: extreme aspect, PBGRA alpha and 32-paint GDI/USER reclamation passed\n"; return 0;
    } catch (const char* error) {
        std::cerr << error << '\n'; if (surface) delete surface; if (parent) DestroyWindow(parent); CoUninitialize(); return 1;
    }
}
