using System;
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
        [Header("モード設定")]
        public CharacterModeData[] modes;

        [Header("行動定義")]
        public ActData[] actDataList;

        [Header("クールタイム")]
        public CoolTimeData coolTimeData;

        [Header("仲間AI設定")]
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
        public CharacterMode mode;
        public float detectionRange;
        public float combatRange;
        public float retreatHpThreshold;
        public int[] availableActIndices;
        public TransitionCondition[] transitions;
    }

    [Serializable]
    public struct TransitionCondition
    {
        public CharacterMode targetMode;
        public TriggerType trigger;
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
        public string actName;
        public ActType actType;
        public int attackInfoIndex;
        public TriggerJudgeData triggerJudge;
        public TargetJudgeData targetJudge;
        public ActJudgeData actJudge;
        public float weight;
        public float coolTime;
        public int maxUseCount;
    }

    [Serializable]
    public struct TriggerJudgeData
    {
        public TriggerConditionType condition;
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
        public float maxRange;
        public float minRange;
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
        public float requiredMp;
        public float requiredStamina;
        public float minHpRatio;
        public float maxHpRatio;
        public bool requireLineOfSight;
        public bool requireGrounded;
    }

    [Serializable]
    public struct CoolTimeData
    {
        public float globalCoolTime;
        public float attackCoolTime;
        public float skillCoolTime;
        public float guardCoolTime;
        public float dodgeCoolTime;
        public float healCoolTime;
    }

    [Serializable]
    public struct CompanionBehaviorSetting
    {
        public float followDistance;
        public float maxLeashDistance;
        public float supportHpThreshold;
        public bool autoHeal;
        public bool prioritizePlayerTarget;
        public CompanionStance defaultStance;
    }
}
