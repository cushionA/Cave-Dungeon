using UnityEngine;
using Cysharp.Threading.Tasks;

using System;
using MoreMountains.CorgiEngine;
using MoreMountains.Tools;
using System.Collections.Generic;
using UnityEngine.Jobs;
using Unity.Jobs;
using Unity.Collections;
using FunkyCode.Buffers;
using static CombatManager;
using static CharacterStatus;
using System.Threading;

/// <summary>
/// 最初にDamageOnTouchと回復量を初期化する。
/// タグ関連の仕様変更
/// 
/// まず攻撃者にかかってるバフを取得
/// そして攻撃力などはステータスから獲得
/// これは通り飛んで当たるだけ
/// 最初のサイズ保存してタイムリセットしてリストもきれいに

/// 最初は追尾弱くして上向きに飛ばす弾丸を徐々に追尾強くしたりすれば曲射とか特殊な軌道の弾丸作れそう
/// </summary>

public class FireBullet : MonoBehaviour
{

    #region 定義


    /// <summary>
    /// 魔法の動作に必要なデータをまとめる
    /// </summary>
    public class MagicActionData
    {

        /// <summary>
        /// 魔法使用者に関する情報
        /// </summary>
        public CharacterIdentify ownerData;


        /// <summary>
        /// ターゲットに関する情報
        /// </summary>
        public CharacterIdentify targetData;


        /// <summary>
        /// ターゲットの位置
        /// </summary>
        public Vector2 targetPosition;




        /// <summary>
        /// データをもとにキャラクターの位置を取得する
        /// </summary>
        /// <param name="data">調査対象のキャラのデータ</param>
        /// <returns></returns>
        public Vector2 GetCharacterPosition(CharacterIdentify data)
        {
            //相手がプレイヤー側なら敵のコンバットマネージャーにいるはず
            if ( data.side == CharacterSide.Player )
            {
                return EnemyManager.instance._targetList[data.obj].condition.targetPosition;
            }
            //敵なら
            else
            {
                return SManager.instance._targetList[data.obj].condition.targetPosition;
            }
        }


        /// <summary>
        /// データをもとにキャラクターのコントロールアビリティを取得
        /// </summary>
        /// <param name="data">調査対象のキャラのデータ</param>
        /// <returns></returns>
        public ControllAbillity GetCharacterController(CharacterIdentify data)
        {
            //相手が敵側なら味方のコンバットマネージャーから呼び出す
            if ( data.side == CharacterSide.Enemy )
            {
                return SManager.instance.GetCharacterControllerByObj(data.obj);
            }
            //敵なら
            else
            {
                return EnemyManager.instance.GetCharacterControllerByObj(data.obj);
            }

        }

    }


    /// <summary>
    /// キャラクターを特定するために必要な要素
    /// 配列で何番目かとIDと陣営
    /// </summary>
    public struct CharacterIdentify
    {

        /// <summary>
        /// キャラの所属
        /// </summary>
        public CharacterSide side;



        /// <summary>
        /// キャラのオブジェクト
        /// </summary>
        public GameObject obj;

    }




    #endregion



    /// 
    /// 改修内容
    /// ・コントローラーから弾丸を生み出す感じに
    /// ・弾丸から子弾に何かを仕込んだりしない
    /// ・コントローラーから出てコントローラーに戻る
    /// 
    /// 
    /// 
    /// ///



    /// <summary>
    /// 魔法の使用者が誰か
    /// ダメージ処理とかで使うかな
    /// 初期化処理でも使うか、使用者の攻撃倍率とか聞かないといけないし
    /// </summary>
    [Header("魔法の使用者が誰か")]
    [SerializeField]
    MasicUser _user;

    [SerializeField]
    MyDamageOntouch _damage;

    public enum MasicUser
    {
        Player,
        Sister,
        Others,
        Child//子どもの弾丸
    }

    // === 外部パラメータ（インスペクタ表示） =====================

    /// <summary>
    /// 魔法データね
    /// </summary>
    public Magic em;




