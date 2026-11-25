using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>依頼や物語イベントで解放された召喚種を、種ごとの永続フラグで管理する。</summary>
    public sealed partial class ExpeditionAidUnlocks
    {
        public const string k_CompletionMessage = "ExpeditionAidCompleted";
        internal const string k_UnlockFlagPrefix = "expedition.ally.unlocked.";

        [Serializable]
        public struct Completion
        {
            [SerializeField] private string completionId;
            [SerializeField] private string speciesId;

            public string CompletionId => completionId;
            public string SpeciesId => speciesId;

            public Completion(string completionId, string speciesId)
            {
                this.completionId = completionId;
                this.speciesId = speciesId;
            }
        }

        private readonly FlagManager _flags;

        public ExpeditionAidUnlocks(FlagManager flags)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        }

        public bool IsUnlocked(string speciesId)
        {
            return !string.IsNullOrWhiteSpace(speciesId) && _flags.GetGlobalFlag(k_UnlockFlagPrefix + speciesId);
        }

        public bool TryComplete(string completionId, Completion[] completions)
        {
            if (string.IsNullOrWhiteSpace(completionId) || completions == null)
            {
                return false;
            }

            string speciesId = null;
            foreach (Completion completion in completions)
            {
                if (!string.Equals(completionId, completion.CompletionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(completion.SpeciesId) ||
                    (speciesId != null && !string.Equals(speciesId, completion.SpeciesId, StringComparison.Ordinal)))
                {
                    return false;
                }

                speciesId = completion.SpeciesId;
            }

            return TryUnlockSpecies(speciesId);
        }

        private bool TryUnlockSpecies(string speciesId)
        {
            if (string.IsNullOrWhiteSpace(speciesId) || IsUnlocked(speciesId))
            {
                return false;
            }

            _flags.SetGlobalFlag(k_UnlockFlagPrefix + speciesId, true);
            return true;
        }
    }
}
