#!/usr/bin/env bash
# unity-console-check.sh — PostToolUse hook (P22-T1)
#
# Edit / Write / MultiEdit で .cs が変更された後、unity-mcp 経由で
# Unity Editor のコンソールエラーを確認する。
#
# 前提: Unity Editor が起動中、unity-mcp が接続済み。
# 起動していない場合は早期 exit (warning なし)。
#
# 環境変数:
#   UNITY_HOOK_PHASE=warn|error  (デフォルト: warn — error 検出でも exit 0)
#
# 設計メモ: Wave 3 Phase 22 の P22-T1 で導入。
# 本 hook は unity-mcp の `read_console` ツールを呼ぶ想定だが、
# CLI 経由の MCP 呼び出しは未確立 (2026-04-25)。
# 本 PR ではスタブ実装に留め、実有効化は別 PR で MCP CLI 整備後とする。

set -uo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0
[ -d ".git" ] || exit 0

# .cs 以外のファイルなら何もしない
FILE=$(cat /dev/stdin 2>/dev/null | python -c "import sys, json; d=json.loads(sys.stdin.read()); print(d.get('tool_input', {}).get('file_path', ''))" 2>/dev/null || echo "")
case "$FILE" in
  *.cs) ;;
  *) exit 0 ;;
esac

PHASE="${UNITY_HOOK_PHASE:-warn}"

# unity-mcp の検出 (将来的に MCP CLI が確立したらここで read_console を呼ぶ)
if ! command -v claude-mcp >/dev/null 2>&1; then
  # MCP CLI 未整備時はスキップ (warning なし)
  exit 0
fi

# TODO: claude-mcp call unity-mcp read_console --filter Error
# 現状は実装スタブ。将来の MCP CLI 整備時に有効化する。
echo "[unity-hook] (stub) unity-mcp read_console 連動は MCP CLI 整備後に有効化"
exit 0
