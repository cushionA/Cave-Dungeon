using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// HP/MP/stamina/armor/position - per-character vitals stored in SoA container.
    /// </summary>
    [Serializable]
    public struct CharacterVitals
    {
        public int currentHp;
        public int maxHp;
        public float currentMp;
        public int maxMp;
        public float currentStamina;
        public float maxStamina;
        public float currentArmor;
        public float maxArmor;
        public Vector2 position;
        public int level;
        public float staminaRecoveryRate;     // スタミナ回復速度(/秒)
        public float staminaRecoveryDelay;    // 消費後回復開始遅延(秒)
        // 割合の正本は現在値と最大値。独立したキャッシュの書き戻しは持たない。
        public readonly byte hpRatio => maxHp > 0
            ? (byte)Math.Min(100L, Math.Max(0L, 100L * currentHp / maxHp)) : (byte)0;
        public readonly byte mpRatio => maxMp > 0
            ? (byte)Math.Min(100f, Math.Max(0f, 100f * currentMp / maxMp)) : (byte)0;
    }
}
