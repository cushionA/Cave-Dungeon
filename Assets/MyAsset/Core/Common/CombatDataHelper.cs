namespace Game.Core
{
    /// <summary>
    /// 戦闘データ取得の共通ヘルパー。
    /// DamageDealer/HitBoxで重複していたロジックを一元化。
    /// </summary>
    public static class CombatDataHelper
    {
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
                attackerHash = attackerHash,
                defenderHash = defenderHash,
                damage = attackStats,
                motionValue = motion.motionValue,
                knockbackForce = motion.knockbackForce,
                attackElement = motion.attackElement,
                statusEffectInfo = motion.statusEffect,
                feature = motion.feature,
                armorBreakValue = motion.armorBreakValue,
                justGuardResistance = motion.justGuardResistance,
                isProjectile = isProjectile
            };
        }
    }
}