    // === 外部パラメータ ======================================
    [System.NonSerialized] GameObject owner;



    // === 内部パラメータ ======================================


    [SerializeField]
    Rigidbody2D rb;

    /// <summary>
    /// 弾丸のコントローラー
    /// これを通じて色々データを取る
    /// そしてここに帰る
    /// </summary>
    [SerializeField]
    BulletController bController;



    //リセット対象
    #region
    /// <summary>
    /// 弾丸がどれくらいの時間存在してるか計測する。
    /// </summary>
    float fireTime;

    /// <summary>
    /// 弾丸全体のヒット制限
    /// 三回衝突したら消える弾丸とか
    /// 壁への衝突もヒット回数として数えよう
    /// でも、壁へのヒットは一秒に一回とかじゃないとヒット回数加算しないようにしよう
    /// じゃないとすぐ消える
    /// </summary>
    int hitLimit;


    /// <summary>
    /// すでに衝突したもの
    /// </summary>
    List<GameObject> collisionList = new List<GameObject>();

    /// <summary>
    /// 弾丸の消滅時に発動するトークン
    /// </summary>
    CancellationTokenSource magicToken;



    #endregion


    #region Job関連


    /// <summary>
    /// 何番目の弾丸であるか
    /// </summary>
    int index;



    #endregion


    Collider2D col;










    string _healTag;




    /// 
    /// 新弾丸処理
    /// ・敵情報参照確認→情報更新（味方情報参照とその確認は必要な時だけ）
    /// ・弾丸移動
    /// ・衝突時処理
    /// ・その他常時処理
    /// ・初期化終了処理
    /// 
    /// に処理を分ける
    /// 
    /// ///





    // === コード（Monobehaviour基本機能の実装） ================

    /// <summary>
    /// 最初の設定をする
    /// 一度きりのやつ
    /// </summary>
    void Start()
    {

        //最初だけやる処理--------------------------------------------------------------





    }


    ///<summary>
    ///起動時の設定をする
    /// </summary>
    private void OnEnable()
    {
        //弾丸初期化
        InitializeBullet();

        //生存時間計測開始
        LifeTimeController().Forget();

        //起動時即子弾なら
        if ( em.bulletData.childType == Magic.ChildBulletType.activate )
        {
            ChildBulletCall();
        }
        //時間待ち子弾なら
        else if ( em.bulletData.childType == Magic.ChildBulletType.timeWait )
        {

        }

        //発射時の時間
        fireTime = GManager.instance.nowTime;

        //トークンの再発行
        magicToken = new CancellationTokenSource();


    }

    /// <summary>
    /// 弾丸消滅時の初期化を入れる
    /// </summary>
    private void OnDisable()
    {

        //衝突リストの初期化
        collisionList.Clear();

        //ここまで初期化
    }


    #region ヒット時の処理
    void OnTriggerEnter2D(Collider2D other)
    {
        //Debug.Log($"sdddssdsdsd{other.gameObject.name}");

        BulletHit(other);
    }
    void OnTriggerStay2D(Collider2D other)
    {
        //Debug.Log($"sdddssdsdsd{other.gameObject.name}");
        BulletHit(other);
    }

    #endregion



    void FixedUpdate()
    {

        //   Debug.Log($"名前{this.gameObject.name}標的{target == null}");

        //	float nowTime = GManager.instance.nowTime - fireTime;


        //BulletMoveController(nowTime);


    }


    #region キャラ情報関連処理









    #endregion


    #region 移動処理



    #endregion

    #region 管理メソッド

