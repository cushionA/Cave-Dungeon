using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// ヒットボックスの矩形判定と、同一対象への命中間隔の管理。
    /// </summary>
    public class HitboxLogic
    {
        private readonly AttackHitTracker _hits;

        public int HitCount => _hits.HitCount;
        public bool IsExhausted => _hits.IsExhausted;

        public HitboxLogic(int maxHitCount) : this(maxHitCount, default) { }

        public HitboxLogic(int maxHitCount, HitRepeatSettings repeatHits) => _hits = new AttackHitTracker(maxHitCount, repeatHits);

        /// <summary>
        /// 矩形ヒットボックスに点が含まれるか判定する。
        /// UnityのRect.Containsと同じ境界ルール（min inclusive, max exclusive）。
        /// </summary>
        /// <param name="hitbox">判定矩形。</param>
        /// <param name="point">判定対象の点。</param>
        /// <returns>点が矩形内にある場合true。</returns>
        public static bool RectContainsPoint(Rect hitbox, Vector2 point)
        {
            return hitbox.Contains(point);
        }

        /// <summary>
        /// ヒット記録。対象数や同じ対象への命中間隔と上限を満たさなければfalse。
        /// </summary>
        /// <param name="targetHash">ターゲット識別ハッシュ。</param>
        /// <returns>ヒットが記録された場合true。上限到達または重複の場合false。</returns>
        public bool TryRegisterHit(int targetHash)
            => TryRegisterHit(targetHash, 0);

        public bool TryRegisterHit(int targetHash, double time)
            => TryRegisterHit(new ActorRef(targetHash, 0), time);

        /// <summary>Runtime経路では登録世代ごとに命中を記録する。</summary>
        public bool TryRegisterHit(ActorRef target) => TryRegisterHit(target, 0);

        public bool TryRegisterHit(ActorRef target, double time) => _hits.TryRegisterHit(target, time);

        /// <summary>
        /// リセット（次の攻撃モーション開始時に呼ぶ）。
        /// </summary>
        /// <param name="newMaxHitCount">新しいヒット上限。</param>
        public void Reset(int newMaxHitCount) => Reset(newMaxHitCount, default);

        public void Reset(int newMaxHitCount, HitRepeatSettings repeatHits) => _hits.Reset(newMaxHitCount, repeatHits);
    }
}
