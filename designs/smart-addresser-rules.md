# Smart Addresser 自動ルール設計

## 概要
Smart Addresserを使って、asset-workflow.mdで定義したAddressable設定を自動化する。
フォルダ配置ルールに基づいてグループ・ラベル・アドレスを自動設定。

## アドレスルール（Address Rule）

| ルール名 | 対象パス | アドレスフォーマット | グループ |
|---------|---------|-------------------|---------|
| Player Sprites | `Assets/Sprites/Player/` | `sprite/player/{FileName}` | Player |
| Enemy Sprites | `Assets/Sprites/Enemy/` | `sprite/enemy/{FileName}` | Enemies |
| Stage Tiles | `Assets/Sprites/Tile/` | `tile/{SubDir}/{FileName}` | Stage_{StageId} |
| SFX | `Assets/Audio/SFX/` | `sfx/{SubDir}/{FileName}` | Audio_SFX |
| BGM | `Assets/Audio/BGM/` | `bgm/{SubDir}/{FileName}` | Audio_BGM |
| Prefabs | `Assets/Prefabs/` | `prefab/{SubDir}/{FileName}` | (サブディレクトリで分類) |
| UI | `Assets/UI/` | `ui/{SubDir}/{FileName}` | UI |
| Animations | `Assets/Animations/` | `anim/{SubDir}/{FileName}` | (キャラ単位で分類) |
| Materials | `Assets/Materials/` | `mat/{SubDir}/{FileName}` | Core |

## ラベルルール（Label Rule）

| ルール名 | 対象条件 | 付与ラベル |
|---------|---------|----------|
| Preload Assets | Core, Audio_SFXグループ | `preload` |
| On-Demand | 上記以外 | `on-demand` |
| Placeholder | `[PLACEHOLDER]`プレフィックス付きオブジェクト | `placeholder` |
| Debug | `Assets/Debug/`配下 | `debug` |
| Type: Sprite | `.png`, `.psd` | `sprite` |
| Type: Audio BGM | `Assets/Audio/BGM/` | `audio-bgm` |
| Type: Audio SFX | `Assets/Audio/SFX/` | `audio-sfx` |
| Type: Prefab | `.prefab` | `prefab` |
| Type: Animation | `.anim`, `.controller` | `animation` |
| Type: Material | `.mat` | `material` |

## グループルール（Group Rule）

| ルール名 | 対象条件 | グループ名 | パッキング |
|---------|---------|-----------|-----------|
| Core | `Assets/Materials/`, `Assets/UI/Common/` | Core | Pack Together |
| Player | `Assets/Sprites/Player/`, `Assets/Animations/Player/` | Player | Pack Together |
| Enemies | `Assets/Sprites/Enemy/`, `Assets/Prefabs/Enemy/` | Enemies | Pack Separately |
| Audio SFX | `Assets/Audio/SFX/` | Audio_SFX | Pack Together |
| Audio BGM | `Assets/Audio/BGM/` | Audio_BGM | Pack Separately |
| UI | `Assets/UI/` | UI | Pack Separately |
| Stage | `Assets/Sprites/Stage_{id}/` | Stage_{id} | Pack Together |

## セットアップ手順

1. Window > Smart Addresser を開く
2. Layout Rule で上記ルールを設定
3. Version Rule でステージ単位のバージョニングを設定
4. Tools > Apply Rules で一括適用
5. 新アセット追加時は自動でルール適用される

## 除外ルール

- `Assets/Plugins/` 配下は除外（有料アセット内部リソース）
- `Assets/ThirdParty/` 配下は除外
- `.cs` ファイルは除外
- `Editor/` フォルダは除外
