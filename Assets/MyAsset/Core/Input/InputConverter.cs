using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 入力値をゲーム内データ型に変換するピュアロジック。
    /// MonoBehaviourに依存しない。
    /// </summary>
    public static class InputConverter
    {
        public const float k_DeadZone = 0.15f;

        /// <summary>
        /// Vector2入力にデッドゾーンを適用して正規化する。
        /// magnitude が DeadZone 以下なら Vector2.zero を返す。
        /// magnitude が 1.0 を超える場合は正規化する。
        /// </summary>
        public static Vector2 NormalizeDirection(Vector2 raw)
        {
            float magnitude = raw.magnitude;

            if (magnitude <= k_DeadZone)
            {
                return Vector2.zero;
            }

            if (magnitude > 1f)
            {
                return raw.normalized;
            }

            return raw;
        }

        /// <summary>
        /// AttackButtonIdからAttackInputTypeに変換する。
        /// 空中かどうかでAerial系に、チャージ中かどうかでCharge系に変換する。
        /// </summary>
        public static AttackInputType? ConvertAttackInput(AttackButtonId buttonId, bool isAirborne, bool isCharging)
        {
            if (buttonId == AttackButtonId.Skill)
            {
                return AttackInputType.Skill;
            }

            if (isAirborne)
            {
                if (buttonId == AttackButtonId.Light)
                {
                    return AttackInputType.AerialLight;
                }
                if (buttonId == AttackButtonId.Heavy)
                {
                    return AttackInputType.AerialHeavy;
                }
            }

            if (isCharging)
            {
                if (buttonId == AttackButtonId.Light)
                {
                    return AttackInputType.ChargeLight;
                }
                if (buttonId == AttackButtonId.Heavy)
                {
                    return AttackInputType.ChargeHeavy;
                }
            }

            if (buttonId == AttackButtonId.Light)
            {
                return AttackInputType.LightAttack;
            }

            if (buttonId == AttackButtonId.Heavy)
            {
                return AttackInputType.HeavyAttack;
            }

            return null;
        }
    }
}
