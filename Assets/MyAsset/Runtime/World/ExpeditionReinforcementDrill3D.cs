using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>訓練の操作地点から指定集団戦と増援の編成画面を開く。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionReinforcementDrill3D : MonoBehaviour
    {
        [SerializeField] private EncounterSession3D encounter;
        [SerializeField] private ExpeditionSquadPanel squadPanel;
        [SerializeField] private GameObject trainingOpponent;
        [SerializeField] private string requiredSpeciesId;

        private bool _openedPanel;

        private void OnEnable()
        {
            if (encounter != null) { encounter.Ended += OnEncounterEnded; }
        }

        private void OnDisable()
        {
            if (encounter != null)
            {
                encounter.Ended -= OnEncounterEnded;
                if (encounter.IsRunning) { encounter.AbortEncounter(); }
            }
            CloseDrill();
        }

        public void Open()
        {
            Scene scene = gameObject.scene;
            if (!isActiveAndEnabled || !scene.isLoaded || scene != SceneManager.GetActiveScene()
                || encounter == null || encounter.gameObject.scene != scene || !encounter.IsGroupBattle
                || squadPanel == null || squadPanel.gameObject.scene != scene
                || !squadPanel.TryGetComponent(out UIDocument document)
                || document.panelSettings == null || document.visualTreeAsset == null
                || GameManager.GetSubManager<ExpeditionSquadManager>()?.Squad == null
                || GameManager.ExpeditionAid?.IsUnlocked(requiredSpeciesId) != true) { return; }

            if (!encounter.IsRunning) { encounter.TryStartEncounter(); }
            if (!encounter.IsRunning) { return; }

            if (trainingOpponent != null) { trainingOpponent.SetActive(true); }
            if (!encounter.IsRunning) { return; }

            _openedPanel = true;
            document.enabled = true;
            squadPanel.enabled = true;
            squadPanel.gameObject.SetActive(true);
            squadPanel.Refresh();
        }

        public void Withdraw()
        {
            if (isActiveAndEnabled && encounter != null) { encounter.AbortEncounter(); }
        }

        private void OnEncounterEnded(ChallengeState _) => CloseDrill();

        private void CloseDrill()
        {
            if (trainingOpponent != null) { trainingOpponent.SetActive(false); }
            if (_openedPanel && squadPanel != null) { squadPanel.Close(); }
            _openedPanel = false;
        }
    }
}
