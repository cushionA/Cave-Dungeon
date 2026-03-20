using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// FlagManagerのセーブデータ。
    /// </summary>
    [Serializable]
    public class FlagSaveData
    {
        public Dictionary<string, bool> globalFlags;
        public Dictionary<string, Dictionary<string, bool>> mapLocalFlags;
        public string currentMapId;
    }

    /// <summary>
    /// グローバルフラグ + マップローカルフラグを管理する。
    /// グローバル: ストーリー進行、好感度、実績等。
    /// マップローカル: イベント再生済み、宝箱開封等（mapId単位で切替）。
    /// ISaveable対応で永続化可能。
    /// </summary>
    public class FlagManager : ISaveable
    {
        private readonly Dictionary<string, bool> _globalFlags;
        private readonly Dictionary<string, Dictionary<string, bool>> _mapLocalFlags;
        private string _currentMapId;

        /// <summary>現在のマップID。SwitchMap未呼び出し時はnull。</summary>
        public string CurrentMapId => _currentMapId;

        public FlagManager()
        {
            _globalFlags = new Dictionary<string, bool>();
            _mapLocalFlags = new Dictionary<string, Dictionary<string, bool>>();
            _currentMapId = null;
        }

        // ===== グローバルフラグ =====

        /// <summary>グローバルフラグを設定する。</summary>
        public void SetGlobalFlag(string flagId, bool value)
        {
            _globalFlags[flagId] = value;
        }

        /// <summary>グローバルフラグを取得する。未設定の場合はfalse。</summary>
        public bool GetGlobalFlag(string flagId)
        {
            if (_globalFlags.TryGetValue(flagId, out bool value))
            {
                return value;
            }
            return false;
        }

        // ===== マップローカルフラグ =====

        /// <summary>マップを切り替える。ローカルフラグセットが切り替わる。</summary>
        public void SwitchMap(string mapId)
        {
            if (mapId == null)
            {
                return;
            }

            _currentMapId = mapId;

            if (!_mapLocalFlags.ContainsKey(mapId))
            {
                _mapLocalFlags[mapId] = new Dictionary<string, bool>();
            }
        }

        /// <summary>現在マップのローカルフラグを設定する。</summary>
        public void SetLocalFlag(string flagId, bool value)
        {
            if (_currentMapId == null)
            {
                return;
            }

            _mapLocalFlags[_currentMapId][flagId] = value;
        }

        /// <summary>現在マップのローカルフラグを取得する。未設定またはマップ未切替時はfalse。</summary>
        public bool GetLocalFlag(string flagId)
        {
            if (_currentMapId == null)
            {
                return false;
            }

            if (_mapLocalFlags.TryGetValue(_currentMapId, out Dictionary<string, bool> localFlags))
            {
                if (localFlags.TryGetValue(flagId, out bool value))
                {
                    return value;
                }
            }
            return false;
        }

        // ===== ISaveable =====

        public string SaveId => "FlagManager";

        object ISaveable.Serialize()
        {
            FlagSaveData saveData = new FlagSaveData
            {
                globalFlags = new Dictionary<string, bool>(_globalFlags),
                mapLocalFlags = new Dictionary<string, Dictionary<string, bool>>(),
                currentMapId = _currentMapId
            };

            foreach (KeyValuePair<string, Dictionary<string, bool>> kvp in _mapLocalFlags)
            {
                saveData.mapLocalFlags[kvp.Key] = new Dictionary<string, bool>(kvp.Value);
            }

            return saveData;
        }

        void ISaveable.Deserialize(object data)
        {
            if (data is FlagSaveData saveData)
            {
                _globalFlags.Clear();
                if (saveData.globalFlags != null)
                {
                    foreach (KeyValuePair<string, bool> kvp in saveData.globalFlags)
                    {
                        _globalFlags[kvp.Key] = kvp.Value;
                    }
                }

                _mapLocalFlags.Clear();
                if (saveData.mapLocalFlags != null)
                {
                    foreach (KeyValuePair<string, Dictionary<string, bool>> kvp in saveData.mapLocalFlags)
                    {
                        _mapLocalFlags[kvp.Key] = new Dictionary<string, bool>(kvp.Value);
                    }
                }

                _currentMapId = saveData.currentMapId;
            }
        }
    }
}
