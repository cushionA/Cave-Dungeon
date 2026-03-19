# System: GateSystem
Section: 2 — AI・仲間・連携

## 責務
エリア間の進行を制御するゲートの管理。エリアクリアゲート、能力ゲート、アイテムゲートの開閉を統括する。

## 依存
- 入力: MapSystem（エリア情報）、SaveSystem（ゲート状態永続化）、InventorySystem（アイテム所持確認）
- 出力: ゲート開閉状態、エリア通行可否

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| GateController | 個別ゲートの開閉制御 | Yes |
| GateRegistry | 全ゲート状態の一元管理 | No |
| GateConditionChecker | ゲート開放条件の判定 | No |

## ゲートの種類

| 種類 | 開放条件 | 例 |
|------|---------|-----|
| **ClearGate** | エリアのボスやイベントクリア | ボス撃破でエリア2へ |
| **AbilityGate** | 特定能力の獲得 | 壁蹴り習得で壁を越える |
| **KeyGate** | 特定アイテム所持 | 鍵で扉を開ける |

## ゲートデータ
```csharp
public enum GateType : byte
{
    Clear,      // エリアクリア必要
    Ability,    // 特定能力必要
    Key,        // 特定アイテム必要
}

// ScriptableObject
public class GateDefinition : ScriptableObject
{
    public string gateId;           // 一意識別子
    public GateType gateType;
    public AbilityFlag requiredAbility; // AbilityGateの場合
    public string requiredItemId;   // KeyGateの場合
    public string requiredClearFlag; // ClearGateの場合
    public bool isPermanent;        // 一度開けたら永続か
}
```

## GateController（シーン配置）
```
GateController (シーン配置)
    ├── gateDefinition : GateDefinition (ScriptableObject)
    ├── closedCollider : 通行を塞ぐコライダー
    ├── openedVisual   : 開放後のビジュアル
    ├── closedVisual   : 閉鎖中のビジュアル
    └── gateHint       : 「○○が必要」ヒント表示
```

### 開閉フロー
```
GateController.CheckOpen()
    ↓
GateConditionChecker.Evaluate(gateDefinition)
    ├── ClearGate: グローバルフラグ確認
    ├── AbilityGate: プレイヤーのAbilityFlag確認
    └── KeyGate: InventorySystem にアイテム確認
    ↓
条件成立:
    → closedCollider 無効化
    → closedVisual → openedVisual 切替
    → GateRegistry に開放記録
    → isPermanent ならセーブデータに永続化
    ↓
未成立:
    → gateHint 表示（「壁蹴りが必要」等）
```

## GateRegistry
```csharp
public class GateRegistry
{
    // グローバルフラグとして管理
    private Dictionary<string, bool> _gateStates;

    public bool IsOpen(string gateId);
    public void Open(string gateId);
    public void SaveState(ISaveWriter writer);
    public void LoadState(ISaveReader reader);
}
```

- マップローカルフラグとグローバルフラグの分離はフラグ管理システムに従う
- 永続ゲート（ボスクリア等）はグローバルフラグ
- 一時ゲート（スイッチ操作等）はマップローカルフラグ

## バックトラック設計
ゲートは**メトロイドヴァニアのバックトラック**の中心要素:
- 初訪問時: AbilityGateが閉じている → 新能力入手後に再訪
- 再訪時: ゲートが開けられる → 隠しアイテム・ショートカット発見
- マップ上にゲート位置をアイコン表示（未開放: 赤、開放: 緑）

## インタフェース
- `GameManager.Events.OnBossDefeated` → ClearGate 開放チェック
- `GameManager.Events.OnAbilityAcquired` → AbilityGate 再チェック
- `SaveSystem` → GateRegistry の永続化
- `MapSystem` → ゲート位置のミニマップ表示

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Gate_ConditionCheck | ゲートタイプ別の開放条件判定 | EditMode | High |
| Gate_OpenClose | ゲートの開閉処理（コライダー・ビジュアル切替） | EditMode | High |
| Gate_Registry | 全ゲート状態の管理・永続化 | EditMode | High |
| Gate_HintDisplay | 未開放ゲートのヒント表示 | EditMode | Medium |
| Gate_MapIntegration | ミニマップ・全体マップへのゲート表示 | EditMode | Medium |

## 設計メモ
- GateControllerはシーン配置。ステージデザインでゲート位置を決定
- Section3のElementalGateはGateTypeに属性条件を追加する形で拡張可能
- ゲートヒントはメトロイドヴァニアの探索誘導として重要（「ここは後で来る場所」を示唆）
- ISaveable実装でセーブシステムと連携
