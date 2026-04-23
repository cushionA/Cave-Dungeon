using System;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>
    /// LevelUpLogicのセーブデータ。
    /// </summary>
    [Serializable]
    public struct LevelUpSaveData
    {
        public int level;
        public int currentExp;
        public int availablePoints;
        public StatModifier allocatedStats;
    }

    /// <summary>
    /// Handles experience accumulation, level-up judgment, stat point allocation,
    /// and vitals recalculation.
    /// </summary>
    public class LevelUpLogic : ISaveable
    {
        public const int k_PointsPerLevel = 3;
        public const int k_HpPerVit = 10;
        public const int k_MpPerMnd = 5;

        /// <summary>
        /// LevelUpConfig 未注入時のデフォルト最大レベル。
        /// 本番は GameManager 初期化時または SetDefaultConfig で LevelUpConfig SO を差し替える。
        /// </summary>
        public const int k_DefaultMaxLevel = 99;

        /// <summary>
        /// 後方互換用の最大レベル定数。既存テストやコードからの参照を保持するため残置。
        /// 新規コードは LevelUpConfig SO の maxLevel を参照すること。
        /// </summary>
        public const int k_MaxLevel = k_DefaultMaxLevel;

        /// <summary>
        /// プロセス全体でのデフォルト LevelUpConfig。GameManager 初期化時にセットされる想定。
        /// 未セット時はコンストラクタが内部でランタイム default (maxLevel = k_DefaultMaxLevel) を用意する。
        /// </summary>
        private static LevelUpConfig s_defaultConfig;

        private int _currentExp;
        private int _level;
        private int _availablePoints;
        private StatModifier _allocatedStats;
        private readonly LevelUpConfig _config;

        public int CurrentExp => _currentExp;
        public int Level => _level;
        public int AvailablePoints => _availablePoints;
        public StatModifier AllocatedStats => _allocatedStats;

        /// <summary>このインスタンスが参照する最大レベルキャップ。</summary>
        public int MaxLevel => _config != null ? _config.maxLevel : k_DefaultMaxLevel;

        /// <summary>
        /// プロセス全体のデフォルト LevelUpConfig を設定する。
        /// GameManager 初期化時に呼び出し、以降生成される LevelUpLogic に適用される。
        /// null を渡すとリセットされ、k_DefaultMaxLevel が使用される。
        /// </summary>
        public static void SetDefaultConfig(LevelUpConfig config)
        {
            s_defaultConfig = config;
        }

        /// <summary>現在のデフォルト LevelUpConfig を取得する (null 可)。</summary>
        public static LevelUpConfig GetDefaultConfig()
        {
            return s_defaultConfig;
        }

        public LevelUpLogic(int initialLevel = 1)
            : this(initialLevel, s_defaultConfig)
        {
        }

        public LevelUpLogic(int initialLevel, LevelUpConfig config)
        {
            if (initialLevel < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(initialLevel), "Level must be at least 1.");
            }

            _config = config;
            _level = initialLevel;
            _currentExp = 0;
            _availablePoints = 0;
            _allocatedStats = default;
        }

        /// <summary>
        /// Returns the experience required to reach the next level.
        /// Simple formula: level * 100.
        /// </summary>
        public int GetExpForNextLevel()
        {
            return _level * 100;
        }

        /// <summary>
        /// Adds experience points and triggers level-ups as needed.
        /// Returns the number of levels gained.
        /// </summary>
        public int AddExp(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Experience amount cannot be negative.");
            }

            _currentExp += amount;
            int levelsGained = 0;
            int maxLevel = MaxLevel;

            while (_level < maxLevel && _currentExp >= GetExpForNextLevel())
            {
                _currentExp -= GetExpForNextLevel();
                _level++;
                _availablePoints += k_PointsPerLevel;
                levelsGained++;
            }

            // 最大レベル到達時は余剰 exp を破棄（無限蓄積防止）
            if (_level >= maxLevel)
            {
                _currentExp = 0;
            }

            return levelsGained;
        }

        /// <summary>
        /// Allocates one stat point to the specified stat.
        /// Returns true if allocation succeeded, false if no points available.
        /// </summary>
        public bool AllocatePoint(StatType stat)
        {
            if (_availablePoints <= 0)
            {
                return false;
            }

            _availablePoints--;

            switch (stat)
            {
                case StatType.Str:
                    _allocatedStats.str++;
                    break;
                case StatType.Dex:
                    _allocatedStats.dex++;
                    break;
                case StatType.Intel:
                    _allocatedStats.intel++;
                    break;
                case StatType.Vit:
                    _allocatedStats.vit++;
                    break;
                case StatType.Mnd:
                    _allocatedStats.mnd++;
                    break;
                case StatType.End:
                    _allocatedStats.end++;
                    break;
                default:
                    _availablePoints++;
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Recalculates vitals based on allocated stats.
        /// Applies Vit bonus to maxHp and Mnd bonus to maxMp, and updates level.
        /// </summary>
        public CharacterVitals RecalculateVitals(CharacterVitals baseVitals)
        {
            CharacterVitals result = baseVitals;
            result.maxHp = baseVitals.maxHp + (_allocatedStats.vit * k_HpPerVit);
            result.maxMp = baseVitals.maxMp + (_allocatedStats.mnd * k_MpPerMnd);
            result.level = _level;
            return result;
        }

        // ===== ISaveable =====

        public string SaveId => "LevelUpLogic";

        object ISaveable.Serialize()
        {
            return new LevelUpSaveData
            {
                level = _level,
                currentExp = _currentExp,
                availablePoints = _availablePoints,
                allocatedStats = _allocatedStats
            };
        }

        void ISaveable.Deserialize(object data)
        {
            LevelUpSaveData saveData;
            if (data is LevelUpSaveData direct)
            {
                saveData = direct;
            }
            else if (data is JObject jObj)
            {
                saveData = jObj.ToObject<LevelUpSaveData>();
            }
            else
            {
                return;
            }

            _level = Math.Max(1, saveData.level);
            _currentExp = Math.Max(0, saveData.currentExp);
            _availablePoints = Math.Max(0, saveData.availablePoints);
            _allocatedStats = saveData.allocatedStats;
        }
    }

    public enum StatType : byte
    {
        Str,
        Dex,
        Intel,
        Vit,
        Mnd,
        End
    }
}
