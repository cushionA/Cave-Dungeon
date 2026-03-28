# Unity Game Make Pipeline — SisterGame

## 環境
- Unity: `C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe`
- Project: `C:\Users\tatuk\Desktop\GameDev\SisterGame`
- Feature DB: `python tools/feature-db.py <command>` (init/add/update/get/list/assets/add-asset/bind/summary)

## プロジェクト概要
- 2Dアクションゲーム（サイドスクロール）
- 独自アーキテクチャ: SoA + SourceGenerator、GameManager中央ハブ、ハッシュベースO(1)データアクセス
- コーギーエンジンからの脱却・自前設計
- 設計文書: `Architect/` ディレクトリに6本のアーキテクチャドキュメント

## アーキテクチャ文書
- `Architect/00_アーキテクチャ概要.md` — 全体構成・GameManager・設計方針
- `Architect/01_データコンテナとソースジェネレーター.md` — SoAコンテナ、SourceGenerator、ハッシュアクセス
- `Architect/02_ベースキャラクターとアクションシステム.md` — BaseCharacter、HP/MP/スタミナ、Abilityコンポーネント拡張
- `Architect/03_武器・攻撃・戦闘システム.md` — 武器変更、コンボ、チャージ、ジャンプ攻撃
- `Architect/04_ダメージ・被弾システム.md` — ダメージ計算、パリィ、ガード、多属性
- `Architect/05_AIシステム.md` — ルールベースAI、仲間AI、認識システム
- `Architect/06_情報クラス群.md` — CharacterInfo、AttackInfo、AIInfo の詳細定義
- `Architect/参考コード/` — 既存コードの参考資料

## 将来タスク管理
- PRレビューや実装中に出た「今ではないが後で対応すべきタスク」は `docs/FUTURE_TASKS.md` に記録する
- カテゴリ: パフォーマンス / 設計改善 / バリデーション / 統合待ち
- 対応完了したらチェックを入れてコミット

## 原則
- TDD: テスト作成 → 実装 → テスト通過 を1セットとして記録する
- 仮素材方式: アセット未準備の場合はプレースホルダーで進め、参照を後から差し替え可能にする
- テンプレート活用: 複雑なGameObjectはテンプレートから派生させる
- 修正容易性: LLM出力は人間が修正しやすい構造（小さなクラス、明確な命名、設定の外出し）
- アーキテクチャ準拠: `Architect/` の設計文書に従う（SoA、GameManager中央ハブ、ハッシュベースアクセス）

## ログ規約
- AI用ログ: `AILogger.Log()` — Editor Buildのみ有効、LLMが状態把握に使用
- 人間用ログ: `Debug.Log()` — 通常のUnityログ
- `ENABLE_AI_LOGGING` Scripting Define Symbolで切替

## テスト
- 全機能にEdit Modeテスト必須、ゲームプレイ機能にはPlay Modeテスト追加
- テスト名: `[機能名]_[条件]_[期待結果]`
- テスト完了時は `python tools/feature-db.py update` で記録
- 詳細: `.claude/rules/test-driven.md`

## アセット管理
- プレースホルダー使用時は `[PLACEHOLDER]` プレフィックス付きGameObject名
- 本番アセット配置後に `/bind-assets` で参照バインド実行
- 詳細: `.claude/rules/asset-workflow.md`

## コード規約・アーキテクチャ
- コード規約: `.claude/rules/unity-conventions.md`
- アーキテクチャ: `.claude/rules/architecture.md`（SoA、GameManager中央ハブ、Ability拡張）
- アーキテクチャ詳細: `Architect/` ディレクトリの設計文書群

## Git運用
- 詳細: `.claude/rules/git-workflow.md`
- コミットメッセージは**必ず日本語タイトル**、形式: `[種類](範囲): 日本語タイトル`
- コミット後は必ずプッシュ
- `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` を付与
- ブランチ: main / feature / bugfix / hotfix / refactor
- `.meta` ファイルはアセットと必ずセットでコミット

## 用語定義
- **セクション**: GDDの論理的な単位。`/design-game` で分割される（例: セクション1=MVP）
- **スプリント**: セクションの実装計画。`/plan-sprint` で生成される
- **機能 (Feature)**: `/create-feature` で実装可能な最小単位（テスト5個以内）
- **システム**: 機能の集合体（例: PlayerSystem = Movement + Combat + Health）
- **ステージ**: ゲーム内のレベル/マップデータ（`/design-stage` で設計）
- **シーン**: Unityの.unityファイル。ステージを含むUnity上の実体
- **システム系**: 他の機能から使われる汎用処理（入力、物理、UI基盤等）
- **コンテンツ系**: システムを使うゲーム固有データ（敵配置、ステージ構成等）

