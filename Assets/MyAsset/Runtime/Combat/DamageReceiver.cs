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
                return new DamageResult
                {
                    totalDamage = 0,
                    guardResult = GuardResult.NoGuard,
                    isKill = false
                };
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(hash);

            // 現在のActStateを取得（ヒットスタン中の吹き飛ばし判定・状況ボーナスに必要）
            ActState currentActState = GameManager.Data.GetFlags(hash).ActState;

            // Step 2: ガード判定
            bool isAttackFromFront = IsAttackFromFront(data);
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                _isGuarding,
                _guardTimeSinceStart,
                combat.guardStats.guardStrength,
                data.armorBreakValue,
                data.feature,
                combat.guardStats.guardDirection,
                isAttackFromFront,
                effectState.hasGuardAttackEffect);

            // Step 3: ジャストガード回復
            if (guardResult == GuardResult.JustGuard)
            {
                GuardJudgmentLogic.ApplyJustGuardRecovery(
                    ref vitals.currentStamina, vitals.maxStamina,
                    ref vitals.currentArmor, vitals.maxArmor);
            }

            // Step 4: ダメージ計算（状況ボーナス + ガード軽減 + ActionEffect軽減）
            float guardReduction = GuardJudgmentLogic.GetDamageReduction(guardResult);
            int rawDamage = DamageCalculator.CalculateTotalDamage(
                data.damage, data.motionValue, combat.defense, Element.None);

            // 状況ダメージボーナス（ガード成功時は適用しない）
            SituationalBonus situationalBonus = SituationalBonus.None;
            if (guardResult == GuardResult.NoGuard || guardResult == GuardResult.GuardBreak)
            {
                bool isFromBehind = !isAttackFromFront;
                bool isTargetAttacking = SituationalBonusLogic.IsTargetAttacking(currentActState);
                bool isInHitstun = HitReactionLogic.IsInHitstun(currentActState);

                (float bonusMult, SituationalBonus bonus) =
                    SituationalBonusLogic.Evaluate(isTargetAttacking, isFromBehind, isInHitstun);

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

            // Step 5: アーマー削り（行動アーマー優先消費）
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

            DamageResult result = new DamageResult
            {
                totalDamage = hpResult.actualDamage,
                guardResult = guardResult,
                hitReaction = hitReaction,
                situationalBonus = situationalBonus,
                isCritical = false,
                isKill = hpResult.isKill,
                armorDamage = armorBefore - vitals.currentArmor,
                appliedEffect = StatusEffectId.None
            };

            // イベント発火
            if (GameManager.Events != null)
            {
                GameManager.Events.FireDamageDealt(result, data.attackerHash, data.defenderHash);

                if (hpResult.isKill)
                {
                    GameManager.Events.FireCharacterDeath(hash, data.attackerHash);
                }
            }

            // ノックバック適用（リアクションがKnockbackの場合のみ）
            if (_rb != null && hasKnockbackForce && hitReaction == HitReaction.Knockback)
            {
                Vector2 knockback = HpArmorLogic.CalculateKnockback(
                    data.knockbackForce, 0f);
                _rb.AddForce(knockback, ForceMode2D.Impulse);
            }

            return result;
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
