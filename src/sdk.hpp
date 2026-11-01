#pragma once

#include <cstdint>
#include <optional>
#include <vector>

#include "config.hpp"
#include "math.hpp"
#include "offsets.hpp"
#include "process.hpp"

// Target-state acquisition. Nothing in this header draws; it only reads.
namespace sdk {

namespace off = cs2_dumper::offsets::client_dll;
namespace sch = cs2_dumper::schemas::client_dll;

// --- Source 2 entity-system layout ---------------------------------------
//
// These are structural constants of CGameEntitySystem / CConcreteEntityList,
// not per-build offsets, but they do move between engine revisions.
//
// FILL: Confirm against the current CConcreteEntityList layout (cs2-dumper or
//       source2gen output) before trusting the values below.
inline constexpr std::ptrdiff_t kEntityListFirstChunk = 0x10;  // First chunk pointer in CConcreteEntityList.
inline constexpr std::ptrdiff_t kChunkPointerStride = 0x8;     // Pointer size per chunk slot.
inline constexpr std::ptrdiff_t kEntityIdentityStride = 0x78;  // sizeof(CEntityIdentity) == 120.
inline constexpr int kEntitiesPerChunk = 512;
inline constexpr std::uint32_t kEntityHandleIndexMask = 0x7FFF;
inline constexpr std::uint32_t kInvalidEntityHandle = 0xFFFFFFFFU;

// --- Teams ----------------------------------------------------------------
inline constexpr int kTeamUnassigned = 0;
inline constexpr int kTeamSpectator = 1;
inline constexpr int kTeamT = 2;
inline constexpr int kTeamCT = 3;

inline constexpr std::size_t kNameCapacity = 128;

struct Player {
    std::uintptr_t controller{0};
    std::uintptr_t pawn{0};
    int entity_index{0};
    int health{0};
    int team{kTeamUnassigned};
    float distance{0.0F};  // World units from the local pawn.
    math::Vec3 origin{};   // Feet.
    math::Vec3 head{};     // Eye position plus config::kHeadRoom.
    char name[kNameCapacity]{};
};

// Resolves one entity index through the chunked entity list.
// Returns 0 for every failure: unloaded chunk, failed read, implausible pointer.
inline std::uintptr_t EntityFromIndex(const Process& proc, std::uintptr_t entity_list, int index) {
    if (entity_list == 0 || index < 0) {
        return 0;
    }

    const std::ptrdiff_t chunk_slot = static_cast<std::ptrdiff_t>(index / kEntitiesPerChunk);
    const std::uintptr_t chunk =
        proc.ReadPointer(entity_list + kEntityListFirstChunk + (kChunkPointerStride * chunk_slot));
    if (chunk == 0) {
        return 0;
    }

    const std::ptrdiff_t slot = static_cast<std::ptrdiff_t>(index % kEntitiesPerChunk);
    return proc.ReadPointer(chunk + (kEntityIdentityStride * slot));
}

inline std::uintptr_t EntityListBase(const Process& proc, std::uintptr_t client_base) {
    if (client_base == 0) {
        return 0;
    }
    return proc.ReadPointer(client_base + off::dwEntityList);
}

inline std::uintptr_t LocalPawn(const Process& proc, std::uintptr_t client_base) {
    if (client_base == 0) {
        return 0;
    }
    return proc.ReadPointer(client_base + off::dwLocalPlayerPawn);
}

inline std::optional<math::ViewMatrix> ViewMatrix(const Process& proc, std::uintptr_t client_base) {
    if (client_base == 0) {
        return std::nullopt;
    }
    const std::optional<math::ViewMatrix> matrix = proc.Read<math::ViewMatrix>(client_base + off::dwViewMatrix);
    if (!matrix || !math::IsFinite(*matrix)) {
        return std::nullopt;
    }
    return matrix;
}

// Reads the absolute origin through pawn -> m_pGameSceneNode -> m_vecAbsOrigin.
inline std::optional<math::Vec3> PawnOrigin(const Process& proc, std::uintptr_t pawn) {
    const std::uintptr_t scene_node = proc.ReadPointer(pawn + sch::C_BaseEntity::m_pGameSceneNode);
    if (scene_node == 0) {
        return std::nullopt;
    }

    const std::optional<math::Vec3> origin = proc.Read<math::Vec3>(scene_node + sch::CGameSceneNode::m_vecAbsOrigin);
    if (!origin || !math::IsFinite(*origin)) {
        return std::nullopt;
    }
    return origin;
}

// Eye height above the pawn origin. Falls back to the configured constant when
// m_vecViewOffset is unresolved or unreadable, so crouch state is only honoured
// when that field is actually filled in.
inline float EyeHeight(const Process& proc, std::uintptr_t pawn) {
    if (!cs2_dumper::HasViewOffset()) {
        return config::kFallbackEyeHeight;
    }

    const std::optional<math::Vec3> view_offset =
        proc.Read<math::Vec3>(pawn + sch::C_BasePlayerPawn::m_vecViewOffset);
    if (!view_offset || !math::IsFinite(*view_offset)) {
        return config::kFallbackEyeHeight;
    }
    if (view_offset->z <= 1.0F || view_offset->z > 128.0F) {
        return config::kFallbackEyeHeight;
    }
    return view_offset->z;
}

inline int PawnTeam(const Process& proc, std::uintptr_t pawn) {
    const std::optional<std::uint8_t> team = proc.Read<std::uint8_t>(pawn + sch::C_BaseEntity::m_iTeamNum);
    if (!team) {
        return kTeamUnassigned;
    }
    return static_cast<int>(*team);
}

// Builds a snapshot of the valid, living, non-local pawns in the entity list.
// Every entry is re-read from scratch each call; nothing is cached across
// frames, so stale entities cannot survive into the next snapshot.
inline void CollectPlayers(const Process& proc,
                           std::uintptr_t client_base,
                           std::uintptr_t local_pawn,
                           int local_team,
                           const math::Vec3& local_origin,
                           std::vector<Player>& out) {
    out.clear();

    const std::uintptr_t entity_list = EntityListBase(proc, client_base);
    if (entity_list == 0) {
        return;
    }

    for (int index = 1; index <= config::kMaxEntities; ++index) {
        const std::uintptr_t controller = EntityFromIndex(proc, entity_list, index);
        if (controller == 0) {
            continue;
        }

        const std::optional<bool> pawn_is_alive =
            proc.Read<bool>(controller + sch::CCSPlayerController::m_bPawnIsAlive);
        if (!pawn_is_alive || !*pawn_is_alive) {
            continue;
        }

        const std::optional<std::uint32_t> pawn_handle =
            proc.Read<std::uint32_t>(controller + sch::CCSPlayerController::m_hPlayerPawn);
        if (!pawn_handle || *pawn_handle == 0 || *pawn_handle == kInvalidEntityHandle) {
            continue;
        }

        const int pawn_index = static_cast<int>(*pawn_handle & kEntityHandleIndexMask);
        const std::uintptr_t pawn = EntityFromIndex(proc, entity_list, pawn_index);
        if (pawn == 0 || pawn == local_pawn) {
            continue;
        }

        const std::optional<std::int32_t> health = proc.Read<std::int32_t>(pawn + sch::C_BaseEntity::m_iHealth);
        if (!health || *health <= 0 || *health > config::kHealthSanityLimit) {
            continue;
        }

        const int team = PawnTeam(proc, pawn);
        if (team != kTeamT && team != kTeamCT) {
            continue;
        }
        if (config::kTeamCheck && local_team != kTeamUnassigned && team == local_team) {
            continue;
        }

        const std::optional<math::Vec3> origin = PawnOrigin(proc, pawn);
        if (!origin) {
            continue;
        }

        Player player{};
        player.controller = controller;
        player.pawn = pawn;
        player.entity_index = pawn_index;
        player.health = static_cast<int>(*health);
        player.team = team;
        player.origin = *origin;
        player.head = *origin;
        player.head.z += EyeHeight(proc, pawn) + config::kHeadRoom;
        player.distance = math::Distance(local_origin, *origin);

        if (!math::IsFinite(player.head) || !math::IsFinite(player.distance)) {
            continue;
        }
        if (config::kMaxDrawDistance > 0.0F && player.distance > config::kMaxDrawDistance) {
            continue;
        }

        if (!proc.ReadCString(controller + sch::CCSPlayerController::m_iszPlayerName, player.name, kNameCapacity)) {
            player.name[0] = '\0';
        }

        out.push_back(player);
    }
}

}  // namespace sdk
