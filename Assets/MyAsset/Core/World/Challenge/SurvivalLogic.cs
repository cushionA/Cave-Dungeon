namespace Game.Core
{
    /// <summary>
    /// 耐久戦(Survival)のWave管理ロジック。
    /// 純ロジッククラスでMonoBehaviourに依存しない。
    /// </summary>
    public class SurvivalLogic
    {
        private readonly int _enemiesPerWave;

        public int CurrentWave { get; private set; }
        public int TotalWaves { get; }
        public int EnemiesRemainingInWave { get; private set; }

        /// <summary>
        /// 全Waveがクリアされたかどうか。
        /// 最終Waveの敵が全滅している場合に true を返す。
        /// </summary>
        public bool IsAllWavesCleared
        {
            get { return CurrentWave >= TotalWaves && EnemiesRemainingInWave <= 0; }
        }

        /// <summary>
        /// SurvivalLogic を初期化する。
        /// </summary>
        /// <param name="totalWaves">総Wave数</param>
        /// <param name="enemiesPerWave">各Waveの敵数</param>
        public SurvivalLogic(int totalWaves, int enemiesPerWave)
        {
            TotalWaves = totalWaves;
            _enemiesPerWave = enemiesPerWave;
            CurrentWave = 1;
            EnemiesRemainingInWave = enemiesPerWave;
        }

        /// <summary>
        /// 敵撃破を記録する。
        /// Wave内の敵が全滅したら true を返す。
        /// </summary>
        /// <returns>Wave内の敵が全滅した場合 true</returns>
        public bool OnEnemyDefeated()
        {
            if (EnemiesRemainingInWave <= 0)
            {
                return false;
            }

            EnemiesRemainingInWave--;
            return EnemiesRemainingInWave <= 0;
        }

        /// <summary>
        /// 次の Wave に進む。
        /// Wave番号をインクリメントし、敵数をリセットする。
        /// </summary>
        public void AdvanceWave()
        {
            if (CurrentWave >= TotalWaves)
            {
                return;
            }

            CurrentWave++;
            EnemiesRemainingInWave = _enemiesPerWave;
        }
    }
}
