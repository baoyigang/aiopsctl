#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -x /workspace/.dotnet/dotnet ]]; then source scripts/env.sh; fi
(cd frontend && npm ci --ignore-scripts && npm run build)
dotnet restore backend/AiOps.Api --locked-mode
dotnet run --project backend/AiOps.Api --no-launch-profile
