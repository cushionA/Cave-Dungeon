using Cysharp.Threading.Tasks;
using DG.Tweening.Core.Easing;
using MoreMountains.Tools;
using System;
using System.Threading;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.UIElements;
using static AttackValueBase;
using static Equip;
using static FunkyCode.Light2D;
using static Micosmo.SensorToolkit.NavMeshSensor;
using static UnityEditor.PlayerSettings;

namespace MoreMountains.CorgiEngine // you might want to use your own namespace here
{
    /// <summary>
    /// 作り直す
    /// 
    /// 入力からパラメータセット、アニメ再生までを引数で値をやり取りする一本の処理にまとめる
    /// 
    /// 必要な機能
    /// ・弱強ため攻撃（最初にボタン押したらそのボタンが離されるまで待機。離されたら秒数数える。押してる間はInputをreturnしないと増え続けちゃう）
    /// ・杖の場合強で魔法処理（詠唱時間までキャスト状態、キャスト後も押し続けてれば発動しない。それか数秒？　movingだからスタミナは回復しない）
    /// ・回避でためキャンセル。詠唱もキャンセル。詠唱中は移動キー、あるいは視界変更でロックオン切り替え
    /// ・アニメーションパラメータを切り替えて各アクションを使用。キャンセル可能ポイントはアニメーションイベントで通知
    /// ・アニメの終了を待つことで攻撃処理終了
    /// 
    /// </summary>
  //  [AddComponentMenu("Corgi Engine/Character/Abilities/WeaponAbillity")]
    public class WeaponAbillity : MyAbilityBase
    {

        #region 定義



        /// <summary>
        /// 攻撃の種類を表す
        /// これに加えてチャージしてるかで攻撃の種類を表す
        /// 1足した数が準備で、2足した数がチャージ
        /// </summary>
        public enum ActType
        {
            noAttack = 0,//なにもなし
            sAttack = 1,
            bAttack = 4,
            aAttack = 7,//空中弱
            fAttack = 10,//空中強
            arts = 13,
            magic = 16//これに攻撃番号で詠唱の種類を決める。魔法の詠唱タイプなどから決める
        }

        /// <summary>
        /// 現在の行動のデータ
        /// </summary>
        public struct NowActionData
        {

            /// <summary>
            /// なんのアクションか
            /// </summary>
            public ActType nowType;

            /// <summary>
            /// 現在のアクションの状態を表す
            /// 0ならそのまま、1ならチャージ、2ならチャージアタック
            /// </summary>
            public int stateNum;

            /// <summary>
            /// 何番目のモーションか
            /// モーションは1から数え始める
            /// </summary>
            public int motionNum;

            /// <summary>
            /// 入力の情報
            /// </summary>
            public Equip.InputData inputData;

            /// <summary>
            /// チャージをいつ開始したかの時間
            /// </summary>
            public float chargeStartTime;

            /// <summary>
            /// コンボ振り切ったか
            /// </summary>
            public bool isComboEnd;

            /// <summary>
            /// 落下攻撃であるかどうか
            /// </summary>
            public bool isFall;

            /// <summary>
            /// 特殊コンボフラグ
            /// r1とr2で派生分岐
            /// </summary>
            public bool isSpecialCombo;


            /// <summary>
            /// 使用する魔法
            /// </summary>
            public PlayerMagic useMagic;

            /// <summary>
            /// この行動で消費するMP
            /// </summary>
            public float useMP;

            /// <summary>
            /// 現在ロックオンしてる敵
            /// </summary>
            public GameObject lockEnemy;

            /// <summary>
            /// 魔法や攻撃の射程。
            /// ロックオン範囲や魔法の射程を入れる
            /// </summary>
            public float range;

        }




        #endregion


        /// このメソッドは、ヘルプボックスのテキストを表示するためにのみ使用されます。
        /// 能力のインスペクタの冒頭にある
        public override string HelpBoxText() { return "TODO_HELPBOX_TEXT."; }

        //   [Header("武器データ")]
        /// declare your parameters here
        ///WeaponHandle参考にして 


        // Animation parameters
        //       protected const string _attackParameterName = "AttackNow";
        //     protected int _attackAnimationParameter;

        protected const string _typeParameterName = "AttackType";
        protected int _typeAnimationParameter;

        protected const string _numberParameterName = "AttackNumber";
        protected int _numberAnimationParameter;


        [SerializeField]
        //当たり判定が出るまでは振り返り可能にする
        CircleCollider2D _attackCircle;

        [SerializeField]
        BoxCollider2D _attackBox;

        [SerializeField]
        MyDamageOntouch _damage;


        /// <summary>
        /// 攻撃中の移動機能
        /// 向いてる方向に従って動く
        /// 
        /// 要改善
        /// 壁検出はいらない、移動処理変えれる
        /// </summary>
        [SerializeField]
        MyAttackMove _rush;

        [SerializeField]
        PlayerController pc;

        /// <summary>
        /// 現在ターゲットにしてるオブジェクト
        /// playerControllerが取ってくれる
        /// </summary>
        public GameObject targetEnemy;

        //内部パラメータ
        #region
        //------------------------------------------内部パラメータ


        /// <summary>
        /// 使用する攻撃のデータ
        /// </summary>
        AttackValue useData;




        //   float gravity;//重力を入れる
        /// <summary>
        /// 空中攻撃を無限に出せないようにする
        /// </summary>
        bool isAirEnd;

        /// <summary>
        /// 現在の行動のデータ
        /// </summary>
        NowActionData nowAction;















        bool fire1Key;
        bool fire2Key;
        bool artsKey;



        /// <summary>
        /// 真のとき振り返り可能
        /// </summary>
        bool _flipable;

        AtEffectCon _atEf;

        /// <summary>
        /// 魔法とかを射出する地点
        /// </summary>
        [SerializeField]
        Transform firePosition;

        /// <summary>
        /// 攻撃関連の処理のキャンセルトークン
        /// 中断時に使う
        /// </summary>
        CancellationTokenSource AttackToken;



        #endregion








        /// <summary>
        /// 1フレームごとに、しゃがんでいるかどうか、まだしゃがんでいるべきかをチェックします
        /// </summary>
        public override void ProcessAbility()
        {
            //攻撃状態じゃないとき
            if ( _movement.CurrentState != CharacterStates.MovementStates.Attack )
            {
                //毎フレーム地面に今着いたかどうかを確認
                //または地面から離れたかを確認する
                if ( _controller.State.JustGotGrounded || (_controller.State.WasGroundedLastFrame && !_controller.State.IsGrounded) )
                {
                    //接地状態が切り替わったら入力リセット
                    InputReset();

                    //空中攻撃ももう一度走れるように
                    isAirEnd = false;
                }
            }

            //攻撃中の振り向き
            AttackFlip();
        }





        /// <summary>
        /// アビリティサイクルの開始時に呼び出され、ここで入力の有無を確認します。
        /// </summary>
        protected override void HandleInput()
        {

            //攻撃の入力
            AttackInput();


        }






        /// <summary>
        ///  必要なアニメーターパラメーターがあれば、アニメーターパラメーターリストに追加します。
        /// </summary>
        protected override void InitializeAnimatorParameters()
        {
            //    RegisterAnimatorParameter(_attackParameterName, AnimatorControllerParameterType.Bool, out _attackAnimationParameter);
            RegisterAnimatorParameter(_typeParameterName, AnimatorControllerParameterType.Int, out _typeAnimationParameter);
            RegisterAnimatorParameter(_numberParameterName, AnimatorControllerParameterType.Int, out _numberAnimationParameter);
        }

