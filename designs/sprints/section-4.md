# Sprint: Section 4 — エンドコンテンツ

## 完了条件
本編クリア後にボスラッシュ・タイムアタック等のチャレンジモードが遊べる。スコア・タイムの自己ベストが記録される。仲間AIテンプレートの保存・適用・推薦が機能する。

## 既存機能の活用
| 既存機能 | 対応方法 | 具体API |
|----------|---------|---------|
| BossSystem (Section 3) | BossRushで連戦管理 | `BossControllerLogic.StartEncounter()`, `.OnBossDefeated` |
| EnemySystem (Section 2) | Survival Wave管理 | `GameEvents.OnEnemyDefeated` 購読 |
| PresetManager (Section 2) | テンプレートの内部基盤 | `PresetManager`, `CompanionAIConfig` |
| SaveSystem (Section 1) | 記録永続化 | `ISaveable { SaveId, Serialize(), Deserialize() }` |
| DamageSystem (Section 1) | スコア計算入力 | `GameEvents.OnDamageDealt`, `DamageResult` |
| GameEvents (R3) | イベント通信 | `Subject<T>` + `Fire*()` パターン |

## 旧設計からの変更点
- GameEvents を R3 Subject<T> パターンに統一（旧: `System.Action<T>` イベント）
- AITemplateData が CompanionAIConfig をそのまま内包（旧: AIMode[] 直接保持）
- AITemplates_ImportExport を除外（YAGNI）→ 全10機能に削減
- BossRush/Survival のAPI参照を具体化（旧: 抽象的な "BossSystem利用"）
- ChallengeRunner にダメージ集計を追加（OnDamageDealt購読）

## 実装順序 — システム系

全10機能。

### Layer 0（依存: Section 1-3のみ）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 1 | Common_Section4Types | Common | system | 4 | Common_SharedTypes | pending |

### Layer 1（← Layer 0）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 2 | ChallengeMode_Runner | ChallengeMode | system | 5 | Common_Section4Types, GameEvents | pending |
| 3 | ChallengeMode_Score | ChallengeMode | system | 4 | Common_Section4Types | pending |
| 4 | AITemplates_Manager | AdvancedAITemplates | system | 4 | Common_Section4Types, PresetManager, ISaveable | pending |

### Layer 2（← Layer 0-1）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 5 | ChallengeMode_Manager | ChallengeMode | system | 4 | Runner, Score, ISaveable | pending |
| 6 | ChallengeMode_BossRush | ChallengeMode | system | 3 | Runner, BossControllerLogic | pending |
| 7 | ChallengeMode_Survival | ChallengeMode | system | 3 | Runner, GameEvents.OnEnemyDefeated | pending |
| 8 | AITemplates_ApplyRevert | AdvancedAITemplates | system | 4 | AITemplates_Manager, GameEvents.FireCustomRulesChanged | pending |
| 9 | Leaderboard_RecordUpdate | Leaderboard | system | 4 | Score, ISaveable, GameEvents.FireNewRecord | pending |

### Layer 3（← Layer 0-2）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 10 | Leaderboard_Statistics | Leaderboard | system | 3 | Leaderboard_RecordUpdate | pending |
| 11 | AITemplates_Suggester | AdvancedAITemplates | system | 3 | AITemplates_Manager | pending |

## 動作確認手順
1. **チャレンジ進行テスト**: BossRush開始→BossControllerLogic.StartEncounter()→OnBossDefeated→次ボス→全撃破→結果画面
2. **タイムアタックテスト**: 制限時間内クリア→ChallengeScoreCalculator→ランク評価
3. **記録テスト**: クリア→LeaderboardManager.UpdateRecord()→FireNewRecord→再プレイ→新記録判定
4. **テンプレートテスト**: CompanionAIConfig保存→AITemplateManager適用→Revert→FireCustomRulesChanged
5. **推薦テスト**: ボス戦中にAITemplateSuggester→BossFightカテゴリ推薦

## 統計
- 総機能数: 11（旧: 11 → ImportExport除外で10 + 既存エントリ調整で11維持）
- 総テスト数目安: ~41
- カテゴリ: system 11, content 0
- 推定実装順: Layer 0 (1) → Layer 1 (3) → Layer 2 (5) → Layer 3 (2)
