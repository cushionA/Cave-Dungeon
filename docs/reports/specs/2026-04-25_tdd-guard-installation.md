# TDD-Guard 導入手順書 (2026-04-25 スペック)

**目的**: Wave 3 Phase 6 の TDD-Guard を SisterGame に本格導入するためのステップバイステップガイド。
**ユーザー意思**: 「使いたい」（2026-04-25 確認済）
**前提**: Wave 3 PR #54 で `.claude/skills/tdd-guard-setup/SKILL.md` が既に配置済

## 本ドキュメントの位置づけ

- 公式リポジトリ ([nizos/tdd-guard](https://github.com/nizos/tdd-guard)) を実際に **clone する前に**、SisterGame との統合方式を確定させる仕様書
- ユーザーが手動で実行する手順 + Claude が補助する役割を分離
- 1 週間試用後の判定基準（FUTURE_TASKS の Phase 6 全体エントリで管理）

## ステップ 0: 前提確認

```bash
# Node.js / npm が入っているか
node --version
npm --version

# .claude/skills/tdd-guard-setup/SKILL.md が存在するか
test -f .claude/skills/tdd-guard-setup/SKILL.md && echo "OK" || echo "PR #54 が未マージ"

# 現在の hooks 設定確認
cat .claude/settings.json | python -m json.tool
```

## ステップ 1: tdd-guard を vendoring（推奨）or submodule

**判断**: SisterGame は Unity プロジェクトで Node プロジェクトではないため、vendoring（手動 copy）よりも `tools/tdd-guard/` への submodule が望ましい。

```bash
# プロジェクトルートで
git submodule add https://github.com/nizos/tdd-guard tools/tdd-guard
git submodule update --init --recursive

# Node 依存をインストール
cd tools/tdd-guard && npm install && cd ../..
```

submodule 化の利点:
- 公式更新を `git submodule update --remote tools/tdd-guard` で取り込める
- リポジトリサイズが膨らまない（commit history 別管理）
- 不採用判断時は `git submodule deinit + rm` で容易に外せる

## ステップ 2: PreToolUse hook ラッパー実装

`.claude/hooks/tdd-guard.sh` を新規作成:

```bash
#!/usr/bin/env bash
# tdd-guard PreToolUse hook ラッパー
set -uo pipefail
cd "$(git rev-parse --show-toplevel 2>/dev/null)" || exit 0

# spike モード（緊急退避）チェック
SPIKE_FLAG=".claude/.tdd-spike-mode"
if [ -f "$SPIKE_FLAG" ]; then
  # 24h 経過していれば自動失効
  MTIME=$(stat -c %Y "$SPIKE_FLAG" 2>/dev/null || stat -f %m "$SPIKE_FLAG" 2>/dev/null)
  AGE_H=$(( ($(date +%s) - MTIME) / 3600 ))
  if [ $AGE_H -ge 24 ]; then
    rm -f "$SPIKE_FLAG"
    echo "[tdd-guard] spike mode expired (>24h), re-enabled" >&2
  else
    # spike モード有効 → チェックスキップ
    exit 0
  fi
fi

# tdd-guard 本体呼び出し
exec node tools/tdd-guard/dist/index.js "$@"
```

```bash
chmod +x .claude/hooks/tdd-guard.sh
```

## ステップ 3: settings.json に PreToolUse hook 追加

`.claude/settings.json` の `hooks` セクションに以下を**追加**（既存の PostToolUse / Stop / SessionStart は残す）:

```jsonc
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit|TodoWrite",
        "hooks": [
          { "type": "command", "command": "bash .claude/hooks/tdd-guard.sh" }
        ]
      }
    ],
    // ... 既存の PostToolUse / Stop / SessionStart はそのまま
  }
}
```

## ステップ 4: `.gitignore` に spike フラグを追加

```bash
echo ".claude/.tdd-spike-mode" >> .gitignore
```

誤コミットを防止。

## ステップ 5: 動作確認

```bash
# 失敗テストなしで実装ファイルを作る試み → ブロックされるはず
echo "public class Foo {}" > Assets/MyAsset/Runtime/_test_foo.cs
# Claude Code セッション内で Edit/Write を試行 → tdd-guard がブロック

# spike モード（緊急退避）動作確認
touch .claude/.tdd-spike-mode
# → 次の Edit/Write はスキップされる
```

## ステップ 6: 1 週間試用

- 試用期間: 導入日 + 7 日
- 観察項目:
  - 誤検知率（規約準拠の Write/Edit がブロックされた回数）
  - 真検知率（テストなし実装をブロックした回数）
  - 開発速度への影響（subjective、ユーザー記録）
- 観察ログ: `.claude/rules/tdd-guard-observation.md`（雛形は別途配置）

## ステップ 7: 本採用 / 撤退判定

| 判定 | 条件 | アクション |
|------|------|----------|
| 本採用 | 誤検知 < 真検知、ユーザー納得 | 維持、`docs/compound/` に効果測定エントリ |
| 撤退 | 誤検知過多 or ワークフロー阻害 | submodule 解除 + settings.json 復元 |

## TDD 3 サブエージェントとの連動

PR #54 で導入した [tdd-test-writer / tdd-implementer / tdd-refactorer](.claude/agents/) と組み合わせると以下のフロー:

1. ユーザー or Claude が `/agent tdd-test-writer <feature>` 起動
2. test-writer が失敗テストを書く（implementation paths への Edit は AGENT.md の Forbidden で禁止）
3. tdd-guard hook は Edit/Write を傍受しつつ、Tests/ 配下の Edit はパスする想定（matcher 詳細は試用で調整）
4. `/agent tdd-implementer` で実装フェーズ → 失敗テストがあるので tdd-guard は Edit を許可
5. `/agent tdd-refactorer` で refactor + commit

## ブロッカー

- **macOS / Windows 互換性**: `nizos/tdd-guard` の対応 OS を試用初日に確認
- **C# / Unity Edit Mode 適合**: Jest/Vitest 想定設計を Unity Test Framework と接続できるか試用で検証
- **spike フラグの永続化問題**: 24h 自動失効で対応済（Stop hook で削除も検討）

## 関連

- `.claude/skills/tdd-guard-setup/SKILL.md` (PR #54)
- `.claude/agents/tdd-{test-writer,implementer,refactorer}/AGENT.md` (PR #54)
- `docs/FUTURE_TASKS.md` の「TDD-Guard Spike モード解除忘れ対策」エントリ
- WAVE_PLAN.md L749-760 (Phase 6 P6-T1〜T8)
- 公式: [nizos/tdd-guard](https://github.com/nizos/tdd-guard)
