using System.Collections.Generic;

namespace Game.Core
{
    public class ProjectilePool
    {
        private List<Projectile> _pool;
        private List<Projectile> _active;

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

            _active.Add(projectile);
            return projectile;
        }

        public void Return(Projectile projectile)
        {
            projectile.Reset();
            int index = _active.IndexOf(projectile);
            if (index >= 0)
            {
                SwapRemoveAt(index);
            }
            _pool.Add(projectile);
        }

        public void ReturnAllDead()
        {
            int i = 0;
            while (i < _active.Count)
            {
                if (!_active[i].IsAlive)
                {
                    Projectile p = _active[i];
                    p.Reset();
                    SwapRemoveAt(i);
                    _pool.Add(p);
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
            if (index < lastIndex)
            {
                _active[index] = _active[lastIndex];
            }
            _active.RemoveAt(lastIndex);
        }

        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Reset();
                _pool.Add(_active[i]);
            }
            _active.Clear();
        }
    }
}
