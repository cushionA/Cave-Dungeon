#!/usr/bin/env bash
# post-build-test.sh — post-build hook (P22-T3)
#
# Unity ビルド後に PlayMode テストを実行する。
# CI 環境向け、ローカルでは Unity Editor が開いていれば MCP 経由を推奨。
#
# Wave 3 Phase 22 P22-T3 で導入。本 PR ではスタブ実装。

set -uo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0

UNITY_PATH="${UNITY_PATH:-C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe}"
RESULTS_PATH="TestResults/post-build-playmode.xml"

echo "=== post-build PlayMode test ==="

if [ ! -x "$UNITY_PATH" ]; then
  echo "WARN: Unity not found at $UNITY_PATH (set UNITY_PATH env var)"
  exit 0
fi

# Unity を batch mode で起動して PlayMode テスト実行
# (TODO) Unity が既に起動中ならファイルロックで失敗する。MCP 経由を推奨
"$UNITY_PATH" \
  -batchmode \
  -projectPath . \
  -runTests \
  -testPlatform PlayMode \
  -testResults "$RESULTS_PATH" \
  -logFile - \
  || {
    echo "ERROR: PlayMode test failed"
    exit 1
  }

echo "=== PlayMode tests passed: $RESULTS_PATH ==="
exit 0
