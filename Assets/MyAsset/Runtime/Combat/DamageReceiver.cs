using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// ダメージ受付MonoBehaviour。IDamageable実装。
    /// DamageCalculator → HpArmorLogic → SoA更新 → Events発火。
    /// </summary>
    public class DamageReceiver : MonoBehaviour, IDamageable
    {
        private BaseCharacter _character;
        private Rigidbody2D _rb;

        public int ObjectHash => _character != null ? _character.ObjectHash : 0;
        public bool IsAlive => _character != null && _character.IsAlive;

        private void Awake()
        {
            _character = GetComponent<BaseCharacter>();
            _rb = GetComponent<Rigidbody2D>();
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

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(hash);
            ref CombatStats combat = ref GameManager.Data.GetCombatStats(hash);

            // ダメージ計算
            int totalDamage = DamageCalculator.CalculateTotalDamage(
                data.damage, data.motionValue, combat.defense, Element.None);

            // HP/Armor適用
            float armorBefore = vitals.currentArmor;
            HpArmorLogic.ApplyDamage(
                ref vitals.currentHp, ref vitals.currentArmor,
                totalDamage, data.armorBreakValue);

            // HP率キャッシュ更新
            vitals.hpRatio = vitals.maxHp > 0
                ? (byte)(100 * vitals.currentHp / vitals.maxHp)
                : (byte)0;

            bool isKill = vitals.currentHp <= 0;

            DamageResult result = new DamageResult
            {
                totalDamage = totalDamage,
                guardResult = GuardResult.NoGuard,
                isCritical = false,
                isKill = isKill,
                armorDamage = armorBefore - vitals.currentArmor,
                appliedEffect = StatusEffectId.None
            };

            // イベント発火
            if (GameManager.Events != null)
            {
                GameManager.Events.FireDamageDealt(result, data.attackerHash, data.defenderHash);

                if (isKill)
                {
                    GameManager.Events.FireCharacterDeath(hash, data.attackerHash);
                }
            }

            // ノックバック適用
            if (_rb != null && data.knockbackForce.sqrMagnitude > 0.01f)
            {
                _rb.AddForce(data.knockbackForce, ForceMode2D.Impulse);
            }

            return result;
        }
    }
}
