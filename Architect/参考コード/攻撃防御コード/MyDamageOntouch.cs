using UnityEngine;
using System.Collections;
using MoreMountains.Tools;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine.Serialization;
using UnityEngine.AddressableAssets;
using static AttackValueBase;
using System.Linq;

namespace MoreMountains.CorgiEngine
{
    public class MyDamageOntouch : DamageOnTouch
    {
        [HideInInspector]
        public AttackData _attackData = new AttackData();

        [SerializeField]
        [Header("このオブジェクトが魔法であるか")]
        protected TypeOfSubject _attacker;

        [SerializeField]
        protected ControllAbillity _cCon;

        /// <summary>
        /// 所有者
        /// </summary>
        [SerializeField]
        protected new GameObject Owner;

        [SerializeField]
        protected new MyHealth _health;
        protected new MyHealth _colliderHealth;

        /// <summary>
        /// ヘルス
        /// </summary>
        protected Dictionary<GameObject, MyHealth> _restoreHealth = new Dictionary<GameObject, MyHealth>();

        /// <summary>
        /// ヒット数
        /// </summary>
        protected Dictionary<GameObject, int> hitCounter = new Dictionary<GameObject, int>();



        //     protected List<int> _restoreCount = new List<int>();

        /// <summary>
        /// 衝突したヘルスの番号
        /// </summary>
        //   protected int collideNum;



        /// <summary>
        /// 攻撃する主体のタイプ
        /// </summary>
        public enum TypeOfSubject
        {
            Enemy,
            Player,
            Magic,
            Gimic
        }





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
        /// ダメージをあたえられるものとの衝突
        /// </summary>
        /// <param name="health"></param>
        protected override void OnCollideWithDamageable(Health health)
        {




            //背後に当たったかどうかを確認するためのアタリハンテイ検査座標
            float basePosition = 0;






            if ( _attacker != TypeOfSubject.Gimic && _attacker != TypeOfSubject.Magic )
            {


                basePosition = _cCon.ReturnPosition().x;

            }
            else
            {

                basePosition = transform.position.x;
            }




            //ここでガード判定を先に行い、ガード成功かどうかをずっと引数に渡して見ていく
            //まず位置関係の確認、次にガード中か、ジャスガ中か、ジャスガ不能かを確認
            //ジャスガはガードとしてカウントしない?

            bool isRight;
            bool isBack;
            int hitState;

            //ヒット状況の結果受け取り
            (isRight, isBack, hitState) = _colliderHealth.HitSituationCheck(basePosition);


            //ジャスガ発生時の流れ
            if ( hitState == 2 )
            {
                //ジャスガ不能攻撃でないなら成立
                if ( !_attackData.actionData.CheckFeature(AttackFeature.disPariable) )
                {

                    //攻撃側がダウンしてパリィが発生するか
                    bool ParryDown = false;

                    //攻撃者がプレイヤーかエネミーならパリィが発生するかを見る
                    //弾丸などはパリィされてもスタンしない
                    if ( (int)_attacker <= 1 )
                    {
                        //スタミナ回復

                        //敵がプレイヤーに攻撃した時、ダウンするかをはかる。
                        //攻撃データの中のパリィ抵抗値できまる
                        ParryDown = _health.ParryArmorCheck();

                        //パリィされたらスタン
                        ParryStunn(ParryDown);
                    }

                    //これでパリィ時にヒットストップかけれるよ
                    //MMFreezeFrameEvent.Trigger(Mathf.Abs(FreezeFramesOnHitDuration));

                    //パリィした相手はボーナス
                    _colliderHealth.ParryStart(ParryDown);

                    //ジャスガ処理の後にリターン
                    return;


                }
                //成立しないならガードへ
                else
                {
                    hitState = 1;
                }
            }




            //ガードしてるかどうかを見つつ状態異常を送る
            BadConditionSend(hitState == 1);



            //吹き飛ばし処理
            MyWakeUp.StunnType stunnState = ApplyDamageCausedKnockback(isBack, isRight);



            OnHitDamageable?.Invoke();

            HitDamageableFeedback?.PlayFeedbacks(_cCon.ReturnPosition());


            //ヒットストップ処理か
            if ( (FreezeFramesOnHitDuration > 0) && (Time.timeScale > 0) )
            {
                MMFreezeFrameEvent.Trigger(Mathf.Abs(FreezeFramesOnHitDuration));
            }

            GameObject attacker;


            //ヘルスに渡す攻撃者のオブジェクトを考える
            if ( _attacker == TypeOfSubject.Magic )
            {
                attacker = _cCon.gameObject;
            }
            else
            {
                attacker = transform.root.gameObject;
            }


            // ぶつかったものにダメージを与える。
            //フリッカーは明滅時間だからダメージ後無敵時間と同じでいい
            _colliderHealth.Damage(_attackData, attacker, InvincibilityDuration, InvincibilityDuration, _damageDirection, isBack, hitState == 1, stunnState);


            if ( _colliderHealth.CurrentHealth <= 0 )
            {
                //キルイベント
                TriggerCheck(ConditionAndEffectControllAbility.EventType.敵撃破時イベント);
                OnKill?.Invoke();
            }



            //ここで攻撃時の事象ダメージを計算できる
            SelfDamage(DamageTakenEveryTime + DamageTakenDamageable);


        }

