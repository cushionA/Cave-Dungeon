using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 戦闘データ取得の共通ヘルパー。
    /// DamageDealer/HitBoxで重複していたロジックを一元化。
    /// </summary>
    public static class CombatDataHelper
    {
        private const int k_DefaultMaxHitCount = 1;

        /// <summary>
        /// AttackInfoからAttackMotionDataを構築する。
        /// ActionExecutorController等、AttackInfoベースで攻撃を駆動する箇所で使用。
        /// </summary>
        public static AttackMotionData BuildMotionData(AttackInfo info, int maxHitCount = k_DefaultMaxHitCount,
            StatusEffectApplicationData[] equipmentEffects = null)
        {
            return new AttackMotionData
            {
                actionName = info.attackName,
                motionValue = info.damageMultiplier,
                attackElement = info.attackElement,
                feature = info.feature,
                knockbackForce = info.knockbackInfo.hasKnockback
                    ? info.knockbackInfo.force
                    : Vector2.zero,
                armorBreakValue = info.armorBreakValue,
                guardDamage = info.guardDamage,
                maxHitCount = maxHitCount,
                staminaCost = info.staminaCost,
                mpCost = info.mpCost,
                statusEffects = StatusEffectApplicationLogic.Resolve(info.statusEffects),
                equipmentStatusEffects = equipmentEffects,
                attackMoveDistance = info.movement.speed * info.movement.duration,
                attackMoveDuration = info.movement.duration,
                contactType = info.contactType,
                isAutoChain = info.isAutoChain,
                isChainEndPoint = info.isChainEndPoint,
                inputWindow = info.inputWindow,
                justGuardResistance = info.justGuardResistance
            };
        }

        /// <summary>
        /// 指定ハッシュのキャラクターの攻撃ステータスを取得する。
        /// キャラクターが存在しない場合はdefaultを返す。
        /// </summary>
        public static ElementalStatus GetAttackStats(SoACharaDataDic data, int ownerHash)
        {
            if (data == null || !data.TryGetValue(ownerHash, out int _))
            {
                return default;
            }
            return data.GetCombatStats(ownerHash).attack;
        }

        /// <summary>
        /// AttackMotionDataからDamageDataを構築する。
        /// </summary>
        public static DamageData BuildDamageData(
            int attackerHash, int defenderHash,
            AttackMotionData motion, ElementalStatus attackStats,
            bool isProjectile = false)
        {
            return new DamageData
            {
                actionInstanceId = motion.actionInstanceId,
                attackerHash = attackerHash,
                defenderHash = defenderHash,
                damage = attackStats,
                motionValue = motion.motionValue,
                knockbackForce = motion.knockbackForce,
                attackElement = motion.attackElement,
                statusEffects = motion.statusEffects,
                equipmentStatusEffects = motion.equipmentStatusEffects,
                feature = motion.feature,
                armorBreakValue = motion.armorBreakValue,
                guardDamage = motion.guardDamage,
                justGuardResistance = motion.justGuardResistance,
                isProjectile = isProjectile
            };
        }
    }
}
