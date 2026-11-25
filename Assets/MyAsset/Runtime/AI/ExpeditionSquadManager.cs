using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>遠征の探索編成をゲームのセッション中に保持する。</summary>
    public sealed class ExpeditionSquadManager : MonoBehaviour, IGameSubManager
    {
        private const int k_InitOrder = 400;

        [SerializeField] private ExpeditionSquadRules rules;
        [SerializeField, Min(0)] private int presetCount = 3;
        private SaveManager _saves;

        public int InitOrder => k_InitOrder;
        public ExpeditionSquad Squad { get; private set; }
        public ExpeditionDeckPresets Presets { get; private set; }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize(SoACharaDataDic data, GameEvents events)
        {
            Dispose();
            if (rules == null || GameManager.GetSubManager<ExpeditionSquadManager>() != this)
            {
                return;
            }

            ExpeditionSquad squad = rules.CreateSquad(GameManager.ExpeditionAid);
            ExpeditionDeckPresets presets = new ExpeditionDeckPresets(squad, Mathf.Max(0, presetCount));
            SaveManager saves = GameManager.Saves;
            saves.Register(squad);
            saves.Register(presets);
            _saves = saves;
            Squad = squad;
            Presets = presets;
        }

        public void Dispose()
        {
            _saves?.Unregister(Squad);
            _saves?.Unregister(Presets);
            _saves = null;
            Squad = null;
            Presets = null;
        }
    }
}
