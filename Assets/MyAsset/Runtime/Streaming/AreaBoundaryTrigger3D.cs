using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>3Dの騎士が境界を横切ったとき、隣接エリアのロードや離脱エリアのアンロードを要求する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class AreaBoundaryTrigger3D : MonoBehaviour
    {
        [SerializeField, Tooltip("Additiveロードするシーンの名前または完全パス。")]
        private string sceneToLoad;

        [SerializeField, Tooltip("境界通過後にアンロードする非アクティブなシーン。")]
        private string sceneToUnload;

        [SerializeField, Tooltip("世界座標 X、Z の進入方向。ゼロなら方向を問わない。")]
        private Vector2 expectedEntryDirection;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || other is not CharacterController body
                || !body.TryGetComponent(out CharacterActor3D actor) || !actor.IsAlive
                || actor.gameObject.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene())
            {
                return;
            }

            ref readonly CharacterFlags flags = ref GameManager.Data.GetFlags(actor.Actor.Hash);
            if ((flags.Feature & CharacterFeature.Player) == 0 || flags.IsVanished || flags.ActState == ActState.Dead
                || !IsMovingInExpectedDirection(actor.Motor.Velocity)) { return; }

            LevelStreamingController streaming = GameManager.LevelStreaming;
            if (streaming == null) { return; }
            if (!string.IsNullOrWhiteSpace(sceneToLoad)) { streaming.RequestAreaLoad(sceneToLoad); }
            if (!string.IsNullOrWhiteSpace(sceneToUnload)) { streaming.RequestAreaUnload(sceneToUnload); }
        }

        private bool IsMovingInExpectedDirection(Vector3 velocity) => expectedEntryDirection.sqrMagnitude <= Mathf.Epsilon
            || Vector2.Dot(new Vector2(velocity.x, velocity.z), expectedEntryDirection) > 0f;
    }
}
