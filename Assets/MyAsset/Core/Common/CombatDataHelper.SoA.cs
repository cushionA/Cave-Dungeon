namespace Game.Core
{
    public static partial class CombatDataHelper
    {
        /// <summary>近接判定の開始時に、発生元と対象条件を同じ時点で捕捉する。</summary>
        public static bool TryCaptureAttack(int ownerHash, in AttackMotionData motion,
            SoACharaDataDic data, ActorRegistry actors, out AttackContext context)
        {
            context = default;
            if (data == null || actors == null || !actors.TryGet(ownerHash, out ActorRef owner)
                || !data.TryGetValue(ownerHash, out int _)) { return false; }
            context = AttackContext.Capture(owner, data.GetCombatStats(ownerHash).attack,
                data.GetStatusEffects(ownerHash).AttackMultiplier, data.GetFlags(ownerHash).Belong,
                motion.equipmentStatusEffects, motion.statusEffects, motion.targetPolicy.Resolve());
            return true;
        }

        /// <summary>対象の現在世代・陣営と、発生時に捕捉した攻撃条件を照合する。</summary>
        public static bool CanAffectTarget(in AttackContext context, ActorRef target,
            SoACharaDataDic data, ActorRegistry actors)
        {
            if (data == null || actors == null || !actors.IsCurrent(target)
                || !data.TryGetValue(target.Hash, out int _)) { return false; }
            CharacterFlags flags = data.GetFlags(target.Hash);
            return !flags.IsVanished && AttackTargetRules.CanAffect(context, target, flags.Belong);
        }

        /// <summary>存在するキャラクターの攻撃力をSoAから取得する。</summary>
        public static ElementalStatus GetAttackStats(SoACharaDataDic data, int ownerHash)
        {
            if (data == null || !data.TryGetValue(ownerHash, out int _))
            {
                return default;
            }
            return data.GetCombatStats(ownerHash).attack;
        }
    }
}
