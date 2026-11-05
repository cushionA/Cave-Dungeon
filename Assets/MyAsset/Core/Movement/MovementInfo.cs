using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    public struct MovementInfo
    {
        public Vector2 moveDirection;
        public bool jumpPressed;
        public bool jumpHeld;
        public bool dodgePressed;   // 単押し回避
        public bool sprintHeld;     // 長押しスプリント
        public AttackInputType? attackInput;
        public bool guardHeld;
        public bool interactPressed;
        public bool cooperationPressed;
        public bool weaponSwitchPressed;
        public bool gripSwitchPressed;
        public bool menuPressed;
        public bool mapPressed;
        public float chargeMultiplier;
    }
}
