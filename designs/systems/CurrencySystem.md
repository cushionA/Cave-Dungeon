# System: CurrencySystem
Section: 1 — MVP

## 責務
通貨の獲得・消費・管理、デスペナルティ（死亡時20%ロスト）。

## 依存
- 入力: EnemySystem（撃破報酬）、ShopSystem（購入消費）、OnCharacterDeath（デスペナルティ）
- 出力: 通貨残高更新、OnCurrencyChangedイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CurrencyManager | 通貨残高管理 | No (GameManager保持) |
| DeathPenalty | 死亡時の通貨ロスト処理 | No |

## インタフェース
```csharp
public class CurrencyManager
{
    int CurrentBalance { get; }
    bool TrySpend(int amount);      // 残高チェック+消費
    void Add(int amount);           // 通貨追加
    void ApplyDeathPenalty();       // 20%ロスト
}
```

## デスペナルティ
```
プレイヤー死亡 → OnCharacterDeath
    → lostAmount = Mathf.FloorToInt(currentBalance * 0.2f)
    → currentBalance -= lostAmount
    → 最後のセーブポイントに復帰
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 通貨獲得 | 敵撃破・アイテム売却 | EditMode | High |
| 通貨消費 | ショップ購入 | EditMode | High |
| デスペナルティ | 死亡時20%ロスト | EditMode | High |
| 残高永続化 | SaveSystem連携 | EditMode | Medium |

## 設計メモ
- 通貨は単一種類（Section1）。複数通貨が必要なら後で拡張
- ロスト通貨の回収機能（ソウルシリーズ風）は対象外（GDD対象外）
