using UnityEngine;
using Game.Core;
using PixelCrushers.QuestMachine;

namespace Game.Runtime
{
    /// <summary>
    /// Quest MachineとGameManagerのイベントシステムを接続するブリッジ。
    /// C# standard eventで購読（R3不要）。
    /// </summary>
    public class QuestMachineBridge : MonoBehaviour
    {
        private void OnEnable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnCharacterDeathEvent += OnCharacterDeath;
        }

        private void OnDisable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            GameManager.Events.OnCharacterDeathEvent -= OnCharacterDeath;
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
    }
}
