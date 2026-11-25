using System;
using Game.Core;
using R3;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Runtime
{
    /// <summary>配置したボス個体の撃破で、3D遭遇の完了と周回条件付きの解放を進める。</summary>
    public sealed class BossEncounter3D : EncounterSession3D
    {
        [SerializeField] private CharacterActor3D boss;
        [SerializeField] private string bossId;
        [SerializeField] private BossPhaseData[] phases = Array.Empty<BossPhaseData>();
        [SerializeField] private ExpeditionAidUnlocks.BossRule[] aidRules = Array.Empty<ExpeditionAidUnlocks.BossRule>();
        [SerializeField] private UnityEvent<int> onPhaseChanged = new UnityEvent<int>();

        private ActorRef _attemptBoss;
        private BossControllerLogic _controller;
        private SaveManager _saves;
        private IDisposable _deathSubscription;
        private IDisposable _actionSubscription;
        private IDisposable _aidSubscription;
        private int _actionCount;
        private bool _defeatConfirmed;

        public int CurrentPhase => _controller?.CurrentPhase ?? -1;

        protected override bool IsObjectiveAvailable
        {
            get
            {
                if (!IsBossBattle || string.IsNullOrWhiteSpace(bossId) || boss == null || !boss.isActiveAndEnabled
                    || boss.gameObject.scene != gameObject.scene) { return false; }

                ActorRef actor = boss.Actor;
                if (!actor.IsValid) { return false; }
                if (IsRunning) { return actor == _attemptBoss && (_defeatConfirmed || boss.IsAlive); }

                CharacterFlags flags = GameManager.Data.GetFlags(actor.Hash);
                return boss.IsAlive && (flags.Feature & CharacterFeature.Boss) != 0
                    && (flags.Belong & CharacterBelong.Enemy) != 0 && !flags.IsVanished && flags.ActState != ActState.Dead;
            }
        }

        protected override bool HasMetCompletionCondition() => _defeatConfirmed;

        public void BeginEncounter() => TryStartEncounter();

        protected override void OnEncounterStarted()
        {
            _attemptBoss = boss.Actor;
            _controller = new BossControllerLogic(phases ?? Array.Empty<BossPhaseData>(), bossId);
            _saves = GameManager.Saves;
            _saves.Loaded += OnSaveLoaded;
            _deathSubscription = GameManager.Events.OnCharacterDeathResolved.Subscribe(OnCharacterDeath);
            _actionSubscription = GameManager.Events.OnActionExecution.Subscribe(OnActionExecution);
            _aidSubscription = GameManager.ExpeditionAid.ObserveBossDefeats(_controller, GameManager.Playthrough, aidRules);
            _controller.StartEncounter();
        }

        protected override void OnEncounterTick(float deltaTime)
        {
            CharacterVitals vitals = GameManager.Data.GetVitals(_attemptBoss.Hash);
            float hpRatio = vitals.maxHp > 0 ? (float)vitals.currentHp / vitals.maxHp : 0f;
            BossPhaseTransitionResult transition = _controller.UpdateEncounter(hpRatio, deltaTime, _actionCount);
            if (transition.transitioned) { onPhaseChanged.Invoke(transition.newPhase); }
        }

        protected override void OnEncounterEnded(ChallengeState state)
        {
            BossControllerLogic controller = _controller;
            IDisposable aidSubscription = _aidSubscription;
            _controller = null;
            _attemptBoss = default;
            _actionCount = 0;
            _defeatConfirmed = false;
            _saves.Loaded -= OnSaveLoaded;
            _saves = null;
            _deathSubscription.Dispose();
            _deathSubscription = null;
            _actionSubscription.Dispose();
            _actionSubscription = null;
            _aidSubscription = null;

            try
            {
                if (state == ChallengeState.Completed) { controller.OnDefeated(); }
            }
            finally { aidSubscription.Dispose(); }
        }

        private void OnCharacterDeath(ActorDeathEvent death)
        {
            if (!IsRunning || death.Dead != _attemptBoss || boss == null || boss.Actor != _attemptBoss) { return; }
            SoACharaDataDic data = GameManager.Data;
            if (data.GetVitals(_attemptBoss.Hash).currentHp > 0 || data.GetFlags(_attemptBoss.Hash).ActState != ActState.Dead)
            {
                return;
            }

            _defeatConfirmed = true;
            CompleteEncounter();
        }

        private void OnActionExecution(ActionExecutionEvent action)
        {
            if (action.kind == ActionSignalKind.Started && action.execution.ownerRef == _attemptBoss) { _actionCount++; }
        }

        private void OnSaveLoaded(int _) => AbortEncounter();

#if UNITY_INCLUDE_TESTS
        internal void ConfigureBossForTest(CharacterActor3D target, string stableBossId, BossPhaseData[] phaseDefinitions,
            ExpeditionAidUnlocks.BossRule[] rules)
        {
            boss = target;
            bossId = stableBossId;
            phases = phaseDefinitions;
            aidRules = rules;
        }
#endif
    }
}
