using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>通常探索で選べる召喚種と編成上限の設定アセット。</summary>
    [CreateAssetMenu(fileName = "ExpeditionSquadRules", menuName = "Game/Expedition/Squad Rules")]
    public sealed class ExpeditionSquadRules : ScriptableObject
    {
        [Min(0)]
        [SerializeField] private int maxCount;
        [Min(0)]
        [SerializeField] private int maxCost;
        [SerializeField] private ExpeditionSquad.Species[] species = Array.Empty<ExpeditionSquad.Species>();

        /// <summary>この設定と進行中の解放状況を参照する編成を作る。</summary>
        public ExpeditionSquad CreateSquad(ExpeditionAidUnlocks unlocks)
        {
            return new ExpeditionSquad(unlocks, species, maxCount, maxCost);
        }
    }
}
