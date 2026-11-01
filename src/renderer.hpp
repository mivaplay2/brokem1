#pragma once

#include <windows.h>

#include <string>
#include <unordered_map>

// Click-through, top-most layered window drawn with GDI into an off-screen
// surface and blitted once per frame. config::kColorKey is the transparency key,
// so pure black is never drawn.
//
// The renderer knows nothing about the game: callers hand it pixels.
class Overlay {
public:
    Overlay() = default;
    ~Overlay();

    Overlay(const Overlay&) = delete;
    Overlay& operator=(const Overlay&) = delete;

    bool Create();
    void Destroy();

    // Drains the message queue. Returns false once the window has been closed.
    bool Pump();

    // Moves and resizes the overlay onto the target's client area.
    // Returns false (and hides the overlay) when the target is gone, minimized
    // or has an empty client rect.
    bool Follow(HWND target);

    void Hide();

    void Begin();
    void End();

    int Width() const noexcept { return width_; }
    int Height() const noexcept { return height_; }

    void Box(int x, int y, int w, int h, COLORREF color);
    void Fill(int x, int y, int w, int h, COLORREF color);
    void Line(int x1, int y1, int x2, int y2, COLORREF color);
    void Label(int x, int y, COLORREF color, const std::string& text);
    void LabelCentered(int center_x, int y, COLORREF color, const std::string& text);
    int TextWidth(const std::string& text) const;

private:
    static LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam);

    bool EnsureSurface(int width, int height);
    void ReleaseSurface();
    HPEN PenFor(COLORREF color);
    HBRUSH BrushFor(COLORREF color);

    HWND hwnd_{nullptr};
    HDC window_dc_{nullptr};
    HDC mem_dc_{nullptr};
    HBITMAP bitmap_{nullptr};
    HGDIOBJ old_bitmap_{nullptr};
    HFONT font_{nullptr};
    HGDIOBJ old_font_{nullptr};

    int width_{0};
    int height_{0};
    bool visible_{false};
    bool closed_{false};

    std::unordered_map<COLORREF, HPEN> pens_;
    std::unordered_map<COLORREF, HBRUSH> brushes_;
};
