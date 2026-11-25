using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>遠征の経験値と能力配分を、現在の騎士へ反映する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class PlayerProgression3D : MonoBehaviour
    {
        private const string k_ExplorationFlagPrefix = "expedition.experience.discovery.";

        [SerializeField] private CharacterActor3D companion;
        [SerializeField] private ExpeditionSummonDeployment3D summonDeployment;

        private readonly List<ExpeditionSummonDeployment3D.DeployedSummon> _summons = new();
        private CharacterActor3D _actor;
        private GameManager _world;
        private GameEvents _events;
        private SaveManager _saves;
        private LevelUpLogic _level;
        private IDisposable _registration;
        private IDisposable _deaths;

        public int Level => _level?.Level ?? 1;
        public int CurrentExp => _level?.CurrentExp ?? 0;
        public int ExpForNextLevel => _level != null ? _level.GetExpForNextLevel() : 100;
        public int AvailablePoints => _level?.AvailablePoints ?? 0;
        public StatModifier AllocatedStats => _level?.AllocatedStats ?? default;

        private void Awake() => _actor = GetComponent<CharacterActor3D>();
        private void OnEnable() => Connect();
        private void Start() => Connect();

        private void Connect()
        {
            if (_saves != null || GameManager.Instance == null) { return; }
            _world = GameManager.Instance;
            _events = GameManager.Events;
            _saves = GameManager.Saves;
            _level = GameManager.PlayerLevel;
            _registration = _events.OnCharacterRegistered.Subscribe(OnCharacterRegistered);
            _deaths = _events.OnCharacterDeathResolved.Subscribe(OnDeathResolved);
            _saves.Loaded += OnSaveLoaded;
            ApplyGrowth();
        }

        private void OnDisable()
        {
            _registration?.Dispose();
            _deaths?.Dispose();
            if (_saves != null) { _saves.Loaded -= OnSaveLoaded; }
            _registration = null;
            _deaths = null;
            _world = null;
            _events = null;
            _saves = null;
            _level = null;
        }

        public bool TryAllocatePoint(StatType stat)
        {
            if (!TryReadAvailablePlayer(out _) || !_level.AllocatePoint(stat, _actor.Definition.statCaps)) { return false; }
            ApplyGrowth();
            return true;
        }

        /// <summary>作者が指定した発見の経験値を、同じ保存状態で一度だけ受け取る。</summary>
        public bool TryAwardExploration(string rewardId, int experience)
        {
            if (experience <= 0 || string.IsNullOrWhiteSpace(rewardId)
                || !TryReadAvailablePlayer(out ActorRef player)) { return false; }
            FlagManager flags = GameManager.Flags;
            string key = k_ExplorationFlagPrefix + rewardId.Trim();
            if (flags.GetGlobalFlag(key)) { return false; }
            flags.SetGlobalFlag(key, true);
            Award(player, experience);
            return true;
        }

        private bool TryReadPlayer(out ActorRef player)
        {
            player = default;
            if (_world == null || _world != GameManager.Instance || _actor == null || _level == null) { return false; }
            player = _actor.Actor;
            return player.IsValid && (GameManager.Data.GetFlags(player.Hash).Feature & CharacterFeature.Player) != 0;
        }

        private bool TryReadAvailablePlayer(out ActorRef player) => TryReadPlayer(out player) && _actor.IsAlive
            && !GameManager.Data.GetFlags(player.Hash).IsVanished;

        private void OnCharacterRegistered(int hash)
        {
            if (hash == _actor.ObjectHash) { ApplyGrowth(); }
        }

        private void OnSaveLoaded(int _) => ApplyGrowth();

        private void ApplyGrowth()
        {
            if (TryReadPlayer(out ActorRef player)) { _world.ApplyCharacterGrowth(player.Hash, _level.Growth); }
        }

        private void OnDeathResolved(ActorDeathEvent death)
        {
            if (!TryReadPlayer(out ActorRef player) || !GameManager.Actors.IsCurrent(death.Dead)) { return; }

            SoACharaDataDic data = GameManager.Data;
            CharacterFlags dead = data.GetFlags(death.Dead.Hash);
            if (dead.ActState != ActState.Dead || (dead.Belong & CharacterBelong.Enemy) == 0
                || data.GetVitals(death.Dead.Hash).currentHp > 0
                || data.GetManaged(death.Dead.Hash) is not CharacterActor3D defeated || defeated == null
                || defeated.gameObject.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene()
                || !defeated.TryGetComponent(out DefeatExperience3D reward)
                || !reward.TryClaim(death.Dead, out int experience)) { return; }

            if (!GameManager.Actors.IsCurrent(death.Killer) || !IsPartyKiller(death.Killer, player)) { return; }
            CharacterBelong killerBelong = data.GetFlags(death.Killer.Hash).Belong;
            if ((killerBelong & CharacterBelong.Ally) != 0 && AttackTargetRules.CanAffect(death.Killer, killerBelong,
                death.Dead, dead.Belong, AttackTargetRelation.Hostile)) { Award(player, experience); }
        }

        private bool IsPartyKiller(ActorRef killer, ActorRef player)
        {
            if (killer == player || (companion != null && companion.Actor == killer)) { return true; }
            if (summonDeployment == null) { return false; }
            summonDeployment.CopyActiveSummons(_summons);
            foreach (ExpeditionSummonDeployment3D.DeployedSummon summon in _summons)
            {
                if (summon.Actor == killer) { return true; }
            }
            return false;
        }

        private void Award(ActorRef player, int experience)
        {
            if (_level.Level >= _level.MaxLevel) { return; }
            ulong revision = _saves.LoadRevision;
            int gained = _level.AddExp(experience);
            int newLevel = _level.Level;
            if (gained > 0) { ApplyGrowth(); }
            if (!TryReadPlayer(out ActorRef current) || current != player || _saves.LoadRevision != revision) { return; }
            _events.FireExpGained(player.Hash, experience);
            if (gained > 0 && TryReadPlayer(out current) && current == player && _saves.LoadRevision == revision)
            {
                _events.FireLevelUp(player.Hash, newLevel);
            }
        }
    }
}
