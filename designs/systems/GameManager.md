# System: GameManager
Section: 1 — MVP

## 責務
全マネージャー・データコンテナへの唯一の参照点。シングルトンとして機能し、システム間通信のイベントハブを提供する。

## 依存
- 入力: なし（最初に初期化される）
- 出力: Data（SoACharaDataDic）、Events（イベントハブ）、各Manager参照

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| GameManager | シングルトン本体・初期化順序管理 | Yes |
| GameEvents | イベント定義・発火（Common_Section1 §4参照） | No |

## インタフェース
```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; }
    public SoACharaDataDic Data { get; }
    public GameEvents Events { get; }

    // マネージャー参照（セクション1）
    public EquipmentManager Equipment { get; }
    public InventoryManager Inventory { get; }
    public CurrencyManager Currency { get; }
    public SaveManager Save { get; }
    public LevelStreamingManager LevelStreaming { get; }
}
```

## データフロー
```
シーン起動 → GameManager.Awake() → Data初期化 → 各Manager初期化
            ↓
全システム → GameManager.Instance.Data / Events / Manager
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| シングルトン初期化 | DontDestroyOnLoad + 初期化順序管理 | EditMode | High |
| データコンテナ保持 | SoACharaDataDicのライフサイクル管理 | EditMode | High |
| イベントハブ | GameEventsの生成・公開 | EditMode | High |
| マネージャー登録 | 各Managerの参照保持と初期化順制御 | EditMode | Medium |

## 設計メモ
- アーキテクチャ00準拠。散在するシングルトンを排除し、全参照をここに集約
- 初期化順序: Data → Events → Equipment → Inventory → Currency → Save → LevelStreaming
- Scene遷移時にDestroyされない（DontDestroyOnLoad）
