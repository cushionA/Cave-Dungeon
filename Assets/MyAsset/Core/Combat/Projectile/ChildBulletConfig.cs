using System;

namespace Game.Core
{
    /// <summary>
    /// 子弾の生成設定。MagicDefinitionから参照される。
    /// trigger条件を満たした時に、親弾の位置からcount発の子弾をspreadAngle角度で生成する。
    /// </summary>
    [Serializable]
    public class ChildBulletConfig
    {
        public ChildBulletTrigger trigger;
        public BulletProfile profile;
        public int count;
        public float spreadAngle;
    }
}
