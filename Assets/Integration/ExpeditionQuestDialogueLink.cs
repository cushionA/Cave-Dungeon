using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Scripting;

namespace Game.Runtime
{
    /// <summary>Dialogue System の会話からシーン内の依頼を参照する。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionQuestDialogueLink : MonoBehaviour
    {
        [SerializeField] private ExpeditionQuestGiver questGiver;

        private static readonly List<ExpeditionQuestDialogueLink> s_activeLinks = new List<ExpeditionQuestDialogueLink>();

        private void OnEnable()
        {
            if (questGiver == null) { return; }

            s_activeLinks.Add(this);
            if (s_activeLinks.Count != 1) { return; }

            Lua.RegisterFunction(nameof(ExpeditionQuestStage), null,
                SymbolExtensions.GetMethodInfo(() => ExpeditionQuestStage()));
            Lua.RegisterFunction(nameof(ExpeditionAcceptQuest), null,
                SymbolExtensions.GetMethodInfo(() => ExpeditionAcceptQuest()));
            Lua.RegisterFunction(nameof(ExpeditionReportQuest), null,
                SymbolExtensions.GetMethodInfo(() => ExpeditionReportQuest()));
        }

        private void OnDisable()
        {
            if (!s_activeLinks.Remove(this) || s_activeLinks.Count != 0) { return; }

            Lua.UnregisterFunction(nameof(ExpeditionQuestStage));
            Lua.UnregisterFunction(nameof(ExpeditionAcceptQuest));
            Lua.UnregisterFunction(nameof(ExpeditionReportQuest));
        }

        [Preserve]
        public static double ExpeditionQuestStage()
        {
            ExpeditionQuestGiver quest = ResolveActiveQuest();
            return quest != null ? quest.Stage : -1;
        }

        [Preserve]
        public static bool ExpeditionAcceptQuest()
        {
            ExpeditionQuestGiver quest = ResolveActiveQuest();
            return quest != null && quest.TryAccept();
        }

        [Preserve]
        public static bool ExpeditionReportQuest()
        {
            ExpeditionQuestGiver quest = ResolveActiveQuest();
            return quest != null && quest.TryReport();
        }

        private static ExpeditionQuestGiver ResolveActiveQuest()
        {
            if (s_activeLinks.Count == 1) { return s_activeLinks[0].questGiver; }

            Transform conversant = DialogueManager.currentConversant;
            if (conversant == null) { return null; }
            foreach (ExpeditionQuestDialogueLink link in s_activeLinks)
            {
                if (link.gameObject.scene == conversant.gameObject.scene) { return link.questGiver; }
            }

            return null;
        }
    }
}
