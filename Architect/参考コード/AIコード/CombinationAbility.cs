using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Tools;
using MoreMountains.Feedbacks;
using UnityEngine.AddressableAssets;
using Rewired.Integration.CorgiEngine;
using DarkTonic.MasterAudio;
using UnityEngine.UI;

namespace MoreMountains.CorgiEngine // you might want to use your own namespace here
{
    /// <summary>
    /// コンビネーションのクラス
    /// コンビネーションにはタイプがいくつかある。
    /// 標的がいるタイプ、ボタン長押ししてる間いろいろ選べるやつ
    /// ボタン長押しが適応されるやつは長押しフラグをコンビネーション側につける
    /// するとFire側にコンビネーション不可能フラグが立つ
    /// こちらでは実行できないストッパーをかけてもう一回ボタンを押したら始動するようにする
    /// コンビネーションや攻撃を凍結するFire側の仕掛けが必要
    /// 最後にStopフラグを解除してFire側に処理を戻す
    /// Combinationのアニメパラメーターの数字によってアニメーションが遷移する
    /// アイコンもいじるここで
    /// </summary>
    [AddComponentMenu("Corgi Engine/Character/Abilities/CombinationAbility")]
    public class CombinationAbility : MyAbilityBase
    {
        /// このメソッドは、ヘルプボックスのテキストを表示するためにのみ使用されます。
        /// 能力のインスペクタの冒頭にある
        public override string HelpBoxText() { return "コンビネーションに関する能力"; }

        //   [Header("武器データ")]
        /// declare your parameters here
        ///WeaponHandle参考にして 


        // Animation parameters
        protected const string _combiParameterName = "CombinationState";
        protected int _combiAnimationParameter;

        //protected RewiredCorgiEngineInputManager _inputManager;

        float combinationCool;

        //コンビネーションのクールタイムを消化してMPなしで使える状態かどうか
        bool combiEnable = true;
        /// <summary>
        /// 連携対象設定で使うためのダミー
        /// 攻撃選択はいらないのでコンビネーション用の処理を追加しよう
        /// 回復に移行とかにコンビネーションを追加してコンビネーション用に作り替える。
        /// </summary>
        [SerializeField] FireCondition dammy;

        [SerializeField]
        Text cCounter;

        [SerializeField]
        Transform cIcon;



        /// <summary>
        /// 連携のクールタイム消化を視覚化したスライダー
        /// </summary>
        [SerializeField]
        Slider cSlider;


        /// <summary>
        /// コンビネーションの段階
        /// 0の時は何もしない、1で待機モーション開始、2で発動モーション
        /// </summary>
        enum CombinationState
        {
            Idle = 0,
            Act = 1,
            End = 2
        }

        CombinationState _combState;



        /// <summary>
        /// 連携が今何段目か
        /// </summary>
        int conboChain = 0;

        float castTime;
        /// <summary>
        /// チェインの終わりを観測
        /// </summary>
        float chainEndTime;

        BrainAbility sb;
        FireAbility fire;

        /// <summary>
        /// 真なら必要な入力がもたらされたということ
        /// つまり長押ししてたボタンが離されたってこと
        /// </summary>
        bool needInput;

        /// <summary>
        /// 詠唱を中止してコンビネーションしてるか
        /// </summary>
        bool castStopping;

        [SerializeField]
        RewiredCorgiEngineInputManager _input;

        /// <summary>
        /// コンビネーションのターゲット
        /// </summary>
        GameObject combinationTarget;

        //攻撃を中断してコンビネーションしたか
        bool attackStop;


        /// <summary>
        ///　ここで、パラメータを初期化する必要があります。
        /// </summary>
        protected override void Initialization()
        {
            base.Initialization();
            // randomBool = false;

            sb = _character.FindAbility<BrainAbility>();
            fire = _character.FindAbility<FireAbility>();
            _inputManager = _input;
        }

        /// <summary>
        /// 1フレームごとに、しゃがんでいるかどうか、まだしゃがんでいるべきかをチェックします
        /// </summary>
        public override void ProcessAbility()
        {


            base.ProcessAbility();
            CombiCool();
            CombinationController();
        }

