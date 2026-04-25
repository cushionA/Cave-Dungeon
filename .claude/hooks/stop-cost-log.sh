#!/usr/bin/env bash
# stop-cost-log.sh — Stop hook (P15-T2)
#
# セッション終了時に Claude Code の /cost 相当情報を
# .claude/cost-log.jsonl に追記する。
#
# 注意:
#   - Claude Code 本体の /cost 出力を hook の stdin から取得できる仕様は未確立。
#   - 現状は雛形として、最低限のメタデータ (timestamp, branch, last_commit) のみ記録。
#   - 完全な token/cost データは ccusage 等の外部ツール、または将来の Claude Code 標準
#     hook 拡張に依存する。
#
# Wave 4 Phase 15 P15-T2 で導入。本 PR は雛形配置のみ、
# 実コスト捕捉は Claude Code 本体の hook 仕様確認後に拡張する。

set -uo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0
[ -d ".git" ] || exit 0

LOG_FILE=".claude/cost-log.jsonl"
mkdir -p "$(dirname "$LOG_FILE")"

BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
COMMIT=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)
SESSION_ID="${CLAUDE_SESSION_ID:-unknown}"

# 環境変数で値が渡れば記録 (Claude Code 拡張時用)
INPUT_TOKENS="${CLAUDE_INPUT_TOKENS:-0}"
OUTPUT_TOKENS="${CLAUDE_OUTPUT_TOKENS:-0}"
CACHE_READ="${CLAUDE_CACHE_READ:-0}"
CACHE_CREATION="${CLAUDE_CACHE_CREATION:-0}"
COST_USD="${CLAUDE_COST_USD:-0}"
MODEL="${CLAUDE_MODEL:-unknown}"

cat <<JSON >> "$LOG_FILE"
{"timestamp":"$TIMESTAMP","session_id":"$SESSION_ID","branch":"$BRANCH","commit":"$COMMIT","input_tokens":$INPUT_TOKENS,"output_tokens":$OUTPUT_TOKENS,"cache_read":$CACHE_READ,"cache_creation":$CACHE_CREATION,"estimated_cost_usd":$COST_USD,"model":"$MODEL"}
JSON

exit 0
