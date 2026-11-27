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
            [SerializeField, Tooltip("騎士の表示リグに置く武器の子オブジェクト。")]
            private GameObject visual;
            [SerializeField] private bool startingOwned;

            public int ItemId => itemId;
            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? attack?.attackName : displayName;
            internal EquipmentDefinition Equipment => equipment;
            internal AttackInfo Attack => attack;
            internal GameObject Visual => visual;
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

        public enum CooperationEffectKind { StatusEffect = 0, ReflectionShield = 1 }

        internal readonly struct CooperationEffect
        {
            internal readonly CooperationEffectKind Kind;
            internal readonly StatusEffectInfo Status;
            internal readonly float ShieldDuration;
            internal readonly float ShieldRadius;

            internal CooperationEffect(CooperationEffectKind kind, StatusEffectInfo status, float shieldDuration, float shieldRadius)
            {
                Kind = kind;
                Status = status;
                ShieldDuration = shieldDuration;
                ShieldRadius = shieldRadius;
            }

            internal bool IsValid
            {
                get
                {
                    switch (Kind)
                    {
                        case CooperationEffectKind.ReflectionShield:
                            return ShieldDuration > 0f && float.IsFinite(ShieldDuration)
                                && ShieldRadius > 0f && float.IsFinite(ShieldRadius);
                        case CooperationEffectKind.StatusEffect:
                            return (Status.effect is StatusEffectId.AttackUp or StatusEffectId.DefenseUp)
                                && float.IsFinite(Status.duration) && Status.duration > 0f
                                && Status.modifier > 0f && Status.modifier <= 1f && Status.tickDamage == 0f
                                && float.IsFinite(Status.accumulateValue) && float.IsFinite(Status.tickInterval);
                        default:
                            return false;
                    }
                }
            }
        }

        [Serializable]
        public sealed class CooperationOption
        {
            [SerializeField, Min(0)] private int itemId;
            [SerializeField] private string displayName;
            [SerializeField] private CooperationEffectKind effectKind;
            [SerializeField] private StatusEffectInfo effect;
            [SerializeField, Min(0f)] private float shieldDuration;
            [SerializeField, Min(0f)] private float shieldRadius;
            [SerializeField] private bool startingOwned;

            public int ItemId => itemId;
            public string DisplayName => displayName;
            internal CooperationEffect Effect => new(effectKind, effect, shieldDuration, shieldRadius);
            internal bool StartingOwned => startingOwned;
        }

        [SerializeField] private WeaponOption[] weapons = Array.Empty<WeaponOption>();
        [SerializeField] private CoreOption[] cores = Array.Empty<CoreOption>();
        [SerializeField] private SpellOption[] spells = Array.Empty<SpellOption>();
        [SerializeField] private CooperationOption[] cooperationOptions = Array.Empty<CooperationOption>();
        [SerializeField] private int startingWeaponId = PlayerCombatLoadoutSelection.k_EmptyItem;
        [SerializeField] private int startingCoreId = PlayerCombatLoadoutSelection.k_EmptyItem;
        [SerializeField] private int startingSpellId = PlayerCombatLoadoutSelection.k_EmptyItem;
        [SerializeField] private int startingCooperationId = PlayerCombatLoadoutSelection.k_EmptyItem;

        private CharacterActor3D _actor;
        private PlayerCharacterInput3D _input;
        private SaveManager _saves;
        private ActorRef _equipmentOwner;
        private ActorRef _weaponVisualOwner;
        private WeaponOption _shownWeapon;
        private long _weaponVisualDeferredBy;
        private EquipmentDefinition _appliedWeapon;
        private EquipmentDefinition _appliedCore;

        public IReadOnlyList<WeaponOption> Weapons => weapons;
        public IReadOnlyList<CoreOption> Cores => cores;
        public IReadOnlyList<SpellOption> Spells => spells;
        public IReadOnlyList<CooperationOption> CooperationOptions => cooperationOptions ?? Array.Empty<CooperationOption>();
        public int CurrentWeaponId => GameManager.CombatLoadout?.WeaponId ?? PlayerCombatLoadoutSelection.k_EmptyItem;
        public int CurrentCoreId => GameManager.CombatLoadout?.CoreId ?? PlayerCombatLoadoutSelection.k_EmptyItem;
        public int CurrentCooperationId => GameManager.CombatLoadout?.CooperationId ?? PlayerCombatLoadoutSelection.k_EmptyItem;
        internal bool HasCooperationOptions => cooperationOptions != null && cooperationOptions.Length > 0;
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
            _weaponVisualOwner = default;
            _shownWeapon = null;
            _weaponVisualDeferredBy = 0;
            ApplyEquipment(equipmentOwner, null, ref _appliedWeapon, EquipSlot.Weapon);
            ApplyEquipment(equipmentOwner, null, ref _appliedCore, EquipSlot.Core);
        }

        public bool OwnsWeapon(int itemId) => FindWeapon(itemId) != null && Owns(itemId, ItemCategory.Weapon);
        public bool OwnsCore(int itemId) => FindCore(itemId) != null && Owns(itemId, ItemCategory.Core);
        public bool OwnsSpell(int itemId) => FindSpell(itemId) != null && Owns(itemId, ItemCategory.PlayerMagic);
        public bool OwnsCooperation(int itemId) => FindCooperation(itemId) != null && Owns(itemId, ItemCategory.CompanionMagic);

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

        public bool TryEquipCooperation(int itemId)
        {
            if (!Synchronize() || !OwnsCooperation(itemId)) { return false; }
            PlayerCombatLoadoutSelection selection = GameManager.CombatLoadout;
            int previous = selection.CooperationId;
            if (!selection.IsCooperationInitialized || !selection.TryEquipCooperation(itemId)) { return false; }
            if (_input != null && previous != selection.CooperationId) { _input.ClearPendingCooperation(); }
            return true;
        }

        internal bool TryResolveCooperation(out int itemId, out CooperationEffect effect)
        {
            itemId = PlayerCombatLoadoutSelection.k_EmptyItem;
            effect = default;
            PlayerCombatLoadoutSelection selection = GameManager.CombatLoadout;
            if (!isActiveAndEnabled || _actor == null || !_actor.IsAlive || selection == null || !HasCooperationOptions)
            {
                return false;
            }
            InitializeCooperation(selection);
            int equipped = selection.CooperationId;
            CooperationOption option = FindCooperation(equipped);
            if (option == null || !Owns(equipped, ItemCategory.CompanionMagic)) { return false; }
            itemId = equipped;
            effect = option.Effect;
            return true;
        }

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
            if (_input != null)
            {
                _input.ClearLoadoutCombatInput();
                _input.ClearPendingCooperation();
            }
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
            InitializeCooperation(selection);

            if (_equipmentOwner != owner)
            {
                _equipmentOwner = owner;
                _appliedWeapon = null;
                _appliedCore = null;
            }
            int weaponId = selection.WeaponId;
            WeaponOption weapon = Owns(weaponId, ItemCategory.Weapon) ? FindWeapon(weaponId) : null;
            ApplyEquipment(owner, weapon?.Equipment, ref _appliedWeapon, EquipSlot.Weapon);
            if (!isActiveAndEnabled || _actor.Actor != owner) { return false; }
            SynchronizeWeaponVisual(owner, weapon);
            if (!isActiveAndEnabled || _actor.Actor != owner) { return false; }
            int coreId = selection.CoreId;
            EquipmentDefinition core = Owns(coreId, ItemCategory.Core) ? FindCore(coreId)?.Equipment : null;
            ApplyEquipment(owner, core, ref _appliedCore, EquipSlot.Core);
            return isActiveAndEnabled && _actor.Actor == owner;
        }

        private void SynchronizeWeaponVisual(ActorRef owner, WeaponOption weapon)
        {
            if (_weaponVisualOwner == owner)
            {
                if (ReferenceEquals(_shownWeapon, weapon))
                {
                    _weaponVisualDeferredBy = 0;
                    return;
                }
                // 持ち替え時の技が完了するか、新しい技に切り替わるまで旧武器を残す。
                if (GameManager.Actions?.Execution.GetPrimary(owner.Hash, out ActionExecution action) == true
                    && (action.definition.gameplay & (ActionGameplayFlags.Attack | ActionGameplayFlags.Cast)) != 0)
                {
                    if (_weaponVisualDeferredBy == 0) { _weaponVisualDeferredBy = action.instanceId; }
                    if (_weaponVisualDeferredBy == action.instanceId) { return; }
                }
            }

            _weaponVisualOwner = owner;
            _shownWeapon = weapon;
            _weaponVisualDeferredBy = 0;
            GameObject selected = weapon?.Visual;
            foreach (WeaponOption option in weapons)
            {
                GameObject visual = option?.Visual;
                if (visual == null || visual == gameObject || !visual.transform.IsChildOf(transform)) { continue; }
                bool visible = visual == selected;
                if (visual.activeSelf != visible) { visual.SetActive(visible); }
            }
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

        private void InitializeCooperation(PlayerCombatLoadoutSelection selection)
        {
            if (!HasCooperationOptions || selection.IsCooperationInitialized) { return; }
            InventoryManager inventory = GameManager.Inventory;
            if (inventory == null) { return; }
            int missingStarters = 0;
            foreach (CooperationOption option in cooperationOptions)
            {
                if (IsCooperationStarter(option) && !Owns(option.ItemId, ItemCategory.CompanionMagic))
                {
                    missingStarters++;
                }
            }
            if (missingStarters > InventoryManager.k_MaxSlotCount - inventory.ItemCount) { return; }
            foreach (CooperationOption option in cooperationOptions)
            {
                if (IsCooperationStarter(option) && !GrantStarter(option.ItemId, ItemCategory.CompanionMagic)) { return; }
            }
            if (!selection.TryInitializeCooperation()) { return; }
            if (FindCooperation(startingCooperationId) != null) { selection.TryEquipCooperation(startingCooperationId); }
        }

        private bool IsCooperationStarter(CooperationOption option) =>
            option?.StartingOwned == true && FindCooperation(option.ItemId) == option;

        private static bool Owns(int itemId, ItemCategory category) => itemId >= 0 && GameManager.Inventory?.GetCount(itemId, category) > 0;

        private static bool GrantStarter(int itemId, ItemCategory category) =>
            Owns(itemId, category) || GameManager.Inventory.Add(itemId, category, 1, 1) == 1;

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

        private CooperationOption FindCooperation(int id)
        {
            if (!HasCooperationOptions) { return null; }
            CooperationOption option = FindUnique(cooperationOptions, id, static entry => entry.ItemId);
            return option != null && option.Effect.IsValid ? option : null;
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
