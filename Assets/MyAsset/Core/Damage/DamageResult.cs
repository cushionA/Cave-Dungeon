using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    public struct DamageResult
    {
        public ActorRef attackerRef;
        public ActorRef defenderRef;
        public long actionInstanceId;
        public int totalDamage;
        public GuardResult guardResult;
        public HitReaction hitReaction;
        public SituationalBonus situationalBonus;
        public bool isCritical;
        public bool isKill;
        public float guardDamage;
        public float armorDamage;
        public float actionArmorDamage;
        public StatusEffectId appliedEffect;
    }
}
