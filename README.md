# SisterGame

骸骨騎士を操作し、相棒のシスターと魔物が暮らすダンジョンの奥を目指す3Dダンジョン探索アクションRPG。道中で助けたNPC魔物との縁が、シスターの呼べる援軍を増やしていく。

## 概要

| 項目 | 内容 |
|------|------|
| ジャンル | 3Dダンジョン探索アクションRPG |
| エンジン | Unity 6000.6.8f1 |
| プラットフォーム | PC・一人用（将来コンソール検討） |
| プレイ時間 | 本編約20時間を目安に試作で調整 |

### コアループ

少人数で探索し、NPC魔物の依頼や物語を進める。シスターの召喚に応じる種類が増えたら、1体ごとのコストを見て次の編成を選ぶ。魔物溜まりや指定ボス戦では援軍を加えて戦い、道が開いたら普段の仲間と探索を再開する。

### 探索で出会った魔物と戦場を分け合う

たとえば、坑道の工兵長を助けるとシスターがゴブリン工兵を呼べるようになる。道中は1体を連れ、魔物溜まりでは同じ工兵を複数呼んで側道を任せる。その間に騎士が橋を渡って奥の術師に迫る。シスターの支援は連携ボタンから頼める。プレイヤーが行動ルールを細かく設定できるのはシスターだけ。召喚魔物は作者が用意した行動で戦い、プレイヤーは指揮官を通じて簡単な指示を出す。[ゲームデザイン](designs/game-design.md)・[召喚システム設計](designs/systems/SummonSystem.md)

## アーキテクチャ

独自設計の高パフォーマンスアーキテクチャを採用。

| 設計方針 | 内容 |
|----------|------|
| SoA + SourceGenerator | キャラクターデータをフィールド別配列で管理。属性付与で自動生成 |
| GameManager中央ハブ | 全システムへの唯一の参照点。散在シングルトン排除 |
| ハッシュベースO(1)アクセス | `GameManager.Data.GetXxx(hashCode)` でGetComponent完全排除 |
| Ability拡張 | IAbilityインターフェースで行動を追加・組み合わせ |
| データ駆動 | 全バランス値をScriptableObjectで定義。ハードコーディング禁止 |
| R3リアクティブ | Subject<T> + Fire*() パターンでシステム間イベント通信 |

### Assembly構成

```
Game.Core  ←  GameCode(Game.Character/Combat/AI/World/Economy/UI)
                ├── Unity.InputSystem
                ├── UniTask
                ├── R3.Unity
                └── LitMotion
```

### レイヤー構成

```
[Input Layer]        — 入力受付、イベント発行
    ↓ events
[Game Logic Layer]   — メカニクス、ルール、状態管理（純粋C#クラス）
    ↓ data
[Presentation Layer] — MonoBehaviour統合、表示、アニメーション
```

## 既存プロトタイプの開発記録

以下は2Dプロトタイプの各セクション完了時点の記録。3Dの部屋で探索から援軍戦まで通して遊べることを、次の試作で確かめる。

**104機能実装済み / 536テスト全Pass**

| Section | 名称 | 概要 | 機能数 | 状態 |
|---------|------|------|--------|------|
| 1 | MVP: 戦闘と探索の基盤 | DataContainer, Movement, Equipment, Damage, Save等 | 35 | 実装済み |
| 2 | AI・仲間・連携 | AIBrain, 仲間AI, 連携スキル, 魔法, 敵AI, ゲート | 38 | 実装済み |
| 3 | 世界の広がり | ボス戦, 召喚, 混乱魔術, 属性ゲート, バックトラック | 25 | 実装済み |
| 4 | エンドコンテンツ | チャレンジモード, リーダーボード, AIテンプレート | 11 | 実装済み |

## プロジェクト構造

```
SisterGame/
├── Assets/
│   ├── MyAsset/
│   │   ├── Core/           # ゲームロジック（MonoBehaviour非依存）
│   │   │   ├── AI/         # AIBrain, ConditionEvaluator, AITemplates
│   │   │   ├── Combat/     # コンボ, チャージ, ActionExecutor
│   │   │   ├── Common/     # 共通型, Enum, Struct, Interface
│   │   │   ├── Damage/     # ダメージ計算, 状態異常, パリィ
│   │   │   ├── Equipment/  # 装備, 重量, ステータス計算
│   │   │   ├── GameManager/# GameManager, GameEvents(R3)
│   │   │   ├── Save/       # ISaveable, SaveManager
│   │   │   └── World/      # Boss, Challenge, Gate, Summon
│   │   ├── Runtime/        # MonoBehaviour統合レイヤー
│   │   └── UI/             # HUD, ダメージポップアップ
│   └── Tests/
│       ├── EditMode/       # 単体・結合テスト（120ファイル）
│       └── PlayMode/       # ゲームプレイテスト（3ファイル）
├── Architect/              # アーキテクチャ設計文書（8本）
├── designs/
│   ├── game-design.md      # GDD
│   ├── systems/            # システム設計書（37本）
│   ├── sprints/            # スプリント計画
│   └── asset-spec.json     # ワールド統一スケール仕様
├── tools/                  # Python開発ツール
├── config/                 # 設定ファイル
└── instruction-formats/    # AI出力テンプレート
```