        /// <summary>
        /// モーションや武器の悪い効果を敵に送る
        /// コントローラーのヌルチェックにより、敵がキャラ以外なら弾くよ
        /// </summary>
        protected virtual void BadConditionSend(bool isGuard)
        {

            //コントローラーを獲得する
            ControllAbillity healthControl = _colliderHealth.GetControllerByHealth();

            //ヌルチェック通過したら先に進む
            if ( healthControl != null )
            {
                float cutRate = isGuard ? _colliderHealth._defData.guardStatus.badConditionCut : 0;


                //データを送らせる
                //ここの引数にヘルスからとった状態異常カット率を入れて蓄積値を変更するのはありかも
                _cCon.SendAttackCondition(healthControl.conditionController, _attackData.useSecondary, isGuard, cutRate);


            }
        }


        /// <summary>
        /// これ攻撃して自分がノックバックするやつだ
        /// 特大の攻撃して反動で下る的な
        /// 基本使わないよこれ
        /// </summary>
        protected override void ApplyDamageTakenKnockback()
        {
            if ( (_corgiController != null) && (DamageTakenKnockbackForce != Vector2.zero) && (!_health.Invulnerable) && (!_health.PostDamageInvulnerable) && (!_health.ImmuneToKnockback) )
            {
                _knockbackForce.x = DamageCausedKnockbackForce.x;
                if ( DamageTakenKnockbackDirection == TakenKnockbackDirections.BasedOnSpeed )
                {
                    Vector2 totalVelocity = _corgiController.Speed + _velocity;
                    _knockbackForce.x *= -1 * Mathf.Sign(totalVelocity.x);
                }
                if ( DamageTakenKnockbackDirection == TakenKnockbackDirections.BasedOnDamagerPosition )
                {
                    Vector2 relativePosition;


                    relativePosition = (Vector3)_cCon.ReturnPosition() - _collidingCollider.bounds.center;


                    //ノックバックする力にダメージ領域に衝突したコライダーの中央と自分の位置で割り出した方向をかけてる
                    _knockbackForce.x *= Mathf.Sign(relativePosition.x);
                }

                _knockbackForce.y = DamageCausedKnockbackForce.y;

                if ( DamageTakenKnockbackType == KnockbackStyles.SetForce )
                {
                    _corgiController.SetForce(_knockbackForce);
                }
                if ( DamageTakenKnockbackType == KnockbackStyles.AddForce )
                {
                    _corgiController.AddForce(_knockbackForce);
                }
            }
        }

