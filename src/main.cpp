#include <windows.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <string>
#include <vector>

#include "config.hpp"
#include "math.hpp"
#include "offsets.hpp"
#include "process.hpp"
#include "renderer.hpp"
#include "sdk.hpp"

namespace {

volatile bool g_running = true;

BOOL WINAPI ConsoleHandler(DWORD signal) {
    switch (signal) {
        case CTRL_C_EVENT:
        case CTRL_BREAK_EVENT:
        case CTRL_CLOSE_EVENT:
            g_running = false;
            return TRUE;
        default:
            return FALSE;
    }
}

bool KeyDown(int vk) {
    return (GetAsyncKeyState(vk) & 0x8000) != 0;
}

// Rising-edge detection for a toggle key.
bool KeyPressed(int vk, bool& previous) {
    const bool down = KeyDown(vk);
    const bool pressed = down && !previous;
    previous = down;
    return pressed;
}

std::string FormatDistance(float units) {
    const float meters = units * config::kUnitsToMeters;
    char buffer[32]{};
    std::snprintf(buffer, sizeof(buffer), "%.0fm", static_cast<double>(meters));
    return std::string(buffer);
}

COLORREF TeamColor(int team, int local_team) {
    if (local_team != sdk::kTeamUnassigned && team == local_team) {
        return config::kColorAlly;
    }
    return config::kColorEnemy;
}

COLORREF HealthColor(int health) {
    const int clamped = std::clamp(health, 0, config::kMaxHealth);
    const float ratio = static_cast<float>(clamped) / static_cast<float>(config::kMaxHealth);

    const auto blend = [ratio](int low, int high) {
        const float value = static_cast<float>(low) + ((static_cast<float>(high) - static_cast<float>(low)) * ratio);
        return static_cast<int>(std::clamp(value, 0.0F, 255.0F));
    };

    return RGB(blend(GetRValue(config::kColorHealthLow), GetRValue(config::kColorHealthHigh)),
               blend(GetGValue(config::kColorHealthLow), GetGValue(config::kColorHealthHigh)),
               blend(GetBValue(config::kColorHealthLow), GetBValue(config::kColorHealthHigh)));
}

struct ScreenBox {
    int x{0};
    int y{0};
    int w{0};
    int h{0};
};

// Projects a pawn into a screen-space box. Returns false when either anchor is
// off-projection, when the box degenerates, or when it lies fully outside the
// viewport.
bool ProjectBox(const math::ViewMatrix& view, const sdk::Player& player, int width, int height, ScreenBox& out) {
    math::Vec2 feet{};
    math::Vec2 head{};
    if (!math::WorldToScreen(view, player.origin, width, height, feet)) {
        return false;
    }
    if (!math::WorldToScreen(view, player.head, width, height, head)) {
        return false;
    }

    const float box_height = feet.y - head.y;
    if (!math::IsFinite(box_height) || box_height < static_cast<float>(config::kMinBoxHeightPx) ||
        box_height > static_cast<float>(config::kMaxBoxHeightPx)) {
        return false;
    }

    const float box_width = box_height * config::kBoxWidthRatio;
    if (box_width < 1.0F) {
        return false;
    }

    out.h = static_cast<int>(box_height);
    out.w = static_cast<int>(box_width);
    out.x = static_cast<int>(head.x - (box_width * 0.5F));
    out.y = static_cast<int>(head.y);

    if (out.x + out.w < 0 || out.x > width || out.y + out.h < 0 || out.y > height) {
        return false;
    }
    return true;
}

void DrawPlayer(Overlay& overlay, const sdk::Player& player, const ScreenBox& box, int local_team) {
    const COLORREF color = TeamColor(player.team, local_team);

    if (config::kDrawSnaplines) {
        overlay.Line(overlay.Width() / 2, overlay.Height(), box.x + (box.w / 2), box.y + box.h, color);
    }

    if (config::kDrawBox) {
        overlay.Box(box.x, box.y, box.w, box.h, color);
    }

    if (config::kDrawHealthBar) {
        const int bar_x = box.x - config::kHealthBarGapPx - config::kHealthBarWidthPx;
        overlay.Fill(bar_x, box.y, config::kHealthBarWidthPx, box.h, config::kColorBarBackdrop);

        const int clamped = std::clamp(player.health, 0, config::kMaxHealth);
        const int filled = (box.h * clamped) / config::kMaxHealth;
        if (filled > 0) {
            overlay.Fill(bar_x, box.y + (box.h - filled), config::kHealthBarWidthPx, filled, HealthColor(player.health));
        }
    }

    if (config::kDrawName && player.name[0] != '\0') {
        overlay.LabelCentered(box.x + (box.w / 2), box.y - config::kFontHeight - 2, config::kColorText,
                              std::string(player.name));
    }

    if (config::kDrawDistance) {
        std::string footer = FormatDistance(player.distance);
        footer += "  ";
        footer += std::to_string(std::clamp(player.health, 0, config::kHealthSanityLimit));
        footer += "hp";
        overlay.LabelCentered(box.x + (box.w / 2), box.y + box.h + 2, config::kColorText, footer);
    }
}

bool ReportMissingOffsets() {
    const std::vector<const char*> missing = cs2_dumper::MissingOffsets();
    if (missing.empty()) {
        if (!cs2_dumper::HasViewOffset()) {
            std::printf("[warn] m_vecViewOffset is unset: head height uses config::kFallbackEyeHeight (%.1f units).\n",
                        static_cast<double>(config::kFallbackEyeHeight));
        }
        return true;
    }

    std::printf("[error] %zu required offset(s) are unset in src/offsets.hpp:\n", missing.size());
    for (const char* name : missing) {
        std::printf("          %s\n", name);
    }
    std::printf(
        "\n        Dump the current CS2 build with cs2-dumper (https://github.com/a2x/cs2-dumper)\n"
        "        and copy the named client_dll values into src/offsets.hpp, then rebuild.\n");
    return false;
}

}  // namespace

