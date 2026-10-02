#!/usr/bin/env bash
# Source this file in this managed cloud workspace. Never repurpose HOME.
export PATH="/workspace/.dotnet:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PLAYWRIGHT_BROWSERS_PATH=/workspace/.playwright
export npm_config_cache=/workspace/.npm-cache
