using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Tools;
using MoreMountains.Feedbacks;
using UnityEngine.AddressableAssets;
using Rewired.Integration.CorgiEngine;
using DarkTonic.MasterAudio;
using System.Runtime.CompilerServices;

namespace MoreMountains.CorgiEngine // you might want to use your own namespace here
{
    /// <summary>
    /// _parry_DESCRIPTION
    /// </summary>
    [AddComponentMenu("Corgi Engine/Character/Abilities/ParryAbility")]
    public class ParryAbility : MyAbilityBase
    {
        /// このメソッドは、ヘルプボックスのテキストを表示するためにのみ使用されます。
        /// 能力のインスペクタの冒頭にある
        public override string HelpBoxText() { return "パリィするようになる"; }

        [SerializeField]
        GuardAbillity _guard;
        float defenceTime;
        // PlayerMove pm;
        bool noGuard;//ガードが成功してない。おねパリ禁止

        // Animation parameters
        protected const string _parryParameterName = "ParryState";
        protected int _parryAnimationParameter;

        /// <summary>
        /// animatorがパリィモーションなのかジャスガモーションなのか判断するための数値
        /// </summary>
        int parryNumber = 0;

        /// <summary>
        /// 前にブロッキングしてるか
        /// </summary>
        bool blocking;


        [SerializeField]
        int _avoidNumber = 15;

        int _initialLayer;

        /// <summary>
        ///　ここで、パラメータを初期化する必要があります。
        /// </summary>
        protected override void Initialization()
        {
            base.Initialization();
            // randomBool = false;
            _initialLayer = transform.root.gameObject.layer;



        }

        /// <summary>
        /// 1フレームごとに、しゃがんでいるかどうか、まだしゃがんでいるべきかをチェックします
        /// </summary>
        public override void ProcessAbility()
        {
            base.ProcessAbility();
            ParryAct();
        }



        /// <summary>
        /// 押し込んでいる場合は、いくつかの条件を満たしているかどうかをチェックして、アクションを実行できるかどうかを確認します。
        /// </summary>
        protected virtual void ParryAct()
        {
            // if the ability is not permitted
            if ( !AbilityPermitted )
            {
                // we do nothing and exit
                return;
            }

            if ( isPlayer )
            {
                // Debug.Log($"ね{_health._parryNow}");
                PlayerParryJudge();
            }
            else
            {
                if ( (_movement.CurrentState != CharacterStates.MovementStates.Guard && _movement.CurrentState != CharacterStates.MovementStates.GuardMove) )
                {
                    ParryJudgeEnd();
                }
            }

            if ( parryNumber != 0 )
            {
                //モーションの終わりを検査
                if ( CheckEnd() )
                {
                    ParryEnd();
                }

                //      Debug.Log("ｓｓｓ");
            }
        }



        public void PlayerParryJudge()
        {

            // Debug.Log($"ねー{_movement.CurrentState}");
            //ガードにまだヒットしてない
            if ( !_guard.guardHit )
            {
                //ガード中で、なおかつパリィしてない


                if ( (_movement.CurrentState == CharacterStates.MovementStates.Guard || _movement.CurrentState == CharacterStates.MovementStates.GuardMove) && !noGuard
                     && _condition.CurrentState == CharacterStates.CharacterConditions.Normal )
                {
                    //ガード時間計測
                    defenceTime += _controller.DeltaTime;

                    if ( !GManager.instance.equipWeapon.twinHand )
                    {
                        Debug.Log("洗面台");
                        if ( (defenceTime >= GManager.instance.equipShield.parryStart || GManager.instance.blocking) && !_health._parryNow )
                        {

                            GManager.instance.PlaySound("ParryStart", GManager.instance.PlayerPosition);
                            ParryJudgeStart();
                            defenceTime = GManager.instance.equipShield.parryStart;
                            blocking = false;
                        }
                        //パリィ継続時間が終了したら
                        else if ( defenceTime - GManager.instance.equipShield.parryStart > GManager.instance.equipShield.parryTime )
                        {
                            defenceTime = 0.0f;
                            ParryJudgeEnd();
                            noGuard = true;

                        }
                    }
                    else
                    {

                        if ( (defenceTime >= GManager.instance.equipWeapon.parryStart || GManager.instance.blocking) && !_health._parryNow )
                        {
                            GManager.instance.PlaySound("ParryStart", GManager.instance.PlayerPosition);
                            ParryJudgeStart();
                            defenceTime = GManager.instance.equipWeapon.parryStart;
                            GManager.instance.blocking = false;

                        }

                        else if ( defenceTime - GManager.instance.equipWeapon.parryStart > GManager.instance.equipWeapon.parryTime )
                        {
                            ;
                            Debug.Log($"dgg{defenceTime}");
                            defenceTime = 0.0f;
                            ParryJudgeEnd();
                            noGuard = true;
                        }
                    }
                }

                else if ( _movement.CurrentState != CharacterStates.MovementStates.Guard && _movement.CurrentState != CharacterStates.MovementStates.GuardMove )
                {
                    //   Debug.Log($"ｇｄｄｇ");
                    if ( !GManager.instance.blocking )
                    {
                        defenceTime = 0;
                    }
                    else
                    {
                        defenceTime += _controller.DeltaTime;
                        //ブロッキング判定は三秒残る
                        if ( defenceTime >= 3 )
                        {
                            GManager.instance.blocking = false;
                            defenceTime = 0;
                        }
                    }

                    ParryJudgeEnd();
                    noGuard = false;
                }
            }
            //ガードがヒットしてる時、もうパリィミスったってことに:noGuard
            else if ( _guard.guardHit && !_health._parryNow )
            {
                noGuard = true;
                defenceTime += _controller.DeltaTime;


            }

        }


