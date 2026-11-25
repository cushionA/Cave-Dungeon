using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>騎士のセーブ済み装備と魔法枠を、所持品と3Dの戦闘入力に接続する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class PlayerCombatLoadout3D : MonoBehaviour
    {
        [Serializable]
        public sealed class WeaponOption
        {
            [SerializeField, Min(0)] private int itemId;
            [SerializeField] private string displayName;
            [SerializeField] private EquipmentDefinition equipment;
            [SerializeField] private AttackInfo attack;
            [SerializeField] private bool startingOwned;

            public int ItemId => itemId;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? attack?.attackName : displayName;
            internal EquipmentDefinition Equipment => equipment;
            internal AttackInfo Attack => attack;
            internal bool StartingOwned => startingOwned;
        }

        [Serializable]
        public sealed class CoreOption
        {
            [SerializeField, Min(0)] private int itemId;
            [SerializeField] private string displayName;
            [SerializeField] private EquipmentDefinition equipment;
            [SerializeField] private bool startingOwned;

            public int ItemId => itemId;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? equipment?.name : displayName;
            internal EquipmentDefinition Equipment => equipment;
            internal bool StartingOwned => startingOwned;
        }

        [Serializable]
        public sealed class SpellOption
        {
            [SerializeField, Min(0)] private int itemId;
            [SerializeField] private string displayName;
            [SerializeField] private AttackInfo attack;
            [SerializeField] private bool startingOwned;

            public int ItemId => itemId;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? attack?.attackName : displayName;
            internal AttackInfo Attack => attack;
            internal bool StartingOwned => startingOwned;
        }

        [SerializeField] private WeaponOption[] weapons = Array.Empty<WeaponOption>();
        [SerializeField] private CoreOption[] cores = Array.Empty<CoreOption>();
        [SerializeField] private SpellOption[] spells = Array.Empty<SpellOption>();
        [SerializeField] private int startingWeaponId = PlayerCombatLoadoutSelection.k_EmptyItem;
        [SerializeField] private int startingCoreId = PlayerCombatLoadoutSelection.k_EmptyItem;
        [SerializeField] private int startingSpellId = PlayerCombatLoadoutSelection.k_EmptyItem;

        private CharacterActor3D _actor;
        private PlayerCharacterInput3D _input;
        private SaveManager _saves;
        private ActorRef _equipmentOwner;
        private EquipmentDefinition _appliedWeapon;
        private EquipmentDefinition _appliedCore;

        public IReadOnlyList<WeaponOption> Weapons => weapons;
        public IReadOnlyList<CoreOption> Cores => cores;
        public IReadOnlyList<SpellOption> Spells => spells;
        public int CurrentWeaponId => GameManager.CombatLoadout?.WeaponId ?? PlayerCombatLoadoutSelection.k_EmptyItem;
        public int CurrentCoreId => GameManager.CombatLoadout?.CoreId ?? PlayerCombatLoadoutSelection.k_EmptyItem;
        public int SpellSlotCount => PlayerCombatLoadoutSelection.k_SpellSlotCount;
        public int ActiveSpellSlot => GameManager.CombatLoadout?.ActiveSpellSlot ?? 0;
        public int GetSpellId(int slot) => GameManager.CombatLoadout?.GetSpellId(slot) ?? PlayerCombatLoadoutSelection.k_EmptyItem;

        private void Awake()
        {
            _actor = GetComponent<CharacterActor3D>();
            _input = GetComponent<PlayerCharacterInput3D>();
        }

        private void OnEnable() => ObserveSaves();

        private void Start() => Synchronize();

        private void Update() => Synchronize();

        private void OnDisable()
        {
            if (_saves != null) { _saves.Loaded -= OnSlotLoaded; }
            _saves = null;
            ActorRef equipmentOwner = _equipmentOwner;
            _equipmentOwner = default;
            ApplyEquipment(equipmentOwner, null, ref _appliedWeapon, EquipSlot.Weapon);
            ApplyEquipment(equipmentOwner, null, ref _appliedCore, EquipSlot.Core);
        }

        public bool OwnsWeapon(int itemId) => FindWeapon(itemId) != null && Owns(itemId, ItemCategory.Weapon);
        public bool OwnsCore(int itemId) => FindCore(itemId) != null && Owns(itemId, ItemCategory.Core);
        public bool OwnsSpell(int itemId) => FindSpell(itemId) != null && Owns(itemId, ItemCategory.PlayerMagic);

        public bool TryEquipWeapon(int itemId)
        {
            if (!Synchronize() || !OwnsWeapon(itemId) || !GameManager.CombatLoadout.TryEquipWeapon(itemId)) { return false; }
            Synchronize();
            return true;
        }

        public bool TryEquipCore(int itemId)
        {
            if (!Synchronize() || !OwnsCore(itemId) || !GameManager.CombatLoadout.TryEquipCore(itemId)) { return false; }
            Synchronize();
            return true;
        }

        public bool TryAssignSpell(int slot, int itemId) =>
            Synchronize() && OwnsSpell(itemId) && GameManager.CombatLoadout.TryAssignSpell(slot, itemId);

        public bool TrySelectSpellSlot(int slot) => Synchronize() && GameManager.CombatLoadout.TrySelectSpellSlot(slot);

        public bool TryClearSpell(int slot) => Synchronize() && GameManager.CombatLoadout.TryClearSpell(slot);

        internal AttackInfo ResolveWeaponAttack() => Synchronize() && Owns(CurrentWeaponId, ItemCategory.Weapon)
            ? FindWeapon(CurrentWeaponId)?.Attack : null;

        internal AttackInfo ResolveSpellAttack()
        {
            if (!Synchronize()) { return null; }
            int selected = GetSpellId(ActiveSpellSlot);
            return Owns(selected, ItemCategory.PlayerMagic) ? FindSpell(selected)?.Attack : null;
        }

        private void ObserveSaves()
        {
            SaveManager saves = GameManager.Saves;
            if (ReferenceEquals(saves, _saves)) { return; }
            if (_saves != null) { _saves.Loaded -= OnSlotLoaded; }
            _saves = saves;
            if (_saves != null) { _saves.Loaded += OnSlotLoaded; }
        }

        private void OnSlotLoaded(int _)
        {
            if (_input != null) { _input.ClearLoadoutCombatInput(); }
            Synchronize();
        }

        private bool Synchronize()
        {
            if (!isActiveAndEnabled || _actor == null || !_actor.IsAlive || GameManager.CombatLoadout == null) { return false; }
            ActorRef owner = _actor.Actor;
            if ((GameManager.Data.GetFlags(owner.Hash).Feature & CharacterFeature.Player) == 0) { return false; }
            ObserveSaves();
            PlayerCombatLoadoutSelection selection = GameManager.CombatLoadout;
            if (selection.TryInitialize()) { AssignStartingLoadout(selection); }

            if (_equipmentOwner != owner)
            {
                _equipmentOwner = owner;
                _appliedWeapon = null;
                _appliedCore = null;
            }
            int weaponId = selection.WeaponId;
            EquipmentDefinition weapon = Owns(weaponId, ItemCategory.Weapon) ? FindWeapon(weaponId)?.Equipment : null;
            ApplyEquipment(owner, weapon, ref _appliedWeapon, EquipSlot.Weapon);
            if (!isActiveAndEnabled || _actor.Actor != owner) { return false; }
            int coreId = selection.CoreId;
            EquipmentDefinition core = Owns(coreId, ItemCategory.Core) ? FindCore(coreId)?.Equipment : null;
            ApplyEquipment(owner, core, ref _appliedCore, EquipSlot.Core);
            return isActiveAndEnabled && _actor.Actor == owner;
        }

        private static void ApplyEquipment(ActorRef owner, EquipmentDefinition desired, ref EquipmentDefinition applied, EquipSlot slot)
        {
            EquipmentDefinition previous = applied;
            applied = desired;
            if (!owner.IsValid || GameManager.Actors?.IsCurrent(owner) != true) { return; }
            EquipmentStore store = GameManager.Equipment;
            IReadOnlyEquipmentHolder holder = store.GetHolder(owner.Hash);
            IEquippable equipped = slot == EquipSlot.Weapon ? holder.Weapon : holder.Core;
            if (desired != null && !ReferenceEquals(desired, equipped)) { store.Equip(owner.Hash, desired); }
            if (desired == null && previous != null && ReferenceEquals(previous, equipped)) { store.Unequip(owner.Hash, slot); }
        }

        private void AssignStartingLoadout(PlayerCombatLoadoutSelection selection)
        {
            foreach (WeaponOption option in weapons)
            {
                if (option?.StartingOwned == true && FindWeapon(option.ItemId) == option) { GrantStarter(option.ItemId, ItemCategory.Weapon); }
            }
            foreach (CoreOption option in cores)
            {
                if (option?.StartingOwned == true && FindCore(option.ItemId) == option) { GrantStarter(option.ItemId, ItemCategory.Core); }
            }
            foreach (SpellOption option in spells)
            {
                if (option?.StartingOwned == true && FindSpell(option.ItemId) == option) { GrantStarter(option.ItemId, ItemCategory.PlayerMagic); }
            }
            if (FindWeapon(startingWeaponId) != null) { selection.TryEquipWeapon(startingWeaponId); }
            if (FindCore(startingCoreId) != null) { selection.TryEquipCore(startingCoreId); }
            if (FindSpell(startingSpellId) != null) { selection.TryAssignSpell(0, startingSpellId); }
        }

        private static bool Owns(int itemId, ItemCategory category) => itemId >= 0 && GameManager.Inventory?.GetCount(itemId, category) > 0;

        private static void GrantStarter(int itemId, ItemCategory category)
        {
            if (!Owns(itemId, category)) { GameManager.Inventory.Add(itemId, category, 1, 1); }
        }

        private WeaponOption FindWeapon(int id)
        {
            WeaponOption option = FindUnique(weapons, id, static entry => entry.ItemId);
            return option != null && option.Equipment != null && option.Equipment.Slot == EquipSlot.Weapon
                && Has3DAttack(option.Attack) ? option : null;
        }

        private CoreOption FindCore(int id)
        {
            CoreOption option = FindUnique(cores, id, static entry => entry.ItemId);
            return option != null && option.Equipment != null && option.Equipment.Slot == EquipSlot.Core ? option : null;
        }

        private SpellOption FindSpell(int id)
        {
            SpellOption option = FindUnique(spells, id, static entry => entry.ItemId);
            return option != null && Has3DAttack(option.Attack) && option.Attack.category == AttackCategory.Magic ? option : null;
        }

        private static bool Has3DAttack(AttackInfo attack) => attack != null && attack.attack3D.shape != AttackShape3D.None;

        private static T FindUnique<T>(T[] options, int id, Func<T, int> getId) where T : class
        {
            if (id < 0) { return null; }
            T match = null;
            foreach (T option in options)
            {
                if (option == null || getId(option) != id) { continue; }
                if (match != null) { return null; }
                match = option;
            }
            return match;
        }
    }
}
