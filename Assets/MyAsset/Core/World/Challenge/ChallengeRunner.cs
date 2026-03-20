namespace Game.Core
{
    /// <summary>
    /// チャレンジ実行中の状態管理・タイマー・勝敗判定を行う純ロジッククラス。
    /// MonoBehaviour非依存。
    /// </summary>
    public class ChallengeRunner
    {
        private ChallengeDefinition _definition;
        private int _playerHash;

        public ChallengeState State { get; private set; }
        public float ElapsedTime { get; private set; }
        public int CurrentWave { get; private set; }
        public int DeathCount { get; private set; }
        public int BossesDefeated { get; private set; }
        public int TotalDamageDealt { get; private set; }
        public int TotalDamageTaken { get; private set; }

        public ChallengeRunner()
        {
            State = ChallengeState.Ready;
        }

        /// <summary>
        /// プレイヤーハッシュを設定する。ダメージ集計でプレイヤー判定に使用。
        /// </summary>
        public void SetPlayerHash(int playerHash)
        {
            _playerHash = playerHash;
        }

        /// <summary>
        /// チャレンジを開始する。全カウンターをリセットしStateをRunningに変更。
        /// </summary>
        public void Start(ChallengeDefinition definition)
        {
            _definition = definition;
            State = ChallengeState.Running;
            ElapsedTime = 0f;
            CurrentWave = 0;
            DeathCount = 0;
            BossesDefeated = 0;
            TotalDamageDealt = 0;
            TotalDamageTaken = 0;
        }

        /// <summary>
        /// 毎フレーム呼び出し。Running中のみ時間を加算し、制限時間超過で失敗判定。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            ElapsedTime += deltaTime;

            if (_definition.TimeLimit > 0f && ElapsedTime >= _definition.TimeLimit)
            {
                State = ChallengeState.Failed;
            }
        }

        /// <summary>
        /// ボス撃破時に呼び出す。
        /// </summary>
        public void OnBossDefeated(string bossId)
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            BossesDefeated++;
        }

        /// <summary>
        /// プレイヤー死亡時に呼び出す。maxDeathCount超過で失敗判定。
        /// </summary>
        public void OnPlayerDeath()
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            DeathCount++;

            if (_definition.MaxDeathCount > 0 && DeathCount > _definition.MaxDeathCount)
            {
                State = ChallengeState.Failed;
            }
        }

        /// <summary>
        /// ウェーブクリア時に呼び出す。
        /// </summary>
        public void OnWaveCleared()
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            CurrentWave++;
        }

        /// <summary>
        /// ダメージ発生時に呼び出す。プレイヤーが攻撃者なら与ダメージ、防御者なら被ダメージを集計。
        /// </summary>
        public void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash)
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            if (attackerHash == _playerHash)
            {
                TotalDamageDealt += result.totalDamage;
            }

            if (defenderHash == _playerHash)
            {
                TotalDamageTaken += result.totalDamage;
            }
        }

        /// <summary>
        /// チャレンジを完了状態にする。
        /// </summary>
        public void Complete()
        {
            if (State != ChallengeState.Running)
            {
                return;
            }

            State = ChallengeState.Completed;
        }

        /// <summary>
        /// 現在の結果を生成して返す。ランク判定はクリアタイムベースで行う。
        /// </summary>
        public ChallengeResult GetResult()
        {
            ChallengeRank rank = CalculateRank();

            return new ChallengeResult
            {
                challengeId = _definition != null ? _definition.ChallengeId : "",
                rank = rank,
                clearTime = ElapsedTime,
                score = CalculateScore(),
                deathCount = DeathCount,
                totalDamageDealt = TotalDamageDealt,
                totalDamageTaken = TotalDamageTaken,
                isNewRecord = false,
            };
        }

        private ChallengeRank CalculateRank()
        {
            if (State != ChallengeState.Completed)
            {
                return ChallengeRank.None;
            }

            if (_definition == null)
            {
                return ChallengeRank.Bronze;
            }

            // タイムベースのランク判定
            if (_definition.GoldTimeThreshold > 0f && ElapsedTime <= _definition.GoldTimeThreshold)
            {
                return ChallengeRank.Gold;
            }

            if (_definition.SilverTimeThreshold > 0f && ElapsedTime <= _definition.SilverTimeThreshold)
            {
                return ChallengeRank.Silver;
            }

            return ChallengeRank.Bronze;
        }

        private int CalculateScore()
        {
            // 基本スコア: 与ダメージ - 被ダメージ + ボス撃破ボーナス
            int score = TotalDamageDealt - TotalDamageTaken + (BossesDefeated * 1000);

            if (score < 0)
            {
                score = 0;
            }

            return score;
        }
    }
}
