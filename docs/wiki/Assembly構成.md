# Assembly構成

## asmdef一覧

| asmdef | パス | 参照先 | 用途 |
|--------|------|--------|------|
| Game.Core | `Assets/MyAsset/Core/` | Collections, InputSystem, UniTask, R3.Unity | コアロジック |
| GameCode | `Assets/MyAsset/` | Game.Core, InputSystem, UniTask, R3.Unity, LitMotion, AnyPortrait, SensorToolkit | ゲーム実装 |
| Game.Tests.EditMode | `Assets/Tests/EditMode/` | Game.Core, GameCode | EditModeテスト |
| Game.Tests.PlayMode | `Assets/Tests/PlayMode/` | Game.Core, GameCode | PlayModeテスト |

## 依存方向

```
Game.Core（共通型・インターフェース・基盤ロジック）
    ↑
GameCode（Character, Combat, AI, World, Economy, UI）
    ↑
Game.Tests.EditMode / Game.Tests.PlayMode
```

- 循環参照禁止
- 上位は下位を参照しない
- Game.Coreは外部パッケージへの依存を最小限に

## 外部パッケージ

| パッケージ | asmdef名 | 用途 |
|-----------|---------|------|
| Unity Collections | Unity.Collections | NativeArray等 |
| Input System | Unity.InputSystem | 入力管理 |
| Addressables | Unity.Addressables | アセット管理 |
| UniTask | UniTask | async/await |
| R3 | R3.Unity | リアクティブ（Subject<T>） |
| LitMotion | LitMotion | Tweenアニメーション |
| AnyPortrait | AnyPortrait | 2Dアニメーション |
| SensorToolkit | SensorToolkit | 検知・認識 |

## 新システム追加時のルール

1. Game.Core に収まるか確認（共通型・インターフェースならCore）
2. 大規模システムは独立asmdef検討（循環参照を避ける）
3. テスト用asmdefの参照設定を確認
4. 外部パッケージ追加時はasmdefに参照追加
