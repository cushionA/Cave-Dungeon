using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// カメラ追従のピュアロジック。
    /// SmoothDamp + デッドゾーン + 境界クランプ。
    /// </summary>
    public class CameraFollowLogic
    {
        private Vector2 _velocity;

        public float SmoothTime { get; set; }
        public float DeadZoneRadius { get; set; }
        public Rect Bounds { get; set; }

        public CameraFollowLogic(float smoothTime, float deadZoneRadius, Rect bounds)
        {
            SmoothTime = smoothTime;
            DeadZoneRadius = deadZoneRadius;
            Bounds = bounds;
            _velocity = Vector2.zero;
        }

        /// <summary>
        /// ターゲット位置に基づいてカメラの次位置を計算する。
        /// 1. デッドゾーン判定：ターゲットとの距離がDeadZoneRadius以下なら移動しない
        /// 2. SmoothDamp：滑らかな追従
        /// 3. 境界クランプ：Bounds内に制限
        /// </summary>
        public Vector2 CalculatePosition(Vector2 currentPos, Vector2 targetPos, float deltaTime)
        {
            Vector2 diff = targetPos - currentPos;

            if (diff.magnitude <= DeadZoneRadius)
            {
                return currentPos;
            }

            float newX = Mathf.SmoothDamp(currentPos.x, targetPos.x, ref _velocity.x, SmoothTime, Mathf.Infinity, deltaTime);
            float newY = Mathf.SmoothDamp(currentPos.y, targetPos.y, ref _velocity.y, SmoothTime, Mathf.Infinity, deltaTime);

            Vector2 result = new Vector2(newX, newY);
            return ClampToBounds(result);
        }

        /// <summary>
        /// 境界クランプのみを適用する（SmoothDamp・デッドゾーンなし）。
        /// </summary>
        public Vector2 ClampToBounds(Vector2 position)
        {
            float clampedX = Mathf.Clamp(position.x, Bounds.xMin, Bounds.xMax);
            float clampedY = Mathf.Clamp(position.y, Bounds.yMin, Bounds.yMax);
            return new Vector2(clampedX, clampedY);
        }

        /// <summary>
        /// カメラ位置を即座にターゲットに合わせる（シーン開始時等）。
        /// velocity をリセットする。
        /// </summary>
        public Vector2 SnapToTarget(Vector2 targetPos)
        {
            _velocity = Vector2.zero;
            return ClampToBounds(targetPos);
        }
    }
}
