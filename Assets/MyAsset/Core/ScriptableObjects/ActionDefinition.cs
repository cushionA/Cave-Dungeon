using System;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct ActionMovementProfile
    {
        public float speed;
        [Min(0f)] public float startTime;
        [Min(0f)] public float duration;
        public bool usesInput;
    }

    [Serializable]
    public struct ActionTransitionDefinition
    {
        public ActionSignalKind trigger;
        public ActionDefinition next;
        public int signalId;
        public bool requiresHit;
        [Min(0f)] public float minElapsed;
        public bool onCompletion;
        public bool detached;
    }

    [CreateAssetMenu(fileName = "Action", menuName = "Game/Action Definition")]
    public class ActionDefinition : ScriptableObject
    {
        [SerializeField] private string persistentId;
        public string PersistentId => persistentId;

        public MotionDefinition motion;
        [Min(0f)] public float staminaCost;
        [Min(0)] public int mpCost;
        [Min(0f)] public float staminaPerSecond;
        public float cancelAfter = -1f;
        [Range(-1f, 1f)] public float cancelPoint = -1f;
        public ActionEndMode endMode = ActionEndMode.Timed;
        public ActionDurationMode durationMode = ActionDurationMode.Motion;
        [Min(0f)] public float duration;
        public ActionResources resources = ActionResources.HorizontalMovement | ActionResources.Hands
            | ActionResources.Facing | ActionResources.UpperPose;
        public int priority = 10;
        public ActState actState = ActState.Attacking;
        public ActionGameplayFlags gameplay;
        public ActionEffect[] actionEffects = Array.Empty<ActionEffect>();
        public ActionTransitionDefinition[] transitions = Array.Empty<ActionTransitionDefinition>();
        public ActionMovementProfile movement;
        [Min(0f)] public float chargeTimeMin;
        [Min(0f)] public float chargeTimeMax;
        [Min(0f)] public float chargeDamageMultiplier;

        public MotionInfo GetMotion() => motion != null ? motion.info : default;

#if UNITY_EDITOR
        protected virtual void OnValidate() => RefreshPersistentId();

        /// <summary>アセット作成後のGUIDを永続参照として確定するEditor専用入口。</summary>
        public void RefreshPersistentId()
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                persistentId = UnityEditor.AssetDatabase.AssetPathToGUID(path);
            }
        }
#endif

#if UNITY_INCLUDE_TESTS
        public void SetPersistentIdForTest(string id) { persistentId = id; }
#endif
    }
}
