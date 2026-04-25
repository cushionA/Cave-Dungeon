# Assets/Scenes/ — シーン作業ガイド

Unity シーン（`.unity`）とシーン固有アセット編集時のルール。

## 自動参照される規約

@../../.claude/rules/asset-workflow.md
@../../.claude/rules/git-workflow.md

## このディレクトリ固有のルール

### シーン保存

- シーン変更後は **必ず Unity で保存** してからコミット（`Ctrl+S` / File > Save）
- シーン変更後のコミット前チェック:
  - [ ] シーンが保存されている（Unity タブに `*` がないか）
  - [ ] Console にエラーがないか
  - [ ] Play モードで基本動作が確認できる

### `.meta` セット管理（重要）

- シーンや追加 GameObject/Prefab の追加時は **`.meta` ファイルも必ず一緒にコミット**
- 削除時も `.meta` ファイルの削除を確認
- `.meta` だけが残る/消える事故を避ける
- 詳細: `@../../.claude/rules/git-workflow.md`

### Addressable 運用

- シーン固有のアセット（タイル、背景、BGM 等）は `Stage_[ID]` グループに登録
- グループ・ラベル・アドレスの命名規則は `@../../.claude/rules/asset-workflow.md`
- 仮素材は `[PLACEHOLDER]` プレフィックス + `placeholder` ラベル
- **ランタイムコードでの `AssetDatabase` 使用は禁止**（Editor 拡張のみ許可）

### テンプレート使用

GameObject/Prefab 新規作成前に `template-registry.json` を確認（存在する場合）。
詳細: `@../../.claude/rules/template-usage.md`

### 関連ディレクトリ

- `Assets/MyAsset/` — ゲームコード（別 CLAUDE.md あり）
- `Assets/Tests/` — テストコード（別 CLAUDE.md あり）
- `Assets/Resources/` — Unity 標準 Resources フォルダ。**新規配置は Addressable 優先**（asset-workflow.md 準拠）

### アセット配置の命名規則

```
Assets/
├── Sprites/[カテゴリ]/[アセット名].png
├── Models/[カテゴリ]/[アセット名].fbx
├── Animations/[カテゴリ]/[アセット名].anim
├── Audio/
│   ├── BGM/[アセット名].mp3
│   └── SFX/[アセット名].wav
├── Materials/[カテゴリ]/[アセット名].mat
└── Prefabs/[カテゴリ]/[アセット名].prefab
```

これらディレクトリを新設する場合、配置時に asset-workflow.md の規約に従う。
