#!/usr/bin/env bash
# pre-build-validate.sh — pre-build hook (P22-T2)
#
# Unity ビルド前に validate:project 相当を実行する。
# - asmdef 整合性
# - Addressable 設定
# - Editor scripts のコンパイルエラー
# - placeholder アセット数 (リリースビルドではエラー)
#
# 起動方法:
#   bash .claude/hooks/pre-build-validate.sh [--release | --dev]
#
# Wave 3 Phase 22 P22-T2 で導入。
# 本 PR ではスタブ実装。実有効化は build CI 整備後。

set -uo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0

MODE="${1:-dev}"

echo "=== pre-build validation ($MODE) ==="

# 1. placeholder アセット数チェック
PLACEHOLDER_COUNT=$(grep -r "\[PLACEHOLDER\]" Assets/ --include="*.prefab" 2>/dev/null | wc -l)
echo "Placeholder count: $PLACEHOLDER_COUNT"

if [ "$MODE" = "release" ] && [ "$PLACEHOLDER_COUNT" -gt 0 ]; then
  echo "ERROR: Release build with $PLACEHOLDER_COUNT placeholder assets remaining"
  exit 1
fi

# 2. asmdef 整合性チェック (TODO: Unity CLI の validate:project 相当)
ASMDEF_COUNT=$(find Assets/ -name "*.asmdef" 2>/dev/null | wc -l)
echo "asmdef count: $ASMDEF_COUNT"

# 3. (TODO) Addressable 設定検証
# Unity CLI で AddressableAssetSettings.BuildPlayerContent() に類する検証コマンド呼出

echo "=== validation passed ==="
exit 0
