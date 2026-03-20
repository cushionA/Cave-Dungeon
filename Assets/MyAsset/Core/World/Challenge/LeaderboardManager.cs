using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// チャレンジモードの記録を管理する純ロジッククラス。
    /// ISaveable を実装し、セーブ/ロードに対応する。
    /// </summary>
    public class LeaderboardManager : ISaveable
    {
        private readonly Dictionary<string, LeaderboardEntry> _records = new Dictionary<string, LeaderboardEntry>();

        public string SaveId => "LeaderboardManager";

        /// <summary>
        /// 記録を更新する。新記録なら true を返す。
        /// - attemptCount は毎回インクリメント
        /// - クリア時は clearCount インクリメント
        /// - bestScore / bestTime / rank は最高値を保持
        /// - bestTime は小さい方が良い（float.MaxValue が初期値）
        /// </summary>
        public bool UpdateRecord(ChallengeResult result)
        {
            bool isNewRecord = false;

            if (!_records.TryGetValue(result.challengeId, out LeaderboardEntry entry))
            {
                // 初回挑戦: 新規エントリ作成
                entry = new LeaderboardEntry
                {
                    challengeId = result.challengeId,
                    rank = result.rank,
                    bestTime = result.clearTime,
                    bestScore = result.score,
                    attemptCount = 1,
                    clearCount = result.state == ChallengeState.Completed ? 1 : 0,
                    dateAchieved = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                };
                _records[result.challengeId] = entry;
                return true;
            }

            // 既存エントリの更新
            entry.attemptCount++;

            if (result.state == ChallengeState.Completed)
            {
                entry.clearCount++;
            }

            // スコアが上回った場合
            if (result.score > entry.bestScore)
            {
                entry.bestScore = result.score;
                isNewRecord = true;
            }

            // タイムが短縮された場合
            if (result.clearTime < entry.bestTime)
            {
                entry.bestTime = result.clearTime;
                isNewRecord = true;
            }

            // ランクが上回った場合
            if (result.rank > entry.rank)
            {
                entry.rank = result.rank;
                isNewRecord = true;
            }

            if (isNewRecord)
            {
                entry.dateAchieved = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }

            _records[result.challengeId] = entry;
            return isNewRecord;
        }

        /// <summary>
        /// チャレンジ別のベスト記録を取得する。
        /// 記録が存在しない場合は初期値の LeaderboardEntry を返す。
        /// </summary>
        public LeaderboardEntry GetBestRecord(string challengeId)
        {
            if (_records.TryGetValue(challengeId, out LeaderboardEntry entry))
            {
                return entry;
            }

            return new LeaderboardEntry
            {
                challengeId = challengeId,
                rank = ChallengeRank.None,
                bestTime = float.MaxValue,
                bestScore = 0,
                attemptCount = 0,
                clearCount = 0,
                dateAchieved = string.Empty,
            };
        }

        /// <summary>
        /// 全記録を配列で取得する。
        /// </summary>
        public LeaderboardEntry[] GetAllRecords()
        {
            LeaderboardEntry[] entries = new LeaderboardEntry[_records.Count];
            int index = 0;
            foreach (KeyValuePair<string, LeaderboardEntry> kvp in _records)
            {
                entries[index] = kvp.Value;
                index++;
            }
            return entries;
        }

        /// <summary>
        /// 統計情報を取得する。総挑戦回数、総クリア回数、Platinum獲得数を集計する。
        /// </summary>
        public (int attempts, int clears, int platinums) GetStatistics()
        {
            int totalAttempts = 0;
            int totalClears = 0;
            int totalPlatinums = 0;

            foreach (KeyValuePair<string, LeaderboardEntry> kvp in _records)
            {
                LeaderboardEntry entry = kvp.Value;
                totalAttempts += entry.attemptCount;
                totalClears += entry.clearCount;

                if (entry.rank == ChallengeRank.Platinum)
                {
                    totalPlatinums++;
                }
            }

            return (totalAttempts, totalClears, totalPlatinums);
        }

        /// <summary>
        /// 特定チャレンジの挑戦回数を取得する。記録が存在しない場合は 0 を返す。
        /// </summary>
        public int GetAttemptCount(string challengeId)
        {
            if (_records.TryGetValue(challengeId, out LeaderboardEntry entry))
            {
                return entry.attemptCount;
            }

            return 0;
        }

        /// <summary>
        /// 特定チャレンジをクリア済みかどうか判定する。記録が存在しない場合は false を返す。
        /// </summary>
        public bool HasCleared(string challengeId)
        {
            if (_records.TryGetValue(challengeId, out LeaderboardEntry entry))
            {
                return entry.clearCount > 0;
            }

            return false;
        }

        /// <summary>
        /// セーブ用にシリアライズする。
        /// </summary>
        public object Serialize()
        {
            LeaderboardEntry[] entries = GetAllRecords();
            return entries;
        }

        /// <summary>
        /// セーブデータからデシリアライズする。
        /// </summary>
        public void Deserialize(object data)
        {
            _records.Clear();

            if (data is LeaderboardEntry[] entries)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    _records[entries[i].challengeId] = entries[i];
                }
            }
        }
    }
}
