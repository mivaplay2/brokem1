#include "renderer.hpp"

#include <algorithm>

#include "config.hpp"

namespace {

constexpr wchar_t kWindowClass[] = L"Cs2RangeEspOverlay";
constexpr wchar_t kWindowTitle[] = L"";

bool RegisterOverlayClass(WNDPROC proc) {
    static bool registered = false;
    if (registered) {
        return true;
    }

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = proc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = nullptr;
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kWindowClass;

    if (RegisterClassExW(&wc) == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        return false;
    }

    registered = true;
    return true;
}

}  // namespace

Overlay::~Overlay() {
    Destroy();
}

LRESULT CALLBACK Overlay::WindowProc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam) {
    switch (msg) {
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
        case WM_ERASEBKGND:
            return 1;
        default:
            break;
    }
    return DefWindowProcW(hwnd, msg, wparam, lparam);
}

bool Overlay::Create() {
    if (hwnd_ != nullptr) {
        return true;
    }
    if (!RegisterOverlayClass(&Overlay::WindowProc)) {
        return false;
    }

    hwnd_ = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        kWindowClass,
        kWindowTitle,
        WS_POPUP,
        0, 0, 1, 1,
        nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (hwnd_ == nullptr) {
        return false;
    }

    if (SetLayeredWindowAttributes(hwnd_, config::kColorKey, 255, LWA_COLORKEY) == 0) {
        Destroy();
        return false;
    }

    window_dc_ = GetDC(hwnd_);
    if (window_dc_ == nullptr) {
        Destroy();
        return false;
    }

    mem_dc_ = CreateCompatibleDC(window_dc_);
    if (mem_dc_ == nullptr) {
        Destroy();
        return false;
    }

    font_ = CreateFontW(
        -config::kFontHeight, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_TT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, config::kFontFace);
    if (font_ != nullptr) {
        old_font_ = SelectObject(mem_dc_, font_);
    }

    SetBkMode(mem_dc_, TRANSPARENT);
    ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
    visible_ = true;
    closed_ = false;
    return true;
}

void Overlay::ReleaseSurface() {
    if (mem_dc_ != nullptr && old_bitmap_ != nullptr) {
        SelectObject(mem_dc_, old_bitmap_);
        old_bitmap_ = nullptr;
    }
    if (bitmap_ != nullptr) {
        DeleteObject(bitmap_);
        bitmap_ = nullptr;
    }
    width_ = 0;
    height_ = 0;
}

