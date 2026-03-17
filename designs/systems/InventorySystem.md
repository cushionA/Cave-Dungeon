# System: InventorySystem
Section: 1 — MVP

## 責務
アイテムの所持・管理・使用。装備品、消耗品、素材、鍵アイテムのカテゴリ管理。

## 依存
- 入力: ShopSystem（購入追加）、EnemySystem（ドロップ追加）、探索（拾得）
- 出力: アイテム使用効果、装備変更要求、OnItemAcquiredイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| InventoryManager | アイテム所持管理 | No (GameManager保持) |
| ItemData | アイテム定義ベース | No (ScriptableObject) |
| ConsumableData | 消耗品定義 | No (ScriptableObject) |
| KeyItemData | 鍵アイテム定義 | No (ScriptableObject) |
| InventoryUI | インベントリ画面 | Yes |

## アイテムカテゴリ
```csharp
public enum ItemCategory : byte
{
    Weapon,         // 武器 → WeaponData
    Shield,         // 盾 → ShieldData
    Core,           // コア → CoreData
    Consumable,     // 使用アイテム → ConsumableData（ショートカットに装備可能）
    PlayerMagic,    // プレイヤーの魔法 → MagicData（習得型、インベントリ管理）
    CompanionMagic, // NPCの魔法 → MagicData（仲間が使用、連携スロットにセット可能）
    Material,       // 素材（売却・交換用）
    KeyItem,        // 鍵アイテム（ストーリー進行、売却不可）
    Flavor,         // フレーバーアイテム（テキスト閲覧用、世界観・ロア）
}
```

### カテゴリ詳細

| カテゴリ | スタック | 売却 | 使用 | 備考 |
|---------|---------|------|------|------|
| Weapon | 不可(各1) | 不可 | 装備 | WeaponDataで定義 |
| Shield | 不可(各1) | 不可 | 装備 | ShieldDataで定義 |
| Core | 不可(各1) | 不可 | 装備 | CoreDataで定義 |
| Consumable | 可(最大99) | 可 | 使用(効果適用) | ベルトショートカットに装備可能 |
| PlayerMagic | 不可(各1) | 不可 | 装備(魔法ベルト) | 探索で習得、ベルトショートカットにセット |
| CompanionMagic | 不可(各1) | 不可 | セット(連携スロット) | 仲間用。連携ボタンで発動 |
| Material | 可(最大999) | 可 | 不可 | 売却・交換・装備強化素材 |
| KeyItem | 不可(各1) | 不可 | 自動/特殊 | 特定場所で自動使用、手動廃棄不可 |
| Flavor | 不可(各1) | 不可 | 閲覧 | テキスト読むだけ。ロア・世界観補強 |

### ベルトショートカット
戦闘中にすぐ使えるよう、消耗品と魔法それぞれにベルト式ショートカットを持つ。
スロット数はconst定数で管理し、開発中に調整する（`k_ConsumableBeltSize`, `k_MagicBeltSize`）。
将来的に魔法ベルトのスロット数はステータス（MND等）で変動する可能性あり（Section 2以降で検討）。

```csharp
public class BeltShortcut<T> where T : ItemData
{
    int slotCount;          // 可変（開発段階で調整）
    int currentIndex;       // 現在選択中のスロット
    T[] slots;              // セットされたアイテム参照

    void RotateNext();      // 次のスロットへ回転
    void RotatePrev();      // 前のスロットへ回転
    T GetCurrent();         // 現在選択中のアイテム取得
    void Set(int index, T item);
    void Clear(int index);
}
```

| ベルト種別 | 対象カテゴリ | 操作 | 発動 |
|-----------|-------------|------|------|
| アイテムベルト | Consumable | 十字キー左右 / ホイール | 使用ボタン → 即座に効果適用 |
| 魔法ベルト | PlayerMagic | 十字キー上下 / 別バインド | 魔法ボタン → MP消費して発動 |

```
ベルト回転UI:
  ... [slot N-1] → [★ current ★] → [slot N+1] ...
  ぐるぐるとループ（末尾→先頭に戻る）
```

## インタフェース
```csharp
public class InventoryManager
{
    bool Add(string itemId, int count = 1);
    bool Remove(string itemId, int count = 1);
    int GetCount(string itemId);
    bool Has(string itemId);
    List<InventorySlot> GetByCategory(ItemCategory category);
    void UseConsumable(string itemId, int targetHash);
    bool CanSell(string itemId);        // 装備品, KeyItem, Flavor, Magic → false
    bool CanStack(string itemId);       // 装備・魔法 → false
    string GetFlavorText(string itemId); // Flavorアイテムのテキスト取得
}
```

## 消耗品使用
```
アイテムベルト or InventoryUI → UseConsumable(itemId, playerHash)
    → ConsumableData.Effect 適用（HP回復、バフ等）
    → Remove(itemId, 1)
```

## 魔法使用
```
魔法ベルト → UseMagic(itemId, playerHash)
    → MP消費チェック → MagicData.Effect 適用
    → MPは消費するが魔法はインベントリから消えない
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| アイテム追加・削除 | 所持数の増減 | EditMode | High |
| カテゴリ別取得 | 装備/消耗品/素材/鍵の分類 | EditMode | High |
| 消耗品使用 | 効果適用+在庫減少 | EditMode | High |
| アイテムベルト | 消耗品ベルトショートカット（可変枠、回転UI） | PlayMode | High |
| 魔法ベルト | プレイヤー魔法ベルトショートカット（可変枠、回転UI） | PlayMode | High |
| フレーバーテキスト閲覧 | Flavorアイテムのテキスト表示UI | PlayMode | Low |
| インベントリUI | アイテム一覧・使用・装備 | PlayMode | Medium |
| 所持上限 | カテゴリ別の上限チェック | EditMode | Low |

## 設計メモ
- アイテムIDはstring（ScriptableObjectのnameと一致）
- 所持上限は消耗品99個、装備品は各種10個程度（バランスで調整）
- ISaveable実装で永続化
