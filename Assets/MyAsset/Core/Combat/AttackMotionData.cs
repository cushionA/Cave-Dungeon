using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct AttackMotionData
    {
        public long actionInstanceId;
        [FoldoutGroup("対象条件")]
        public AttackTargetPolicy targetPolicy;
        [FoldoutGroup("基本")]
        [LabelText("行動名")] public string actionName;
        [FoldoutGroup("基本")]
        [MinValue(0)] public float motionValue;
        [FoldoutGroup("基本")]
        [EnumToggleButtons] public Element attackElement;
        [FoldoutGroup("基本")]
        public AttackFeature feature;

        [FoldoutGroup("コスト")]
        [MinValue(0)] public float staminaCost;
        [FoldoutGroup("コスト")]
        [MinValue(0)] public int mpCost;

        [FoldoutGroup("ヒット")]
        [MinValue(1)] public int maxHitCount;
        [FoldoutGroup("ヒット")]
        [MinValue(0)] public float guardDamage;
        [FoldoutGroup("ヒット")]
        [MinValue(0)] public float armorBreakValue;

        [FoldoutGroup("ノックバック")]
        public Vector2 knockbackForce;

        [FoldoutGroup("状態異常")]
        public StatusEffectApplicationData[] statusEffects;
        public IReadOnlyList<StatusEffectApplicationData> equipmentStatusEffects;

        [FoldoutGroup("移動")]
        [MinValue(0)] public float attackMoveDistance;
        [FoldoutGroup("移動")]
        [MinValue(0)] public float attackMoveDuration;
        [FoldoutGroup("移動")]
        public AttackContactType contactType;

        [FoldoutGroup("コンボ")]
        public bool isAutoChain;
        [FoldoutGroup("コンボ")]
        public bool isChainEndPoint;
        [FoldoutGroup("コンボ")]
        [MinValue(0)] public float inputWindow;

        [FoldoutGroup("ガード関連")]
        [Range(0f, 100f)] public float justGuardResistance;

        [FoldoutGroup("行動特殊効果")]
        [ListDrawerSettings(ShowFoldout = true)]
        public ActionEffect[] actionEffects;
    }
}
