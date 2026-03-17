# System: MapSystem
Section: 1 — MVP

## 責務
2Dタイルマップ管理、カメラ追従、ミニマップ、全体マップの表示と更新。

## 依存
- 入力: タイルマップデータ、プレイヤー位置、LevelStreaming（エリア情報）
- 出力: カメラ位置更新、マップUI更新、OnAreaTransitionイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CameraController | プレイヤー追従・カメラ境界 | Yes |
| MinimapRenderer | ミニマップUIの描画 | Yes |
| WorldMapUI | 全体マップの表示・操作 | Yes |
| MapDataManager | 訪問済みエリアの記録 | No |
| AreaBoundary | エリア境界定義（Collider Trigger） | Yes |

## カメラ追従
```csharp
// スムーズ追従 + デッドゾーン
Vector2 target = player.position + lookAhead * moveDirection;
camera.position = Vector2.SmoothDamp(camera.position, target, ref velocity, smoothTime);
camera.position = ClampToBounds(camera.position, currentAreaBounds);
```

## ミニマップ
- RenderTextureベースのリアルタイムミニマップ
- 訪問済み/未訪問の霧表示
- プレイヤー・仲間・敵のアイコン表示

## 全体マップ
- ポーズ中に表示
- エリア一覧 + 探索率表示
- ファストトラベル先の選択（SaveSystemと連携）

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| カメラ追従 | スムーズ追従+デッドゾーン+境界クランプ | PlayMode | High |
| ミニマップ描画 | RenderTextureベースのリアルタイム表示 | PlayMode | High |
| 全体マップ表示 | ポーズ時の全体マップUI | PlayMode | Medium |
| 訪問記録 | エリアの探索率管理 | EditMode | Medium |
| エリア境界 | Triggerによるエリア検出 | PlayMode | Medium |

## 設計メモ
- カメラのorthographicSize=7（asset-spec.json準拠）
- エリア境界はLevelStreamingのトリガーと兼用
- 全体マップデータはSaveSystemで永続化
