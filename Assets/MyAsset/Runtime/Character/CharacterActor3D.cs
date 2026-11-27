using Game.Core;
using UnityEngine;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>3Dの身体を、世代付き個体と共通の戦闘データへ接続する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterMotor3D), typeof(DamageReceiver))]
    public sealed partial class CharacterActor3D : ManagedCharacter
    {
        [SerializeField] private CharacterInfo characterInfo;
        [SerializeField, Tooltip("共通アクションを表示するモデル側のアニメーション制御。")]
        private CharacterAnimationController animationController;

        private GameManager _world;
        private DamageReceiver _damageReceiver;
        private int _objectHash;
        private ActorRef _animationOwner;
        private float _lastStamina;
        private float _staminaRecoveryWait;

        public override int ObjectHash => _objectHash;
        public override IDamageable Damageable => _damageReceiver != null ? _damageReceiver : null;
        public override bool Uses3DPhysics => true;
        internal CharacterInfo Definition => characterInfo;
        public ActorRef Actor => _world != null && _world.TryGetRegisteredActor(this, out ActorRef actor) ? actor : default;
        public CharacterMotor3D Motor { get; private set; }
        public CharacterController Body { get; private set; }
        public CharacterAnimationController AnimationController => animationController;

        public bool IsAlive
        {
            get
            {
                ActorRef actor = Actor;
                return actor.IsValid && GameManager.Data.GetVitals(actor.Hash).currentHp > 0;
            }
        }

        private void Awake()
        {
            _objectHash = gameObject.GetHashCode();
            _damageReceiver = GetComponent<DamageReceiver>();
            Motor = GetComponent<CharacterMotor3D>();
            Body = GetComponent<CharacterController>();
        }

        private void OnEnable() => Register();

        private void Start() => Register();

        private void OnDisable()
        {
            GameManager world = _world;
            ActorRef actor = Actor;
            _world = null;
            ReleaseAnimation();
            if (world != null && actor.IsValid)
            {
                if (Motor != null) { Motor.ResetMotion(); }
                world.UnregisterCharacter(this, actor);
            }
        }

        private void Register()
        {
            if (characterInfo == null || Actor.IsValid || GameManager.Instance == null) { return; }
            _world = GameManager.Instance;
            _world.RegisterCharacter(this, characterInfo);
            ActorRef actor = Actor;
            if (!actor.IsValid) { return; }
            _lastStamina = GameManager.Data.GetVitals(actor.Hash).currentStamina;
            _staminaRecoveryWait = 0f;
            if (animationController != null)
            {
                _animationOwner = actor;
                animationController.Initialize3D(this, actor);
            }
        }

        internal void RecoverPlayerStamina(float deltaTime, bool dodging)
        {
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
            if (vitals.currentHp <= 0) { return; }

            float current = vitals.currentStamina;
            // 装備で上限が下がっても、上限に収めた分は消費扱いにしない。
            bool spent = current < Mathf.Min(_lastStamina, vitals.maxStamina);
            if (spent)
            {
                _staminaRecoveryWait = current <= 0f
                    ? Mathf.Max(0f, characterInfo.staminaExhaustionPenalty)
                    : vitals.staminaRecoveryDelay > 0f ? vitals.staminaRecoveryDelay : 1f;
            }
            else
            {
                float recoveryTime = Mathf.Max(0f, deltaTime - _staminaRecoveryWait);
                _staminaRecoveryWait = Mathf.Max(0f, _staminaRecoveryWait - deltaTime);
                if (!dodging && vitals.staminaRecoveryRate > 0f && current < vitals.maxStamina)
                {
                    vitals.currentStamina = Mathf.Min(vitals.maxStamina, current + vitals.staminaRecoveryRate * recoveryTime);
                }
            }
            _lastStamina = vitals.currentStamina;
        }

        internal void ReleaseAnimation()
        {
            if (animationController != null && _animationOwner.IsValid)
            {
                animationController.Release3D(this, _animationOwner);
            }
            _animationOwner = default;
        }

#if UNITY_INCLUDE_TESTS
        public void SetCharacterInfoForTest(CharacterInfo info) { characterInfo = info; }
#endif
    }
}
