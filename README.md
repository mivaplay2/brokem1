# cs2_range_esp

External, read-only ESP overlay for a local CS2 bot range. The companion runs as
a separate x64 process, reads client state with `ReadProcessMemory`, and draws
boxes, health bars, names and distances on a click-through top-most window
placed over the game's client area.

## Scope

* Windows x64 only.
* Local listen server with scripted bots (`map de_dust2` / `bot_add`).
* Separate native process; no DLL, no injection, no hooks.
* Read-only process access: the handle is opened with
  `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`. There is no `PROCESS_VM_WRITE`
  right and no `WriteProcessMemory` call anywhere in the project.
* No network client, no server plugin, no driver, no persistence.
* **Visibility filtering is not implemented.** There is no occlusion or trace
  data path in this companion, so `config::kVisibleOnly` is `false` and no
  visibility claim is made. Everything drawn is a living, in-range pawn,
  occluded or not.

## Layout

| File | Contents |
|---|---|
| `src/config.hpp` | `constexpr` configuration: keys, pacing, filters, colors, geometry |
| `src/offsets.hpp` | Build-specific `cs2-dumper` values plus the startup validator |
| `src/process.hpp/.cpp` | Attach, module resolution, typed reads, window lookup |
| `src/sdk.hpp` | Entity-system traversal and the player snapshot |
| `src/math.hpp/.cpp` | `Vec2`/`Vec3`, view matrix, world-to-screen, finiteness checks |
| `src/renderer.hpp/.cpp` | Layered color-keyed GDI overlay |
| `src/main.cpp` | Attach / resolve / acquire / render loop |

## Offsets

Every value in `src/offsets.hpp` ships unset (`0x0`) and is marked with a
`// FILL:` recipe. The program prints the unset entries and exits with code `2`
rather than reading from `client.dll + 0`.

Fill them once per CS2 update:

```powershell
git clone https://github.com/a2x/cs2-dumper
cd cs2-dumper
cargo build --release
# Run the dumper while CS2 is running, then copy:
#   output/offsets.hpp     -> cs2_dumper::offsets::client_dll
#   output/client_dll.hpp  -> cs2_dumper::schemas::client_dll
```

Required:

* `offsets::client_dll::dwEntityList`
* `offsets::client_dll::dwLocalPlayerPawn`
* `offsets::client_dll::dwViewMatrix`
* `schemas::client_dll::C_BaseEntity::m_iHealth`
* `schemas::client_dll::C_BaseEntity::m_iTeamNum`
* `schemas::client_dll::C_BaseEntity::m_pGameSceneNode`
* `schemas::client_dll::CGameSceneNode::m_vecAbsOrigin`
* `schemas::client_dll::CCSPlayerController::m_hPlayerPawn`
* `schemas::client_dll::CCSPlayerController::m_bPawnIsAlive`
* `schemas::client_dll::CCSPlayerController::m_iszPlayerName`

Optional:

* `schemas::client_dll::C_BasePlayerPawn::m_vecViewOffset` — when unset, head
  height falls back to `config::kFallbackEyeHeight` and crouch state is ignored.
  Confirm which class the current dump places this field on.

The entity-system strides in `src/sdk.hpp`
(`kEntityListFirstChunk`, `kChunkPointerStride`, `kEntityIdentityStride`,
`kEntitiesPerChunk`) are structural Source 2 layout rather than per-build
offsets, but they also carry a `// FILL:` note: confirm them against the current
`CConcreteEntityList` layout before trusting them.

## Entity pipeline

```
client.dll + dwLocalPlayerPawn      -> local pawn
client.dll + dwEntityList           -> CConcreteEntityList
  chunk[index / 512] + 0x78 * (index % 512)
                                    -> CCSPlayerController
  m_bPawnIsAlive                    -> alive gate
  m_hPlayerPawn & 0x7FFF            -> pawn index -> pawn
  m_iHealth                         -> (0, 1000] gate
  m_iTeamNum                        -> T/CT gate, team filter
  m_pGameSceneNode -> m_vecAbsOrigin-> feet
  m_vecViewOffset.z + kHeadRoom     -> head
client.dll + dwViewMatrix           -> world-to-screen
```

The snapshot is rebuilt from scratch every frame, so a freed entity cannot
survive into the next frame. Null pointers, implausible pointers, failed reads,
out-of-range health, non-finite vectors, points behind the camera and
degenerate or fully off-screen boxes are all dropped before rendering.

## Build

```powershell
cmake -S . -B build -A x64
cmake --build build --config Release
```

Output: `build\Release\cs2_range_esp.exe`.

Requirements: Visual Studio 2022 (MSVC v143) or clang-cl with the Windows SDK,
CMake 3.20+.

## Run

```powershell
.\build\Release\cs2_range_esp.exe
```

* Run the console from an **elevated** prompt: opening a handle to `cs2.exe`
  needs the same or higher integrity level than the game.
* CS2 must run **windowed** or **borderless fullscreen**. A top-most layered
  window cannot composite over exclusive fullscreen.
* The overlay tracks the game's client rect every frame, so resolution and
  window moves are handled live.
* `VK_END` exits, `VK_INSERT` toggles drawing, `Ctrl+C` shuts down cleanly.
* The loop detaches and retries every `config::kReattachDelayMs` when the client
  exits or restarts, and re-resolves `client.dll` on each new attach.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Clean shutdown |
| `2` | One or more required offsets unset in `src/offsets.hpp` |
| `3` | Overlay window creation failed |

## Verification

* Cross-compiled clean with `x86_64-w64-mingw32-g++ -std=c++20 -Wall -Wextra -O2`
  (PE32+ x86-64 console binary, no warnings).
* MSVC / `cmake -A x64` configure and build: didn't check.
* Runtime attach, offset resolution, entity enumeration, projection accuracy and
  on-screen result: didn't check — no Windows host and no CS2 client here, and
  every offset in `src/offsets.hpp` is still unset.
* `grep` over `src/` confirms no `WriteProcessMemory`, `PROCESS_VM_WRITE` or
  `PROCESS_VM_OPERATION` use.
