using System;
using UnityEngine;

namespace Game.Core
{
    [Flags]
    public enum ActionResources : byte
    {
        None = 0,
        HorizontalMovement = 1,
        VerticalMovement = 2,
        Hands = 4,
        Facing = 8,
        UpperPose = 16,
        LowerPose = 32,
        All = HorizontalMovement | VerticalMovement | Hands | Facing | UpperPose | LowerPose,
    }

    public enum ActionEndMode : byte
    {
        Timed,
        Held,
        Manual,
    }

    public enum ActionDurationMode : byte
    {
        Motion,
        Fixed,
    }

    public enum ActionSignalKind : byte
    {
        Started,
        PhaseChanged,
        HitConfirmed,
        Completed,
        Interrupted,
        Released,
        Custom,
    }

    [Flags]
    public enum ActionGameplayFlags : byte
    {
        None = 0,
        Move = 1,
        Guard = 2,
        Attack = 4,
        Cast = 8,
        PreserveVelocity = 16,
    }

    /// <summary>検証・登録前の編集可能な定義。実行時は不変のActionDefinitionSnapshotへ捕捉する。</summary>
    [Serializable]
    public struct ActionDefinitionData
    {
        public int id;
        public bool motionOnly;
        public ActionResources resources;
        public int priority;
        public float staminaCost;
        public int mpCost;
        public float staminaPerSecond;
        public float anticipationDuration;
        public float activeDuration;
        public float recoveryDuration;
        public float cancelAfter;
        public float cancelPoint;
        public ActionEndMode endMode;
        public ActionDurationMode durationMode;
        public float duration;
        public ActState actState;
        public ActionGameplayFlags gameplay;
        public ActionEffect[] effects;
        public ActionTransition[] transitions;
        public float moveSpeed;
        public float moveStartTime;
        public float moveDuration;
        public bool movementUsesInput;
    }

    [Serializable]
    public struct ActionTransition
    {
        public ActionSignalKind trigger;
        public int nextDefinitionId;
        public int signalId;
        public bool requiresHit;
        public float minElapsed;
        public bool onCompletion;
        /// <summary>独立した行動グループを開始する。省略時は元の行動全体に寿命を結び付ける。</summary>
        public bool detached;
    }

    public struct ActionRequest
    {
        public int ownerHash;
        public ActorRef ownerRef;
        public int definitionId;
        public int targetHash;
        public ActorRef targetRef;
        public Vector2 direction;
        public Vector2 targetPosition;
        public float speedMultiplier;
        public bool forced;
        public long sourceInstanceId;
        public long groupId;
        /// <summary>新しいrootを受理できる場合だけ、指定した既存グループ全体を置換する。</summary>
        public long replaceGroupId;
        public bool detached;
        public float chargeMultiplier;
    }

    public struct ActionActorState
    {
        public CharacterVitals vitals;
        public CharacterFlags flags;
        public CharacterStatusEffects statusEffects;
    }

    public interface IExecutionWorld
    {
        bool TryGetActor(int hash, out ActionActorState actor);
        void CommitActor(int hash, in ActionActorState actor);
    }

    public struct ActionExecution
    {
        public long instanceId;
        public long groupId;
        public int ownerHash;
        public ActorRef ownerRef;
        public int definitionId;
        public int targetHash;
        public ActorRef targetRef;
        public Vector2 direction;
        public Vector2 targetPosition;
        public float speedMultiplier;
        public float chargeMultiplier;
        public float elapsed;
        public float phaseElapsed;
        public float normalizedTime;
        public AnimationPhase phase;
        public bool canCancel;
        public bool forced;
        public bool released;
        public long sourceInstanceId;
        public ActionDefinitionSnapshot definition;
    }

    public struct ActionExecutionEvent
    {
        public ActionSignalKind kind;
        public ActionExecution execution;
        public int signalId;
    }

    public enum ActionGroupSignalKind : byte
    {
        Completed,
        Cancelled,
    }

    public struct ActionGroupEvent
    {
        public long groupId;
        public int ownerHash;
        public ActorRef ownerRef;
        public ActionGroupSignalKind kind;
    }
}
