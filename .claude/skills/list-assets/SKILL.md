---
name: list-assets
description: List pending or all tracked assets from feature-db
user-invocable: true
argument-hint: [pending|all|feature-name]
---

# List Assets: $ARGUMENTS

アセットの状態をリストアップする。

## モード

- `pending` — 未配置（プレースホルダー状態）のアセットのみ表示
- `all` — 全トラッキング中アセットを表示
- `[feature-name]` — 特定機能に関連するアセットのみ表示

## 手順

1. `python tools/feature-db.py assets` でアセット一覧を取得
2. シーン内の `[PLACEHOLDER]` GameObjectと照合
3. 結果をテーブル形式で出力

## 出力フォーマット

```
=== Asset Status ===

Feature: PlayerCharacter
| Asset | Type | Status | Path |
|-------|------|--------|------|
| player_idle.png | Sprite | PENDING | Assets/Sprites/Player/ |
| walk.anim | Animation | PLACED | Assets/Animations/Player/ |

Summary: 5 pending, 3 placed, 8 total
```
