# System: SaveSystem
Section: 1 — MVP

## 責務
セーブポイントでのデータ永続化、回復、ファストトラベル。ISaveableインタフェースにより各システムが独立してシリアライズ/デシリアライズする。

## 依存
- 入力: ISaveable実装システム群、MapSystem（ファストトラベル先）
- 出力: セーブデータファイル、回復処理、ファストトラベル実行

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| SaveManager | セーブ/ロード統括 | No (GameManager保持) |
| SavePoint | セーブポイントオブジェクト | Yes (IInteractable) |
| SaveDataStore | セーブデータの構造・シリアライズ | No |

## セーブポイント機能
```
セーブポイント使用時:
1. HP/MP/スタミナ全回復
2. セーブデータ書き出し
3. エリア内の敵リスポーン
4. ファストトラベルメニュー表示（開通済みポイント一覧）
```

## セーブデータ構造
```csharp
[Serializable]
public class SaveDataStore
{
    public string currentSavePointId;
    public string currentAreaId;
    public int playerLevel;
    public int[] statAllocation;        // STR, DEX, INT, VIT, MND, END
    public int currency;
    public float playTime;
    public Dictionary<string, SaveData> systemData; // ISaveable.SaveIdをキーに各システムのデータ

    // ISaveable実装例:
    // - EquipmentSystem → 装備中のアイテムID
    // - InventorySystem → 所持アイテムリスト
    // - MapDataManager → 訪問済みエリア・探索率
    // - FlagManager → フラグ状態（Section2以降）
}
```

## ファストトラベル
```
1. SavePoint使用 → ファストトラベルメニュー表示
2. 開通済みSavePointを一覧表示
3. 選択 → LevelStreamingManager経由でエリア遷移
4. 遷移先SavePointの位置にプレイヤー配置
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| セーブデータ書出 | JSON or Binary でファイル保存 | EditMode | High |
| セーブデータ読込 | ファイルからの復元 | EditMode | High |
| HP/MP/スタミナ回復 | セーブポイント使用時の全回復 | EditMode | High |
| ファストトラベル | 開通済みポイント間の移動 | PlayMode | Medium |
| 敵リスポーン | セーブポイント使用時の敵再配置 | PlayMode | Medium |
| セーブスロット管理 | 複数セーブスロット | EditMode | Low |

## 設計メモ
- セーブ形式: JSON（開発中）→ バイナリ（リリース時）
- セーブデータのバージョニング対応（フィールド追加時の後方互換）
- 暗号化はリリース時に追加（開発中はプレーンテキスト）
