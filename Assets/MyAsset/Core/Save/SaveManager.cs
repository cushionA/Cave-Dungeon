using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>保存先に依存するデータを、現在の実行状態と区別して取得する。</summary>
    internal interface ISaveableForDestinationSlot : ISaveable
    {
        object SerializeForSlot(int slotIndex);
    }

    /// <summary>通常の保存形式では保持できない実行中の識別子も、ロード失敗時に戻す。</summary>
    internal interface ISaveableWithLoadRollback : ISaveable
    {
        object CaptureLoadRollback();
        void RestoreLoadRollback(object snapshot);
    }

    /// <summary>
    /// セーブデータ管理（スロット制）。
    /// ISaveableを登録し、スロット単位でシリアライズ/デシリアライズを行う。
    /// ディスク永続化は SaveDataStore に分離。保存済み状態の公開と通知は SaveManager が行う。
    /// IGameSubManagerとしてGameManagerCoreに登録可能。
    /// </summary>
    public class SaveManager : IGameSubManager
    {
        public const int k_MaxSlots = 3;
        private const int k_InitOrder = 900;

        private enum Operation { None, Capturing, ApplyingLoad, NotifyingLoaded }

        private readonly struct LoadParticipant
        {
            public readonly ISaveable Saveable;
            public readonly object Original;
            public readonly object Candidate;
            public readonly bool RestoreDefault;

            public LoadParticipant(ISaveable saveable, object original, object candidate, bool restoreDefault)
            {
                Saveable = saveable;
                Original = original;
                Candidate = candidate;
                RestoreDefault = restoreDefault;
            }
        }

        private readonly SaveSlotData[] _slots;
        private readonly List<ISaveable> _saveables;
        private readonly HashSet<string> _registeredIds;
        private int _activeSlotIndex;
        private Operation _operation;

        public int ActiveSlotIndex => _activeSlotIndex;
        public int InitOrder => k_InitOrder;

        /// <summary>読み込み前の状態を取得してから、候補の確定またはロールバックが終わるまで有効。</summary>
        public bool IsApplyingLoad => _operation == Operation.ApplyingLoad;

        /// <summary>最後に復元されたデータ。保存・スロット選択・ディスク読み込みだけでは変わらない。</summary>
        public SaveSlotData LastLoadedSlot { get; private set; }
        public ulong LoadRevision { get; private set; }

        /// <summary>登録済みのデータをすべて復元し、アクティブスロットを確定した後に通知する。</summary>
        public event Action<int> Loaded;

        public SaveManager()
        {
            _slots = new SaveSlotData[k_MaxSlots];
            _saveables = new List<ISaveable>();
            _registeredIds = new HashSet<string>();
            _activeSlotIndex = 0;
        }

        /// <summary>ISaveableを登録。SaveIdの重複は例外。同一インスタンスの再登録は無視。</summary>
        public void Register(ISaveable saveable)
        {
            if (saveable == null)
            {
                return;
            }

            // 同一インスタンスの再登録は無視
            if (_saveables.Contains(saveable))
            {
                return;
            }

            if (_operation == Operation.Capturing || IsApplyingLoad)
            {
                throw new InvalidOperationException("Cannot register a saveable while capturing or applying a slot.");
            }

            // SaveIdの重複チェック
            if (_registeredIds.Contains(saveable.SaveId))
            {
                throw new InvalidOperationException(
                    $"SaveId '{saveable.SaveId}' is already registered. Each ISaveable must have a unique SaveId.");
            }

            _saveables.Add(saveable);
            _registeredIds.Add(saveable.SaveId);
        }

        /// <summary>ISaveableを解除。シーン遷移時などに使用。</summary>
        public void Unregister(ISaveable saveable)
        {
            if (saveable == null)
            {
                return;
            }

            if (_saveables.Remove(saveable))
            {
                _registeredIds.Remove(saveable.SaveId);
            }
        }

        /// <summary>全ISaveableをシリアライズしてスロットに保存（インメモリのみ）。
        /// ディスク永続化が必要な呼び出し側は、返却された SaveSlotData を SaveDataStore に渡す。</summary>
        public SaveSlotData Save(int slotIndex)
        {
            SaveSlotData slotData = CaptureSlot(slotIndex);
            if (slotData != null) { _slots[slotIndex] = slotData; }
            return slotData;
        }

        /// <summary>スロットを更新せずに状態を取得する。次周用では周回ごとの項目を除く。</summary>
        internal SaveSlotData CaptureSlot(int slotIndex, bool forNextPlaythrough = false)
        {
            if (_operation != Operation.None || !IsValidSlot(slotIndex)) { return null; }

            _operation = Operation.Capturing;
            try
            {
                SaveSlotData slotData = new SaveSlotData(slotIndex);
                foreach (ISaveable saveable in _saveables.ToArray())
                {
                    if (forNextPlaythrough && saveable is IResetOnNextPlaythrough) { continue; }
                    slotData.entries[saveable.SaveId] = saveable is ISaveableForDestinationSlot destination
                        ? destination.SerializeForSlot(slotIndex) : saveable.Serialize();
                }
                return slotData;
            }
            finally { _operation = Operation.None; }
        }

        /// <summary>スロットからデータをロードして全ISaveableに復元（インメモリのみ）。
        /// ディスクからのロードは SaveDataStore.ReadFromDisk + SetSlotData 経由で行う。</summary>
        public bool Load(int slotIndex) => IsValidSlot(slotIndex) && TryLoadSlot(slotIndex, _slots[slotIndex]);

        /// <summary>候補を復元し、指定した確定処理が成功してからスロット情報を公開する。</summary>
        internal bool TryLoadSlot(int slotIndex, SaveSlotData slotData, Func<bool> commit = null)
        {
            if (_operation != Operation.None || !IsValidSlot(slotIndex) || slotData?.entries == null) { return false; }

            _operation = Operation.ApplyingLoad;
            try
            {
                if (!TryApplySlot(slotIndex, slotData, commit)) { return false; }

                _slots[slotIndex] = slotData;
                _activeSlotIndex = slotIndex;
                LastLoadedSlot = slotData;
                LoadRevision++;

                _operation = Operation.NotifyingLoaded;
                NotifyLoaded(slotIndex);
                return true;
            }
            finally { _operation = Operation.None; }
        }

        private bool TryApplySlot(int slotIndex, SaveSlotData slotData, Func<bool> commit)
        {
            List<LoadParticipant> participants = new List<LoadParticipant>(_saveables.Count);
            try
            {
                foreach (ISaveable saveable in _saveables.ToArray())
                {
                    bool hasEntry = slotData.entries.TryGetValue(saveable.SaveId, out object candidate);
                    if (!hasEntry && saveable is not ISaveableWithMissingEntry) { continue; }
                    object original = saveable is ISaveableWithLoadRollback live
                        ? live.CaptureLoadRollback() : saveable.Serialize();
                    participants.Add(new LoadParticipant(saveable, original, candidate, !hasEntry));
                }
            }
            catch (Exception exception)
            {
                LogFailure("読み込み前の状態の取得に失敗しました", slotIndex, exception);
                return false;
            }

            int attempted = 0;
            bool accepted = false;
            string failure = "スロットの復元に失敗しました";
            try
            {
                while (attempted < participants.Count)
                {
                    // 途中で例外を投げる参加者にも、読み込み前の値を戻す。
                    LoadParticipant participant = participants[attempted++];
                    if (participant.RestoreDefault)
                    {
                        ((ISaveableWithMissingEntry)participant.Saveable).RestoreMissingEntry();
                    }
                    else { participant.Saveable.Deserialize(participant.Candidate); }
                }

                failure = "スロットの確定に失敗しました";
                accepted = commit == null || commit();
                return accepted;
            }
            catch (Exception exception)
            {
                LogFailure(failure, slotIndex, exception);
                return false;
            }
            finally
            {
                if (!accepted) { RestoreOriginals(slotIndex, participants, attempted); }
            }
        }

        private static void RestoreOriginals(int slotIndex, List<LoadParticipant> participants, int attempted)
        {
            // 復元でも依存先から先に戻す。例: 所持品から装備、解放状態から編成。
            for (int i = 0; i < attempted; i++)
            {
                LoadParticipant participant = participants[i];
                try
                {
                    if (participant.Saveable is ISaveableWithLoadRollback live) { live.RestoreLoadRollback(participant.Original); }
                    else { participant.Saveable.Deserialize(participant.Original); }
                }
                catch (Exception exception)
                {
                    LogFailure("読み込み前の状態に戻せませんでした", slotIndex, exception);
                }
            }
        }

        private void NotifyLoaded(int slotIndex)
        {
            Action<int> loaded = Loaded;
            if (loaded == null) { return; }
            foreach (Action<int> observer in loaded.GetInvocationList())
            {
                try { observer(slotIndex); }
                catch (Exception exception) { LogFailure("ロード完了通知に失敗しました", slotIndex, exception); }
            }
        }

        private static void LogFailure(string message, int slotIndex, Exception exception)
            => Debug.LogError($"[SaveManager] {message} (slot={slotIndex}): {exception.Message}");

        private static bool IsValidSlot(int slotIndex) => (uint)slotIndex < k_MaxSlots;

        /// <summary>スロットにデータが存在するか（メモリのみ。ディスクの存在確認は SaveDataStore に委譲）</summary>
        public bool HasSaveData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            return _slots[slotIndex] != null;
        }

        /// <summary>アクティブスロット切替</summary>
        public void SetActiveSlot(int slotIndex)
        {
            if (_operation != Operation.None || !IsValidSlot(slotIndex))
            {
                return;
            }

            _activeSlotIndex = slotIndex;
        }

        /// <summary>スロットデータ取得（表示用）</summary>
        public SaveSlotData GetSlotData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return null;
            }

            return _slots[slotIndex];
        }

        /// <summary>スロットデータを外部から設定（SaveDataStoreからのロード時に使用）</summary>
        public void SetSlotData(int slotIndex, SaveSlotData data)
        {
            if (_operation != Operation.None || !IsValidSlot(slotIndex))
            {
                return;
            }

            _slots[slotIndex] = data;
        }

        /// <summary>セーブデータをメモリから削除する。ディスクの削除は SaveDataStore に委譲する。</summary>
        public bool DeleteSaveData(int slotIndex)
        {
            if (_operation != Operation.None || !IsValidSlot(slotIndex))
            {
                return false;
            }

            _slots[slotIndex] = null;
            return true;
        }

        // ===== IGameSubManager =====

        public void Initialize(SoACharaDataDic data, GameEvents events)
        {
            // SaveManagerはSoAデータに依存しないが、
            // GameEventsのセーブ関連イベントを購読する場所として使用可能
        }

        public void Dispose()
        {
            if (_operation != Operation.None) { throw new InvalidOperationException("Cannot dispose during a save or load."); }
            Loaded = null;
            LastLoadedSlot = null;
            LoadRevision = 0;
            _saveables.Clear();
            _registeredIds.Clear();
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = null;
            }
        }
    }
}
