using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>登録時に配列を捕捉し、実行・通知の全読取先へ同じ不変定義を渡す。</summary>
    public readonly struct ActionDefinitionSnapshot
    {
        private readonly ActionDefinitionData _data;
        private readonly ReadOnlyCollection<ActionEffect> _effects;
        private readonly ReadOnlyCollection<ActionTransition> _transitions;

        public int id => _data.id;
        public bool motionOnly => _data.motionOnly;
        public ActionResources resources => _data.resources;
        public int priority => _data.priority;
        public float staminaCost => _data.staminaCost;
        public int mpCost => _data.mpCost;
        public float staminaPerSecond => _data.staminaPerSecond;
        public float anticipationDuration => _data.anticipationDuration;
        public float activeDuration => _data.activeDuration;
        public float recoveryDuration => _data.recoveryDuration;
        public float cancelAfter => _data.cancelAfter;
        public float cancelPoint => _data.cancelPoint;
        public ActionEndMode endMode => _data.endMode;
        public ActionDurationMode durationMode => _data.durationMode;
        public float duration => _data.duration;
        public ActState actState => _data.actState;
        public ActionGameplayFlags gameplay => _data.gameplay;
        public IReadOnlyList<ActionEffect> effects => _effects != null ? _effects : Array.Empty<ActionEffect>();
        public IReadOnlyList<ActionTransition> transitions => _transitions != null ? _transitions : Array.Empty<ActionTransition>();
        public float moveSpeed => _data.moveSpeed;
        public float moveStartTime => _data.moveStartTime;
        public float moveDuration => _data.moveDuration;
        public bool movementUsesInput => _data.movementUsesInput;

        public ActionDefinitionSnapshot(in ActionDefinitionData data)
        {
            ActionDefinitionData captured = data;
            captured.effects = Copy(data.effects);
            captured.transitions = Copy(data.transitions);
            _data = captured;
            _effects = Array.AsReadOnly(captured.effects);
            _transitions = Array.AsReadOnly(captured.transitions);
        }

        /// <summary>派生定義の登録用コピー。返した配列の変更は元の実行定義へ伝わらない。</summary>
        public ActionDefinitionData ToData()
        {
            ActionDefinitionData copy = _data;
            copy.effects = Copy(_data.effects);
            copy.transitions = Copy(_data.transitions);
            return copy;
        }

        public static implicit operator ActionDefinitionSnapshot(ActionDefinitionData data) => new ActionDefinitionSnapshot(data);

        private static T[] Copy<T>(T[] source) => source != null && source.Length > 0
            ? (T[])source.Clone() : Array.Empty<T>();
    }
}
