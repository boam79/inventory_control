#!/usr/bin/env bash
# Restore, build, and test on macOS or Linux. WPF UI still requires Windows.
set -euo pipefail
Root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$Root"

if [[ "$(uname -s)" == "Darwin" ]]; then
  # shellcheck disable=SC1091
  source "$Root/scripts/mac-env.sh"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found. Install .NET 10 SDK, then retry."
  echo "  macOS: curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --install-dir \"\$HOME/.dotnet\""
  echo "         source \"$Root/scripts/mac-env.sh\""
  echo "  Windows: https://aka.ms/dotnet/download  then  powershell -File scripts/dev.ps1"
  exit 1
fi

echo "SDK: $(dotnet --version)  ($(uname -ms))"
dotnet restore --nologo
dotnet build --nologo --no-restore
dotnet test Inventory.Tests/Inventory.Tests.csproj --nologo --no-build
echo "Build/test OK on this OS. WPF app and Setup.exe still require Windows."
