using System;
using System.Collections.Generic;

namespace Game.Core
{
    public class ProjectilePool
    {
        private readonly List<Projectile> _pool;
        private readonly List<Projectile> _active;
        private ulong _nextLeaseId;

        public IReadOnlyList<Projectile> ActiveProjectiles => _active;
        public int ActiveCount => _active.Count;

        public ProjectilePool(int preWarmCount = 32)
        {
            _pool = new List<Projectile>(preWarmCount);
            _active = new List<Projectile>();

            for (int i = 0; i < preWarmCount; i++)
            {
                _pool.Add(new Projectile());
            }
        }

        public Projectile Get()
        {
            if (_nextLeaseId == ulong.MaxValue) { throw new InvalidOperationException("Projectile lease identifiers are exhausted."); }
            Projectile projectile;
            if (_pool.Count > 0)
            {
                int lastIndex = _pool.Count - 1;
                projectile = _pool[lastIndex];
                _pool.RemoveAt(lastIndex);
            }
            else
            {
                projectile = new Projectile();
            }

            projectile.Acquire(this, _active.Count, ++_nextLeaseId);
            _active.Add(projectile);
            return projectile;
        }

        public void Return(Projectile projectile)
        {
            if (projectile != null) { TryReturn(projectile, projectile.LeaseId); }
        }

        /// <summary>参照と取得世代の両方を照合する。古い通知や別 Pool からの返却は無作用。</summary>
        public bool TryReturn(Projectile projectile, ulong expectedLeaseId)
        {
            if (!IsActive(projectile, expectedLeaseId)) { return false; }
            int index = projectile.ActiveIndex;
            SwapRemoveAt(index);
            projectile.Reset();
            _pool.Add(projectile);
            return true;
        }

        public bool IsActive(Projectile projectile, ulong expectedLeaseId)
        {
            return projectile != null && expectedLeaseId != 0 && projectile.LeaseId == expectedLeaseId
                && ReferenceEquals(projectile.PoolOwner, this) && projectile.ActiveIndex >= 0
                && projectile.ActiveIndex < _active.Count && ReferenceEquals(_active[projectile.ActiveIndex], projectile);
        }

        public void ReturnAllDead()
        {
            int i = 0;
            while (i < _active.Count)
            {
                if (!_active[i].IsAlive)
                {
                    Projectile projectile = _active[i];
                    TryReturn(projectile, projectile.LeaseId);
                }
                else
                {
                    i++;
                }
            }
        }

        private void SwapRemoveAt(int index)
        {
            int lastIndex = _active.Count - 1;
            Projectile removed = _active[index];
            if (index < lastIndex)
            {
                _active[index] = _active[lastIndex];
                _active[index].ActiveIndex = index;
            }
            _active.RemoveAt(lastIndex);
            removed.ActiveIndex = -1;
        }

        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Projectile projectile = _active[i];
                projectile.ActiveIndex = -1;
                projectile.Reset();
                _pool.Add(projectile);
            }
            _active.Clear();
        }
    }
}
