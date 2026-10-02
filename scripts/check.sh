#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -x /workspace/.dotnet/dotnet ]]; then source scripts/env.sh; fi
dotnet restore AiOps.slnx --locked-mode
dotnet build AiOps.slnx --no-restore
dotnet test backend/AiOps.Tests --no-build
DOTNET_EXE="$(command -v dotnet)" python3 tests/integration.py
(cd frontend && npm ci --ignore-scripts && npm run typecheck && npm run build && npm test && npm audit)
# UI interaction tests require a separate local instance with a disposable DB.