        /// <summary>
        /// アビリティサイクルの開始時に呼び出され、ここで入力の有無を確認します。
        /// </summary>
        protected override void HandleInput()
        {
            //ここで何ボタンが押されているかによって引数渡すか

            // here as an example we check if we're pressing down
            // on our main stick/direction pad/keyboard


            //長押し系の連携で、待機モーション中に手を離したら発動へ
            if (_combState == CombinationState.Act)
            {
                //ボタンから手が離れているなら真に
                needInput =  (_inputManager.CombinationButton.State.CurrentState == MMInput.ButtonStates.Off);
                
            }
            //長押しコンビネーションでストップされてるなら入力禁止で
            if (_movement.CurrentState == CharacterStates.MovementStates.Combination || _combState != CombinationState.Idle)
            {
                return;
            }

            if (sb.status.equipCombination != null)
            {
             
                　　　//地面に立っているときしか連携は使えない
                if (_inputManager.CombinationButton.State.CurrentState == MMInput.ButtonStates.ButtonDown
                    && _controller.State.IsGrounded)
                {
                    
                    chainEndTime = 0;


                    //条件の意図としてはコンビネーションが設定されてて地面にいてクールタイム中じゃなくて魔法発動中じゃない時だけ
                    //連携可能フラグはコンボ中は不可になってる。チェイン数見て最後に改めて戻される。
                    if (combiEnable && conboChain == 0) //&& !(SManager.instance.actNow && !SManager.instance.castNow))
                    {
                       // Debug.Log($"お肉{conboChain}");
                        
                        //コンボ中にコンボ受付時間切れを待つ時間計測のタイマーを再入力するたびにリセット
                        combinationCool = 0;

                        //連携出せないように
                        combiEnable = false;
                        Combination();
                       
                        //クールタイム計算開始
                        if (cIcon.gameObject.activeSelf)
                        {
                            cIcon.gameObject.SetActive(false);
                        }
                        cSlider.value = 0;

                    }
                    //コンビネーションのクールタイムが完了してないとMP使う
                    //初回で、なおかつMP足りない奴は弾く
                    else if (!combiEnable && combinationCool >= 1f && !(sb.status.equipCombination.useMP > sb.mp && conboChain == 0))
                    {
                        


                        //初回だけMP消費
                        if (conboChain == 0)
                        {
                            sb.mp -= sb.status.equipCombination.useMP;
                        }

                        Combination();

                        //クールタイムとかはそのままでいい
/*
                        if (cIcon.gameObject.activeSelf)
                        {
                            cIcon.gameObject.SetActive(false);
                        }
                       cSlider.value = 0;*/
                    }
                }
                
            }
        }



