#!/usr/bin/env bash
# Compatibility wrapper. Prefer scripts/dev.sh.
exec "$(cd "$(dirname "$0")" && pwd)/dev.sh"
