using System;
using System.Collections.Generic;
using Game.Core;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>3Dのシスターが騎士に追従し、戦術と連携を同じ行動実行へ渡す。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class CompanionController3D : MonoBehaviour
    {
        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private EncounterCombat3D groupCombat;
        [SerializeField] private CompanionAIConfig tactics;
        [SerializeField] private AttackInfo[] attacks = Array.Empty<AttackInfo>();
        [SerializeField, Min(0.1f)] private float followDistance = 2.2f;
        [SerializeField, Min(0.1f)] private float sightRange = 15f;
        [SerializeField] private LayerMask obstructionLayers = 1;
        [SerializeField, Min(0.1f)] private float cooperationRange = 6f;
        [SerializeField, Min(0.05f)] private float cooperationDuration = 0.35f;
        [SerializeField, Min(1)] private int cooperationMpCost = 12;
        [SerializeField, Min(0f)] private float cooperationCooldown = 8f;
        [SerializeField, Min(0.1f)] private float wardDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float wardDefense = 0.4f;
        [SerializeField, Min(0)] private int initialReserveMp = 30;
        [SerializeField] private CompanionMpSettings mpSettings = new CompanionMpSettings
        {
            baseRecoveryRate = 2f, vanishRecoveryMultiplier = 2f, returnThresholdRatio = 0.25f, maxReserveMp = 60,
        };
        [SerializeField] private GameObject presentation;

        private readonly Dictionary<int, CoopCooldownTracker> _cooperationCooldowns = new();
        private CharacterActor3D _actor;
        private PlayerCombatLoadout3D _cooperationLoadout;
        private ActorRef _owner;
        private GameActionRuntime _actions;
        private CompanionMpManager _mp;
        private CompanionTactics3D _tactics;
        private ExpeditionSummonDeployment3D _deployment;
        private IDisposable _actorRemoved;
        private bool _groupWasDriving;
        private int _freeCast;
        private int _paidCast;
        private long _cast;
        private bool _vanishPresentation;
        private bool _bodyBeforeVanish;
        private bool _presentationBeforeVanish;

        public bool IsCooperating => _actor != null && _actor.Actor == _owner && _cast != 0
            && _actions.Execution.TryGetInstance(_cast, out _);
        public int ActiveModeIndex => _tactics?.ActiveModeIndex ?? 0;
        public float CooperationCooldownRemaining
        {
            get
            {
                int id = _cooperationLoadout != null && _cooperationLoadout.HasCooperationOptions
                    ? _cooperationLoadout.CurrentCooperationId : PlayerCombatLoadoutSelection.k_EmptyItem;
                return _cooperationCooldowns.TryGetValue(id, out CoopCooldownTracker cooldown)
                    ? cooldown.GetRemaining(Time.fixedTime) : 0f;
            }
        }
        public IReadOnlyList<AttackInfo> AvailableAttacks => attacks;

        private void Awake()
        {
            _actor = GetComponent<CharacterActor3D>();
            _cooperationLoadout = knight != null ? knight.GetComponent<PlayerCombatLoadout3D>() : null;
            _deployment = GetComponent<ExpeditionSummonDeployment3D>();
            GetComponent<DamageReceiver>().BindCompanion(this);
        }

        private void OnDisable()
        {
            if (gameObject.activeInHierarchy && _owner.IsValid && _actor != null && _actor.Actor == _owner
                && ReferenceEquals(_actions, GameManager.Actions))
            {
                // AIだけを止めても、同じ個体のMPと消滅表示は維持する。
                _actions.ReleaseHeldInputs(_owner.Hash);
                return;
            }
            ReleaseActor();
        }

        private void OnDestroy() => ReleaseActor();

        internal CompanionMpManager GetDamageBarrier(ActorRef target) =>
            _actor != null && _actor.Actor == target && BindActor() ? _mp : null;

        public CompanionAIConfig GetAIConfig() => AIConfigurationCopy.Clone(tactics);

        public void SetAIConfig(CompanionAIConfig config)
        {
            tactics = AIConfigurationCopy.Clone(config);
            _tactics?.SetConfig(tactics);
        }

        public static bool SupportsAIAction(ActionSlot slot) => CompanionTactics3D.Supports(ActionCommand.FromLegacySlot(slot).kind);

        public bool TryCooperate(CharacterActor3D requester)
        {
            if (!isActiveAndEnabled || requester != knight || !BindActor() || IsCooperating
                || !CanSupport(requester, out ActorRef target)) { return false; }

            ActorRef source = _owner;
            GameActionRuntime actions = _actions;
            if (!TryResolveCooperation(out int cooperationId, out StatusEffectInfo effect)
                || !IsCurrentCooperationSource(source, actions) || requester == null || requester.Actor != target) { return false; }
            if (!_cooperationCooldowns.TryGetValue(cooperationId, out CoopCooldownTracker cooldown))
            {
                cooldown = new CoopCooldownTracker();
                _cooperationCooldowns.Add(cooperationId, cooldown);
            }

            float now = Time.fixedTime;
            int availableMp = Mathf.FloorToInt(_mp.CurrentMp);
            bool free = cooldown.IsCooldownReady(now);
            if (!free && availableMp < cooperationMpCost) { return false; }
            if (!actions.Execution.TryStart(new ActionRequest
            {
                ownerHash = source.Hash, ownerRef = source, targetHash = target.Hash, targetRef = target,
                definitionId = free ? _freeCast : _paidCast, speedMultiplier = 1f,
            }, out long cast)) { return false; }

            if (!IsCurrentCooperationSource(source, actions)) { return false; }
            // The accepted caster motion can finish even if a start callback retired its recipient.
            _cast = cast;
            if (requester == null || requester.Actor != target) { return false; }
            cooldown.TryActivate(now, cooperationMpCost, availableMp, cooperationCooldown);
            GameManager.StatusEffects.Apply(target.Hash, effect, source.Hash);
            return true;
        }

        private bool TryResolveCooperation(out int id, out StatusEffectInfo effect)
        {
            if (_cooperationLoadout == null && knight != null)
            {
                _cooperationLoadout = knight.GetComponent<PlayerCombatLoadout3D>();
            }
            if (_cooperationLoadout != null && _cooperationLoadout.HasCooperationOptions)
            {
                if (_cooperationLoadout.TryResolveCooperation(out id, out effect)) { return true; }
                if (GameManager.CombatLoadout?.IsCooperationInitialized != false) { return false; }
            }

            id = PlayerCombatLoadoutSelection.k_EmptyItem;
            effect = new StatusEffectInfo
            {
                effect = StatusEffectId.DefenseUp, modifier = wardDefense, duration = wardDuration, maxStack = 1,
            };
            return true;
        }

        private bool IsCurrentCooperationSource(ActorRef source, GameActionRuntime actions)
            => _owner == source && _actor != null && _actor.Actor == source
                && ReferenceEquals(_actions, actions) && ReferenceEquals(GameManager.Actions, actions);

        private bool CanSupport(CharacterActor3D requester, out ActorRef target)
        {
            target = requester != null ? requester.Actor : default;
            if (!target.IsValid || !requester.IsAlive || !requester.Body.enabled || !_actor.Body.enabled
                || requester.gameObject.scene != gameObject.scene) { return false; }
            CharacterFlags owner = GameManager.Data.GetFlags(_owner.Hash);
            CharacterFlags player = GameManager.Data.GetFlags(target.Hash);
            return !owner.IsVanished && !player.IsVanished && (owner.Feature & CharacterFeature.Companion) != 0
                && (player.Feature & CharacterFeature.Player) != 0 && (owner.Belong & player.Belong & CharacterBelong.Ally) != 0
                && CompanionTactics3D.CanSee(_actor, requester, cooperationRange, obstructionLayers);
        }

        private void FixedUpdate()
        {
            if (!BindActor()) { return; }
            float delta = Time.fixedDeltaTime;
            _mp.Tick(delta);
            bool groupDriving = groupCombat != null && groupCombat.Drives(_actor);
            if (groupDriving && !_groupWasDriving) { _tactics.Stop(); }
            _groupWasDriving = groupDriving;
            if (groupDriving || _mp.IsVanished || _actor.Motor.IsFinishingEncounterJump) { return; }

            if (IsCooperating || _actions.IsMovementBlocked(_owner.Hash)
                || GameManager.Data.GetStatusEffects(_owner.Hash).BlocksActions)
            {
                _actor.Motor.Simulate(Vector3.zero, false, delta);
                return;
            }

            if (_deployment != null && _deployment.TryGetSisterRally(_actor, knight, out float rallyDistance))
            {
                _actor.Motor.Simulate(CompanionTactics3D.FollowDirection(_actor, knight, rallyDistance), false, delta);
                return;
            }

            _tactics.Evaluate(delta);
            Vector3 direction = _tactics.TryGetMovement(out Vector3 commanded) ? commanded
                : CompanionTactics3D.FollowDirection(_actor, knight, followDistance);
            _actor.Motor.Simulate(_actions.IsMovementBlocked(_owner.Hash) ? Vector3.zero : direction, false, delta);
        }

        private bool BindActor()
        {
            ActorRef actor = _actor.Actor;
            if (_owner == actor && actor.IsValid && _actor.IsAlive && ReferenceEquals(_actions, GameManager.Actions)) { return true; }
            ReleaseActor();
            if (!actor.IsValid || !_actor.IsAlive || GameManager.Actions == null) { return false; }
            _owner = actor;
            _actions = GameManager.Actions;
            _mp = new CompanionMpManager(new SoAExecutionWorld(GameManager.Data), actor.Hash, initialReserveMp, mpSettings);
            _mp.OnVanish += OnVanish;
            _mp.OnReturn += RestorePresentation;
            _tactics = new CompanionTactics3D(_actor, knight, _actions, attacks, sightRange, obstructionLayers, followDistance);
            _tactics.SetConfig(tactics);
            _freeCast = _actions.Execution.RegisterDefinition(CastDefinition(0));
            _paidCast = _actions.Execution.RegisterDefinition(CastDefinition(cooperationMpCost));
            _actorRemoved = GameManager.Events.OnCharacterRemoved.Subscribe(OnActorRemoved);
            if (_mp.IsVanished) { OnVanish(); }
            return true;
        }

        private void OnActorRemoved(int hash)
        {
            if (_owner.Hash == hash && GameManager.Actors?.IsCurrent(_owner) != true) { ReleaseActor(); }
        }

        private ActionDefinitionData CastDefinition(int cost) => new ActionDefinitionData
        {
            resources = ActionResources.Hands | ActionResources.HorizontalMovement | ActionResources.Facing,
            priority = 15, mpCost = cost, cancelPoint = -1f, cancelAfter = -1f,
            endMode = ActionEndMode.Timed, durationMode = ActionDurationMode.Fixed, duration = cooperationDuration,
            actState = ActState.AttackPrep, gameplay = ActionGameplayFlags.Cast,
        };

        private void OnVanish()
        {
            if (_vanishPresentation) { return; }
            ActorRef owner = _owner;
            GameActionRuntime actions = _actions;
            _vanishPresentation = true;
            _bodyBeforeVanish = _actor.Body.enabled;
            _actor.Body.enabled = false;
            if (presentation != null)
            {
                _presentationBeforeVanish = presentation.activeSelf;
                presentation.SetActive(false);
            }
            // 受理済みの魔法・連携は共有実行に任せ、保持入力だけを解除する。
            if (_owner == owner && _actor != null && _actor.Actor == owner && ReferenceEquals(_actions, actions))
            {
                actions.ReleaseHeldInputs(owner.Hash);
            }
        }

        private void RestorePresentation()
        {
            if (!_vanishPresentation) { return; }
            _vanishPresentation = false;
            if (_actor != null && _actor.Body != null) { _actor.Body.enabled = _bodyBeforeVanish; }
            if (presentation != null) { presentation.SetActive(_presentationBeforeVanish); }
        }

        private void CancelCast()
        {
            if (_cast != 0) { _actions.Execution.Cancel(_cast, force: true); }
            _cast = 0;
        }

        private void ReleaseActor()
        {
            _actorRemoved?.Dispose();
            _actorRemoved = null;
            RestorePresentation();
            _tactics?.Dispose();
            _tactics = null;
            _mp?.Dispose();
            _mp = null;
            if (_actions != null)
            {
                CancelCast();
                _actions.Execution.UnregisterDefinition(_freeCast);
                _actions.Execution.UnregisterDefinition(_paidCast);
            }
            _actions = null;
            _owner = default;
            _groupWasDriving = false;
            _cooperationCooldowns.Clear();
        }

#if UNITY_INCLUDE_TESTS
        internal void ConfigureForTest(CharacterActor3D leader) => knight = leader;
#endif
    }
}