        /// <summary>
        /// アビリティのサイクルが終了した時点。
        /// 現在のしゃがむ、這うの状態をアニメーターに送る。
        /// </summary>
        public override void UpdateAnimator()
        {

            //今のステートがAttackであるかどうかでBool入れ替えてる
            // MMAnimatorExtensions.UpdateAnimatorBool(_animator, _attackAnimationParameter, (_movement.CurrentState == CharacterStates.MovementStates.Attack), _character._animatorParameters);
            MMAnimatorExtensions.UpdateAnimatorInteger(_animator, _typeAnimationParameter, (int)nowAction.nowType + nowAction.stateNum, _character._animatorParameters);
            MMAnimatorExtensions.UpdateAnimatorInteger(_animator, _numberAnimationParameter, nowAction.motionNum, _character._animatorParameters);
        }


        #region 入力処理



        /// 新入力処理の流れ
        /// 
        /// ・まずボタンを押す
        /// ・押したボタンに応じて攻撃モーションの入力タイプを取得
        /// ・タイプに応じてチャージモーションとかをおこなう
        /// ・入力確定後はチャージかどうかで分岐する
        /// 
        /// 
        /// アニメーションパラメータは攻撃の種類、チャージかどうか、攻撃中か待機中かで決まる
        /// 攻撃の種類とチャージかどうか、待機中かどうか、全てAttackTypeのintで済ませられるな
        /// 
        /// ///


        #region メインの入力の流れ



        /// <summary>
        /// 攻撃入力の受付
        /// 
        /// 入力受付状態の種類は以下
        /// 
        /// ・未攻撃時
        /// ・攻撃入力検知中（チャージとか。キャンセル入力も受け付ける）
        /// ・攻撃発動（何も受け付けない）
        /// ・攻撃後（モーションキャンセル入力受付。落下以外。なんかボタン押したら解除）
        /// 
        /// 非同期はやめとこう
        /// 入力タイプ（押す、押して待つ、押して離すまで）によって処理を分ける
        /// </summary>
        void AttackInput()
        {

            //通常状態でなくて、攻撃中でもないなら戻る
            //もしチャージ中ならキャンセル
            if ( _condition.CurrentState != CharacterStates.CharacterConditions.Normal && _movement.CurrentState != CharacterStates.MovementStates.Attack )
            {

                //攻撃中でなくていま何か入力されてるなら（チャージ中、詠唱中なら）取り消し
                //つまり非攻撃時のチャージ入力中にノーマルじゃなくなって、つまりスタンとかした時はチャージ状態キャンセルする
                if ( nowAction.inputData.motionInput != Equip.InputType.non )
                {
                    ChargeEnd(true);
                }
                return;
            }

            //攻撃中も入力は読む
            fire1Key = (_inputManager.sAttackButton.State.CurrentState == MMInput.ButtonStates.ButtonDown);
            fire2Key = (_inputManager.bAttackButton.State.CurrentState == MMInput.ButtonStates.ButtonDown);
            artsKey = (_inputManager.ArtsButton.State.CurrentState == MMInput.ButtonStates.ButtonDown);




            //でもノーマルじゃないとこの先には行けない
            if ( _condition.CurrentState != CharacterStates.CharacterConditions.Normal )
            {
                return;
            }

            //ここでは通常状態、攻撃可能状態以外では入力を処理しない
            //しかしキャンセル可能時のみは例外で、攻撃の入力や移動によってモーションを終わらせる

            //まだ何も入力されてないなら
            if ( nowAction.inputData.motionInput == Equip.InputType.non )
            {

                InitialInput();

                //まだ何も入力がないなら
                if ( nowAction.inputData.motionInput == Equip.InputType.non )
                {
                    return;
                }
                //決まったなら初期化を入れる
                else
                {
                    //移動を停止
                    _characterHorizontalMovement.SetHorizontalMove(0);
                    _controller.SetForce(Vector2.zero);


                    if ( nowAction.nowType == ActType.magic )
                    {
                        //モーション番号ゼロは詠唱無しの魔法なので即時実行
                        if ( nowAction.motionNum == 0 )
                        {
                            //魔法開始
                            MagicAct();
                            return;
                        }

                        //チャージ時間を現在にして
                        nowAction.chargeStartTime = GManager.instance.nowTime;

                        //ステートをチャージ段階に
                        //魔法でもこれいる？
                        nowAction.stateNum = 1;

                        //チャージ開始
                        //引数には魔法かどうかを
                        ChargeStart(true);

                    }
                    //ノーマルじゃないなら
                    else if ( nowAction.inputData.motionInput != Equip.InputType.normal )
                    {
                        //チャージ時間を現在にして
                        nowAction.chargeStartTime = GManager.instance.nowTime;

                        //ステートをチャージ段階に
                        nowAction.stateNum = 1;

                        //チャージ開始
                        //引数には魔法かどうかを
                        ChargeStart(false);
                    }
                }
            }

            //ここからは入力タイプ確定後の処理
            InputController();

        }




        /// <summary>
        /// 最初に入力を受け取り、それに応じて入力タイプを受け取り
        /// 後の処理を進める
        /// 
        /// ナンバーを指定すると明示的に起動できる
        /// </summary>
        void InitialInput(int numSelect = 99)
        {


            if ( artsKey || numSelect == 3 )
            {
                if ( _controller.State.IsGrounded )
                {

                    //MPチェック
                    if ( GManager.instance.nowCondition.twinHand || GManager.instance.GetShield().weaponArts )
                    {
                        nowAction.useMP = GManager.instance.GetWeapon().artsMP[nowAction.motionNum];
                    }
                    else
                    {
                        nowAction.useMP = GManager.instance.GetShield().artsMP[nowAction.motionNum];
                    }

                    //mp足りないなら戻る
                    if ( GManager.instance.nowCondition.nowMP < nowAction.useMP )
                    {
                        return;
                    }

                    nowAction.nowType = ActType.arts;
                }
            }
            else if ( fire2Key || numSelect == 2 )
            {
                //魔法を使う装備なら
                if ( GManager.instance.GetWeapon().isMagic )
                {
                    //使う魔法がないなら戻る
                    if ( pc.useMagic == null )
                    {
                        return;
                    }

                    //MPチェック通過しないなら戻る
                    //MP消費を出して
                    nowAction.useMP = pc.useMagic.useMP * GManager.instance.GetWeapon().magicMultipler.mpMultipler;

                    //mp足りないなら戻る
                    if ( GManager.instance.nowCondition.nowMP < nowAction.useMP )
                    {
                        return;
                    }

                    //nowActionに魔法のデータセット
                    nowAction.useMagic = pc.useMagic;
                    MagicDataSet(nowAction.useMagic, true);


                }
                //そうでないなら強攻撃
                else
                {
                    if ( _controller.State.IsGrounded )
                    {
                        nowAction.nowType = ActType.bAttack;
                    }
                    else
                    {
                        nowAction.nowType = ActType.fAttack;
                    }
                }

            }
            else if ( fire1Key || numSelect == 1 )
            {
                if ( _controller.State.IsGrounded )
                {
                    nowAction.nowType = ActType.sAttack;
                }
                else
                {
                    //空中攻撃出し切ってるならもう終わり
                    if ( isAirEnd )
                    {
                        return;
                    }
                    nowAction.nowType = ActType.aAttack;
                }
            }
            else
            {
                return;
            }

            //チャージ可能か、入力方式を獲得する
            if ( nowAction.nowType != ActType.magic )
            {
                nowAction.inputData = GetInputType(nowAction.nowType, GManager.instance.nowCondition.twinHand, nowAction.motionNum);
            }
        }






