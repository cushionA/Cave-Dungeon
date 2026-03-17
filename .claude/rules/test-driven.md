---
description: Test-driven development workflow rules for Unity game features
---

# TDD開発規約

## ワークフロー
1. テストファイルを先に作成する（Red）
2. テストが全てFailすることを確認する
3. 実装コードを作成する
4. テストが全てPassすることを確認する（Green）
5. `python tools/feature-db.py` で feature-log.db に完了記録を追加する

## テスト命名規則
- フォーマット: `[機能名]_[条件]_[期待結果]`
- 例: `PlayerMovement_WhenSpeedIsZero_ShouldNotMove`
- 例: `HealthSystem_WhenDamageTaken_ShouldReduceHealth`

## テストファイル配置
- Edit Mode: `Tests/EditMode/[機能名]Tests.cs`
- Play Mode: `Tests/PlayMode/[機能名]PlayTests.cs`

## Edit Mode vs Play Mode
- Edit Mode: ロジック単体テスト、データ変換、計算処理
- Play Mode: MonoBehaviour連携、物理演算、コルーチン、シーン動作

## feature-log.db 記録方法 (SQLite)
```bash
# 機能追加
python tools/feature-db.py add "機能名" --tests テストファイル1 テストファイル2 --impl 実装ファイル1

# ステータス更新
python tools/feature-db.py update "機能名" --status complete --test-passed 3 --test-failed 0

# 機能取得
python tools/feature-db.py get "機能名"

# 一覧表示
python tools/feature-db.py list [--status in_progress|complete|failed]

# サマリー
python tools/feature-db.py summary
```

## テスト実行
- Edit Mode: Unity CLI `-runTests -testPlatform EditMode`
- Play Mode: Unity CLI `-runTests -testPlatform PlayMode`
- MCP経由: unity-mcp の run_tests ツール使用
