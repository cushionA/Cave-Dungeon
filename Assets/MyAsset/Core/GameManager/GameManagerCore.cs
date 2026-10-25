using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// GameManagerのコアロジック。MonoBehaviour非依存でテスト可能。
    /// SoACharaDataDic保持、GameEventsハブ、サブマネージャー管理。
    /// </summary>
    public class GameManagerCore : IDisposable
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private StatusEffectSystem _statusEffects;
        private EquipmentStore _equipment;
        private ActionExecutionSystem _actions;
        private List<IGameSubManager> _subManagers;
        private bool _initialized;
        private bool _disposed;

        public SoACharaDataDic Data => _data;
        public GameEvents Events => _events;
        public StatusEffectSystem StatusEffects => _statusEffects;
        public EquipmentStore Equipment => _equipment;
        public ActionExecutionSystem Actions => _actions;
        public bool IsInitialized => _initialized;

        public GameManagerCore()
        {
            _subManagers = new List<IGameSubManager>();
            _events = new GameEvents();
        }

        /// <summary>
        /// 初期化。SoAコンテナを生成し、登録済みサブマネージャーをInitOrder順に初期化する。
        /// </summary>
        public void Initialize(int containerCapacity = 64)
        {
            if (_initialized)
            {
                return;
            }

            _data = new SoACharaDataDic(containerCapacity);
            _statusEffects = new StatusEffectSystem(_data, _events);
            _equipment = new EquipmentStore(_events);
            _actions = new ActionExecutionSystem(new SoAExecutionWorld(_data));

            // InitOrder昇順でソートして初期化
            _subManagers.Sort((a, b) => a.InitOrder.CompareTo(b.InitOrder));
            foreach (IGameSubManager mgr in _subManagers)
            {
                mgr.Initialize(_data, _events);
            }

            _initialized = true;
        }

        /// <summary>サブマネージャーを登録する（Initialize前に呼ぶ）</summary>
        public void RegisterSubManager(IGameSubManager manager)
        {
            _subManagers.Add(manager);
        }

        /// <summary>キャラクター登録</summary>
        public int RegisterCharacter(int hash, CharacterVitals vitals, CombatStats combat,
            CharacterFlags flags, MoveParams move, ManagedCharacter managed = null,
            CharacterDefenseState defenseState = default)
        {
            CharacterBaseStats basis = CharacterBaseStats.FromValues(vitals, combat, flags, move);
            CharacterGrowth growth = new CharacterGrowth { level = Math.Max(1, vitals.level) };
            vitals.level = growth.level;
            return RegisterCharacterData(hash, basis, growth, vitals, flags, managed, defenseState);
        }

        public int RegisterCharacter(int hash, in CharacterRegistration registration, ManagedCharacter managed = null)
        {
            CharacterVitals vitals = CharacterStatCalculator.CreateVitals(registration.baseStats, registration.growth);
            CharacterFlags flags = registration.flags;
            flags.AbilityFlags = registration.baseStats.abilityFlags;
            return RegisterCharacterData(hash, registration.baseStats, registration.growth,
                vitals, flags, managed, registration.defenseState);
        }

        private int RegisterCharacterData(int hash, in CharacterBaseStats basis, in CharacterGrowth growth,
            CharacterVitals vitals, CharacterFlags flags, ManagedCharacter managed, CharacterDefenseState defenseState)
        {
            int index = _data.Add(hash, vitals, basis.combat, flags, basis.movement,
                default, default, default, managed);
            _data.GetBaseStats(hash) = basis;
            _data.GetGrowth(hash) = growth;
            defenseState.maxArmor = vitals.maxArmor;
            defenseState.previousActState = flags.ActState;
            defenseState.continuousJustGuardUntil = -1f;
            _data.GetDefenseState(hash) = defenseState;
            _statusEffects.Register(hash);
            _equipment.Register(hash);
            _actions.RegisterActor(hash);
            _events.FireCharacterRegistered(hash);
            return index;
        }

        public void ApplyCharacterGrowth(int hash, in CharacterGrowth growth)
        {
            CharacterVitals vitals = CharacterStatCalculator.RecalculateVitals(
                _data.GetBaseStats(hash), growth, _data.GetVitals(hash));
            _data.GetGrowth(hash) = growth;
            _data.GetVitals(hash) = vitals;
            _events.FireCharacterStatsChanged(hash);
        }

        /// <summary>キャラクター削除</summary>
        public void UnregisterCharacter(int hash)
        {
            _actions.RemoveActor(hash);
            _statusEffects.Unregister(hash);
            _equipment.Unregister(hash);
            _data.Remove(hash);
            _events.FireCharacterRemoved(hash);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (IGameSubManager mgr in _subManagers)
            {
                mgr.Dispose();
            }

            _actions?.Dispose();
            _equipment?.Dispose();
            _statusEffects?.Dispose();
            _data?.Dispose();
            _events?.Dispose();
            _subManagers.Clear();
            _initialized = false;
            _disposed = true;
        }
    }
}