        /// <summary>
        /// これがEndによってトリガーされるメソッド。具体的な処理
        /// Endになったら発射モーションに移ってその終わりのアニメイベントで呼ばれる。
        /// chainナンバーはここで活用する。
        /// あるいはアニメーションパラメーターにしてチェインで詠唱モーション変えるとかしてもいいかも
        /// </summary>
        public void CombinationDo()
        {
            
            
            //まず使用するコンビネーションがどれか確認
           // Debug.Log($"sdsdddddddd{sb.status.equipCombination._sortNumber}");
            //ワープ
            if (sb.status.equipCombination._sortNumber == 1)
            { //Debug.Log($"sdsdsd{SManager.instance.target == null}");
                Vector2 warpPosi =  combinationTarget.transform.position;
              

                float exY = sb.RayGroundCheck(warpPosi);

                //地表から十五以下の距離なら接地
                warpPosi.y = warpPosi.y - exY <= 15 ? exY : warpPosi.y;
                        
                if (combinationTarget.transform.localScale.x > 0)
                {
                    warpPosi.Set(warpPosi.x - 10, warpPosi.y);

                    GManager.instance.Player.transform.position = warpPosi;
                    if (GManager.instance.Player.transform.localScale.x < 0)
                    {
                        GManager.instance.pc.PlayerFlip();
                    }
                }
                else
                {
                    warpPosi.Set(warpPosi.x + 10, warpPosi.y);

                    GManager.instance.Player.transform.position = warpPosi;
                  //  GManager.instance.pm.Flip();
                    if (GManager.instance.Player.transform.localScale.x > 0)
                    {
                        GManager.instance.pc.PlayerFlip();
                    }
                }
                GManager.instance.pc.PlayerStop();
                Vector3 posi = new Vector3(GManager.instance.Player.transform.position.x, GManager.instance.Player.transform.position.y,40);
                //gofire.localScale *= 0.8f;
                
                Addressables.InstantiateAsync("WarpCircle", posi, Quaternion.Euler(-98, 0, 0));//.Result;//発生位置をPlayer
                GManager.instance.PlaySound("Warp", GManager.instance.Player.transform.position);
                //return 1;
            }
            // return 0;



            //行動中止フラグ
            bool stop = false;

            combinationTarget = null;
            //攻撃中とかで前のターゲットが消滅してるなら
            if (castStopping || attackStop)
            {
                stop = true;

            }

            if (castStopping)
            {
                castStopping = false;
                if (stop || fire.MPCheck())
                {
                    fire.MagicEnd();
                }
                else
                {
                    _movement.ChangeState(CharacterStates.MovementStates.Cast);
                }
            }
            else if (attackStop)
            {
                attackStop = false;
                if (stop || fire.MPCheck())
                {
                    fire.MagicEnd();
                }
                else
                {
                    _movement.ChangeState(CharacterStates.MovementStates.Attack);
                }
            }
            else
            {
                _condition.ChangeState(CharacterStates.CharacterConditions.Normal);
                _movement.ChangeState(CharacterStates.MovementStates.Idle);
            }

            //最後に初期化
            _combState = CombinationState.Idle;
            needInput = false;
        }

        public void CombinationStart()
        {
            _combState = CombinationState.Act;
            //これでコンビネーションの詠唱モーションが始まる。
            //Endになるまで始動モーションに入らない
            //Stopはモーション終わりの処理の後解除する
            if (_movement.CurrentState == CharacterStates.MovementStates.Cast)
            {
                castStopping = true;
            }
            else if (_movement.CurrentState == CharacterStates.MovementStates.Attack)
            {
                attackStop = true;
            }

            _condition.ChangeState(CharacterStates.CharacterConditions.Moving);
            _movement.ChangeState(CharacterStates.MovementStates.Combination);
        }


        /// <summary>
        /// コンビネーションタイプによって効果発動のタイミングをコントロール
        /// </summary>
        void CombinationController()
        {
            //Act中にEndを導く処理
            //詠唱とか待ってる間の処理を入れていいかもCombinationDo()みたいに場合分けして
            if (_combState == CombinationState.Act)
            {
                //Endにする処理をいくつか入れる
                //タイプで分けて、詠唱時間の時間経過や即座に始動モーションに入るもの、あとはボタンを離したとき始動モーションに入ったり
                if (sb.status.equipCombination._combiType == SisterCombination.ActType.soon)
                {
               //     Debug.Log("ssssss");
                    _combState = CombinationState.End;
                }
                else if (sb.status.equipCombination._combiType == SisterCombination.ActType.cast)
                {
                    castTime += _controller.DeltaTime;
                    //音鳴らしたりも頼むわ
                    //詠唱して即座に開始系
                    if(castTime >= sb.status.equipCombination.castTime)
                    {
                        _combState = CombinationState.End;
                        castTime = 0;
                    }
                }
                else if (sb.status.equipCombination._combiType == SisterCombination.ActType.longPress)
                {
                    //長押しでなくボタンが押されてない状態ならでやる？
                    //ボタン押して、離された時始動
                    if (needInput)
                    {
                        _combState = CombinationState.End;
                    }
                }
                else if (sb.status.equipCombination._combiType == SisterCombination.ActType.castAndPress)
                {
                    //詠唱が終わった時ボタンが押されていない状態なら発動
                    castTime += _controller.DeltaTime;
                    bool nowOk = false;
                    if (castTime >= sb.status.equipCombination.castTime)
                    {
                        nowOk = true;
                    }
                    if (nowOk && needInput)
                    {
                        _combState = CombinationState.End;
                        castTime = 0;
                    }
                }
            }

            if(_combState == CombinationState.End)
            {
             //   Debug.Log("gggg");
                //  if (CheckEnd())
                //      {
                CombinationDo();
            //    }
            }
        }



