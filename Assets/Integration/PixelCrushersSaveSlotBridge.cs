using System;
using System.Collections;
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

        private SaveManager _saves;
        private SaveManager _observedSaveManager;
        private ulong _observedLoadRevision;
        private Coroutine _initialization;
        private bool _started;
        private static int _restoreDepth;
        private static ulong _journalLoadRevision;

        public string SaveId => k_SaveId;
        internal static bool IsRestoring => _restoreDepth != 0;

        private void OnEnable()
        {
            UseGameSlots(questJournal);
            UseGameSlots(dialogueSystemSaver);
            if (_started) { Register(); }
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
            if (saves == null || _saves == saves || (questJournal == null && dialogueSystemSaver == null)) { return; }

            saves.Register(this);
            _saves = saves;
            saves.Loaded += ObserveLoad;
            if (_observedSaveManager != saves || _observedLoadRevision != saves.LoadRevision)
            {
                ObserveLoad(saves.ActiveSlotIndex);
                if (saves.LastLoadedSlot != null)
                {
                    saves.LastLoadedSlot.entries.TryGetValue(SaveId, out object data);
                    Restore(ReadSnapshot(data));
                }
            }
        }

        private void ObserveLoad(int _)
        {
            if (_saves == null) { return; }
            _observedSaveManager = _saves;
            _observedLoadRevision = _saves.LoadRevision;
        }

        private void OnDisable()
        {
            if (_initialization != null)
            {
                StopCoroutine(_initialization);
                _initialization = null;
            }
            if (_saves == null) { return; }
            _saves.Loaded -= ObserveLoad;
            _saves.Unregister(this);
            _saves = null;
        }

        private static void UseGameSlots(Saver saver)
        {
            if (saver == null) { return; }
            saver.restoreStateOnStart = false;
            SaveSystem.UnregisterSaver(saver);
        }

        object ISaveable.Serialize() => JsonUtility.ToJson(new Snapshot
        {
            version = k_Version,
            questJournal = questJournal != null ? questJournal.RecordData() : null,
            dialogue = dialogueSystemSaver != null ? dialogueSystemSaver.RecordData() : null,
        });

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

        private void Restore(Snapshot snapshot)
        {
            _restoreDepth++;
            try
            {
                // クエストの条件や UI が復元中に参照する Lua 状態を先に戻す。
                if (dialogueSystemSaver != null)
                {
                    if (string.IsNullOrEmpty(snapshot?.dialogue)) { dialogueSystemSaver.OnRestartGame(); }
                    else
                    {
                        DialogueManager.StopAllConversations();
                        dialogueSystemSaver.ApplyData(snapshot.dialogue);
                    }
                }
                if (questJournal == null) { return; }
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
