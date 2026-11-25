using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>指定した集団戦が進行している間、通常編成と別の増援枠を提供する。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionEncounterBudget3D : MonoBehaviour
    {
        [Serializable]
        private struct Reinforcement
        {
            [SerializeField] private string speciesId;
            [Min(1)] [SerializeField] private int count;

            public ExpeditionSquad.Selection ToSelection() => new ExpeditionSquad.Selection(speciesId, count);
        }

        [SerializeField] private EncounterSession3D encounter;
        [SerializeField] private ExpeditionSummonDeployment3D deployment;
        [Min(0)] [SerializeField] private int additionalCount;
        [Min(0)] [SerializeField] private int additionalCost;
        [SerializeField] private Reinforcement[] deployAtStart = Array.Empty<Reinforcement>();

        private EncounterSession3D _encounter;
        private ExpeditionSummonDeployment3D _deployment;
        private SaveManager _saves;
        private object _attempt;
        private object _deployedAttempt;

        public int ConfiguredAdditionalCountLimit => additionalCount;
        public int ConfiguredAdditionalCostLimit => additionalCost;
        public int AdditionalCountLimit => IsOpen ? ConfiguredAdditionalCountLimit : 0;
        public int AdditionalCostLimit => IsOpen ? ConfiguredAdditionalCostLimit : 0;
        public bool SupportsReinforcements => _encounter != null && _encounter.IsGroupBattle;
        public bool IsOpen => _attempt != null && SupportsReinforcements && _encounter.IsRunning;
        public bool CanDeployReinforcements => IsOpen && _deployment != null && !ReferenceEquals(_attempt, _deployedAttempt);
        internal object CurrentAttempt => IsOpen ? _attempt : null;

        private void OnEnable()
        {
            if (encounter == null || !encounter.IsGroupBattle || deployment == null
                || encounter.gameObject.scene != deployment.gameObject.scene)
            {
                return;
            }

            _encounter = encounter;
            _deployment = deployment;
            _encounter.Started += Open;
            _encounter.Ended += OnEnded;
            _saves = GameManager.Saves;
            if (_saves != null) { _saves.Loaded += OnSaveLoaded; }
            if (_encounter.IsRunning) { Open(); }
        }

        private void OnDisable()
        {
            if (_encounter != null)
            {
                _encounter.Started -= Open;
                _encounter.Ended -= OnEnded;
            }
            if (_saves != null) { _saves.Loaded -= OnSaveLoaded; }
            _saves = null;
            Close();
            _encounter = null;
            _deployment = null;
        }

        public bool TryDeployReinforcements(IReadOnlyList<ExpeditionSquad.Selection> requested)
        {
            if (!CanDeployReinforcements) { return false; }
            ExpeditionSquadManager manager = GameManager.GetSubManager<ExpeditionSquadManager>();
            ExpeditionSquad squad = manager != null ? manager.Squad : null;
            if (squad == null || !squad.TryMeasure(requested, out int count, out int cost)
                || count == 0 || count > additionalCount || cost > additionalCost)
            {
                return false;
            }

            object attempt = _attempt;
            ExpeditionSummonDeployment3D deployer = _deployment;
            if (!deployer.TryDeployReinforcements(attempt, requested)) { return false; }
            if (IsOpen && ReferenceEquals(_attempt, attempt))
            {
                _deployedAttempt = attempt;
                return true;
            }
            // A prefab can invoke callbacks while being activated. Discard a superseded deployment only.
            if (deployer != null) { deployer.DismissReinforcements(attempt); }
            return false;
        }

        private void Open()
        {
            Close();
            if (!SupportsReinforcements || !_encounter.IsRunning || additionalCount <= 0 || additionalCost <= 0) { return; }
            _attempt = new object();
            if (deployAtStart == null || deployAtStart.Length == 0) { return; }

            ExpeditionSquad.Selection[] configured = new ExpeditionSquad.Selection[deployAtStart.Length];
            for (int i = 0; i < configured.Length; i++) { configured[i] = deployAtStart[i].ToSelection(); }
            TryDeployReinforcements(configured);
        }

        private void OnEnded(ChallengeState state)
        {
            if (_encounter == null || !_encounter.IsRunning) { Close(); }
        }

        private void OnSaveLoaded(int slot)
        {
            if (_encounter != null) { _encounter.AbortEncounter(); }
            Close();
        }

        private void Close()
        {
            object attempt = _attempt;
            _attempt = null;
            _deployedAttempt = null;
            if (attempt != null && _deployment != null) { _deployment.DismissReinforcements(attempt); }
        }

#if UNITY_INCLUDE_TESTS
        internal void ConfigureForTest(EncounterSession3D session, ExpeditionSummonDeployment3D summonDeployment,
            int count, int cost)
        {
            encounter = session;
            deployment = summonDeployment;
            additionalCount = count;
            additionalCost = cost;
        }
#endif
    }
}
