namespace Game.Core
{
    /// <summary>
    /// MagicSystemからSummonManagerへの橋渡しクラス。
    /// MagicCasterのCast()フローでMagicType.Summonの場合に使用する。
    /// </summary>
    public class SummonMagicBridge
    {
        private readonly SummonManager _summonManager;

        public SummonMagicBridge(SummonManager summonManager)
        {
            _summonManager = summonManager;
        }

        /// <summary>
        /// MagicType が Summon かどうか判定する。
        /// </summary>
        public static bool IsSummonMagic(MagicType type)
        {
            return type == MagicType.Summon;
        }

        /// <summary>
        /// 召喚を試行する。枠がなければfalseを返す。
        /// </summary>
        public bool TrySummon(int summonHash, float duration, SummonType summonType, int casterHash)
        {
            return _summonManager.AddSummon(summonHash, duration, summonType);
        }

        /// <summary>
        /// 召喚を試行し、枠がなければ最古を解除して入れ替える。
        /// パーティ制限のコア実装。
        /// </summary>
        public bool TrySummonWithReplace(int summonHash, float duration, SummonType summonType, int casterHash)
        {
            if (_summonManager.HasEmptySlot())
            {
                return _summonManager.AddSummon(summonHash, duration, summonType);
            }

            // 枠満杯 → 最古の召喚獣を解除
            int oldestHash = _summonManager.GetOldestSummonHash();
            if (oldestHash != 0)
            {
                _summonManager.Dismiss(oldestHash);
            }

            return _summonManager.AddSummon(summonHash, duration, summonType);
        }
    }
}
