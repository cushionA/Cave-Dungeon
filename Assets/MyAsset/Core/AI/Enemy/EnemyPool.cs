using System.Collections.Generic;

namespace Game.Core
{
    public class EnemyPool
    {
        private Stack<int> _available;
        private HashSet<int> _active;
        private int _nextId;

        public int ActiveCount => _active.Count;

        public EnemyPool(int preWarmCount = 16)
        {
            _available = new Stack<int>(preWarmCount);
            _active = new HashSet<int>();
            _nextId = 5000;

            for (int i = 0; i < preWarmCount; i++)
            {
                _available.Push(_nextId++);
            }
        }

        public int Get()
        {
            int id;
            if (_available.Count > 0)
            {
                id = _available.Pop();
            }
            else
            {
                id = _nextId++;
            }
            _active.Add(id);
            return id;
        }

        public void Return(int id)
        {
            if (_active.Remove(id))
            {
                _available.Push(id);
            }
        }

        public void ReturnAll()
        {
            foreach (int id in _active)
            {
                _available.Push(id);
            }
            _active.Clear();
        }
    }
}
