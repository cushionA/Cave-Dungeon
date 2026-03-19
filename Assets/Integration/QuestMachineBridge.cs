using UnityEngine;
using Game.Core;
using PixelCrushers.QuestMachine;
using R3;

namespace Game.Runtime
{
    /// <summary>
    /// Quest MachineとGameManagerのイベントシステムを接続するブリッジ。
    /// ゲーム内イベント（敵撃破、アイテム取得等）をQuest Machineのメッセージとして送信。
    /// </summary>
    public class QuestMachineBridge : MonoBehaviour
    {
        private System.IDisposable _deathSubscription;

        private void OnEnable()
        {
            if (GameManager.Events == null)
            {
                return;
            }

            // 敵撃破イベントをQuest Machineに通知
            _deathSubscription = GameManager.Events.OnCharacterDeath
                .Subscribe(e => OnCharacterDeath(e.deadHash));
        }

        private void OnDisable()
        {
            _deathSubscription?.Dispose();
            _deathSubscription = null;
        }

        private void OnCharacterDeath(int characterHash)
        {
            // Quest Machineへメッセージ送信: "EnemyDefeated:ハッシュ"
            PixelCrushers.MessageSystem.SendMessage(
                gameObject,
                "EnemyDefeated",
                characterHash.ToString());

#if UNITY_EDITOR
            Debug.Log($"[Quest] EnemyDefeated message sent: {characterHash}");
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
