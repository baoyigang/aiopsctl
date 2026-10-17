#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
if ! command -v "$dotnet_bin" >/dev/null 2>&1 && [ -x /workspace/.tools/dotnet/dotnet ]; then dotnet_bin=/workspace/.tools/dotnet/dotnet; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
cd "$task_root"
(cd frontend && npm ci --no-fund --no-audit && npm run build)
"$dotnet_bin" restore backend/Hengchuan.Api/Hengchuan.Api.csproj --locked-mode
"$dotnet_bin" publish backend/Hengchuan.Api/Hengchuan.Api.csproj --no-restore -c Release -o .local/publish
mkdir -p .local/publish/wwwroot
cp -R frontend/dist/. .local/publish/wwwroot/
printf '%s\n' "已发布到 .local/publish；在该目录执行 dotnet Hengchuan.Api.dll，仅本机127.0.0.1:5080。"
