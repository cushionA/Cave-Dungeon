using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>指定した騎士自身が作者の配置した3D目標に到達すると、遭遇を完了する。</summary>
    public sealed class EncounterGoal3D : EncounterSession3D
    {
        [SerializeField] private BoxCollider goal;

        protected override bool IsObjectiveAvailable => goal != null && goal.enabled && goal.isTrigger
            && goal.gameObject.activeInHierarchy && goal.gameObject.scene == gameObject.scene;
        protected override bool CompletesAutomatically => true;

        protected override bool HasMetCompletionCondition()
        {
            Transform frame = goal.transform;
            CharacterController body = Knight.Body;
            Transform pose = body.transform;
            // 判定対象が決まっているので、別のColliderの有無や物理空間の検索更新に依存せず直接照合する。
            return Physics.ComputePenetration(goal, frame.position, frame.rotation,
                body, pose.position, pose.rotation, out _, out _);
        }

#if UNITY_INCLUDE_TESTS
        internal void ConfigureForTest(CharacterActor3D player, ChallengeDefinition definition, BoxCollider objective,
            EncounterTraits encounterTraits = EncounterTraits.None)
        {
            goal = objective;
            base.ConfigureForTest(player, definition, encounterTraits);
        }
#endif
    }
}