        /// <summary>
        /// 入力データを返す
        /// アクションと両手持ちとモーション番号から
        /// </summary>
        /// <param name="action"></param>
        /// <param name="isTwinHand"></param>
        /// <param name="actionNum"></param>
        /// <returns></returns>
        Equip.InputData GetInputType(ActType action, bool isTwinHand, int actionNum)
        {
            if ( action == ActType.arts )
            {
                //両手持ちか
                return (isTwinHand || GManager.instance.GetShield().weaponArts) ? GManager.instance.GetWeapon().artsValue.inputData[actionNum] : GManager.instance.GetShield().artsValue.inputData[actionNum];
            }
            else if ( action == ActType.bAttack )
            {
                return isTwinHand ? GManager.instance.GetWeapon().twinBValue.inputData[actionNum] : GManager.instance.GetWeapon().bValue.inputData[actionNum];
            }
            else if ( action == ActType.sAttack )
            {
                return isTwinHand ? GManager.instance.GetWeapon().twinSValue.inputData[actionNum] : GManager.instance.GetWeapon().sValue.inputData[actionNum];
            }
            else if ( action == ActType.aAttack )
            {
                return isTwinHand ? GManager.instance.GetWeapon().twinAirValue.inputData[actionNum] : GManager.instance.GetWeapon().airValue.inputData[actionNum];
            }
            else
            {
                return isTwinHand ? GManager.instance.GetWeapon().twinStrikeValue.inputData[actionNum] : GManager.instance.GetWeapon().strikeValue.inputData[actionNum];
            }
        }


        /// <summary>
        /// 入力タイプごとに入力を実行する
        /// 
        /// チャージ中は位置が動かないようにする？
        /// 空中でも回避ボタンでキャンセル可能
        /// </summary>
        void InputController()
        {
            //インプットタイプによって処理を分ける
            if ( nowAction.inputData.motionInput == Equip.InputType.normal )
            {
                //すぐ実行
                //攻撃実行
                AttackAct();
            }
            else if ( nowAction.inputData.motionInput == Equip.InputType.chargeAttack )
            {
                ChargeInputExe();
            }
            else if ( nowAction.inputData.motionInput == Equip.InputType.waitableCharge )
            {
                WaitableChargeInputExe();
            }
            else if ( nowAction.inputData.motionInput == Equip.InputType.magic )
            {


                MagicInputExe();
            }
        }

        #endregion

        #region 入力の種類ごとのインプット処理




        /// <summary>
        /// チャージ入力を実行する
        /// 入力後指定秒数
        /// </summary>
        void ChargeInputExe()
        {
            //チャージ中かのチェック
            if ( ChargeCancelJudge() )
            {
                //チャージ終了なら戻ろうね
                return;
            }



            //チャージタイムを超えたなら
            if ( (GManager.instance.nowTime - nowAction.chargeStartTime) >= nowAction.inputData.chargeTime )
            {
                //状態をジャージ終了に変更
                //攻撃実行
                //と言っても状態を変えた時点でモーションが再生される
                //のでChangeStateとかするだけか、攻撃実行は
                //あと攻撃移動もかな
                nowAction.stateNum = 2;

                //チャージ終了して攻撃開始
                ChargeEnd(false);
            }

            //もし時間を満たしてなくてボタンを離したなら
            if ( !ChargeInputCheck() )
            {
                //状態を未チャージに
                nowAction.stateNum = 0;
                ChargeEnd(false);

                //さらに通常攻撃実行へ移行
            }
        }

        /// <summary>
        /// 待機可能チャージ入力を実行する
        /// 入力後指定秒数
        /// </summary>
        void WaitableChargeInputExe()
        {
            //チャージ中かのチェック
            if ( ChargeCancelJudge() )
            {
                //チャージ終了なら戻ろうね
                return;
            }

            //ボタン離すまではチャージ
            if ( !ChargeInputCheck() )
            {
                //離したときチャージタイムを超えてるなら
                if ( (GManager.instance.nowTime - nowAction.chargeStartTime) >= nowAction.inputData.chargeTime )
                {
                    //状態をチャージ終了に変更
                    //攻撃実行
                    //と言っても状態を変えた時点でモーションが再生される
                    //のでChangeStateとかするだけか、攻撃実行は
                    //あと攻撃移動もかな
                    nowAction.stateNum = 2;


                    //チャージ終了して攻撃開始
                    ChargeEnd(false);

                }
                //超えてなかったなら
                else
                {
                    //状態を未チャージに
                    nowAction.stateNum = 0;
                    ChargeEnd(false);

                    //さらに通常攻撃実行へ移行
                }

            }
        }

        /// <summary>
        /// 魔法の入力を実行する
        /// 入力後指定秒数経過後、ボタンを離すことで発動
        /// </summary>
        void MagicInputExe()
        {
            //チャージ中かのチェック
            if ( ChargeCancelJudge() )
            {
                //チャージ終了なら戻ろうね
                return;
            }

            //ボタンを離すまでは待機し続ける
            //ボタン離しても詠唱終了までは勝手に進む
            if ( !ChargeInputCheck() )
            {
                //離したときチャージタイムを超えてるなら
                if ( (GManager.instance.nowTime - nowAction.chargeStartTime) >= nowAction.inputData.chargeTime )
                {
                    //状態をジャージ終了に変更
                    //攻撃実行
                    //と言っても状態を変えた時点でモーションが再生される
                    //のでChangeStateとかするだけか、攻撃実行は
                    //あと攻撃移動もかな

                    //ここは魔法は独自処理考えないとな
                    nowAction.stateNum = 0;


                    //チャージ終了して攻撃開始
                    ChargeEnd(false);

                }

            }
        }

        #endregion

        #region　共通機能


        /// <summary>
        /// チャージ中、攻撃中に振り向きを行う
        /// 攻撃判定発生までは振り向けるようにしたい
        /// そのためにどこで呼べばいいのか…
        /// </summary>
        void AttackFlip()
        {
            //攻撃判定でてない攻撃時か、
            //いやふつうに攻撃アニメーションイベントでこれやるか
            //それか非同期で攻撃判定出るのを待つことにする、攻撃時に
            if ( _movement.CurrentState == CharacterStates.MovementStates.charging || (_movement.CurrentState == CharacterStates.MovementStates.Attack && _flipable) )
            {

                //左向いてて右に入力されてるなら
                if ( _horizontalInput > 0 && !_character.IsFacingRight )
                {
                    //振り向く
                    _character.Flip();
                }
                //右向いてて左に入力されてるなら
                else if ( _horizontalInput < 0 && _character.IsFacingRight )
                {
                    //振り向く
                    _character.Flip();
                }
            }

        }




