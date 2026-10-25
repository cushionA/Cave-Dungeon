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
    }

    public struct ActionRequest
    {
        public int ownerHash;
        public int definitionId;
        public int targetHash;
        public Vector2 direction;
        public Vector2 targetPosition;
        public float speedMultiplier;
        public bool forced;
        public long sourceInstanceId;
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
        public int ownerHash;
        public int definitionId;
        public int targetHash;
        public Vector2 direction;
        public Vector2 targetPosition;
        public float speedMultiplier;
        public float elapsed;
        public float phaseElapsed;
        public float normalizedTime;
        public AnimationPhase phase;
        public bool canCancel;
        public bool forced;
        public bool released;
        public long sourceInstanceId;
        public ActionDefinitionData definition;
    }

    public struct ActionExecutionEvent
    {
        public ActionSignalKind kind;
        public ActionExecution execution;
        public int signalId;
    }
}
