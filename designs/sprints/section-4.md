# Sprint: Section 4 — エンドコンテンツ

## 完了条件
本編クリア後にボスラッシュ・タイムアタック等のチャレンジモードが遊べる。スコア・タイムの自己ベストが記録される。仲間AIテンプレートの保存・適用・推薦が機能する。

## 既存機能の活用
| 既存機能 | 対応方法 | 備考 |
|----------|---------|------|
| BossSystem (Section 3) | そのまま利用 | ボスラッシュのボスデータ再利用 |
| EnemySystem (Section 2) | そのまま利用 | Survival Waveのスポーン |
| AIRuleBuilder (Section 2) | そのまま利用 | テンプレート = AIMode[]のスナップショット |
| SaveSystem (Section 1) | そのまま利用 | ISaveable実装で記録永続化 |
| DamageSystem (Section 1) | そのまま利用 | スコア計算の入力データ |

## 実装順序 — システム系

全11機能。

### Layer 0（依存: Section 1-3のみ）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 1 | Common_Section4Types | Common | system | 4 | Common_SharedTypes | pending |

### Layer 1（← Layer 0）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 2 | ChallengeMode_Runner | ChallengeMode | system | 5 | Common_Section4Types | pending |
| 3 | ChallengeMode_Score | ChallengeMode | system | 4 | Common_Section4Types | pending |
| 4 | AITemplates_Manager | AdvancedAITemplates | system | 4 | Common_Section4Types, AIRuleBuilder | pending |

### Layer 2（← Layer 0-1）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 5 | ChallengeMode_Manager | ChallengeMode | system | 4 | ChallengeMode_Runner, ChallengeMode_Score | pending |
| 6 | ChallengeMode_BossRush | ChallengeMode | system | 3 | ChallengeMode_Runner, BossSystem | pending |
| 7 | ChallengeMode_Survival | ChallengeMode | system | 3 | ChallengeMode_Runner, EnemySystem | pending |
| 8 | AITemplates_ApplyRevert | AdvancedAITemplates | system | 4 | AITemplates_Manager | pending |
| 9 | Leaderboard_RecordUpdate | Leaderboard | system | 4 | ChallengeMode_Score | pending |

### Layer 3（← Layer 0-2）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 10 | Leaderboard_Statistics | Leaderboard | system | 3 | Leaderboard_RecordUpdate | pending |
| 11 | AITemplates_Suggester | AdvancedAITemplates | system | 3 | AITemplates_Manager | pending |

## 動作確認手順
1. **チャレンジ進行テスト**: BossRush開始→ボス撃破→次ボス→全撃破→結果画面
2. **タイムアタックテスト**: 制限時間内クリア→スコア計算→ランク評価
3. **記録テスト**: クリア→記録保存→再プレイ→新記録判定
4. **テンプレートテスト**: AIルール保存→テンプレート適用→元に戻す
5. **推薦テスト**: ボス戦中にBossFightテンプレートが推薦される

## 統計
- 総機能数: 11
- 総テスト数目安: ~41
- カテゴリ: system 11, content 0
- 推定実装順: Layer 0 (1) → Layer 1 (3) → Layer 2 (5) → Layer 3 (2)
