#include "Surface.h"
#include "Layout.h"
#include "Accessible.h"
#include "../generated/WorkspaceTheme.generated.h"
#include <commctrl.h>
#include <shlobj.h>
#include <shellapi.h>
#include <windowsx.h>
#include <algorithm>
#include <cstring>
#include <new>

namespace gallery {
namespace {
struct OwnerReference {
    IUnknown* value;
    explicit OwnerReference(IUnknown* owner) noexcept : value(owner) { if (value) value->AddRef(); }
    OwnerReference(const OwnerReference&) = delete;
    OwnerReference& operator=(const OwnerReference&) = delete;
    ~OwnerReference() { if (value) value->Release(); }
};
template<typename T> struct OwnedState {
    // Model/type-info cleanup must precede the final owner/DLL lease release.
    OwnerReference owner;
    std::shared_ptr<T> state;
    explicit OwnedState(std::shared_ptr<T> value) noexcept : owner(value->callbacks.lifetimeOwner), state(std::move(value)) {}
};
void Fill(HDC dc, const RECT& rectangle, COLORREF color) noexcept {
    HBRUSH brush = CreateSolidBrush(color); if (brush) { FillRect(dc, &rectangle, brush); DeleteObject(brush); }
}
bool Contains(const VisibleFiles& files, UINT index) noexcept {
    for (UINT at = 0; at < files.count; ++at) if (files.indices[at] == index) return true;
    return false;
}
bool Same(const VisibleFiles& first, const VisibleFiles& second) noexcept {
    return first.count == second.count && std::equal(first.indices.begin(), first.indices.begin() + first.count, second.indices.begin());
}
bool PixelsValid(const Pbgra& image) noexcept {
    return image.width >= 1 && image.width <= 512 && image.height >= 1 && image.height <= 512 &&
        image.stride == image.width * 4 && image.pixels.size() == static_cast<size_t>(image.stride) * image.height;
}
}

struct Surface::State : std::enable_shared_from_this<State> {
    HWND window = nullptr, canvas = nullptr;
    std::array<HWND, 6> buttons{};
    Callbacks callbacks;
    std::shared_ptr<AccessibleModel> model = std::make_shared<AccessibleModel>();
    Layout layout;
    std::vector<LayoutItem> layoutItems;
    std::array<std::shared_ptr<const Pbgra>, snapshot::MaxItems> images{};
    VisibleFiles visible;
    Mode mode = Mode::Gallery;
    UINT density = DefaultDensityDip, dpi = 96;
    int scroll = 0, hover = -1, anchor = -1;
    bool shown = true, retiring = false;
    HFONT font = nullptr;
    HICON folderIcon = nullptr, fileIcon = nullptr;
    workspace_theme::Colors colors = workspace_theme::light;

