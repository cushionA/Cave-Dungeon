using Cysharp.Threading.Tasks;
using MoreMountains.CorgiEngine;
using Sirenix.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using static AttackData;
using static Equip;
using static UnityEngine.InputManagerEntry;

/// <summary>
/// 弾丸が一発以上の時に挙動をコントロールしてくれるやつ
/// あと情報を渡してくれる
/// 位置を整える役目も持つ（この場合最初、弾丸をアクティブ化する前にやる。非アクティブ状態だと座標操作に変な負荷がかからない）
/// 単発の場合これはいらない
/// あくまで複数の弾丸をまとめる処理
/// したがって単発かどうかで弾丸の消滅と呼び出しの処理が変わる？
/// 
/// 求められる機能
/// ・弾丸が必要とする情報を受け取って保持する機能（情報は参照たどって弾丸の方が取りに来る）　×　controllアビリティと繋がる
/// ・弾丸を順番にアクティブ化していく機能。（個別に秒数決めれる）　〇
/// ・アクティブ化と同時に親子関係を切り離す　〇
/// ・参照たどって弾丸が終了呼び出ししてきたら非アクティブ化させる。全部非アクティブ化したらもうプールに帰る　〇
/// ・ターゲットが健在であるかをこいつが検査する機能。検査系は一括で一度だけ　×
/// ・ターゲットの位置を更新し続ける機能　×
/// ・魔法がヒットした時、ヒット報告を受けてヒット回復などの効果を受ける
/// 
/// 問題
/// ・マズルフラッシュ系はどうするか（各魔法がアクティブ時に自分でこいつのatefを通じて呼び出す？）
/// ・単発は処理分けるか？（分けない。非アクティブの時に親子関係の操作をおこなえばいい）
/// ・別々の挙動の弾丸を入れるには？　→バレットコントローラーに魔法をつけておく。弾丸の挙動データと魔法データを分ける？
/// ・poolからの呼び出しがparticleじゃなくなる。着弾エフェクトと子魔法は分けるか？
/// ・プールへのエフェクト追加どうするか（弾丸情報みたいにして配列でまとめるか。魔法の弾丸情報のどれを参照するかみたいなのを番号で持たせとけ）
/// 
/// 
/// ・アイテムとしてのデータ
/// ・挙動データ（速度、追尾形式、弾丸の生存時間）
/// ・攻撃力データ（ヒット回数、モーション値、攻撃力）　弾丸所属（攻撃力だけ魔法から持ってくる）
/// ・エフェクトデータ（フラッシュエフェクト、着弾エフェクト、魔法エフェクト）　弾丸所属（フラッシュエフェクトと着弾エフェクトは）
/// ・詠唱データ（詠唱時間、モーション指定）　魔法所属
/// ・バレットコントローラープレハブへの参照　魔法所属
/// 
/// 
/// </summary>
public class BulletController : MonoBehaviour
{

    /// <summary>
    /// 弾丸をコントロールするのに必要な情報
    /// </summary>
    public struct BulletControllData
    {

        /// <summary>
        /// 生成するまでの時間
        /// </summary>
        [Header("生成されるまでの時間")]
        public float emitSecond;

        [Header("初期位置")]
        public Vector2 firstPosition;

        [Header("弾丸オブジェクト")]
        public FireBullet bullet;

    }


    #region 弾丸管理フィールド

    /// <summary>
    /// 最初に初期化する
    /// 弾丸数
    /// </summary>
    int bulletCount;

    /// <summary>
    /// 非アクティブになったオブジェクトを数える
    /// 全部非アクティブになったらプールに帰る
    /// </summary>
    int disenableCount;


    /// <summary>
    /// 弾丸の配列
    /// これらを起動する
    /// </summary>
    [SerializeField]
    BulletControllData[] bullets;


    /// <summary>
    /// エフェクトコントローラー
    /// </summary>
    [HideInInspector]
    public AtEffectCon atEf;

    /// <summary>
    /// コントローラー
    /// </summary>
    [HideInInspector]
    public ControllAbillity userController;



