using System.Collections.Generic;
using Game.Core;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>会話から通常の編成画面と確定済み編成の出撃を呼び出す。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionDialogueActions : MonoBehaviour
    {
        private const string k_IsAidUnlocked = "ExpeditionIsAidUnlocked";
        private const string k_HasConfirmedSquad = "ExpeditionHasConfirmedSquad";
        private const string k_OpenSquadPanel = "ExpeditionOpenSquadPanel";
        private const string k_TryDeployConfirmed = "ExpeditionTryDeployConfirmed";
        private static readonly List<ExpeditionDialogueActions> s_Bindings = new List<ExpeditionDialogueActions>();

        [SerializeField] private ExpeditionSquadPanel squadPanel;
        [SerializeField] private ExpeditionSummonDeployment3D deployment;
        [SerializeField] private UnityEvent<bool> onDeploymentResult = new UnityEvent<bool>();

        private void OnEnable()
        {
            if (s_Bindings.Count > 0) { UnregisterLua(); }
            s_Bindings.Add(this);
            RegisterLua();
        }

        private void OnDisable()
        {
            int index = s_Bindings.IndexOf(this);
            if (index < 0) { return; }
            bool wasCurrent = index == s_Bindings.Count - 1;
            s_Bindings.RemoveAt(index);
            if (!wasCurrent) { return; }

            UnregisterLua();
            if (s_Bindings.Count > 0) { s_Bindings[s_Bindings.Count - 1].RegisterLua(); }
        }

        public bool IsAidUnlocked(string speciesId)
            => isActiveAndEnabled && (GameManager.ExpeditionAid?.IsUnlocked(speciesId) ?? false);

        public bool HasConfirmedSquad()
        {
            ExpeditionSquad squad = FindSquad();
            return isActiveAndEnabled && squad != null && squad.ConfirmedCount > 0
                && squad.TryMeasure(squad.Confirmed, out int count, out int cost)
                && count <= squad.MaxCount && cost <= squad.MaxCost;
        }

        public void OpenSquadPanel() => TryOpenSquadPanel();

        public bool TryOpenSquadPanel()
        {
            if (!isActiveAndEnabled || squadPanel == null || !squadPanel.gameObject.scene.isLoaded
                || FindSquad() == null || !squadPanel.TryGetComponent(out UIDocument document)
                || document.panelSettings == null || document.visualTreeAsset == null) { return false; }

            Transform parent = squadPanel.transform.parent;
            if (parent != null && !parent.gameObject.activeInHierarchy) { return false; }
            document.enabled = true;
            squadPanel.enabled = true;
            squadPanel.gameObject.SetActive(true);
            squadPanel.Refresh();
            return squadPanel.isActiveAndEnabled;
        }

        public void DeployConfirmed() => TryDeployConfirmed();

        public bool TryDeployConfirmed()
        {
            bool deployed = isActiveAndEnabled && deployment != null && deployment.TryDeployConfirmed();
            onDeploymentResult?.Invoke(deployed);
            return deployed;
        }

        private void RegisterLua()
        {
            Lua.RegisterFunction(k_IsAidUnlocked, this, SymbolExtensions.GetMethodInfo(() => IsAidUnlocked(string.Empty)));
            Lua.RegisterFunction(k_HasConfirmedSquad, this, SymbolExtensions.GetMethodInfo(() => HasConfirmedSquad()));
            Lua.RegisterFunction(k_OpenSquadPanel, this, SymbolExtensions.GetMethodInfo(() => TryOpenSquadPanel()));
            Lua.RegisterFunction(k_TryDeployConfirmed, this, SymbolExtensions.GetMethodInfo(() => TryDeployConfirmed()));
        }

        private static void UnregisterLua()
        {
            Lua.UnregisterFunction(k_IsAidUnlocked);
            Lua.UnregisterFunction(k_HasConfirmedSquad);
            Lua.UnregisterFunction(k_OpenSquadPanel);
            Lua.UnregisterFunction(k_TryDeployConfirmed);
        }

        private static ExpeditionSquad FindSquad()
        {
            ExpeditionSquadManager manager = GameManager.GetSubManager<ExpeditionSquadManager>();
            return manager != null && manager.isActiveAndEnabled ? manager.Squad : null;
        }
    }
}
