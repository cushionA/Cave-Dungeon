namespace Game.Core
{
    /// <summary>
    /// Processes projectile hits against targets, handling attack damage,
    /// recovery healing, and support effects based on MagicType.
    /// </summary>
    public static class ProjectileHitProcessor
    {
        public struct HitResult
        {
            public int damage;
            public int healAmount;
            public bool isKill;
            public MagicType magicType;
        }

        /// <summary>
        /// Processes a projectile hit on a target. Applies damage or healing
        /// based on magic type and registers the hit on the projectile.
        /// </summary>
        public static HitResult ProcessHit(Projectile projectile, int targetHash,
            SoACharaDataDic data, MagicDefinition magic)
        {
            HitResult result = new HitResult { magicType = magic.magicType };

            if (!data.TryGetValue(projectile.CasterHash, out int _) ||
                !data.TryGetValue(targetHash, out int _))
            {
                return result;
            }

            switch (magic.magicType)
            {
                case MagicType.Attack:
                {
                    ref CombatStats casterStats = ref data.GetCombatStats(projectile.CasterHash);
                    ref CombatStats targetStats = ref data.GetCombatStats(targetHash);

                    int damage = DamageCalculator.CalculateTotalDamage(
                        casterStats.attack, magic.motionValue, targetStats.defense,
                        magic.attackElement);

                    ref CharacterVitals targetVitals = ref data.GetVitals(targetHash);
                    targetVitals.currentHp -= damage;
                    result.damage = damage;
                    result.isKill = targetVitals.currentHp <= 0;
                    break;
                }

                case MagicType.Recover:
                {
                    ref CharacterVitals targetVitals = ref data.GetVitals(targetHash);
                    int heal = magic.healAmount;
                    targetVitals.currentHp += heal;
                    if (targetVitals.currentHp > targetVitals.maxHp)
                    {
                        targetVitals.currentHp = targetVitals.maxHp;
                    }
                    result.healAmount = heal;
                    break;
                }

                case MagicType.Support:
                {
                    result.healAmount = 0;
                    break;
                }
            }

            projectile.RegisterHit();
            return result;
        }
    }
}
