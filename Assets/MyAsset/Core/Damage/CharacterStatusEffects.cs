using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// キャラクターの状態異常蓄積・アクティブ効果。SoAコンテナで管理。
    /// </summary>
    [Serializable]
    public struct CharacterStatusEffects
    {
        private ulong _activeMask;
        private int _activeCount;
        private float _moveSpeedMultiplier;
        private float _attackMultiplier;
        private float _defenseMultiplier;
        private float _damageTakenMultiplier;

        public readonly int ActiveCount => _activeCount;
        public readonly float MoveSpeedMultiplier => _activeCount > 0 ? _moveSpeedMultiplier : 1f;
        public readonly float AttackMultiplier => _activeCount > 0 ? _attackMultiplier : 1f;
        public readonly float DefenseMultiplier => _activeCount > 0 ? _defenseMultiplier : 1f;
        public readonly float DamageTakenMultiplier => _activeCount > 0 ? _damageTakenMultiplier : 1f;
        public readonly bool BlocksMovement => IsActive(StatusEffectId.Stun) || IsActive(StatusEffectId.Freeze);
        public readonly bool BlocksActions => BlocksMovement;
        public readonly bool BlocksCasting => BlocksActions || IsActive(StatusEffectId.Silence);

        public static bool IsValidId(StatusEffectId id)
        {
            return id > StatusEffectId.None && id < StatusEffectId.Count;
        }

        public readonly bool IsActive(StatusEffectId id)
        {
            return IsValidId(id) && (_activeMask & (1UL << (int)id)) != 0;
        }

        internal void Include(StatusEffectId id, float modifier, int stackCount)
        {
            if (_activeCount == 0)
            {
                _moveSpeedMultiplier = 1f;
                _attackMultiplier = 1f;
                _defenseMultiplier = 1f;
                _damageTakenMultiplier = 1f;
            }
            _activeMask |= 1UL << (int)id;
            _activeCount++;
            float increase = (float)Math.Pow(1f + modifier, stackCount);
            switch (id)
            {
                case StatusEffectId.Slow:
                    _moveSpeedMultiplier *= (float)Math.Pow(1f - modifier, stackCount);
                    break;
                case StatusEffectId.SpeedUp:
                    _moveSpeedMultiplier *= increase;
                    break;
                case StatusEffectId.AttackUp:
                    _attackMultiplier *= increase;
                    break;
                case StatusEffectId.DefenseUp:
                    _defenseMultiplier *= increase;
                    break;
                case StatusEffectId.Weakness:
                    _damageTakenMultiplier *= increase;
                    break;
            }
        }
    }
}
