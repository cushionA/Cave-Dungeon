using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 装備由来のステータス合計。SoAコンテナで管理。
    /// </summary>
    [Serializable]
    public struct EquipmentStatus
    {
        public int weaponId;
        public int shieldId;
        public int coreId;
        public GripMode gripMode;
        public StatModifier attributeModifiers;
        public AbilityFlag activeFlags;
        public float weightRatio;
        public float totalWeight;
        public float maxWeightCapacity;
        public float justGuardStartTime;
        public float justGuardDuration;
    }
}
