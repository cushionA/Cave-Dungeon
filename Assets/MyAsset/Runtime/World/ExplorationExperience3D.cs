using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>探索地点の通過を、保存可能な発見報酬に結び付ける。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class ExplorationExperience3D : MonoBehaviour
    {
        [SerializeField] private string rewardId;
        [SerializeField, Min(0)] private int experience;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || other is not CharacterController
                || other.gameObject.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene()
                || !other.TryGetComponent(out PlayerProgression3D player)) { return; }
            player.TryAwardExploration(rewardId, experience);
        }
    }
}
