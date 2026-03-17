using System.Collections.Generic;
using UnityEngine;

namespace MoreMountains.CorgiEngine
{
    ///このクラスでプレイヤーは攻撃時に前進する
    ///攻撃開始から何秒後に何秒間移動するみたいな感じ
    ///レイキャストで障害物を確認したら止まるか敵を押す
    ///外部から移動距離を指定したりもできる（ロックオンで敵の位置拾う）
    ///接触時は停止か押して前進かの二つを選べる
    ///押して前進中は速度が一定になり、予定していた時間で止まる
    ///落下攻撃用に距離無制限で進み続けるようなオプションも
    ///コライダーのサイズをレイキャスト距離に足せ
    ///ガードはいらない
    ///ルートオブジェクトにつけて
    ///簡単に言うと攻撃中前進する機能、衝突する状態で他人や自分を停止させるコードがある、といった感じ
    ///
    /// これ壁検出はコーギーエンジンで見つけたやつでいいだろ
    /// 
    /// transform操作関連はブラッシュアップできる
    /// 
    /// 機能をブラッシュアップしよう。
    /// 主な機能は二つ
    /// ・攻撃移動をする
    /// ・ヒット時の処理をする
    /// ・運搬する。
    /// ・拘束する。
    /// 
    /// 実装は触れた相手の運搬判定メソッドを起動して、運搬判定成功したらAttackMoveを返して、受け取ってリストに入れる。
    /// 拘束も同じ
    /// 運搬は毎フレーム進んだベクターを渡して、それに従って動く
    /// 
    /// 仕様、現在運搬や拘束ができない敵（攻撃中の敵など）に当たったら通過していく？　それか停止
    /// このクラスはガードしてる敵に歩いててぶつかったりしたら止まる機能も含んでるよね
    /// <summary>
    /// このクラスでプレイヤーは攻撃時に前進する
    /// 攻撃開始から何秒後に何秒間移動するみたいな感じ
    /// 
    /// 
    /// 機能をまとめよう
    /// ・移動開始
    /// ・移動
    /// ・接触調査
    /// ・壁、またはガード、攻撃、ガード移動、してる敵にぶつかると停止する。
    /// ・接触時に敵を運搬。正確には敵を拘束して、自分が動いた時はそれと同じだけ動かす機能？
    /// ・それか、拘束用の空のオブジェクトを作ってそれを追うようにする？　それなら掴み上げたりとかできる？　ならもう子オブジェクトにするで良くね？
    /// ・運搬は怯んでる敵だけ押せるようにする。それ以外なら止まる。掴み技は確定怯み？
    /// 
    /// </summary>
    [AddComponentMenu("Corgi Engine/Character/Abilities/MyAttackMove")]
    public class MyAttackMove : MyAbilityBase
    {
        public override string HelpBoxText() { return "This component allows your _character to push blocks. This is not a mandatory component, it will just override CorgiController push settings, and allow you to have a dedicated push animation."; }

        /// <summary>
        /// 攻撃移動の状態を表す列挙型。
        /// 
        /// </summary>
        enum RushState
        {
            停止,
            待機,
            移動
        }

        /// <summary>
        /// 攻撃移動中、敵と接触した際の行動。
        /// </summary>
        public enum AttackContactType
        {
            通過,//通り抜ける。接触ない
            停止,//敵と接触したら止まる
            押す//敵を押して進んでいく
        }


        /// <summary>
        /// 押されている最中のみ使うフラグ。<br></br>
        /// レイを食らったところでこのフラグを真にする。<br></br>
        /// そして押す相手リストの中のキャラに移動メソッドを呼び出す。<br></br>
        /// その時、このフラグが真なら進んでこのフラグが偽なら押すリストから抜けて、さらにロック解除。<br></br>
        /// 押された後はまた偽にして、また移動メソッドが呼ばれるのを待つ。
        /// でもその時このフラグがどうなってるかで振る舞いが変わる。
        /// </summary>
        public bool isContactNow;

        /// the length of the raycast used to detect if we're colliding with a pushable object. Increase this if your animation is flickering.
        [Tooltip("押せるオブジェクトと衝突しているかどうかを検出するために使用するレイキャストの長さ。アニメーションがちらつく場合は、この値を大きくしてください。")]
        [SerializeField]
        float detectionRaycastLength = 5f;

