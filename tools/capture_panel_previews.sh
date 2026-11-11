#!/usr/bin/env bash
# Render and check the operator panel previews in batch mode.
#
#   tools/capture_panel_previews.sh                 # update docs/images (the README images)
#   tools/capture_panel_previews.sh <folder>        # every sample state, plus Quest 3 eye-buffer densities
#                                                   # (a relative <folder> is relative to where you run the script)
#
# Exit code: 0 = OK, 1 = problems found, 2 = no GPU.
# The Unity editor sometimes aborts while shutting down (exit code 134) after the preview has finished,
# so the result is read from the log line "preview finished with exit code N", not from Unity's exit code.
# Only one Unity instance can open the project at a time: close the editor first.
set -euo pipefail
unset CDPATH # cd must not print the folder or pick one from CDPATH

# Resolve the output folder before moving to the project folder
OUT=""
if [[ $# -gt 0 ]]; then
  mkdir -p "$1"
  OUT="$(cd "$1" && pwd)"
fi

cd "$(dirname "$0")/.."

find_unity() {
  if [[ -n "${UNITY:-}" ]]; then echo "$UNITY"; return; fi
  local c="/Applications/Unity/Hub/Editor/6000.3.7f1/Unity.app/Contents/MacOS/Unity"
  if [[ -x "$c" ]]; then echo "$c"; return; fi
  echo "Unity 6000.3.7f1 not found. Set UNITY=/path/to/Unity." >&2
  exit 1
}

UNITY_BIN="$(find_unity)"
LOG="$(mktemp "${TMPDIR:-/tmp}/qse_panel_preview.XXXXXX")"
args=(-batchmode -projectPath . -buildTarget Android
      -executeMethod QuestSaturation.Editor.OperatorPanelPreview.RunBatch -logFile "$LOG")
if [[ -n "$OUT" ]]; then
  args+=(-previewOut "$OUT")
fi

# The preview draws with the GPU, so -nographics must not be used.
"$UNITY_BIN" "${args[@]}" || true

sed -n 's/.*\[QSE-TOOL\] preview: //p' "$LOG"
code="$(sed -n 's/.*\[QSE-TOOL\] preview finished with exit code \([0-9][0-9]*\).*/\1/p' "$LOG" | tail -n 1)"
if [[ -z "$code" ]]; then
  echo "The preview did not finish. See $LOG" >&2
  exit 1
fi
echo "Log: $LOG"
exit "$code"
