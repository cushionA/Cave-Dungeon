using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// モード単体プリセット（AIMode）をGUIDベースで管理するレジストリ。
    /// 保存・更新・削除・一覧取得を提供し、UpdateById 時に OnModeUpdated を発行してカスケード同期を促す。
    /// 参照元の戦術プリセットは TacticalPresetRegistry が購読して自動同期する。
    /// </summary>
    public class ModePresetRegistry
    {
        private const int k_MaxPresets = 40;
        private readonly Dictionary<string, AIMode> _modes = new Dictionary<string, AIMode>();

        /// <summary>モード更新時のカスケード通知。引数: (modeId, 更新後のAIMode)</summary>
        public event Action<string, AIMode> OnModeUpdated;

        /// <summary>登録済みモード数</summary>
        public int Count => _modes.Count;

        /// <summary>
        /// モードを新規保存する。新しいGUIDを発行して modeId として返す。
        /// 上限超過時は null を返す。
        /// </summary>
        public string Save(AIMode mode)
        {
            if (_modes.Count >= k_MaxPresets)
            {
                return null;
            }

            string modeId = Guid.NewGuid().ToString("N");
            mode.modeId = modeId;
            _modes[modeId] = mode;
            return modeId;
        }

        /// <summary>
        /// 既存IDに対して上書き保存する。成功時は OnModeUpdated を発行する。
        /// IDが見つからなければ false を返す。
        /// </summary>
        public bool UpdateById(string modeId, AIMode mode)
        {
            if (string.IsNullOrEmpty(modeId) || !_modes.ContainsKey(modeId))
            {
                return false;
            }

            mode.modeId = modeId;
            _modes[modeId] = mode;
            OnModeUpdated?.Invoke(modeId, mode);
            return true;
        }

        /// <summary>
        /// IDを指定してモードを取得する。見つからなければ null を返す。
        /// </summary>
        public AIMode? GetById(string modeId)
        {
            if (string.IsNullOrEmpty(modeId) || !_modes.TryGetValue(modeId, out AIMode mode))
            {
                return null;
            }
            return mode;
        }

        /// <summary>
        /// IDを指定して削除する。存在しなければ false を返す。
        /// 参照元の戦術からは自動的には切り離さない（呼び出し側の責任）。
        /// </summary>
        public bool Delete(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return false;
            }
            return _modes.Remove(modeId);
        }

        /// <summary>登録済み全モードを配列で返す。</summary>
        public AIMode[] GetAll()
        {
            AIMode[] result = new AIMode[_modes.Count];
            _modes.Values.CopyTo(result, 0);
            return result;
        }
    }
}
