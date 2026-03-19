using System.Collections.Generic;

namespace Game.Runtime
{
    /// <summary>
    /// アクティブキャラクターのハッシュリストを管理する。
    /// AIのターゲット候補リスト等に使用する。
    /// </summary>
    public static class CharacterRegistry
    {
        private static readonly List<int> _allHashes = new List<int>(16);
        private static readonly List<int> _allyHashes = new List<int>(4);
        private static readonly List<int> _enemyHashes = new List<int>(16);
        private static int _playerHash;

        public static int PlayerHash => _playerHash;
        public static List<int> AllHashes => _allHashes;
        public static List<int> AllyHashes => _allyHashes;
        public static List<int> EnemyHashes => _enemyHashes;

        public static void RegisterPlayer(int hash)
        {
            _playerHash = hash;
            _allHashes.Add(hash);
            _allyHashes.Add(hash);
        }

        public static void RegisterAlly(int hash)
        {
            _allHashes.Add(hash);
            _allyHashes.Add(hash);
        }

        public static void RegisterEnemy(int hash)
        {
            _allHashes.Add(hash);
            _enemyHashes.Add(hash);
        }

        public static void Unregister(int hash)
        {
            _allHashes.Remove(hash);
            _allyHashes.Remove(hash);
            _enemyHashes.Remove(hash);
            if (_playerHash == hash)
            {
                _playerHash = 0;
            }
        }

        public static void Clear()
        {
            _allHashes.Clear();
            _allyHashes.Clear();
            _enemyHashes.Clear();
            _playerHash = 0;
        }
    }
}
