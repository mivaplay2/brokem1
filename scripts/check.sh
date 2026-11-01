#!/usr/bin/env bash
# Runs both test suites. GODOT may point at a Godot 4.5 .NET binary; when it is
# unset only the simulation tests run, since those need no engine at all.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== simulation tests =="
( cd tests/SimTests && dotnet run -c Release )

if [[ -n "${GODOT:-}" ]]; then
  echo
  echo "== headless scene test =="
  dotnet build MyAutumnCar.csproj -v q -nologo
  "$GODOT" --headless --path . -- --smoke
else
  echo
  echo "GODOT is not set, skipping the headless scene test."
  echo "  GODOT=/path/to/Godot_v4.5-stable_mono_linux.x86_64 scripts/check.sh"
fi
