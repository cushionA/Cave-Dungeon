# System: EnemySystem
Section: 2 — AI・仲間・連携

## 責務
雑魚敵の配置、スポーン、AI行動、ドロップ管理。AICore基盤の3層判定の上で敵固有のロジックを実装。

## 依存
- 入力: AICore（AIBrain基盤・3層判定）、DataContainer、DamageSystem
- 出力: 敵の行動実行、撃破時のドロップ・経験値・通貨配布

## アーキテクチャ準拠
- AICore の3層判定ループ上で動作
- 敵の行動パターンはAIInfo（ScriptableObject）のAIMode配列で定義
- DamageScoreTrackerにより「最もダメージを与えてくる相手」をターゲットに選択

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| EnemyController | 敵固有AI補助（巡回ルート等） | Yes |
| EnemySpawner | スポーンポイント管理・リスポーン | Yes |
| DropTable | 撃破時のドロップアイテム定義 | No (ScriptableObject) |
| LootDropper | ドロップアイテムのワールド生成 | Yes |
| EnemyPool | 敵オブジェクトのプーリング | No |

## スポーンシステム
```
EnemySpawner (シーン配置)
    ├── spawnPoints[]       : スポーン位置
    ├── enemyPrefab         : 敵プレハブ参照（Addressable）
    ├── maxActive           : 同時存在数上限
    ├── respawnDelay        : リスポーン間隔
    └── activateRange       : プレイヤーからの有効化距離
```

- プレイヤーがactivateRange外の場合は非アクティブ（処理節約）
- エリア再入場時にリスポーン（SavePoint休息でリセット）

## 敵AIの3層判定例
```
AIMode "通常":
  targetRules:
    [0] conditions: [DamageScore > 100]
        → targetSelects[0] = DamageScore降順 → 最もダメージを与えてくる相手
    [1] conditions: [Count(敵陣営) >= 1]
        → targetSelects[1] = Distance昇順 → 最寄りの敵を攻撃

  actionRules:
    [0] conditions: [Distance InRange(0, 3)]   → actions[0] = "近距離攻撃"
    [1] conditions: [Distance InRange(3, 8)]   → actions[1] = "接近"
    [2] conditions: [SelfHpRatio < 20]         → actions[2] = "逃走"

  defaultActionIndex: 3 → actions[3] = "巡回"
```

## 撃破時の処理
```
敵HP <= 0 → GameManager.Events.OnCharacterDeath
    ↓
1. 経験値配布 → OnExpGained(killerHash, expAmount)
2. 通貨ドロップ → OnCurrencyChanged(amount)
3. アイテムドロップ → DropTable.Roll() → LootDropper.SpawnLoot()
4. 死亡演出（フィードバック）
5. EnemyPool に返却 or Destroy
6. SoAコンテナから Remove(hash)
```

## ドロップテーブル
```csharp
// ScriptableObject
public class DropTable : ScriptableObject
{
    public int expReward;
    public int currencyMin;
    public int currencyMax;
    public DropEntry[] drops;
}

public struct DropEntry
{
    public ItemId itemId;
    public float dropRate;    // 0.0-1.0
    public int minCount;
    public int maxCount;
}
```

## インタフェース
- `GameManager.Events.OnCharacterDeath` → 撃破処理トリガー
- `GameManager.Events.OnExpGained` → LevelUpSystem が受信
- `GameManager.Events.OnCurrencyChanged` → CurrencySystem が受信
- `GameManager.Events.OnItemAcquired` → InventorySystem が受信
- `SaveSystem.OnRest` → EnemySpawner がリスポーン実行

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Enemy_AIBehavior | AICore上の敵固有3層判定実行 | EditMode | High |
| Enemy_SpawnManagement | 配置・リスポーン・有効化距離判定 | EditMode | High |
| Enemy_DropSystem | ドロップテーブルに基づくアイテム・通貨・経験値生成 | EditMode | High |
| Enemy_RewardDistribution | 撃破時のリワード配布（経験値・通貨・アイテム） | EditMode | Medium |
| Enemy_PatrolBehavior | Patrol用の巡回ルート移動 | EditMode | Medium |
| Enemy_Pooling | 敵オブジェクトのプーリングと再利用 | EditMode | Medium |

## 設計メモ
- 雑魚敵数種のみ。ボスAIはSection3のBossSystem
- 敵のAIMode定義はAIInfo（ScriptableObject）で設定
- ドロップテーブルは確率ベース（重み付き抽選）
- SavePoint休息でリスポーンするが、ボスは除外（Section3）
- EnemyPoolはSection2時点でSimplePool実装、大量敵対応はSection3以降で拡張