        /// <summary>
        /// レイキャストに使用するフィルター。
        /// </summary>
        [SerializeField]
        ContactFilter2D rayFilter;


        ///外部から設定するやつ
        #region
        /// <summary>
        /// 突進中敵とぶつかった時どんな対応をするか
        /// </summary>
        [HideInInspector]
        private AttackContactType contactType;

        /// <summary>
        /// 何秒後に移動開始するか
        /// </summary>
        [HideInInspector]
        private float moveStartTime;
        /// <summary>
        /// 何秒間移動するか
        /// </summary>
        [HideInInspector]
        private float moveDuration;


        /// <summary>
        /// 落下攻撃で無制限に衝突と前進するフラグ
        /// 距離制限なく進む
        /// </summary>
        bool fallMove;


        /// <summary>
        /// 何秒かけてどれくらいの距離移動するかで割り出した速度
        /// </summary>
        float moveSpeed;

        /// <summary>
        /// 背中攻撃かどうか
        /// </summary>
        bool backAttack;

        // 衝突した相手に関する処理がある場合はレイキャストを飛ばす。
        // 入力で方向変化とかもあるかもしれんしこれは毎回入れる
        // でも攻撃移動開始時に入れてFlipイベントで逆転とかもありかも。
        //条件判断減らしたいな
        Vector3 raycastDirection;

        /// <summary>
        /// 状態に応じてレイキャストの方向を返すプロパティ
        /// </summary>
        private Vector3 RaycastDirection
        {
            get
            {
                if ( _controller.Speed.x != 0 )
                {
                    return (_controller.Speed.x > 0) ? Vector3.right : Vector3.left;
                }
                else
                {
                    return (_character.IsFacingRight) ? Vector3.right : Vector3.left;
                }
            }
        }
        #endregion


        /// <summary>
        /// 移動時間計測
        /// この時間が経過するか、
        /// </summary>
        float moveTime;

        /// <summary>
        /// 現在の突進の状態
        /// </summary>
        RushState nowState;

        /// <summary>
        /// 攻撃移動で衝突するオブジェクト。
        /// </summary>
        [SerializeField]
        LayerMask hitObjectMask;


        /// <summary>
        /// 押す対象のゲームオブジェクト。
        /// 
        /// </summary>
        List<GameObject> pushObjects = new List<GameObject>();


        /// <summary>
        /// 押せる相手は最大五体迄ってことで配列制限。
        /// レイキャストの結果を収納する。
        /// </summary>
        RaycastHit2D[] hitResult = new RaycastHit2D[5];

        /// <summary>
        /// 運搬時に利用する、前回運搬時の自分の位置。
        /// この差分で敵を動かす。
        /// </summary>
        private Vector2 lastPosition;

        /// <summary>
        /// 自分をロックしてるオブジェクト
        /// ガードや攻撃中の敵に触れるとロックされてしまう。
        /// </summary>
        private ControllAbillity lockObject;




        /// <summary>
        /// On Start(), we initialize our various flags
        /// </summary>
        protected override void Initialization()
        {
            base.Initialization();
            //	HitObjectMask |= _controller.PlatformMask;

            rayFilter = new ContactFilter2D();
            rayFilter.layerMask = hitObjectMask;
            // トリガー状態のコライダーも検出する。
            rayFilter.useTriggers = true;

        }

        /// <summary>
        /// Every frame we override parameters if needed and cast a ray to see if we're actually pushing anything
        /// </summary>
        public override void ProcessAbility()
        {
            base.ProcessAbility();

            if ( !AbilityAuthorized )
            {
                return;
            }

            // 攻撃中なら
            if ( _movement.CurrentState == CharacterStates.MovementStates.Attack )
            {
                // 衝突してるかフラグを取得する。
                bool isCollide = CollisionStopCheckForAttackMove();
                //突進機能部分
                AttackRush(isCollide);
            }

            // 攻撃してないならガードを監視してロックする。
            else
            {
                // ガード時のみ敵をロック
                MoveLockOnGuard();

            }

            // 常に、ロックされてる時のみ終わりを監視
            MoveLockEndCheck();

        }

        // 振り向いた時レイキャストと移動方向を変化させる。
        public override void Flip()
        {
            base.Flip();

        }






