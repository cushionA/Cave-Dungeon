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
| ConsumableDefinition | 3D用の使用アクション、効果値、演出、消費の設定 | No (ScriptableObject) |
| ConsumableBelt | 消耗品の登録と選択をセーブスロットごとに管理 | No (GameManager保持) |
| KeyItemData | 鍵アイテム定義 | No (ScriptableObject) |
| InventoryUI | インベントリ画面 | Yes |

## アイテムカテゴリ
```csharp
public enum ItemCategory : byte
{
    Weapon,         // 武器 → WeaponData
    Shield,         // 盾 → ShieldData
    Core,           // コア → CoreData
    Consumable,     // 使用アイテム → ConsumableDefinition（ショートカットに登録可能）
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
| Consumable | 可（最大数は定義ごと） | 可 | 使用モーションから効果を発揮 | ベルトショートカットに登録可能 |
| PlayerMagic | 不可(各1) | 不可 | 装備(魔法ベルト) | 探索で習得、ベルトショートカットにセット |
| CompanionMagic | 不可(各1) | 不可 | セット(連携スロット) | 仲間用。連携ボタンで発動 |
| Material | 可(最大999) | 可 | 不可 | 売却・交換・装備強化素材 |
| KeyItem | 不可(各1) | 不可 | 自動/特殊 | 特定場所で自動使用、手動廃棄不可 |
| Flavor | 不可(各1) | 不可 | 閲覧 | テキスト読むだけ。ロア・世界観補強 |

### ベルトショートカット
戦闘中にすぐ使えるよう、消耗品と魔法それぞれにベルト式ショートカットを持つ。
現在の3D消耗品ベルトは4枠。「消耗品」ボタン、Esc／ゲームパッドのStartで登録画面を開く。所持している消耗品を登録し、全枠を順に切り替えられる。登録と切り替えで所持数は変わらない。使い切った枠も登録を保持し、補充するとその枠から再び使用できる。所持数、登録、選択枠はゲームのセーブスロットに保存する。
将来的に魔法ベルトのスロット数はステータス（MND等）で変動する可能性あり（Section 2以降で検討）。

消耗品の登録は `ConsumableBelt.TryAssign(slot, itemId)`、選択は `TrySelect(slot)` または `Next()`／`Prev()`、登録解除は `TryClear(slot)` を使う。現在の枠に登録されたIDは `ActiveItemId` で取得する。

| ベルト種別 | 対象カテゴリ | 操作 | 発動 |
|-----------|-------------|------|------|
| アイテムベルト | Consumable | 1・2／十字キー左右 | R／ゲームパッド右側ボタンで使用モーションを開始 |
| 魔法ベルト | PlayerMagic | 十字キー上下 / 別バインド | 魔法ボタン → MP消費して発動 |

```
ベルト回転UI:
  ... [slot N-1] → [★ current ★] → [slot N+1] ...
  ぐるぐるとループ（末尾→先頭に戻る）
```

## インタフェース
`InventoryManager.Add(itemId, category, count, maxStack)` と `Remove(itemId, count)` は実際に増減した個数を返す。カテゴリ別の一覧は `GetByCategory(category)`、ベルト登録・表示・使用で参照する消耗品の合計は `GetConsumableCount(itemId)` を使う。[所持品の実装](../../Assets/MyAsset/Core/Inventory/InventoryManager.cs)

## 消耗品使用
拾ったアイテムは所持品に加わる。ベルトから使うと選択中のアイテムと使用者を確定し、アイテム固有のモーションを準備・効果発揮・後隙の順で再生する。所持数とMPは開始時と効果発揮時に確認し、効果発揮時に消費を確定してから回復や3D攻撃判定とエフェクトを出す。発揮前に中断した場合や、その時点で所持数・MPが足りなくなっていた場合は、所持数とMPを維持する。攻撃アイテムは判定を出した時点で消費が確定する。

アイテム定義の `Consume On Use` と `Mp Cost` は個別に設定できる。所持はどの設定でも必要で、消費するMPはベルトに表示する。`Mp Cost` の初期値は0。

| Consume On Use | Mp Cost | 1回の使用で消費するもの |
|---|---|---|
| オン | 0 | アイテム1個 |
| オフ | 正の値 | 設定したMP |
| オン | 正の値 | アイテム1個と設定したMP |
| オフ | 0 | なし |

効果値は `基礎値 + 使用者の有効能力値 × 係数` を四捨五入して求める。係数が0なら固定値になる。HP回復は最大HPまで、攻撃は既存の属性防御や攻撃側の判定設定に従う。[アイテム定義](../../Assets/MyAsset/Core/Inventory/ConsumableDefinition.cs)・[効果発揮の処理](../../Assets/MyAsset/Runtime/Combat/GameActionRuntime.Consumables3D.cs)

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
| 消耗品使用 | モーション中の回復・攻撃判定、在庫とMPの確定 | PlayMode | High |
| アイテムベルト | 消耗品ベルトショートカット（可変枠、回転UI） | PlayMode | High |
| 魔法ベルト | プレイヤー魔法ベルトショートカット（可変枠、回転UI） | PlayMode | High |
| フレーバーテキスト閲覧 | Flavorアイテムのテキスト表示UI | PlayMode | Low |
| インベントリUI | アイテム一覧・使用・装備 | PlayMode | Medium |
| 所持上限 | カテゴリ別の上限チェック | EditMode | Low |

## 設計メモ
- 3D消耗品のIDは整数で、同じIDを所持品・ベルト・アイテム定義で使う。
- 3D消耗品は1枠あたりの最大数を定義で指定する。所持品の枠数上限は99枠。
- ISaveable実装で永続化
