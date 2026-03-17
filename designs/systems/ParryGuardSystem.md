# System: ParryGuardSystem
Section: 1 — MVP

## 責務
ガード・ジャストガード・ガードブレイクの判定。ガード入力からガード状態管理、ジャストガードのタイミングウィンドウ管理を行う。

## 依存
- 入力: InputSystem（guardHeld）、EquipmentSystem（GuardStats, justGuardTiming）、DamageSystem（ガード判定要求）
- 出力: GuardResult、OnGuardEventイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| GuardAbility | ガード状態管理（IAbility） | Yes |
| JustGuardWindow | ジャストガードのタイミングウィンドウ管理 | No |
| GuardFeedback | ガード関連エフェクト・SE再生 | Yes |

## ガード判定フロー

### 1. ガード開始
```
ガードボタン押下 → GuardAbility.Execute()
    → ガードモーション開始
    → JustGuardWindow.Start(startTime, duration)
```

### 2. ジャストガードウィンドウ
```
[ガード開始] ─── [justGuardStartTime] ─── [JG判定開始] ─── [justGuardDuration] ─── [JG判定終了] ─── [通常ガード継続]
                                          ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                                          この間に攻撃を受けるとジャストガード
```
- `justGuardStartTime`: ガード開始からJG判定開始までの遅延（盾依存）
- `justGuardDuration`: JG判定の持続時間（盾依存）
- 例: 小盾 → startTime=0.1s, duration=0.15s（早く出て長い）
- 例: 壁盾 → startTime=0.3s, duration=0.05s（遅くて短い）

### 3. 攻撃受け時の判定
```csharp
GuardResult JudgeGuard(DamageData data, int defenderHash)
{
    ref EquipmentStatus equip = ref GetEquipmentStatus(defenderHash);

    // ガードしていない
    if (!isGuarding) return GuardResult.NoGuard;

    // 背面攻撃チェック（ガード方向と攻撃方向）
    if (IsBackAttack(data, defenderHash)) return GuardResult.NoGuard;

    // ジャストガードチェック
    if (justGuardWindow.IsActive && !data.feature.HasFlag(AttackFeature.JustGuardImmune))
    {
        // ジャストガード抵抗による効果
        float resistance = data.justGuardResistance;
        float armorDamage = data.armorBreakValue * (1f - resistance / 100f);
        // armorDamage処理後、JustGuard成功
        return GuardResult.JustGuard;
    }

    // 通常ガード: スタミナ消費チェック
    float staminaCost = data.armorBreakValue / (equip.finalGuardStats.guardStrength / 100f);
    if (CanConsumeStamina(defenderHash, staminaCost))
    {
        ConsumeStamina(defenderHash, staminaCost);
        return GuardResult.Guarded;
    }

    // スタミナ不足 → ガードブレイク
    return GuardResult.GuardBreak;
}
```

### 4. ジャストガード抵抗の仕組み
```
justGuardResistance = 0   → アーマー削り100% → 一撃で怯み
justGuardResistance = 50  → アーマー削り50%  → 2回で怯み
justGuardResistance = 90  → アーマー削り10%  → 10回で怯み
justGuardResistance = 100 → アーマー削り0%   → 怯まない（実質JG不可にする必要あり）

JustGuardImmune flag → ジャストガード判定自体をスキップ
```

## ジャストガードフィードバック（エフェクト・SE）

### ジャストガード成功時
```
JustGuard成功 → GuardFeedback.OnJustGuard()
    1. 微小ヒットストップ（3-5フレーム、Time.timeScale=0.1）
    2. パーティクルエフェクト: 盾衝突点に小さい光の破裂（白/青、0.2秒）
    3. SE: 金属的な高音の弾き音（"sfx/guard/just-guard"）
    4. カメラシェイク: 微小（amplitude=0.05, duration=0.1s）
    5. 攻撃者にスタガー開始（反撃チャンス）
```

### 通常ガード成功時
```
Guard成功 → GuardFeedback.OnGuard()
    1. SE: 鈍い防御音（"sfx/guard/block"）
    2. パーティクル: 小さい火花（0.1秒）
    3. スタミナバー点滅
```

### ガードブレイク時
```
GuardBreak → GuardFeedback.OnGuardBreak()
    1. SE: 盾が弾かれる重い音（"sfx/guard/break"）
    2. パーティクル: 盾が光って砕ける演出（0.3秒）
    3. カメラシェイク: 中程度
    4. 行動不能スタン開始（キャラクターが大きく仰け反る）
```

### ジャストガード不可攻撃の警告
敵がJustGuardImmune攻撃を繰り出す際に、プレイヤーに視覚的な警告を表示する。
```
敵のJustGuardImmune攻撃の予備動作開始
    → 攻撃者の武器/体に赤い警告エフェクト（パーティクル or SpriteGlow）
    → 警告SE: 低い不穏な音（"sfx/warning/unblockable"）
    → プレイヤーは回避を選択すべきシグナル
```

### フィードバック定義（ScriptableObject）
```csharp
[CreateAssetMenu]
public class GuardFeedbackData : ScriptableObject
{
    [Header("ジャストガード")]
    public GameObject justGuardEffect;       // パーティクルプレハブ
    public AudioClip justGuardSE;
    public float hitStopDuration;            // ヒットストップ時間
    public float hitStopTimeScale;           // ヒットストップ中のTimeScale
    public float cameraShakeAmplitude;
    public float cameraShakeDuration;

    [Header("通常ガード")]
    public GameObject guardEffect;
    public AudioClip guardSE;

    [Header("ガードブレイク")]
    public GameObject guardBreakEffect;
    public AudioClip guardBreakSE;
    public float breakStunDuration;

    [Header("JG不可攻撃の警告")]
    public GameObject unblockableWarningEffect;
    public AudioClip unblockableSE;
    public float warningLeadTime;            // 攻撃の何秒前から表示するか
}
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| ガード状態管理 | ボタン押下→ガード開始/終了 | PlayMode | High |
| ジャストガード判定 | タイミングウィンドウ内の攻撃検出 | EditMode | High |
| ガードブレイク判定 | スタミナ不足時のガード崩壊 | EditMode | High |
| 背面攻撃判定 | ガード方向と攻撃方向の比較 | EditMode | High |
| JG抵抗計算 | justGuardResistance → アーマー削り率 | EditMode | High |
| JG不可攻撃 | JustGuardImmune → JG判定スキップ | EditMode | Medium |
| ガード方向（前面のみ） | 正面からの攻撃のみガード有効 | EditMode | Medium |
| JG成功エフェクト・SE | ヒットストップ+パーティクル+弾き音 | PlayMode | High |
| 通常ガードSE | 防御音+火花 | PlayMode | Medium |
| ガードブレイク演出 | 砕け演出+重い音+スタン | PlayMode | High |
| JG不可攻撃警告 | 赤い警告エフェクト+不穏SE（敵予備動作時） | PlayMode | High |

## 設計メモ
- 参考コードのParryAbility + MyHealthのGuardCheck()をリファクタ
- ジャストガードの名称は「パリィ」ではなく「ジャストガード」に統一（パリィは参考コードの名残）
- ガード中はスタミナ自然回復が停止
- ガードブレイク時は一定時間行動不能（スタン）
- 強化ガード（参考コードのguardPower×1.3）は一旦保留。必要なら追加
