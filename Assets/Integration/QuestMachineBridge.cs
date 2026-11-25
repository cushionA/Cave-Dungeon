using UnityEngine;
using Game.Core;
using PixelCrushers;

namespace Game.Runtime
{
    /// <summary>
    /// Quest MachineとGameManagerのイベントシステムを接続するブリッジ。
    /// C# standard eventで購読（R3不要）。
    /// </summary>
    public class QuestMachineBridge : MonoBehaviour, IMessageHandler
    {
        [SerializeField]
        [Tooltip("Quest Machine の成功アクションまたは物語イベントから ExpeditionAidCompleted を送ります。Parameter はこの欄の Completion Id に合わせます。")]
        private ExpeditionAidUnlocks.Completion[] aidCompletions;

        private GameEvents _events;

        private void OnEnable()
        {
            MessageSystem.AddListener(this, ExpeditionAidUnlocks.k_CompletionMessage, string.Empty);
            _events = GameManager.Events;
            if (_events != null)
            {
                _events.OnCharacterDeathEvent += OnCharacterDeath;
            }
        }

        private void OnDisable()
        {
            MessageSystem.RemoveListener(this, ExpeditionAidUnlocks.k_CompletionMessage, string.Empty);
            if (_events != null)
            {
                _events.OnCharacterDeathEvent -= OnCharacterDeath;
                _events = null;
            }
        }

        private void OnCharacterDeath(int deadHash, int killerHash)
        {
            PixelCrushers.MessageSystem.SendMessage(
                gameObject,
                "EnemyDefeated",
                deadHash.ToString());

#if UNITY_EDITOR
            Debug.Log($"[Quest] EnemyDefeated message sent: {deadHash}");
#endif
        }

        /// <summary>
        /// アイテム取得時にQuest Machineへ通知する。
        /// </summary>
        public void NotifyItemCollected(string itemId, int amount)
        {
            PixelCrushers.MessageSystem.SendMessage(
                gameObject,
                "ItemCollected",
                itemId,
                amount.ToString());

#if UNITY_EDITOR
            Debug.Log($"[Quest] ItemCollected: {itemId} x{amount}");
#endif
        }

        /// <summary>
        /// エリア到達時にQuest Machineへ通知する。
        /// </summary>
        public void NotifyAreaReached(string areaId)
        {
            PixelCrushers.MessageSystem.SendMessage(
                gameObject,
                "AreaReached",
                areaId);

#if UNITY_EDITOR
            Debug.Log($"[Quest] AreaReached: {areaId}");
#endif
        }

        /// <summary>
        /// カスタムイベントをQuest Machineへ通知する。
        /// </summary>
        public void NotifyCustomEvent(string eventName, string parameter = "")
        {
            PixelCrushers.MessageSystem.SendMessage(
                gameObject,
                eventName,
                parameter);
        }

        void IMessageHandler.OnMessage(MessageArgs message)
        {
            if (PixelCrushersSaveSlotBridge.IsRestoring || PixelCrushers.QuestMachine.QuestMachine.isLoadingGame) { return; }
            GameManager.ExpeditionAid?.TryComplete(message.parameter, aidCompletions);
        }
    }
}