## パイプラインの責任範囲
1. **設計補助** — 対話型要件整理、ジャンル調査→不足機能提案、ワールド設定、共通設計、asmdef設計、既存機能との照合
2. **機能単位の実装と管理** — TDD実装、feature-db管理、重複検出、コードレビュー・リファクタリング
3. **テストの設計と実行** — 単体/結合テスト、MCP経由テスト実行、コンソール監視、MLプレイテスト
4. **その他制作補助** — HTMLマップ案、バランスシート、draw.io図、Animator定義、UI構造定義、MCP経由シーン検証、デバッグ支援、Git操作、ドキュメント生成、ビルドスクリプト、パラメータ設計、設定ファイル生成

### パイプラインがやらないこと
- タイル配置によるステージ自動生成（人間が主導）
- Unityシーンの自動構築（MCP経由の検証・補助のみ）
- ユーザー確認なしの自律進行

## 開発フロー（対話型・セクション単位）
- `/build-pipeline <コンセプト>` で設計→計画→実装を**対話しながら**進行
- `/build-pipeline continue` で中断したパイプラインを再開
- 進行状態: `designs/pipeline-state.json` で追跡
- 各フェーズでユーザー確認を挟む（自動で全て決めない）
- 個別実行も可能:
  1. `/design-game` → 対話型GDD作成 + ワールド設定 + ジャンル調査
  2. `/design-systems section-1` → 共通設計 + asmdef設計 + システム設計書
  3. `/plan-sprint section-1` → 重複チェック + 機能分解 + feature-db登録
  4. `/create-feature` → 1機能ずつTDD実装
  5. セクション1完了後 → `/design-systems section-2` で次へ進む

## ディレクトリ構成
- `Assets/MyAsset/` — ゲームコード（GameCode.asmdef）
- `Assets/ODCGenerator/` — SourceGenerator DLL
- `Assets/Scenes/` — Unityシーン
- `Architect/` — アーキテクチャ設計文書
- `instruction-formats/` — AI向け構造化指示フォーマット
- `tools/` — Pythonユーティリティ（feature-db等）
- `designs/` — 設計書・ステージデータ・スプリント計画
- `config/` — 設定ファイル

## テンプレート使用
- GameObjectを新規作成する前に `template-registry.json` を確認（存在する場合）
- 詳細: `.claude/rules/template-usage.md`

## アセット仕様（ワールド統一スケール）
- `designs/asset-spec.json` に画面サイズ、タイルサイズ、PPU、キャラ寸法等を一元管理
- `/design-game` で基礎部分（画面、カメラ、タイル、PPU）を設定
- `/design-systems` でプレイヤー能力（ジャンプ距離等）やスプライトカテゴリを追加
- `/design-stage` はこの仕様を参照してギャップ幅やチャンクサイズを決定
- アセット作成時はこの仕様に従って正しいサイズで作成する

## ステージ設計（2Dサイドスクロール）
- `/design-stage` → ステージデータ生成（`designs/stages/` に出力）
- `/adjust-stage` → ステージ調整+学習記録
- ステージフォーマット: `instruction-formats/stage-layout-2d.md`
- 学習ノート: `designs/stage-design-notes.md` — 過去の調整ルール（`/design-stage` 時に必ず参照）
- ギミックレジストリ: `designs/gimmick-registry.json` — 成功パターンの蓄積

## イベントシーン
- `/create-event` → 会話データ+キャラクター指定からイベントシーン構築
- フォーマット: `instruction-formats/event-scene.md`
- ステージ内トリガー: `event_zone` オブジェクトタイプ（stage-layout-2dで配置）
- アニメーション設定は人間が行う（空トラックを作成）

## フラグ管理（グローバル + マップローカル分離）
- グローバルフラグ: ストーリー進行、好感度、実績等
- マップローカルフラグ: イベント再生済み、宝箱開封等
- マップ遷移時にローカルフラグを自動切替

## MLプレイテスト
- `/test-game-ml <stage_id>` → ML-Agentsで自動プレイテスト実行・分析
- レポート出力先: `Assets/PlaytestReports/`
- 分析結果は `stage-design-notes.md` にフィードバック
- `python tools/analyze-playtest.py <report>.json` でClaude Agent SDKが自動分析・設計ノート更新

## アセット自動生成
- 設定: `config/asset-gen.json`（音声ライブラリパス、Kaggle設定）
- 画像生成: `python tools/generate-images.py` — KaggleでFLUX.2バッチ生成
- 音声マッチング: `python tools/asset-index.py` — 手持ちライブラリからLLM選定
- `/generate-assets` で画像生成+音声マッチングを一括実行
- `/index-assets` でインデックス管理

## AnimatorController自動生成
- フォーマット: `instruction-formats/animator-state-machine.md`
- AIはテキストフォーマットを生成するだけ。AnimatorBuilderがUnityアセットに変換
- Motion(AnimationClip)のバインドは手動または`/bind-assets`で実行

## 指示フォーマット
- `instruction-formats/animator-state-machine.md`
- `instruction-formats/scene-layout.md`
- `instruction-formats/stage-layout-2d.md`
- `instruction-formats/ui-structure.md`
- `instruction-formats/asset-request.md`
- `instruction-formats/feature-spec.md`
- `instruction-formats/event-scene.md`
