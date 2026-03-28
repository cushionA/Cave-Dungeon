namespace Game.Core
{
    /// <summary>
    /// 運搬状態を追跡する構造体。
    /// 運搬者と被運搬者のハッシュを保持し、運搬の開始・解除を管理する。
    /// </summary>
    public struct CarryState
    {
        public int CarrierHash { get; private set; }
        public int CarriedHash { get; private set; }
        public bool IsActive { get; private set; }

        /// <summary>
        /// 運搬を開始する。
        /// </summary>
        public static CarryState Start(int carrierHash, int carriedHash)
        {
            return new CarryState
            {
                CarrierHash = carrierHash,
                CarriedHash = carriedHash,
                IsActive = true,
            };
        }

        /// <summary>
        /// 運搬を解除する。
        /// </summary>
        public static CarryState Release()
        {
            return default;
        }
    }
}
