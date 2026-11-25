using System;
using UnityEngine;

namespace Game.Core
{
    public sealed partial class ExpeditionAidUnlocks
    {
        [Serializable]
        public struct BossRule
        {
            [SerializeField] private string bossId;
            [SerializeField] private string speciesId;
            [SerializeField, Min(2)] private int minimumCycle;
            [SerializeField] private string requiredGlobalFlag;

            public string BossId => bossId;
            public string SpeciesId => speciesId;
            public int MinimumCycle => minimumCycle;
            public string RequiredGlobalFlag => requiredGlobalFlag;

            public BossRule(string bossId, string speciesId, int minimumCycle, string requiredGlobalFlag = null)
            {
                this.bossId = bossId;
                this.speciesId = speciesId;
                this.minimumCycle = minimumCycle;
                this.requiredGlobalFlag = requiredGlobalFlag;
            }
        }

        /// <summary>ボス戦の開始と撃破を受け取り、確定した同一周回の解放条件を照合する。</summary>
        public IDisposable ObserveBossDefeats(BossControllerLogic controller, ExpeditionPlaythrough playthrough, BossRule[] rules)
        {
            if (controller == null) { throw new ArgumentNullException(nameof(controller)); }
            if (playthrough == null) { throw new ArgumentNullException(nameof(playthrough)); }
            return new BossDefeatSubscription(this, controller, playthrough,
                rules == null ? Array.Empty<BossRule>() : (BossRule[])rules.Clone());
        }

        public bool TryCompleteBoss(string bossId, ExpeditionPlaythrough playthrough, BossRule[] rules)
            => playthrough != null && TryCompleteBoss(bossId, playthrough.CycleNumber, rules);

        private bool TryCompleteBoss(string bossId, int cycle, BossRule[] rules)
        {
            if (cycle < 2 || string.IsNullOrWhiteSpace(bossId) || rules == null) { return false; }
            string species = null;
            bool eligible = false;
            foreach (BossRule rule in rules)
            {
                if (!string.Equals(rule.BossId, bossId, StringComparison.Ordinal)) { continue; }
                if (string.IsNullOrWhiteSpace(rule.SpeciesId) || rule.MinimumCycle < 2
                    || (species != null && !string.Equals(species, rule.SpeciesId, StringComparison.Ordinal)))
                {
                    return false;
                }
                species = rule.SpeciesId;
                eligible |= cycle >= rule.MinimumCycle && (string.IsNullOrEmpty(rule.RequiredGlobalFlag)
                    || _flags.GetGlobalFlag(rule.RequiredGlobalFlag));
            }
            return eligible && TryUnlockSpecies(species);
        }

        private sealed class BossDefeatSubscription : IDisposable
        {
            private readonly ExpeditionAidUnlocks _unlocks;
            private readonly ExpeditionPlaythrough _playthrough;
            private readonly BossRule[] _rules;
            private BossControllerLogic _controller;
            private ExpeditionPlaythrough.CycleState _encounterCycle;

            public BossDefeatSubscription(ExpeditionAidUnlocks unlocks, BossControllerLogic controller,
                ExpeditionPlaythrough playthrough, BossRule[] rules)
            {
                _unlocks = unlocks;
                _controller = controller;
                _playthrough = playthrough;
                _rules = rules;
                controller.OnEncounterStart += EncounterStarted;
                controller.OnBossDefeated += BossDefeated;
            }

            private void EncounterStarted(string _) => _encounterCycle = _playthrough.CurrentCycle;

            private void BossDefeated(string bossId)
            {
                ExpeditionPlaythrough.CycleState cycle = _encounterCycle;
                _encounterCycle = null;
                if (ReferenceEquals(cycle, _playthrough.CurrentCycle))
                {
                    _unlocks.TryCompleteBoss(bossId, cycle.Number, _rules);
                }
            }

            public void Dispose()
            {
                BossControllerLogic controller = _controller;
                if (controller == null) { return; }
                _controller = null;
                _encounterCycle = null;
                controller.OnEncounterStart -= EncounterStarted;
                controller.OnBossDefeated -= BossDefeated;
            }
        }
    }
}
