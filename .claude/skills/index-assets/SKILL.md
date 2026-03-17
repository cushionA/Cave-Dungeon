---
name: index-assets
description: 音声アセットライブラリのインデックス作成・更新・検索を行う。手持ちの音声ファイルからゲームに最適なSE/BGMを見つける。
user-invocable: true
argument-hint: build|update|search <query>|stats
---

# Index Assets: $ARGUMENTS

音声ライブラリのインデックス管理と検索を行う。

## サブコマンド

### `build` — フルスキャン

1. `config/asset-gen.json` の `audio_libraries` を確認
2. パスが存在しない場合はユーザーに修正を案内
3. `python tools/asset-index.py build` を実行
4. 結果を報告（ファイル数、ライブラリ数）

### `update` — 差分更新

1. `python tools/asset-index.py update` を実行
2. 追加・削除されたファイル数を報告

### `search <query>` — 検索+候補提示

1. `python tools/asset-index.py search "<query>"` を実行
2. ツール出力のJSON候補リストを評価
3. feature-dbのpending assetsのDescriptionと照合
4. 最も適切な候補を理由付きで提示
5. ユーザーが承認したら、そのファイルをUnityプロジェクトのAssets/にコピーする手順を案内

### `stats` — 統計表示

1. `python tools/asset-index.py stats` を実行
2. 結果をそのまま表示

## 前提条件

- `config/asset-gen.json` が存在すること
- `audio_libraries` のパスにアクセス可能であること
- 初回は `build` でインデックスを作成する必要がある

## 検索のコツ

- 日本語キーワードとファイル名（通常英語）の両方で検索
- 「ジャンプSE」→ `jump sound effect short` のように英語キーワードも試す
- フォルダ名でカテゴリを絞り込む: `Action jump`, `UI click`
