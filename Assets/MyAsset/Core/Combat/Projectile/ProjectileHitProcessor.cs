namespace Game.Core
{
    /// <summary>
    /// 飛翔体の命中処理。
    /// Attack系は IDamageable.ReceiveDamage を経由することで
    /// ガード/無敵/行動アーマー/状況ボーナス/HitReaction/イベント発火を共通化する。
    /// Recover/Support系は SoA を直接書き換える(被ダメージパイプライン不要)。
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
        /// 飛翔体がターゲットに命中した時の処理。
        /// Attack: receiver.ReceiveDamage経由で共通パイプラインを通す。
        /// Recover/Support: receiver.ObjectHash から SoA を直接書き換える。
        /// </summary>
        /// <param name="projectile">飛翔体(生存・キャスター有効性は呼び出し側で保証)</param>
        /// <param name="receiver">被弾側 IDamageable (通常 DamageReceiver)。null はスキップ</param>
        /// <param name="data">SoAコンテナ(Recover/Support で必要)</param>
        /// <param name="magic">魔法定義</param>
        /// <param name="events">Recover系のイベント発火(Attack系はDamageReceiverが発火)</param>
        public static HitResult ProcessHit(Projectile projectile, IDamageable receiver,
            SoACharaDataDic data, MagicDefinition magic, GameEvents events = null)
        {
            HitResult result = new HitResult { magicType = magic.magicType };

            if (projectile == null || receiver == null)
            {
                return result;
            }

            if (data == null || !data.TryGetValue(projectile.CasterHash, out int _))
            {
                return result;
            }

            int targetHash = receiver.ObjectHash;
            if (!data.TryGetValue(targetHash, out int _))
            {
                return result;
            }

            switch (magic.magicType)
            {
                case MagicType.Attack:
                {
                    // IDamageable経由でガード/無敵/HitReaction/イベント発火まで一括処理
                    DamageData damageData = BuildProjectileDamageData(
                        projectile.CasterHash, targetHash, magic, data);

                    DamageResult damageResult = receiver.ReceiveDamage(damageData);
                    result.damage = damageResult.totalDamage;
                    result.isKill = damageResult.isKill;
                    // GameEvents.FireDamageDealt/FireCharacterDeath は DamageReceiver 内で発火済み
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

        /// <summary>
        /// 飛翔体の DamageData を組み立てる。isProjectile=true でDamageReceiverが
        /// JustGuard時のアーマー削り0扱いに分岐する。
        /// </summary>
        private static DamageData BuildProjectileDamageData(
            int attackerHash, int defenderHash, MagicDefinition magic, SoACharaDataDic data)
        {
            ElementalStatus attackStats = CombatDataHelper.GetAttackStats(data, attackerHash);
            return new DamageData
            {
                attackerHash = attackerHash,
                defenderHash = defenderHash,
                damage = attackStats,
                motionValue = magic.motionValue,
                knockbackForce = default,
                attackElement = magic.attackElement,
                statusEffectInfo = default,
                feature = AttackFeature.None,
                armorBreakValue = 0f,
                justGuardResistance = 0f,
                isProjectile = true
            };
        }
    }
}
