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
                shortcutModeBindings = CreateDefaultShortcutBindings(),
            };
            _editingBuffer = CloneConfig(_currentTactic);
        }

        /// <summary>
        /// ショートカットスロット配列のデフォルト値（全スロット未割当 = -1）を生成する。
        /// 0埋めだと「modes[0]を指す」と区別できないため、未割当は -1 で表現する。
        /// </summary>
        public static int[] CreateDefaultShortcutBindings()
        {
            int[] arr = new int[k_ShortcutSlotCount];
            for (int i = 0; i < k_ShortcutSlotCount; i++)
            {
                arr[i] = -1;
            }
            return arr;
        }

        /// <summary>
        /// shortcutModeBindings の範囲外参照を一括で未割当(-1)に書き戻す純関数。
        /// モード削除/追加や戦術切替に伴い、modes.Length が変動したあとで呼び出し、
        /// 旧 binding が modes 配列の範囲外を指していた場合に -1 へクランプする。
        ///
        /// UI 描画中に「表示しながら書き換える」という副作用を排除するため、
        /// Controller.RefreshShortcutDropdowns ではなく Switch/Add/Remove 系の操作で
        /// 明示的に呼ぶ設計にしている。戻り値は「変更があったか」で、呼び出し側は
        /// Dirty 判定に利用できる (config.shortcutModeBindings が null の場合は
        /// デフォルトを再生成する必要があるが、その場合も true を返す)。
        /// </summary>
        public static bool ClearInvalidShortcutBindings(ref CompanionAIConfig config, int modeCount)
        {
            int safeModeCount = modeCount < 0 ? 0 : modeCount;

            if (config.shortcutModeBindings == null || config.shortcutModeBindings.Length != k_ShortcutSlotCount)
            {
                // スロット数不整合はデフォルト再生成。modes が空なら全 -1 なので実質差分なしだが、
                // 既存データが不正構造なら「構造的に修正した」とみなして true を返す。
                config.shortcutModeBindings = CreateDefaultShortcutBindings();
                return true;
            }

            bool changed = false;
            int[] bindings = config.shortcutModeBindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                int bound = bindings[i];
                if (bound < 0)
                {
                    // 既に未割当なら何もしない（負値の正規化は SetShortcutBinding 側の責務）
                    continue;
                }
                if (bound >= safeModeCount)
                {
                    bindings[i] = -1;
                    changed = true;
                }
            }
            return changed;
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
                // 切替元で modes 数が変動している可能性があるのでショートカットを正規化。
                // Switch 直後は Dirty=false が期待値なので戻り値は読み捨てる（書き換えは
                // 「切替元データの自動修復」であってユーザー編集ではない）。
                int modeCount = _editingBuffer.modes != null ? _editingBuffer.modes.Length : 0;
                ClearInvalidShortcutBindings(ref _editingBuffer, modeCount);
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
            int loadedModeCount = _editingBuffer.modes != null ? _editingBuffer.modes.Length : 0;
            ClearInvalidShortcutBindings(ref _editingBuffer, loadedModeCount);
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
            // modes 数が増えた場合、既存 binding が新たに範囲内に収まる可能性はあるが、
            // 逆に範囲外が発生することはない。ただし shortcutModeBindings が null や
            // 不正長さの場合はここでデフォルト再生成されるので常に呼ぶ。
            ClearInvalidShortcutBindings(ref _editingBuffer, newModes.Length);
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
            // 削除で modes 数が減ったので、削除された index を指していた binding を -1 に戻す。
            // なお「index より大きい binding を decrement する」運用は採用しない
            // （ショートカットは "このスロット番号のモード" を割り当てる UI 指向で、
            //  削除後はユーザーに再割り当てを促す方が誤爆が少ない）。
            ClearInvalidShortcutBindings(ref _editingBuffer, newModes.Length);
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
        /// ショートカットスロットに「現在編集中の戦術内の modeIndex」を割り当てる。
        /// -1 は未割当、0..3 は modes 配列の index。それ以外の負値は未割当 (-1) に正規化する。
        /// モード数上限側（modeIndex >= modes.Length）は UI 側で描画時にチェックするが、
        /// 不正な負値だけは保存段階で落として、後続の差分保存やファイル永続化に不正値が漏れないようにする。
        /// slotIndex が範囲外の場合は false を返す。
        /// </summary>
        public bool SetShortcutBinding(int slotIndex, int modeIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_ShortcutSlotCount)
            {
                return false;
            }

            if (_editingBuffer.shortcutModeBindings == null || _editingBuffer.shortcutModeBindings.Length != k_ShortcutSlotCount)
            {
                _editingBuffer.shortcutModeBindings = CreateDefaultShortcutBindings();
            }

            // -1 未満は未割当として正規化。上限側の範囲チェックは呼び出し側の責務。
            int normalized = modeIndex < -1 ? -1 : modeIndex;
            _editingBuffer.shortcutModeBindings[slotIndex] = normalized;
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
                clone.shortcutModeBindings = CreateDefaultShortcutBindings();
            }

            return clone;
        }

        /// <summary>
        /// targetSelect を1件削除した時の targetRules 配列を返す純粋関数。
        /// 参照していた rule は破棄し、より大きい actionIndex を decrement する。
        /// UI の RebuildTargetSelectList 削除ボタンから呼ばれ、同時にテストから直接検証される。
        /// </summary>
        public static AIRule[] AdjustTargetRulesForRemovedSelect(AIRule[] rules, int removedIdx)
        {
            if (rules == null || rules.Length == 0)
            {
                return rules ?? new AIRule[0];
            }

            List<AIRule> adjusted = new List<AIRule>(rules.Length);
            for (int k = 0; k < rules.Length; k++)
            {
                AIRule r = rules[k];
                if (r.actionIndex == removedIdx)
                {
                    continue;
                }
                if (r.actionIndex > removedIdx)
                {
                    r.actionIndex--;
                }
                adjusted.Add(r);
            }
            return adjusted.ToArray();
        }

        // =====================================================================
        // 行動ルール×ActionSlot 統合モデル用のヘルパー群（純関数）
        //
        // UI上は「1行=1ルール（条件+行動）」として見せるが、内部モデルは従来どおり
        // AIMode.actions[] と AIMode.actionRules[]（actionIndexで参照）を維持する。
        // 複数ルールが同一 ActionSlot を共有するケース（「同じ攻撃を別条件で」）も
        // サポートするため、編集中は actions[] を保持し、保存時に未参照スロットを
        // GC する設計にしている。
        //
        // いずれも AIMode を値渡し→更新→返却の形にして、テスト可能な純関数にしている。
        // ただし配列は新規確保するが、内部の struct 要素コピーは浅い（conditions 等の
        // 参照型フィールドはそのまま引き継ぐ）。UI側の呼び出しはローカル working を
        // 上書きする前提なので、呼び出し側が不変性を要求する場合は事前に CloneMode 済み。
        // =====================================================================

        /// <summary>
        /// モード保存時に未参照(orphan)となっている ActionSlot を除去し、
        /// 残ったスロットを詰めたうえで actionRules と defaultActionIndex の
        /// 参照インデックスを再マッピングする純関数。
        ///
        /// 編集中は呼ばない（index がズレると編集UIの整合が崩れるため）。
        /// 保存ボタン押下時にのみ呼び、その結果を UpdateModeInBuffer に渡す。
        ///
        /// 注: targetRules[*].actionIndex は targetSelects[] 側を指すので touch しない。
        /// </summary>
        public static AIMode GcOrphanActionSlots(AIMode mode)
        {
            int slotCount = mode.actions != null ? mode.actions.Length : 0;

            if (slotCount == 0)
            {
                // actions 配列が空なら defaultActionIndex と actionRules は意味を持たない
                mode.defaultActionIndex = 0;
                mode.actionRules = mode.actionRules ?? new AIRule[0];
                mode.actions = mode.actions ?? new ActionSlot[0];
                return mode;
            }

            bool[] used = new bool[slotCount];

            if (mode.actionRules != null)
            {
                for (int i = 0; i < mode.actionRules.Length; i++)
                {
                    int idx = mode.actionRules[i].actionIndex;
                    if (idx >= 0 && idx < slotCount)
                    {
                        used[idx] = true;
                    }
                }
            }

            int def = mode.defaultActionIndex;
            if (def >= 0 && def < slotCount)
            {
                used[def] = true;
            }

            bool anyUnused = false;
            for (int i = 0; i < slotCount; i++)
            {
                if (!used[i])
                {
                    anyUnused = true;
                    break;
                }
            }
            if (!anyUnused)
            {
                return mode;
            }

            // 残存スロットを詰める
            int[] remap = new int[slotCount];
            List<ActionSlot> compact = new List<ActionSlot>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                if (used[i])
                {
                    remap[i] = compact.Count;
                    compact.Add(mode.actions[i]);
                }
                else
                {
                    remap[i] = -1;
                }
            }

            // actionRules の actionIndex を詰め後の index にリマップ
            if (mode.actionRules != null)
            {
                for (int i = 0; i < mode.actionRules.Length; i++)
                {
                    AIRule r = mode.actionRules[i];
                    int oldIdx = r.actionIndex;
                    int newIdx = (oldIdx >= 0 && oldIdx < slotCount) ? remap[oldIdx] : -1;
                    // 原則 used[oldIdx]=true なので remap[oldIdx] >= 0。不正値は 0 に寄せて保険。
                    r.actionIndex = newIdx < 0 ? 0 : newIdx;
                    mode.actionRules[i] = r;
                }
            }

            if (def >= 0 && def < slotCount)
            {
                int newDef = remap[def];
                mode.defaultActionIndex = newDef < 0 ? 0 : newDef;
            }
            else
            {
                mode.defaultActionIndex = 0;
            }

            mode.actions = compact.ToArray();
            return mode;
        }

        /// <summary>
        /// 新しい ActionSlot を追加し、同時にそれを指す AIRule を actionRules 末尾に追加する。
        /// UI の「＋ 行動を追加」ボタンから呼ばれる。
        /// </summary>
        public static AIMode AddActionRuleWithNewSlot(AIMode mode, AICondition[] conditions, ActionSlot slot, byte probability)
        {
            int newSlotIdx = AppendSlot(ref mode, slot);

            int oldRuleCount = mode.actionRules != null ? mode.actionRules.Length : 0;
            AIRule[] newRules = new AIRule[oldRuleCount + 1];
            for (int i = 0; i < oldRuleCount; i++)
            {
                newRules[i] = mode.actionRules[i];
            }
            newRules[oldRuleCount] = new AIRule
            {
                conditions = conditions ?? new AICondition[0],
                actionIndex = newSlotIdx,
                probability = probability,
            };
            mode.actionRules = newRules;
            return mode;
        }

        /// <summary>
        /// 指定 ruleIdx のルールを「別条件で複製」する。actionIndex を共有したまま
        /// conditions を深くコピーして直後に挿入することで、「同じ行動を別条件で使う」
        /// というユースケースを実現する。
        /// </summary>
        public static AIMode DuplicateActionRule(AIMode mode, int ruleIdx)
        {
            AIRule[] rules = mode.actionRules ?? new AIRule[0];
            if (ruleIdx < 0 || ruleIdx >= rules.Length)
            {
                return mode;
            }

            AIRule orig = rules[ruleIdx];
            AIRule dup = new AIRule
            {
                actionIndex = orig.actionIndex, // ActionSlot は共有
                probability = orig.probability,
                conditions = orig.conditions != null ? (AICondition[])orig.conditions.Clone() : new AICondition[0],
            };

            AIRule[] newRules = new AIRule[rules.Length + 1];
            for (int i = 0; i <= ruleIdx; i++)
            {
                newRules[i] = rules[i];
            }
            newRules[ruleIdx + 1] = dup;
            for (int i = ruleIdx + 1; i < rules.Length; i++)
            {
                newRules[i + 1] = rules[i];
            }
            mode.actionRules = newRules;
            return mode;
        }

        /// <summary>
        /// 指定 ruleIdx のルールを削除する。ActionSlot は GC しない（保存時に
        /// <see cref="GcOrphanActionSlots"/> で一括処理する方針）。
        /// 編集中に index が動くと UI 側の再構築が煩雑になるのを避ける意図。
        /// </summary>
        public static AIMode RemoveActionRule(AIMode mode, int ruleIdx)
        {
            AIRule[] rules = mode.actionRules ?? new AIRule[0];
            if (ruleIdx < 0 || ruleIdx >= rules.Length)
            {
                return mode;
            }

            AIRule[] newRules = new AIRule[rules.Length - 1];
            int dst = 0;
            for (int i = 0; i < rules.Length; i++)
            {
                if (i == ruleIdx)
                {
                    continue;
                }
                newRules[dst] = rules[i];
                dst++;
            }
            mode.actionRules = newRules;
            return mode;
        }

        /// <summary>
        /// 指定 ruleIdx のルールが指す ActionSlot の内容を差し替える。
        ///
        /// - 他のルール/defaultActionIndex が同じスロットを共有している場合: 新スロットを
        ///   actions[] 末尾に追加し、対象ルールだけをそこに repoint する。共有先は影響なし。
        /// - 単独参照の場合: actions[既存index] をその場で書き換える。
        ///
        /// 参照整合性が崩れている（actionIndex が範囲外）場合は安全側として新スロットを追加。
        /// </summary>
        public static AIMode ReplaceActionRuleSlot(AIMode mode, int ruleIdx, ActionSlot newSlot)
        {
            AIRule[] rules = mode.actionRules ?? new AIRule[0];
            if (ruleIdx < 0 || ruleIdx >= rules.Length)
            {
                return mode;
            }

            int targetActionIdx = rules[ruleIdx].actionIndex;
            int slotCount = mode.actions != null ? mode.actions.Length : 0;

            if (targetActionIdx < 0 || targetActionIdx >= slotCount)
            {
                return AppendSlotAndRepoint(mode, ruleIdx, newSlot);
            }

            bool shared = false;
            if (mode.defaultActionIndex == targetActionIdx)
            {
                shared = true;
            }
            if (!shared)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    if (i == ruleIdx)
                    {
                        continue;
                    }
                    if (rules[i].actionIndex == targetActionIdx)
                    {
                        shared = true;
                        break;
                    }
                }
            }

            if (shared)
            {
                return AppendSlotAndRepoint(mode, ruleIdx, newSlot);
            }

            mode.actions[targetActionIdx] = newSlot;
            return mode;
        }

        /// <summary>
        /// defaultActionIndex が指す ActionSlot を差し替える。
        /// 共有時は新スロット追加、単独時はその場更新（<see cref="ReplaceActionRuleSlot"/> の
        /// default 版）。
        /// </summary>
        public static AIMode ReplaceDefaultActionSlot(AIMode mode, ActionSlot newSlot)
        {
            int slotCount = mode.actions != null ? mode.actions.Length : 0;
            int def = mode.defaultActionIndex;

            if (def < 0 || def >= slotCount)
            {
                // 不正参照なら末尾に追加して default を向け直す
                int newIdx = AppendSlot(ref mode, newSlot);
                mode.defaultActionIndex = newIdx;
                return mode;
            }

            bool shared = false;
            AIRule[] rules = mode.actionRules ?? new AIRule[0];
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].actionIndex == def)
                {
                    shared = true;
                    break;
                }
            }

            if (shared)
            {
                int newIdx = AppendSlot(ref mode, newSlot);
                mode.defaultActionIndex = newIdx;
                return mode;
            }

            mode.actions[def] = newSlot;
            return mode;
        }

        /// <summary>
        /// 新しい ActionSlot を actions[] の末尾に追加し、追加後の index を返すヘルパー。
        /// AIMode は struct なので ref 渡し。呼び出し側は返却 index を使って AIRule や
        /// defaultActionIndex を更新する。
        /// </summary>
        private static int AppendSlot(ref AIMode mode, ActionSlot slot)
        {
            int oldCount = mode.actions != null ? mode.actions.Length : 0;
            ActionSlot[] newActions = new ActionSlot[oldCount + 1];
            if (mode.actions != null)
            {
                for (int i = 0; i < oldCount; i++)
                {
                    newActions[i] = mode.actions[i];
                }
            }
            newActions[oldCount] = slot;
            mode.actions = newActions;
            return oldCount;
        }

        private static AIMode AppendSlotAndRepoint(AIMode mode, int ruleIdx, ActionSlot newSlot)
        {
            int newIdx = AppendSlot(ref mode, newSlot);
            AIRule r = mode.actionRules[ruleIdx];
            r.actionIndex = newIdx;
            mode.actionRules[ruleIdx] = r;
            return mode;
        }
    }
}
