#!/usr/bin/env bash
# Copy the session CSV logs from a connected Quest to this computer.
#
#   tools/pull_sessions.sh [destination folder]
#
# Default destination: ./quest_sessions/<yyyymmdd-hhmmss>/
# Pull BEFORE uninstalling the app: uninstalling deletes the logs.
# (adb install -r keeps them.)
set -euo pipefail

PKG="com.ryomtzk.questsaturationexperiment"
REMOTE="/sdcard/Android/data/${PKG}/files/sessions"
DEST="${1:-./quest_sessions/$(date +%Y%m%d-%H%M%S)}"

find_adb() {
  if [[ -n "${ADB:-}" ]]; then echo "$ADB"; return; fi
  if command -v adb >/dev/null 2>&1; then command -v adb; return; fi
  for c in "$HOME/Library/Android/sdk/platform-tools/adb" \
           /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb; do
    if [[ -x "$c" ]]; then echo "$c"; return; fi
  done
  echo "adb not found. Install Android platform-tools or set ADB=/path/to/adb." >&2
  exit 1
}

ADB_BIN="$(find_adb)"

state="$("$ADB_BIN" get-state 2>/dev/null || true)"
if [[ "$state" != "device" ]]; then
  echo "No headset found (adb state: ${state:-none}). Connect the Quest and allow USB debugging." >&2
  exit 1
fi

if ! "$ADB_BIN" shell "ls '${REMOTE}'" >/dev/null 2>&1; then
  echo "No logs on the headset yet (${REMOTE} does not exist)." >&2
  exit 1
fi

mkdir -p "$DEST"
"$ADB_BIN" pull "${REMOTE}/." "$DEST"
count="$(find "$DEST" -name 'session_*.csv' | wc -l | tr -d ' ')"
echo "Copied ${count} session file(s) to ${DEST}"
