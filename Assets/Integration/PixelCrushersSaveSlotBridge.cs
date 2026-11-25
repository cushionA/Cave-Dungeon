using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Game.Core;
using PixelCrushers;
using PixelCrushers.DialogueSystem;
using PixelCrushers.QuestMachine;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>Quest Machine と Dialogue System のスナップショットをゲーム本体のスロットへ保存する。</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PixelCrushersSaveSlotBridge : MonoBehaviour, IResetOnNextPlaythrough
    {
        public const string k_SaveId = "PixelCrushers.Story";
        private const int k_Version = 1;

        [SerializeField, Tooltip("保存する Quest Journal。Include In Saved Game Data を有効にする。")]
        private QuestJournal questJournal;

        [SerializeField, Tooltip("会話の Lua 変数なども同じスロットに保存するときに指定する。")]
        private DialogueSystemSaver dialogueSystemSaver;

        [Serializable]
        private sealed class Snapshot
        {
            public int version;
            public string questJournal;
            public string dialogue;
        }

        private sealed class StoryHandoff
        {
            public readonly Snapshot snapshot = new Snapshot();
            public ulong revision;
            public int slot;
            public WeakReference<QuestJournal> journal;
            public WeakReference<DialogueSystemSaver> dialogue;
        }

        private SaveManager _saves;
        private SaveManager _observedSaveManager;
        private ulong _observedLoadRevision;
        private int _observedSlot;
        private Coroutine _initialization;
        private bool _started;
        private static int _restoreDepth;
        private static ulong _journalLoadRevision;
        private static ConditionalWeakTable<SaveManager, StoryHandoff> _handoffs = new();

        public string SaveId => k_SaveId;
        internal static bool IsRestoring => _restoreDepth != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _restoreDepth = 0;
            _journalLoadRevision = 0;
            _handoffs = new ConditionalWeakTable<SaveManager, StoryHandoff>();
        }

        private void OnEnable()
        {
            UseGameSlots(questJournal);
            UseGameSlots(dialogueSystemSaver);
            if (_started && GameManager.Saves?.IsApplyingLoad != true) { Register(); }
            else { _initialization = StartCoroutine(RegisterAfterJournalStartup()); }
        }

        private IEnumerator RegisterAfterJournalStartup()
        {
            // QuestListContainer.Start はリストを作り直し、その初期化をさらにフレーム末尾へ送る。
            yield return null;
            _initialization = null;
            _started = true;
            Register();
        }

        private void Register()
        {
            SaveManager saves = GameManager.Saves;
            if (saves == null || _saves == saves || saves.IsApplyingLoad
                || (questJournal == null && dialogueSystemSaver == null)) { return; }

            saves.Register(this);
            _saves = saves;
            saves.Loaded += ObserveLoad;
            bool restoreLoadedSlot = !ObservesStory(saves) && saves.LastLoadedSlot != null;
            ObserveLoad(saves.ActiveSlotIndex);
            if (_handoffs.TryGetValue(saves, out StoryHandoff handoff) && MatchesStory(handoff, saves))
            {
                bool restoreJournal = NeedsRestore(questJournal, handoff.journal, restoreLoadedSlot);
                bool restoreDialogue = NeedsRestore(dialogueSystemSaver, handoff.dialogue, restoreLoadedSlot);
                if (restoreJournal || restoreDialogue)
                {
                    Snapshot loaded = restoreLoadedSlot ? ReadLoadedSnapshot(saves) : null;
                    Restore(new Snapshot
                    {
                        questJournal = handoff.journal != null ? handoff.snapshot.questJournal : loaded?.questJournal,
                        dialogue = handoff.dialogue != null ? handoff.snapshot.dialogue : loaded?.dialogue,
                    }, restoreJournal, restoreDialogue);
                }
            }
            else if (restoreLoadedSlot) { Restore(ReadLoadedSnapshot(saves)); }
        }

        private static bool NeedsRestore<T>(T target, WeakReference<T> previous, bool restoreLoadedSlot) where T : UnityEngine.Object
        {
            return target != null && (previous == null ? restoreLoadedSlot
                : !previous.TryGetTarget(out T prior) || !ReferenceEquals(target, prior));
        }

        private static Snapshot ReadLoadedSnapshot(SaveManager saves)
        {
            saves.LastLoadedSlot.entries.TryGetValue(k_SaveId, out object data);
            return ReadSnapshot(data);
        }

        private void ObserveLoad(int _)
        {
            if (_saves == null) { return; }
            _observedSaveManager = _saves;
            _observedLoadRevision = _saves.LoadRevision;
            _observedSlot = StorySlot(_saves);
        }

        // 保存先だけの変更は、現在再生しているストーリーの読込とは異なる。
        private static int StorySlot(SaveManager saves) => saves.LastLoadedSlot?.slotIndex ?? saves.ActiveSlotIndex;

        private bool ObservesStory(SaveManager saves) => _observedSaveManager == saves
            && _observedLoadRevision == saves.LoadRevision && _observedSlot == StorySlot(saves);

        private static bool MatchesStory(StoryHandoff handoff, SaveManager saves) => handoff.revision == saves.LoadRevision
            && handoff.slot == StorySlot(saves);

        private void OnDisable()
        {
            if (_initialization != null)
            {
                StopCoroutine(_initialization);
                _initialization = null;
            }
            if (_saves == null) { return; }
            SaveManager saves = _saves;
            try { RememberLiveStory(saves); }
            finally
            {
                saves.Loaded -= ObserveLoad;
                saves.Unregister(this);
                _saves = null;
            }
        }

        private void RememberLiveStory(SaveManager saves)
        {
            if (GameManager.Saves != saves || IsRestoring || saves.IsApplyingLoad || !ObservesStory(saves)) { return; }
            Snapshot live = CaptureSnapshot();
            if (!_handoffs.TryGetValue(saves, out StoryHandoff handoff) || !MatchesStory(handoff, saves))
            {
                _handoffs.Remove(saves);
                handoff = new StoryHandoff { revision = saves.LoadRevision, slot = StorySlot(saves) };
                _handoffs.Add(saves, handoff);
            }
            if (questJournal != null)
            {
                handoff.snapshot.questJournal = live.questJournal;
                handoff.journal = new WeakReference<QuestJournal>(questJournal);
            }
            if (dialogueSystemSaver != null)
            {
                handoff.snapshot.dialogue = live.dialogue;
                handoff.dialogue = new WeakReference<DialogueSystemSaver>(dialogueSystemSaver);
            }
        }

        private static void UseGameSlots(Saver saver)
        {
            if (saver == null) { return; }
            saver.restoreStateOnStart = false;
            SaveSystem.UnregisterSaver(saver);
        }

        object ISaveable.Serialize() => JsonUtility.ToJson(CaptureSnapshot());

        private Snapshot CaptureSnapshot() => new Snapshot
        {
            version = k_Version,
            questJournal = questJournal != null ? questJournal.RecordData() : null,
            dialogue = dialogueSystemSaver != null ? dialogueSystemSaver.RecordData() : null,
        };

        void ISaveable.Deserialize(object data) => Restore(ReadSnapshot(data));

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Restore(null);

        private static Snapshot ReadSnapshot(object data)
        {
            if (data is not string text || string.IsNullOrWhiteSpace(text)) { return null; }
            try
            {
                Snapshot snapshot = JsonUtility.FromJson<Snapshot>(text);
                return snapshot?.version == k_Version ? snapshot : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void Restore(Snapshot snapshot, bool restoreJournal = true, bool restoreDialogue = true)
        {
            _restoreDepth++;
            try
            {
                // クエストの条件や UI が復元中に参照する Lua 状態を先に戻す。
                if (restoreDialogue && dialogueSystemSaver != null)
                {
                    if (string.IsNullOrEmpty(snapshot?.dialogue)) { dialogueSystemSaver.OnRestartGame(); }
                    else
                    {
                        DialogueManager.StopAllConversations();
                        dialogueSystemSaver.ApplyData(snapshot.dialogue);
                    }
                }
                if (!restoreJournal || questJournal == null) { return; }
                if (string.IsNullOrEmpty(snapshot?.questJournal))
                {
                    // ベンダーはフレーム末尾に実行する初期化にもこの状態を引き継ぐ。
                    bool wasLoading = QuestMachine.isLoadingGame;
                    QuestMachine.isLoadingGame = true;
                    try
                    {
                        questJournal.ResetToOriginalState();
                        questJournal.RepaintUIs();
                    }
                    finally { QuestMachine.isLoadingGame = wasLoading; }
                }
                else { ApplyJournal(snapshot.questJournal); }
            }
            finally { _restoreDepth--; }
        }

        private void ApplyJournal(string data)
        {
            QuestMachineConfiguration configuration = QuestMachineConfiguration.instance;
            if (configuration == null || !configuration.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("Quest Journal の復元には有効な Quest Machine Configuration が必要です。", this);
                return;
            }

            ulong revision = ++_journalLoadRevision;
            try { questJournal.ApplyData(data); }
            finally
            {
                GameManager manager = GameManager.Instance;
                if (manager != null && manager.gameObject.activeInHierarchy)
                {
                    manager.StartCoroutine(ReleaseInterruptedJournalLoad(configuration, revision));
                }
            }
        }

        private static IEnumerator ReleaseInterruptedJournalLoad(QuestMachineConfiguration configuration, ulong revision)
        {
            // ベンダーの解除処理は2フレーム後。構成とそのコルーチンが先に消えた場合だけ補う。
            yield return null;
            yield return null;
            yield return null;
            if (_journalLoadRevision != revision || (configuration != null && configuration.gameObject.activeInHierarchy))
            {
                yield break;
            }
            QuestMachine.isLoadingGame = false;
            MessageSystem.allowReceiveSameFrameAdded = true;
        }
    }
}
