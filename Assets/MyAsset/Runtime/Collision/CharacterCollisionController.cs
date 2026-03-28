using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// キャラクター同士の衝突を動的制御するMonoBehaviour。
    /// 通常時はすり抜け、アクション時のcontactTypeに応じて衝突/運搬を有効化する。
    /// BaseCharacterと同じGameObjectにアタッチする。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class CharacterCollisionController : MonoBehaviour
    {
        private Collider2D _bodyCollider;
        private int _ownerHash;
        private AttackContactType _currentMode;
        private CarryState _carryState;
        private Transform _carriedTransform;

        // 衝突を有効にした相手コライダーのリスト（アクション終了時に復元用）
        private readonly List<Collider2D> _enabledCollisions = new List<Collider2D>(4);

        // ハッシュ→コントローラーの辞書（O(1)検索）
        private static readonly Dictionary<int, CharacterCollisionController> s_controllerMap =
            new Dictionary<int, CharacterCollisionController>(16);

        // 全コライダーリスト（衝突ペア設定用。Dictionaryのvaluesイテレーションはアロケーションが発生するため分離）
        private static readonly List<CharacterCollisionController> s_allControllers =
            new List<CharacterCollisionController>(16);

        public int OwnerHash => _ownerHash;
        public CarryState CarryState => _carryState;
        public AttackContactType CurrentMode => _currentMode;

        private void Awake()
        {
            _bodyCollider = GetComponent<Collider2D>();
        }

        private void OnEnable()
        {
            _ownerHash = gameObject.GetInstanceID();
            s_controllerMap[_ownerHash] = this;
            s_allControllers.Add(this);
        }

        private void OnDisable()
        {
            RestorePassThrough();
            ReleaseCarry();
            s_controllerMap.Remove(_ownerHash);
            s_allControllers.Remove(this);
        }

        /// <summary>
        /// アクション開始時に衝突モードを設定する。
        /// </summary>
        public void SetCollisionMode(AttackContactType contactType)
        {
            AttackContactType newMode = CharacterCollisionLogic.GetCollisionMode(
                isActionActive: true, contactType);

            if (_currentMode == newMode)
            {
                return;
            }

            // 前のモードが衝突有効だった場合、まず復元
            if (CharacterCollisionLogic.ShouldBlockMovement(_currentMode))
            {
                RestorePassThrough();
            }

            _currentMode = newMode;

            // 新モードが衝突有効の場合、他キャラとの衝突を有効化
            if (CharacterCollisionLogic.ShouldBlockMovement(_currentMode))
            {
                EnableCollisionWithOthers();
            }
        }

        /// <summary>
        /// アクション終了時に呼ぶ。すり抜けに戻す。
        /// </summary>
        public void ClearCollisionMode()
        {
            if (CharacterCollisionLogic.ShouldBlockMovement(_currentMode))
            {
                RestorePassThrough();
            }

            _currentMode = AttackContactType.PassThrough;
            ReleaseCarry();
        }

        /// <summary>
        /// 運搬を試みる。対象が運搬可能な状態でなければ失敗する。
        /// </summary>
        public bool TryStartCarry(int targetHash)
        {
            if (_carryState.IsActive)
            {
                return false;
            }

            if (!GameManager.IsCharacterValid(targetHash))
            {
                return false;
            }

            CharacterFlags targetFlags = GameManager.Data.GetFlags(targetHash);
            ActState targetState = targetFlags.ActState;
            CharacterBelong targetBelong = targetFlags.Belong;

            if (!CharacterCollisionLogic.CanCarry(targetState, targetBelong))
            {
                return false;
            }

            _carryState = CarryState.Start(_ownerHash, targetHash);

            // 運搬対象のTransformをキャッシュ（毎フレーム検索回避）
            if (s_controllerMap.TryGetValue(targetHash, out CharacterCollisionController carried))
            {
                _carriedTransform = carried.transform;
            }

            return true;
        }

        /// <summary>
        /// 運搬を解除する。
        /// </summary>
        public void ReleaseCarry()
        {
            if (!_carryState.IsActive)
            {
                return;
            }

            _carryState = CarryState.Release();
            _carriedTransform = null;
        }

        /// <summary>
        /// 運搬中の対象位置を同期する。FixedUpdateで呼ぶ。
        /// </summary>
        public void SyncCarriedPosition(Vector2 carrierPosition, Vector2 carryOffset)
        {
            if (!_carryState.IsActive)
            {
                return;
            }

            if (!GameManager.IsCharacterValid(_carryState.CarriedHash))
            {
                ReleaseCarry();
                return;
            }

            Vector2 targetPos = carrierPosition + carryOffset;

            // SoAコンテナの位置同期
            ref CharacterVitals carriedVitals = ref GameManager.Data.GetVitals(_carryState.CarriedHash);
            carriedVitals.position = targetPos;

            // キャッシュ済みTransformで同期（検索不要）
            if (_carriedTransform != null)
            {
                _carriedTransform.position = new Vector3(targetPos.x, targetPos.y, _carriedTransform.position.z);
            }
            else
            {
                ReleaseCarry();
            }
        }

        private void EnableCollisionWithOthers()
        {
            _enabledCollisions.Clear();

            for (int i = 0; i < s_allControllers.Count; i++)
            {
                CharacterCollisionController other = s_allControllers[i];
                if (other == this || other._bodyCollider == null)
                {
                    continue;
                }

                Physics2D.IgnoreCollision(_bodyCollider, other._bodyCollider, false);
                _enabledCollisions.Add(other._bodyCollider);
            }
        }

        private void RestorePassThrough()
        {
            for (int i = 0; i < _enabledCollisions.Count; i++)
            {
                if (_enabledCollisions[i] != null)
                {
                    Physics2D.IgnoreCollision(_bodyCollider, _enabledCollisions[i], true);
                }
            }

            _enabledCollisions.Clear();
        }

        /// <summary>
        /// 新しいキャラクターが登場したとき、デフォルトですり抜け設定にする。
        /// </summary>
        private void Start()
        {
            for (int i = 0; i < s_allControllers.Count; i++)
            {
                CharacterCollisionController other = s_allControllers[i];
                if (other == this || other._bodyCollider == null)
                {
                    continue;
                }

                Physics2D.IgnoreCollision(_bodyCollider, other._bodyCollider, true);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_currentMode != AttackContactType.Carry)
            {
                return;
            }

            // Collider→hashの逆引きでO(1)検索（GetComponent回避）
            int otherInstanceId = collision.gameObject.GetInstanceID();
            if (s_controllerMap.ContainsKey(otherInstanceId))
            {
                TryStartCarry(otherInstanceId);
            }
        }
    }
}
