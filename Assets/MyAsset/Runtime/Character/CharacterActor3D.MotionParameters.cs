using System;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed partial class CharacterActor3D
    {
        [Flags]
        private enum MotionParameters : byte
        {
            None = 0,
            Speed = 1 << 0,
            Grounded = 1 << 1,
            Guarding = 1 << 2,
            Dodging = 1 << 3,
        }

        private static readonly int s_MotionSpeed = Animator.StringToHash("Speed");
        private static readonly int s_MotionGrounded = Animator.StringToHash("Grounded");
        private static readonly int s_MotionGuarding = Animator.StringToHash("Guarding");
        private static readonly int s_MotionDodging = Animator.StringToHash("Dodging");

        private RuntimeAnimatorController _parameterController;
        private MotionParameters _motionParameters;

        internal void UpdateMotionParameters(CharacterAnimationController presentation, Animator animator)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (!ReferenceEquals(_parameterController, controller))
            {
                _parameterController = controller;
                _motionParameters = MotionParameters.None;
                if (controller != null)
                {
                    foreach (AnimatorControllerParameter parameter in animator.parameters)
                    {
                        if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == s_MotionSpeed)
                        {
                            _motionParameters |= MotionParameters.Speed;
                        }
                        if (parameter.type != AnimatorControllerParameterType.Bool) { continue; }
                        if (parameter.nameHash == s_MotionGrounded) { _motionParameters |= MotionParameters.Grounded; }
                        if (parameter.nameHash == s_MotionGuarding) { _motionParameters |= MotionParameters.Guarding; }
                        if (parameter.nameHash == s_MotionDodging) { _motionParameters |= MotionParameters.Dodging; }
                    }
                }
            }

            AnimationBridge bridge = presentation.Bridge;
            if ((_motionParameters & MotionParameters.Speed) != 0)
            {
                Vector3 velocity = Motor.Velocity;
                bridge.SetFloat(s_MotionSpeed, Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z));
            }
            if ((_motionParameters & MotionParameters.Grounded) != 0)
            {
                bridge.SetBool(s_MotionGrounded, Motor.IsGrounded);
            }
            if ((_motionParameters & MotionParameters.Guarding) != 0)
            {
                bridge.SetBool(s_MotionGuarding, GameManager.Data.GetDefenseState(ObjectHash).isGuarding);
            }
            if ((_motionParameters & MotionParameters.Dodging) != 0)
            {
                bridge.SetBool(s_MotionDodging, GameManager.Actions?.IsDodging(ObjectHash) == true);
            }
        }
    }
}
