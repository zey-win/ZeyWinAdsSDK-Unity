#!/usr/bin/env bash
# Usage: check_app_size.sh <path-to-apk-or-aab>
# Env overrides: WARN_MB (default 30), FAIL_MB (default 100)
set -euo pipefail

FILE="${1:-}"
WARN_MB="${WARN_MB:-30}"
FAIL_MB="${FAIL_MB:-100}"

if [ -z "$FILE" ]; then
  echo "::error::check_app_size.sh: missing required <path-to-apk-or-aab> argument"
  exit 1
fi

if [ ! -f "$FILE" ]; then
  echo "::error::check_app_size.sh: file not found: $FILE"
  exit 1
fi

SIZE_BYTES=$(stat -c%s "$FILE")
SIZE_MB=$(( SIZE_BYTES / 1024 / 1024 ))

echo "App artifact size: ${SIZE_MB}MB (warn >${WARN_MB}MB, fail >${FAIL_MB}MB) — $FILE"

if [ "$SIZE_MB" -gt "$FAIL_MB" ]; then
  echo "::error::App size ${SIZE_MB}MB exceeds the ${FAIL_MB}MB hard limit — build rejected"
  exit 1
elif [ "$SIZE_MB" -gt "$WARN_MB" ]; then
  echo "::warning::App size ${SIZE_MB}MB exceeds the ${WARN_MB}MB optimal target"
fi

exit 0
