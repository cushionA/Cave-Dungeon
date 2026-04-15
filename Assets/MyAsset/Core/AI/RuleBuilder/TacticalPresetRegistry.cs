using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 戦術プリセット（CompanionAIConfig）をGUIDベースで管理するレジストリ。
    /// ModePresetRegistry.OnModeUpdated を購読し、参照元の戦術の modes 配列を自動同期する。
    /// </summary>
    public class TacticalPresetRegistry : IDisposable
    {
        private const int k_MaxPresets = 20;
        private readonly Dictionary<string, CompanionAIConfig> _configs = new Dictionary<string, CompanionAIConfig>();
        private ModePresetRegistry _modeRegistry;

        public int Count => _configs.Count;

        public TacticalPresetRegistry(ModePresetRegistry modeRegistry)
        {
            _modeRegistry = modeRegistry;
            if (_modeRegistry != null)
            {
                _modeRegistry.OnModeUpdated += OnModePresetUpdated;
            }
        }

        /// <summary>
        /// 戦術を新規保存する。新しいGUIDを発行して configId として返す。
        /// 上限超過時は null を返す。
        /// </summary>
        public string Save(string configName, CompanionAIConfig config)
        {
            if (_configs.Count >= k_MaxPresets)
            {
                return null;
            }

            string configId = Guid.NewGuid().ToString("N");
            config.configId = configId;
            config.configName = configName;
            _configs[configId] = config;
            return configId;
        }

        /// <summary>
        /// 既存IDに対して上書き保存する。存在しなければ false を返す。
        /// </summary>
        public bool UpdateById(string configId, CompanionAIConfig config)
        {
            if (string.IsNullOrEmpty(configId) || !_configs.ContainsKey(configId))
            {
                return false;
            }

            config.configId = configId;
            _configs[configId] = config;
            return true;
        }

        /// <summary>IDを指定して戦術を取得する。見つからなければ null を返す。</summary>
        public CompanionAIConfig? GetById(string configId)
        {
            if (string.IsNullOrEmpty(configId) || !_configs.TryGetValue(configId, out CompanionAIConfig config))
            {
                return null;
            }
            return config;
        }

        /// <summary>IDを指定して削除する。存在しなければ false を返す。</summary>
        public bool Delete(string configId)
        {
            if (string.IsNullOrEmpty(configId))
            {
                return false;
            }
            return _configs.Remove(configId);
        }

        /// <summary>登録済み全戦術を配列で返す。</summary>
        public CompanionAIConfig[] GetAll()
        {
            CompanionAIConfig[] result = new CompanionAIConfig[_configs.Count];
            _configs.Values.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// 指定モードIDを参照している戦術の configId リストを返す。
        /// UI側で「このモードは N個の戦術で使われています」の影響範囲表示に使う。
        /// </summary>
        public List<string> GetReferencingConfigs(string modeId)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(modeId))
            {
                return result;
            }

            foreach (KeyValuePair<string, CompanionAIConfig> kvp in _configs)
            {
                AIMode[] modes = kvp.Value.modes;
                if (modes == null)
                {
                    continue;
                }
                for (int i = 0; i < modes.Length; i++)
                {
                    if (modes[i].modeId == modeId)
                    {
                        result.Add(kvp.Key);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// ModePresetRegistry.OnModeUpdated の購読ハンドラ。
        /// 全戦術の modes 配列を走査し、対応する modeId のエントリを更新後のモードで置換する。
        /// </summary>
        private void OnModePresetUpdated(string modeId, AIMode updatedMode)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return;
            }

            // Dictionary の列挙中に値を書き換えられないので、一旦キーを退避する
            List<string> keys = new List<string>(_configs.Keys);
            for (int k = 0; k < keys.Count; k++)
            {
                CompanionAIConfig config = _configs[keys[k]];
                AIMode[] modes = config.modes;
                if (modes == null)
                {
                    continue;
                }
                bool changed = false;
                for (int i = 0; i < modes.Length; i++)
                {
                    if (modes[i].modeId == modeId)
                    {
                        modes[i] = updatedMode;
                        changed = true;
                    }
                }
                if (changed)
                {
                    config.modes = modes;
                    _configs[keys[k]] = config;
                }
            }
        }

        public void Dispose()
        {
            if (_modeRegistry != null)
            {
                _modeRegistry.OnModeUpdated -= OnModePresetUpdated;
                _modeRegistry = null;
            }
        }
    }
}