        /// <summary>
        /// チャージの入力が行われているかのチェック
        /// </summary>
        /// <returns></returns>
        bool ChargeInputCheck()
        {
            if ( nowAction.nowType == ActType.sAttack || nowAction.nowType == ActType.aAttack )
            {
                return fire1Key;
            }
            //強攻撃か落下攻撃か魔法なら
            else if ( nowAction.nowType == ActType.bAttack || nowAction.nowType == ActType.fAttack || nowAction.nowType == ActType.magic )
            {
                return fire2Key;
            }
            //固有技なら
            else if ( nowAction.nowType == ActType.arts )
            {
                return artsKey;
            }
            return false;
        }

        /// <summary>
        /// チャージ状態か詠唱状態に移行する処理
        /// 移動ロック
        /// </summary>
        /// <param name="isCast"></param>
        void ChargeStart(bool isCast)
        {
            if ( isCast )
            {
                _movement.ChangeState(CharacterStates.MovementStates.Cast);
            }
            else
            {
                _movement.ChangeState(CharacterStates.MovementStates.charging);
            }
            //移動をロック
            _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.全ロック);

            //重力を消す
            _controller.GravityActive(false);

            //魔法攻撃かロックオン攻撃ならロック処理開始
            if ( nowAction.nowType == ActType.magic || useData.baseData.moveData.lockAttack )
            {

                //魔法の射程範囲か攻撃の射程を入れる
                //ロック処理
                LockOnController(99, nowAction.range, _character.IsFacingRight).Forget();
            }

        }


        /// <summary>
        /// 何らかの形でチャージを終える時の処理
        /// isStopが真なら中止で入力情報は初期化
        /// 偽なら円満終了で攻撃(魔法)処理開始
        /// </summary>
        void ChargeEnd(bool isStop)
        {
            //移動をアンロック
            _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし);



