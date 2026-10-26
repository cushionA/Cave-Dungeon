namespace Game.Core
{
    public static partial class CombatDataHelper
    {
        /// <summary>存在するキャラクターの攻撃力をSoAから取得する。</summary>
        public static ElementalStatus GetAttackStats(SoACharaDataDic data, int ownerHash)
        {
            if (data == null || !data.TryGetValue(ownerHash, out int _))
            {
                return default;
            }
            return data.GetCombatStats(ownerHash).attack;
        }
    }
}
