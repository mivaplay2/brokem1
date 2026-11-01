#pragma once

#include <cstddef>
#include <vector>

// ---------------------------------------------------------------------------
// Build-specific target values.
//
// Every entry in this header is intentionally unset (0x0). The program refuses
// to start while any of them is unset instead of reading from module base + 0.
//
// Recipe, once per CS2 update:
//   1. git clone https://github.com/a2x/cs2-dumper
//   2. cargo build --release
//   3. Run the dumper while CS2 is running.
//   4. Copy values from the generated output/offsets.hpp      -> offsets::client_dll
//      and          from the generated output/client_dll.hpp  -> schemas::client_dll
// ---------------------------------------------------------------------------

namespace cs2_dumper {

namespace offsets::client_dll {

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t dwEntityList = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t dwLocalPlayerPawn = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t dwViewMatrix = 0x0;

}  // namespace offsets::client_dll

namespace schemas::client_dll {

namespace C_BaseEntity {

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_iHealth = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_iTeamNum = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_pGameSceneNode = 0x0;

}  // namespace C_BaseEntity

namespace C_BasePlayerPawn {

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
//       Confirm the owning class in the current dump; depending on the build the
//       dumper may list m_vecViewOffset on C_BaseEntity instead. When this value
//       stays unset, config::kFallbackEyeHeight is used for the head position.
inline constexpr std::ptrdiff_t m_vecViewOffset = 0x0;

}  // namespace C_BasePlayerPawn

namespace CGameSceneNode {

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_vecAbsOrigin = 0x0;

}  // namespace CGameSceneNode

namespace CCSPlayerController {

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_hPlayerPawn = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_bPawnIsAlive = 0x0;

// FILL: Dump the current CS2 build with cs2-dumper and copy the named client_dll value here.
inline constexpr std::ptrdiff_t m_iszPlayerName = 0x0;

}  // namespace CCSPlayerController

}  // namespace schemas::client_dll

// ---------------------------------------------------------------------------
// Startup validation.
//
// Values that are genuinely 0x0 in a dump cannot be distinguished from unset
// ones here. None of the fields above is expected to resolve to 0x0; a dump
// that reports 0x0 for one of them needs a manual review of that entry.
// m_vecViewOffset is optional and is reported as a warning, not an error.
// ---------------------------------------------------------------------------

struct OffsetEntry {
    const char* name;
    std::ptrdiff_t value;
};

inline std::vector<OffsetEntry> RequiredOffsets() {
    namespace off = offsets::client_dll;
    namespace sch = schemas::client_dll;
    return {
        {"offsets::client_dll::dwEntityList", off::dwEntityList},
        {"offsets::client_dll::dwLocalPlayerPawn", off::dwLocalPlayerPawn},
        {"offsets::client_dll::dwViewMatrix", off::dwViewMatrix},
        {"schemas::client_dll::C_BaseEntity::m_iHealth", sch::C_BaseEntity::m_iHealth},
        {"schemas::client_dll::C_BaseEntity::m_iTeamNum", sch::C_BaseEntity::m_iTeamNum},
        {"schemas::client_dll::C_BaseEntity::m_pGameSceneNode", sch::C_BaseEntity::m_pGameSceneNode},
        {"schemas::client_dll::CGameSceneNode::m_vecAbsOrigin", sch::CGameSceneNode::m_vecAbsOrigin},
        {"schemas::client_dll::CCSPlayerController::m_hPlayerPawn", sch::CCSPlayerController::m_hPlayerPawn},
        {"schemas::client_dll::CCSPlayerController::m_bPawnIsAlive", sch::CCSPlayerController::m_bPawnIsAlive},
        {"schemas::client_dll::CCSPlayerController::m_iszPlayerName", sch::CCSPlayerController::m_iszPlayerName},
    };
}

inline std::vector<const char*> MissingOffsets() {
    std::vector<const char*> missing;
    for (const OffsetEntry& entry : RequiredOffsets()) {
        if (entry.value == 0) {
            missing.push_back(entry.name);
        }
    }
    return missing;
}

inline bool HasViewOffset() {
    return schemas::client_dll::C_BasePlayerPawn::m_vecViewOffset != 0;
}

}  // namespace cs2_dumper
