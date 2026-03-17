namespace Game.Core
{
    /// <summary>
    /// GameManagerに登録されるサブマネージャーのインターフェース。
    /// InitOrder で初期化順序を制御する。
    /// </summary>
    public interface IGameSubManager
    {
        /// <summary>初期化順序。小さい値ほど先に初期化される。</summary>
        int InitOrder { get; }

        void Initialize(SoACharaDataDic data, GameEvents events);

        void Dispose();
    }
}