    /// <summary>
    /// ターゲットオブジェクト
    /// </summary>
    public GameObject target;


    /// <summary>
    /// ターゲットの位置
    /// </summary>
    public Vector2 targetPoint;

    /// <summary>
    /// 最初の弾丸の角度
    /// 必要に応じて計算して出す
    /// </summary>
    public float firstAngle;



    /// <summary>
    /// 弾丸に一つのダメージスクリプト
    /// </summary>
    public BulletDamageOntouch _damage;


    /// <summary>
    /// 魔法データ
    /// 弾道計算までここでやるなら必要
    /// </summary>
    Magic bulletData;


    /// <summary>
    /// 弾丸起動時のこのコントローラーの位置
    /// アングルやRainの場合初期角度計算で使う
    /// </summary>
    Vector2 controllerPosition;

    #endregion



    #region Job関連

    TransformAccessArray myTransform;


    /// <summary>
    /// 弾丸の弾道計算の結果
    /// </summary>
    NativeArray<Vector3> result;

    /// <summary>
    /// 弾丸がアクティブかどうか
    /// </summary>
    NativeArray<bool> bulletActive;

    /// <summary>
    /// いつ弾丸が生成されたかという時間
    /// </summary>
    NativeArray<float> spornTime;


    /// <summary>
    /// すでに弾丸が一個でもアクティブになったか
    /// これが真になるまではジョブは動かない
    /// </summary>
    bool alreadyStart;

    #endregion


    private void Awake()
    {
        bulletCount = bullets.Length;


        //ダメージスクリプトにコントローラーを渡す
        //ならもうバフ計算とかいらないかこれ
        //いやいるね、魔法倍率とかは独自処理だからさ
        //でもこれでヒットやキルを報告できる、敵味方に
        //data.bullet.GetDamageScript().BulletControllerSetting(userController);
        _damage.BulletControllerSetting(userController);
    }

    private void FixedUpdate()
    {
        //ターゲットがいるなら位置を取得
        if ( target != null )
        {
            targetPoint = GManager.instance.GetControllAbilityByObject(target).ReturnPosition();
        }

        //弾丸のジョブシステム使用
        BulletMoveController();
    }

    #region 弾丸とのやり取りに使う

    /// <summary>
    /// ターゲットがいるかのチェック
    /// </summary>
    /// <returns></returns>
    public bool TargetExitCheck()
    {
        return target != null;
    }


    /// <summary>
    /// 管理下の弾丸が子弾を出す時にターゲットへの最初の角度を計算して渡してやる
    /// </summary>
    /// <returns></returns>
    public float FirstAngleCalcu(Vector3 position)
    {
        Vector3 direction = (Vector3)targetPoint - position;
        return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

    }


    /// <summary>
    /// コントローラーからその魔法に何倍の倍率がかかるかという情報を引きずり出す
    /// コントローラーに魔法の属性とかを読み込ませてな
    /// たとえば魔物系統の魔法1.3倍とか
    /// </summary>
    /// <returns></returns>
    public float GetMagicMultipler(Magic useMagi)
    {

        return 1;
    }


    /// <summary>
    /// 弾丸がヒットした時に呼ぶ処理
    /// 返り値でちゃんとヒットしたかを返す
    /// </summary>
    public bool HitReport(Collider2D collider)
    {
        return _damage.BulletHit(collider);
    }


    #endregion


    ///
    /// 考えること
    /// ・ガードヒット時は状態をノーマルじゃなくするか
    /// ・魔法へダメージ倍率ではない、攻撃力増加や倍変化処理はどう乗せるか
    /// 
    /// ///


    #region 使用者情報取得

