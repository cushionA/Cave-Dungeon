# Sprint: Section 3 — 世界の広がり

## 完了条件
ボスがフェーズ遷移しながら戦闘し、撃破でClearGateが開く。属性攻撃で環境パズル（氷壁溶解・機械起動等）を解ける。召喚魔法で一時仲間を2体まで呼び出せる。混乱魔術で敵を味方化して元仲間を攻撃させられる。新能力獲得後に過去エリアで隠し報酬を発見できる。

## 既存機能の活用
| 既存機能 | 対応方法 | 備考 |
|----------|---------|------|
| AICore (AIBrain/AIMode全般) | そのまま利用 | BossControllerがコンポジションで保持 |
| DamageSystem_StatusEffects | 拡張 | Confusion蓄積を追加 |
| GateSystem (4機能) | 拡張 | GateType.Elemental追加 |
| CompanionAI_FollowBehavior | そのまま利用 | 召喚獣の追従ロジック |
| MagicSystem_CastingFlow | 拡張 | MagicType.Summon分岐追加 |
| EnemySystem_SpawnManagement | そのまま利用 | ボスの雑魚召喚 |
| EnemySystem_DropTable/LootAndReward | そのまま利用 | ボス報酬 |
| MapSystem_MinimapAndWorld | そのまま利用 | バックトラックマーカー |
| SaveSystem_Core | そのまま利用 | 報酬回収状態永続化 |
| EquipmentSystem (AbilityFlag) | そのまま利用 | バックトラック能力判定 |

## 実装順序 — システム系

全25機能。25がシステム系、0がコンテンツ系。

### Layer 0（依存: Section 1-2のみ）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 1 | Common_Section3Types | Common | system | 5 | Common_SharedTypes | pending |

### Layer 1（← Layer 0）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 2 | BossSystem_PhaseManager | BossSystem | system | 5 | Common_Section3Types, AICore_ModeController | pending |
| 3 | ConfusionMagic_Accumulation | ConfusionMagic | system | 5 | Common_Section3Types, DamageSystem_StatusEffects | pending |
| 4 | BacktrackReward_Manager | BacktrackReward | system | 5 | Common_Section3Types, MapSystem, SaveSystem | pending |
| 5 | ElementalGate_Interaction | ElementalGate | system | 5 | Common_Section3Types, GateSystem_ConditionCheck | pending |

### Layer 2（← Layer 0-1）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 6 | BossSystem_Controller | BossSystem | system | 5 | BossSystem_PhaseManager, AICore_JudgmentLoop | pending |
| 7 | BossSystem_Arena | BossSystem | system | 4 | BossSystem_Controller, GateSystem_Registry | pending |
| 8 | ConfusionMagic_FactionSwitch | ConfusionMagic | system | 5 | ConfusionMagic_Accumulation, AICore_ConditionEvaluator | pending |
| 9 | ConfusionMagic_AIOverride | ConfusionMagic | system | 4 | ConfusionMagic_FactionSwitch, AICore_TargetSelector | pending |
| 10 | ElementalGate_Integration | ElementalGate | system | 4 | ElementalGate_Interaction, GateSystem_OpenClose, GateSystem_Registry | pending |
| 11 | BacktrackReward_Checker | BacktrackReward | system | 4 | BacktrackReward_Manager, EquipmentSystem_EquipUnequip | pending |
| 12 | SummonSystem_Manager | SummonSystem | system | 5 | Common_Section3Types, CompanionAI_FollowBehavior | pending |

### Layer 3（← Layer 0-2）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 13 | BossSystem_AddSpawn | BossSystem | system | 3 | BossSystem_Controller, EnemySystem_SpawnManagement | pending |
| 14 | BossSystem_Rewards | BossSystem | system | 4 | BossSystem_Controller, EnemySystem_DropTable | pending |
| 15 | ConfusionMagic_Duration | ConfusionMagic | system | 4 | ConfusionMagic_FactionSwitch | pending |
| 16 | ConfusionMagic_Limits | ConfusionMagic | system | 3 | ConfusionMagic_FactionSwitch | pending |
| 17 | SummonSystem_Controller | SummonSystem | system | 5 | SummonSystem_Manager, AICore_JudgmentLoop | pending |
| 18 | SummonSystem_Lifetime | SummonSystem | system | 4 | SummonSystem_Manager | pending |
| 19 | ElementalGate_MultiHit | ElementalGate | system | 3 | ElementalGate_Interaction | pending |
| 20 | ElementalGate_HintDisplay | ElementalGate | system | 3 | ElementalGate_Integration, MapSystem | pending |
| 21 | BacktrackReward_Reevaluation | BacktrackReward | system | 4 | BacktrackReward_Checker | pending |
| 22 | BacktrackReward_MapIntegration | BacktrackReward | system | 3 | BacktrackReward_Manager, MapSystem | pending |

### Layer 4（← Layer 0-3）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 23 | SummonSystem_MagicIntegration | SummonSystem | system | 4 | SummonSystem_Manager, MagicSystem_CastingFlow | pending |
| 24 | SummonSystem_PartyLimit | SummonSystem | system | 3 | SummonSystem_Manager | pending |
| 25 | BacktrackReward_Pickup | BacktrackReward | system | 3 | BacktrackReward_Checker | pending |

## asmdef配置マッピング

| asmdef | 対象機能 |
|--------|---------|
| Game.Core (既存拡張) | Common_Section3Types |
| Game.AI (既存拡張) | BossSystem全機能, SummonSystem全機能, ConfusionMagic全機能 |
| Game.World (既存拡張) | ElementalGate全機能, BacktrackReward全機能 |
| Game.Tests.EditMode (既存拡張) | 全EditModeテスト |

## 動作確認手順
全機能完了後、以下を確認:

1. **ボスフェーズ遷移テスト**: HP閾値でフェーズ1→2→3遷移、各フェーズでAIMode変化
2. **アリーナロックテスト**: ボスエリア侵入→出入口ロック→撃破→ClearGate開放
3. **ボス雑魚召喚テスト**: フェーズ遷移時に雑魚がスポーン
4. **ボス報酬テスト**: 撃破→EXP/通貨/アイテムドロップ
5. **属性ゲートテスト**: 炎攻撃で氷壁解除、雷で機械起動、マルチヒットギミック
6. **召喚テスト**: 召喚魔法→一時仲間出現→AI追従→寿命切れで消滅
7. **パーティ制限テスト**: 3体目召喚→最古の召喚獣が消えて新しいものに入れ替え
8. **混乱テスト**: 混乱蓄積→閾値→敵が味方化→元仲間を攻撃→時間切れで敵復帰
9. **混乱耐性テスト**: ボスに混乱→完全耐性で無効
10. **バックトラックテスト**: 壁蹴り獲得→マップマーカー出現→過去エリアで隠しアイテム回収

## 統計
- 総機能数: 25
- 総テスト数目安: ~101
- カテゴリ: system 25, content 0
- 推定実装順: Layer 0 (1) → Layer 1 (4) → Layer 2 (7) → Layer 3 (10) → Layer 4 (3)
