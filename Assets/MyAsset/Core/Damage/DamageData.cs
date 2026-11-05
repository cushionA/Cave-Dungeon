using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 攻撃時に生成されるダメージ情報パケット。
    /// DamageDealerが生成し、DamageReceiverに渡す。
    /// </summary>
    public struct DamageData
    {
        public ActorRef attackerRef;
        public ActorRef defenderRef;
        public AttackContext attackContext;
        public AttackTargetPolicy targetPolicy;
        public long actionInstanceId;
        public int attackerHash;
        public int defenderHash;
        public ElementalStatus damage;
        public float motionValue;
        public Vector2 knockbackForce;
        public Element attackElement;
        public StatusEffectApplicationData[] statusEffects;
        public IReadOnlyList<StatusEffectApplicationData> equipmentStatusEffects;
        public AttackFeature feature;
        public float guardDamage;
        public float armorBreakValue;
        public float justGuardResistance;
        public bool isProjectile;
    }
}
