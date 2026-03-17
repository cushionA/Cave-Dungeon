# System: CompanionAI_Basic
Section: 1 — MVP

## 責務
常駐仲間AIの基本行動。プレイヤー追従、スタンス切替（4種）、連携ボタンによる支援魔法発動。

## 依存
- 入力: AICore（AIBrain基盤）、PlayerMovement（追従対象）、InputSystem（連携ボタン）
- 出力: 仲間の行動実行、支援魔法発動

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CompanionController | 仲間AI固有のロジック統括 | Yes |
| FollowBehavior | プレイヤー追従・リーシュ距離管理 | No |
| StanceManager | 4スタンスの切替・行動パラメータ変更 | No |
| CooperationButton | 連携ボタン入力 → 支援魔法発動 | No |
| CompanionMagicSlot | セットされた支援魔法の管理 | No |

## 追従ロジック
```
distance = |companion.position - player.position|

if distance < followDistance:
    → 待機（Idle）、戦闘があれば戦闘参加
elif distance < maxLeashDistance:
    → プレイヤー方向へ移動
else:
    → テレポート（プレイヤー近くにワープ）
```

### 追従パラメータ（CompanionBehaviorSetting）
```csharp
followDistance = 2.0f       // この距離以内なら追従停止
maxLeashDistance = 15.0f    // これ以上離れたらテレポート
supportHpThreshold = 0.5f  // プレイヤーHP50%以下で回復優先
```

## スタンス切替

| スタンス | 戦闘行動 | 追従距離 | 回復優先度 |
|---------|---------|---------|-----------|
| Aggressive | 積極攻撃、敵に接近 | 広め（5m） | 低 |
| Defensive | プレイヤー防衛、ガード多用 | 近め（2m） | 中 |
| Supportive | 回復・バフ優先、攻撃控え | 中（3m） | 高 |
| Passive | 戦闘しない、追従のみ | 近め（1.5m） | なし |

スタンスごとにAIBrainのActData配列の重みを動的調整:
```csharp
void ApplyStance(CompanionStance stance)
{
    // Aggressiveなら攻撃系ActDataのweightを2倍
    // Supportiveなら回復系ActDataのweightを3倍、攻撃系を0.3倍
    // Passiveなら全戦闘ActDataのweightを0
}
```

## 連携ボタンシステム

### セットアップ
プレイヤーがメニューで「連携スロット」に支援魔法をセット（最大1つ、Section2で拡張）

### 発動フロー
```
連携ボタン押下 → CompanionMagicSlot.GetSetMagic()
    → MP残量チェック（仲間のMP消費）
    → 魔法実行（支援/攻撃/回復）
    → クールタイム開始
```

### クールタイムと無料連携（Section2準備）
```csharp
public class CooperationButton
{
    float cooldownTimer;
    float cooldownDuration;   // 魔法ごとに異なる

    bool IsCooldownReady => cooldownTimer <= 0;

    void Activate()
    {
        Magic magic = companionMagicSlot.GetSetMagic();
        if (magic == null) return;

        // Section2: IsCooldownReady && AI自動発動なら MP無料
        // Section1: 常にMP消費
        if (!TryConsumeMp(magic.useMP)) return;

        ExecuteMagic(magic);
        cooldownTimer = cooldownDuration;
    }
}
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| プレイヤー追従 | 距離ベースの追従・テレポート | PlayMode | High |
| スタンス切替 | 4スタンス、ActData重み調整 | EditMode | High |
| 連携ボタン発動 | 支援魔法のワンボタン発動 | PlayMode | High |
| 連携スロット設定 | メニューから支援魔法をセット | EditMode | Medium |
| 戦闘AI（基本） | スタンスに応じた基本戦闘行動 | PlayMode | Medium |
| プレイヤーHP監視 | supportHpThreshold以下で回復行動 | EditMode | Medium |
| テレポート | リーシュ距離超過時のワープ | PlayMode | Low |

## 設計メモ
- AICore基盤の上で動作する。CompanionControllerはAIBrainのActData重みを操作するだけ
- Section2でAIRuleBuilderがActDataを動的に追加・条件設定可能にする拡張点
- 連携ボタンのクールタイム管理は、Section2の「クールタイム消化→MP無料」の土台
- 仲間のHPやMPはSoAコンテナで管理（プレイヤーと同じデータ構造）
