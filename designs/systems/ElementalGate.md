# System: ElementalGate
Section: 3 — 世界の広がり

## 責務
属性を使った環境パズルギミックを管理する。特定の属性攻撃で反応するオブジェクト（氷壁を炎で溶かす、雷で機械を起動する等）を既存GateSystemの拡張として実現する。

## 依存
- 入力: GateSystem（GateController, GateConditionChecker, GateRegistry）、DamageSystem（属性ダメージ検知）、Element enum
- 出力: ゲート開放イベント（既存OnGateOpened）、マップアイコン更新

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ElementalGateInteractor | 属性攻撃のヒット検知。属性一致判定→ゲート開放トリガー | Yes |
| ElementalGateDefinition | 属性ゲートのデータ定義（必要属性、必要ダメージ量、演出設定） | No (ScriptableObject) |
| ElementalGateConditionChecker | GateConditionCheckerの拡張。Elemental条件の評価 | No |

## インタフェース

### ElementalGateInteractor → GateController
- 属性条件達成時に `GateController.TryOpen()` を呼ぶ
- GateControllerは既存の開閉ロジック（コライダー無効化、ビジュアル切替）を実行

### ElementalGateInteractor → DamageSystem
- `OnTriggerEnter2D` / `OnDamageReceived` で属性攻撃を検知
- Projectileの `attackElement` またはWeaponSystemの `attackElement` を参照
- IDamageableを実装するが、キャラクターではなく環境オブジェクトとして機能

### ElementalGateInteractor → MapSystem
- 属性ヒント（「炎が必要」等）をミニマップアイコンに反映

## データフロー

```
プレイヤー/仲間が属性攻撃を属性ゲートにヒット
    → ElementalGateInteractor.OnDamageReceived(element, damage)
    → 属性一致判定: attackElement に requiredElement が含まれるか（ビット演算）
    → ダメージ閾値判定: damage >= minDamage（0なら即開放）
    → 条件達成 → GateController.TryOpen()
    → GateRegistry に状態保存（isPermanentなら永続）
    → GameManager.Events.OnGateOpened
```

## データ構造

### ElementalGateDefinition (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/Gate/ElementalGateDefinition")]
public class ElementalGateDefinition : ScriptableObject
{
    [Header("基本")]
    public string gateId;
    public ElementalRequirement requiredElement;

    [Header("条件")]
    public float minDamage;           // 必要最低ダメージ（0 = 属性攻撃で触れるだけ）
    public bool isPermanent;          // 永続開放か（ボス撃破後の通路等）
    public bool multiHitRequired;     // 複数回ヒットが必要か
    public int requiredHitCount;      // 必要ヒット数（multiHitRequired時）

    [Header("演出")]
    public string hintText;           // 「炎で溶かせそうだ」等
    public string solvedText;         // 「氷壁が溶けた！」等
}
```

### ElementalGateInteractor (MonoBehaviour)
```csharp
public class ElementalGateInteractor : MonoBehaviour
{
    [SerializeField] private ElementalGateDefinition _definition;
    [SerializeField] private GateController _gateController;

    private int _currentHitCount;

    // 属性ダメージを受けた時の処理
    public void OnElementalHit(Element attackElement, float damage);

    // 属性一致判定（Element [Flags] のビット演算）
    private bool IsElementMatch(Element attackElement);
}
```

### ElementalGateConditionChecker (既存GateConditionChecker拡張)
```csharp
// GateConditionChecker.Evaluate() 内で GateType.Elemental の分岐を追加
// ElementalGateInteractorの状態（hitCount達成済みか）を参照
```

## ElementalRequirement → Element マッピング

| ElementalRequirement | 対応する Element フラグ | ゲームプレイ例 |
|---------------------|----------------------|--------------|
| Fire | Element.Fire | 氷壁を溶かす、松明に点火 |
| Thunder | Element.Thunder | 機械を起動、水面を感電 |
| Light | Element.Light | 闇を浄化、隠し通路を照らす |
| Dark | Element.Dark | 暗幕を張る、幻影壁を通過 |
| Slash | Element.Slash | ロープ/蔦を切断 |
| Strike | Element.Strike | ひび割れ壁を破壊 |
| Pierce | Element.Pierce | 小さな穴を穿つ、圧力板を押す |

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| ElementalGate_Interaction | 属性攻撃の検知と属性一致判定（ビット演算） | EditMode | High |
| ElementalGate_MultiHit | 複数回ヒットの蓄積カウントとリセット | EditMode | Medium |
| ElementalGate_Integration | GateController/GateRegistryとの統合、永続化 | EditMode | High |
| ElementalGate_HintDisplay | ヒントテキスト表示とマップアイコン連携 | EditMode | Low |

## 設計メモ
- **既存GateSystemの自然な拡張**。GateTypeにElementalを追加し、GateConditionCheckerにElemental分岐を追加するだけ
- ElementalGateInteractorはIDamageableではなく、**専用のヒット検知**を使う。キャラクターのダメージ処理パイプラインに乗せると不要な処理（ノックバック等）が発生するため
- 代わりにProjectileのOnTriggerEnter2Dまたは武器ヒットボックスのレイヤーマスクで「環境オブジェクト」レイヤーを検知し、ElementalGateInteractorに通知する
- multiHitRequired はボス戦中のギミック（弱点を数回殴る等）に使える
- 仲間の属性攻撃でも開放可能にすることで、連携パズルの面白さを出す