    ~State() {
        if (font) DeleteObject(font);
        if (folderIcon) DestroyIcon(folderIcon);
        if (fileIcon) DestroyIcon(fileIcon);
    }
    int Scale(int value) const noexcept { return MulDiv(value, static_cast<int>(dpi), 96); }
    bool Alive() const noexcept { return model->alive && !retiring && window && canvas; }
    void Notify(void (*callback)(void*)) noexcept {
        const auto retained = shared_from_this();
        const auto owner = callbacks.lifetimeOwner; const auto context = callbacks.context;
        if (callback) { OwnerReference hold(owner); callback(context); }
    }
    void Activate(UINT index) noexcept {
        if (!Alive() || index >= model->page.entries.size()) return;
        const auto retained = shared_from_this();
        const auto callback = callbacks.activateItem; const auto context = callbacks.context;
        if (callback) { OwnerReference hold(callbacks.lifetimeOwner); callback(context, index); }
    }
    void Menu(int index, POINT point) noexcept {
        if (!Alive()) return;
        const auto retained = shared_from_this(); const auto callback = callbacks.contextMenu; const auto context = callbacks.context;
        if (callback) { OwnerReference hold(callbacks.lifetimeOwner); callback(context, index, point); }
    }
    void Empty(snapshot::Status status, std::uint64_t generation) noexcept {
        images.fill(nullptr); model->thumbnailReady.fill(false); model->thumbnailUnavailable.fill(false); visible = {};
        model->page.entries.clear(); model->page.status = status; model->page.epoch = {};
        model->generation = generation; model->selected.fill(false); model->bounds.fill({}); model->focus = -1;
        layout.items.clear(); layout.height = 0; layoutItems.clear(); scroll = 0; hover = anchor = -1;
        if (canvas) { SCROLLINFO info{sizeof(info), SIF_RANGE | SIF_POS, 0, 0, 0, 0, 0}; SetScrollInfo(canvas, SB_VERT, &info, TRUE); }
    }
    void Retire() noexcept {
        if (retiring) return;
        retiring = true; Empty(snapshot::Status::Unavailable, model->generation + 1);
        model->alive = false; model->visible = false; model->window = nullptr; model->context = nullptr;
        model->select = nullptr; model->extend = nullptr; model->activate = nullptr;
        window = nullptr; canvas = nullptr;
        Notify(callbacks.viewportChanged);
    }
    void UpdateTheme() noexcept {
        DWORD light = 1, bytes = sizeof(light);
        const auto read = RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
            L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &bytes);
        colors = read == ERROR_SUCCESS && light == 0 ? workspace_theme::dark : workspace_theme::light;
        HIGHCONTRASTW contrast{sizeof(contrast), 0, nullptr};
        if (SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0) && (contrast.dwFlags & HCF_HIGHCONTRASTON)) {
            colors.surface = colors.surface_strong = GetSysColor(COLOR_WINDOW); colors.ink = GetSysColor(COLOR_WINDOWTEXT);
            colors.muted = GetSysColor(COLOR_WINDOWTEXT); colors.line = GetSysColor(COLOR_WINDOWTEXT);
            colors.accent = colors.accent_soft = GetSysColor(COLOR_HIGHLIGHT); colors.on_accent = GetSysColor(COLOR_HIGHLIGHTTEXT);
            colors.focus = GetSysColor(COLOR_HIGHLIGHT); colors.folder = GetSysColor(COLOR_WINDOWTEXT);
        }
    }
    void UpdateFont() noexcept {
        HFONT next = CreateFontW(-Scale(13), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        if (!next) return;
        for (HWND button : buttons) if (button) SendMessageW(button, WM_SETFONT, reinterpret_cast<WPARAM>(next), TRUE);
        const auto previous = font; font = next; if (previous) DeleteObject(previous);
    }
    void Resize() {
        if (!Alive()) return;
        RECT client{}; GetClientRect(window, &client);
        const int width = client.right;
        const int rowHeight = Scale(34), gap = Scale(6), desired = Scale(72);
        const int columns = std::max(1, std::min(6, (width - gap) / std::max(1, desired + gap)));
        const int rows = (6 + columns - 1) / columns;
        const int toolbarHeight = rows * rowHeight + gap * 2;
        for (size_t index = 0; index < buttons.size(); ++index) {
            const int column = static_cast<int>(index) % columns, row = static_cast<int>(index) / columns;
            const int buttonWidth = std::max(1, std::min(desired, (width - gap * (columns + 1)) / columns));
            MoveWindow(buttons[index], gap + column * (buttonWidth + gap), gap + row * rowHeight, buttonWidth, rowHeight - gap, TRUE);
        }
        MoveWindow(canvas, 0, toolbarHeight, std::max(1, width), std::max<int>(1, client.bottom - toolbarHeight), TRUE);
        Reflow(true);
    }
    void Reflow(bool preserveAnchor) {
        if (!Alive()) return;
        int oldIndex = -1, oldOffset = 0;
        if (preserveAnchor) for (const auto& item : layout.items) if (item.bounds.bottom > scroll) {
            oldIndex = static_cast<int>(item.index); oldOffset = item.bounds.top - scroll; break;
        }
        RECT bounds{}; GetClientRect(canvas, &bounds);
        layout = Arrange(layoutItems, bounds.right, density, dpi, mode);
        if (oldIndex >= 0) for (const auto& item : layout.items) if (item.index == static_cast<UINT>(oldIndex)) { scroll = item.bounds.top - oldOffset; break; }
        scroll = std::clamp(scroll, 0, std::max<int>(0, layout.height - bounds.bottom));
        SCROLLINFO info{sizeof(info), SIF_RANGE | SIF_PAGE | SIF_POS, 0, std::max(0, layout.height - 1),
            static_cast<UINT>(std::max(0L, bounds.bottom)), scroll, 0};
        SetScrollInfo(canvas, SB_VERT, &info, TRUE);
        size_t order = 0;
        for (const auto& item : layout.items) { model->order[order++] = item.index; model->bounds[item.index] = item.bounds; OffsetRect(&model->bounds[item.index], 0, -scroll); }
        UpdateVisible(bounds.bottom);
        if (Alive()) InvalidateRect(canvas, nullptr, FALSE);
    }
    void UpdateVisible(int height) noexcept {
        const VisibleFiles next = shown ? Visible(layout, layoutItems, scroll, height) : VisibleFiles{};
        const bool changed = !Same(visible, next); visible = next;
        for (UINT index = 0; index < images.size(); ++index) if (!Contains(next, index)) { images[index].reset(); model->thumbnailReady[index] = false; }
        model->visible = shown;
        if (changed) Notify(callbacks.viewportChanged);
    }
    void ChangeVisibility(bool value) noexcept {
        const bool changed = shown != value; const auto previous = visible; shown = value;
        RECT client{}; if (canvas) GetClientRect(canvas, &client);
        UpdateVisible(client.bottom);
        if (Alive() && changed && Same(previous, visible)) Notify(callbacks.viewportChanged);
    }
    void ScrollTo(int top) {
        if (!Alive()) return;
        scroll = top; Reflow(false);
        if (Alive()) NotifyWinEvent(EVENT_OBJECT_LOCATIONCHANGE, canvas, OBJID_CLIENT, CHILDID_SELF);
    }
    void EnsureVisible(UINT index) {
        if (!Alive() || index >= model->page.entries.size()) return;
        RECT client{}; GetClientRect(canvas, &client);
        const RECT item = model->bounds[index];
        if (item.top < 0) ScrollTo(scroll + item.top);
        else if (item.bottom > client.bottom) ScrollTo(scroll + item.bottom - client.bottom);
    }
    HRESULT Select(UINT index, UINT flags) {
        if (!Alive() || index >= model->page.entries.size()) return E_INVALIDARG;
        if ((flags & SVSI_EDIT) == SVSI_EDIT) return E_ACCESSDENIED;
        if (flags & SVSI_DESELECTOTHERS) model->selected.fill(false);
        if (flags == SVSI_DESELECT || (flags & (SVSI_DESELECTOTHERS | SVSI_SELECT))) model->selected[index] = (flags & SVSI_SELECT) != 0;
        const auto generation = model->generation;
        if (flags & SVSI_FOCUSED) { model->focus = static_cast<int>(index); SetFocus(canvas); }
        if (!Alive() || model->generation != generation) return S_OK;
        if (flags & SVSI_SELECTIONMARK) anchor = static_cast<int>(index);
        if (anchor < 0) anchor = static_cast<int>(index);
        if (flags & SVSI_ENSUREVISIBLE) EnsureVisible(index);
        if (!Alive()) return S_OK;
        InvalidateRect(canvas, nullptr, FALSE);
        NotifyWinEvent(EVENT_OBJECT_SELECTIONWITHIN, canvas, OBJID_CLIENT, CHILDID_SELF);
        if (flags & SVSI_FOCUSED) NotifyWinEvent(EVENT_OBJECT_FOCUS, canvas, OBJID_CLIENT, static_cast<LONG>(index + 1));
        Notify(callbacks.selectionChanged); return S_OK;
    }
    void SelectFromInput(UINT index, bool control, bool shift) {
        if (!Alive() || index >= model->page.entries.size()) return;
        if (shift && anchor >= 0) {
            if (!control) model->selected.fill(false);
            size_t origin = 0, destination = 0;
            for (size_t position = 0; position < layout.items.size(); ++position) {
                if (layout.items[position].index == static_cast<UINT>(anchor)) origin = position;
                if (layout.items[position].index == index) destination = position;
            }
            for (size_t at = std::min(origin, destination); at <= std::max(origin, destination); ++at) model->selected[layout.items[at].index] = true;
            Select(index, SVSI_SELECT | SVSI_FOCUSED | SVSI_ENSUREVISIBLE);
        } else {
            UINT flags = SVSI_FOCUSED | SVSI_ENSUREVISIBLE | SVSI_SELECTIONMARK;
            if (!control) flags |= SVSI_DESELECTOTHERS | SVSI_SELECT;
            else if (!model->selected[index]) flags |= SVSI_SELECT;
            Select(index, flags);
        }
    }
    bool Key(WPARAM key) {
        if (!Alive()) return false;
        const bool control = (GetKeyState(VK_CONTROL) & 0x8000) != 0, shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
        if (key == VK_F5) { Notify(callbacks.refresh); return true; }
        if (key == VK_APPS || (key == VK_F10 && shift)) {
            POINT point{Scale(12), Scale(12)};
            if (model->focus >= 0) { const auto& item = model->bounds[static_cast<size_t>(model->focus)]; point = {item.left, item.bottom}; }
            ClientToScreen(canvas, &point); Menu(model->focus, point); return true;
        }
        if (key == VK_RETURN && model->focus >= 0) { Activate(static_cast<UINT>(model->focus)); return true; }
        if (key == 'A' && control) {
            for (UINT index = 0; index < model->page.entries.size(); ++index) model->selected[index] = true;
            InvalidateRect(canvas, nullptr, FALSE); NotifyWinEvent(EVENT_OBJECT_SELECTIONWITHIN, canvas, OBJID_CLIENT, CHILDID_SELF);
            Notify(callbacks.selectionChanged); return true;
        }
        if (key == VK_SPACE && model->focus >= 0) { SelectFromInput(static_cast<UINT>(model->focus), true, shift); return true; }
        LONG direction = 0;
        if (key == VK_LEFT) direction = NAVDIR_LEFT; if (key == VK_RIGHT) direction = NAVDIR_RIGHT;
        if (key == VK_UP) direction = NAVDIR_UP; if (key == VK_DOWN) direction = NAVDIR_DOWN;
        int next = direction ? SpatialNeighbor(*model, model->focus, direction) : -1;
        if (key == VK_HOME && !layout.items.empty()) next = static_cast<int>(layout.items.front().index);
        if (key == VK_END && !layout.items.empty()) next = static_cast<int>(layout.items.back().index);
        if (key == VK_PRIOR || key == VK_NEXT) {
            RECT client{}; GetClientRect(canvas, &client); const int step = std::max(1L, client.bottom - Scale(32));
            ScrollTo(scroll + (key == VK_NEXT ? step : -step));
            if (Alive()) for (const auto& item : layout.items) if (item.bounds.bottom > scroll) { next = static_cast<int>(item.index); break; }
        }
        if (next >= 0 && Alive()) {
            if (control && !shift) Select(static_cast<UINT>(next), SVSI_FOCUSED | SVSI_ENSUREVISIBLE);
            else SelectFromInput(static_cast<UINT>(next), control, shift);
            return true;
        }
        return direction != 0 || key == VK_HOME || key == VK_END || key == VK_PRIOR || key == VK_NEXT;
    }
    void PaintImage(HDC destination, const Pbgra& image, const RECT& box) noexcept {
        if (box.right <= box.left || box.bottom <= box.top) return;
        BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = static_cast<LONG>(image.width);
        info.bmiHeader.biHeight = -static_cast<LONG>(image.height); info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr; HBITMAP bitmap = CreateDIBSection(destination, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        HDC source = bitmap ? CreateCompatibleDC(destination) : nullptr;
        if (bitmap && bits && source) {
            std::memcpy(bits, image.pixels.data(), image.pixels.size()); const HGDIOBJ old = SelectObject(source, bitmap);
            const double factor = std::min(static_cast<double>(box.right - box.left) / image.width,
                static_cast<double>(box.bottom - box.top) / image.height);
            const int width = std::max(1, static_cast<int>(image.width * factor)), height = std::max(1, static_cast<int>(image.height * factor));
            const int left = box.left + (box.right - box.left - width) / 2, top = box.top + (box.bottom - box.top - height) / 2;
            const BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
            AlphaBlend(destination, left, top, width, height, source, 0, 0, static_cast<int>(image.width), static_cast<int>(image.height), blend);
            SelectObject(source, old);
        }
        if (source) DeleteDC(source); if (bitmap) DeleteObject(bitmap);
    }
    void Paint(HDC dc, const RECT& clip) noexcept {
        const int saved = SaveDC(dc);
        if (saved == 0) return;
        RECT client{}; GetClientRect(canvas, &client);
        if (IntersectClipRect(dc, client.left, client.top, client.right, client.bottom) == ERROR) { RestoreDC(dc, saved); return; }
        Fill(dc, clip, colors.surface);
        const HGDIOBJ previous = font ? SelectObject(dc, font) : nullptr; SetBkMode(dc, TRANSPARENT);
        if (model->page.entries.empty()) {
            RECT text{Scale(20), Scale(24), 0, 0}; GetClientRect(canvas, &text); InflateRect(&text, -Scale(20), -Scale(20));
            SetTextColor(dc, colors.muted);
            const wchar_t* message = model->page.status == snapshot::Status::Ready ? L"此文件夹为空" : snapshot::StatusText(model->page.status);
            DrawTextW(dc, message, -1, &text, DT_LEFT | DT_TOP | DT_WORDBREAK | DT_NOPREFIX);
        }
        for (const auto& placement : layout.items) {
            RECT card = placement.bounds; OffsetRect(&card, 0, -scroll); RECT intersection{};
            if (!IntersectRect(&intersection, &card, &clip)) continue;
            const UINT index = placement.index;
            Fill(dc, card, model->selected[index] ? colors.accent_soft : static_cast<int>(index) == hover ? colors.surface_strong : colors.surface);
            RECT image = placement.image; OffsetRect(&image, 0, -scroll);
            const auto& entry = model->page.entries[index];
            if (images[index]) PaintImage(dc, *images[index], image);
            else {
                const bool folder = entry.kind == snapshot::Kind::Directory || entry.kind == snapshot::Kind::Library;
                const int icon = std::min<int>(Scale(32), std::min(image.right - image.left, image.bottom - image.top));
                if (icon > 0) DrawIconEx(dc, image.left + (image.right - image.left - icon) / 2,
                    image.top + (image.bottom - image.top - icon) / 2, folder ? folderIcon : fileIcon, icon, icon, 0, nullptr, DI_NORMAL);
                if (entry.kind == snapshot::Kind::File && mode == Mode::Gallery && image.bottom - image.top > Scale(70)) {
                    RECT note = image; note.top = (image.top + image.bottom) / 2 + Scale(22); SetTextColor(dc, colors.muted);
                    DrawTextW(dc, model->thumbnailUnavailable[index] ? L"预览不可用" : L"等待预览", -1, &note, DT_CENTER | DT_SINGLELINE | DT_NOPREFIX);
                }
            }
            RECT caption = placement.caption; OffsetRect(&caption, 0, -scroll); InflateRect(&caption, -Scale(4), 0);
            SetTextColor(dc, colors.ink); DrawTextW(dc, entry.name.c_str(), static_cast<int>(entry.name.size()), &caption,
                DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
            if (model->focus == static_cast<int>(index) && GetFocus() == canvas) { InflateRect(&card, -1, -1); DrawFocusRect(dc, &card); }
        }
        if (previous) SelectObject(dc, previous);
        RestoreDC(dc, saved);
    }
    static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR id, DWORD_PTR reference) noexcept {
        auto* stored = reinterpret_cast<std::shared_ptr<State>*>(reference);
        OwnerReference owner((*stored)->callbacks.lifetimeOwner); const auto state = *stored;
        const bool isCanvas = hwnd == state->canvas;
        const bool isRoot = hwnd == state->window;
        try {
            if (message == WM_NCDESTROY) {
                if (isCanvas || isRoot) state->Retire(); RemoveWindowSubclass(hwnd, WindowProc, id); delete stored;
                return DefSubclassProc(hwnd, message, wParam, lParam);
            }
            if (isRoot && !state->retiring && state->callbacks.completion && message == state->callbacks.completionMessage)
                return state->callbacks.completion(state->callbacks.context, wParam, lParam);
            if (message == WM_SHOWWINDOW && isRoot) state->ChangeVisibility(wParam != 0);
            if (!state->Alive()) return DefSubclassProc(hwnd, message, wParam, lParam);
            if (message == WM_SIZE && isRoot) { state->Resize(); return 0; }
            if (isRoot && (message == WM_DPICHANGED_AFTERPARENT || message == WM_DPICHANGED)) {
                state->dpi = std::clamp(GetDpiForWindow(hwnd), 48u, 768u); state->UpdateFont(); state->Resize(); return 0;
            }
            if (message == WM_SETTINGCHANGE || message == WM_SYSCOLORCHANGE || message == WM_THEMECHANGED) {
                state->UpdateTheme(); InvalidateRect(state->canvas, nullptr, FALSE);
            }
            if (message == WM_SETFOCUS) {
                if (isRoot) SetFocus(state->canvas);
                if (state->Alive()) { InvalidateRect(state->canvas, nullptr, FALSE); state->Notify(state->callbacks.focusActivated); }
                if (isCanvas || isRoot) return 0;
            }
            if (message == WM_KILLFOCUS) { InvalidateRect(state->canvas, nullptr, FALSE); return 0; }
            if (message == WM_COMMAND && isRoot && HIWORD(wParam) == BN_CLICKED) { state->Command(LOWORD(wParam)); return 0; }
            if (message == WM_PAINT && isRoot) {
                PAINTSTRUCT paint{}; const HDC dc = BeginPaint(hwnd, &paint); if (dc) Fill(dc, paint.rcPaint, state->colors.surface_strong); EndPaint(hwnd, &paint); return 0;
            }
            if (isCanvas) return state->CanvasMessage(hwnd, message, wParam, lParam);
        } catch (const std::bad_alloc&) {
            if (state->Alive()) { state->Empty(snapshot::Status::Unavailable, state->model->generation + 1); InvalidateRect(state->canvas, nullptr, FALSE); state->Notify(state->callbacks.viewportChanged); }
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    void Command(WORD id) {
        if (id == 100) mode = Mode::Gallery;
        else if (id == 101) mode = Mode::List;
        else if (id == 102) density = std::max(MinimumDensityDip, density > 24 ? density - 24 : MinimumDensityDip);
        else if (id == 103) density = std::min(MaximumDensityDip, density + 24);
        else if (id == 104) { Notify(callbacks.refresh); return; }
        else if (id == 105) { Notify(callbacks.settings); return; }
        UpdateModeButtons(); model->title = mode == Mode::Gallery ? L"资产库图库" : L"资产库列表"; Reflow(true);
    }
    void UpdateModeButtons() noexcept {
        SendMessageW(buttons[0], BM_SETCHECK, mode == Mode::Gallery ? BST_CHECKED : BST_UNCHECKED, 0);
        SendMessageW(buttons[1], BM_SETCHECK, mode == Mode::List ? BST_CHECKED : BST_UNCHECKED, 0);
    }
    LRESULT CanvasMessage(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
        if (message == WM_GETOBJECT && static_cast<LONG>(lParam) == OBJID_CLIENT) {
            IAccessible* accessible = nullptr;
            if (FAILED(CreateAccessible(model, &accessible))) return 0;
            const LRESULT result = LresultFromObject(IID_IAccessible, wParam, accessible); accessible->Release(); return result;
        }
        if (message == WM_GETDLGCODE) return DLGC_WANTARROWS | DLGC_WANTCHARS;
        if (message == WM_KEYDOWN && Key(wParam)) return 0;
        if (message == WM_ERASEBKGND) return 1;
        if (message == WM_PRINTCLIENT && wParam) { RECT client{}; GetClientRect(hwnd, &client); Paint(reinterpret_cast<HDC>(wParam), client); return 0; }
        if (message == WM_PAINT) { PAINTSTRUCT paint{}; HDC dc = BeginPaint(hwnd, &paint); if (dc) Paint(dc, paint.rcPaint); EndPaint(hwnd, &paint); return 0; }
        if (message == WM_MOUSEWHEEL) { ScrollTo(scroll - GET_WHEEL_DELTA_WPARAM(wParam) * Scale(48) / WHEEL_DELTA); return 0; }
        if (message == WM_VSCROLL) {
            SCROLLINFO info{sizeof(info), SIF_ALL}; GetScrollInfo(hwnd, SB_VERT, &info); int next = scroll;
            switch (LOWORD(wParam)) {
                case SB_LINEUP: next -= Scale(32); break; case SB_LINEDOWN: next += Scale(32); break;
                case SB_PAGEUP: next -= static_cast<int>(info.nPage); break; case SB_PAGEDOWN: next += static_cast<int>(info.nPage); break;
                case SB_THUMBTRACK: next = info.nTrackPos; break; case SB_TOP: next = 0; break; case SB_BOTTOM: next = layout.height; break;
                default: break;
            }
            ScrollTo(next); return 0;
        }
        if (message == WM_MOUSEMOVE) {
            const POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) + scroll}; const int next = HitTest(layout, point);
            if (hover != next) { hover = next; InvalidateRect(hwnd, nullptr, FALSE); }
            TRACKMOUSEEVENT track{sizeof(track), TME_LEAVE, hwnd, 0}; TrackMouseEvent(&track); return 0;
        }
        if (message == WM_MOUSELEAVE) { hover = -1; InvalidateRect(hwnd, nullptr, FALSE); return 0; }
        if (message == WM_LBUTTONDOWN || message == WM_LBUTTONDBLCLK) {
            const POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) + scroll}; const int index = HitTest(layout, point);
            if (index >= 0) {
                SelectFromInput(static_cast<UINT>(index), (wParam & MK_CONTROL) != 0, (wParam & MK_SHIFT) != 0);
                if (Alive() && message == WM_LBUTTONDBLCLK) Activate(static_cast<UINT>(index));
            } else { model->selected.fill(false); SetFocus(canvas); if (Alive()) { InvalidateRect(canvas, nullptr, FALSE); Notify(callbacks.selectionChanged); } }
            return 0;
        }
        if (message == WM_CONTEXTMENU) {
            POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)}; int index = model->focus;
            if (point.x == -1 && point.y == -1) { point = {Scale(12), Scale(12)}; ClientToScreen(hwnd, &point); }
            else { POINT local = point; ScreenToClient(hwnd, &local); local.y += scroll; index = HitTest(layout, local); }
            const auto generation = model->generation;
            if (index >= 0 && !model->selected[static_cast<size_t>(index)]) Select(static_cast<UINT>(index), SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_FOCUSED);
            if (!Alive() || generation != model->generation) return 0;
            Menu(index, point); return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    HRESULT Attach(HWND target) noexcept {
        auto* storage = new(std::nothrow) std::shared_ptr<State>(shared_from_this());
        if (!storage) return E_OUTOFMEMORY;
        if (!SetWindowSubclass(target, WindowProc, reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(storage))) {
            delete storage; return HRESULT_FROM_WIN32(GetLastError());
        }
        return S_OK;
    }
};

