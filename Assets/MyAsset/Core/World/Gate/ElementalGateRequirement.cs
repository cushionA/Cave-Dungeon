using System;

namespace Game.Core
{

    /// <summary>
    /// 属性ゲートの属性要件定義。
    /// </summary>
    [Serializable]
    public struct ElementalGateRequirement
    {
        public ElementalRequirement element;
        public float minDamage;    // 必要最低ダメージ（0なら属性攻撃で触れるだけでOK）
    }
}
