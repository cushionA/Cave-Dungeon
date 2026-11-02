# System: DamageSystem
Section: 1 — MVP

## 責務
ダメージ計算の中核。攻撃側のDamageDataと防御側のステータスから最終ダメージを算出し、HP/アーマー/状態異常を適用する。

## 依存
- 入力: DamageData（WeaponSystemが生成）、DataContainer（防御ステータス）
- 出力: DamageResult、HP変更、状態異常蓄積、OnDamageDealtイベント

## 対象条件の共通契約（2026-10-27）

`AttackInfo.targetPolicy` に対象条件を設定し、`ActionCatalog` / `CombatDataHelper` が登録時に解決して近接・弾・DamageDataへ渡す。`overrideTargets = true` のとき `targets` のビット組合せを使い、`None` は明示的な対象なし。未指定は Attack / Support が Hostile、Recover が Friendly | Self。Supportの味方バフはFriendly等を明示し、効果種別と対象条件を独立して指定できる。

| 関係 | 共通の評価 |
|---|---|
| Self | 発生元と対象のActorRefが完全一致。hash再利用後の別世代を含めない |
| Friendly | Ally / Enemyの戦闘陣営ビットを共有する他個体 |
| Hostile | 双方が戦闘陣営を持ち、共有ビットがない他個体 |
| Neutral | 上記以外。Neutralだけの個体同士もFriendlyにしない |

`AttackTargetRules` が純関数としてこの評価を担う。近接の判定開始時と弾の発射時に `AttackContext` へ発生元陣営・対象条件を捕捉し、射手の消失や陣営変更後も保持する。対象の現在世代・現在陣営は各命中時に再照合する。近接・弾・爆発は命中数や重複記録より前に対象外を除き、対象外への接触で爆発や子弾を発動しない。

追尾も同じ対象条件で明示対象と自動候補を選ぶ。操舵対象の観測は固定tick開始時の一括処理なので、同tickの被弾callbackによる陣営変更が操舵へ反映されるのは次tick。命中可否はその場の陣営で再評価する。

`DamageReceiver` と純 `HitResolver` でも捕捉済み条件を再確認し、対象外ならHP・アーマー・SP・状態異常・反動・通知を変更しない。直接 `ReceiveDamage` へ渡す旧packetは、明示条件があれば発生元を解決して同じルールを適用し、解決できなければ拒否する。捕捉も明示条件もない旧packetは発生元なしダメージとの互換経路として維持する。通常の近接・弾経路は必ず捕捉済み条件を使う。

近接の回復効果、トラップ発射元の登録、Physicsレイヤー構成の変更は今回追加していない。対象条件は効果の実装を増やすものではない。

## 状態異常の防御データ（2026-10-27）

攻撃側は引き続き共通の `StatusEffectApplicationData`（効果・蓄積/即時・付与先・ガード規則）を渡す。防御側は `StatusEffectDefenseProfile` を `CombatStats.statusDefense` に保持し、`StatusEffectSystem` が付与時の対象の現在値を読む。攻撃packetへ防御側の耐性を捕捉しない。

- **設定元**: `CharacterInfo.statusDefense` と、武器・盾・コアそれぞれの `EquipmentStatModifiers.statusDefense`。いずれも共通/個別軽減、共通閾値倍率、15効果別の閾値・免疫を持つ値型。配列や可変参照をSoAの防御値に持たせない。
- **基礎耐性の互換**: 旧 `CharacterInfo.statusResistance` は名前・float型を保持し、登録時に `thresholdMultiplierBonus` へ一度加算する。既存tooltip/設計の意図どおり閾値を `1 + 値` 倍する値で、蓄積軽減率へ読み替えない。旧入力はInspectorで非表示、新規設定は `statusDefense` に統一する。旧値を新フィールドへ自動コピーしない。
- **軽減**: 素体と全装備の同じチャンネルを加算し、共通軽減と対象効果の個別軽減の和を利用時に0〜1へ制限する。既存 `guardStats.statusCut` は従来どおり残存蓄積へ乗算する。例: 共通0.2・毒個別0.25・旧cut0.5なら、蓄積100は `100 × (1 - 0.45) × (1 - 0.5) = 27.5`。
- **閾値**: 素体の個別閾値0は100、装備の個別閾値0は補正なし。装備の平坦補正がある場合は加算後に下限1とし、共通倍率 `1 + thresholdMultiplierBonus` を適用して再度下限1とする。共通倍率bonusも全装備から加算する。既定値ではraw0を維持し、利用時に100へ解決する。個別効果の仮バランス値は導入しない。
- **免疫**: 素体・全装備の指定をORで合成し、蓄積と即時付与の両方を拒否する。即時付与は軽減率や閾値を通らず、軽減率1だけで免疫にはならない。Sourceへの自己蓄積付与にも現在の防御値を使い、従来のSource向け旧cut0だけは維持する。
- **変更時**: 着脱のたびに基礎値から再導出し、補正を累積しない。免疫取得で既存異常・過去蓄積を自動解除しない。閾値変更も次の有効な蓄積入力から評価し、自動発症や治療を追加しない。

非有限な防御値は副作用の前に拒否する。装備入口でも先に検証し、不正装備で旧装備の解除やcallbackだけが実行される状態を防ぐ。低水準の `StatusEffectStore` / `StatusEffectManager` は従来のcut引数の互換入口と、防御値を明示する入口を持つ。ゲーム内の通常付与は現在の防御値を解決する `StatusEffectSystem` を使う。

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