int main() {
    SetConsoleCtrlHandler(&ConsoleHandler, TRUE);
    SetProcessDPIAware();

    std::printf("cs2_range_esp - external read-only overlay\n");
    std::printf("  exit: VK_END    toggle drawing: VK_INSERT\n");
    std::printf("  team check: %s   visibility filter: not implemented\n\n",
                config::kTeamCheck ? "on" : "off");

    if (!ReportMissingOffsets()) {
        return 2;
    }

    Overlay overlay;
    if (!overlay.Create()) {
        std::printf("[error] Overlay window creation failed (%lu).\n", GetLastError());
        return 3;
    }

    Process proc;
    std::uintptr_t client_base = 0;
    HWND game_window = nullptr;
    bool drawing = true;
    bool toggle_previous = false;
    DWORD last_attach_attempt = 0;
    bool announced = false;

    std::vector<sdk::Player> players;
    players.reserve(static_cast<std::size_t>(config::kMaxEntities));

    while (g_running) {
        if (!overlay.Pump()) {
            break;
        }
        if (KeyDown(config::kExitKey)) {
            break;
        }
        if (KeyPressed(config::kToggleKey, toggle_previous)) {
            drawing = !drawing;
            std::printf("[info] drawing %s\n", drawing ? "on" : "off");
            if (!drawing) {
                overlay.Hide();
            }
        }

        // --- Attach / reattach -------------------------------------------
        if (!proc.IsAttached() || !proc.IsAlive()) {
            if (proc.IsAttached()) {
                std::printf("[info] client exited, detaching.\n");
            }
            proc.Detach();
            client_base = 0;
            game_window = nullptr;
            announced = false;
            overlay.Hide();

            const DWORD now = GetTickCount();
            if (now - last_attach_attempt < static_cast<DWORD>(config::kReattachDelayMs)) {
                Sleep(static_cast<DWORD>(config::kTickMs));
                continue;
            }
            last_attach_attempt = now;

            if (!proc.Attach(config::kProcessName)) {
                Sleep(static_cast<DWORD>(config::kReattachDelayMs));
                continue;
            }
            std::printf("[info] attached to pid %lu (read-only).\n", proc.Pid());
        }

        // --- Module resolution -------------------------------------------
        if (client_base == 0) {
            client_base = proc.ModuleBase(config::kClientModule);
            if (client_base == 0) {
                Sleep(static_cast<DWORD>(config::kReattachDelayMs));
                continue;
            }
            std::printf("[info] client.dll at 0x%llX\n", static_cast<unsigned long long>(client_base));
        }

        // --- Target window ------------------------------------------------
        if (game_window == nullptr || IsWindow(game_window) == 0) {
            game_window = proc.MainWindow();
            if (game_window == nullptr) {
                Sleep(static_cast<DWORD>(config::kReattachDelayMs));
                continue;
            }
        }

        if (!drawing) {
            Sleep(static_cast<DWORD>(config::kTickMs));
            continue;
        }

        if (!overlay.Follow(game_window)) {
            Sleep(static_cast<DWORD>(config::kTickMs));
            continue;
        }

        // --- Local state ---------------------------------------------------
        const std::uintptr_t local_pawn = sdk::LocalPawn(proc, client_base);
        const auto view = sdk::ViewMatrix(proc, client_base);
        if (local_pawn == 0 || !view) {
            overlay.Begin();
            overlay.End();
            Sleep(static_cast<DWORD>(config::kTickMs));
            continue;
        }

        const int local_team = sdk::PawnTeam(proc, local_pawn);
        const auto local_origin = sdk::PawnOrigin(proc, local_pawn);
        const math::Vec3 origin = local_origin ? *local_origin : math::Vec3{};

        if (!announced) {
            std::printf("[info] local pawn resolved, rendering.\n");
            announced = true;
        }

        // --- Acquire, then render -----------------------------------------
        sdk::CollectPlayers(proc, client_base, local_pawn, local_team, origin, players);

        overlay.Begin();
        for (const sdk::Player& player : players) {
            ScreenBox box{};
            if (!ProjectBox(*view, player, overlay.Width(), overlay.Height(), box)) {
                continue;
            }
            DrawPlayer(overlay, player, box, local_team);
        }
        overlay.End();

        Sleep(static_cast<DWORD>(config::kTickMs));
    }

    overlay.Destroy();
    proc.Detach();
    std::printf("[info] stopped.\n");
    return 0;
}
