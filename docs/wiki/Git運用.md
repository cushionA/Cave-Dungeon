# Git運用

## ブランチ戦略

```
main              ← 安定版（リリース可能な状態）
├── feature/○○   ← 機能開発
├── bugfix/○○    ← バグ修正
├── hotfix/○○    ← 緊急修正（mainから分岐）
└── refactor/○○  ← 大規模リファクタリング
```

- ブランチ名は日本語OK
- featureブランチはmainから分岐し、mainへマージ
- **区切りついたらPRでmainに統合し、ブランチは適切に切り替える**

## コミットルール

### メッセージ形式

```
[種類](範囲): 日本語タイトル

- 変更内容の箇条書き（任意）

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
```

### コミットタイプ

| 種類 | 用途 | 例 |
|------|------|-----|
| feat | 新機能 | feat(player): ジャンプ機能を実装 |
| fix | バグ修正 | fix(camera): 追従時の揺れを修正 |
| test | テスト | test(combat): ダメージ計算テスト追加 |
| refactor | リファクタリング | refactor(input): InputHandler分離 |
| perf | パフォーマンス改善 | perf(enemy): GetComponent呼び出しキャッシュ化 |
| chore | その他 | chore(config): editorconfig追加 |
| asset | アセット | asset(sprite): プレイヤースプライト追加 |
| docs | ドキュメント | docs(設計): GDD更新 |

### コミットタイミング

- 1機能 = 1コミットが目安
- テストとその対象の実装は同じコミットに含める
- 動かないコードをコミットしない
- **コミット後は必ずプッシュ**

## ステージングルール

### ステージしてはいけないもの

- **アセットストア由来など、他者に権利があるアセット**
- 機密情報（APIキー、credentials）
- ビルド出力（`Build/`, `Builds/`）
- IDE設定（`.vs/`, `.idea/`）

### .metaファイル管理

- アセット追加時は`.meta`も必ずセットでコミット
- アセット削除時も`.meta`の削除を確認
- `.meta`だけが残る/消えることがないようにする

## PRルール

1. テスト全Passを確認
2. `gh pr create` でPR作成（日本語タイトル、70文字以内）
3. PR作成後に `gh pr review` でレビューコメントを付ける
4. レビュー観点:
   - Code Reuse: 重複パターン、既存ユーティリティ未使用
   - Code Quality: バグ、命名規約違反、イベントリーク
   - Efficiency: ホットパスアロケーション、キャッシュ漏れ

## .gitignore

```
Library/
Temp/
Logs/
obj/
Build/
Builds/
.vs/
.idea/
```

## AI（Claude）のGit操作ルール

- コミットメッセージは必ず日本語タイトル
- `Co-Authored-By` を付与
- ユーザーの明示的指示なしにコミット・プッシュしない
- destructive操作は必ずユーザー確認
