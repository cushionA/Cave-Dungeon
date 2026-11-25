using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>指定した区画のロードと遭遇の達成後に、同じ遠征スロットを保存する。</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionAutosave3D : MonoBehaviour
    {
        [SerializeField] private ExpeditionSavePoint3D savePoint;
        [SerializeField, Tooltip("オートセーブするストリーミング先の完全シーンパス。")]
        private string[] areaPaths = Array.Empty<string>();
        [SerializeField] private EncounterSession3D[] encounters = Array.Empty<EncounterSession3D>();

        private struct PendingSave
        {
            public SaveManager manager;
            public SaveSlotData previous;
            public ActorRef actor;
            public int slot;
            public ulong revision;
            public int queuedFrame;
        }

        private readonly HashSet<EncounterSession3D> _subscribedEncounters = new HashSet<EncounterSession3D>();
        private LevelStreamingOrchestrator _streaming;
        private PendingSave _pending;

        private void OnEnable()
        {
            if (encounters != null)
            {
                foreach (EncounterSession3D encounter in encounters)
                {
                    if (encounter != null && _subscribedEncounters.Add(encounter)) { encounter.Ended += OnEncounterEnded; }
                }
            }
            BindStreaming();
        }

        private void Start() => BindStreaming();

        private void OnDisable()
        {
            if (_streaming != null) { _streaming.OnAreaLoadCompleted -= OnAreaLoaded; }
            _streaming = null;
            foreach (EncounterSession3D encounter in _subscribedEncounters)
            {
                if (encounter != null) { encounter.Ended -= OnEncounterEnded; }
            }
            _subscribedEncounters.Clear();
            _pending = default;
        }

        private void LateUpdate()
        {
            PendingSave pending = _pending;
            if (pending.manager == null || pending.queuedFrame == Time.frameCount) { return; }

            _pending = default;
            if (savePoint == null || pending.manager != GameManager.Saves
                || pending.manager.ActiveSlotIndex != pending.slot || pending.manager.LoadRevision != pending.revision
                || !ReferenceEquals(pending.manager.GetSlotData(pending.slot), pending.previous))
            {
                return;
            }
            savePoint.TryAutosaveCurrentSlot(pending.actor);
        }

        private void BindStreaming()
        {
            LevelStreamingOrchestrator streaming = areaPaths?.Length > 0 ? GameManager.LevelStreaming?.Orchestrator : null;
            if (_streaming == streaming) { return; }
            if (_streaming != null) { _streaming.OnAreaLoadCompleted -= OnAreaLoaded; }
            _streaming = streaming;
            if (_streaming != null) { _streaming.OnAreaLoadCompleted += OnAreaLoaded; }
        }

        private void OnAreaLoaded(string path)
        {
            if (areaPaths != null && Array.IndexOf(areaPaths, path) >= 0) { QueueSave(); }
        }

        private void OnEncounterEnded(ChallengeState state)
        {
            if (state == ChallengeState.Completed) { QueueSave(); }
        }

        private void QueueSave()
        {
            SaveManager saves = GameManager.Saves;
            if (!isActiveAndEnabled || saves == null || saves.IsApplyingLoad || savePoint == null
                || !savePoint.TryGetKnight(out ActorRef actor)) { return; }

            // 同じフレームの通知をまとめ、撃破と到達の報酬が反映された後に保存する。
            int slot = saves.ActiveSlotIndex;
            _pending = new PendingSave
            {
                manager = saves,
                previous = saves.GetSlotData(slot),
                actor = actor,
                slot = slot,
                revision = saves.LoadRevision,
                queuedFrame = Time.frameCount,
            };
        }
    }
}
