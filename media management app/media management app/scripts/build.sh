#!/usr/bin/env bash
# Minimal build wrapper for Git Bash on Windows. See docs/BUILD.md.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/media management app.csproj"
CONFIG="${1:-Release}"
PLATFORM="${2:-x64}"

dotnet restore "$PROJ"
dotnet build "$PROJ" -c "$CONFIG" -p:Platform="$PLATFORM" -v minimal --no-restore
