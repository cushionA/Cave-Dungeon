# System: BacktrackReward
Section: 3 — 世界の広がり

## 責務
新能力獲得後に過去エリアへ戻ることで発見できる隠し報酬を管理する。能力ゲートで塞がれた報酬の追跡、マップ上のマーカー表示、回収状態の永続化を担う。

## 依存
- 入力: MapSystem（ミニマップ、エリア情報）、EquipmentSystem（AbilityFlag）、GateSystem（AbilityGate再評価）、SaveSystem（回収状態永続化）
- 出力: OnBacktrackRewardAvailable, OnBacktrackRewardCollected イベント → MapSystem（マーカー更新）、UISystem（通知）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| BacktrackRewardManager | 全バックトラック報酬の状態管理。能力獲得時の再評価 | No |
| BacktrackRewardChecker | 個別報酬の回収可能判定。AbilityFlag + 位置条件 | No |
| BacktrackRewardTable | エリアごとのバックトラック報酬定義 | No (ScriptableObject) |
| BacktrackRewardPickup | ワールド上の報酬インタラクトオブジェクト | Yes |

## インタフェース

### GameManager.Events.OnAbilityAcquired → BacktrackRewardManager
- 新能力獲得時に全BacktrackEntryを再評価
- 新たにアクセス可能になった報酬を通知
```csharp
GameManager.Events.OnAbilityAcquired += (AbilityFlag newAbility) =>
{
    backtrackRewardManager.ReevaluateAll(currentAbilities);
};
```

### BacktrackRewardManager → MapSystem
- アクセス可能な未回収報酬をマップマーカーとして表示
- 報酬回収後にマーカーを「回収済み」に変更
- 能力獲得前は非表示（存在を知らない）

### BacktrackRewardManager → SaveSystem
- ISaveable実装で回収済み報酬リストを永続化
- マップローカルフラグとして各エリアの回収状態を管理

### BacktrackRewardManager → GameManager.Events
```csharp
// 新たにアクセス可能になった報酬を通知
GameManager.Events.OnBacktrackRewardAvailable?.Invoke(rewardId, requiredAbility);

// 報酬回収時
GameManager.Events.OnBacktrackRewardCollected?.Invoke(rewardId);
```

## データフロー

```
プレイヤーが新能力を獲得（例: 壁蹴り）
    → GameManager.Events.OnAbilityAcquired(AbilityFlag.WallKick)
    → BacktrackRewardManager.ReevaluateAll(currentAbilities)
        → 全BacktrackEntryを走査
        → requiredAbility がcurrentAbilitiesに含まれる & 未回収
        → OnBacktrackRewardAvailable発火（UIに通知「新しい場所に行けるようになった！」）
        → MapSystem: 該当エリアにマーカー追加

プレイヤーが過去エリアに戻って報酬に到達:
    → BacktrackRewardPickup.OnPlayerInteract()
    → BacktrackRewardChecker.CanCollect(entry, currentAbilities)
    → 回収処理:
        → rewardType に応じてアイテム/通貨/能力を付与
        → BacktrackRewardManager.MarkCollected(rewardId)
        → GameManager.Events.OnBacktrackRewardCollected
        → MapSystem: マーカーを「回収済み」に変更
        → SaveSystem: 状態保存
```

## データ構造

### BacktrackRewardTable (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/World/BacktrackRewardTable")]
public class BacktrackRewardTable : ScriptableObject
{
    [Header("エリア情報")]
    public string areaId;
    public string areaName;

    [Header("報酬リスト")]
    public BacktrackRewardData[] rewards;
}
```

### BacktrackRewardData (Serializable)
```csharp
[System.Serializable]
public struct BacktrackRewardData
{
    public string rewardId;                 // ユニークID
    public BacktrackRewardType rewardType;
    public AbilityFlag requiredAbility;     // 必要な能力

