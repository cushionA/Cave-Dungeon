namespace Game.Core
{
    /// <summary>
    /// 片手⇔両手持ち切替とスキルソース判定を管理するピュアロジック。
    /// </summary>
    public class GripManager
    {
        private const float k_TwoHandedAttackSpeedBonus = 1.15f;
        private const float k_OneHandedAttackSpeedBonus = 1.0f;

        private GripMode _currentGrip;

        public GripMode CurrentGrip => _currentGrip;

        public GripManager(GripMode initialGrip = GripMode.OneHanded)
        {
            _currentGrip = initialGrip;
        }

        /// <summary>片手⇔両手切替。戻り値:切替後のGripMode。</summary>
        public GripMode ToggleGrip()
        {
            _currentGrip = _currentGrip == GripMode.OneHanded
                ? GripMode.TwoHanded
                : GripMode.OneHanded;

            return _currentGrip;
        }

        /// <summary>
        /// スキル使用時のソース判定。
        /// 両手持ち→常にWeapon。片手持ち+盾あり→Shield。片手持ち+盾なし→Weapon。
        /// </summary>
        public static SkillSource DetermineSkillSource(GripMode grip, bool hasShield)
        {
            if (grip == GripMode.TwoHanded)
            {
                return SkillSource.Weapon;
            }

            return hasShield ? SkillSource.Shield : SkillSource.Weapon;
        }

        /// <summary>
        /// 両手持ちボーナス。両手持ち時はAttackSpeed+0.15の倍率を返す。片手は1.0。
        /// </summary>
        public static float GetTwoHandedAttackBonus(GripMode grip)
        {
            return grip == GripMode.TwoHanded
                ? k_TwoHandedAttackSpeedBonus
                : k_OneHandedAttackSpeedBonus;
        }
    }
}
