# Assets/MyAsset/ — ゲームコード作業ガイド

このディレクトリで作業する際、以下の rules を**必ず参照**する。

## 自動参照される規約

@../../.claude/rules/architecture.md
@../../.claude/rules/unity-conventions.md
@../../.claude/rules/asset-workflow.md

## 補足（このディレクトリ固有）

### ディレクトリ構成
- `Core/` — ピュアロジック層（MonoBehaviour 非依存）。**Debug.Log ではなく AILogger.Log を使用**
- `Runtime/` — ランタイム MonoBehaviour 層
- `Editor/` — Editor 拡張（`AssetDatabase` 使用許可範囲）
- `Data/` — ScriptableObject / 設定データ
- `UI/` — UI Toolkit / uGUI 関連
- `GameCode.asmdef` — 単一 asmdef でまとめる

### 最優先ルール
1. **GameManager 経由アクセス**: `FindObjectOfType` 禁止、ハッシュキーで O(1)
2. **SoA + SourceGenerator**: キャラデータはフィールド配列、属性で自動生成
3. **Ability 拡張**: 継承ではなくコンポーネント追加で差分実装
4. **Update 内アロケーション禁止**: new / string+ / GetComponent は Awake でキャッシュ
5. **`var` 禁止**: 型を明示
6. **`CompareTag` 使用**: `obj.tag == "xxx"` は遅い
7. **Addressable のみ**: `AssetDatabase` はランタイム禁止、`Resources.Load` も禁止

### よくあるミス
- `[SerializeField] private _fieldName` → 正: `[SerializeField] private fieldName`（アンダースコア不要）
- `Vector3.Distance(a, b) < range` → 正: `(a - b).sqrMagnitude < range * range`
- イベント購読が OnEnable、解除が OnDisable に対称にない → リーク
- `?.` / `??` を Unity Object に → fake-null trap で破棄済みオブジェクト操作（`@../../.claude/rules/unity-conventions.md` ライフサイクル罠）
- `Awaitable` インスタンスの再 await → プール再利用で undefined behavior（同上 async/await 致命罠）
- `Physics.RaycastAll` で GC alloc → `RaycastNonAlloc` + 事前確保バッファ（同上 物理クエリ規約）

詳細は `@../../.claude/rules/` 配下を参照。

### 領域別の詳細リファレンス（必要時のみ `@` 参照）

- 数学（座標変換 / quaternion 等）: `@../../.claude/refs/external/nice-wolf-studio/unity-3d-math/SKILL.md`
- 物理（Rigidbody / raycasting 詳細）: `@../../.claude/refs/external/nice-wolf-studio/unity-physics/SKILL.md`
- 2D（Sprite / Tilemap / SortingLayer 詳細）: `@../../.claude/refs/external/nice-wolf-studio/unity-2d/SKILL.md`
- Profiler 操作手順: `@../../.claude/refs/external/nice-wolf-studio/unity-performance/references/profiler-guide.md`
