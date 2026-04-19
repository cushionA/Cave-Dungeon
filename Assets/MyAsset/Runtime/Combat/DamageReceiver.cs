using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージ受付MonoBehaviour。IDamageable実装。
    /// ActionEffectProcessor → ガード判定(スタミナ削り+連続JG窓) → DamageCalculator(属性別カット) → HpArmorLogic → SoA更新 → Events発火。
    /// </summary>
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private Rigidbody2D _rb;

        // 状態異常管理
        private StatusEffectManager _statusEffectManager;

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

        // 連続ジャストガード窓: 直前にJustGuardが成立した場合、
        // k_ContinuousJustGuardWindow 秒以内の次ガードは即ジャスガ扱いになる。
        private float _continuousJustGuardExpireTime = -1f;

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

        /// <summary>
        /// StatusEffectManagerを設定する。初期化時にCharacterSetupから呼ばれる。
        /// </summary>
        public void SetStatusEffectManager(StatusEffectManager manager)
        {
            _statusEffectManager = manager;
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
            if (!GameManager.IsCharacterValid(hash))
            {
                return;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            HpArmorLogic.RecoverArmor(ref vitals.currentArmor, _maxArmor,
                _armorRecoveryRate, deltaTime);
        }

        public DamageResult ReceiveDamage(DamageData data)
        {
            if (_character == null)
            {
                return default;
            }

            int hash = _character.ObjectHash;
            if (!GameManager.IsCharacterValid(hash))
            {
                return default;
            }

            // Step 0: 行動特殊効果を評価
            ActionEffectProcessor.EffectState effectState =
                ActionEffectProcessor.Evaluate(_currentActionEffects, _actionElapsedTime);

            // Step 1: 無敵チェック（ActionEffect無敵 or 起き上がり無敵）
            ActState currentActState = GameManager.Data.GetFlags(hash).ActState;
            if (effectState.isInvincible || WakeUpLogic.IsWakeUpState(currentActState))
            {
                return CreateInvincibleResult();
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(hash);

            // Step 2: ガード判定（連続JG窓・スタミナ削り判定を含む）
            bool isAttackFromFront = IsAttackFromFront(data);
            bool inContinuousJustGuardWindow = IsInContinuousJustGuardWindow();
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                _isGuarding,
                _guardTimeSinceStart,
                inContinuousJustGuardWindow,
                data.feature,
                combat.guardStats.guardDirection,
                isAttackFromFront,
                effectState.hasGuardAttackEffect,
                vitals.currentStamina,
                combat.guardStats.guardStrength,
                data.armorBreakValue);

            // Step 3: ジャストガード回復 + 連続JG窓更新
            ApplyJustGuardRecovery(guardResult, ref vitals);

            // Step 3.5: 通常ガード/ブレイクのスタミナ削りを適用
            ApplyGuardStaminaDrain(data, guardResult, combat, ref vitals);

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

            // Step 6.5: 状態異常蓄積
            StatusEffectId appliedEffect = ApplyStatusEffect(
                data.statusEffectInfo, guardResult, combat.guardStats.statusCut, hash);

            // Step 7: 結果構築 + イベント + ノックバック
            DamageResult result = BuildResult(
                actualDamage, guardResult, hitReaction, situationalBonus,
                isKill, armorBefore - vitals.currentArmor, appliedEffect);

            FireEvents(result, hash, data);
            ApplyKnockback(data, hitReaction, hasKnockbackForce, hash);

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

        /// <summary>
        /// 直前のJustGuard成立から連続ジャスガ窓内かを判定する。
        /// </summary>
        private bool IsInContinuousJustGuardWindow()
        {
            return _continuousJustGuardExpireTime > 0f && Time.time < _continuousJustGuardExpireTime;
        }

        private void ApplyJustGuardRecovery(GuardResult guardResult, ref CharacterVitals vitals)
        {
            if (guardResult == GuardResult.JustGuard)
            {
                GuardJudgmentLogic.ApplyJustGuardRecovery(
                    ref vitals.currentStamina, vitals.maxStamina,
                    ref vitals.currentArmor, vitals.maxArmor);

                // JustGuard成立直後は次の1ヒットも連続ジャスガ窓内とする
                _continuousJustGuardExpireTime = Time.time + GuardJudgmentLogic.k_ContinuousJustGuardWindow;
            }
        }

        /// <summary>
        /// 通常ガード/ブレイク時のスタミナ削りを適用する。JustGuard/NoGuard時は削らない。
        /// 削り量 = max(0, armorBreakValue - guardStrength)。スタミナは0にクランプ。
        /// </summary>
        private static void ApplyGuardStaminaDrain(
            DamageData data, GuardResult guardResult, CombatStats combat, ref CharacterVitals vitals)
        {
            if (guardResult != GuardResult.Guarded && guardResult != GuardResult.GuardBreak)
            {
                return;
            }

            float drain = GuardJudgmentLogic.CalculateStaminaDrain(
                data.armorBreakValue, combat.guardStats.guardStrength);
            if (drain <= 0f)
            {
                return;
            }

            vitals.currentStamina = Mathf.Max(0f, vitals.currentStamina - drain);
        }

        private int CalculateDamage(
            DamageData data, GuardResult guardResult, CombatStats combat,
            ActState currentActState, bool isAttackFromFront,
            ActionEffectProcessor.EffectState effectState,
            out SituationalBonus situationalBonus)
        {
            situationalBonus = SituationalBonus.None;

            // JustGuardは軽減率100%相当（ダメージ完全0）
            if (guardResult == GuardResult.JustGuard)
            {
                return 0;
            }

            bool guardSucceeded = GuardJudgmentLogic.IsGuardSucceeded(guardResult);

            // 属性別カット率はガード成功時のみ適用
            int rawDamage = DamageCalculator.CalculateTotalDamageWithElementalCut(
                data.damage, data.motionValue, combat.defense,
                Element.None,
                combat.guardStats,
                applyCuts: guardSucceeded);

            // 状況ダメージボーナス（ガード成功時は適用しない）
            if (!guardSucceeded)
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

            // ActionEffect のダメージ軽減適用
            int reducedDamage = rawDamage;
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

            // ジャストガード時のアーマー削り: 飛翔体は削り0固定、近接は justGuardResistance で軽減
            float effectiveArmorBreak = data.armorBreakValue;
            if (guardResult == GuardResult.JustGuard)
            {
                if (data.isProjectile)
                {
                    effectiveArmorBreak = 0f;
                }
                else
                {
                    effectiveArmorBreak *= (1f - data.justGuardResistance / 100f);
                }
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
            SituationalBonus situationalBonus, bool isKill,
            float armorDamage, StatusEffectId appliedEffect)
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
                appliedEffect = appliedEffect
            };
        }

        /// <summary>
        /// 状態異常蓄積を適用する。ガード成功時はstatusEffectを無効化する。
        /// </summary>
        private StatusEffectId ApplyStatusEffect(
            StatusEffectInfo info, GuardResult guardResult, float statusCut, int targetHash)
        {
            if (_statusEffectManager == null)
            {
                return StatusEffectId.None;
            }

            if (info.effect == StatusEffectId.None)
            {
                return StatusEffectId.None;
            }

            // ガード成功時は状態異常蓄積しない
            if (GuardJudgmentLogic.IsGuardSucceeded(guardResult))
            {
                return StatusEffectId.None;
            }

            bool triggered = _statusEffectManager.Accumulate(info, statusCut);
            if (triggered)
            {
                if (GameManager.Events != null)
                {
                    GameManager.Events.FireStatusEffectApplied(targetHash, info.effect);
                }
                return info.effect;
            }

            return StatusEffectId.None;
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

        private void ApplyKnockback(DamageData data, HitReaction hitReaction, bool hasKnockbackForce, int hash)
        {
            if (_rb != null && hasKnockbackForce && hitReaction == HitReaction.Knockback)
            {
                // TODO: CombatStatsにknockbackResistanceフィールド追加後、ここで参照する
                float knockbackResistance = 0f;
                Vector2 knockback = HpArmorLogic.CalculateKnockback(
                    data.knockbackForce, knockbackResistance);
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