        public void ParryJudgeStart()
        {
            _health._defData.nowParry = true;

        }

        /// <summary>
        /// ジャスガ判定を消し去る
        /// </summary>
        public void ParryJudgeEnd()
        {
            _health._defData.nowParry = false;
        }

        /// <summary>
        /// ジャストガード
        /// 敵がアーマーブレイクしてるならパリィ開始
        /// </summary>
        /// <param name="siBreake"></param>
        public void ParryStart(bool isBreake)
        {
            //   Debug.Log($"ｊｃｌ7{_movement.CurrentState}");

            _condition.ChangeState(CharacterStates.CharacterConditions.Moving);

            //レイヤーも回避レイヤーに
            //スタミナ回復とかも持たせるか

            if ( !isBreake )// && isPlayer)
            {
                parryNumber = 1;
                _movement.ChangeState(CharacterStates.MovementStates.justGuard);
                blocking = true;
                //  GManager.instance.PlaySound(MyCode.SoundManager.instance.blockingSound, transform.position);
            }
            else
            {
                parryNumber = 2;
                _movement.ChangeState(CharacterStates.MovementStates.Parry);
                blocking = false;
                //  GManager.instance.PlaySound(MyCode.SoundManager.instance.parrySound, transform.position);

            }
            ParryJudgeEnd();
            ParryAvoid();
        }

        void ParryEnd()
        {
            _movement.ChangeState(CharacterStates.MovementStates.Idle);
            _health._parryNow = false;
            if ( _condition.CurrentState != CharacterStates.CharacterConditions.Stunned )
            {
                _condition.ChangeState(CharacterStates.CharacterConditions.Normal);
            }
            ParryAvoid();
            parryNumber = 0;
        }


        bool CheckEnd()
        {

            if ( _animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f )
            {   // 待機時間を作りたいならば、ここの値を大きくする。
                return false;
            }
            //AnimatorClipInfo[] clipInfo = sAni.GetCurrentAnimatorClipInfo(0);

            ////Debug.Log($"アニメ終了");

            return true;

            // return !(sAni.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1);
            //  (_currentStateName);
        }


        void ParryAvoid()
        {
            if ( _initialLayer == transform.root.gameObject.layer )
            {
                transform.root.gameObject.layer = _avoidNumber;
            }
            else
            {
                transform.root.gameObject.layer = _initialLayer;
            }
        }


        /// <summary>
        ///  必要なアニメーターパラメーターがあれば、アニメーターパラメーターリストに追加します。
        /// </summary>
        protected override void InitializeAnimatorParameters()
        {
            //    RegisterAnimatorParameter(_attackParameterName, AnimatorControllerParameterType.Bool, out _attackAnimationParameter);
            RegisterAnimatorParameter(_parryParameterName, AnimatorControllerParameterType.Int, out _parryAnimationParameter);
        }

        /// <summary>
        /// アビリティのサイクルが終了した時点。
        /// 現在のしゃがむ、這うの状態をアニメーターに送る。
        /// </summary>
        public override void UpdateAnimator()
        {
            //今のステートがAttackであるかどうかでBool入れ替えてる

            MMAnimatorExtensions.UpdateAnimatorInteger(_animator, _parryAnimationParameter, parryNumber, _character._animatorParameters);

        }
    }
}