            if ( isStop )
            {
                if ( nowAction.nowType == ActType.magic )
                {
                    //詠唱エフェクト停止
                    _atEf.CastStop(nowAction.useMagic.magicData.effectLevel, nowAction.useMagic.magicData.actionImfo.mainElement);
                }

                //入力情報を消去
                InputReset();
                //重力を有効化
                _controller.GravityActive(true);


            }
            else
            {
                if ( nowAction.nowType == ActType.magic )
                {
                    //詠唱エフェクト完走
                    _atEf.CastEnd(nowAction.useMagic.magicData.effectLevel, nowAction.useMagic.magicData.actionImfo.mainElement);
                    //魔法実行
                    MagicAct();
                }
                else
                {
                    //攻撃実行
                    AttackAct();
                }

            }
        }

        /// <summary>
        /// 入力情報を初期化する
        /// </summary>
        void InputReset(bool isStop = true)
        {
            //入力キャンセル
            nowAction.inputData.motionInput = Equip.InputType.non;

            if ( isStop )
            {
                //モーションを0に戻す
                nowAction.motionNum = 0;
            }
            //状態もゼロに
            nowAction.stateNum = 0;

            //攻撃もしてない
            nowAction.nowType = ActType.noAttack;



        }


        /// <summary>
        /// 回避ボタンでチャージキャンセルする機能
        /// チャージ中は共通で使える
        /// </summary>
        bool ChargeCancelJudge()
        {
            //詠唱でもチャージでもないならチャージ終了して戻る
            if ( _movement.CurrentState != CharacterStates.MovementStates.Cast && _movement.CurrentState != CharacterStates.MovementStates.charging )
            {
                ChargeEnd(true);
                return true;
            }

            //もし回避ボタンが押されたら戻る
            if ( _inputManager.AvoidButton.State.CurrentState == MMInput.ButtonStates.ButtonDown )
            {
                ChargeEnd(true);
                return true;
            }
            return false;
        }


        /// <summary>
        /// ロックオン処理
        /// チャージや待機中のみ呼ぶ
        /// 0.2秒に一回ロック距離や方向、入力に基づいてロックオンする相手を選ぶ
        /// </summary>
        async UniTaskVoid LockOnController(int number, float lockRange, bool isRight)
        {
            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;

            while ( true )
            {

                //詠唱かチャージ中じゃないなら戻る
                //ここでロック終了というわけ
                if ( _movement.CurrentState != CharacterStates.MovementStates.charging && _movement.CurrentState != CharacterStates.MovementStates.Cast )
                {
                    //番号が99、つまり最初なら一番近い相手を
                    if ( number == 99 )
                    {
                        number = SManager.instance.PlayerLockEnemySelect(99, lockRange, isRight, false);
                    }

                    //そして今の番号の敵を設定
                    nowAction.lockEnemy = SManager.instance.GetTargetObjByNumber(number);


                    return;
                }

                //右向いてたのに左向いてるなら
                //あるいは左向いてたのに右向いてるなら
                //とにかく二つのフラグが食い違ってるなら
                //一番近い敵を再取得
                if ( isRight != _character.IsFacingRight )
                {
                    isRight = _character.IsFacingRight;
                    number = 99;
                }






                //ターゲットリストから一定の距離以内の敵を獲得する
                //入力が入るたびにそれが次のターゲットに
                //さらに言うと向いてる方向の敵だけを取得する
                //逆側の敵をロックオンしたければflipしてね
                //振り向くと一番近いそっち側の敵に変更される

                //縦入力があれば
                //または最初なら
                if ( _inputManager.SiteMovement.y != 0 || number == 99 )
                {
                    //上入力か最初（ナンバー99）なら近いやつ
                    if ( _inputManager.SiteMovement.y > 0 )
                    {
                        number = SManager.instance.PlayerLockEnemySelect(99, lockRange, isRight, false);
                    }
                    //下入力なら遠いやつ
                    else
                    {
                        number = SManager.instance.PlayerLockEnemySelect(99, lockRange, isRight, true);
                    }

                }
                //横入力があれば
                else if ( _inputManager.SiteMovement.x != 0 )
                {
                    //右入力なら一つ近いやつ
                    if ( _inputManager.SiteMovement.x > 0 )
                    {
                        number = SManager.instance.PlayerLockEnemySelect(number, lockRange, isRight, false);
                    }
                    //左入力なら一つ遠いやつ
                    else
                    {
                        number = SManager.instance.PlayerLockEnemySelect(number, lockRange, isRight, false);
                    }

                }



                //最後に0.2秒待つ
                await UniTask.Delay(TimeSpan.FromSeconds(0.2));

                // キャンセルされてたら戻る。
                if ( token.IsCancellationRequested )
                {
                    return;
                }
            }

        }






        #endregion





        #endregion


        #region 攻撃実行処理・連続コンボ処理

        /// 
        /// 攻撃状態を始動
        /// あと振り向き可能にして攻撃判定検出メソッドを呼ぶ
        /// チャージかどうかで挙動分かれる？
        /// 別に分かれないか、stateNumでわかれます
        /// 攻撃移動や攻撃エフェクトの起動も行う
        /// 
        /// 魔法実行と攻撃実行の二種類必要
        /// 
        /// /// 



        /// <summary>
        /// 攻撃の開始と状態の変化
        /// ここから終了待ちメソッドを呼ぶか
        /// 落下かどうかで挙動が変わる
        /// あと落下ならアニメイベントの挙動も変わる
        /// </summary>
        void AttackAct()
        {

            _condition.ChangeState(CharacterStates.CharacterConditions.Moving);

            _movement.ChangeState(CharacterStates.MovementStates.Attack);


            //攻撃中は重力を消す
            _controller.GravityActive(false);

            //どの攻撃を呼び出すか
            #region

            //データをセット
            AttackPrepare(nowAction.nowType, (nowAction.stateNum == 2), nowAction.motionNum);


            //追加エフェクト今のところなさそう
            int adType = 0;

            //攻撃エフェクト準備
            //攻撃エフェクト発生アニメイベントを
            //別途設定する
            //攻撃で炎の魔法でたりするのはどうするか
            //別途設定やな！
            //攻撃時に出る魔法エフェクト、みたいな感じでコントローラのゲームオブジェクトを保存する変数を攻撃データに入れる
            _atEf.EffectPrepare(useData.baseData.effectLevel, adType, useData.baseData.actionImfo.mainElement, useData.baseData.motionType);


            #endregion


            AttackMoveData moveData = useData.baseData.moveData;


            //今のモーション番号でやること終えたら次の攻撃番号へ
            nowAction.motionNum++;

            //現在の空中モーションがコンボ限界なら
            if ( nowAction.isComboEnd && nowAction.nowType == ActType.aAttack )
            {

                //空中攻撃を終了に
                isAirEnd = true;
            }


            //ロック攻撃なら
            if ( moveData.lockAttack )
            {

                targetEnemy = nowAction.lockEnemy;

                //横の距離
                float distance = GManager.instance.PlayerPosition.x - SManager.instance._targetList[targetEnemy].controlAbility.ReturnPosition().x;


                //敵との距離が移動範囲内で、ロックオンするなら移動距離を敵の前の位置までに縮める
                moveData._moveDistance = (distance < moveData._moveDistance) ? distance - 10 : moveData._moveDistance;
            }


            //特殊派生を持っているかの確認
            nowAction.isSpecialCombo = useData.baseData.actionImfo.CheckFeature(AttackFeature.specialCombo);


            if ( useData.baseData.actionImfo.CheckFeature(AttackFeature.fallAttack) )
            {
                //落下攻撃特性を含むかどうか
                nowAction.isFall = true;

                //着地モーションがあるので確実にコンボ
                //着地モーションは攻撃扱いに
                //ちゃんと設定しとけばこれいらんけどな
                //いちおうわかりやすくね
                useData.baseData.isCombo = true;
            }

            //攻撃移動開始
            //落下攻撃かどうかで挙動が変わるが、落下自体は武器アビリティが扱う
            _rush.RushStart(moveData._moveDuration, moveData._moveDistance, moveData._contactType, nowAction.isFall, moveData.startMoveTime, useData.baseData.actionImfo.CheckFeature(AttackFeature.backAttack));

            GManager.instance.StaminaUse(useData.useStamina);


            //ヘルスを攻撃中に
            _health.HealthStateChange(false, DefenseData.DefState.攻撃中);

            //ヘルスをアーマー付きに
            _health.HealthStateChange(false, DefenseData.DefState.アーマー付き);



            //ガード攻撃ならガード判定とスパアマ判定開始
            if ( useData.baseData.actionImfo.CheckFeature(AttackFeature.guardAttack) )
            {
                //ガード攻撃開始
                _health.HealthStateChange(false, DefenseData.DefState.ガード中);
                _health.HealthStateChange(false, DefenseData.DefState.スーパーアーマー);
            }
            //スパアマ攻撃ならスパアマ開始
            else if ( useData.baseData.actionImfo.CheckFeature(AttackFeature.superArmor) )
            {
                //スパアマ開始
                _health.HealthStateChange(false, DefenseData.DefState.スーパーアーマー);
            }

            //振り返り可能判断開始
            AttackFlipEndJudge().Forget();
        }


        /// <summary>
        /// コンボ攻撃を実行する
        /// 次にどの攻撃を出すのか
        /// 現在の攻撃のタイプから次の攻撃を確認
        /// 今がチャージで次がチャージ攻撃あるなら問答無用でチャージ攻撃
        /// </summary>
        void ComboAttackJudge()
        {
            //準備
            ComboAttackPrepare();

            //チャージコンボ攻撃を出す
            if ( nowAction.stateNum == 2 && NextChargiableCheck() )
            {
                //チャージ状態
                nowAction.stateNum = 2;
            }
            else
            {
                //チャージ状態じゃない
                nowAction.stateNum = 0;
            }

            //コンボ攻撃実行
            AttackAct();
        }

        /// <summary>
        /// 次の攻撃はチャージ可能かをチェック
        /// </summary>
        /// <returns></returns>
        bool NextChargiableCheck()
        {
            //次の入力タイプがふつうじゃないならチャージモーションを出す
            return GetInputType(nowAction.nowType, GManager.instance.nowCondition.twinHand, nowAction.motionNum).motionInput != InputType.normal;
        }


        /// <summary>
        /// コンボアタックを始めるために必要な処理
        /// </summary>
        void ComboAttackPrepare()
        {


            //落下攻撃なら
            if ( nowAction.isFall )
            {
                nowAction.isFall = false;
                //ヘルス状態を戻す
                AttackHealthStateEnd();
                //重力も戻す
                _controller.DefaultParameters.Gravity = -GManager.instance.gameData.firstGravity;
            }

            //当たり判定の記録をリセット
            _damage.CollidRestoreResset();
        }


        #endregion


        #region 魔法処理

        /// 
        /// 
        /// 詠唱ステートに行ってから発射
        /// エフェクトやらモーションの情報は使用魔法から取得
        /// 魔法管理はプレイヤーコントローラーでやらせるか
        /// 
        /// 




        ///<sammary>
        /// 魔法のモーションデータを入れる
        /// isCast真なら詠唱段階
        /// あとエフェクトも開始する
        /// </sammary>
        void MagicDataSet(PlayerMagic useMagic, bool isCast)
        {
            if ( isCast )
            {
                nowAction.nowType = ActType.magic;

                /// 入力の情報
                nowAction.inputData.motionInput = InputType.magic;

                //詠唱時間設定
                //武器の詠唱速度倍率をかける
                nowAction.inputData.chargeTime = useMagic.castTime * GManager.instance.GetWeapon().magicMultipler.castSpeedMultipler;

                nowAction.motionNum = (int)useMagic.castType;

                nowAction.range = useMagic.magicRange;

                //詠唱エフェクト開始
                _atEf.CastStart(useMagic.magicData.effectLevel, useMagic.magicData.actionImfo.mainElement);
            }
            else
            {
                nowAction.motionNum = (int)useMagic.fireType;
            }

        }



        /// <summary>
        /// 攻撃の開始と状態の変化
        /// ここから終了待ちメソッドを呼ぶか
        /// 落下かどうかで挙動が変わる
        /// あと落下ならアニメイベントの挙動も変わる
        /// </summary>
        void MagicAct()
        {
            _condition.ChangeState(CharacterStates.CharacterConditions.Moving);

            _movement.ChangeState(CharacterStates.MovementStates.Attack);


            //魔法攻撃中は重力を消す
            _controller.GravityActive(false);

            //どの攻撃を呼び出すか
            #region

            //nowActionに魔法のデータセット
            MagicDataSet(pc.useMagic, false);

            /// 
            /// 魔法の攻撃力はどう決めるか
            /// 弾丸がバフ倍率を親元に尋ねてDamageOnTouchに与える
            /// 弾丸に能力値で補正を与える方法は？　武器からとればいい
            /// それか魔法ステータスみたいなのを用意して
            /// 詠唱速度、威力、削り、mp消費みたいなのをステータスでそれぞれを強化できるようにする
            /// 補正レベルと能力値で係数が変化する
            /// 
            /// 
            /// ///


            //弾丸呼び出しはアニメイベントに任すか？


            //追加エフェクト   のタイプ。今のところなさそう
            int adType = 0;


            //必要な要素
            //ロックオンした敵情報(nowActionに。ふつうの攻撃でも弾丸射出攻撃ならおなじことしなくちゃ？)
            //ロックオン処理
            //魔法攻撃開始アニメイベント
            //弾丸射出地点
            //MP消費は弾丸射出開始にするか

            #endregion






            //攻撃移動開始
            //落下攻撃かどうかで挙動が変わるが、落下自体は武器アビリティが扱う
            _rush.RushStart(nowAction.useMagic.magicData.moveData._moveDuration, nowAction.useMagic.magicData.moveData._moveDistance, nowAction.useMagic.magicData.moveData._contactType, nowAction.isFall, nowAction.useMagic.magicData.moveData.startMoveTime, nowAction.useMagic.magicData.actionImfo.CheckFeature(AttackFeature.backAttack));

            //スタミナどうしようかな
            GManager.instance.StaminaUse(nowAction.useMagic.useStamina);


            //ヘルスを攻撃中に
            _health.HealthStateChange(false, DefenseData.DefState.攻撃中);

            if ( nowAction.useMagic.magicData.actionImfo.additionalArmor > 0 )
            {
                //ヘルスをアーマー付きに
                _health.HealthStateChange(false, DefenseData.DefState.アーマー付き);
            }


            //ガード判定が出る魔法ならガード判定とスパアマ判定開始
            if ( nowAction.useMagic.magicData.actionImfo.CheckFeature(AttackFeature.guardAttack) )
            {
                //ガード攻撃開始
                _health.HealthStateChange(false, DefenseData.DefState.ガード中);
                _health.HealthStateChange(false, DefenseData.DefState.スーパーアーマー);
            }
            //スパアマ判定が出る魔法ならスパアマ開始
            else if ( nowAction.useMagic.magicData.actionImfo.CheckFeature(AttackFeature.superArmor) )
            {
                //スパアマ開始
                _health.HealthStateChange(false, DefenseData.DefState.スーパーアーマー);
            }

            //振り返り可能判断開始
            AttackFlipEndJudge().Forget();
        }



        #endregion

        #region モーション番号と攻撃タイプに基づいて攻撃データを取得

        /// <summary>
        /// 攻撃の準備
        /// データを入れる
        /// ここまではただの入力待ち受けかな
        /// </summary>
        /// <param name="type"></param>
        /// <param name="attackNum"></param>
        void AttackPrepare(ActType type, bool isCharge, int attackNum)
        {

            useData = GetAttackImfo(type, isCharge, attackNum, GManager.instance.nowCondition.twinHand);


            //ダメージにアクションデータを渡す
            _damage._attackData.actionData = useData.baseData.actionImfo;



            nowAction.range = useData.baseData.moveData._moveDistance;

            //状態異常仕込み
            //攻撃が持つコンディションコントローラーの特殊効果を記録
            _damage._attackData.attackMotionEvent = useData.baseData.attackMotionEvent;



            //現在のモーションでコンボが終わるかを確認する
            nowAction.isComboEnd = useData.baseData.isComboEndPoint;


            //装備が持つ異常データも入れないとダメだな
            //毒エンチャで入れたやつとか
            //攻撃に応じて装備の攻撃力とエンチャントデータを保存していく
            _character.characterController.AttackStatusPrepare(_damage._attackData.useSecondary);


            //当たり判定の記録をリセット
            //このへんはdamageOnの攻撃準備メソッドにまとめていいかも
            //攻撃終了に入れることにした
            //_damage.CollidRestoreResset();


        }



        ///<summary>
        ///攻撃するときに呼ぶ
        /// 使用する攻撃データを返す
        /// ついでにuseSecondaryもハメとく
        /// </summary>
        AttackValue GetAttackImfo(ActType type, bool isCharge, int attackNum, bool twinHand)//デフォが斬撃
        {

            //コンボの最初にコンボ何回繋がるか確認する。


            MotionChargeImfo container;

            _damage._attackData.useSecondary = false;


            if ( type == ActType.sAttack )
            {
                container = twinHand ? GManager.instance.GetWeapon().twinSValue : GManager.instance.GetWeapon().sValue;
            }
            else if ( type == ActType.bAttack )
            {

                container = twinHand ? GManager.instance.GetWeapon().twinBValue : GManager.instance.GetWeapon().bValue;
            }
            else if ( type == ActType.aAttack )
            {
                container = twinHand ? GManager.instance.GetWeapon().twinAirValue : GManager.instance.GetWeapon().airValue;
            }
            else if ( type == ActType.fAttack )
            {

                container = twinHand ? GManager.instance.GetWeapon().twinStrikeValue : GManager.instance.GetWeapon().strikeValue;
            }
            else// if (type == ActType.arts)
            {
                //両手持ちか武器優先なら武器
                if ( twinHand || GManager.instance.GetShield().weaponArts )
                {
                    container = GManager.instance.GetWeapon().artsValue;
                }
                else
                {
                    //ついでに二番目の武器使うフラグも立てる
                    _damage._attackData.useSecondary = true;

                    //そうでないなら盾
                    container = GManager.instance.GetShield().artsValue;
                }


            }

            if ( !isCharge )
            {

                return container.normalValue[attackNum];
            }
            else
            {

                return container.chargeValue[attackNum];
            }

        }




        #endregion


        #region　攻撃管理用アニメイベント

        /// <summary>
        /// 攻撃中に呼ばれるアニメイベント
        /// キャンセル可能点の通知、あるいは落下開始の通知
        /// </summary>
        public void Continue()
        {

            if ( nowAction.nowType == ActType.arts )
            {
                GManager.instance.MPChange(-nowAction.useMP);
            }



            //重力を有効化
            _controller.GravityActive(true);


            //落下攻撃はここで落下開始
            if ( nowAction.isFall )
            {


                //1.4倍の重力をかける
                _controller.DefaultParameters.Gravity = -GManager.instance.gameData.firstGravity * 1.4f;

                //落下攻撃終了待ち
                //時限待機して一定時間経過後解除も付けていいかも
                //解除後地面についてなかったら着地アニメスルーで
                FallAttackEndWait().Forget();

            }
            //それ以外なら攻撃中のアーマーやらを消して
            //ここからキャンセル可能に
            else
            {
                //ヘルスをもとに戻す
                AttackHealthStateEnd();



                //モーション終了検査を呼ぶ
                AttackEndWait().Forget();

                //コンボじゃないならキャンセルを呼ぶ
                if ( !useData.baseData.isCombo )
                {
                    CancelInputWait().Forget();
                }

            }
        }

        /// <summary>
        /// 攻撃用のヘルスのステートを全解除する
        /// fallのときは攻撃終了時に呼ぶ
        /// </summary>
        void AttackHealthStateEnd()
        {
            _health.HealthStateChange(true, DefenseData.DefState.ガード中);
            _health.HealthStateChange(true, DefenseData.DefState.スーパーアーマー);
            _health.HealthStateChange(true, DefenseData.DefState.アーマー付き);
            _health.HealthStateChange(true, DefenseData.DefState.攻撃中);
        }


        /// <summary>
        /// 攻撃中に呼ばれるアニメイベント
        /// キャンセル可能点の通知、あるいは落下開始の通知
        /// </summary>
        public void MagicContinue()
        {

            GManager.instance.MPChange(-nowAction.useMP);

            //弾丸呼び出しはアニメイベントに任すか？
            GameObject controller = _atEf.BulletCall(nowAction.useMagic.bulletData.fireController, firePosition.position, firePosition.rotation, nowAction.useMagic.bulletData.flashEffect).gameObject;


            //重力を有効化
            _controller.GravityActive(true);


            //落下攻撃はここで落下開始
            if ( nowAction.isFall )
            {


                //1.4倍の重力をかける
                _controller.DefaultParameters.Gravity = -GManager.instance.gameData.firstGravity * 1.4f;

                //落下攻撃終了待ち
                //時限待機して一定時間経過後解除も付けていいかも
                //解除後地面についてなかったら着地アニメスルーで
                FallAttackEndWait().Forget();

            }
            //それ以外なら攻撃中のアーマーやらを消して
            //ここからキャンセル可能に
            else
            {
                //ヘルスをもとに戻す
                AttackHealthStateEnd();



                //モーション終了検査を呼ぶ
                AttackEndWait().Forget();

                //コンボじゃないならキャンセルを呼ぶ
                if ( !useData.baseData.isCombo )
                {
                    CancelInputWait().Forget();
                }

            }
        }


        /// <summary>
        /// 生成した弾丸の設定をする
        /// </summary>
        void BulletSetting(GameObject bulletCon)
        {
            float firstAngle;

            //雨系統で敵がしっかりいるなら
            if ( nowAction.lockEnemy != null && nowAction.useMagic.bulletData.childM.bulletData._moveSt.fireType == Magic.FIREBULLET.RAIN )
            {
                //敵の方向を向く
                Vector3 direction = (Vector3)GManager.instance.GetControllAbilityByObject(nowAction.lockEnemy).ReturnPosition() - firePosition.position;
                firstAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            }
            else
            {
                firstAngle = nowAction.useMagic.bulletData.childM.bulletData._moveSt.angle;

            }

            //ここキャッシュする？
            //衝突のたびならありだよ
            bulletCon.GetComponent<BulletController>().UserDataSet(_character.characterController, _atEf, firstAngle, nowAction.useMagic, nowAction.lockEnemy);
        }


        #endregion



        #region 特殊派生

        //当身とか戦技派生とか

        /// <summary>
        /// 動きを次に入れ替える
        /// 当身とかでヘルスから行く
        /// 外部から呼ぶよ
        /// コンティニューとかすっ飛ばして次に進む
        /// 引数に攻撃移行状態（当身とか特殊フラグとか入れて）音とか鳴らしてもいいかも
        /// でも基本は当身成立時に鳴らすべきでは
        /// 当身は当身モーション（攻撃モーション）開始から終わるまでの間に攻撃受けたら始動するよね
        /// 当身状態はcontinue呼ばれるまでは続くattackHealthState
        /// 当身時は発動の際に攻撃してきた敵をターゲットとして引数にしてもいいかも
        /// 
        /// numの分だけ進む
        /// でも基本は1だよね
        ///
        /// </summary>
        public void StartNextMove(int num)// , AtCOndition condi)
        {


            if ( nowAction.nowType == ActType.arts )
            {
                GManager.instance.MPChange(-nowAction.useMP);
            }


            //コンティニューでやる処理

            //重力を有効化
            _controller.GravityActive(true);
            //ヘルスをもとに戻す
            AttackHealthStateEnd();
            nowAction.isFall = false;
            //当たり判定の記録をリセット
            _damage.CollidRestoreResset();


            //ターゲットを削除
            targetEnemy = null;
            //ここで無敵判定出す？


            //ここから次の攻撃へ
            AttackAct();


        }



        /// <summary>
        /// 特定の攻撃からr1かr2で特殊派生を行う
        /// 
        /// タイプは変えずにr1ならそのまま（すでに+1されてる）
        /// r2ならさらに+1（合計+2）
        /// 
        /// 何も入力ない、あるいはr1r2以外なら攻撃終了
        /// 攻撃後、入力があった時点で呼ばれるので色々気にしなくていい
        /// ここで終わらせていい
        /// 
        /// 構え系のやつは判定あったりなかったりする長いモーションから派生する感じ
        /// 
        /// </summary>
        void SpecialCombo()
        {

            bool isEnd = true;



            //入力された時攻撃ボタンを押してるか
            if ( fire1Key )
            {
                isEnd = false;

            }
            else if ( fire2Key )
            {
                isEnd = false;
                //もう一つ先のモーションへ
                nowAction.motionNum++;
            }



            nowAction.isComboEnd = isEnd;

            //攻撃終了
            AttackEnd();

            //攻撃入力あったなら
            if ( !isEnd )
            {

                //このタイプのまま次の入力情報をセット
                nowAction.inputData = GetInputType(nowAction.nowType, GManager.instance.nowCondition.twinHand, nowAction.motionNum);

                //まだ何も入力がないなら
                if ( nowAction.inputData.motionInput == Equip.InputType.non )
                {
                    return;
                }
                //決まったなら初期化を入れる
                else
                {
                    //移動を停止
                    _characterHorizontalMovement.SetHorizontalMove(0);
                    _controller.SetForce(Vector2.zero);


                    //ノーマルじゃないなら
                    if ( nowAction.inputData.motionInput != Equip.InputType.normal )
                    {
                        //チャージ時間を現在にして
                        nowAction.chargeStartTime = GManager.instance.nowTime;

                        //ステートをチャージ段階に
                        nowAction.stateNum = 1;

                        //チャージ開始
                        //魔法ではない
                        ChargeStart(false);
                    }
                }

            }


        }



        #endregion

        #region チャージ・攻撃中の振り向き処理


        /// <summary>
        /// 攻撃振り向きが可能な時間の制御をする
        /// 攻撃判定が出るまでは振り向き可能
        /// これ単体で動作できる
        /// 他でフラグ管理しなくていい
        /// どうせ最初は真だから
        /// </summary>
        /// <returns></returns>
        async UniTaskVoid AttackFlipEndJudge()
        {
            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;


            //まず最初に振り向き可能に
            _flipable = true;

            //当たり判定が出るのを待つ
            await UniTask.WaitUntil(() => (_attackBox.enabled || _attackCircle.enabled));
            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }
            //振り向けなくする
            _flipable = false;

        }


        #endregion



        #region モーション終了・キャンセル待機




        /// <summary>
        /// モーションの終了待ちをする
        /// 攻撃終了
        /// 自動コンボ派生が必要
        /// 
        /// コンボ攻撃では必ずここでモーション終了待ちしてから次につながる
        /// </summary>
        /// <returns></returns>
        private async UniTask AttackEndWait()
        {

            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;

            // 現在のモーション終了まで待機
            await UniTask.WaitUntil(() =>
            {
                return _animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f;
            });

            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }

            //入力された時すでに攻撃状態じゃなかったら戻る
            if ( _movement.CurrentState != CharacterStates.MovementStates.Attack )
            {
                return;
            }

            //コンボなら
            //チャージしてるかで分岐するよね
            //現在チャージしてて次にチャージ攻撃があるならチャージか
            if ( useData.baseData.isCombo )
            {
                //現在チャージ攻撃か、次もチャージがあるかを調べて
                //即座に攻撃実行
                ComboAttackJudge();
            }
            else
            {
                //攻撃終了
                AttackEnd();
            }
        }



        /// <summary>
        /// 入力による攻撃状態のキャンセルを見る
        /// 自動コンボ攻撃では呼ばれない
        /// コンティニューで呼ぶ
        /// 
        /// 特殊派生機能も入れるか
        /// </summary>
        /// <returns></returns>
        private async UniTaskVoid CancelInputWait()
        {
            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;

            // 入力、あるいはアニメ終了を待つ
            await UniTask.WaitUntil(() => AnyKey());

            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }

            //入力された時すでに攻撃状態じゃなかったら戻る
            if ( _movement.CurrentState != CharacterStates.MovementStates.Attack )
            {
                return;
            }

            AttackEnd();


            //特殊コンボ攻撃ならそちらに派生する
            if ( nowAction.isSpecialCombo )
            {
                SpecialCombo();
                return;
            }


            //魔法だとこのメソッド呼ばれないから安心して武器準拠で判断していい

            ActType nextType = ActType.noAttack;

            //次の入力の番号
            //これで入力処理の途中から始まる
            int inputNum = 99;

            //入力された時攻撃ボタンを押してるか
            if ( fire1Key )
            {


                inputNum = 1;
                nextType = _controller.State.IsGrounded ? ActType.sAttack : ActType.aAttack;

                //空中攻撃終了ならキャンセル
                if ( isAirEnd && nextType == ActType.aAttack )
                {
                    inputNum = 99;
                    nextType = ActType.noAttack;
                }
            }
            else if ( fire2Key )
            {
                inputNum = 2;
                nextType = _controller.State.IsGrounded ? ActType.bAttack : ActType.fAttack;
            }
            else if ( artsKey )
            {
                inputNum = 3;
                nextType = ActType.arts;
            }

            //攻撃しないか別の攻撃をするのだったら番号を戻す
            if ( nextType == ActType.noAttack || nextType != nowAction.nowType )
            {
                nowAction.isComboEnd = true;
            }


            //攻撃終了
            AttackEnd();

            //攻撃入力あったなら
            if ( inputNum < 4 )
            {
                //入力に従って次の情報をセット
                InitialInput(inputNum);
                //まだ何も入力がないなら
                if ( nowAction.inputData.motionInput == Equip.InputType.non )
                {
                    return;
                }
                //決まったなら初期化を入れる
                else
                {
                    //移動を停止
                    _characterHorizontalMovement.SetHorizontalMove(0);
                    _controller.SetForce(Vector2.zero);


                    //ノーマルじゃないなら
                    if ( nowAction.inputData.motionInput != Equip.InputType.normal )
                    {
                        //チャージ時間を現在にして
                        nowAction.chargeStartTime = GManager.instance.nowTime;

                        //ステートをチャージ段階に
                        nowAction.stateNum = 1;

                        //チャージ開始
                        //引数には魔法かどうかを
                        ChargeStart(nowAction.nowType == ActType.magic);
                    }
                }


            }

        }

        /// <summary>
        /// 何かボタンがいじられているかを調べる
        /// </summary>
        /// <returns></returns>
        bool AnyKey()
        {

            if ( _inputManager.CheckButtonUsing() )
            {
                return true;
            }
            else
            {
                return (_horizontalInput != 0 || _verticalInput != 0);

            }
        }


        /// <summary>
        /// 落下後の地面検査するかこれで
        /// </summary>
        async UniTaskVoid FallAttackEndWait()
        {
            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;


            //地面につくまで待つ
            //あるいは2.5秒立つまで待つ
            await UniTask.WhenAny(UniTask.Delay(TimeSpan.FromSeconds(2.5)),
                UniTask.WaitUntil(() => _controller.State.IsGrounded));

            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }

            //地面ついてたら着地モーション
            if ( _controller.State.IsGrounded )
            {
                //0.01秒待つ
                await UniTask.Delay(10);

                // キャンセルされてたら戻る。
                if ( token.IsCancellationRequested )
                {
                    return;
                }

                //即座にコンボ（着地モーション）攻撃実行
                //着地で衝撃波が出る類ならこうなる
                ComboAttackJudge();

            }

            //そうでないなら（着地出来ずなら）ふつうにそのまま終了
            //コンボや着地モーションも中断？
            else
            {
                nowAction.isComboEnd = true;
                AttackEnd();
            }

        }








        #endregion


        #region 攻撃終了・中断処理



        /// <summary>
        ///  攻撃終了メソッド
        /// </summary>
        /// <param name="conti"></param>
        public void AttackEnd()
        {


            //状態をもとに戻す
            if ( _condition.CurrentState != CharacterStates.CharacterConditions.Stunned )
            {
                if ( _controller.State.IsGrounded )
                {

                    _movement.ChangeState(CharacterStates.MovementStates.Idle);
                }
                else
                {
                    _movement.ChangeState(CharacterStates.MovementStates.Falling);
                }
                _condition.ChangeState(CharacterStates.CharacterConditions.Normal);
            }

            //ターゲットを削除
            targetEnemy = null;

            //落下攻撃なら
            if ( nowAction.isFall )
            {
                nowAction.isFall = false;
                //ヘルス状態を戻す
                AttackHealthStateEnd();
                //重力も戻す
                _controller.DefaultParameters.Gravity = -GManager.instance.gameData.firstGravity;
            }

            //コンボ終了だったら番号を戻す

            //入力リセット
            InputReset(nowAction.isComboEnd);


            nowAction.isComboEnd = false;

            //当たり判定の記録をリセット
            _damage.CollidRestoreResset();

            //遅れてスタミナ回復
            StaminaRecover().Forget();

        }



        /// <summary>
        /// 攻撃後少し待ってスタミナ回復開始
        /// </summary>
        /// <returns></returns>
        async UniTaskVoid StaminaRecover()
        {
            // 現時点でのトークンを保存。
            CancellationToken token = AttackToken.Token;


            await UniTask.Delay(1000);

            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }
            //連続攻撃してなければ回復開始
            if ( _movement.CurrentState != CharacterStates.MovementStates.Attack )
            {
                GManager.instance.nowCondition.isStUse = false;
            }
        }


        /// <summary>
        /// 主にスタンした時に呼ばれるメソッド
        /// 行動中止処理を入れておく
        /// キャンセルトークンも使うか
        /// </summary>
        public override void StopAbillity()
        {

            //チャージ終了
            ChargeEnd(true);

            //重力を有効化
            _controller.GravityActive(true);



            //ヘルスをもとに戻す
            AttackHealthStateEnd();


            //落下攻撃なら
            if ( nowAction.isFall )
            {
                nowAction.isFall = false;
                //ヘルス状態を戻す
                AttackHealthStateEnd();
                //重力も戻す
                _controller.DefaultParameters.Gravity = -GManager.instance.gameData.firstGravity;
            }

            //入力リセット
            InputReset(true);

            nowAction.isComboEnd = false;

            AttackToken.Cancel();
            AttackToken = new CancellationTokenSource();
        }





        #endregion






    }










}