    [Header("報酬内容")]
    public string itemId;                   // Item/AbilityOrb時
    public int currencyAmount;              // Currency時
    public string shortcutGateId;           // Shortcut時: 開放するGateID
    public string loreText;                 // Lore時

    [Header("表示")]
    public string locationHint;             // マップ上のヒント「壁蹴りで到達できる高台」
    public Vector2 mapPosition;             // マップ上の座標
}
```

### BacktrackRewardManager (Pure Logic)
```csharp
public class BacktrackRewardManager : ISaveable
{
    private Dictionary<string, bool> _collectedRewards;  // rewardId → collected
    private List<BacktrackRewardTable> _allTables;

    // 全報酬を再評価（新能力獲得時に呼ばれる）
    public void ReevaluateAll(AbilityFlag currentAbilities);

    // 指定エリアのアクセス可能な未回収報酬を取得
    public List<BacktrackRewardData> GetAvailableRewards(string areaId, AbilityFlag currentAbilities);

    // 回収済みにマーク
    public void MarkCollected(string rewardId);

    // 回収済みか判定
    public bool IsCollected(string rewardId);

    // ISaveable実装
    public SaveData GetSaveData();
    public void LoadSaveData(SaveData data);
}
```

### BacktrackRewardChecker (Pure Logic)
```csharp
public class BacktrackRewardChecker
{
    // 報酬が回収可能か判定
    public bool CanCollect(BacktrackRewardData reward, AbilityFlag currentAbilities);

    // 報酬を付与
    public void GrantReward(BacktrackRewardData reward);
}
```

### BacktrackRewardPickup (MonoBehaviour)
```csharp
public class BacktrackRewardPickup : MonoBehaviour
{
    [SerializeField] private string _rewardId;
    [SerializeField] private BacktrackRewardTable _table;
    [SerializeField] private SpriteRenderer _visualIndicator;

    // 能力獲得前: 非表示 or 半透明
    // 能力獲得後: 発光エフェクト
    // 回収済み: 非表示
    public void UpdateVisual(bool accessible, bool collected);

    public void OnPlayerInteract();
}
```

## 報酬種別ごとの処理

| 種別 | 付与処理 | 既存システム連携 |
|------|---------|-----------------|
| Item | InventorySystem.AddItem(itemId) | InventorySystem |
| Currency | CurrencySystem.Add(amount) | CurrencySystem |
| AbilityOrb | AbilityFlag に新フラグ追加 → 更に別のバックトラック報酬が解放される連鎖 | EquipmentSystem |
| Shortcut | GateRegistry.Open(gateId) | GateSystem |
| Lore | UIにテキスト表示（コレクション登録） | UISystem |

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| BacktrackReward_Manager | 報酬状態管理（全報酬走査、回収済みマーク、SaveSystem連携） | EditMode | High |
| BacktrackReward_Checker | AbilityFlag判定、回収可能チェック、報酬付与 | EditMode | High |
| BacktrackReward_Reevaluation | 新能力獲得時の再評価と通知 | EditMode | High |
| BacktrackReward_MapIntegration | マップマーカー表示/更新（アクセス可能/回収済み） | EditMode | Medium |
| BacktrackReward_Pickup | ワールド上のインタラクトオブジェクト制御 | EditMode | Medium |

## 設計メモ
- **メトロイドヴァニアの核心**。新能力獲得→過去エリア再訪→新発見のループがコアループの一部
- AbilityOrbを回収すると新能力が得られ、それにより更に別のバックトラック報酬が解放される連鎖が可能
- BacktrackRewardTableはエリアごとにScriptableObjectで定義。レベルデザイナーがInspectorで編集可能
- マップマーカーは能力獲得前は非表示（ネタバレ防止）。獲得後に「この能力で行ける場所がある」とだけ表示
- 回収率トラッキング: BacktrackRewardManagerが全テーブルの回収/未回収を集計→UIで「エリア回収率 3/5」等の表示に対応可能
