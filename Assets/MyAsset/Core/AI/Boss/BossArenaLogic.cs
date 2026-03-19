using System;

namespace Game.Core
{
    /// <summary>
    /// ボスアリーナのロック/解除ロジック（Pure Logic）。
    /// MonoBehaviour版BossArenaManagerがコライダー制御を担当し、
    /// このクラスは状態遷移とClearGate連携を管理する。
    /// </summary>
    public class BossArenaLogic
    {
        private readonly string _clearGateId;
        private ArenaState _state;

        public ArenaState State => _state;
        public string ClearGateId => _clearGateId;

        /// <summary>
        /// アリーナ解除時にClearGateを開放するイベント。
        /// </summary>
        public event Action<string> OnClearGateOpen;

        /// <summary>
        /// アリーナ状態変化イベント。
        /// </summary>
        public event Action<ArenaState> OnStateChanged;

        public BossArenaLogic(string clearGateId)
        {
            _clearGateId = clearGateId;
            _state = ArenaState.Open;
        }

        /// <summary>
        /// アリーナをロックする（戦闘開始時）。
        /// </summary>
        public void LockArena()
        {
            _state = ArenaState.Locked;
            OnStateChanged?.Invoke(_state);
        }

        /// <summary>
        /// アリーナを解除する（ボス撃破後）。
        /// ClearGateを永続開放する。
        /// </summary>
        public void UnlockArena()
        {
            _state = ArenaState.Cleared;
            OnStateChanged?.Invoke(_state);
            OnClearGateOpen?.Invoke(_clearGateId);
        }
    }
}
