#pragma once

#include <windows.h>

namespace config {

// --- Target ---------------------------------------------------------------
inline constexpr wchar_t kProcessName[] = L"cs2.exe";
inline constexpr wchar_t kClientModule[] = L"client.dll";

// --- Control --------------------------------------------------------------
inline constexpr int kExitKey = VK_END;      // Stops the loop and tears the overlay down.
inline constexpr int kToggleKey = VK_INSERT;  // Hides / shows the drawing without detaching.
inline constexpr int kTickMs = 8;             // Overlay loop pacing in milliseconds.
inline constexpr int kReattachDelayMs = 500;  // Throttle between attach attempts.

// --- Entity pipeline ------------------------------------------------------
inline constexpr int kMaxEntities = 128;        // Entity indices scanned per frame.
inline constexpr bool kTeamCheck = true;        // Skip pawns on the local pawn's team.
inline constexpr int kMaxHealth = 100;          // Health bar reference value.
inline constexpr int kHealthSanityLimit = 1000; // Reject pawns outside (0, limit].
inline constexpr float kMaxDrawDistance = 4096.0F;  // World units.
inline constexpr float kFallbackEyeHeight = 64.0F;  // Used when m_vecViewOffset is unset.
inline constexpr float kHeadRoom = 8.0F;            // Units added above the eye position.
inline constexpr float kUnitsToMeters = 0.0254F;    // 1 world unit == 1 inch.

// Visibility filtering is NOT implemented: this companion has no visibility data
// path (no traces, no occlusion query), so no visibility claim is made and the
// flag stays off.
inline constexpr bool kVisibleOnly = false;

// --- Rendering ------------------------------------------------------------
inline constexpr bool kDrawBox = true;
inline constexpr bool kDrawHealthBar = true;
inline constexpr bool kDrawName = true;
inline constexpr bool kDrawDistance = true;
inline constexpr bool kDrawSnaplines = false;

inline constexpr float kBoxWidthRatio = 0.42F;  // Box width as a fraction of projected height.
inline constexpr int kMinBoxHeightPx = 6;
inline constexpr int kMaxBoxHeightPx = 4096;
inline constexpr int kHealthBarWidthPx = 3;
inline constexpr int kHealthBarGapPx = 3;
inline constexpr int kFontHeight = 14;
inline constexpr wchar_t kFontFace[] = L"Tahoma";

// Pure black is the layered-window color key, so it must never be drawn.
inline constexpr COLORREF kColorKey = RGB(0, 0, 0);
inline constexpr COLORREF kColorEnemy = RGB(235, 70, 70);
inline constexpr COLORREF kColorAlly = RGB(80, 150, 255);
inline constexpr COLORREF kColorText = RGB(235, 235, 235);
inline constexpr COLORREF kColorTextShadow = RGB(8, 8, 8);
inline constexpr COLORREF kColorHealthHigh = RGB(90, 220, 90);
inline constexpr COLORREF kColorHealthLow = RGB(230, 80, 60);
inline constexpr COLORREF kColorBarBackdrop = RGB(24, 24, 24);

}  // namespace config
