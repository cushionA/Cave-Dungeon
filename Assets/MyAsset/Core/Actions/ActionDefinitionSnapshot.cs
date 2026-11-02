using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>登録時に配列を捕捉し、実行・通知の全読取先へ同じ不変定義を渡す。</summary>
    public readonly struct ActionDefinitionSnapshot
    {
        private sealed class CapturedData
        {
            public readonly ActionDefinitionData data;
            public readonly ReadOnlyCollection<ActionEffect> effects;
            public readonly ReadOnlyCollection<ActionTransition> transitions;

            public CapturedData(in ActionDefinitionData source)
            {
                ActionDefinitionData captured = source;
                captured.effects = Copy(source.effects);
                captured.transitions = Copy(source.transitions);
                data = captured;
                effects = Array.AsReadOnly(captured.effects);
                transitions = Array.AsReadOnly(captured.transitions);
            }
        }

        private static readonly ActionDefinitionData DefaultData = default;
        // 定義の捕捉時だけ確保し、実行snapshotや通知はこの参照1本を共有する。
        private readonly CapturedData _captured;

        private ref readonly ActionDefinitionData Data
        {
            get
            {
                if (_captured == null) { return ref DefaultData; }
                return ref _captured.data;
            }
        }

        public int id => Data.id;
        public bool motionOnly => Data.motionOnly;
        public ActionResources resources => Data.resources;
        public int priority => Data.priority;
        public float staminaCost => Data.staminaCost;
        public int mpCost => Data.mpCost;
        public float staminaPerSecond => Data.staminaPerSecond;
        public float anticipationDuration => Data.anticipationDuration;
        public float activeDuration => Data.activeDuration;
        public float recoveryDuration => Data.recoveryDuration;
        public float cancelAfter => Data.cancelAfter;
        public float cancelPoint => Data.cancelPoint;
        public ActionEndMode endMode => Data.endMode;
        public ActionDurationMode durationMode => Data.durationMode;
        public float duration => Data.duration;
        public ActState actState => Data.actState;
        public ActionGameplayFlags gameplay => Data.gameplay;
        public IReadOnlyList<ActionEffect> effects => _captured != null ? _captured.effects : Array.Empty<ActionEffect>();
        public IReadOnlyList<ActionTransition> transitions => _captured != null ? _captured.transitions : Array.Empty<ActionTransition>();
        public float moveSpeed => Data.moveSpeed;
        public float moveStartTime => Data.moveStartTime;
        public float moveDuration => Data.moveDuration;
        public bool movementUsesInput => Data.movementUsesInput;

        public ActionDefinitionSnapshot(in ActionDefinitionData data)
        {
            _captured = new CapturedData(data);
        }

        internal bool Matches(in ActionDefinitionData source)
        {
            ref readonly ActionDefinitionData captured = ref Data;
            return captured.id == source.id
                && captured.motionOnly == source.motionOnly
                && captured.resources == source.resources
                && captured.priority == source.priority
                && captured.staminaCost == source.staminaCost
                && captured.mpCost == source.mpCost
                && captured.staminaPerSecond == source.staminaPerSecond
                && captured.anticipationDuration == source.anticipationDuration
                && captured.activeDuration == source.activeDuration
                && captured.recoveryDuration == source.recoveryDuration
                && captured.cancelAfter == source.cancelAfter
                && captured.cancelPoint == source.cancelPoint
                && captured.endMode == source.endMode
                && captured.durationMode == source.durationMode
                && captured.duration == source.duration
                && captured.actState == source.actState
                && captured.gameplay == source.gameplay
                && captured.moveSpeed == source.moveSpeed
                && captured.moveStartTime == source.moveStartTime
                && captured.moveDuration == source.moveDuration
                && captured.movementUsesInput == source.movementUsesInput
                && SameEffects(captured.effects, source.effects)
                && SameTransitions(captured.transitions, source.transitions);
        }

        /// <summary>派生定義の登録用コピー。返した配列の変更は元の実行定義へ伝わらない。</summary>
        public ActionDefinitionData ToData()
        {
            ActionDefinitionData copy = Data;
            copy.effects = Copy(copy.effects);
            copy.transitions = Copy(copy.transitions);
            return copy;
        }

        public static implicit operator ActionDefinitionSnapshot(ActionDefinitionData data) => new ActionDefinitionSnapshot(data);

        private static bool SameEffects(ActionEffect[] first, ActionEffect[] second)
        {
            int count = first != null ? first.Length : 0;
            if (count != (second != null ? second.Length : 0)) { return false; }
            // 入力配列は前回と同じ参照でも内容を変更できるため、要素を比較する。
            for (int i = 0; i < count; i++)
            {
                ref readonly ActionEffect left = ref first[i];
                ref readonly ActionEffect right = ref second[i];
                if (left.type != right.type || left.startTime != right.startTime
                    || left.duration != right.duration || left.value != right.value) { return false; }
            }
            return true;
        }

        private static bool SameTransitions(ActionTransition[] first, ActionTransition[] second)
        {
            int count = first != null ? first.Length : 0;
            if (count != (second != null ? second.Length : 0)) { return false; }
            for (int i = 0; i < count; i++)
            {
                ref readonly ActionTransition left = ref first[i];
                ref readonly ActionTransition right = ref second[i];
                if (left.trigger != right.trigger || left.nextDefinitionId != right.nextDefinitionId
                    || left.signalId != right.signalId || left.requiresHit != right.requiresHit
                    || left.minElapsed != right.minElapsed || left.onCompletion != right.onCompletion
                    || left.detached != right.detached) { return false; }
            }
            return true;
        }

        private static T[] Copy<T>(T[] source) => source != null && source.Length > 0
            ? (T[])source.Clone() : Array.Empty<T>();
    }
}