        /// <summary>
        /// レイキャストを使用して、ヒット情報を更新する。
        /// </summary>
        /// <returns></returns>
        int RayCastUse()
        {

            //自分のコライダーの先端から光線を飛ばしてる
            Vector3 raycastOrigin = _controller.ColliderCenterPosition + RaycastDirection * (_controller.Width() / 2);

            // we cast our ray to see if we're hitting something
            //何か当たってるか確認するため光線を当てる
            return Physics2D.Raycast(raycastOrigin, RaycastDirection, rayFilter, hitResult);
        }




        #region 攻撃移動関連メソッド

        /// <summary>
        ///  突進開始
        /// 必要な情報を初期化
        /// 外部から呼び出す
        /// 移動する距離は計算に使う
        /// 何秒かけてどれくらいの距離移動するか、でだいたいの速度を出せる
        /// </summary>
        /// <param name="duration">移動する時間の長さ</param>
        /// <param name="distance">移動する距離。ロックオンした敵との距離を入れてもいい</param>
        /// <param name="type">敵と接触した時の対応</param>
        /// <param name="infinityMove">落下攻撃で無限に横移動し続けるかどうか</param>
        /// <param name="startTime">移動を開始するまでの時間</param>
        public void RushStart(float duration, float distance, AttackContactType type, bool fallAttack = false, float startTime = 0, bool isBack = false)
        {
            //動かないなら戻る
            if ( distance == 0 )
            {
                return;
            }

            backAttack = isBack;
            if ( !fallAttack )
            {
                nowState = RushState.待機;
                moveDuration = duration;
                moveStartTime = startTime;
                contactType = type;
                moveSpeed = distance / moveDuration;
                fallMove = false;
            }
            else
            {
                nowState = RushState.待機;
                moveDuration = duration;
                moveStartTime = startTime;
                contactType = AttackContactType.通過;
                moveSpeed = distance / moveDuration;
                fallMove = true;
            }

            // 右向いてるなら右、向いてる方向へ動く。でももしバックアタックならその限りではない。
            int direction = _character.IsFacingRight ? 1 : -1;

            if ( backAttack )
            {
                _controller.SetHorizontalForce(moveSpeed * -direction); //* direction
            }
            else
            {
                _controller.SetHorizontalForce(moveSpeed * direction); //* direction
            }

            // もし押すなら現在の位置で差分求める用のベクターを書き変える
            if ( contactType == AttackContactType.押す )
            {
                lastPosition = _character.characterPosition;
            }
        }