    //廃棄コード
    /*
    /// <summary>
    /// 毎フレーム実行
    /// 衝突したものを記録して管理するメソッド
    /// すでに当たったものには当たらないようにする
    /// </summary>
    void CollisionObjectController()
	{
        if (collisionList.Count > 0)
        {

            //攻撃魔法ならより短い時間で衝突をリセット
            if (em.mType == Magic.MagicType.Attack)
            {
                //貫通弾なら。速度0以上かつ弾丸も一秒以上生きるなら爆発とかではない
                if (em.BulletFeatureJudge(Magic.BulletType.貫通する) && em.bulletData._moveSt.speedV > 0 && em.bulletData._moveSt.lifeTime > 1)
                {
                    if (GManager.instance.nowTime - effectWait >= 0.5f)
                    {
                        collisionList = null;
                    }
                }

            }
			//そうでないなら三秒に一回効果する
            else if (GManager.instance.nowTime - effectWait >= 3)
            {
                collisionList = null;
            }
            if (collisionList == null)
            {
                collisionList = new List<Transform>();
            }

        }

    }

    /// <summary>
    /// 生存時間が経過したら消える
    /// そして音を消したりする
    /// </summary>
    /// <param name="nowTime"></param>
    void LifeTimeController(float nowTime)
	{
        //弾丸の生存時間終わりなら
        //あるいは追尾弾の標的消えたら
        //それか直進でいいか
        if (nowTime >= em.bulletData._moveSt.lifeTime) // ((em.bulletData._moveSt.fireType == Magic.FIREBULLET.HOMING || em.bulletData._moveSt.fireType == Magic.FIREBULLET.HOMING) && target == null))
        {

            //   存在中の音声がなってるなら消す
            if (isExPlay)
            {
                GManager.instance.StopSound(em.existSound, 1f);
            }
            //子弾丸であるなら消える



            atEf.BulletClear(transform);
            Destroy(this.gameObject);

        }
    }

	*/


