using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Edit Mode テスト用の IDamageable モック。
    /// SoACharaDataDic に紐付き、ReceiveDamage で DamageCalculator+HpArmorLogic を使って
    /// HPを減算する最小実装。ガード/無敵/HitReaction は扱わないため
    /// ProjectileHitProcessor のAttack経路を純ロジックでテストする用途向け。
    /// </summary>
    internal class SoABackedMockDamageable : IDamageable
    {
        private readonly SoACharaDataDic _data;
        private readonly int _hash;
        private bool _isAlive;

        public int ObjectHash => _hash;
        public bool IsAlive => _isAlive;
        public DamageData LastReceived { get; private set; }

        public SoABackedMockDamageable(SoACharaDataDic data, int hash)
        {
            _data = data;
            _hash = hash;
            _isAlive = true;
        }

        public DamageResult ReceiveDamage(DamageData data)
        {
            LastReceived = data;

            if (!_data.TryGetValue(_hash, out int _))
            {
                return default;
            }

            ref CharacterVitals vitals = ref _data.GetVitals(_hash);
            ref CombatStats combat = ref _data.GetCombatStats(_hash);

            int raw = DamageCalculator.CalculateTotalDamage(
                data.damage, data.motionValue, combat.defense, Element.None);

            float actionArmor = 0f;
            (int actualDamage, bool isKill, bool _) = HpArmorLogic.ApplyDamage(
                ref vitals.currentHp, ref vitals.currentArmor,
                raw, data.armorBreakValue, ref actionArmor);

            vitals.hpRatio = vitals.maxHp > 0
                ? (byte)(100 * vitals.currentHp / vitals.maxHp)
                : (byte)0;

            if (isKill)
            {
                _isAlive = false;
            }

            return new DamageResult
            {
                totalDamage = actualDamage,
                guardResult = GuardResult.NoGuard,
                hitReaction = isKill ? HitReaction.Knockback : HitReaction.Flinch,
                situationalBonus = SituationalBonus.None,
                isCritical = false,
                isKill = isKill,
                armorDamage = 0f,
                appliedEffect = StatusEffectId.None
            };
        }
    }

    /// <summary>
    /// DamageResult を任意に返す最小 IDamageable モック。
    /// ガード/無敵/HitReaction の挙動を制御したいテスト向け。
    /// </summary>
    internal class StubDamageable : IDamageable
    {
        public int ObjectHash { get; set; }
        public bool IsAlive { get; set; } = true;
        public DamageResult ReturnValue { get; set; }
        public DamageData LastReceived { get; private set; }
        public int ReceiveCount { get; private set; }

        public DamageResult ReceiveDamage(DamageData data)
        {
            LastReceived = data;
            ReceiveCount++;
            return ReturnValue;
        }
    }
}