        /// <summary>
        /// レイキャストを飛ばして衝突しているかどうか確認する。
        /// 進行方向と現在のスピードで左右のどちらに出すかを確認
        /// 衝突したら停止か押すかを選ぶ
        /// まずはレイキャストで衝突確認する作業から
        /// 
        /// 壁とキャラは別々に検知しよう
        /// というか壁は普通にコーギーエンジンが検知してそう
        /// そのへん確認するか。
        /// 
        /// ここでの処理は変えるよ
        /// 仮に押せる相手（怯み中とか）の敵がいたら押していく
        /// 押されている側は移動距離やベクトルと同じ方向にレイキャスト飛ばして、壁があったら押されるのをやめる。押されるのは自分で解除する。
        /// 押してる側は壁にぶつかれば勝手に止まるはず。だってSethorizontalで動いてるし
        /// 
        /// また、通常時停止はガード中の自分がレイキャストで触れた相手のhorizontalにロックを仕掛ける感じで
        /// ロックされたキャラは自分の状態を判断して、特定方向への移動を禁止する。
        /// ロックしたオブジェクトはハッシュに記憶しておいて、再度触れることができなかったらロック解除。
        /// あるいは禁止されてない方に動いたらロック解除
        /// すでにロックされてるオブジェクトに対してもロックはできるが、移動クラスの方で弾かれる。
        /// 
        /// 
        /// 
        /// </summary>
        private bool CollisionStopCheckForAttackMove()
        {

            // 右に移動中に右に壁接触してるか、左に移動中に左に…ってことだな
            if ( (_controller.Speed.x > 0 && _controller.State.IsCollidingRight) ||
                (_controller.Speed.x < 0 && _controller.State.IsCollidingLeft) )
            {
                return true;
            }


            //通過突進の時は処理を行わない
            if ( contactType == AttackContactType.通過 )
            {
                return false;
            }
            else if ( contactType == AttackContactType.停止 )
            {
                // ここからは運搬の処理。
                //何か当たってるか確認するため光線を当てる
                int hitNum = RayCastUse();


                //何かしらにヒットしたら移動禁止フラグを立てていく。
                if ( hitNum > 0 )
                {

                    for ( int i = 0; i < hitNum; i++ )
                    {

                        MyAttackMove contactObj = _character.characterController.GetOtherCharacterController(hitResult[i].collider.gameObject).ReturnAttackMove();

                        if ( contactObj == null )
                        {
                            continue;
                        }

                        contactObj.MoveThroughLock(_character.characterController);
                    }
                }

                //何か当たってるかを返す
                return hitNum > 0;
            }

            // ここからは運搬の処理。
            //何か当たってるか確認するため光線を当てる
            int hitCount = RayCastUse();


            //何かしらにヒットしたら
            if ( hitCount > 0 )
            {
                GameObject hitTarget;

                for ( int i = 0; i < hitCount; i++ )
                {
                    hitTarget = hitResult[i].collider.gameObject;

                    MyAttackMove contactObj = _character.characterController.GetOtherCharacterController(hitTarget).ReturnAttackMove();

                    if ( contactObj == null )
                    {
                        continue;
                    }

                    // もし衝突対象が衝突対象リストに含まれていないならリストに追加
                    if ( !pushObjects.Contains(hitTarget) )
                    {

                        // 押せる状態なら押すリストへ
                        if ( contactObj.PushableCheck() )
                        {
                            pushObjects.Add(hitTarget);
                        }

                        // 押せる状態でないならループスキップ
                        // さらに通り抜け禁止の移動ロックをかける。
                        // 押す対象ではないキャラクターなので。
                        else
                        {
                            contactObj.MoveThroughLock(_character.characterController);
                            continue;
                        }

                    }

                    // 衝突を真に。
                    contactObj.isContactNow = true;
                }
            }

            int pushObjCount = pushObjects.Count;

            if ( pushObjCount == 0 )
            {
                // 差分位置を上書き。
                lastPosition = _character.characterPosition;
                return true;
            }

            // 移動方向は運搬時の位置と前回運搬時の位置との差分。
            // 移動距離は先に計算して使いまわす。
            Vector2 moveDirection = _character.characterPosition - lastPosition;
            float distance = moveDirection.magnitude;

            // 運搬が成功したかどうか
            bool isSuccess = false;

            for ( int i = 0; i < pushObjCount; i++ )
            {
                // オブジェクトを移動させる。
                bool isResult = _character.characterController.GetOtherCharacterController(pushObjects[i]).ReturnAttackMove().PushTranslate(moveDirection, distance);

                // 移動失敗なら削除
                if ( isResult == false )
                {
                    pushObjects.Remove(pushObjects[i]);
                }
                // 一人でも運べれば成功
                else
                {
                    isSuccess = true;
                }
            }

            // 運搬が成功しなかった場合は障害物アリとする。
            return !isSuccess;
        }


        /// <summary>
        /// 攻撃移動の実行メソッド。<br></br>
        /// 毎フレーム実行して動く。
        /// </summary>
        void AttackRush(bool isCollide)
        {

            if ( nowState != RushState.停止 )
            {

                moveTime += _controller.DeltaTime;

                //移動開始時間が来たら移動開始
                if ( moveTime >= moveStartTime && nowState == RushState.待機 )
                {

                    nowState = RushState.移動;
                    moveTime = 0;
                }
                else if ( nowState == RushState.移動 )
                {

                    // 移動終了条件満たしてるかどうかを見る
                    if ( CheckRushStop(isCollide) )
                    {
                        StopRush();
                    }

                    //まだ突進中の場合の処理
                    else
                    {
                        // オスで衝突の時は止まって移動スキップ。
                        if ( contactType == AttackContactType.押す && isCollide )
                        {
                            _controller.SetHorizontalForce(0);
                            return;
                        }

                        //ここから移動処理
                        // 右向いてるなら右、向いてる方向へ動く。でももしバックアタックならその限りではない。
                        int direction = _character.IsFacingRight ? 1 : -1;

                        if ( backAttack )
                        {
                            _controller.SetHorizontalForce(moveSpeed * -direction); //* direction
                        }
                        else
                        {
                            _controller.SetHorizontalForce(moveSpeed * direction); //* direction
                        }


                    }
                }
            }
        }