Surface::Surface(std::shared_ptr<State> state) noexcept : state_(std::move(state)) {}
Surface::~Surface() { Destroy(); }

HRESULT Surface::Create(HWND parent, const RECT& bounds, const Callbacks& callbacks, Surface** result) noexcept {
    if (!result) return E_POINTER; *result = nullptr;
    if (!parent || !callbacks.lifetimeOwner || bounds.right < bounds.left || bounds.bottom < bounds.top ||
        (callbacks.completion && (callbacks.completionMessage < WM_APP || callbacks.completionMessage > 0xBFFF))) return E_INVALIDARG;
    try {
        auto state = std::make_shared<State>(); state->callbacks = callbacks; state->model->lifetimeOwner = callbacks.lifetimeOwner;
        auto hr = PrepareAccessibility(*state->model); if (FAILED(hr)) return hr;
        state->window = CreateWindowExW(WS_EX_CONTROLPARENT, L"STATIC", L"资产库", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top, parent, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!state->window) return HRESULT_FROM_WIN32(GetLastError());
        hr = state->Attach(state->window); if (FAILED(hr)) { DestroyWindow(state->window); return hr; }
        state->canvas = CreateWindowExW(0, L"STATIC", L"资产库图库", WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | SS_NOTIFY,
            0, 0, 1, 1, state->window, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!state->canvas) { const auto error = HRESULT_FROM_WIN32(GetLastError()); DestroyWindow(state->window); return error; }
        hr = state->Attach(state->canvas); if (FAILED(hr)) { DestroyWindow(state->window); return hr; }
        state->model->window = state->canvas; state->model->context = state.get();
        state->model->select = [](void* context, UINT index, UINT flags) noexcept -> HRESULT {
            auto retained = static_cast<State*>(context)->shared_from_this();
            try { return retained->Select(index, flags); } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        };
        state->model->extend = [](void* context, UINT index) noexcept -> HRESULT {
            auto retained = static_cast<State*>(context)->shared_from_this();
            try { retained->SelectFromInput(index, false, true); return S_OK; } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        };
        state->model->activate = [](void* context, UINT index) noexcept { auto retained = static_cast<State*>(context)->shared_from_this(); retained->Activate(index); };
        const wchar_t* labels[] = {L"图库", L"列表", L"缩小", L"放大", L"刷新", L"连接设置"};
        for (size_t index = 0; index < state->buttons.size(); ++index) {
            const DWORD buttonStyle = index < 2 ? BS_AUTORADIOBUTTON | BS_PUSHLIKE : BS_PUSHBUTTON;
            state->buttons[index] = CreateWindowExW(0, L"BUTTON", labels[index], WS_CHILD | WS_VISIBLE | WS_TABSTOP | buttonStyle,
                0, 0, 1, 1, state->window, reinterpret_cast<HMENU>(100 + index), GetModuleHandleW(nullptr), nullptr);
            if (!state->buttons[index]) { const auto error = HRESULT_FROM_WIN32(GetLastError()); DestroyWindow(state->window); return error; }
            hr = state->Attach(state->buttons[index]); if (FAILED(hr)) { DestroyWindow(state->window); return hr; }
        }
        state->dpi = std::clamp(GetDpiForWindow(parent), 48u, 768u); state->UpdateTheme(); state->UpdateFont();
        state->UpdateModeButtons();
        SHSTOCKICONINFO icon{sizeof(icon)};
        if (SUCCEEDED(SHGetStockIconInfo(SIID_FOLDER, SHGSI_ICON | SHGSI_SMALLICON, &icon))) state->folderIcon = icon.hIcon;
        if (SUCCEEDED(SHGetStockIconInfo(SIID_DOCNOASSOC, SHGSI_ICON | SHGSI_SMALLICON, &icon))) state->fileIcon = icon.hIcon;
        state->Resize();
        if (!state->Alive()) return E_ABORT;
        auto surface = new(std::nothrow) Surface(state); if (!surface) { DestroyWindow(state->window); return E_OUTOFMEMORY; }
        *result = surface; return S_OK;
    } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
}

