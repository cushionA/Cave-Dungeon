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
            SoACharaDataDic data, MagicDefinition magic, GameEvents events = null)
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

                    int rawDamage = DamageCalculator.CalculateTotalDamage(
                        casterStats.attack, magic.motionValue, targetStats.defense,
                        magic.attackElement);

                    ref CharacterVitals targetVitals = ref data.GetVitals(targetHash);
                    float actionArmor = 0f;
                    (int actualDamage, bool isKill, bool _) = HpArmorLogic.ApplyDamage(
                        ref targetVitals.currentHp, ref targetVitals.currentArmor,
                        rawDamage, 0f, ref actionArmor);
                    result.damage = actualDamage;
                    result.isKill = isKill;

                    // HP率キャッシュ更新
                    targetVitals.hpRatio = targetVitals.maxHp > 0
                        ? (byte)(100 * targetVitals.currentHp / targetVitals.maxHp)
                        : (byte)0;

                    // GameEventsに通知（HUD・音声・クエスト等の外部システム連携）
                    if (events != null)
                    {
                        DamageResult damageResult = new DamageResult
                        {
                            totalDamage = actualDamage,
                            guardResult = GuardResult.NoGuard,
                            hitReaction = isKill ? HitReaction.Knockback : HitReaction.Flinch,
                            isKill = isKill
                        };
                        events.FireDamageDealt(damageResult, projectile.CasterHash, targetHash);

                        if (isKill)
                        {
                            events.FireCharacterDeath(targetHash, projectile.CasterHash);
                        }
                    }
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