        /// <summary>
        /// もし真なら攻撃移動を停止する。
        /// </summary>
        /// <returns></returns>
        bool CheckRushStop(bool isCollide)
        {
            // 押す時以外で衝突してるなら移動終了
            // 通過で衝突対象になるのは普通に壁だけなので壁に当たったならやめよ
            if ( (isCollide) && contactType != AttackContactType.押す )
            {
                return true;
            }

            //落下攻撃中じゃないとき、移動する期間超えたら停止
            //落下攻撃中で地面についても終わり
            if ( (!fallMove && moveTime >= moveDuration) || (fallMove && _controller.State.IsGrounded) )
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 戦闘移動終了。停止。<br></br>
        /// ここに子オブジェクト解除も入れるか？
        /// </summary>
        protected virtual void StopRush()
        {
            _controller.SetHorizontalForce(0);
            nowState = RushState.停止;
            moveTime = 0;
        }

        #endregion 攻撃移動関連メソッド


        #region 運搬移動させる関連のメソッド

        /// <summary>
        /// 現在このオブジェクトが押せるかどうかを返すメソッド<br></br>
        /// 押せるなら真を返し、押すリストに加える
        /// </summary>
        /// <returns></returns>
        public bool PushableCheck()
        {
            bool isPushable = _health._defData.CheckFeature(DefenseData.DefState.運搬可能);

            // 押せるなら全ロックにして運搬スタン開始。
            if ( isPushable )
            {
                _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.全ロック);
                _character.characterController._wakeup.StartStunn(MyWakeUp.StunnType.bind);
            }

            return isPushable;
        }

        /// <summary>
        /// 押しているオブジェクトに対して移動を命じるメソッド。<br></br>
        /// 現在の状態に合わせて異動に応じるか決める。<br></br>
        /// 応じなかった場合は押すリストから消す。
        /// </summary>
        /// <param name="movedirection">移動ベクトル</param>
        /// <param name="distance">移動距離</param>
        /// <returns>移動に応じたかどうか。真なら応じた</returns>
        public bool PushTranslate(in Vector2 movedirection, float distance)
        {
            // もしもう接触してないか、運搬不能ならfalseを返す。
            if ( !isContactNow || !_health._defData.CheckFeature(DefenseData.DefState.運搬可能) )
            {
                PushEnd();
                return false;
            }

            // 接触を初期化
            isContactNow = false;

            // 移動先で地面に当たるかを考える。
            bool isHit = Physics2D.Raycast(_character.characterPosition, movedirection, distance + _controller.Width() / 2, _controller.PlatformMask);

            // もしヒットするなら移動せずに戻る
            if ( isHit )
            {
                PushEnd();
                return false;
            }

            // 移動処理
            transform.Translate(movedirection);

            return true;
        }

        /// <summary>
        /// 押し終わった時の処理。
        /// ロック解除して怯みを始動する。
        /// </summary>
        private void PushEnd()
        {
            // ロック解除して怯みへ派生
            _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし);
            _character.characterController._wakeup.StartStunn(MyWakeUp.StunnType.Falter);
        }


        #endregion 運搬移動させる関連のメソッド

        #region ガード中など、攻撃中の時に相手の通り抜けを禁じるメソッド

