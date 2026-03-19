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
                .Subscribe(e => OnCharacterDeath(e.characterHash));
        }

        private void OnDisable()
        {
            _deathSubscription?.Dispose();
            _deathSubscription = null;
        }

        private void OnCharacterDeath(int characterHash)
        {
            // Quest Machineへメッセージ送信: "EnemyDefeated:ハッシュ"
            MessageSystem.SendMessage(
                gameObject,
                "EnemyDefeated",
                characterHash.ToString());

            AILogger.Log($"[Quest] EnemyDefeated message sent: {characterHash}");
        }

        /// <summary>
        /// アイテム取得時にQuest Machineへ通知する。
        /// </summary>
        public void NotifyItemCollected(string itemId, int amount)
        {
            MessageSystem.SendMessage(
                gameObject,
                "ItemCollected",
                itemId,
                amount.ToString());

            AILogger.Log($"[Quest] ItemCollected: {itemId} x{amount}");
        }

        /// <summary>
        /// エリア到達時にQuest Machineへ通知する。
        /// </summary>
        public void NotifyAreaReached(string areaId)
        {
            MessageSystem.SendMessage(
                gameObject,
                "AreaReached",
                areaId);

            AILogger.Log($"[Quest] AreaReached: {areaId}");
        }

        /// <summary>
        /// カスタムイベントをQuest Machineへ通知する。
        /// </summary>
        public void NotifyCustomEvent(string eventName, string parameter = "")
        {
            MessageSystem.SendMessage(
                gameObject,
                eventName,
                parameter);
        }
    }
}
