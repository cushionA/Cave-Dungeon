using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 攻撃モーション中の移動距離計算ロジック。
    /// </summary>
    public static class AttackMovementLogic
    {
        /// <summary>
        /// 攻撃中の移動オフセットを時間経過率から計算する。
        /// 0からdurationの間でtotalDistanceを線形補間する。
        /// duration=0の場合は即座にtotalDistanceを返す。
        /// </summary>
        /// <param name="elapsed">経過時間（秒）。</param>
        /// <param name="duration">移動にかかる総時間（秒）。</param>
        /// <param name="totalDistance">移動する総距離。</param>
        /// <returns>現在時点での移動オフセット。</returns>
        public static float CalculateAttackMoveOffset(float elapsed, float duration, float totalDistance)
        {
            if (duration <= 0f)
            {
                return totalDistance;
            }

            float t = Mathf.Clamp01(elapsed / duration);
            return t * totalDistance;
        }
    }
}
