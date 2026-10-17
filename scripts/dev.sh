#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
if ! command -v "$dotnet_bin" >/dev/null 2>&1 && [ -x /workspace/.tools/dotnet/dotnet ]; then dotnet_bin=/workspace/.tools/dotnet/dotnet; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
cd "$task_root"
"$dotnet_bin" run --project backend/Hengchuan.Api/Hengchuan.Api.csproj --no-launch-profile &
api_pid=$!
(cd frontend && npm run dev) &
ui_pid=$!
cleanup() { kill "$api_pid" "$ui_pid" 2>/dev/null || true; }
trap cleanup EXIT INT TERM
wait "$api_pid" "$ui_pid"
