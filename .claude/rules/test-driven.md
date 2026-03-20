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

## 結合テスト（Cross-System Testing）

単体テストに加え、以下の観点で結合テストを作成する。
配置先: `Tests/EditMode/Integration_{テスト名}Tests.cs`

### 必須観点

1. **既存ロジック呼び出し検証**: 新コードが既存ユーティリティを正しく経由しているか
   - 例: ProjectileHitProcessorがHpArmorLogic.ApplyDamageを経由し、HPクランプ・アーマー処理が適用されるか
   - **NG**: 「HPが減った」だけの検証 → **OK**: 「HP >= 0 にクランプされる」「アーマーブレイクボーナスが乗る」

2. **状態シーケンス検証**: 同じ操作を複数回行った時に状態が壊れないか
   - 例: ActionExecutorでA→B→Aと実行した後、OnCompletedが1回だけ発火するか
   - 例: HudのTweenBarを連続呼び出しした時に前回のハンドルが正しくキャンセルされるか

3. **境界値・不変条件検証**: 単体テストの「正常系OK」だけでなく「壊れない保証」を追加
   - HP < 0 にならない、インデックスが範囲外にならない、リソースがリークしない
   - イベント購読数が増え続けない（subscribe/unsubscribeの対称性）

### テスト設計チェックリスト（機能実装時に確認）

- [ ] この機能は他システムのメソッドを呼んでいるか？ → 呼び先の効果まで検証するテストを書く
- [ ] この機能はイベントを購読/発行しているか？ → 購読解除・多重購読のテストを書く
- [ ] この機能は状態を持つか？ → 連続操作・リセット後の再操作テストを書く
- [ ] この機能はリソース（ハンドル・マテリアル等）を確保するか？ → 解放テストを書く

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
