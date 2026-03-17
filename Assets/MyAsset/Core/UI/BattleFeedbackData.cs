using UnityEngine;

namespace Game.Core
{
    public enum FeedbackType : byte
    {
        Normal,
        Critical,
        Heal
    }

    public struct DamagePopupData
    {
        public int value;
        public FeedbackType type;
        public Vector2 worldPosition;
    }

    /// <summary>
    /// 戦闘フィードバック表示データ生成。UI描画側はこのデータを受け取って表示する。
    /// </summary>
    public static class BattleFeedbackFactory
    {
        /// <summary>ダメージポップアップデータ生成</summary>
        public static DamagePopupData CreateDamagePopup(int damage, bool isCritical, Vector2 position)
        {
            return new DamagePopupData
            {
                value = damage,
                type = isCritical ? FeedbackType.Critical : FeedbackType.Normal,
                worldPosition = position
            };
        }

        /// <summary>回復ポップアップデータ生成</summary>
        public static DamagePopupData CreateHealPopup(int healAmount, Vector2 position)
        {
            return new DamagePopupData
            {
                value = healAmount,
                type = FeedbackType.Heal,
                worldPosition = position
            };
        }
    }
}
