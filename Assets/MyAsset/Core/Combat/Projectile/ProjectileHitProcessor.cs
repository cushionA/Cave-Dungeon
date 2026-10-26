using System;

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
        /// <param name="projectile">飛翔体。捕捉済み攻撃は射手消失後も元個体の入力で成立する。</param>
        /// <param name="receiver">被弾側 IDamageable (通常 DamageReceiver)。null はスキップ</param>
        /// <param name="data">SoAコンテナ(Recover/Support で必要)</param>
        /// <param name="magic">魔法定義</param>
        /// <param name="events">互換引数。Attack系のイベントはDamageReceiverが発火する。</param>
        /// <param name="actors">Runtimeの世代照合。省略はhashだけの既存呼出しの互換経路。</param>
        /// <param name="observedTarget">接触・範囲検索時に観測した対象個体。</param>
        public static HitResult ProcessHit(Projectile projectile, IDamageable receiver,
            SoACharaDataDic data, MagicDefinition magic, GameEvents events = null,
            StatusEffectSystem statusEffects = null, ActorRegistry actors = null, ActorRef observedTarget = default)
        {
            HitResult result = new HitResult { magicType = magic.magicType };

            if (projectile == null || receiver == null)
            {
                return result;
            }

            bool captured = magic.attackContext.IsCaptured;
            if (data == null || (!captured && !data.TryGetValue(projectile.CasterHash, out int _)))
            {
                return result;
            }

            int targetHash = receiver.ObjectHash;
            if (!data.TryGetValue(targetHash, out int _))
            {
                return result;
            }
            if (data.GetFlags(targetHash).IsVanished) { return result; }
            ActorRef targetRef = observedTarget;
            if (captured || projectile.CasterRef.IsValid || actors != null)
            {
                if (actors == null || !captured || magic.attackContext.Owner != projectile.CasterRef) { return result; }
                if (!targetRef.IsValid && !actors.TryGet(targetHash, out targetRef)) { return result; }
                if (targetRef.Hash != targetHash || !projectile.CanHit(targetRef, actors)) { return result; }
            }

            bool appliesEffects = magic.magicType != MagicType.Attack && (captured
                ? magic.attackContext.HasStatusEffects
                : ((magic.statusEffects != null && magic.statusEffects.Length > 0)
                    || (magic.equipmentStatusEffects != null && magic.equipmentStatusEffects.Length > 0)));
            if (appliesEffects && statusEffects == null)
            {
                throw new InvalidOperationException("Configured magic effects require a StatusEffectSystem.");
            }

            switch (magic.magicType)
            {
                case MagicType.Attack:
                {
                    // IDamageable経由でガード/無敵/HitReaction/イベント発火まで一括処理
                    DamageData damageData = BuildProjectileDamageData(
                        projectile.CasterHash, targetHash, magic, data);
                    if (captured) { magic.attackContext.ApplyTo(ref damageData, targetRef); }

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

            if (appliesEffects)
            {
                float statusCut = data.GetCombatStats(targetHash).guardStats.statusCut;
                if (captured)
                {
                    statusEffects.ApplyApplications(magic.attackContext, targetHash, GuardResult.NoGuard, statusCut);
                }
                else
                {
                    statusEffects.ApplyApplications(magic.statusEffects, projectile.CasterHash,
                        targetHash, GuardResult.NoGuard, statusCut, magic.equipmentStatusEffects);
                }
            }

            // 総ヒット数の消費 (Projectile.RegisterHit) は ProjectileController.TryRegisterHit 側で
            // ターゲット別上限ゲートと合わせて実行する。ここでは呼ばない (二重消費防止)。
            // AoE 爆発 (ProjectileManager.ProcessExplosion) は 1 弾丸に対し 1 回の発動なので
            // 個別被弾者ごとに RemainingHits を消費させる必要はない。
            return result;
        }

        /// <summary>
        /// 飛翔体の DamageData を組み立てる。isProjectile=true でDamageReceiverが
        /// JustGuard時のアーマー削り0扱いに分岐する。
        /// </summary>
        private static DamageData BuildProjectileDamageData(
            int attackerHash, int defenderHash, MagicDefinition magic, SoACharaDataDic data)
        {
            ElementalStatus attackStats = magic.attackContext.IsCaptured
                ? magic.attackContext.AttackStats : CombatDataHelper.GetAttackStats(data, attackerHash);
            return new DamageData
            {
                actionInstanceId = magic.actionInstanceId,
                attackerHash = attackerHash,
                defenderHash = defenderHash,
                damage = attackStats,
                motionValue = magic.motionValue,
                knockbackForce = magic.knockbackForce,
                attackElement = magic.attackElement,
                statusEffects = magic.statusEffects,
                equipmentStatusEffects = magic.equipmentStatusEffects,
                feature = magic.feature,
                guardDamage = magic.guardDamage,
                armorBreakValue = magic.armorBreakValue,
                justGuardResistance = magic.justGuardResistance,
                isProjectile = true
            };
        }
    }
}
