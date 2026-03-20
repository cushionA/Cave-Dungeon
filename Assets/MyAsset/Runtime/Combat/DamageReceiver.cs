using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージ受付MonoBehaviour。IDamageable実装。
    /// ActionEffectProcessor → ガード判定 → DamageCalculator → HpArmorLogic → SoA更新 → Events発火。
    /// </summary>
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private Rigidbody2D _rb;

        // ガード状態（外部から設定される）
        private bool _isGuarding;
        private float _guardTimeSinceStart;

        // 行動特殊効果（外部から設定される）
        private ActionEffect[] _currentActionEffects;
        private float _actionElapsedTime;

        // アーマー回復管理
        private float _armorRecoveryTimer;
        private float _armorRecoveryDelay;
        private float _armorRecoveryRate;
        private float _maxArmor;

        // 状況ボーナス設定（外部から注入可能）
        private SituationalBonusConfig _bonusConfig = SituationalBonusConfig.Default;

        public int ObjectHash => _character != null ? _character.ObjectHash : 0;
        public bool IsAlive => _character != null && _character.IsAlive;

        private void Awake()
        {
            _character = GetComponent<BaseCharacter>();
            _rb = GetComponent<Rigidbody2D>();
        }

        /// <summary>
        /// ガード状態を設定する。GuardAbilityから呼ばれる。
        /// </summary>
        public void SetGuarding(bool isGuarding)
        {
            if (isGuarding && !_isGuarding)
            {
                _guardTimeSinceStart = 0f;
            }
            _isGuarding = isGuarding;
        }

        /// <summary>
        /// 現在の行動特殊効果を設定する。ActionExecutorから呼ばれる。
        /// </summary>
        public void SetActionEffects(ActionEffect[] effects)
        {
            _currentActionEffects = effects;
            _actionElapsedTime = 0f;
        }

        /// <summary>
        /// 行動特殊効果をクリアする。行動終了時に呼ばれる。
        /// </summary>
        public void ClearActionEffects()
        {
            _currentActionEffects = null;
            _actionElapsedTime = 0f;
        }

        /// <summary>
        /// アーマー回復パラメータを設定する。初期化時にCharacterInfoから呼ばれる。
        /// </summary>
        public void SetArmorRecoveryParams(float maxArmor, float recoveryRate, float recoveryDelay)
        {
            _maxArmor = maxArmor;
            _armorRecoveryRate = recoveryRate;
            _armorRecoveryDelay = recoveryDelay;
        }

        /// <summary>
        /// 状況ダメージボーナス設定を注入する。
        /// </summary>
        public void SetBonusConfig(SituationalBonusConfig config)
        {
            _bonusConfig = config;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_isGuarding)
            {
                _guardTimeSinceStart += dt;
            }

            if (_currentActionEffects != null)
            {
                _actionElapsedTime += dt;
            }

            // アーマー自然回復
            if (_armorRecoveryRate > 0f && _character != null && _character.IsAlive)
            {
                UpdateArmorRecovery(dt);
            }
        }

        private void UpdateArmorRecovery(float deltaTime)
        {
            if (_armorRecoveryTimer > 0f)
            {
                _armorRecoveryTimer -= deltaTime;
                return;
            }

            int hash = _character.ObjectHash;
            if (GameManager.Data == null || !GameManager.Data.TryGetValue(hash, out int _))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            HpArmorLogic.RecoverArmor(ref vitals.currentArmor, _maxArmor,
                _armorRecoveryRate, deltaTime);
        }

        public DamageResult ReceiveDamage(DamageData data)
        {
            if (_character == null || GameManager.Data == null)
            {
                return default;
            }

            int hash = _character.ObjectHash;
            if (!GameManager.Data.TryGetValue(hash, out int _))
            {
                return default;
            }

            // Step 0: 行動特殊効果を評価
            ActionEffectProcessor.EffectState effectState =
                ActionEffectProcessor.Evaluate(_currentActionEffects, _actionElapsedTime);

            // Step 1: 無敵チェック
            if (effectState.isInvincible)
            {
                return CreateInvincibleResult();
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(hash);
            ActState currentActState = GameManager.Data.GetFlags(hash).ActState;

            // Step 2: ガード判定
            bool isAttackFromFront = IsAttackFromFront(data);
            GuardResult guardResult = EvaluateGuard(data, combat, isAttackFromFront, effectState);

            // Step 3: ジャストガード回復
            ApplyJustGuardRecovery(guardResult, ref vitals);

            // Step 4: ダメージ計算
            int reducedDamage = CalculateDamage(
                data, guardResult, combat, currentActState,
                isAttackFromFront, effectState,
                out SituationalBonus situationalBonus);

            // Step 5: アーマー削り + HP適用
            (int actualDamage, bool isKill, float armorBefore) = ApplyDamageToVitals(
                data, guardResult, effectState, reducedDamage, ref vitals);

            // Step 6: 被弾リアクション判定
            float totalArmorBefore = armorBefore + effectState.actionArmorValue;
            bool hasKnockbackForce = data.knockbackForce.sqrMagnitude > 0.01f;
            HitReaction hitReaction = HitReactionLogic.Determine(
                effectState.hasSuperArmor,
                effectState.hasKnockbackImmunity,
                totalArmorBefore,
                hasKnockbackForce,
                guardResult,
                currentActState);

            // Step 7: 結果構築 + イベント + ノックバック
            DamageResult result = BuildResult(
                actualDamage, guardResult, hitReaction, situationalBonus,
                isKill, armorBefore - vitals.currentArmor);

            FireEvents(result, hash, data);
            ApplyKnockback(data, hitReaction, hasKnockbackForce);

            return result;
        }

        // ===== ステップ別メソッド =====

        private static DamageResult CreateInvincibleResult()
        {
            return new DamageResult
            {
                totalDamage = 0,
                guardResult = GuardResult.NoGuard,
                hitReaction = HitReaction.None,
                isKill = false
            };
        }

        private GuardResult EvaluateGuard(
            DamageData data, CombatStats combat,
            bool isAttackFromFront, ActionEffectProcessor.EffectState effectState)
        {
            return GuardJudgmentLogic.Judge(
                _isGuarding,
                _guardTimeSinceStart,
                combat.guardStats.guardStrength,
                data.armorBreakValue,
                data.feature,
                combat.guardStats.guardDirection,
                isAttackFromFront,
                effectState.hasGuardAttackEffect);
        }

        private static void ApplyJustGuardRecovery(GuardResult guardResult, ref CharacterVitals vitals)
        {
            if (guardResult == GuardResult.JustGuard)
            {
                GuardJudgmentLogic.ApplyJustGuardRecovery(
                    ref vitals.currentStamina, vitals.maxStamina,
                    ref vitals.currentArmor, vitals.maxArmor);
            }
        }

        private int CalculateDamage(
            DamageData data, GuardResult guardResult, CombatStats combat,
            ActState currentActState, bool isAttackFromFront,
            ActionEffectProcessor.EffectState effectState,
            out SituationalBonus situationalBonus)
        {
            float guardReduction = GuardJudgmentLogic.GetDamageReduction(guardResult);
            int rawDamage = DamageCalculator.CalculateTotalDamage(
                data.damage, data.motionValue, combat.defense, Element.None);

            // 状況ダメージボーナス（ガード成功時は適用しない）
            situationalBonus = SituationalBonus.None;
            if (guardResult == GuardResult.NoGuard || guardResult == GuardResult.GuardBreak)
            {
                bool isFromBehind = !isAttackFromFront;
                bool isTargetAttacking = SituationalBonusLogic.IsTargetAttacking(currentActState);
                bool isInHitstun = HitReactionLogic.IsInHitstun(currentActState);

                (float bonusMult, SituationalBonus bonus) =
                    SituationalBonusLogic.Evaluate(isTargetAttacking, isFromBehind, isInHitstun, _bonusConfig);

                if (bonusMult > 1.0f)
                {
                    rawDamage = Mathf.FloorToInt(rawDamage * bonusMult);
                    situationalBonus = bonus;
                }
            }

            // ガード軽減適用
            int reducedDamage = Mathf.FloorToInt(rawDamage * (1f - guardReduction));

            // ActionEffect のダメージ軽減適用
            if (effectState.damageReduction > 0f)
            {
                reducedDamage = Mathf.FloorToInt(reducedDamage * (1f - effectState.damageReduction));
            }

            return reducedDamage;
        }

        private (int actualDamage, bool isKill, float armorBefore) ApplyDamageToVitals(
            DamageData data, GuardResult guardResult,
            ActionEffectProcessor.EffectState effectState,
            int reducedDamage, ref CharacterVitals vitals)
        {
            float actionArmor = effectState.actionArmorValue;
            float armorBefore = vitals.currentArmor;

            // ジャストガード時はアーマー削りを justGuardResistance で軽減
            float effectiveArmorBreak = data.armorBreakValue;
            if (guardResult == GuardResult.JustGuard)
            {
                effectiveArmorBreak *= (1f - data.justGuardResistance / 100f);
            }

            (int actualDamage, bool isKill, bool armorBroken) hpResult =
                HpArmorLogic.ApplyDamage(
                    ref vitals.currentHp, ref vitals.currentArmor,
                    reducedDamage, effectiveArmorBreak, ref actionArmor);

            // 被弾したらアーマー回復タイマーリセット
            if (reducedDamage > 0)
            {
                _armorRecoveryTimer = _armorRecoveryDelay;
            }

            // HP率キャッシュ更新
            vitals.hpRatio = vitals.maxHp > 0
                ? (byte)(100 * vitals.currentHp / vitals.maxHp)
                : (byte)0;

            return (hpResult.actualDamage, hpResult.isKill, armorBefore);
        }

        private static DamageResult BuildResult(
            int actualDamage, GuardResult guardResult, HitReaction hitReaction,
            SituationalBonus situationalBonus, bool isKill, float armorDamage)
        {
            return new DamageResult
            {
                totalDamage = actualDamage,
                guardResult = guardResult,
                hitReaction = hitReaction,
                situationalBonus = situationalBonus,
                isCritical = false,
                isKill = isKill,
                armorDamage = armorDamage,
                appliedEffect = StatusEffectId.None
            };
        }

        private static void FireEvents(DamageResult result, int hash, DamageData data)
        {
            if (GameManager.Events != null)
            {
                GameManager.Events.FireDamageDealt(result, data.attackerHash, data.defenderHash);

                if (result.isKill)
                {
                    GameManager.Events.FireCharacterDeath(hash, data.attackerHash);
                }
            }
        }

        private void ApplyKnockback(DamageData data, HitReaction hitReaction, bool hasKnockbackForce)
        {
            if (_rb != null && hasKnockbackForce && hitReaction == HitReaction.Knockback)
            {
                Vector2 knockback = HpArmorLogic.CalculateKnockback(
                    data.knockbackForce, 0f);
                _rb.AddForce(knockback, ForceMode2D.Impulse);
            }
        }

        /// <summary>
        /// 攻撃が前方からかどうかを判定する。
        /// knockbackForce の向きとキャラクターの向きを比較。
        /// </summary>
        private bool IsAttackFromFront(DamageData data)
        {
            if (data.knockbackForce.sqrMagnitude < 0.001f)
            {
                return true;
            }

            float facingX = _character.transform.localScale.x >= 0 ? 1f : -1f;
            float attackDirX = data.knockbackForce.x;

            // 攻撃方向とキャラの向きが反対（攻撃が正面から来ている）
            return (facingX > 0 && attackDirX < 0) || (facingX < 0 && attackDirX > 0);
        }
    }
}
