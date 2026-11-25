using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>召喚種や敵の作戦を設定し、遭遇の固定更新から身体を動かす。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class EncounterCombatant3D : MonoBehaviour
    {
        [SerializeField] private AttackInfo attack;
        [SerializeField, Min(0.1f)] private float attackRange = 1.8f;
        [SerializeField] private bool prefersFrontStation;
        [SerializeField] private int assignmentPriority;
        [SerializeField] private Transform home;
        [SerializeField] private bool canUseJumpLinks;

        private CharacterActor3D _actor;

        internal CharacterActor3D Actor => _actor != null ? _actor : _actor = GetComponent<CharacterActor3D>();
        internal AttackInfo Attack => attack;
        internal float AttackRange => attackRange;
        internal bool PrefersFrontStation => prefersFrontStation;
        internal int AssignmentPriority => assignmentPriority;
        internal Transform Home => home;
        internal bool CanUseJumpLinks => canUseJumpLinks;
    }
}
