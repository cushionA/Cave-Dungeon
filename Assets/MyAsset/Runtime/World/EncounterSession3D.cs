using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    [Flags]
    public enum EncounterTraits
    {
        None = 0,
        Boss = 1 << 0,
        GroupBattle = 1 << 1,
    }

    /// <summary>3D遭遇の試行を開始・終了し、集団戦とボス戦の設定を保持する。</summary>
    [DisallowMultipleComponent]
    public class EncounterSession3D : MonoBehaviour
    {
        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private ChallengeDefinition challenge;
        [SerializeField] private bool startAutomatically = true;
        [SerializeField] private UnityEvent onCompleted = new UnityEvent();
        [SerializeField] private EncounterTraits traits;

        private readonly ChallengeRunner _runner = new ChallengeRunner();
        private ActorRef _participant;

        public EncounterTraits Traits => traits;
        public bool IsBossBattle => (traits & EncounterTraits.Boss) != 0;
        public bool IsGroupBattle => (traits & EncounterTraits.GroupBattle) != 0;
        public bool IsRunning => _runner.State == ChallengeState.Running;
        public ChallengeResult Result => _runner.GetResult();

        public event Action Started;
        public event Action<ChallengeState> Ended;

        protected CharacterActor3D Knight => knight;
        protected virtual bool IsObjectiveAvailable => true;
        protected virtual bool CompletesAutomatically => false;
        protected virtual bool HasMetCompletionCondition() => true;
        protected virtual void OnEncounterStarted() { }
        protected virtual void OnEncounterTick(float deltaTime) { }
        protected virtual void OnEncounterEnded(ChallengeState state) { }

        protected void Start()
        {
            if (startAutomatically) { TryStartEncounter(); }
        }

        protected void FixedUpdate()
        {
            if (!IsRunning) { return; }
            if (!HasCurrentParticipant())
            {
                AbortEncounter();
                return;
            }

            _runner.Tick(Time.fixedDeltaTime);
            if (!IsRunning)
            {
                NotifyEnded();
                return;
            }

            OnEncounterTick(Time.fixedDeltaTime);
            if (IsRunning && CompletesAutomatically) { CompleteIfConditionMet(); }
        }

        protected void OnDisable() => AbortEncounter();

        public bool TryStartEncounter()
        {
            if (IsRunning || !isActiveAndEnabled || challenge == null || !TryReadKnight(out ActorRef actor))
            {
                return false;
            }

            _participant = actor;
            _runner.SetPlayerHash(actor.Hash);
            _runner.Start(challenge);
            OnEncounterStarted();
            Started?.Invoke();
            return true;
        }

        public void CompleteEncounter()
        {
            if (!IsRunning) { return; }
            if (HasCurrentParticipant()) { CompleteIfConditionMet(); }
            else { AbortEncounter(); }
        }

        public void AbortEncounter()
        {
            if (_runner.Abort()) { NotifyEnded(); }
        }

        private bool HasCurrentParticipant() => TryReadKnight(out ActorRef actor) && actor == _participant;

        private bool TryReadKnight(out ActorRef actor)
        {
            actor = default;
            Scene room = gameObject.scene;
            if (!room.isLoaded || knight == null || !knight.IsAlive || knight.gameObject.scene != room
                || knight.Body == null || !knight.Body.enabled || !IsObjectiveAvailable)
            {
                return false;
            }

            actor = knight.Actor;
            CharacterFlags flags = GameManager.Data.GetFlags(actor.Hash);
            return (flags.Feature & CharacterFeature.Player) != 0 && !flags.IsVanished && flags.ActState != ActState.Dead;
        }

        private void CompleteIfConditionMet()
        {
            if (!HasMetCompletionCondition()) { return; }
            _runner.Complete();
            NotifyEnded();
        }

        private void NotifyEnded()
        {
            ChallengeState state = _runner.State;
            _participant = default;
            OnEncounterEnded(state);
            Ended?.Invoke(state);
            if (state == ChallengeState.Completed) { onCompleted.Invoke(); }
        }

#if UNITY_INCLUDE_TESTS
        internal void ConfigureForTest(CharacterActor3D actor, ChallengeDefinition definition,
            EncounterTraits encounterTraits = EncounterTraits.None, bool autoStart = false)
        {
            knight = actor;
            challenge = definition;
            traits = encounterTraits;
            startAutomatically = autoStart;
        }
#endif
    }
}