        /// <summary>
        /// 移動ロックをする。
        /// 現在自分をロックしているオブジェクトを受け取り、距離が離れたらロック解除
        /// ロック方向はロック時の位置関係で決める
        /// ロック解除はUpdateでロックオブジェクトある時の位置判定で決めようね
        /// チェック時にもしロック距離内で、位置入れ替わってたらロック方向も変える
        /// 
        /// そういえばガード時とかは特に自分も止まらないとダメだよね
        /// 自分では呼び出さないよ
        /// </summary>
        public void MoveThroughLock(ControllAbillity setController)
        {
            if ( lockObject == null )
            {
                // ロックオブジェクトを設定して位置関係に基づいてロック設定
                lockObject = setController;

                // 相手の方が自分より右にいるなら右に移動を禁じる
                bool isRightLock = _character.characterPosition.x < setController.ReturnPosition().x;

                // 右ロックかどうかで選択
                if ( isRightLock )
                {
                    _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.右ロック, true);
                }
                else
                {
                    _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.左ロック, true);
                }
            }
            // 既に設定されているなら、より距離が近ければ入れ替える。
            else
            {
                float nowDistance = Vector2.SqrMagnitude(lockObject.ReturnPosition() - _character.characterPosition);
                float compareDistance = Vector2.SqrMagnitude(setController.ReturnPosition() - _character.characterPosition);

                // 新たなやつの方が近いなら
                if ( compareDistance < nowDistance )
                {
                    // ロックオブジェクトを設定して位置関係に基づいてロック設定
                    lockObject = setController;

                    // 相手の方が自分より右にいるなら右に移動を禁じる
                    bool isRightLock = _character.characterPosition.x < setController.ReturnPosition().x;

                    // 右ロックかどうかで選択
                    if ( isRightLock )
                    {
                        _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.右ロック, true);
                    }
                    else
                    {
                        _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.左ロック, true);
                    }
                }
            }
        }

        /// <summary>
        /// ガード時に相手の動きを止め、ガード移動時のみ自分の動きも停止させる。
        /// 向いてる方向に動けなくする
        /// 非攻撃時毎フレーム実行する。
        /// </summary>
        void MoveLockOnGuard()
        {

            // ガード時でないなら戻る。
            if ( _movement.CurrentState != CharacterStates.MovementStates.GuardMove && _movement.CurrentState != CharacterStates.MovementStates.Guard )
            {
                // セーフティでロック解除
                _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし, true);
                return;
            }

            // ここからは運搬の処理。
            //何か当たってるか確認するため光線を当てる
            int hitNum = RayCastUse();


            //何かしらにヒットしたら移動禁止フラグを立てていく。
            if ( hitNum > 0 )
            {

                for ( int i = 0; i < hitNum; i++ )
                {

                    MyAttackMove contactObj = _character.characterController.GetOtherCharacterController(hitResult[i].collider.gameObject).ReturnAttackMove();

                    if ( contactObj == null )
                    {
                        continue;
                    }

                    contactObj.MoveThroughLock(_character.characterController);
                }

                // ガード移動中ならセーフティでロックする。
                if ( _movement.CurrentState != CharacterStates.MovementStates.GuardMove )
                {
                    if ( _character.IsFacingRight )
                    {
                        _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.右ロック, true);
                    }
                    else
                    {
                        _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.左ロック, true);
                    }
                }
            }
            // ガード移動中、ヒットしなければセーフティで解除
            else if ( _movement.CurrentState == CharacterStates.MovementStates.GuardMove )
            {
                // セーフティでロック解除
                _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし, true);
            }
        }

        /// <summary>
        /// 移動ロックを解除するかチェックするメソッド
        /// 毎フレーム実行する。
        /// 
        /// 挙動は二つに分かれる。
        /// 攻撃時、ガード時には問答無用でロック解除
        /// それ以外の時、ロックされてるならロック対象の距離見て解除
        /// 
        /// ロックオブジェクトがなければ作動しないようにすれば、他のアビリティのロックを解除する心配はない。
        /// でも一応、全ロックは解除できないフラグをメソッドにたててからここでは解除する。
        /// 左右ロックを解除する機能だからね、本質的に
        /// </summary>
        void MoveLockEndCheck()
        {
            if ( lockObject != null )
            {
                // ガード中か、コンディションがノーマルでなくなったら解除
                if ( _movement.CurrentState == CharacterStates.MovementStates.GuardMove || _movement.CurrentState == CharacterStates.MovementStates.Guard ||
                    _condition.CurrentState != CharacterStates.CharacterConditions.Normal )
                {
                    // セーフティでロックを解除する。
                    lockObject = null;
                    _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし, true);
                }
                else
                {
                    // 差のベクトルを求める
                    Vector2 lockVector = lockObject.ReturnPosition() - _character.characterPosition;

                    // 距離がロック解除基準を超えているなら
                    //基準はコントローラーの幅とレイキャスト距離を足したもの
                    if ( Vector2.SqrMagnitude(lockVector) > Mathf.Pow(detectionRaycastLength + _controller.Width() / 2, 2) )
                    {
                        // セーフティでロックを解除する。
                        lockObject = null;
                        _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.ロックなし, true);
                    }
                    // 入れ替えに備えて再度設定
                    else
                    {
                        // 相手のx座標の方が大きい、つまり相手の方が右にいるなら右移動を禁じる
                        if ( lockVector.x > 0 )
                        {
                            _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.右ロック);
                        }
                        else
                        {
                            _characterHorizontalMovement.MoveLockSet(PlayerHorizontalMove.MoveLockType.左ロック);
                        }
                    }
                }
            }
        }


        #endregion　ガード中など、相手の通り抜けを禁じるメソッド
    }

}
