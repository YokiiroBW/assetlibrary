#pragma once
#include <windows.h>
namespace gallery_test {
class Capture {
    HBITMAP bitmap_ = nullptr;
    HGDIOBJ old_ = nullptr;
public:
    HDC dc = nullptr;
    BYTE* pixels = nullptr;
    const int width, height;
    Capture(int w, int h) : width(w), height(h) {
        dc = CreateCompatibleDC(nullptr);
        BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = w;
        info.bmiHeader.biHeight = -h; info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
        void* data = nullptr; bitmap_ = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &data, nullptr, 0); pixels = static_cast<BYTE*>(data);
        if (dc && bitmap_) old_ = SelectObject(dc, bitmap_);
    }
    ~Capture() { if (dc && old_) SelectObject(dc, old_); if (dc) DeleteDC(dc); if (bitmap_) DeleteObject(bitmap_); }
    void Draw(HWND canvas) { if (!dc || !pixels) throw "memory canvas allocation"; SendMessageW(canvas, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(dc), PRF_CLIENT); GdiFlush(); }
    void Save(const wchar_t* path) {
        BITMAPFILEHEADER file{}; file.bfType = 0x4d42; file.bfOffBits = sizeof(file) + sizeof(BITMAPINFOHEADER);
        file.bfSize = file.bfOffBits + static_cast<DWORD>(width * height * 4);
        BITMAPINFOHEADER info{}; info.biSize = sizeof(info); info.biWidth = width; info.biHeight = -height;
        info.biPlanes = 1; info.biBitCount = 32; info.biSizeImage = static_cast<DWORD>(width * height * 4);
        const HANDLE output = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (output == INVALID_HANDLE_VALUE) throw "render evidence path must be new";
        DWORD written = 0; bool ok = WriteFile(output, &file, sizeof(file), &written, nullptr) && written == sizeof(file);
        ok = ok && WriteFile(output, &info, sizeof(info), &written, nullptr) && written == sizeof(info);
        ok = ok && WriteFile(output, pixels, info.biSizeImage, &written, nullptr) && written == info.biSizeImage;
        CloseHandle(output); if (!ok) throw "render evidence write";
    }
};
}
