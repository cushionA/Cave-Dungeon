# System: CooldownReward
Section: 2 — AI・仲間・連携

## 責務
連携アクションのクールタイム消化報酬（MP無料発動）のバランス管理とUIフィードバック。実行判定ロジック自体はCoopAction.CoopCooldownTrackerに統合されている。

## 依存
- 入力: CoopAction（CoopCooldownTrackerの状態）、AIRuleBuilder（将来拡張: AIルール条件成立での追加報酬）
- 出力: UIフィードバック指示、バランスパラメータ

## 注記: CoopActionとの関係
クールタイムのMP無料判定ロジックは `CoopAction.CoopCooldownTracker` に実装されている。
CooldownRewardシステムは以下に焦点を当てる:
1. UIフィードバック（クールタイム完了表示、MP無料発動エフェクト）
2. バランスパラメータの外出し管理
3. 将来拡張: AIRuleBuilderのカスタムルール条件成立で追加ボーナス

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| RewardFeedback | MP無料発動時/クールタイム完了時のUIフィードバック | No |
| RewardConfig | クールタイム・報酬のバランスパラメータ（ScriptableObject） | No |

## クールタイムの挙動（CoopAction.CoopCooldownTracker で実行）

```
タイムライン:
t=0   連携発動（クールタイム消化済み → MP無料、クールタイム15秒開始）
t=5   連携発動（クールタイム未消化 → MP消費、タイマー残り10秒のまま）
t=10  連携発動（クールタイム未消化 → MP消費、タイマー残り5秒のまま）
t=15  クールタイム完了 → RewardFeedback.OnCooldownReady()
t=16  連携発動（クールタイム消化済み → MP無料 + RewardFeedback.OnFreeActivation()）
```

重要: MP消費で発動してもクールタイムタイマーはリセットも一時停止もしない。

## フィードバック

### MP無料発動時
- UIエフェクト: 連携ボタン周辺に「FREE」表示 + 金色フラッシュ
- SE: 通常連携とは異なるリッチSE

### クールタイム完了時
- UIアイコン: 連携スロットのアイコンが輝く
- SE: 小さなチャイム音

## 二重報酬構造（GDDコアコンセプト）

```
カジュアル層:
  連携ボタン → コンボ連打 → 仲間のMP消費
  → シンプルに遊べる。MPが尽きたら待つ

やり込み層:
  クールタイムを待って発動 → MP無料でコンボ全段無料
  → MP管理不要で連携が使い放題
  → さらにAIルール構築と組み合わせて最適化（将来拡張）
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Cooldown_Feedback | クールタイム完了・MP無料発動のUI/SE通知 | EditMode | Medium |
| Cooldown_BalanceConfig | クールタイム・報酬パラメータ設定（ScriptableObject） | EditMode | Medium |

## 設計メモ
- MP無料判定ロジック自体はCoopAction.CoopCooldownTrackerに実装済み
- このシステムはフィードバックとバランス管理に特化
- Section4のAdvancedAITemplatesで「AIルール条件成立+クールタイム消化→追加ボーナス」の拡張ポイント