    /// <summary>
    /// ここから弾丸発射が始動するぽいな
    /// 名前ちょっとわかりにくいかも
    /// コントローラーに使用者の情報を入れていく
    /// 攻撃倍率に魔術の触媒の倍率をかける
    /// また、特定の魔術の倍率を強化する効果があるかを見て、これも倍率にかけていく
    /// 初期化終了したら弾丸を呼ぶよ
    /// </summary>
    /// <param name="controller"></param>
    /// <param name="atEfAbillity"></param>
    /// <param name="magicMultipler"></param>
    public void UserDataSet(ControllAbillity controller, AtEffectCon atEfAbillity, GameObject targetObj)
    {
        atEf = atEfAbillity;
        userController = controller;
        target = targetObj;

        if ( target != null )
        {
            targetPoint = GManager.instance.GetControllAbilityByObject(target).ReturnPosition();
        }



        //ダメージ設定
        DamageSatusSetting();

        //ここから○○魔法は何倍、とか言う特殊バフ計算も入れる？

        //サーチ攻撃なら、弾丸コントローラー自体を移動させる
        //プールに戻せば後始末はしなくていい
        if ( bulletData.BulletFeatureJudge(Magic.BulletType.サーチ攻撃) )
        {
            transform.position = targetPoint;
            controllerPosition = targetPoint;
        }
        //角度設定に使う自座標を入れる
        //これを使うことでローカル座標からワールド座標を割り出せる
        else if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.ANGLE || bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.RAIN )
        {
            controllerPosition = transform.position;
        }

