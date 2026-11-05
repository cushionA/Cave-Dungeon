using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// アニメーション状態。AI判定でAction Maskingとframe advantage計算に使用。
    /// BaseCharacterのアニメーションコントローラから毎フレーム更新される。
    /// フィールド順序はメモリアライメント最適化済み（サイズ降順）。
    /// </summary>
    public struct AnimationStateData
    {
        /// <summary>フェーズ内の正規化時間（0.0〜1.0）</summary>
        public float normalizedTime;          // 4 bytes, offset 0
        /// <summary>アクション可能になるまでのフレーム数</summary>
        public short framesUntilActionable;   // 2 bytes, offset 4
        /// <summary>現在のアニメーションフェーズ</summary>
        public AnimationPhase currentPhase;   // 1 byte,  offset 6
        /// <summary>攻撃モーションID（0 = 攻撃中でない）</summary>
        public byte currentMoveId;            // 1 byte,  offset 7
        /// <summary>キャンセル可能か</summary>
        public bool isCancelable;             // 1 byte,  offset 8
        /// <summary>キャンセル不能な攻撃フェーズか</summary>
        public bool isCommitted;              // 1 byte,  offset 9
    }
}
