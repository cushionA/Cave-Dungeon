using System;

namespace Game.Core
{
    /// <summary>
    /// 召喚枠管理（最大2枠）の純ロジッククラス。
    /// 召喚追加/解除/全解除/寿命Tickを管理する。
    /// </summary>
    public class SummonManager
    {
        private readonly SummonSlot[] _slots;
        private readonly bool[] _occupied;
        private int _activeCount;

        public int ActiveCount => _activeCount;

        public event Action<int> OnSummonCreated;
        public event Action<int> OnSummonDismissed;

        public SummonManager()
        {
            _slots = new SummonSlot[PartyManager.k_MaxSummonSlots];
            _occupied = new bool[PartyManager.k_MaxSummonSlots];
            _activeCount = 0;
        }

        /// <summary>
        /// 召喚枠に空きがあるか。
        /// </summary>
        public bool HasEmptySlot()
        {
            return _activeCount < PartyManager.k_MaxSummonSlots;
        }

        /// <summary>
        /// 召喚獣を追加する。枠が空いていればtrueを返す。
        /// </summary>
        public bool AddSummon(int summonHash, float duration, SummonType summonType)
        {
            int freeIndex = FindFreeSlot();
            if (freeIndex < 0)
            {
                return false;
            }

            _slots[freeIndex] = new SummonSlot
            {
                summonHash = summonHash,
                remainingTime = duration,
                summonType = summonType
            };
            _occupied[freeIndex] = true;
            _activeCount++;

            OnSummonCreated?.Invoke(summonHash);
            return true;
        }

        /// <summary>
        /// 指定召喚獣を解除する。
        /// </summary>
        public void Dismiss(int summonHash)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_occupied[i] && _slots[i].summonHash == summonHash)
                {
                    _occupied[i] = false;
                    _activeCount--;
                    OnSummonDismissed?.Invoke(summonHash);
                    return;
                }
            }
        }

        /// <summary>
        /// 全召喚獣を解除する。
        /// </summary>
        public void DismissAll()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_occupied[i])
                {
                    int hash = _slots[i].summonHash;
                    _occupied[i] = false;
                    OnSummonDismissed?.Invoke(hash);
                }
            }
            _activeCount = 0;
        }

        /// <summary>
        /// 毎フレーム寿命更新。期限切れの召喚獣を自動解除する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_occupied[i])
                {
                    continue;
                }

                SummonSlot slot = _slots[i];
                if (slot.remainingTime <= 0f)
                {
                    // duration=0は無制限
                    continue;
                }

                slot.remainingTime -= deltaTime;
                _slots[i] = slot;

                if (slot.remainingTime <= 0f)
                {
                    _occupied[i] = false;
                    _activeCount--;
                    OnSummonDismissed?.Invoke(slot.summonHash);
                }
            }
        }

        /// <summary>
        /// 召喚獣の死亡時枠解放。
        /// </summary>
        public void OnSummonDeath(int summonHash)
        {
            Dismiss(summonHash);
        }

        /// <summary>
        /// 最も古い（残り時間が少ない）召喚獣のハッシュを返す。
        /// 枠入替え用。
        /// </summary>
        public int GetOldestSummonHash()
        {
            float minTime = float.MaxValue;
            int oldestHash = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_occupied[i] && _slots[i].remainingTime < minTime)
                {
                    minTime = _slots[i].remainingTime;
                    oldestHash = _slots[i].summonHash;
                }
            }

            return oldestHash;
        }

        /// <summary>
        /// アクティブスロットの情報を取得する。
        /// </summary>
        public SummonSlot[] GetActiveSlots()
        {
            SummonSlot[] active = new SummonSlot[_activeCount];
            int idx = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_occupied[i])
                {
                    active[idx++] = _slots[i];
                }
            }
            return active;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_occupied[i])
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
