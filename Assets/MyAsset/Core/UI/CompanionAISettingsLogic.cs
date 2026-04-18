using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Game.Core
{
    /// <summary>
    /// 仲間AI設定画面のUI状態管理。MonoBehaviour非依存の純ロジック。
    /// ModePresetRegistry / TacticalPresetRegistry をラップし、UIが必要とする
    /// 「編集中の戦術」「未保存フラグ」「タブ切替結果」などの状態を提供する。
    /// </summary>
    public class CompanionAISettingsLogic
    {
        /// <summary>UI上のタブ。</summary>
        public enum TabId : byte
        {
            TacticEdit = 0,
            Shortcut = 1,
        }

        /// <summary>SwitchTacticTargetの結果。</summary>
        public enum SwitchResult : byte
        {
            Succeeded = 0,
            RequiresUnsavedConfirm = 1,
            NotFound = 2,
        }

        private const int k_MaxModesPerTactic = 4;
        private const int k_ShortcutSlotCount = 4;

        private readonly ModePresetRegistry _modeRegistry;
        private readonly TacticalPresetRegistry _tacticalRegistry;

        private CompanionAIConfig _currentTactic;
        private CompanionAIConfig _editingBuffer;
        private string _editingConfigId;
        private TabId _activeTab;
        private bool _isDirty;

        public CompanionAISettingsLogic(ModePresetRegistry modeRegistry, TacticalPresetRegistry tacticalRegistry)
        {
            _modeRegistry = modeRegistry;
            _tacticalRegistry = tacticalRegistry;
            _activeTab = TabId.TacticEdit;
            _isDirty = false;
            _editingConfigId = null;

            // 現在の戦術の初期値（空）
            _currentTactic = new CompanionAIConfig
            {
                configId = null,
                configName = "現在の戦術",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[k_ShortcutSlotCount],
            };
            _editingBuffer = CloneConfig(_currentTactic);
        }

        /// <summary>現在アクティブなタブ。</summary>
        public TabId ActiveTab => _activeTab;

        /// <summary>編集中のバッファが未保存の変更を持っているか。</summary>
        public bool IsDirty => _isDirty;

        /// <summary>編集対象のプリセットID。null なら「現在の戦術」を編集中。</summary>
        public string EditingConfigId => _editingConfigId;

        /// <summary>編集中の戦術バッファ（読み取り専用ビュー）。UI側での参照用。</summary>
        public CompanionAIConfig EditingBuffer => _editingBuffer;

        /// <summary>現在の戦術（ゲーム実行時の実値）。</summary>
        public CompanionAIConfig CurrentTactic => _currentTactic;

        /// <summary>モードレジストリ参照。UI側で一覧取得に使う。</summary>
        public ModePresetRegistry ModeRegistry => _modeRegistry;

        /// <summary>戦術レジストリ参照。UI側で一覧取得・影響範囲取得に使う。</summary>
        public TacticalPresetRegistry TacticalRegistry => _tacticalRegistry;

        /// <summary>
        /// タブを切り替える。戦術編集→ショートカットなどの単純切替で、未保存フラグには影響しない。
        /// </summary>
        public void SwitchTab(TabId tab)
        {
            _activeTab = tab;
        }

        /// <summary>
        /// 編集対象を別プリセットへ切り替える。未保存変更がある場合は確認が必要であることを返す。
        /// force=true の場合は確認をスキップして切替を実行する（破棄確認後のUIから呼ぶ）。
        /// null を渡すと「現在の戦術」を編集対象にする。
        /// </summary>
        public SwitchResult SwitchEditingTarget(string configId, bool force)
        {
            if (_isDirty && !force)
            {
                return SwitchResult.RequiresUnsavedConfirm;
            }

            if (string.IsNullOrEmpty(configId))
            {
                _editingConfigId = null;
                _editingBuffer = CloneConfig(_currentTactic);
                _isDirty = false;
                return SwitchResult.Succeeded;
            }

            CompanionAIConfig? found = _tacticalRegistry.GetById(configId);
            if (!found.HasValue)
            {
                return SwitchResult.NotFound;
            }

            _editingConfigId = configId;
            _editingBuffer = CloneConfig(found.Value);
            _isDirty = false;
            return SwitchResult.Succeeded;
        }

        /// <summary>
        /// 編集バッファの戦術名を変更する。Dirty フラグを立てる。
        /// </summary>
        public void SetEditingName(string newName)
        {
            _editingBuffer.configName = newName;
            _isDirty = true;
        }

        /// <summary>
        /// 編集バッファに新しいモードを追加する。
        /// 最大数に達している場合は false を返す。
        /// </summary>
        public bool AddModeToBuffer(AIMode mode)
        {
            int currentCount = _editingBuffer.modes != null ? _editingBuffer.modes.Length : 0;
            if (currentCount >= k_MaxModesPerTactic)
            {
                return false;
            }

            AIMode[] newModes = new AIMode[currentCount + 1];
            if (_editingBuffer.modes != null)
            {
                for (int i = 0; i < currentCount; i++)
                {
                    newModes[i] = _editingBuffer.modes[i];
                }
            }
            newModes[currentCount] = mode;
            _editingBuffer.modes = newModes;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 編集バッファの指定インデックスのモードを削除する。
        /// インデックス範囲外の場合は false を返す。
        /// </summary>
        public bool RemoveModeFromBuffer(int index)
        {
            if (_editingBuffer.modes == null || index < 0 || index >= _editingBuffer.modes.Length)
            {
                return false;
            }

            AIMode[] newModes = new AIMode[_editingBuffer.modes.Length - 1];
            int dst = 0;
            for (int i = 0; i < _editingBuffer.modes.Length; i++)
            {
                if (i == index)
                {
                    continue;
                }
                newModes[dst] = _editingBuffer.modes[i];
                dst++;
            }
            _editingBuffer.modes = newModes;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 指定スロットのモードを、モードプリセットから差し替える（参照リンク維持）。
        /// </summary>
        public bool ReplaceModeFromPreset(int slotIndex, string modeId)
        {
            if (_editingBuffer.modes == null || slotIndex < 0 || slotIndex >= _editingBuffer.modes.Length)
            {
                return false;
            }

            AIMode? fromRegistry = _modeRegistry.GetById(modeId);
            if (!fromRegistry.HasValue)
            {
                return false;
            }

            _editingBuffer.modes[slotIndex] = fromRegistry.Value;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 指定スロットのモードを「独立コピー」に変換する（安全弁）。
        /// modeId を空文字列にすることで、将来のカスケード更新の対象から外す。
        /// </summary>
        public bool ConvertModeToIndependent(int slotIndex)
        {
            if (_editingBuffer.modes == null || slotIndex < 0 || slotIndex >= _editingBuffer.modes.Length)
            {
                return false;
            }

            AIMode mode = _editingBuffer.modes[slotIndex];
            if (string.IsNullOrEmpty(mode.modeId))
            {
                return false; // 既に独立
            }

            mode.modeId = "";
            _editingBuffer.modes[slotIndex] = mode;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 編集バッファの指定インデックスのモードを「まるごと差し替える」。
        /// モード詳細ダイアログからの編集結果を反映する用途で使う。
        /// カスケード参照を切るため、呼び出し側で modeId を適切に（空 or 新ID）設定しておくこと。
        /// インデックス範囲外の場合は false を返す。
        /// </summary>
        public bool UpdateModeInBuffer(int index, AIMode updated)
        {
            if (_editingBuffer.modes == null || index < 0 || index >= _editingBuffer.modes.Length)
            {
                return false;
            }
            _editingBuffer.modes[index] = updated;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// 編集バッファのモード遷移ルール配列を丸ごと差し替える。
        /// モード遷移ダイアログからの編集結果反映用途。null を渡した場合は空配列として扱う。
        /// </summary>
        public void SetTransitionRulesInBuffer(ModeTransitionRule[] rules)
        {
            _editingBuffer.modeTransitionRules = rules ?? new ModeTransitionRule[0];
            _isDirty = true;
        }

        /// <summary>
        /// 編集バッファを「現在の戦術」に反映する。プリセットへの書き戻しはしない。
        /// </summary>
        public void ApplyBufferToCurrentTactic()
        {
            _currentTactic = CloneConfig(_editingBuffer);
            // 現在の戦術として保存したら、editingConfigId は null に戻す
            _currentTactic.configId = null;
            _currentTactic.configName = "現在の戦術";
            _editingConfigId = null;
            _editingBuffer = CloneConfig(_currentTactic);
            _isDirty = false;
        }

        /// <summary>
        /// 編集中のバッファを現在の editingConfigId に対して上書き保存する。
        /// editingConfigId が null（現在の戦術編集中）の場合は ApplyBufferToCurrentTactic を代わりに呼ぶ。
        /// プリセットが存在しなければ false を返す。
        /// </summary>
        public bool SaveBufferToEditingPreset()
        {
            if (string.IsNullOrEmpty(_editingConfigId))
            {
                ApplyBufferToCurrentTactic();
                return true;
            }

            bool ok = _tacticalRegistry.UpdateById(_editingConfigId, _editingBuffer);
            if (ok)
            {
                CompanionAIConfig? reloaded = _tacticalRegistry.GetById(_editingConfigId);
                if (reloaded.HasValue)
                {
                    _editingBuffer = CloneConfig(reloaded.Value);
                }
                _isDirty = false;
            }
            return ok;
        }

        /// <summary>
        /// 現在の編集バッファを新しい戦術プリセットとして保存する。
        /// 保存後は editingConfigId を新しい ID に切り替え、バッファを再読み込みする。
        /// 上限超過時は null を返す。
        /// </summary>
        public string SaveBufferAsNewPreset(string presetName)
        {
            CompanionAIConfig toSave = CloneConfig(_editingBuffer);
            toSave.configName = presetName;
            string newId = _tacticalRegistry.Save(presetName, toSave);
            if (newId != null)
            {
                _editingConfigId = newId;
                CompanionAIConfig? reloaded = _tacticalRegistry.GetById(newId);
                if (reloaded.HasValue)
                {
                    _editingBuffer = CloneConfig(reloaded.Value);
                }
                _isDirty = false;
            }
            return newId;
        }

        /// <summary>
        /// 既存の戦術プリセットを複製して新しいプリセットを作成する。
        /// 名前は既存プリセットとの衝突を避けて「元名 (複製)」→「元名 (複製 2)」... のように解決する。
        /// 元名末尾の " (複製)" / " (複製 N)" サフィックスは剥がしてから採番するため、
        /// 多重複製しても末尾が膨張しない。上限超過時は null を返す。
        /// </summary>
        public string DuplicatePreset(string sourceConfigId)
        {
            CompanionAIConfig? source = _tacticalRegistry.GetById(sourceConfigId);
            if (!source.HasValue)
            {
                return null;
            }

            CompanionAIConfig[] allPresets = _tacticalRegistry.GetAll();
            List<string> existingNames = new List<string>(allPresets.Length);
            for (int i = 0; i < allPresets.Length; i++)
            {
                existingNames.Add(allPresets[i].configName);
            }

            string newName = ResolveDuplicateName(source.Value.configName, existingNames);

            // 浅いコピーだと modes 配列を共有してしまうので CloneConfig で深いコピーにする
            CompanionAIConfig duplicate = CloneConfig(source.Value);
            duplicate.configId = null; // Save 側で新GUIDを発行する
            duplicate.configName = newName;

            return _tacticalRegistry.Save(newName, duplicate);
        }

        /// <summary>
        /// 既存名と衝突しない「複製」系の新名を生成する。
        /// 最初の候補は「元名 (複製)」、衝突する場合は「元名 (複製 2)」「元名 (複製 3)」...と採番する。
        /// 入力名末尾の " (複製)" / " (複製 N)" サフィックスは事前に剥がすため、多重複製でも末尾が伸びない。
        /// 空名や null は "(無名)" として扱う。純関数のためテスト可能。
        /// </summary>
        public static string ResolveDuplicateName(string originalName, IEnumerable<string> existingNames)
        {
            string baseName = StripDuplicateSuffix(originalName);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "(無名)";
            }

            HashSet<string> existing = new HashSet<string>();
            if (existingNames != null)
            {
                foreach (string n in existingNames)
                {
                    if (!string.IsNullOrEmpty(n))
                    {
                        existing.Add(n);
                    }
                }
            }

            string candidate = baseName + " (複製)";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }

            // 既存名に (複製 N) が連続していない場合でも、最大 existing.Count + 2 回以内に空きが必ず見つかる
            int maxAttempt = existing.Count + 2;
            for (int n = 2; n <= maxAttempt; n++)
            {
                candidate = baseName + " (複製 " + n + ")";
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            // 実運用では到達しないが保険。maxAttempt + 1 なら必ず空き
            return baseName + " (複製 " + (maxAttempt + 1) + ")";
        }

        private static readonly Regex s_DuplicateSuffixRegex =
            new Regex(@"\s*\(複製(\s+\d+)?\)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 名前末尾の " (複製)" / " (複製 N)" サフィックスを1つだけ剥がす。
        /// ResolveDuplicateName 内部で使う純関数。
        /// </summary>
        private static string StripDuplicateSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            return s_DuplicateSuffixRegex.Replace(name, "").TrimEnd();
        }

        /// <summary>
        /// 指定された戦術プリセットを削除する。
        /// 最後の1個は削除を拒否する（プリセットが0個にならない保証）。
        /// </summary>
        public bool DeletePreset(string configId)
        {
            if (_tacticalRegistry.Count <= 1)
            {
                return false;
            }

            bool ok = _tacticalRegistry.Delete(configId);
            if (ok && configId == _editingConfigId)
            {
                // 削除したプリセットを編集中なら、現在の戦術に戻す
                _editingConfigId = null;
                _editingBuffer = CloneConfig(_currentTactic);
                _isDirty = false;
            }
            return ok;
        }

        /// <summary>
        /// モードプリセット保存時の影響範囲（このモードIDを参照している戦術の数）を返す。
        /// </summary>
        public int GetModeReferenceCount(string modeId)
        {
            List<string> list = _tacticalRegistry.GetReferencingConfigs(modeId);
            return list.Count;
        }

        /// <summary>
        /// モードプリセット保存時の影響範囲リスト（参照元の戦術プリセット名）を返す。
        /// </summary>
        public List<string> GetModeReferencingConfigNames(string modeId)
        {
            List<string> result = new List<string>();
            List<string> ids = _tacticalRegistry.GetReferencingConfigs(modeId);
            for (int i = 0; i < ids.Count; i++)
            {
                CompanionAIConfig? cfg = _tacticalRegistry.GetById(ids[i]);
                if (cfg.HasValue)
                {
                    result.Add(string.IsNullOrEmpty(cfg.Value.configName) ? ids[i] : cfg.Value.configName);
                }
            }
            return result;
        }

        /// <summary>
        /// ショートカットスロットに戦術プリセットindexを割り当てる。
        /// </summary>
        public bool SetShortcutBinding(int slotIndex, int tacticIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_ShortcutSlotCount)
            {
                return false;
            }

            if (_editingBuffer.shortcutModeBindings == null || _editingBuffer.shortcutModeBindings.Length != k_ShortcutSlotCount)
            {
                _editingBuffer.shortcutModeBindings = new int[k_ShortcutSlotCount];
            }

            _editingBuffer.shortcutModeBindings[slotIndex] = tacticIndex;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// Dirtyフラグを明示的にクリアする（UI側で外部要因による保存完了時などに使う）。
        /// </summary>
        public void ClearDirty()
        {
            _isDirty = false;
        }

        /// <summary>
        /// CompanionAIConfigの浅いコピー＋配列を複製する。
        /// struct型のため、配列共有を避けるために内部で使う。
        /// </summary>
        private static CompanionAIConfig CloneConfig(CompanionAIConfig source)
        {
            CompanionAIConfig clone = new CompanionAIConfig
            {
                configId = source.configId,
                configName = source.configName,
            };

            if (source.modes != null)
            {
                clone.modes = new AIMode[source.modes.Length];
                for (int i = 0; i < source.modes.Length; i++)
                {
                    clone.modes[i] = source.modes[i];
                }
            }
            else
            {
                clone.modes = new AIMode[0];
            }

            if (source.modeTransitionRules != null)
            {
                clone.modeTransitionRules = new ModeTransitionRule[source.modeTransitionRules.Length];
                for (int i = 0; i < source.modeTransitionRules.Length; i++)
                {
                    clone.modeTransitionRules[i] = source.modeTransitionRules[i];
                }
            }
            else
            {
                clone.modeTransitionRules = new ModeTransitionRule[0];
            }

            if (source.shortcutModeBindings != null)
            {
                clone.shortcutModeBindings = new int[source.shortcutModeBindings.Length];
                for (int i = 0; i < source.shortcutModeBindings.Length; i++)
                {
                    clone.shortcutModeBindings[i] = source.shortcutModeBindings[i];
                }
            }
            else
            {
                clone.shortcutModeBindings = new int[k_ShortcutSlotCount];
            }

            return clone;
        }
    }
}
