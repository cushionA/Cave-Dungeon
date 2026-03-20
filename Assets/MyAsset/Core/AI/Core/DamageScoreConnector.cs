using System;
using R3;

namespace Game.Core
{
    /// <summary>
    /// GameEvents.OnDamageDealtを購読し、DamageScoreTrackerにスコアを蓄積する接続クラス。
    /// 各AIキャラクターがDamageScoreTrackerインスタンスを保持し、
    /// このコネクタでイベント購読→スコア蓄積の配線を行う。
    /// </summary>
    public class DamageScoreConnector : IDisposable
    {
        private readonly DamageScoreTracker _tracker;
        private IDisposable _subscription;
        private float _currentTime;

        public DamageScoreConnector(DamageScoreTracker tracker, GameEvents events)
        {
            _tracker = tracker;
            _currentTime = 0f;

            _subscription = events.OnDamageDealt.Subscribe(e =>
            {
                if (e.result.totalDamage > 0)
                {
                    _tracker.AddDamage(e.attackerHash, e.result.totalDamage, _currentTime);
                }
            });
        }

        /// <summary>
        /// 時間を更新する。AIキャラクターのTickから呼ばれる。
        /// </summary>
        public void UpdateTime(float currentTime)
        {
            _currentTime = currentTime;
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
