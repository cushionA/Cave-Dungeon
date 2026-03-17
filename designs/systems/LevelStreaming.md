# System: LevelStreaming
Section: 1 — MVP

## 責務
エリア境界のトリガーによるAdditive Scene Loadingでシームレスなエリア遷移を実現する。

## 依存
- 入力: MapSystem（AreaBoundary）、プレイヤー位置
- 出力: シーンのロード/アンロード、OnAreaTransitionイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| LevelStreamingManager | シーンロード/アンロードの統括 | No (GameManager保持) |
| AreaTrigger | エリア境界のTrigger検知 | Yes |
| SceneRegistry | エリアID→シーン名のマッピング | No (ScriptableObject) |

## シームレス遷移フロー
```
1. プレイヤーがAreaTrigger (Collider2D, IsTrigger=true) に進入
2. AreaTrigger → LevelStreamingManager.RequestLoad(nextAreaId)
3. SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive)
4. ロード完了 → 新エリアのルートオブジェクトを有効化
5. プレイヤーが前エリアのTriggerから十分離れたら
6. SceneManager.UnloadSceneAsync(previousAreaId)
7. OnAreaTransition発火
```

## エリア配置
```
[エリアA (Scene)] ─── [AreaTrigger] ─── [エリアB (Scene)]
                       ↕ overlap zone
                  両方のシーンが同時にロードされている領域
```

- オーバーラップゾーン: 境界前後のチャンクが両方表示される
- アンロード条件: プレイヤーがオーバーラップゾーンを完全に抜けたら

## SceneRegistry
```csharp
[CreateAssetMenu]
public class SceneRegistry : ScriptableObject
{
    [Serializable]
    public struct AreaEntry
    {
        public string areaId;
        public string sceneName;        // Addressable or Build Settings name
        public string[] adjacentAreaIds; // 隣接エリア（先行ロード候補）
    }

    public AreaEntry[] areas;
}
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Additiveシーンロード | 非同期シーンロード | PlayMode | High |
| シーンアンロード | 離脱エリアの非同期アンロード | PlayMode | High |
| AreaTrigger検知 | Collider2Dトリガーによるエリア検出 | PlayMode | High |
| オーバーラップ管理 | 両エリア同時存在の制御 | PlayMode | Medium |
| 先行ロード | 隣接エリアのバックグラウンドプリロード | PlayMode | Low |

## 設計メモ
- 各エリアは独立したUnityシーン（.unity）
- GameScene（永続シーン）+ エリアシーン（Additive）の構成
- プレイヤー・仲間・UIは永続シーンに属する（DontDestroyOnLoad不要）
- ロード中のフリーズ回避: 非同期ロード + allowSceneActivation制御
