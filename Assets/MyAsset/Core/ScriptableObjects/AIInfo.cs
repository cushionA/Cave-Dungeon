using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// AIの行動パターン、モード設定、判定条件を定義するScriptableObject。
    /// 既存のBrainStatusに相当する。
    /// </summary>
    [CreateAssetMenu(fileName = "NewAIInfo", menuName = "Game/AIInfo")]
    public class AIInfo : ScriptableObject
    {
        [TitleGroup("モード設定")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        public CharacterModeData[] modes;

        [TitleGroup("行動定義")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
        public ActData[] actDataList;

        [TitleGroup("クールタイム")]
        [InlineProperty]
        public CoolTimeData coolTimeData;

        [TitleGroup("仲間AI設定")]
        [InlineProperty]
        public CompanionBehaviorSetting companionSetting;
    }

    /// <summary>キャラクターの行動モード</summary>
    public enum CharacterMode : byte
    {
        Idle,
        Patrol,
        Alert,
        Combat,
        Retreat,
        Support,
    }

    /// <summary>AI行動の種類</summary>
    public enum ActType : byte
    {
        Attack,
        Move,
        Guard,
        Dodge,
        Heal,
        Buff,
        Wait,
        Flee,
        FeintCancel,
    }

    [Serializable]
    public struct CharacterModeData
    {
        [EnumToggleButtons]
        public CharacterMode mode;

        [FoldoutGroup("検知範囲")]
        [MinValue(0)]
        public float detectionRange;

        [FoldoutGroup("検知範囲")]
        [MinValue(0)]
        public float combatRange;

        [FoldoutGroup("検知範囲")]
        [Range(0f, 1f)]
        public float retreatHpThreshold;

        [Tooltip("actDataList のインデックス")]
        public int[] availableActIndices;

        [ListDrawerSettings(ShowFoldout = true)]
        public TransitionCondition[] transitions;
    }

    [Serializable]
    public struct TransitionCondition
    {
        [EnumToggleButtons]
        public CharacterMode targetMode;
        public TriggerType trigger;
        [MinValue(0)]
        public float threshold;
    }

    public enum TriggerType : byte
    {
        EnemyDetected,
        EnemyLost,
        HpBelowThreshold,
        HpAboveThreshold,
        AllyInDanger,
        TimerExpired,
    }

    [Serializable]
    public struct ActData
    {
        [LabelText("$actName")]
        [FoldoutGroup("基本")]
        public string actName;

        [FoldoutGroup("基本")]
        [EnumToggleButtons]
        public ActType actType;

        [FoldoutGroup("基本")]
        [Tooltip("AttackInfo配列のインデックス")]
        public int attackInfoIndex;

        [FoldoutGroup("基本")]
        [Range(0f, 100f)]
        public float weight;

        [FoldoutGroup("基本")]
        [MinValue(0)]
        public float coolTime;

        [FoldoutGroup("基本")]
        [Tooltip("0=無制限")]
        [MinValue(0)]
        public int maxUseCount;

        [FoldoutGroup("判定条件")]
        [InlineProperty]
        public TriggerJudgeData triggerJudge;

        [FoldoutGroup("判定条件")]
        [InlineProperty]
        public TargetJudgeData targetJudge;

        [FoldoutGroup("判定条件")]
        [InlineProperty]
        public ActJudgeData actJudge;
    }

    [Serializable]
    public struct TriggerJudgeData
    {
        public TriggerConditionType condition;
        [MinValue(0)]
        public float value;
        public ComparisonOperator comparison;
    }

    public enum TriggerConditionType : byte
    {
        Always,
        EnemyInRange,
        HpBelow,
        MpAbove,
        AllyHpBelow,
        CoolTimeReady,
        TargetStatusEffect,
    }

    public enum ComparisonOperator : byte
    {
        LessThan,
        LessOrEqual,
        Equal,
        GreaterOrEqual,
        GreaterThan,
    }

    [Serializable]
    public struct TargetJudgeData
    {
        public TargetSelectionType selectionType;
        [MinValue(0)]
        public float maxRange;
        [MinValue(0)]
        public float minRange;
        [EnumToggleButtons]
        public CharacterBelong targetBelong;
        public TargetFilter targetFilter;
    }

    public enum TargetSelectionType : byte
    {
        Nearest,
        Farthest,
        LowestHp,
        HighestThreat,
        Random,
        Self,
        NearestAlly,
    }

    [Serializable]
    public struct ActJudgeData
    {
        [MinValue(0)]
        public float requiredMp;
        [MinValue(0)]
        public float requiredStamina;
        [Range(0f, 1f)]
        public float minHpRatio;
        [Range(0f, 1f)]
        public float maxHpRatio;
        public bool requireLineOfSight;
        public bool requireGrounded;
    }

    [Serializable]
    public struct CoolTimeData
    {
        [MinValue(0)] public float globalCoolTime;
        [MinValue(0)] public float attackCoolTime;
        [MinValue(0)] public float skillCoolTime;
        [MinValue(0)] public float guardCoolTime;
        [MinValue(0)] public float dodgeCoolTime;
        [MinValue(0)] public float healCoolTime;
    }

    [Serializable]
    public struct CompanionBehaviorSetting
    {
        [MinValue(0)] public float followDistance;
        [MinValue(0)] public float maxLeashDistance;
        [Range(0f, 1f)] public float supportHpThreshold;
        public bool autoHeal;
        public bool prioritizePlayerTarget;
        public CompanionStance defaultStance;
    }
}