        /// <summary>
        /// こいつが衝突時の処理か
        /// ここで状態異常を伝えたい
        /// 
        /// ・武器の状態異常
        /// ・攻撃の状態異常
        /// ・しかも敵キャラや魔法、ギミックの場合はまた処理が違う
        /// 
        /// それらすべて引きずり出して伝えないとダメ
        /// 
        /// </summary>
        /// <param name="collider"></param>
        protected override void Colliding(Collider2D collider)
        {

            if ( !this.isActiveAndEnabled )
            {
                return;
            }

            // 衝突しているオブジェクトが無視リストに含まれている場合は、何もせずに終了します。
            if ( _ignoredGameObjects.Contains(collider.gameObject) )
            {
                Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return;
            }

            // 衝突しているものがターゲットレイヤに含まれない場合は、何もせずに終了します。
            if ( !MMLayers.LayerInLayerMask(collider.gameObject.layer, TargetLayerMask) )
            {
                return;
            }

            //ここまで対象外を弾く

            _collidingCollider = collider;

            /* 旧ヘルスキャッシュ
            //含んでないなら
            if (_restoreHealth.Count != 0)
            {
                collideNum = 100;

                if(_restoreHealth.ContainsKey())

                for (int i = 0;i<_restoreHealth.Count;i++)
                {
                    //ゲームオブジェクトがキャッシュしてるものなら
                    if (_restoreHealth[i].gameObject == collider.gameObject)
                    {
                        _colliderHealth = _restoreHealth[i];
                        collideNum = i;
                        break;
                    }
                }
                //含まれてない場合
                if (collideNum == 100)
                {
                    _colliderHealth = _colliderHealth = GetColliderHealth(collider.gameObject);
                    _restoreHealth.Add(_colliderHealth);
                    collideNum = _restoreHealth.Count - 1;
                     _restoreCount.Add(0);
                }
            }
            else
            {
                _colliderHealth = GetColliderHealth(collider.gameObject);
                _restoreHealth.Add(_colliderHealth,0);
                collideNum = 0;
                _restoreCount.Add(0);
            }

            //ここでヘルスの無敵確認するか
            if (_colliderHealth.InvulnerableCheck())
            {
                //   Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return;
            }

            if (_restoreCount[collideNum] >= _attackData.actionData._hitLimit)
            {
                Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return;
            }
            else
            {
                _restoreCount[collideNum]++;
            }
            
            */

            //ヘルスキャッシュ
            //含んでないなら



            //何も要素がないか、キャッシュされてないなら
            //新しく要素を追加
            if ( !_restoreHealth.ContainsKey(collider.gameObject) )
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
            if ( _colliderHealth.InvulnerableCheck() )
            {
                //   Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return;
            }

            //ヒット数超えてるなら無視
            if ( hitCounter[collider.gameObject] >= _attackData.actionData._hitLimit )
            {
                Debug.Log($"{this.gameObject.name}が{collider.transform.gameObject.name}");
                return;
            }
            else
            {
                hitCounter[collider.gameObject]++;
            }

            OnHit?.Invoke();

            // ぶつかるものが壊れるものであれば
            if ( (_colliderHealth != null) && (_colliderHealth.enabled) )
            {
                //こっちにコントローラがあって
                //攻撃当てた相手と自分が敵対関係じゃないなら攻撃中断
                if ( _cCon != null && !_colliderHealth.EnemyCheck(_cCon.gameObject) )
                {
                    return;
                }

                //攻撃ヒットイベント
                TriggerCheck(ConditionAndEffectControllAbility.EventType.攻撃ヒットイベント);

                if ( _colliderHealth.CurrentHealth > 0 )
                {

                    OnCollideWithDamageable(_colliderHealth);
                }
            }
            // ぶつかるものが壊れないのであれば
            else
            {

                OnCollideWithNonDamageable();
            }
        }


        /// <summary>
        /// 触れた敵のヘルスを獲得
        /// </summary>
        /// <param name="colliderObj"></param>
        /// <returns></returns>
        protected MyHealth GetColliderHealth(GameObject colliderObj)
        {
            //環境オブジェクトならアビリティ経由ではなくふつうにゲッコンする
            if ( colliderObj.tag == GManager.instance.gameData.objectTag )
            {
                return colliderObj.MMGetComponentNoAlloc<MyHealth>();
            }
            //タグごとにキャラの陣営を見て上手いことコンバットコントローラーから引きだしたいね
            else
            {
                return GManager.instance.GetControllAbilityByObject(colliderObj).ReturnHealth();
            }
        }



