using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// GameManagerのコアロジック。MonoBehaviour非依存でテスト可能。
    /// SoACharaDataDic保持、GameEventsハブ、サブマネージャー管理。
    /// </summary>
    public class GameManagerCore : IDisposable
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private List<IGameSubManager> _subManagers;
        private bool _initialized;
        private bool _disposed;

        public SoACharaDataDic Data => _data;
        public GameEvents Events => _events;
        public bool IsInitialized => _initialized;

        public GameManagerCore()
        {
            _subManagers = new List<IGameSubManager>();
            _events = new GameEvents();
        }

        /// <summary>
        /// 初期化。SoAコンテナを生成し、登録済みサブマネージャーをInitOrder順に初期化する。
        /// </summary>
        public void Initialize(int containerCapacity = 64)
        {
            if (_initialized)
            {
                return;
            }

            _data = new SoACharaDataDic(containerCapacity);

            // InitOrder昇順でソートして初期化
            _subManagers.Sort((a, b) => a.InitOrder.CompareTo(b.InitOrder));
            foreach (IGameSubManager mgr in _subManagers)
            {
                mgr.Initialize(_data, _events);
            }

            _initialized = true;
        }

        /// <summary>サブマネージャーを登録する（Initialize前に呼ぶ）</summary>
        public void RegisterSubManager(IGameSubManager manager)
        {
            _subManagers.Add(manager);
        }

        /// <summary>キャラクター登録</summary>
        public int RegisterCharacter(int hash, CharacterVitals vitals, CombatStats combat,
            CharacterFlags flags, MoveParams move)
        {
            int index = _data.Add(hash, vitals, combat, flags, move);
            _events.FireCharacterRegistered(hash);
            return index;
        }

        /// <summary>キャラクター削除</summary>
        public void UnregisterCharacter(int hash)
        {
            _data.Remove(hash);
            _events.FireCharacterRemoved(hash);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (IGameSubManager mgr in _subManagers)
            {
                mgr.Dispose();
            }

            _data?.Dispose();
            _events?.Dispose();
            _subManagers.Clear();
            _initialized = false;
            _disposed = true;
        }
    }
}
