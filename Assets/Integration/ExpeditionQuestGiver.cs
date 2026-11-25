using Game.Core;
using PixelCrushers;
using PixelCrushers.QuestMachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>NPCの依頼と騎士の3D到達を Quest Machine のクエストへ接続する。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionQuestGiver : MonoBehaviour
    {
        [SerializeField] private Quest quest;
        [SerializeField] private QuestJournal journal;
        [SerializeField] private CharacterActor3D knight;
        [SerializeField] private BoxCollider objective;
        [SerializeField] private string reachNodeId = "reach";
        [SerializeField] private string reportNodeId = "report";

        /// <summary>会話分岐用の段階。-1: 利用不可、0: 未受注、1: 到達前、2: 報告待ち、3: 完了。</summary>
        public int Stage
        {
            get
            {
                if (!CanUseJournal) { return -1; }
                Quest current = journal.FindQuest(quest.id);
                if (current == null) { return 0; }

                QuestState state = current.GetState();
                if (state == QuestState.Successful) { return 3; }
                if (state != QuestState.Active) { return -1; }
                if (current.GetNode(reportNodeId)?.GetState() == QuestNodeState.Active) { return 2; }
                return current.GetNode(reachNodeId)?.GetState() == QuestNodeState.Active ? 1 : -1;
            }
        }

        private bool CanUseJournal => isActiveAndEnabled && !QuestMachine.isLoadingGame && journal != null
            && journal.isActiveAndEnabled && quest != null && quest.isAsset && !StringField.IsNullOrEmpty(quest.id);

        public bool TryAccept()
        {
            if (!CanUseJournal || !HasObjective() || !TryReadKnight(out _)
                || journal.FindQuest(quest.id) != null || reachNodeId == reportNodeId
                || quest.GetNode(reachNodeId)?.nodeType != QuestNodeType.Condition
                || quest.GetNode(reportNodeId)?.nodeType != QuestNodeType.Condition)
            {
                return false;
            }

            return QuestMachine.GiveQuestToQuester(quest, journal) != null;
        }

        public bool TryReport()
        {
            if (!TryGetActiveNode(reportNodeId, out QuestNode node) || !TryReadKnight(out _)) { return false; }

            node.SetState(QuestNodeState.True);
            return true;
        }

        private void FixedUpdate()
        {
            if (!TryGetActiveNode(reachNodeId, out QuestNode node) || !HasObjective()
                || !TryReadKnight(out CharacterController body))
            {
                return;
            }

            Transform frame = objective.transform;
            Transform pose = body.transform;
            if (Physics.ComputePenetration(objective, frame.position, frame.rotation,
                    body, pose.position, pose.rotation, out _, out _))
            {
                node.SetState(QuestNodeState.True);
            }
        }

        private bool TryGetActiveNode(string id, out QuestNode node)
        {
            node = null;
            if (!CanUseJournal) { return false; }

            Quest current = journal.FindQuest(quest.id);
            if (current == null || current.GetState() != QuestState.Active) { return false; }

            node = current.GetNode(id);
            return node?.GetState() == QuestNodeState.Active;
        }

        private bool HasObjective() => objective != null && objective.isTrigger && objective.enabled
            && objective.gameObject.activeInHierarchy && objective.gameObject.scene == gameObject.scene;

        private bool TryReadKnight(out CharacterController body)
        {
            body = null;
            Scene room = gameObject.scene;
            if (!room.isLoaded || knight == null || !knight.IsAlive || knight.gameObject.scene != room) { return false; }

            CharacterFlags flags = GameManager.Data.GetFlags(knight.Actor.Hash);
            body = knight.Body;
            return body != null && body.enabled && (flags.Feature & CharacterFeature.Player) != 0
                && !flags.IsVanished && flags.ActState != ActState.Dead;
        }
    }
}
