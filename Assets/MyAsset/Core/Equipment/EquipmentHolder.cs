using System;

namespace Game.Core
{
    /// <summary>
    /// 装備スロット管理のピュアロジック。3スロット（Weapon/Shield/Core）を管理。
    /// </summary>
    public class EquipmentHolder
    {
        private IEquippable _weapon;
        private IEquippable _shield;
        private IEquippable _core;
        private readonly int _ownerHash;

        public IEquippable Weapon => _weapon;
        public IEquippable Shield => _shield;
        public IEquippable Core => _core;
        public bool HasWeapon => _weapon != null;
        public bool HasShield => _shield != null;
        public bool HasCore => _core != null;

        public EquipmentHolder(int ownerHash)
        {
            _ownerHash = ownerHash;
        }

        /// <summary>
        /// 指定スロットに装備をセット。前の装備があればOnUnequipを呼ぶ。新装備のOnEquipを呼ぶ。
        /// </summary>
        /// <param name="item">装備するアイテム</param>
        /// <returns>前の装備（なければnull）</returns>
        public IEquippable Equip(IEquippable item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            IEquippable previous = null;

            switch (item.Slot)
            {
                case EquipSlot.Weapon:
                    previous = _weapon;
                    if (previous != null)
                    {
                        previous.OnUnequip(_ownerHash);
                    }
                    _weapon = item;
                    break;

                case EquipSlot.Shield:
                    previous = _shield;
                    if (previous != null)
                    {
                        previous.OnUnequip(_ownerHash);
                    }
                    _shield = item;
                    break;

                case EquipSlot.Core:
                    previous = _core;
                    if (previous != null)
                    {
                        previous.OnUnequip(_ownerHash);
                    }
                    _core = item;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(item), $"Unknown EquipSlot: {item.Slot}");
            }

            item.OnEquip(_ownerHash);
            return previous;
        }

        /// <summary>
        /// 指定スロットの装備を外す。OnUnequipを呼ぶ。
        /// </summary>
        /// <param name="slot">外すスロット</param>
        /// <returns>外した装備（なければnull）</returns>
        public IEquippable Unequip(EquipSlot slot)
        {
            IEquippable removed = null;

            switch (slot)
            {
                case EquipSlot.Weapon:
                    removed = _weapon;
                    _weapon = null;
                    break;

                case EquipSlot.Shield:
                    removed = _shield;
                    _shield = null;
                    break;

                case EquipSlot.Core:
                    removed = _core;
                    _core = null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), $"Unknown EquipSlot: {slot}");
            }

            if (removed != null)
            {
                removed.OnUnequip(_ownerHash);
            }

            return removed;
        }

        /// <summary>
        /// 全装備のAbilityFlagを合算して返す。
        /// </summary>
        public AbilityFlag GetCombinedAbilityFlags()
        {
            AbilityFlag flags = AbilityFlag.None;

            if (_weapon != null)
            {
                flags |= _weapon.GrantedFlags;
            }

            if (_shield != null)
            {
                flags |= _shield.GrantedFlags;
            }

            if (_core != null)
            {
                flags |= _core.GrantedFlags;
            }

            return flags;
        }

        /// <summary>
        /// 全装備の合計重量を返す。
        /// </summary>
        public int GetTotalWeight()
        {
            int total = 0;

            if (_weapon != null)
            {
                total += _weapon.Weight;
            }

            if (_shield != null)
            {
                total += _shield.Weight;
            }

            if (_core != null)
            {
                total += _core.Weight;
            }

            return total;
        }
    }
}
