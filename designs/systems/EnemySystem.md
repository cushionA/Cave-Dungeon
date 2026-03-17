# System: EnemySystem
Section: 1 — MVP

## 責務
雑魚敵の配置、スポーン、AI行動、ドロップ管理。AICore基盤の上で敵固有のロジックを実装。

## 依存
- 入力: AICore（AIBrain基盤）、DataContainer、DamageSystem
- 出力: 敵の行動実行、撃破時のドロップ、経験値/通貨配布

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| EnemyController | 敵固有AI補助（巡回ルート等） | Yes |
| EnemySpawner | スポーンポイント管理・リスポーン | Yes |
| DropTable | 撃破時のドロップアイテム定義 | No (ScriptableObject) |
| LootDropper | ドロップアイテムのワールド生成 | Yes |

## スポーンシステム
```
EnemySpawner (シーン配置)
    ├── spawnPoints[]       : スポーン位置
    ├── enemyPrefab         : 敵プレハブ参照
    ├── maxActive           : 同時存在数上限
    ├── respawnDelay        : リスポーン間隔
    └── activateRange       : プレイヤーからの有効化距離
```

- プレイヤーがactivateRange外の場合は非アクティブ（処理節約）
- エリア再入場時にリスポーン（SavePoint休息でリセット）

## 撃破時の処理
```
敵HP <= 0 → OnCharacterDeath
    ↓
1. 経験値配布 → OnExpGained(killerHash, expAmount)
2. 通貨ドロップ → OnCurrencyChanged(amount)
3. アイテムドロップ → DropTable.Roll() → LootDropper.SpawnLoot()
4. 死亡演出（フィードバック）
5. オブジェクトプールに返却 or Destroy
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 敵AI行動 | AICore上の敵固有ActData実行 | PlayMode | High |
| スポーン管理 | 配置・リスポーン・有効化距離 | PlayMode | High |
| 撃破ドロップ | ドロップテーブルに基づくアイテム生成 | EditMode | High |
| 経験値・通貨配布 | 撃破時のリワード | EditMode | Medium |
| 巡回行動 | Patrol用の巡回ルート | PlayMode | Medium |
| プール管理 | 敵オブジェクトのプーリング | PlayMode | Medium |

## 設計メモ
- Section1は雑魚敵数種のみ。ボスAIはSection3
- 敵のActData定義はAIInfo（ScriptableObject）で設定
- ドロップテーブルは確率ベース（レアリティ付き）
- SavePoint休息でリスポーンするが、ボスは除外（Section3）
