using System;

namespace Game.Core
{
    /// <summary>
    /// ボスフェーズ遷移条件。
    /// </summary>
    [Serializable]
    public struct PhaseCondition
    {
        public PhaseConditionType type;
        public float threshold;    // HP割合 or 秒数 or 回数
    }

    /// <summary>
    /// 召喚スロット情報。SummonManagerが管理する固定長配列の要素。
    /// </summary>
    [Serializable]
    public struct SummonSlot
    {
        public int summonHash;        // 召喚獣のhashCode
        public float remainingTime;   // 残り時間
        public SummonType summonType;
    }

    /// <summary>
    /// バックトラック報酬エントリ。回収状態を含む。
    /// </summary>
    [Serializable]
    public struct BacktrackEntry
    {
        public string rewardId;
        public BacktrackRewardType rewardType;
        public AbilityFlag requiredAbility;    // 必要な能力フラグ
        public string locationHint;            // マップ上のヒント文
        public bool collected;                 // 回収済みフラグ
    }

    /// <summary>
    /// 属性ゲートの属性要件定義。
    /// </summary>
    [Serializable]
    public struct ElementalGateRequirement
    {
        public ElementalRequirement element;
        public float minDamage;    // 必要最低ダメージ（0なら属性攻撃で触れるだけでOK）
    }

    /// <summary>
    /// 混乱状態の追跡データ。ConfusionEffectProcessorが内部管理に使用。
    /// </summary>
    public struct ConfusionState
    {
        public int targetHash;
        public int controllerHash;        // 混乱をかけたキャラのハッシュ
        public float remainingDuration;
        public CharacterBelong originalBelong;  // 復帰用に保存
        public float accumulatedDamage;    // 混乱中に受けたダメージ（解除閾値用）
    }

    /// <summary>
    /// ボスの1フェーズ分のデータ定義。BossDefinitionが配列で保持する。
    /// </summary>
    [Serializable]
    public struct BossPhaseData
    {
        public string phaseName;               // "第1形態" 等
        public AIMode[] modes;                 // このフェーズのAIモード配列
        public PhaseCondition exitCondition;   // このフェーズから次への遷移条件
        public float transitionInvincibleTime; // 遷移時無敵秒数
        public bool spawnAdds;                 // 雑魚召喚フラグ
        public string[] addSpawnerIds;         // 召喚する雑魚のスポナーID
    }
}
