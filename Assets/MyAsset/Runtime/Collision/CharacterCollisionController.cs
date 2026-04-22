using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// キャラクター同士の衝突をレイヤー切替で動的制御するMonoBehaviour。
    /// 通常時はCharaPassThrough（すり抜け）、アクション時にCharaCollide/CharaInvincibleに切替。
    /// Architect/08_物理レイヤー定義.md 参照。
    /// </summary>
    public class CharacterCollisionController : MonoBehaviour
    {
        private int _ownerHash;
        private AttackContactType _currentMode;
        private CarryState _carryState;
        private Transform _carriedTransform;

        // ハッシュ→コントローラーの辞書（O(1)検索）
        private static readonly Dictionary<int, CharacterCollisionController> s_controllerMap =
            new Dictionary<int, CharacterCollisionController>(16);

        /// <summary>
        /// Enter Play Mode Settings で Domain Reload OFF 時に静的フィールドをリセットする。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            s_controllerMap.Clear();
        }

        public int OwnerHash => _ownerHash;
        public CarryState CarryState => _carryState;
        public AttackContactType CurrentMode => _currentMode;

        private void OnEnable()
        {
            _ownerHash = gameObject.GetHashCode();
            s_controllerMap[_ownerHash] = this;
            gameObject.layer = GameConstants.k_LayerCharaPassThrough;
        }

        private void OnDisable()
        {
            ReleaseCarry();
            s_controllerMap.Remove(_ownerHash);
        }

        /// <summary>
        /// アクション開始時に衝突モードを設定する。レイヤー切替のみでO(1)。
        /// </summary>
        public void SetCollisionMode(AttackContactType contactType)
        {
            AttackContactType newMode = CharacterCollisionLogic.GetCollisionMode(
                isActionActive: true, contactType);

            if (_currentMode == newMode)
            {
                return;
            }

            _currentMode = newMode;
            ApplyLayer();
        }

        /// <summary>
        /// 無敵モードを設定する。ActionEffect.Invincibleと連動。
        /// </summary>
        public void SetInvincible(bool invincible)
        {
            if (invincible)
            {
                gameObject.layer = GameConstants.k_LayerCharaInvincible;
            }
            else
            {
                ApplyLayer();
            }
        }

        /// <summary>
        /// アクション終了時に呼ぶ。すり抜けに戻す。
        /// </summary>
        public void ClearCollisionMode()
        {
            _currentMode = AttackContactType.PassThrough;
            gameObject.layer = GameConstants.k_LayerCharaPassThrough;
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
        /// 運搬中の対象位置を同期する。
        /// 呼び出し元: <see cref="ActionExecutorController"/> の FixedUpdate から、
        /// 運搬系 Sustained 行動（Carry / ShieldDeploy 等）の実行中に毎物理フレーム呼ぶ想定。
        /// Rigidbody2D の物理解決と同じ周期で位置更新するため Update ではなく FixedUpdate 推奨。
        /// 対象キャラが SoA から居なくなっていたら自動で ReleaseCarry を呼んで運搬解除する。
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

            ref CharacterVitals carriedVitals = ref GameManager.Data.GetVitals(_carryState.CarriedHash);
            carriedVitals.position = targetPos;

            if (_carriedTransform != null)
            {
                _carriedTransform.position = new Vector3(targetPos.x, targetPos.y, _carriedTransform.position.z);
            }
            else
            {
                ReleaseCarry();
            }
        }

        private void ApplyLayer()
        {
            if (CharacterCollisionLogic.ShouldBlockMovement(_currentMode))
            {
                gameObject.layer = GameConstants.k_LayerCharaCollide;
            }
            else
            {
                gameObject.layer = GameConstants.k_LayerCharaPassThrough;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_currentMode != AttackContactType.Carry)
            {
                return;
            }

            int otherInstanceId = collision.gameObject.GetHashCode();
            if (s_controllerMap.ContainsKey(otherInstanceId))
            {
                TryStartCarry(otherInstanceId);
            }
        }
    }
}
