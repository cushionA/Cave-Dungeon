using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// バックトラック報酬のデータ定義。ScriptableObjectのシリアライズ用。
    /// </summary>
    [Serializable]
    public struct BacktrackRewardData
    {
        public string rewardId;
        public BacktrackRewardType rewardType;
        public AbilityFlag requiredAbility;
        public string locationHint;
        public string itemId;
        public int currencyAmount;
        public string shortcutGateId;
        public string loreText;
    }

    /// <summary>
    /// 全バックトラック報酬の状態管理。
    /// エリアごとの報酬登録、回収状態追跡、能力獲得時の再評価を担う。
    /// </summary>
    public class BacktrackRewardManager : ISaveable
    {
        private readonly Dictionary<string, BacktrackRewardData[]> _areaRewards;
        private readonly HashSet<string> _collectedRewards;
        private int _totalCount;

        public int TotalRewardCount => _totalCount;

        public event Action<string, AbilityFlag> OnRewardAvailable;
        public event Action<string> OnRewardCollected;

        public BacktrackRewardManager()
        {
            _areaRewards = new Dictionary<string, BacktrackRewardData[]>();
            _collectedRewards = new HashSet<string>();
            _totalCount = 0;
        }

        /// <summary>
        /// エリアの報酬リストを登録する。
        /// </summary>
        public void RegisterRewards(string areaId, BacktrackRewardData[] rewards)
        {
            if (rewards == null)
            {
                return;
            }

            _areaRewards[areaId] = rewards;
            _totalCount += rewards.Length;
        }

        /// <summary>
        /// 指定報酬が回収済みか判定する。
        /// </summary>
        public bool IsCollected(string rewardId)
        {
            return _collectedRewards.Contains(rewardId);
        }

        /// <summary>
        /// 報酬を回収済みとしてマークする。
        /// </summary>
        public void MarkCollected(string rewardId)
        {
            if (_collectedRewards.Add(rewardId))
            {
                OnRewardCollected?.Invoke(rewardId);
            }
        }

        /// <summary>
        /// 指定エリアで現在の能力でアクセス可能かつ未回収の報酬を取得する。
        /// </summary>
        public BacktrackRewardData[] GetAvailableRewards(string areaId, AbilityFlag currentAbilities)
        {
            if (!_areaRewards.TryGetValue(areaId, out BacktrackRewardData[] rewards))
            {
                return new BacktrackRewardData[0];
            }

            List<BacktrackRewardData> available = new List<BacktrackRewardData>();
            for (int i = 0; i < rewards.Length; i++)
            {
                BacktrackRewardData reward = rewards[i];
                if (!_collectedRewards.Contains(reward.rewardId) &&
                    (reward.requiredAbility & currentAbilities) == reward.requiredAbility)
                {
                    available.Add(reward);
                }
            }

            return available.ToArray();
        }

        /// <summary>
        /// 全報酬を再評価し、新たにアクセス可能になった報酬をイベント通知する。
        /// 新能力獲得時に呼ばれる。
        /// </summary>
        public int ReevaluateAll(AbilityFlag currentAbilities)
        {
            int newlyAvailable = 0;

            foreach (KeyValuePair<string, BacktrackRewardData[]> kvp in _areaRewards)
            {
                BacktrackRewardData[] rewards = kvp.Value;
                for (int i = 0; i < rewards.Length; i++)
                {
                    BacktrackRewardData reward = rewards[i];
                    if (!_collectedRewards.Contains(reward.rewardId) &&
                        (reward.requiredAbility & currentAbilities) == reward.requiredAbility)
                    {
                        OnRewardAvailable?.Invoke(reward.rewardId, reward.requiredAbility);
                        newlyAvailable++;
                    }
                }
            }

            return newlyAvailable;
        }

        /// <summary>
        /// 報酬IDから報酬データを検索する。見つからない場合はrewardId=nullの空データを返す。
        /// </summary>
        public BacktrackRewardData FindReward(string rewardId)
        {
            foreach (KeyValuePair<string, BacktrackRewardData[]> kvp in _areaRewards)
            {
                BacktrackRewardData[] rewards = kvp.Value;
                for (int i = 0; i < rewards.Length; i++)
                {
                    if (rewards[i].rewardId == rewardId)
                    {
                        return rewards[i];
                    }
                }
            }
            return default;
        }

        /// <summary>
        /// 回収率を取得する（回収数 / 総数）。
        /// </summary>
        public float GetCollectionRate()
        {
            if (_totalCount == 0)
            {
                return 0f;
            }
            return (float)_collectedRewards.Count / _totalCount;
        }

        /// <summary>
        /// セーブ用: 回収済み報酬IDリストを取得する。
        /// </summary>
        public string[] GetCollectedIds()
        {
            string[] ids = new string[_collectedRewards.Count];
            _collectedRewards.CopyTo(ids);
            return ids;
        }

        /// <summary>
        /// ロード用: 回収済み報酬IDリストを復元する。
        /// </summary>
        public void LoadCollectedIds(string[] ids)
        {
            _collectedRewards.Clear();
            if (ids != null)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    _collectedRewards.Add(ids[i]);
                }
            }
        }

        // ===== ISaveable =====

        public string SaveId => "BacktrackRewardManager";

        object ISaveable.Serialize()
        {
            return GetCollectedIds();
        }

        void ISaveable.Deserialize(object data)
        {
            if (data is string[] ids)
            {
                LoadCollectedIds(ids);
            }
        }
    }
}
