using System;
using System.Collections;
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

        private sealed class PendingSave
        {
            public readonly GameManager World;
            public readonly ExpeditionSavePoint3D Point;
            public readonly ActorRef Actor;

            private readonly ExpeditionCheckpoint.AutosaveTicket _ticket;

            public PendingSave(GameManager world, SaveManager saves, ExpeditionSavePoint3D point, ActorRef actor)
            {
                World = world;
                Point = point;
                Actor = actor;
                _ticket = new ExpeditionCheckpoint.AutosaveTicket(saves, point.Store);
            }

            public bool IsCurrent() => World != null && World == GameManager.Instance && Point != null
                && _ticket.IsCurrent(GameManager.Saves, Point.Store)
                && Point.TryGetKnight(out ActorRef actor) && actor == Actor;

            public void Save()
            {
                if (IsCurrent()) { Point.TryAutosaveCurrentSlot(Actor, _ticket, IsCurrent); }
            }
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
        }

        /// <summary>遠征シーンのアンロードを始める前に、受理済みの保存処理が終わるまで待つ。</summary>
        public IEnumerator WaitForPendingSave()
        {
            while (_pending != null && _pending.World != null && _pending.World == GameManager.Instance)
            {
                yield return null;
            }
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
            GameManager world = GameManager.Instance;
            SaveManager saves = GameManager.Saves;
            if (!isActiveAndEnabled || world == null || saves == null || saves.IsApplyingLoad || savePoint == null
                || !savePoint.TryGetKnight(out ActorRef actor)) { return; }

            if (_pending != null && _pending.Point == savePoint && _pending.Actor == actor && _pending.IsCurrent()) { return; }
            // 撃破・到達の同じフレームの通知を処理し終えるまで待つ。装置の無効化だけでは予約を取り消さない。
            PendingSave pending = new PendingSave(world, saves, savePoint, actor);
            _pending = pending;
            world.StartCoroutine(SaveOnNextFrame(pending));
        }

        private IEnumerator SaveOnNextFrame(PendingSave pending)
        {
            yield return null;
            if (!ReferenceEquals(_pending, pending)) { yield break; }
            _pending = null;
            pending.Save();
        }
    }
}
