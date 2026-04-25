---
description: Unity 特化 hook の用途・有効化方法・注意点 (Wave 3 Phase 22)
---

# Unity-specific hooks (Wave 3 Phase 22)

PostToolUse / pre-build / post-build / pre-release の 4 種類の hook を Unity プロジェクト向けに整備した雛形と運用ガイド。
**本 PR では雛形のみ配置、settings.json への登録はユーザー判断**。

## 4 つの hook

| hook スクリプト | トリガー | 用途 |
|----------------|---------|------|
| `.claude/hooks/unity-console-check.sh` | PostToolUse(Write/Edit/MultiEdit) | unity-mcp `read_console` 連動でエラー検出 (P22-T1) |
| `.claude/hooks/pre-build-validate.sh` | ビルド前 | placeholder 数 / asmdef / Addressable 設定検証 (P22-T2) |
| `.claude/hooks/post-build-test.sh` | ビルド後 | Unity CLI で PlayMode テスト実行 (P22-T3) |
| `.claude/hooks/pre-release-size-check.sh` | リリース前 | APK / IPA / exe サイズ回帰検出 (P22-T4) |

## 現状 (2026-04-25)

すべて**スタブ実装**として配置。実有効化には以下が必要:

1. **`unity-console-check.sh`**: `claude-mcp` CLI 整備後、`read_console` を呼べるようにする
2. **`pre-build-validate.sh` / `post-build-test.sh` / `pre-release-size-check.sh`**: GitHub Actions / ローカル Make スクリプトから呼び出す

## 有効化方法 (将来)

### PostToolUse hook (unity-console-check)

`.claude/settings.json` の PostToolUse に追加:

```jsonc
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit",
        "hooks": [
          { "type": "command", "command": "bash .claude/hooks/post-edit-dispatch.sh" },
          { "type": "command", "command": "bash .claude/hooks/unity-console-check.sh" }
        ]
      }
    ]
  }
}
```

**注意**: 既存の `post-edit-dispatch.sh`（lint hook）と競合しないよう、配列で並列に登録するか、dispatcher 内に統合する。

### pre-build / post-build / pre-release

- ローカル: `Makefile` または `tools/build.sh` から呼び出し
- CI: GitHub Actions の build job の前後ステップで実行

## 環境変数

| 変数 | デフォルト | 用途 |
|------|-----------|------|
| `UNITY_HOOK_PHASE` | warn | unity-console-check の error 昇格制御 |
| `UNITY_PATH` | `C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe` | post-build-test の Unity 実行パス |
| `BUILD_OUTPUT_DIR` | `Builds/` | pre-release-size-check の対象ディレクトリ |
| `SIZE_REGRESSION_THRESHOLD_PCT` | 10 | pre-release-size-check の警告閾値 |

## 注意点

- **Unity CLI のロック競合**: `post-build-test.sh` は Unity Editor が起動中だとロックエラーになる。MCP 経由 `run_tests` を推奨
- **placeholder アセット**: `pre-build-validate.sh` はリリースビルド時のみ placeholder 残存をエラー扱い。dev ビルドでは許容
- **サイズ履歴**: `.claude/build-size-history.tsv` に追記される。`.gitignore` に追加推奨（リリースタグ単位で git にコミットする運用も可）

## P22-T5〜T7 (外部 skill 輸入) について

WAVE_PLAN.md L783-786 の以下は **`docs/FUTURE_TASKS.md` 登録済み**:

- **P22-T5**: Unity App UI Plugin 動作確認 + 導入判定
- **P22-T6**: TheOne Studio skills の C# 9 規約と Architect/ 整合性チェック
- **P22-T7**: 選定した外部 skill を `.claude/skills/` にインポート

ライセンス精査・Architect 整合性確認・ユーザー判断が必要なため、本 PR では含めず将来タスクとして残す。

## 関連

- WAVE_PLAN.md L778-786 (Phase 22 タスク定義)
- 関連 PR (Phase 11 lint hook 既存): #47
- 関連 PR (Phase 13 TDD agent): #54
- `.claude/hooks/post-edit-dispatch.sh` — 既存 dispatcher (PR #47)
- `tools/lint_check.py` — 既存 lint 検査 (PR #47)