    /// <summary>
    /// 衝突したものを記録して管理するメソッド
    /// すでに当たったものには当たらないようにする
    /// 衝突時に呼び出して、非同期メソッドで要素を削除
    /// ヒット時に呼ぶ
    /// </summary>
    async UniTaskVoid CollisionObjectController(GameObject collideObj)
    {
        // 現時点でのトークンを保存。
        CancellationToken token = magicToken.Token;

        //攻撃魔法ならより短い時間で衝突をリセット
        //そもそもヒット上限はつけてあるので貫通弾だけ見る
        if ( em.mType == Magic.MagicType.Attack )
        {
            if ( em.BulletFeatureJudge(Magic.BulletType.貫通する) )
            {
                collisionList.Add(collideObj);
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f));

                // キャンセルされてたら戻る。
                if ( token.IsCancellationRequested )
                {
                    return;
                }
                collisionList.Remove(collideObj);
            }
        }
        //そうでないなら三秒に一回効果する
        else
        {
            collisionList.Add(collideObj);
            await UniTask.Delay(TimeSpan.FromSeconds(3));

            // キャンセルされてたら戻る。
            if ( token.IsCancellationRequested )
            {
                return;
            }

            collisionList.Remove(collideObj);
        }




    }






    /// <summary>
    /// 生存時間が経過したら消える
    /// そして音を消したりする
    /// onEnableで呼ぶ
    /// </summary>
    /// <param name="nowTime"></param>
    async UniTaskVoid LifeTimeController()
    {

        // 現時点でのトークンを保存。
        CancellationToken token = magicToken.Token;

        //弾丸の生存時間待って
        await UniTask.Delay(TimeSpan.FromSeconds(em.bulletData._moveSt.lifeTime));

        // キャンセルされてたら戻る。
        if ( token.IsCancellationRequested )
        {
            return;
        }
        //弾丸の消滅処理
        bController.BulletClear(this.gameObject, index);

    }



    /// <summary>
    /// 弾丸の初期化
    /// 毎回やる初期化
    /// ターゲット情報などがもうある前提で進める
    /// レイヤー設定もここでするか
    /// 所属情報と魔法の種類をセットして
    /// </summary>

    void InitializeBullet()
    {



        if ( _user == MasicUser.Sister )
        {


            _healTag = "Player";
            //em.bulletData._moveSt.angle = SManager.instance.useAngle;
        }




        //弾の存在時間などのステータス初期化
        //	fireTime = GManager.instance.nowTime;

        //当たり判定初期化
        _damage.CollidRestoreResset();

        Vector2 position = transform.position;

        //発生音
        GManager.instance.PlaySound(em.fireSound, position);



        // 存在してる間のサウンドがあるなら
        if ( em.existSound != null )
        {
            //存在音鳴らす
            GManager.instance.PlaySound(em.existSound, position);

        }

        ///　
        /// 使用者（親）が子に渡す物
        /// 
        /// 敵と使用者の情報
        /// (バフもここで渡す？)
        /// 自分のオブジェクト
        /// 攻撃エフェクト管理機能
        /// 狙ってる角度（自分の向きとか角度も？）
        /// 
        /// ///

        //これが子の弾丸なら親の方向で向きを変える
        //いや、localScaleの向きは変えない。回転だけで対処する


        hitLimit = em.bulletData.hitLimit;

        //ジョブの初期化
        job.Initialize(bController.targetPoint, transform.position);


    }



    /// <summary>
    /// 衝突時にヒットエフェクトを出す
    /// </summary>
    void HitEffectCall()
    {


        //ヒットエフェクトあるなら
        if ( em.bulletData.hitEffect != null )
        {

            bController.atEf.HitEffectCall(em.bulletData.hitEffect, this.gameObject.transform.position, transform.rotation);

        }

        //衝突するたびに子弾を出すなら子弾を出す
        if ( em.bulletData.childType == Magic.ChildBulletType.everyCollide )
        {
            ChildBulletCall();
        }


    }




    /// <summary>
    /// 弾丸消す時の処理
    /// </summary>
    /// <param name="hitDesporn">ヒットして消える時限定の処理をする</param>
    void DespornBullet(bool hitDesporn = false)
    {
        //存在音あるなら消す
        if ( em.existSound != null )
        {
            GManager.instance.StopSound(em.existSound, 1f);
        }

        //消滅時に子弾を出すなら子弾を出す
        if ( em.bulletData.childType == Magic.ChildBulletType.bulletLost )
        {
            ChildBulletCall();
        }
        //ヒットして消える時の子弾発射
        else if ( em.bulletData.childType == Magic.ChildBulletType.hitLost && hitDesporn )
        {
            ChildBulletCall();
        }

        //トークンのキャンセル
        magicToken.Cancel();


    }

    /// <summary>
    /// 子弾丸を出す
    /// </summary>
    void ChildBulletCall()
    {

        Vector3 nowPosition = transform.position;

        //プレハブを呼び出すまえにバレットコントローラーに干渉するのもありだな？
        //変数に色々入れたりアングルぶち込んだり
        //このコントローラーはすでにインスタンス化されてて、プレハブじゃなくてオブジェクトとしてプールされてる。
        //だからここでは流石にゲットコンポーネントしないとダメ

        //次の弾丸のゲームオブジェクト
        GameObject nextBullet = bController.atEf.BulletCall(em.bulletData.childM.bulletData.fireController, nowPosition, transform.rotation).gameObject;

        float firstAngle;
        if ( em.bulletData.childM.bulletData._moveSt.fireType == Magic.FIREBULLET.RAIN )
        {
            firstAngle = bController.FirstAngleCalcu(nowPosition);
        }
        else
        {
            firstAngle = em.bulletData.childM.bulletData._moveSt.angle;

        }

        //ここキャッシュする？
        //衝突のたびならありだよ
        nextBullet.GetComponent<BulletController>().UserDataSet(bController.userController, bController.atEf, bController.target);


        ///やるべきこと
        ///
        /// ・弾丸のヒット時点でターゲットへのアングルを出す（向きだけ向くとか、最初に角度があるとか、ターゲットがどこにいるとかで決める）　〇
        /// ・コントローラーに魔法倍率魔法特徴からfloatで返すメソッドを作る？　〇
        /// ・敵ヒットと味方ヒットの判別方法を作るか？　
        /// 
        /// ///

    }


    #endregion



    public int RandomValue(int X, int Y)
    {
        return UnityEngine.Random.Range(X, Y + 1);

    }



    /// <summary>
    /// 内容置き換えます
    /// ゲームオブジェクトからコントロールアビリティに飛んでそこから回復させます
    /// </summary>
    /// <param name="target"></param>
    void AllyHitEffect(ControllAbillity target)
    {
        //特殊効果を送る
        em.allyMagicEvent.SendData(target.conditionController);

    }

    /// <summary>
    /// 弾丸当たった時の処理
    /// </summary>
    void BulletHit(Collider2D other)
    {


        // オーナーチェック。あるかどうか、Nullなら戻る
        if ( owner == other.gameObject || other == null )
        {
            //Debug.Log($"衝突{owner == other.gameObject.transform}{isAct}{other != null}");// {gameObject.name}");
            return;
        }

        if ( collisionList.Contains(other.gameObject) )
        {
            return;
        }

        //衝突管理
        //衝突リストに追加して時間経過後に削除まで
        CollisionObjectController(other.gameObject).Forget();


        //ヒットしたかどうか
        bool isHit = false;


        //ヒットエフェクトを呼ぶ
        HitEffectCall();


        //ここから衝突と消滅を
        //特殊効果も与えるか


        //攻撃以外の回復魔法などなら接触時に効果発動
        //特殊効果で送るか
        if ( em.allyMagicEvent.CheckEventExist() )
        {
            ControllAbillity targetController = GManager.instance.GetControllAbilityByObject(other.gameObject);

            //もし敵ならリターンしてね
            if ( targetController.EnemyCheck(bController.userController.gameObject) )
            {
                return;
            }

            //効果与える
            AllyHitEffect(targetController);

            //ここで回復魔法の場合は、回復量に応じて回復効果を作成して送る？
            //回復倍率とかいろいろしないとダメだからな

            isHit = true;
        }

        //攻撃魔法なら接触後消えて
        //貫通弾でないなら消えてね
        if ( Magic.MagicType.Attack == em.mType )
        {
            isHit = bController.HitReport(other);
        }

        //ヒットしたなら
        //ここはちゃんと敵に当たってる、ダメージスクリプトからのisHitだから
        //なんで前回当った時間を記録しておいて、isHitつまり壁なら総ヒット回数を減らさないように
        if ( isHit && !em.BulletFeatureJudge(Magic.BulletType.貫通する) )
        {
            hitLimit--;
            if ( hitLimit < 0 )
            {
                //ヒットして消滅
                DespornBullet(true);
            }
        }

        //ヒット数を消化したら消えるとかにしない？
        //一体の敵へのヒット数と全体のヒット数があると思うんよ

    }



    #region 汎用ツール

    /// <summary>
    /// 二点間の角度を求める
    /// </summary>
    /// <param name="p1">自分の座標</param>
    /// <param name="p2">相手の座標</param>
    /// <returns></returns>
    float GetAim(Vector2 p1, Vector2 p2)
    {
        float dx = p2.x - p1.x;
        float dy = p2.y - p1.y;
        float rad = Mathf.Atan2(dy, dx);
        return rad * Mathf.Rad2Deg;
    }


    #endregion


    #region バレットコントローラーとの連携

    /// <summary>
    /// これを通じてダメージスクリプト渡して
    /// コントローラーにダメージ設定してもらうよ
    /// </summary>
    /// <returns></returns>
    public MyDamageOntouch GetDamageScript()
    {
        return _damage;
    }

    /// <summary>
    /// 最初のデータ設定
    /// </summary>
    /// <param name="myIndex"></param>
    public void FirstDataSet(int myIndex)
    {
        index = myIndex;
    }


    /// <summary>
    /// ジョブシステムから受け取った演算結果を弾丸に反映していく
    /// 動ける時間かどうかの判断はJobシステムがかってにやるよ
    /// 生成時間からげんざいじかんをひいて
    /// </summary>
    public void SetVelocity(in Vector3 moveSpeed)
    {
        rb.velocity = moveSpeed;
    }


    #endregion

}
