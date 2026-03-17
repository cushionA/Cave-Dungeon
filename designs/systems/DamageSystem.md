# System: DamageSystem
Section: 1 — MVP

## 責務
ダメージ計算の中核。攻撃側のDamageDataと防御側のステータスから最終ダメージを算出し、HP/アーマー/状態異常を適用する。

## 依存
- 入力: DamageData（WeaponSystemが生成）、DataContainer（防御ステータス）
- 出力: DamageResult、HP変更、状態異常蓄積、OnDamageDealtイベント

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| DamageCalculator | ダメージ計算ロジック（純粋関数） | No |
| DamageReceiver | ダメージ受付・HP適用 | Yes (IDamageable) |
| StatusEffectManager | 状態異常の蓄積・発症・持続管理 | No (GameManager保持) |
| ArmorSystem | アーマー値の管理・削り・回復 | No |
| KnockbackApplier | ノックバック力の適用 | Yes |

## ダメージ計算式（参考コード準拠）

### 1. 各属性ダメージ算出
```csharp
static float CalcElementDamage(float atk, float def, float motionValue)
{
    if (atk <= 0) return 0;
    return (atk * atk * motionValue) / (atk + def);
}
```

### 2. 合計ダメージ
```csharp
static int ComputeTotalDamage(DamageData data, ref CharacterDefStatus defStatus,
                               ref DefenseMultiplier atkMul, ref DefenseMultiplier defMul)
{
    float total = 0;
    total += CalcElementDamage(data.damage.physical, defStatus.physical, data.motionValue);
    total += CalcElementDamage(data.damage.fire, defStatus.fire, data.motionValue);
    total += CalcElementDamage(data.damage.thunder, defStatus.thunder, data.motionValue);
    total += CalcElementDamage(data.damage.light, defStatus.light, data.motionValue);
    total += CalcElementDamage(data.damage.dark, defStatus.dark, data.motionValue);

    total *= atkMul.allMultiplier * defMul.allMultiplier;

    if (defStatus.isStunned)
        total *= 1.2f;

    return Mathf.CeilToInt(total);
}
```

### 3. ガードダメージ計算
```csharp
static int ComputeGuardDamage(DamageData data, ref GuardStats guard,
                               ref DefenseMultiplier atkMul, ref DefenseMultiplier defMul)
{
    float total = 0;
    total += CalcElementDamage(data.damage.physical, 0, data.motionValue)
             * (100f - guard.physicalCut) * 0.01f;
    total += CalcElementDamage(data.damage.fire, 0, data.motionValue)
             * (100f - guard.fireCut) * 0.01f;
    // ... 各属性同様

    total *= atkMul.allMultiplier * defMul.allMultiplier;
    return Mathf.CeilToInt(total);
}
```

## 状態異常システム

### 蓄積モデル
```
蓄積量 = 装備の状態異常値 + モーションの状態異常値
ヒットごとに対象の蓄積カウンターに加算
蓄積カウンター >= 閾値 → 発症
発症後: 効果適用 + 蓄積リセット + 耐性一時上昇
```

### StatusEffectManager
```csharp
public class StatusEffectManager
{
    // 蓄積
    void Accumulate(int targetHash, StatusEffectId effect, float amount);

    // 発症チェック
    bool CheckThreshold(int targetHash, StatusEffectId effect);

    // Tick更新（DoT処理、持続時間カウント）
    void Tick(float deltaTime);
}
```

## データフロー
```
DamageDealer → DamageData生成
    ↓
DamageReceiver.ReceiveDamage(data)
    ↓
1. ガードチェック → ParryGuardSystem に委譲
2. ガード結果に応じてダメージ計算分岐
3. DamageCalculator.Compute() → 最終ダメージ
4. HP適用 → SoAコンテナ書き戻し
5. アーマー削り → ArmorSystem
6. 状態異常蓄積 → StatusEffectManager
7. ノックバック → KnockbackApplier
8. OnDamageDealt発火
9. HP <= 0 → OnCharacterDeath発火
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 属性別ダメージ計算 | (atk²×mv)/(atk+def) 式 | EditMode | High |
| ガードダメージ計算 | カット率適用 | EditMode | High |
| HP適用 | SoAコンテナへの書き戻し | EditMode | High |
| スタンボーナス | スタン中1.2倍 | EditMode | High |
| アーマーシステム | 削り・回復・怯み判定 | EditMode | High |
| 状態異常蓄積 | ヒットごとの加算・閾値チェック | EditMode | High |
| 状態異常発症・持続 | DoT/行動阻害/デバフの効果適用 | EditMode/PlayMode | Medium |
| ノックバック適用 | 方向・力の計算とRigidbody適用 | PlayMode | Medium |
| 防御倍率システム | バフ/デバフによる被ダメ増減 | EditMode | Medium |
| 無敵時間 | 被ダメ後の短い無敵フレーム | PlayMode | Medium |

## 設計メモ
- DamageCalculatorは純粋関数（static）でテストしやすくする
- 参考コードのMyHealth.csのComputeDamageOutput()をリファクタ
- 属性ダメージは5属性（物理/火/雷/光/闇）。参考コードの7属性から水・風を削除（GDDに合わせる）
  ※ GDDは7属性だがElement enumは4属性 → 要確認。ここではアーキテクチャ文書のElement enumを優先
- 無敵時間はデフォルト0.15秒（参考コード準拠）
