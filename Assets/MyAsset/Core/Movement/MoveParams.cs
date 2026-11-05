using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// Movement parameters for a character.
    /// </summary>
    [Serializable]
    public struct MoveParams
    {
        public float moveSpeed;
        public float jumpForce;
        public float dashSpeed;
        public float dashDuration;
        public float gravityScale;
        public float weightRatio;
        public float jumpStaminaCost;
        public float dodgeStaminaCost;
        public float sprintStaminaPerSecond;
        /// <summary>回避（ドッジ）持続時間（秒）。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float dodgeDuration;
        /// <summary>回避時の moveSpeed 倍率。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float dodgeSpeedMultiplier;
        /// <summary>スプリント時の moveSpeed 倍率。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float sprintSpeedMultiplier;
        /// <summary>Coyote Time: 接地を離れた後でもジャンプを許容する秒数。0 以下は GroundMovementLogic のデフォルト値を使用。</summary>
        public float coyoteTime;
    }
}