        public void CombiCool()
        {
            //クールタイム消化中に
            if (!combiEnable)
            {
                //	Debug.Log($"野菜売り場");
                //連携のクールタイム計測
                combinationCool += _controller.DeltaTime;

                cSlider.value = combinationCool / sb.status.equipCombination.coolTime;

                if (combinationCool >= sb.status.equipCombination.coolTime)
                {
                    combinationCool = 0;
                    conboChain = 0;
                    combiEnable = true;
                    cIcon.gameObject.SetActive(true);

                }
                cCounter.text = (Mathf.Round(sb.status.equipCombination.coolTime - combinationCool)).ToString();
            }
            //ここでコンビネーション可能とか
            if (conboChain > 0 && _movement.CurrentState != CharacterStates.MovementStates.Combination)
            {
                //
                //チェイン中でかつ行動してないとき

                chainEndTime += _controller.DeltaTime;

                if (chainEndTime >= 3.5f)
                {
                    //Debug.Log($"お菓子売り場{conboChain}");

                    conboChain = 0;
                    chainEndTime = 0;

                }
            }


                


        }



        bool CheckEnd()
        {

            if (_animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
            {   // 待機時間を作りたいならば、ここの値を大きくする。
                return false;
            }
            //AnimatorClipInfo[] clipInfo = sAni.GetCurrentAnimatorClipInfo(0);

            ////Debug.Log($"アニメ終了");

            return true;

            // return !(sAni.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1);
            //  (_currentStateName);
        }


        /// <summary>
        /// 入力から呼ばれる
        /// ターゲットの設定とチェインの管理
        /// そして処理の始動
        /// </summary>
        void Combination()
        {

            if (sb.status.equipCombination.isTargeting)
            {
                //ターゲットが必要なら指定

                

                //_condition.ChangeState(CharacterStates.CharacterConditions.Moving);
               // _movement.ChangeState(CharacterStates.MovementStates.Combination);
                if (SManager.instance.target != null)
                {
                  
                    if (SManager.instance.target != transform.root.gameObject &&  SManager.instance.target != GManager.instance.Player)
                    {
                        //ロックオンを解除
                        SManager.instance.target.MMGetComponentNoAlloc<EnemyAIBase>().TargetEffectCon(3);
                        
                    }

                   
                }
                   combinationTarget = fire.TargetSelect(sb.status.equipCombination.mainTarget[conboChain], dammy);
                   

                if (combinationTarget == null)
                {
                    combinationTarget = fire.TargetSelect(sb.status.equipCombination.subTarget[conboChain], dammy);
                }

                if (combinationTarget == null)
                {
                   combinationTarget = transform.root.gameObject;
                }
            }



            //	ちゃんとターゲットがあるか、またはなくてもターゲットを必要としないなら処理を進める
            if (combinationTarget != null || !sb.status.equipCombination.isTargeting)
            {
                CombinationStart();
                conboChain++;
               // Debug.Log($"お肉売り場{conboChain}");

               //上限までチェインしてたらもうやめ
                if (conboChain >= sb.status.equipCombination.chainNumber)
                {
                    conboChain = 0;

                }
            }

        }



        /// <summary>
        ///  必要なアニメーターパラメーターがあれば、アニメーターパラメーターリストに追加します。
        /// </summary>
        protected override void InitializeAnimatorParameters()
        {
            RegisterAnimatorParameter(_combiParameterName, AnimatorControllerParameterType.Int, out _combiAnimationParameter);
        }

        /// <summary>
        /// これをオーバーライドすると、キャラクターのアニメーターにパラメータを送信することができます。
        /// これは、Characterクラスによって、Early、normal、Late process()の後に、1サイクルごとに1回呼び出される。
        /// </summary>
        public override void UpdateAnimator()
        {
            
            MMAnimatorExtensions.UpdateAnimatorInteger(_animator, _combiAnimationParameter, (int)_combState, _character._animatorParameters);
        }


    }
}
