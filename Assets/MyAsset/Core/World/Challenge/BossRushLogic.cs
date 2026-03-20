using System;

namespace Game.Core
{
    /// <summary>
    /// ボスラッシュの連戦進行を管理する純ロジッククラス。
    /// ボスID配列を受け取り、順番に撃破を記録して次のボスへ進行する。
    /// </summary>
    public class BossRushLogic
    {
        private readonly string[] _bossIds;
        private int _currentBossIndex;

        /// <summary>
        /// 現在のボスインデックス（0始まり）。全撃破済みの場合は TotalBossCount と同値。
        /// </summary>
        public int CurrentBossIndex
        {
            get { return _currentBossIndex; }
            private set { _currentBossIndex = value; }
        }

        /// <summary>
        /// ボスの総数。
        /// </summary>
        public int TotalBossCount
        {
            get { return _bossIds.Length; }
        }

        /// <summary>
        /// 全ボスが撃破済みかどうか。
        /// </summary>
        public bool IsAllDefeated
        {
            get { return _currentBossIndex >= _bossIds.Length; }
        }

        /// <summary>
        /// コンストラクタ。ボスIDの配列を受け取る。
        /// </summary>
        /// <param name="bossIds">出現順に並んだボスID配列。null や空配列は不可。</param>
        public BossRushLogic(string[] bossIds)
        {
            if (bossIds == null || bossIds.Length == 0)
            {
                throw new ArgumentException("bossIds must not be null or empty.", nameof(bossIds));
            }

            _bossIds = new string[bossIds.Length];
            Array.Copy(bossIds, _bossIds, bossIds.Length);
            _currentBossIndex = 0;
        }

        /// <summary>
        /// 次のボスIDを返す。全撃破済みなら null を返す。
        /// </summary>
        public string GetNextBossId()
        {
            if (IsAllDefeated)
            {
                return null;
            }

            return _bossIds[_currentBossIndex];
        }

        /// <summary>
        /// ボス撃破を記録し、次に進む。
        /// bossId が現在のボスと一致しない場合は false を返す。
        /// </summary>
        /// <param name="bossId">撃破したボスのID。</param>
        /// <returns>記録に成功した場合 true、不一致の場合 false。</returns>
        public bool MarkDefeated(string bossId)
        {
            if (IsAllDefeated)
            {
                return false;
            }

            if (_bossIds[_currentBossIndex] != bossId)
            {
                return false;
            }

            _currentBossIndex++;
            return true;
        }
    }
}
