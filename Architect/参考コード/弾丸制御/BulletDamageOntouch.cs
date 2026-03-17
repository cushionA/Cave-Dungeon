using UnityEngine;
using System.Collections;
using MoreMountains.Tools;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine.Serialization;
using UnityEngine.AddressableAssets;
using static AttackValueBase;

namespace MoreMountains.CorgiEngine
{
    public class BulletDamageOntouch : MyDamageOntouch
    {


        /// 
        /// 必要な機能
        /// 
        /// ・全ての弾丸がこいつにアクセスする
        /// ・バレットコントローラーにあって、そこからアクセスする
        /// ・攻撃対象とぶつかったら弾丸コントローラーのこいつに送る
        /// ・そしてヒット数管理などを行う
        /// 
        /// 
        /// ///





        protected override void Awake()
        {
            base.Awake();





            DamageTakenInvincibilityDuration = 0.15f;
            DamageCausedKnockbackType = KnockbackStyles.AddForce;
            DamageCausedKnockbackDirection = CausedKnockbackDirections.BasedOnOwnerPosition;
            InvincibilityDuration = 0.25f;
            //   _attackData
            //   _health = Owner.gameObject.GetComponent<MyHealth>();
        }




        /// <summary>
        /// モーションや武器の悪い効果を敵に送る
        /// コントローラーのヌルチェックにより、敵がキャラ以外なら弾くよ
        /// </summary>
       protected override void BadConditionSend(bool isGuard)
        {

            //コントローラーを獲得する
            ControllAbillity healthControl = _colliderHealth.GetControllerByHealth();

            //ヌルチェック通過したら先に進む
            if (healthControl != null)
            {
                float cutRate = isGuard ? _colliderHealth._defData.guardStatus.badConditionCut : 0;


                    //モーションデータ送る
                    _attackData.attackMotionEvent.AttackDataSend(healthControl.conditionController, isGuard, cutRate);


            }
        }


        /// <summary>
        /// これ攻撃して自分がノックバックするやつだ
        /// 特大の攻撃して反動で下る的な
        /// 基本使わないよこれ
        /// </summary>
        protected override void ApplyDamageTakenKnockback()
        {
            if ((_corgiController != null) && (DamageTakenKnockbackForce != Vector2.zero) && (!_health.Invulnerable) && (!_health.PostDamageInvulnerable) && (!_health.ImmuneToKnockback))
            {
                _knockbackForce.x = DamageCausedKnockbackForce.x;
                if (DamageTakenKnockbackDirection == TakenKnockbackDirections.BasedOnSpeed)
                {
                    Vector2 totalVelocity = _corgiController.Speed + _velocity;
                    _knockbackForce.x *= -1 * Mathf.Sign(totalVelocity.x);
                }
                if (DamageTakenKnockbackDirection == TakenKnockbackDirections.BasedOnDamagerPosition)
                {
                    Vector2 relativePosition;

                    relativePosition = transform.position - _collidingCollider.bounds.center;

                    //ノックバックする力にダメージ領域に衝突したコライダーの中央と自分の位置で割り出した方向をかけてる
                    _knockbackForce.x *= Mathf.Sign(relativePosition.x);
                }

                _knockbackForce.y = DamageCausedKnockbackForce.y;

                if (DamageTakenKnockbackType == KnockbackStyles.SetForce)
                {
                    _corgiController.SetForce(_knockbackForce);
                }
                if (DamageTakenKnockbackType == KnockbackStyles.AddForce)
                {
                    _corgiController.AddForce(_knockbackForce);
                }
            }
        }





        /// <summary>
        /// パリィされてスタンする処理
        /// </summary>
        /// <param name="isDown"></param>
        public override void ParryStunn(bool isDown = false)
        {


        }


        #region 弾丸に固有の処理

        /// <summary>
        /// 魔法のコントローラーをセットする
        /// 弾丸コードに固有
        /// </summary>
        public void BulletControllerSetting(ControllAbillity controller)
        {
            _cCon = controller;
        }




        /// <summary>
        /// ヒットしたかどうかチェックしながらCollidingと同じ処理を行う
        /// </summary>
        /// <param name="collider"></param>
        /// <returns></returns>
        bool HitableCheckColliding(Collider2D collider)
        {
            if (!this.isActiveAndEnabled)
            {
                return false;
            }

            // 衝突しているオブジェクトが無視リストに含まれている場合は、何もせずに終了します。
            if (_ignoredGameObjects.Contains(collider.gameObject))
            {
                Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return false;
            }

            // 衝突しているものがターゲットレイヤに含まれない場合は、何もせずに終了します。
            if (!MMLayers.LayerInLayerMask(collider.gameObject.layer, TargetLayerMask))
            {
                return false;
            }

            //ここまで対象外を弾く

            _collidingCollider = collider;


            //ヘルスキャッシュ
            //含んでないなら
            //何も要素がないか、キャッシュされてないなら
            //新しく要素を追加
            if (!_restoreHealth.ContainsKey(collider.gameObject))
            {
                _colliderHealth = GetColliderHealth(collider.gameObject);


                _restoreHealth.Add(collider.gameObject, _colliderHealth);

                hitCounter.Add(collider.gameObject, 0);

            }
            //含まれてる場合
            else
            {
                _colliderHealth = _restoreHealth[collider.gameObject];


            }


            //ここでヘルスの無敵確認するか
            if (_colliderHealth.InvulnerableCheck())
            {
                //   Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return false;
            }

            //ヒット数超えてるなら無視
            if (hitCounter[collider.gameObject] >= _attackData.actionData._hitLimit)
            {
                Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return false;
            }
            else
            {
                hitCounter[collider.gameObject]++;
            }

            OnHit?.Invoke();

            // ぶつかるものが壊れるものであれば
            if ((_colliderHealth != null) && (_colliderHealth.enabled))
            {
                //こっちにコントローラがあって
                //攻撃当てた相手と自分が敵対関係じゃないなら攻撃中断
                if (_cCon != null && !_colliderHealth.EnemyCheck(_cCon.gameObject))
                {
                    return false;
                }

                //攻撃ヒットイベント
                TriggerCheck(ConditionAndEffectControllAbility.EventType.攻撃ヒットイベント);

                if (_colliderHealth.CurrentHealth > 0)
                {

                    OnCollideWithDamageable(_colliderHealth);
                }
            }
            // ぶつかるものが壊れないのであれば
            else
            {

                OnCollideWithNonDamageable();
            }

            return true;
        }


        /// <summary>
        /// これを外部から呼び出すか
        /// </summary>
        /// <param name="collider"></param>
        public bool BulletHit(Collider2D collider)
        {
            return HitableCheckColliding(collider);
        }



        #endregion
    }
}