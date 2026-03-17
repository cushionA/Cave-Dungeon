# System: ShopSystem
Section: 1 — MVP

## 責務
ショップUIの表示、アイテム購入・売却。商品ラインナップの管理。

## 依存
- 入力: CurrencySystem（残高）、InventorySystem（所持品）、IInteractable（ショップNPC）
- 出力: アイテム追加/通貨消費

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ShopController | ショップUIロジック | Yes (IInteractable) |
| ShopInventory | 商品ラインナップ定義 | No (ScriptableObject) |
| ShopUI | UI表示・操作 | Yes |

## インタフェース
```csharp
[CreateAssetMenu]
public class ShopInventory : ScriptableObject
{
    [Serializable]
    public struct ShopEntry
    {
        public ItemData item;
        public int price;
        public int stockLimit;  // -1 = 無限
    }
    public ShopEntry[] entries;
}
```

## 購入フロー
```
ショップNPCインタラクト → ShopUI表示
    → アイテム選択
    → CurrencyManager.TrySpend(price) → 成功
    → InventoryManager.Add(itemId, 1)
    → stock更新（有限の場合）
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 購入処理 | 通貨消費+アイテム追加 | EditMode | High |
| 売却処理 | アイテム消費+通貨獲得（買値の一定割合） | EditMode | Medium |
| 商品ラインナップ | ScriptableObjectで定義 | EditMode | Medium |
| 在庫管理 | 有限在庫のカウント | EditMode | Low |

## 設計メモ
- 売却額は買値の30%程度（バランスシートで調整）
- 商品ラインナップはストーリー進行で変化する可能性（Section3以降）