void Overlay::Destroy() {
    for (const auto& [color, pen] : pens_) {
        (void)color;
        DeleteObject(pen);
    }
    pens_.clear();

    for (const auto& [color, brush] : brushes_) {
        (void)color;
        DeleteObject(brush);
    }
    brushes_.clear();

    ReleaseSurface();

    if (mem_dc_ != nullptr) {
        if (old_font_ != nullptr) {
            SelectObject(mem_dc_, old_font_);
            old_font_ = nullptr;
        }
        DeleteDC(mem_dc_);
        mem_dc_ = nullptr;
    }
    if (font_ != nullptr) {
        DeleteObject(font_);
        font_ = nullptr;
    }
    if (window_dc_ != nullptr && hwnd_ != nullptr) {
        ReleaseDC(hwnd_, window_dc_);
    }
    window_dc_ = nullptr;

    if (hwnd_ != nullptr) {
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
    visible_ = false;
}

bool Overlay::Pump() {
    MSG msg{};
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
        if (msg.message == WM_QUIT) {
            closed_ = true;
            continue;
        }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return !closed_;
}

bool Overlay::EnsureSurface(int width, int height) {
    if (width <= 0 || height <= 0) {
        return false;
    }
    if (bitmap_ != nullptr && width == width_ && height == height_) {
        return true;
    }

    ReleaseSurface();

    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(info.bmiHeader);
    info.bmiHeader.biWidth = width;
    info.bmiHeader.biHeight = -height;  // Top-down.
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    bitmap_ = CreateDIBSection(window_dc_, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bitmap_ == nullptr) {
        return false;
    }

    old_bitmap_ = SelectObject(mem_dc_, bitmap_);
    width_ = width;
    height_ = height;
    SetBkMode(mem_dc_, TRANSPARENT);
    return true;
}

void Overlay::Hide() {
    if (hwnd_ != nullptr && visible_) {
        ShowWindow(hwnd_, SW_HIDE);
        visible_ = false;
    }
}

bool Overlay::Follow(HWND target) {
    if (hwnd_ == nullptr) {
        return false;
    }
    if (target == nullptr || IsWindow(target) == 0 || IsIconic(target) != 0) {
        Hide();
        return false;
    }

    RECT client{};
    if (GetClientRect(target, &client) == 0) {
        Hide();
        return false;
    }

    POINT origin{client.left, client.top};
    if (ClientToScreen(target, &origin) == 0) {
        Hide();
        return false;
    }

    const int width = client.right - client.left;
    const int height = client.bottom - client.top;
    if (width <= 0 || height <= 0) {
        Hide();
        return false;
    }

    SetWindowPos(hwnd_, HWND_TOPMOST, origin.x, origin.y, width, height, SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    if (!visible_) {
        ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
        visible_ = true;
    }

    return EnsureSurface(width, height);
}

HPEN Overlay::PenFor(COLORREF color) {
    const auto found = pens_.find(color);
    if (found != pens_.end()) {
        return found->second;
    }

    const HPEN pen = CreatePen(PS_SOLID, 1, color);
    if (pen == nullptr) {
        return nullptr;
    }
    pens_.emplace(color, pen);
    return pen;
}

HBRUSH Overlay::BrushFor(COLORREF color) {
    const auto found = brushes_.find(color);
    if (found != brushes_.end()) {
        return found->second;
    }

    const HBRUSH brush = CreateSolidBrush(color);
    if (brush == nullptr) {
        return nullptr;
    }
    brushes_.emplace(color, brush);
    return brush;
}

void Overlay::Begin() {
    if (mem_dc_ == nullptr || bitmap_ == nullptr) {
        return;
    }

    const RECT full{0, 0, width_, height_};
    const HBRUSH key = BrushFor(config::kColorKey);
    if (key != nullptr) {
        FillRect(mem_dc_, &full, key);
    }
}

void Overlay::End() {
    if (window_dc_ == nullptr || mem_dc_ == nullptr || bitmap_ == nullptr) {
        return;
    }
    BitBlt(window_dc_, 0, 0, width_, height_, mem_dc_, 0, 0, SRCCOPY);
}

void Overlay::Fill(int x, int y, int w, int h, COLORREF color) {
    if (mem_dc_ == nullptr || bitmap_ == nullptr || w <= 0 || h <= 0) {
        return;
    }

    const int left = std::clamp(x, 0, width_);
    const int top = std::clamp(y, 0, height_);
    const int right = std::clamp(x + w, 0, width_);
    const int bottom = std::clamp(y + h, 0, height_);
    if (right <= left || bottom <= top) {
        return;
    }

    const HBRUSH brush = BrushFor(color);
    if (brush == nullptr) {
        return;
    }

    const RECT rect{left, top, right, bottom};
    FillRect(mem_dc_, &rect, brush);
}

void Overlay::Box(int x, int y, int w, int h, COLORREF color) {
    if (w <= 0 || h <= 0) {
        return;
    }
    Fill(x, y, w, 1, color);
    Fill(x, y + h - 1, w, 1, color);
    Fill(x, y, 1, h, color);
    Fill(x + w - 1, y, 1, h, color);
}

void Overlay::Line(int x1, int y1, int x2, int y2, COLORREF color) {
    if (mem_dc_ == nullptr || bitmap_ == nullptr) {
        return;
    }

    const HPEN pen = PenFor(color);
    if (pen == nullptr) {
        return;
    }

    const HGDIOBJ previous = SelectObject(mem_dc_, pen);
    MoveToEx(mem_dc_, x1, y1, nullptr);
    LineTo(mem_dc_, x2, y2);
    SelectObject(mem_dc_, previous);
}

int Overlay::TextWidth(const std::string& text) const {
    if (mem_dc_ == nullptr || text.empty()) {
        return 0;
    }

    SIZE size{};
    if (GetTextExtentPoint32A(mem_dc_, text.c_str(), static_cast<int>(text.size()), &size) == 0) {
        return 0;
    }
    return size.cx;
}

void Overlay::Label(int x, int y, COLORREF color, const std::string& text) {
    if (mem_dc_ == nullptr || bitmap_ == nullptr || text.empty()) {
        return;
    }
    if (y < -config::kFontHeight || y > height_) {
        return;
    }

    const int length = static_cast<int>(text.size());

    SetTextColor(mem_dc_, config::kColorTextShadow);
    TextOutA(mem_dc_, x + 1, y + 1, text.c_str(), length);

    SetTextColor(mem_dc_, color);
    TextOutA(mem_dc_, x, y, text.c_str(), length);
}

void Overlay::LabelCentered(int center_x, int y, COLORREF color, const std::string& text) {
    Label(center_x - (TextWidth(text) / 2), y, color, text);
}
