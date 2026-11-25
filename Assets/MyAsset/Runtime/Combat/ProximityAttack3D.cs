using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>近づいた相手に試作用の攻撃を繰り返す、移動しない3Dの術者。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class ProximityAttack3D : MonoBehaviour
    {
        [SerializeField] private CharacterActor3D target;
        [SerializeField] private AttackInfo attack;
        [SerializeField] private EncounterSession3D encounter;
        [SerializeField, Min(0f)] private float activationDistance = 2.4f;
        [SerializeField, Min(0.1f)] private float cooldown = 3f;

        private CharacterActor3D _actor;
        private CharacterMotor3D _motor;
        private float _nextAttackTime;

        private void Awake()
        {
            _actor = GetComponent<CharacterActor3D>();
            _motor = GetComponent<CharacterMotor3D>();
        }

        private void FixedUpdate()
        {
            bool canFight = ReferenceEquals(encounter, null) || (encounter != null && encounter.IsRunning);
            if (!_actor.IsAlive || !canFight) { return; }

            _motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime);
            GameActionRuntime actions = GameManager.Actions;
            if (actions == null || attack == null || target == null || !target.IsAlive
                || !_motor.IsGrounded || Time.fixedTime < _nextAttackTime)
            {
                return;
            }

            // 橋下から近づいた場合も詠唱を始め、どの床に当たるかは攻撃側が判定する。
            Vector3 offset = target.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= activationDistance * activationDistance
                && actions.Execute3D(_actor, attack, attack.attack3D.shape == AttackShape3D.Projectile ? target.Actor : default, out _))
            {
                _nextAttackTime = Time.fixedTime + cooldown;
            }
        }
    }
}
