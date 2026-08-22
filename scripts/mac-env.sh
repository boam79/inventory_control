#!/usr/bin/env bash
# User-local .NET 10 (no sudo). Source from other Mac scripts or your shell.
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