void Surface::Destroy() noexcept {
    if (!state_ || state_->retiring) return;
    const OwnedState call(state_); const auto& state = call.state;
    const HWND window = state->window; state->Retire(); if (window && IsWindow(window)) DestroyWindow(window);
}
HWND Surface::Window() const noexcept { const auto state = state_; return state && !state->retiring ? state->window : nullptr; }
bool Surface::Shown() const noexcept { const auto state = state_; return state && state->Alive() && state->shown; }

HRESULT Surface::SetPage(const snapshot::Page& page, std::uint64_t generation) noexcept {
    if (!state_ || !state_->Alive() || page.entries.size() > snapshot::MaxItems) return E_INVALIDARG;
    const OwnedState call(state_); const auto& state = call.state;
    if (generation < state->model->generation) return S_FALSE;
    try {
        snapshot::Page next = page;
        state->Empty(page.status, generation);
        if (page.status == snapshot::Status::Ready) state->model->page = std::move(next);
        for (const auto& item : state->model->page.entries) state->layoutItems.push_back({item.kind, 4.0 / 3.0});
        state->Reflow(false);
        if (state->Alive() && state->visible.count == 0) state->Notify(state->callbacks.viewportChanged);
        if (state->Alive()) { NotifyWinEvent(EVENT_OBJECT_REORDER, state->canvas, OBJID_CLIENT, CHILDID_SELF); state->Notify(state->callbacks.selectionChanged); }
        return S_OK;
    } catch (const std::bad_alloc&) { state->Empty(snapshot::Status::Unavailable, generation); if (state->Alive()) state->Notify(state->callbacks.viewportChanged); return E_OUTOFMEMORY; }
}
void Surface::Clear(snapshot::Status status, std::uint64_t generation) noexcept {
    if (!state_ || !state_->Alive()) return;
    const OwnedState call(state_); const auto& state = call.state;
    if (generation < state->model->generation) return;
    state->Empty(status, generation); NotifyWinEvent(EVENT_OBJECT_REORDER, state->canvas, OBJID_CLIENT, CHILDID_SELF);
    InvalidateRect(state->canvas, nullptr, FALSE); state->Notify(state->callbacks.viewportChanged);
    if (state->Alive()) state->Notify(state->callbacks.selectionChanged);
}
HRESULT Surface::SetThumbnail(UINT index, std::uint64_t generation, std::shared_ptr<const Pbgra> image) noexcept {
    if (!state_ || !state_->Alive()) return S_FALSE;
    const OwnedState call(state_); const auto& state = call.state;
    if (generation != state->model->generation ||
        index >= state->model->page.entries.size() || state->model->page.entries[index].kind != snapshot::Kind::File || !Contains(state->visible, index)) return S_FALSE;
    if (image && !PixelsValid(*image)) return E_INVALIDARG;
    size_t retained = 0;
    for (UINT at = 0; at < state->images.size(); ++at) if (at != index && state->images[at]) retained += state->images[at]->pixels.capacity();
    if (image && image->pixels.capacity() > MaxImageBytes - retained) {
        state->images[index].reset(); state->model->thumbnailReady[index] = false; state->model->thumbnailUnavailable[index] = true;
        InvalidateRect(state->canvas, &state->model->bounds[index], FALSE);
        NotifyWinEvent(EVENT_OBJECT_DESCRIPTIONCHANGE, state->canvas, OBJID_CLIENT, static_cast<LONG>(index + 1));
        return E_OUTOFMEMORY;
    }
    try {
        state->images[index] = std::move(image); state->model->thumbnailReady[index] = !!state->images[index];
        state->model->thumbnailUnavailable[index] = !state->images[index];
        if (state->images[index]) state->layoutItems[index].aspect = static_cast<double>(state->images[index]->width) / state->images[index]->height;
        state->Reflow(true);
        if (state->Alive() && state->model->generation == generation) NotifyWinEvent(EVENT_OBJECT_DESCRIPTIONCHANGE, state->canvas, OBJID_CLIENT, static_cast<LONG>(index + 1));
        return S_OK;
    } catch (const std::bad_alloc&) { state->images[index].reset(); return E_OUTOFMEMORY; }
}
VisibleFiles Surface::VisibleFileItems() const noexcept { const auto state = state_; return state && state->Alive() ? state->visible : VisibleFiles{}; }
size_t Surface::RetainedImageBytes() const noexcept {
    const auto state = state_; size_t bytes = 0; if (state) for (const auto& image : state->images) if (image) bytes += image->pixels.capacity(); return bytes;
}
Selection Surface::SelectedItems() const noexcept {
    const auto state = state_; Selection result;
    if (state && state->Alive()) for (UINT index = 0; index < state->model->page.entries.size(); ++index) if (state->model->selected[index]) result.indices[result.count++] = index;
    return result;
}
int Surface::FocusedItem() const noexcept { const auto state = state_; return state && state->Alive() ? state->model->focus : -1; }
HRESULT Surface::SelectItem(UINT index, UINT flags) noexcept {
    if (!state_ || !state_->Alive()) return E_INVALIDARG;
    const OwnedState call(state_); const auto& state = call.state;
    try { return state->Select(index, flags); } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
}
HRESULT Surface::ItemRect(UINT index, RECT* rectangle) const noexcept {
    if (!rectangle) return E_POINTER; *rectangle = {};
    const auto state = state_; if (!state || !state->Alive() || index >= state->model->page.entries.size()) return E_INVALIDARG;
    *rectangle = state->model->bounds[index]; POINT origin{}; MapWindowPoints(state->canvas, state->window, &origin, 1); OffsetRect(rectangle, origin.x, origin.y); return S_OK;
}
void Surface::Focus() noexcept {
    if (!state_ || !state_->Alive()) return;
    const OwnedState call(state_); SetFocus(call.state->canvas);
}
bool Surface::TranslateAccelerator(const MSG& message) noexcept {
    if (!state_ || !state_->Alive() || message.message != WM_KEYDOWN) return false;
    const OwnedState call(state_); const auto& state = call.state;
    if (message.wParam == VK_TAB) {
        if ((GetKeyState(VK_CONTROL) & 0x8000) || (GetKeyState(VK_MENU) & 0x8000)) return false;
        std::array<HWND, 7> order{state->canvas, state->buttons[0], state->buttons[1], state->buttons[2], state->buttons[3], state->buttons[4], state->buttons[5]};
        const auto at = std::find(order.begin(), order.end(), message.hwnd);
        if (at == order.end()) return false;
        const auto next = (at - order.begin()) + ((GetKeyState(VK_SHIFT) & 0x8000) ? -1 : 1);
        if (next < 0 || next >= static_cast<ptrdiff_t>(order.size())) return false;
        SetFocus(order[static_cast<size_t>(next)]); return true;
    }
    if (message.hwnd != state->canvas && message.hwnd != state->window) return false;
    try { return state->Key(message.wParam); } catch (const std::bad_alloc&) { return false; }
}
void Surface::SetVisible(bool visible) noexcept {
    if (!state_ || !state_->Alive()) return;
    const OwnedState call(state_); const auto& state = call.state;
    ShowWindow(state->window, visible ? SW_SHOWNA : SW_HIDE);
    if (state->Alive()) state->ChangeVisibility(visible);
}
void Surface::SetMode(Mode mode) noexcept {
    if (!state_ || !state_->Alive()) return;
    const OwnedState call(state_); const auto& state = call.state;
    try { state->mode = mode; state->UpdateModeButtons(); state->model->title = mode == Mode::Gallery ? L"资产库图库" : L"资产库列表"; state->Reflow(true); }
    catch (const std::bad_alloc&) { state->Empty(snapshot::Status::Unavailable, state->model->generation + 1); state->Notify(state->callbacks.viewportChanged); }
}
Mode Surface::CurrentMode() const noexcept { const auto state = state_; return state ? state->mode : Mode::Gallery; }
void Surface::SetDensity(UINT value) noexcept {
    if (!state_ || !state_->Alive()) return;
    const OwnedState call(state_); const auto& state = call.state;
    try { state->density = std::clamp(value, MinimumDensityDip, MaximumDensityDip); state->Reflow(true); }
    catch (const std::bad_alloc&) { state->Empty(snapshot::Status::Unavailable, state->model->generation + 1); state->Notify(state->callbacks.viewportChanged); }
}
UINT Surface::Density() const noexcept { const auto state = state_; return state ? state->density : DefaultDensityDip; }
} // namespace gallery
