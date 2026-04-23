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
        /// Returns true if allocation succeeded, false if no points available
        /// or the stat has reached its cap.
        /// </summary>
        /// <param name="stat">振るステータスの種類。</param>
        /// <param name="statCaps">
        /// 各ステータスの上限値（Str/Dex/Intel/Vit/Mnd/End の順、要素数 6）。
        /// null または要素数不足なら上限チェックをスキップする。
        /// </param>
        public bool AllocatePoint(StatType stat, int[] statCaps = null)
        {
            if (_availablePoints <= 0)
            {
                return false;
            }

            // 上限チェック（statCaps が与えられた場合のみ）
            if (statCaps != null && statCaps.Length >= 6)
            {
                int currentValue = GetAllocatedValue(stat);
                int capIndex = (int)stat;
                if (capIndex >= 0 && capIndex < statCaps.Length && currentValue >= statCaps[capIndex])
                {
                    return false;
                }
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
        /// 振り直し: 全てのステータスを 0 に戻し、
        /// 振れるポイントを `_level * k_PointsPerLevel` に復元する。
        /// 特殊アイテム消費トリガーを想定しているため、コスト処理は呼び出し元の責任。
        /// </summary>
        public void RefundAllStatusPoints()
        {
            _allocatedStats = default;
            _availablePoints = _level * k_PointsPerLevel;
        }

        /// <summary>
        /// 指定ステータスを指定ポイント分減らし、振れるポイントを増やす。
        /// 負の points または現在値を超える points が渡された場合は false を返し、状態を変更しない。
        /// </summary>
        public bool RefundStatus(StatType stat, int points)
        {
            if (points < 0)
            {
                return false;
            }
            if (points == 0)
            {
                return true;
            }

            int currentValue = GetAllocatedValue(stat);
            if (points > currentValue)
            {
                return false;
            }

            switch (stat)
            {
                case StatType.Str:
                    _allocatedStats.str -= points;
                    break;
                case StatType.Dex:
                    _allocatedStats.dex -= points;
                    break;
                case StatType.Intel:
                    _allocatedStats.intel -= points;
                    break;
                case StatType.Vit:
                    _allocatedStats.vit -= points;
                    break;
                case StatType.Mnd:
                    _allocatedStats.mnd -= points;
                    break;
                case StatType.End:
                    _allocatedStats.end -= points;
                    break;
                default:
                    return false;
            }

            _availablePoints += points;
            return true;
        }

        /// <summary>
        /// 指定ステータスの現在の割り振り値を返す。
        /// </summary>
        private int GetAllocatedValue(StatType stat)
        {
            switch (stat)
            {
                case StatType.Str: return _allocatedStats.str;
                case StatType.Dex: return _allocatedStats.dex;
                case StatType.Intel: return _allocatedStats.intel;
                case StatType.Vit: return _allocatedStats.vit;
                case StatType.Mnd: return _allocatedStats.mnd;
                case StatType.End: return _allocatedStats.end;
                default: return 0;
            }
        }

        /// <summary>
        /// statCaps から動的最大レベルを算出し、ハードキャップ (LevelUpConfig.maxLevel) との min を返す。
        /// 動的最大レベル = sum(statCaps) / k_PointsPerLevel
        /// 実効最大レベル = Min(dynamicMaxLevel, hardCap)
        /// statCaps が null または要素数不足の場合は hardCap を返す。
        ///
        /// ハードキャップは B3 で導入された <see cref="LevelUpConfig"/> SO (静的デフォルト) を優先参照し、
        /// SO 未設定時は <see cref="k_DefaultMaxLevel"/> にフォールバックする。これにより SO で
        /// maxLevel を差し替えれば動的最大レベル算出も自動連動する。
        /// </summary>
        public static int GetEffectiveMaxLevel(int[] statCaps)
        {
            LevelUpConfig config = GetDefaultConfig();
            int hardCap = config != null ? config.maxLevel : k_DefaultMaxLevel;

            if (statCaps == null || statCaps.Length < 6)
            {
                return hardCap;
            }

            int sum = 0;
            for (int i = 0; i < 6; i++)
            {
                int cap = statCaps[i];
                if (cap < 0)
                {
                    cap = 0;
                }
                sum += cap;
            }

            int dynamicMaxLevel = sum / k_PointsPerLevel;
            return Math.Min(dynamicMaxLevel, hardCap);
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
