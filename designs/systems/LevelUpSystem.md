# System: LevelUpSystem
Section: 1 — MVP

## 責務
経験値蓄積、レベルアップ判定、能力値振り分けUI。レベルアップ時にステータスポイントを獲得し、プレイヤーがビルド方向を決定する。

## 依存
- 入力: EnemySystem（経験値獲得）、DataContainer
- 出力: レベルアップ、能力値更新、OnLevelUp/OnExpGainedイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| LevelManager | 経験値・レベル管理 | No (GameManager保持) |
| StatAllocator | 能力値振り分けロジック | No |
| LevelUpUI | 振り分けUI | Yes |

## レベルアップフロー
```
敵撃破 → OnExpGained(expAmount)
    → LevelManager.AddExp(amount)
    → 累計EXP >= 次レベル必要EXP
    → レベルアップ！
    → statPoints += pointsPerLevel (例: 3ポイント)
    → OnLevelUp発火
    → セーブポイントでStatAllocatorを使って振り分け
```

## 能力値振り分け
```
セーブポイント → 「レベルアップ」メニュー
    → 未使用statPointsを表示
    → STR / DEX / INT / VIT / MND / END に振り分け
    → 確定 → ステータス再計算 → EquipmentSystem再計算トリガー
```

### 能力値→ステータス影響（Common_Section1 §6準拠）
- STR → 物理攻撃力, 光攻撃力(部分), 最大重量上限
- DEX → 物理攻撃力(部分), 闇攻撃力(部分), クリティカル
- INT → 火攻撃力, 雷攻撃力, 光(部分), 闇(部分), 最大MP
- VIT → 最大HP, 物理防御
- MND → 最大MP(部分), 魔法防御, 状態異常耐性
- END → 最大スタミナ, スタミナ回復速度, 最大重量上限(部分)

## 経験値テーブル
- EXP必要量はAnimationCurveで定義（レベルごとの必要EXP曲線）
- バランスシートで調整

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 経験値蓄積 | 敵撃破時のEXP加算 | EditMode | High |
| レベルアップ判定 | 累計EXP vs テーブル | EditMode | High |
| 能力値振り分け | ポイント割り当てとステータス再計算 | EditMode | High |
| 振り分けUI | セーブポイントでの振り分け画面 | PlayMode | Medium |
| ステータス再計算 | 能力値変更→全ステータス更新 | EditMode | Medium |

## 設計メモ
- 振り分けはセーブポイントでのみ可能（レベルアップ即時ではない）
- リスペック（振り直し）は対象外（Section3以降で検討）
- 仲間AIのレベルアップはプレイヤーと自動同期（独立振り分けなし）
