#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
if ! command -v "$dotnet_bin" >/dev/null 2>&1 && [ -x /workspace/.tools/dotnet/dotnet ]; then dotnet_bin=/workspace/.tools/dotnet/dotnet; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
cd "$task_root"
"$dotnet_bin" restore backend/Hengchuan.Tests/Hengchuan.Tests.csproj --locked-mode
"$dotnet_bin" test backend/Hengchuan.Tests/Hengchuan.Tests.csproj --no-restore --logger 'trx;LogFileName=backend-tests.trx' --results-directory tests/results
(cd frontend && npm ci --no-fund --no-audit && npm run typecheck && npm run build)