        /// <summary>
        /// 敵をノックバックさせるメソッド
        /// 
        /// </summary>
        /// <param name="isRight">攻撃を受けた敵が攻撃者より右にいるか</param>
        /// <returns></returns>
        protected virtual MyWakeUp.StunnType ApplyDamageCausedKnockback(bool isRight, bool isGuard)
        {//

            // 衝突した相手が CorgiController の場合、ノックバック力を適用する
            CorgiController _colliderController = _colliderHealth.GetController();

            //コントローラーがないなら戻る
            //あるいはスタン受け付けてないなら戻す。起き上がり中など
            //でも吹き飛ばしなら受け入れる
            if ( _colliderController == null || (_colliderHealth._defData.CheckFeature(DefenseData.DefState.スタン禁止) && _attackData.actionData.blowPower == Vector2.zero) )
            {
                return MyWakeUp.StunnType.notStunned;
            }

            _colliderController.SetForce(Vector2.zero);
            MyWakeUp.StunnType result = _colliderHealth.ArmorCheck(_attackData.actionData.shock, _attackData.actionData.blowPower != Vector2.zero, isGuard);

            bool isAirDown = false;

            //空中特殊ダウンが発生するなら
            if ( _colliderHealth.AirDownJudge(result) )
            {

                isAirDown = true;
                result = MyWakeUp.StunnType.Down;
                _colliderHealth.AirDown();
            }

            if ( result == MyWakeUp.StunnType.Down )
            {

                if ( !isAirDown )
                {
                    float blowDire = isRight ? _attackData.actionData.blowPower.x : _attackData.actionData.blowPower.x * -1;

                    DamageCausedKnockbackForce.Set(blowDire, _attackData.actionData.blowPower.y);
                }
                else
                {

                    //少しだけ浮く
                    DamageCausedKnockbackForce.Set(0, 60);
                }
            }
            else if ( result == MyWakeUp.StunnType.Falter )
            {
                //アニメで動かす

                float blowDire = isRight ? 160 : -160;

                DamageCausedKnockbackForce.Set(blowDire, 0);

                //吹き飛ばさないときBlowPowerで怯み方向を確認
                //基本的に0（初期値）なら普通に吹き飛ばす

                //怯む方向を大事ですよこいつ
                // DamageCausedKnockbackForce.Set(fDire, 0);
                //  DamageCausedKnockbackForce.Set(0, 0);
            }
            else
            {
                DamageCausedKnockbackForce.Set(0, 0);
            }

            if ( (_colliderController != null) && (DamageCausedKnockbackForce != Vector2.zero) && (!_colliderHealth.Invulnerable) && (!_colliderHealth.PostDamageInvulnerable) && (!_colliderHealth.ImmuneToKnockback) )
            {
                _knockbackForce.x = DamageCausedKnockbackForce.x;

                _knockbackForce.y = DamageCausedKnockbackForce.y;

                if ( DamageCausedKnockbackType == KnockbackStyles.SetForce )
                {
                    _colliderController.SetForce(_knockbackForce);
                }
                if ( DamageCausedKnockbackType == KnockbackStyles.AddForce )
                {
                    _colliderController.AddForce(_knockbackForce);
                }
            }
            return result;
        }


        /// <summary>
        /// パリィされてスタンする処理
        /// </summary>
        /// <param name="isDown"></param>
        public virtual void ParryStunn(bool isDown = false)
        {


            _cCon.StartStun(MyWakeUp.StunnType.Parried);

        }

        /// <summary>
        /// キルやヒットなどでトリガーイベントを見る
        /// ヒット時や時などに
        /// </summary>
        /// <param name="triggerType"></param>
        protected void TriggerCheck(ConditionAndEffectControllAbility.EventType triggerType)
        {
            if ( _cCon != null )
            {
                _cCon.conditionController.CheckTriggerEvent(triggerType);
            }
        }


        /// <summary>
        /// 衝突状況をリセット
        /// </summary>
        public void CollidRestoreResset()
        {
            _restoreHealth.Clear();
            hitCounter.Clear();
        }





    }
}