using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>ワールド座標の移動入力を受け、3Dの移動と接地を確定する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotor3D : MonoBehaviour
    {
        public readonly struct GroundContact
        {
            public Collider Surface { get; }
            public Vector3 Point { get; }
            public Vector3 Normal { get; }
            public bool IsGrounded => Surface != null;

            internal GroundContact(Collider surface, Vector3 point, Vector3 normal)
            {
                Surface = surface;
                Point = point;
                Normal = normal;
            }
        }

        private struct DetachedJump
        {
            public CharacterActor3D owner;
            public ActorRef actor;
            public Scene scene;
            public Vector3 landing;
            public float groundSpeed;
            public float deadline;
            public bool interrupted;
        }

        [Header("移動")]
        [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(0.01f)] private float jumpHeight = 1.15f;
        [SerializeField, Min(0.01f)] private float gravity = 22f;

        [Header("接地")]
        [SerializeField, Min(0.01f)] private float groundStickSpeed = 2f;
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;

        private CharacterController _controller;
        private GroundContact _groundFromMove;
        private float _verticalSpeed;
        private float _minimumGroundNormalY;
        private double _lastSimulatedFixedTime = double.NegativeInfinity;
        private DetachedJump _detachedJump;

        /// <summary>直近の移動で接地したコライダーと、ワールド座標の接触点・法線。</summary>
        public GroundContact Ground { get; private set; }
        public bool IsGrounded => Ground.IsGrounded;
        public CollisionFlags Collisions { get; private set; }

        /// <summary>衝突による押し戻しを含めた、直近のワールド速度。</summary>
        public Vector3 Velocity { get; private set; }
        public Vector3 FeetPosition => _controller.transform.TransformPoint(
            _controller.center - Vector3.up * (_controller.height * 0.5f));
        public Vector3 Facing => transform.forward;
        internal float MoveSpeed => moveSpeed;
        internal float Gravity => gravity;
        internal float JumpLaunchSpeed => Mathf.Sqrt(2f * gravity * jumpHeight);
        internal bool IsFinishingEncounterJump => !IsGrounded && HasCurrentDetachedJump();

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void OnDisable()
        {
            Ground = default;
            Collisions = CollisionFlags.None;
            Velocity = Vector3.zero;
            _verticalSpeed = 0f;
            _detachedJump = default;
        }

        private void FixedUpdate()
        {
            if (!_detachedJump.actor.IsValid) { return; }
            if (!HasCurrentDetachedJump() || (IsGrounded && _lastSimulatedFixedTime != Time.fixedTimeAsDouble))
            {
                _detachedJump = default;
                return;
            }
            if (!IsGrounded) { SimulateFixedStep(Vector3.zero, false, Time.fixedDeltaTime); }
        }

        internal void FinishDetachedEncounterJump(CharacterActor3D owner, Vector3 landing, float groundSpeed,
            float deadline, bool interrupted)
        {
            if (!isActiveAndEnabled || !_controller.enabled || IsGrounded || owner == null || owner.Motor != this) { return; }
            _detachedJump = new DetachedJump
            {
                owner = owner,
                actor = owner.Actor,
                scene = owner.gameObject.scene,
                landing = landing,
                groundSpeed = groundSpeed,
                deadline = deadline,
                interrupted = interrupted,
            };
        }

        internal Vector3 EncounterJumpDirection(Vector3 landing, float groundSpeed, float deltaTime)
        {
            if (!(moveSpeed > 0f) || !(deltaTime > 0f)) { return Vector3.zero; }
            Vector3 offset = landing - FeetPosition;
            offset.y = 0f;
            return Vector3.ClampMagnitude(offset / (moveSpeed * deltaTime), groundSpeed / moveSpeed);
        }

        private bool HasCurrentDetachedJump() => _detachedJump.actor.IsValid && _detachedJump.owner != null
            && _detachedJump.owner.isActiveAndEnabled && _detachedJump.owner.Actor == _detachedJump.actor && _detachedJump.owner.IsAlive
            && _detachedJump.owner.gameObject.scene == _detachedJump.scene;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if ((groundLayers.value & (1 << hit.gameObject.layer)) == 0 || hit.normal.y < _minimumGroundNormalY)
            {
                return;
            }

            // 段差で複数の面に触れたときは、足を支える高い面を記録する。
            if (!_groundFromMove.IsGrounded || hit.point.y > _groundFromMove.Point.y)
            {
                _groundFromMove = new GroundContact(hit.collider, hit.point, hit.normal);
            }
        }

        /// <summary>一回の固定ステップを進める。入力はワールドXZ、ジャンプはそのステップでの押下。</summary>
        public void Simulate(Vector3 moveDirection, bool jumpPressed, float deltaTime)
            => Simulate(moveDirection, jumpPressed, deltaTime, oncePerFixedStep: false);

        // 遭遇と通常探索の制御を、同じ固定ステップで重ねて適用しない。
        internal void SimulateFixedStep(Vector3 moveDirection, bool jumpPressed, float deltaTime)
            => Simulate(moveDirection, jumpPressed, deltaTime, oncePerFixedStep: true);

        internal void SimulateVelocityStep(Vector3 planarVelocity, float deltaTime)
            => Simulate(planarVelocity, false, deltaTime, oncePerFixedStep: true, useVelocity: true);

        private void Simulate(Vector3 movement, bool jumpPressed, float deltaTime, bool oncePerFixedStep, bool useVelocity = false)
        {
            if (!isActiveAndEnabled || !_controller.enabled || deltaTime <= 0f
                || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return;
            }

            if (_detachedJump.actor.IsValid)
            {
                if (!HasCurrentDetachedJump()) { _detachedJump = default; }
                else if (IsGrounded)
                {
                    if (_lastSimulatedFixedTime == Time.fixedTimeAsDouble) { return; }
                    _detachedJump = default;
                }
                else
                {
                    GameActionRuntime actions = GameManager.Actions;
                    _detachedJump.interrupted |= Time.fixedTime >= _detachedJump.deadline
                        || (Collisions & (CollisionFlags.Above | CollisionFlags.Sides)) != 0
                        || (actions != null && (actions.IsMovementBlocked(_detachedJump.actor.Hash)
                            || GameManager.Data.GetStatusEffects(_detachedJump.actor.Hash).BlocksActions));
                    movement = _detachedJump.interrupted ? Vector3.zero
                        : EncounterJumpDirection(_detachedJump.landing, _detachedJump.groundSpeed, deltaTime);
                    jumpPressed = false;
                    oncePerFixedStep = true;
                    useVelocity = false;
                }
            }

            if (oncePerFixedStep)
            {
                double fixedTime = Time.fixedTimeAsDouble;
                if (_lastSimulatedFixedTime == fixedTime) { return; }
                _lastSimulatedFixedTime = fixedTime;
            }

            Vector3 move = new Vector3(movement.x, 0f, movement.z);
            if (!useVelocity) { move = Vector3.ClampMagnitude(move, 1f); }
            if (move.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(move, Vector3.up);
            }

            float verticalDistance = CalculateVerticalDistance(jumpPressed, deltaTime);
            Vector3 displacement = move * ((useVelocity ? 1f : moveSpeed) * deltaTime) + Vector3.up * verticalDistance;
            _groundFromMove = default;
            _minimumGroundNormalY = Mathf.Max(0.01f, Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad));

            Vector3 previousPosition = transform.position;
            Collisions = _controller.Move(displacement);
            Velocity = (transform.position - previousPosition) / deltaTime;
            Ground = verticalDistance <= 0f && (Collisions & CollisionFlags.Below) != 0 ? _groundFromMove : default;
            if (IsGrounded || (_verticalSpeed > 0f && (Collisions & CollisionFlags.Above) != 0))
            {
                _verticalSpeed = 0f;
            }
        }

        private float CalculateVerticalDistance(bool jumpPressed, float deltaTime)
        {
            if (IsGrounded)
            {
                if (!jumpPressed)
                {
                    return -groundStickSpeed * deltaTime;
                }

                _verticalSpeed = JumpLaunchSpeed;
            }

            float nextVerticalSpeed = _verticalSpeed - gravity * deltaTime;
            float distance = (_verticalSpeed + nextVerticalSpeed) * (0.5f * deltaTime);
            _verticalSpeed = nextVerticalSpeed;
            return distance;
        }
    }
}
