using System;

namespace Game.Core
{
    /// <summary>
    /// 通貨の獲得・消費・残高管理を担当。
    /// デスペナルティ（残高の20%ロスト）にも対応。
    /// </summary>
    public class CurrencyManager : ISaveable
    {
        public const float k_DeathPenaltyRate = 0.2f;

        private int _balance;

        /// <summary>現在の残高。</summary>
        public int Balance => _balance;

        public CurrencyManager(int initialBalance = 0)
        {
            _balance = Math.Max(0, initialBalance);
        }

        /// <summary>通貨を追加。amount が 0 以下なら無視。int.MaxValue で飽和する。</summary>
        public void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (amount > int.MaxValue - _balance)
            {
                _balance = int.MaxValue;
                return;
            }

            _balance += amount;
        }

        /// <summary>通貨を消費。残高不足なら false を返し消費しない。</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (_balance < amount)
            {
                return false;
            }

            _balance -= amount;
            return true;
        }

        /// <summary>デスペナルティ。残高の20%を失う（切り捨て）。失った額を返す。</summary>
        public int ApplyDeathPenalty()
        {
            int lost = (int)(_balance * k_DeathPenaltyRate);
            _balance -= lost;
            return lost;
        }

        /// <summary>永続化用: 残高をシリアライズ。</summary>
        public int SerializeBalance()
        {
            return _balance;
        }

        /// <summary>永続化用: 残高をデシリアライズ。負数は0にクランプ。</summary>
        public void DeserializeBalance(int balance)
        {
            _balance = Math.Max(0, balance);
        }

        // ===== ISaveable =====

        public string SaveId => "CurrencyManager";

        object ISaveable.Serialize()
        {
            return SerializeBalance();
        }

        void ISaveable.Deserialize(object data)
        {
            int balance = Convert.ToInt32(data);
            DeserializeBalance(balance);
        }
    }
}
