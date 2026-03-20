using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// チャレンジモードのアンロック状態を管理する純ロジッククラス。
    /// ISaveableを実装し、セーブ/ロードに対応する。
    /// </summary>
    public class ChallengeManager : ISaveable
    {
        // Fields

        private readonly HashSet<string> _unlockedChallengeIds = new HashSet<string>();

        // Properties

        public string SaveId => "ChallengeManager";

        // Public Methods

        /// <summary>
        /// アンロック済みチャレンジIDの配列を返す。
        /// </summary>
        public string[] GetUnlockedChallengeIds()
        {
            string[] result = new string[_unlockedChallengeIds.Count];
            _unlockedChallengeIds.CopyTo(result);
            return result;
        }

        /// <summary>
        /// 指定チャレンジをアンロックする。重複呼び出しは無視される。
        /// </summary>
        public void UnlockChallenge(string challengeId)
        {
            if (string.IsNullOrEmpty(challengeId))
            {
                return;
            }

            _unlockedChallengeIds.Add(challengeId);
        }

        /// <summary>
        /// 指定チャレンジがアンロック済みかを返す。
        /// </summary>
        public bool IsUnlocked(string challengeId)
        {
            if (string.IsNullOrEmpty(challengeId))
            {
                return false;
            }

            return _unlockedChallengeIds.Contains(challengeId);
        }

        /// <summary>
        /// アンロック状態をシリアライズする。
        /// </summary>
        public object Serialize()
        {
            List<string> list = new List<string>(_unlockedChallengeIds);
            return list;
        }

        /// <summary>
        /// シリアライズされたデータからアンロック状態を復元する。
        /// </summary>
        public void Deserialize(object data)
        {
            _unlockedChallengeIds.Clear();

            if (data is List<string> list)
            {
                foreach (string id in list)
                {
                    _unlockedChallengeIds.Add(id);
                }
            }
        }
    }
}
