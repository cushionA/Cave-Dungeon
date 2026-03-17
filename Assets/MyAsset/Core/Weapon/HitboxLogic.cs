using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// ヒットボックス判定ロジック。矩形判定、ヒット回数管理、同一ターゲット重複防止。
    /// </summary>
    public class HitboxLogic
    {
        private int _hitCount;
        private int _maxHitCount;
        private HashSet<int> _hitTargets;

        public int HitCount => _hitCount;
        public bool IsExhausted => _hitCount >= _maxHitCount;

        public HitboxLogic(int maxHitCount)
        {
            _maxHitCount = maxHitCount;
            _hitCount = 0;
            _hitTargets = new HashSet<int>();
        }

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
        /// ヒット記録。同一ターゲット重複防止。上限到達でfalse。
        /// </summary>
        /// <param name="targetHash">ターゲット識別ハッシュ。</param>
        /// <returns>ヒットが記録された場合true。上限到達または重複の場合false。</returns>
        public bool TryRegisterHit(int targetHash)
        {
            if (_hitCount >= _maxHitCount)
            {
                return false;
            }

            if (!_hitTargets.Add(targetHash))
            {
                return false;
            }

            _hitCount++;
            return true;
        }

        /// <summary>
        /// リセット（次の攻撃モーション開始時に呼ぶ）。
        /// </summary>
        /// <param name="newMaxHitCount">新しいヒット上限。</param>
        public void Reset(int newMaxHitCount)
        {
            _maxHitCount = newMaxHitCount;
            _hitCount = 0;
            _hitTargets.Clear();
        }
    }
}