## 開発ワークフロー

### TDD駆動開発

```
テスト作成(Red) → テストFail確認 → 実装(Green) → feature-db記録
```

### AI支援パイプライン

Claude Codeによる対話型開発パイプラインを採用。

```bash
/design-game        # GDD作成
/design-systems     # システム設計 + 機能分解 + feature-db登録（旧 plan-sprint 統合）
/create-feature     # 1機能ずつTDD実装
/run-tests          # テスト実行
```

### feature-db（機能管理）

```bash
python tools/feature-db.py summary    # 進捗サマリー
python tools/feature-db.py list       # 全機能一覧
python tools/feature-db.py get "名前"  # 個別確認
```

## セットアップ

### 必須環境

- Unity 6000.6.8f1
- Python 3.x（tools/用）
- Git

### 手順

1. リポジトリをクローン
2. Unity Hubで `SisterGame/` フォルダを開く
3. パッケージの自動解決を待つ
4. `Assets/Scenes/` から任意のシーンを開く

### Codex CloudでのUnity実行

Codex Cloudの環境編集で、このリポジトリのルートを作業ディレクトリとして次を実行する。Editor版は`ProjectSettings/ProjectVersion.txt`から取得する。

```bash
bash scripts/setup-codex-unity.sh
bash scripts/setup-codex-unity.sh --check
bash scripts/run-unity-tests.sh All
```

Install scriptには`bash scripts/setup-codex-unity.sh`を設定する。Start skillには、リポジトリ更新後に`--check`で依存と認証を確認し、必要なテストを`run-unity-tests.sh`から実行する手順を設定する。保存後の再公開は利用者が行う。新しいタスクは公開済みのファイルシステムから開始し、既存タスクには環境の再公開が反映されない。[Codex Cloud公式手順](https://learn.chatgpt.com/docs/environments/cloud-environments)

初回導入には次の準備が必要。

- Unity Editorの利用条件への同意を済ませる。スクリプトから同意する場合だけ`UNITY_ACCEPT_EDITOR_TERMS=1`を明示する。
- クラウド上のマシンでUnityライセンスを認証する。既存認証は`UNITY_LICENSE_MODE=existing`、正式なfloating設定済みなら`floating`、同じマシン用のライセンスファイルを読み込む場合は`file`と`UNITY_LICENSE_FILE`を指定する。Windowsの認証ファイルを別マシンへコピーして使わない。[Unityライセンス手順](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/auth-license-cloud.md)
- 許諾済みのOdin、AnyPortrait、SensorToolkit、ProCamera2D、MasterAudio、Pixel CrushersのCommon・Dialogue System・Quest Machine・LoveHateをクラウドの非公開領域へ配置する。OdinにはAttributesに加えてInspector Editor・Validator EditorのDLLが必要。`Assets/`からの相対パスと`.meta`を保ったtarアーカイブを用意した場合は`UNITY_PRIVATE_ASSETS_ARCHIVE`で指定できる。実体はGitへ追加しない。NuGet依存は`Assets/packages.config`から復元する。
- Unityのダウンロード・認証・UPM、GitHubとNuGetへのネットワークを許可する。HTTP secretはHTTPS先で代理置換される値なので、プロセスが直接読むライセンスファイルの内容として扱わない。

各アセットはUnity 6.6（6000.6.8f1）に対応した版を使う。ローカルだけの互換修正はGitから復元されないため、投入する非公開アーカイブにも反映する。セットアップは必要ファイルの有無を判定し、API互換性はEditorのコンパイルとテストで確認する。

2026-11-06にはOdin Inspector/Validator 4.1.0.0、AnyPortrait 1.7.0と正式UPM更新後の環境で、EditMode 2,097件・PlayMode 223件がすべて成功した。第三者アセットの実体はGitへ含めていない。

テストのXML・Editorログ・CLI結果は実行ごとに別ディレクトリへ保存する。`UNITY_TEST_FILTER`で対象を絞り、`UNITY_TEST_TIMEOUT_SECONDS`で制限時間、`UNITY_TEST_RESULTS_DIR`で保存先を指定できる。XML未生成・0件・失敗は成功として扱わない。ローカルのシェル検証だけでクラウド上のEditor起動成功とは扱わず、環境編集で認証とテストを確認してから再公開する。

## 主要な外部パッケージ

| パッケージ | 用途 |
|-----------|------|
| R3 | リアクティブイベント（Subject<T>） |
| UniTask | async/await対応 |
| LitMotion | Tweenアニメーション |
| Addressables | アセット管理 |
| Input System | 入力管理 |
| AnyPortrait | 既存プロトタイプの2Dアニメーション |
| SensorToolkit | 検知・認識システム |

## ドキュメント

| ドキュメント | 場所 | 内容 |
|-------------|------|------|
| アーキテクチャ設計 | `Architect/` | SoA, GameManager, 戦闘, AI等の詳細設計 |
| システム設計書 | `designs/systems/` | 各システムのAPI・データフロー定義 |
| GDD | `designs/game-design.md` | ゲーム全体の設計 |
| コード規約 | `.claude/rules/` | 命名, テスト, Git, アセット管理の規約 |

詳細は [Wiki](../../wiki) を参照。

## ライセンス

MIT
