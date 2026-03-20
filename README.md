# SisterGame

2Dメトロイドヴァニア（HD 2D）アクションゲーム。仲間AIカスタムと連携アクションを核とした探索・戦闘体験を提供する。

## 概要

| 項目 | 内容 |
|------|------|
| ジャンル | 2Dメトロイドヴァニア |
| エンジン | Unity 6000.3.9f1 |
| プラットフォーム | PC（将来コンソール検討） |
| プレイ時間 | 本編約20時間 + エンドコンテンツ |

### コアループ

```
探索 → 戦闘 → 連携発見 → 成長・カスタム → 新エリア開通 → バックトラック → 探索...
```

### 面白さの核: 仲間AIカスタム

- **カジュアル層**: 連携ボタン1つで仲間の支援魔法を発動
- **やり込み層**: 条件付きAIルールを構築し、クールタイム最適管理で連携がMP無料に
- **エンドコンテンツ**: ボスラッシュ・タイムアタックでAI最適化の真価が問われる

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

## 開発状況

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
/design-systems     # システム設計
/plan-sprint        # スプリント計画 + feature-db登録
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

- Unity 6000.3.9f1
- Python 3.x（tools/用）
- Git

### 手順

1. リポジトリをクローン
2. Unity Hubで `SisterGame/` フォルダを開く
3. パッケージの自動解決を待つ
4. `Assets/Scenes/` から任意のシーンを開く

## 主要な外部パッケージ

| パッケージ | 用途 |
|-----------|------|
| R3 | リアクティブイベント（Subject<T>） |
| UniTask | async/await対応 |
| LitMotion | Tweenアニメーション |
| Addressables | アセット管理 |
| Input System | 入力管理 |
| AnyPortrait | 2Dアニメーション |
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
