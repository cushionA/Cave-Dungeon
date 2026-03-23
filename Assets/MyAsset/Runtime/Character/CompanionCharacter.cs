using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 仲間キャラクターMonoBehaviour。
    /// FollowBehavior純ロジックでプレイヤーを追従する。
    /// </summary>
    public class CompanionCharacter : BaseCharacter
    {
        [Header("追従設定")]
        [SerializeField] private float _followDistance = 2.0f;
        [SerializeField] private float _maxLeashDistance = 15.0f;
        [SerializeField] private Transform _playerTransform;

        private FollowBehavior _followBehavior;

        protected override void Awake()
        {
            base.Awake();
            _followBehavior = new FollowBehavior(_followDistance, _maxLeashDistance);
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterAlly(ObjectHash);

            // プレイヤーが指定されていなければタグで探す
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    _playerTransform = player.transform;
                }
            }
        }

        private void FixedUpdate()
        {
            if (!IsAlive || _playerTransform == null)
            {
                return;
            }

            UpdateGroundCheck();

            Vector2 myPos = (Vector2)transform.position;
            Vector2 playerPos = (Vector2)_playerTransform.position;

            FollowState state = _followBehavior.Evaluate(myPos, playerPos);

            switch (state)
            {
                case FollowState.Waiting:
                    _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                    break;

                case FollowState.Following:
                    Vector2 target = _followBehavior.GetFollowTarget(myPos, playerPos);
                    ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);

                    float dir = target.x > myPos.x ? 1f : -1f;
                    _rb.linearVelocity = new Vector2(dir * moveParams.moveSpeed * 0.8f, _rb.linearVelocity.y);

                    // 向き更新
                    SetFacing(dir > 0f);
                    break;

                case FollowState.Teleporting:
                    Vector2 teleportPos = playerPos + new Vector2(_isFacingRight ? -_followDistance : _followDistance, 0f);
                    transform.position = (Vector3)teleportPos;
                    _rb.linearVelocity = Vector2.zero;
                    break;
            }

            SyncPositionToData();
        }

        protected override void OnDestroy()
        {
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }
    }
}