        //初期化したら弾丸生成
        BulletGenerater();
    }

    /// <summary>
    /// 攻撃力バフを反映
    /// </summary>
    /// <returns></returns>
    AttackStatus AtkSetting()
    {


        if ( userController != null )
        {
            AttackStatus atkStatus = bulletData.atkStatus;
            ConditionAndEffectControllAbility.EventType eventType = ConditionAndEffectControllAbility.EventType.攻撃力変動効果;

            //攻撃バフを反映

            if ( atkStatus.phyAtk > 0 )
            {

                atkStatus.phyAtk = userController.conditionController.GetValueData(eventType, 0, atkStatus.phyAtk);

                atkStatus.phyAtk = userController.conditionController.GetValueData(eventType, 1, atkStatus.phyAtk);
                //  atkStatus.Atk += atkStatus.phyAtk;
            }

            if ( atkStatus.fireAtk > 0 )
            {
                atkStatus.fireAtk = userController.conditionController.GetValueData(eventType, 0, atkStatus.fireAtk);
                atkStatus.fireAtk = userController.conditionController.GetValueData(eventType, 5, atkStatus.fireAtk);
                //  atkStatus.Atk += atkStatus.fireAtk;
            }


            if ( atkStatus.thunderAtk > 0 )
            {
                atkStatus.thunderAtk = userController.conditionController.GetValueData(eventType, 0, atkStatus.thunderAtk);
                atkStatus.thunderAtk = userController.conditionController.GetValueData(eventType, 6, atkStatus.thunderAtk);
                //     atkStatus.Atk += atkStatus.thunderAtk;
            }


            if ( atkStatus.holyAtk > 0 )
            {
                atkStatus.holyAtk = userController.conditionController.GetValueData(eventType, 0, atkStatus.holyAtk);
                atkStatus.holyAtk = userController.conditionController.GetValueData(eventType, 7, atkStatus.holyAtk);
                //    atkStatus.Atk += atkStatus.holyAtk;
            }


            if ( atkStatus.darkAtk > 0 )
            {
                atkStatus.darkAtk = userController.conditionController.GetValueData(eventType, 0, atkStatus.darkAtk);
                atkStatus.darkAtk = userController.conditionController.GetValueData(eventType, 8, atkStatus.darkAtk);
                //   atkStatus.Atk += atkStatus.darkAtk;
            }
            return atkStatus;
        }

        return bulletData.atkStatus;


    }

    /// <summary>
    /// ヒット時のダメージ計算に使うデータを入れる
    /// ステータスとバフを親から取得してダメージ計算
    /// </summary>
    /// <param name="isFriend">真なら味方</param>
    void DamageSatusSetting()
    {
        //GManager.instance.isDamage = true;
        //useEquip.hitLimmit--;
        //mValueはモーション値

        //攻撃倍率を入れる
        _damage._attackData.multipler = userController.BulletBuffCalc(bulletData);
        _damage._attackData.attackStatus = AtkSetting();

        //魔法にステータス入れるよ
        _damage._attackData.attackStatus = bulletData.atkStatus;
        _damage._attackData.actionData = bulletData.magicData.actionImfo;
        //状態異常もぶち込む
        _damage._attackData.attackMotionEvent = bulletData.magicData.attackMotionEvent;


    }

    #endregion

    #region　生成・消滅系のメソッド

    /// <summary>
    /// 最初に弾丸を生成していく処理
    /// 弾丸使用者から呼ばれるデータセットの後に起動
    /// </summary>
    void BulletGenerater()
    {


        //弾丸ジョブの初期化
        BulletJobInitialize();


        disenableCount = 0;


        //弾丸の初期角度
        float firstAngle;

        //雨の場合は弾丸コントローラーの位置とターゲット位置で決める
        if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.RAIN )
        {
            firstAngle = bulletData.bulletData._moveSt.angle;

            //相手座標からコントローラー座標を引くことで、ターゲットへのベクトルを獲得
            Vector2 targetVector = targetPoint - controllerPosition;
            //逆正接、Atan2()でベクトルのラジアンを獲得
            //簡単に言うとベクトルを角度に変換してるということ
            //ラジアンとは、半径と、角度が作り出す円弧の長さの比率で角度を表現する単位
            float rad = Mathf.Atan2(targetVector.y, targetVector.x);

            //そしてラジアンを度で表現する角度に変換
            //Mathf.Rad2Degはラジアンを角度に変換するための定数
            //180 / π が中身
            //なお、返ってくる角度の範囲は -180 度から 180度
            float targetAngle = rad * Mathf.Rad2Deg;

            //もし角度の絶対値が90度以上なら
            //あるいはちょうど90度で使用者が左向いてるなら
            if ( MathF.Abs(targetAngle) > 90 || (MathF.Abs(targetAngle) == 90 && !userController.ReturnCharacterFaceRight()) )
            {
                //最初の角度を引いてあげることで敵への角度に初期角度を混ぜ込める
                firstAngle = targetAngle - firstAngle;
            }
            //もし角度の絶対値が90度以下なら
            else if ( MathF.Abs(targetAngle) <= 90 )
            {
                //最初の角度を足してあげることで敵への角度に初期角度を混ぜ込める
                firstAngle = targetAngle - firstAngle;
            }
        }
        //アングルなら、ループで使えるように初期角度を入れておいてやる
        else if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.ANGLE )
        {
            firstAngle = bulletData.bulletData._moveSt.angle;
        }
        //雨でもなく、アングルでもないなら指定された通りの角度を入れるだけ
        else
        {
            //もし弾丸使用者が右を向いてるなら右向きに弾丸を放つ
            //左なら左へ放つ
            //その結果ゼロと180が入れ替わってる
            //仮に初期角度がなくても、左向いて撃てば180度、すなわち左に飛んでいく
            firstAngle = userController.ReturnCharacterFaceRight() ? 0 + bulletData.bulletData._moveSt.angle : 180 - bulletData.bulletData._moveSt.angle;
        }

        //弾丸が沢山あるなら
        if ( bulletCount > 1 )
        {
            for ( int i = 0; i < bulletCount; i++ )
            {
                //弾丸生成処理を振り分けていく
                BulletGenerateExe(bullets[i], i, firstAngle).Forget();
            }
        }
        //弾丸が一つなら
        else
        {
            //弾丸生成処理を振り分けていく
            BulletGenerateExe(bullets[0], 0, firstAngle).Forget();
        }

    }


    /// <summary>
    /// 弾丸生成実行
    /// ポジションや回転の変更を非アクティブで行うことで負荷低減
    /// 初期角度も各弾丸にセット
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    async UniTaskVoid BulletGenerateExe(BulletControllData data, int index, float firstAngle)
    {


        //アングルなら各弾丸で条件をセット
        //自分と敵の位置で角度を決めて、それが90度以上かどうかで左右を決める
        //そして左なら初期角度を引いて、
        if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.ANGLE )
        {
            //相手座標から自座標を引くことで、ターゲットへのベクトルを獲得
            Vector2 targetVector = targetPoint - (controllerPosition + bullets[index].firstPosition);
            //逆正接、Atan2()でベクトルのラジアンを獲得
            //簡単に言うとベクトルを角度に変換してるということ
            //ラジアンとは、半径と、角度が作り出す円弧の長さの比率で角度を表現する単位
            float rad = Mathf.Atan2(targetVector.y, targetVector.x);

            //そしてラジアンを度で表現する角度に変換
            //Mathf.Rad2Degはラジアンを角度に変換するための定数
            //180 / π が中身
            //なお、返ってくる角度の範囲は -180 度から 180度
            float targetAngle = rad * Mathf.Rad2Deg;

            //もし角度の絶対値が90度以上なら
            //あるいはちょうど90度で使用者が左向いてるなら
            if ( MathF.Abs(targetAngle) > 90 || (MathF.Abs(targetAngle) == 90 && !userController.ReturnCharacterFaceRight()) )
            {
                //最初の角度を引いてあげることで敵への角度に初期角度を混ぜ込める
                firstAngle = targetAngle - firstAngle;
            }
            //もし角度の絶対値が90度以下なら
            else if ( MathF.Abs(targetAngle) <= 90 )
            {
                //最初の角度を足してあげることで敵への角度に初期角度を混ぜ込める
                firstAngle = targetAngle - firstAngle;
            }


        }


        //回転とローカルポジに同時に初期値を仕込む
        data.bullet.gameObject.transform.SetLocalPositionAndRotation(data.firstPosition, Quaternion.Euler(0, 0, firstAngle));



        //親子関係を排除
        data.bullet.gameObject.transform.parent = null;

        //ダメージスクリプトにコントローラーを渡す
        //ならもうバフ計算とかいらないかこれ
        //いやいるね、魔法倍率とかは独自処理だからさ
        //でもこれでヒットやキルを報告できる、敵味方に
        //data.bullet.GetDamageScript().BulletControllerSetting(userController);


        //弾丸のジョブデータの設定
        myTransform.Add(data.bullet.gameObject.transform);


        if ( data.emitSecond > 0 )
        {
            //待ち時間あるなら待つ
            await UniTask.Delay(TimeSpan.FromSeconds(data.emitSecond));
        }

        //弾丸を有効化
        data.bullet.gameObject.SetActive(true);

        //ジョブで使う生成時間はここ
        spornTime[index] = GManager.instance.nowTime;

        //今この瞬間まで生成されてないものとする
        //これが真にされた瞬間から動く
        bulletActive[index] = true;

        //ジョブ処理開始
        alreadyStart = true;

        //インデックス設定
        data.bullet.FirstDataSet(index);
    }




    /// <summary>
    /// 弾丸を消去するメソッド
    /// 非アクティブ化後親子関係を戻す
    /// 全て戻したらプールから消える
    /// 
    /// ちなみにマズルフラッシュは時間経過で勝手に消えるはずです
    /// 
    /// 弾丸を戻すたびに全ての弾丸を戻せたかのチェックをする
    /// それぞれの弾丸からこれを呼び出して自分を引数にする
    /// </summary>
    /// <param name="bullet"></param>
    public void BulletClear(GameObject bullet, int bulletNum)
    {
        //すでに非アクティブなら
        //つまりもう消去されてるなら多重呼び出しは拒否
        if ( bullet.activeSelf == false )
        {
            return;
        }

        //弾丸アクティブフラグを折る
        bulletActive[bulletNum] = false;

        //無効化
        bullet.SetActive(false);

        //親オブジェクトに戻る
        bullet.transform.parent = this.transform;

        //カウントをプラスに
        disenableCount++;

        //弾丸の数を帰ってきた弾丸の数が超えた
        //つまり全ての弾丸が返ってきたと思われるときだけ
        //子オブジェクトの数確認をする
        //慎重にやる
        if ( disenableCount >= bulletCount )
        {
            //子オブジェクトにみんな戻っているなら
            if ( transform.childCount >= bulletCount )
            {
                target = null;

                //プールに帰る
                atEf.BulletClear(this.transform, bulletData.mType == Magic.MagicType.WeaponEffect);

                //再度処理をせき止めるフラグを立てる
                alreadyStart = false;

                //弾丸ジョブのメモリ消去
                BulletMemoryRelease();
            }

        }


    }






    #endregion


    #region 弾丸の軌道管理


    /// <summary>
    /// 起動するたびに行う弾丸ジョブの初期化
    /// 
    /// Jobのためにnewしまくってるけど、こいつのメモリはスタックに割り当てられて高速で消えるので気にするな
    /// Transform操作についても同様
    /// Initializeで初期角度つけるとか言うのはなし
    /// 代わりに、弾丸生成した直後からJobを開始しないといけない
    /// これは初期化されるまではDespornBuを真とする？
    /// つまり、弾丸生成の前にJobの初期化しないとダメってことだ
    /// </summary>
    void BulletJobInitialize()
    {
        //回転したりするやつは追尾なので、ストップはほんとに処理しない
        //データ領域を確保もしない
        if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.STOP )
        {
            return;
        }

        // ジョブのためにメモリ領域を確保して渡す
        myTransform = new TransformAccessArray(bulletCount);
        //結果として、移動速度を返してもらう配列
        result = new NativeArray<Vector3>(bulletCount, Allocator.Persistent);


        //弾丸がアクティブになってるかの配列をセットしていく
        bulletActive = new NativeArray<bool>(bulletCount, Allocator.Persistent);


        //弾丸が生成された時間を持つ配列
        //速度や挙動変化に用いる
        spornTime = new NativeArray<float>(bulletCount, Allocator.Persistent);


        /// 弾丸に渡すのは敵位置、自分のトランスフォーム、初期角度
        /// 初期角度はangleでいいじゃん

        /// 
    }


    /// <summary>
    /// 弾丸ごとに今何秒経過したか、とかホーミングの弱まり方とか違うはずなんだよな
    /// 弾丸たちがそれぞれの違いを埋めれるか？
    /// それぞれの生成時間を記録してるのを渡して（それぞれの変数で）
    /// それで現在のステータスを再現
    /// マジで計算速度早いらしいからそれくらいいける
    /// </summary>
    void BulletMoveController()
    {

        //回転したりするやつは追尾なので、ストップはほんとに処理しない
        if ( !alreadyStart || bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.STOP )
        {
            return;
        }

        //ジョブのインスタンスは使いまわしできない。
        //ハンドルも
        FireJobEdit job = new FireJobEdit
        { _status = bulletData.bulletData._moveSt };

        /// やること
        /// ・最初の角度
        /// ・渡した時間で現在のステータスを計算（job内でシングルトンから時間参照していいらしい）
        /// ///


        //ここでジョブ呼び出し


        //ターゲット見失ってるか
        job.targetLost = TargetExitCheck();
        job.posTarget = targetPoint;

        ///nativeArrayを引き渡す
        job.result = result;
        job.bulletActive = bulletActive;
        job.spornTime = spornTime;



        JobHandle _handler = job.Schedule(myTransform);

        //完了待ち
        _handler.Complete();

        for ( int i = 0; i < bulletCount; i++ )
        {
            //弾丸が有効なら
            if ( bulletActive[i] )
            {
                bullets[i].bullet.SetVelocity(result[i]);
            }
        }

    }



    /// <summary>
    /// ジョブのために確保してたメモリ領域を解放
    /// 毎回弾丸を消すたびにやる
    /// </summary>
    void BulletMemoryRelease()
    {

        //回転したりするやつは追尾なので、ストップはほんとに処理しない
        //データ領域を確保もしない
        if ( bulletData.bulletData._moveSt.fireType == Magic.FIREBULLET.STOP )
        {
            return;
        }

        bulletActive.Dispose();
        myTransform.Dispose();
        spornTime.Dispose();
        result.Dispose();
    }



    #endregion

}
