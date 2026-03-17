---
description: Unity C# coding conventions and component design rules
---

# Unity コード規約

## 原則
- **KISS**: 可能な限りシンプルな解決策を選ぶ
- **YAGNI**: 現在必要な機能のみを実装する
- **DRY**: コードの重複を避ける
- **問題の根本原因を特定する**: 応急処置ではなく根本的な解決を図る

## 命名規則

### ケーシング

| 対象 | スタイル | 例 |
|------|---------|-----|
| クラス名 | PascalCase | `PlayerMovement` |
| インターフェース | I + PascalCase | `IDamageable` |
| メソッド名 | PascalCase | `CalculateDistance()` |
| プロパティ | PascalCase | `MaxHealth` |
| public フィールド | PascalCase | `MovementSpeed` |
| ローカル変数・引数 | camelCase | `moveDirection` |
| privateフィールド | _camelCase | `_currentHealth` |
| 定数 (const) | k_ + PascalCase | `k_MaxSpeed` |
| SerializeField | camelCase | `[SerializeField] private float moveSpeed` |
| enum 名 | PascalCase (単数形) | `WeaponType` |
| enum フラグ | PascalCase (複数形) | `[Flags] AttackModes` |
| 名前空間 | PascalCase | `MyGame.PlayerSystems` |

### 命名の原則
- 変数名は名詞: `playerScore`, `targetObject`
- bool値は動詞で始める: `isAlive`, `hasWeapon`, `canJump`
- メソッド名は動詞: `FireWeapon()`, `CalculateDistance()`
- boolを返すメソッドは疑問文: `IsPlayerAlive()`, `HasAmmo()`
- イベント名は動詞句: `PointsScored`, `DoorOpened`
- イベント発生メソッドは "On" で始める: `OnDoorOpened()`
- 特殊文字・略語は避ける（数学的表現やループカウンタを除く）

## コンポーネント設計
- 1コンポーネント = 1責務（Single Responsibility）
- publicフィールドではなく `[SerializeField] private` を使用
- Inspector設定値は `[Header("Section")]` でグループ化
- `RequireComponent` 属性で依存コンポーネントを明示
- `GetComponent` 呼び出しは `Awake`/`Start` でキャッシュ

## クラス構成順序
1. Fields
2. Properties
3. Events / Delegates
4. MonoBehaviour Methods (Awake, Start, OnEnable, etc.)
5. Public Methods
6. Private Methods

## ファイル構成
- 1ファイル = 1クラス
- ファイル名 = クラス名
- Editor専用スクリプトは `Editor/` フォルダに配置
- テストは `Tests/EditMode/` または `Tests/PlayMode/` に配置

## マジックナンバー禁止
```csharp
// NG
transform.position += Vector3.up * 9.81f * Time.deltaTime;

// OK
private const float k_Gravity = 9.81f;
transform.position += Vector3.up * k_Gravity * Time.deltaTime;
```

## MonoBehaviour ライフサイクル順序
1. Awake() — コンポーネント参照の取得
2. OnEnable() — イベント登録
3. Start() — 初期化ロジック
4. Update/FixedUpdate — 毎フレーム処理
5. OnDisable() — イベント解除
6. OnDestroy() — リソース解放

## フォーマット
- インデント: スペース4つ（タブ不使用）
- 中括弧: Allman スタイル（新しい行に開き括弧）
- `csharp_prefer_braces = true`（1行でも中括弧を付ける）
- `var` は使用しない（型を明示）

## パフォーマンス規約

### Update内のアロケーション禁止
```csharp
// NG: 毎フレームでアロケーション
void Update()
{
    List<GameObject> tempList = new List<GameObject>();
}

// OK: フィールドで事前確保して再利用
private List<GameObject> _reusableList = new List<GameObject>();
void Update()
{
    _reusableList.Clear();
}
```

### 文字列連結の最適化
- Update内での `string +` 連結禁止
- `StringBuilder` またはキャッシュを使用

### GetComponent呼び出しの最小化
- 初期化時（Awake/Start）にキャッシュ
- `transform` プロパティもキャッシュ推奨

### 距離計算の最適化
- `Vector3.Distance` の代わりに `sqrMagnitude` 比較を検討
- 範囲値も2乗でキャッシュ

### タグ比較
- `obj.tag == "Player"` ではなく `obj.CompareTag("Player")` を使用

### enum型の最適化
- 値が少ない場合は `byte` 型指定でメモリ節約
- 4つ以上の条件分岐では `switch` 文推奨

### 条件コンパイル
```csharp
#if UNITY_EDITOR
    // エディター専用デバッグ処理
#endif

[System.Diagnostics.Conditional("UNITY_EDITOR")]
void DebugLog(string message)
{
    Debug.Log(message); // リリースビルドでは完全に除去
}
```

## コメント規約
- コードが自明でない場合のみ使用
- 「何を」ではなく「なぜ」を説明
- `[Tooltip("説明")]` でInspectorのヒントを付与
- XML ドキュメント (`/// <summary>`) は公開APIに付与
