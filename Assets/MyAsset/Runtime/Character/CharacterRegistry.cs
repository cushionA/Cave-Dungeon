using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private static readonly Dictionary<string, int> _nameToHash = new Dictionary<string, int>(16);
        private static readonly Dictionary<int, string> _hashToName = new Dictionary<int, string>(16);
        private static int _playerHash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
        }

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

        /// <summary>
        /// キャラクター名とハッシュの対応を登録する。
        /// DialogueSystem等、名前でキャラクターを参照する外部システム向け。
        /// </summary>
        public static void RegisterName(string name, int hash)
        {
            _nameToHash[name] = hash;
            _hashToName[hash] = name;
        }

        /// <summary>
        /// 名前からハッシュを検索する。
        /// </summary>
        public static bool TryGetHashByName(string name, out int hash)
        {
            return _nameToHash.TryGetValue(name, out hash);
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

            // 名前マッピングから削除（逆引きDictionaryでO(1)）
            if (_hashToName.TryGetValue(hash, out string name))
            {
                _nameToHash.Remove(name);
                _hashToName.Remove(hash);
            }
        }

        public static void Clear()
        {
            _allHashes.Clear();
            _allyHashes.Clear();
            _enemyHashes.Clear();
            _nameToHash.Clear();
            _hashToName.Clear();
            _playerHash = 0;
        }
    }
}
