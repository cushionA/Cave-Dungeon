using DarkTonic.MasterAudio;
using MoreMountains.InventoryEngine;
using PathologicalGames;
using RenownedGames.Apex;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static AttackData;
using static AttackValueBase;
using static DefenseData;
using static Equip;
using static MoreMountains.CorgiEngine.ConditionAndEffectControllAbility;
/// ・プールへのエフェクト追加どうするか（弾丸情報みたいにして配列でまとめるか。魔法の弾丸情報のどれを参照するかみたいなのを番号で持たせとけ）
/// 
/// 
/// ・アイテムとしてのデータ
/// ・挙動データ（速度、追尾形式、弾丸の生存時間）
/// ・攻撃力データ（ヒット回数、モーション値、攻撃力）　弾丸所属（攻撃力だけ魔法から持ってくる）
/// ・エフェクトデータ（フラッシュエフェクト、着弾エフェクト、魔法エフェクト）　弾丸所属（フラッシュエフェクトと着弾エフェクトは）
/// ・詠唱データ（詠唱時間、モーション指定）　魔法所属
/// ・バレットコントローラープレハブへの参照　魔法所属
public class Magic : Item
{


	///　魔法は完全に弾丸の威力は固定にする
	///　回復も含めて触媒で決める
	///　そのための改造をする


    #region 定義


	/// <summary>
	/// 弾丸のデータをまとめたもの
	/// 弾丸がどういう風に動くか
	/// そして何回ヒットするか
	/// bulletDataからAttackDataにデータを送れるようにしたい
	/// そこはもう、少し苦労するか…
	/// </summary>

	public class BulletData
	{

        #region 弾丸の動きについて

        [Header("弾丸の動きの設定")]

        [Foldout("挙動設定")]
        [Header("弾丸の動きの設定")]
        public BMoveStatus _moveSt;




        [Foldout("挙動設定")]
        /// <summary>
        /// 子弾であるかどうか
        /// </summary>
        public bool isChild;

        [Foldout("挙動設定")]
        /// <summary>
        /// 全体で何回ヒットできるか
		/// ヒット回数消化したら消える
		/// 敵に三回当たったら消えるみたいな
        /// </summary>
        public int hitLimit = 1;






        #endregion


        #region エフェクト関連


        [Foldout("エフェクト関連")]
        [Header("射撃オブジェクト")]
        ///<summary>
        ///生成する弾丸オブジェクト
		///コントローラーに管理してもらうよう変わったのでこれはいらない
        ///</summary>
        public ParticleSystem effects;


        [Foldout("エフェクト関連")]
        [Header("射撃オブジェクト")]
        ///<summary>
        ///生成する弾丸オブジェクト
		///コントローラーに管理してもらうよう変わったのでこれのゲームオブジェクトを生成
        ///</summary>
        public GameObject fireController;






        [Foldout("エフェクト関連")]
        [Header("発射時のエフェクト")]
        /// <summary>
        /// いわゆるマズルフラッシュ
        /// </summary>
        public ParticleSystem flashEffect;


        [Foldout("エフェクト関連")]
        [Header("ヒット時のエフェクト")]
        /// <summary>
        /// こちらに爆発処理とか乗せる
        /// </summary>
        public ParticleSystem hitEffect;

		[Foldout("エフェクト関連")]
		[Header("ヒット時の子弾の設定")]
		///<summary>
		///　どのタイミングで子弾を出すか
		///</summary>
		public ChildBulletType childType;


        [Foldout("エフェクト関連")]
        /// <summary>
        /// 子弾丸の魔法
        /// </summary>
        public Magic childM;




        [Foldout("エフェクト関連")]
        [Tooltip("ヒット時とかのエフェクトも含むので配列")]
        [Header("この魔法のエフェクトのプレハブ")]
        public PrefabPool[] _usePrefab;

        #endregion
    }




    /// <summary>
    /// 魔法のタイプ
    /// </summary>
    public enum MagicType
	{
		Attack,//攻撃
		Recover,//回復
		Support,//支援
		help,//条件で勝手に発動？
		WeaponEffect,//武器から出るタイプの魔法
	}

    /// <summary>
    /// 子弾の扱いのタイプ
    /// </summary>
    public enum ChildBulletType
    {
        noChild,//子弾なし
		activate,//最初
		timeWait,//
		everyCollide,//衝突時
		bulletLost,//消滅時
		hitLost,//ヒットして消滅した時だけ
    }


    /// <summary>
    /// 弾丸の挙動分類
	/// 
	/// 最初の角度はこれとは関係ない
	/// 最初の角度が設定されてる場合、それを設定したうえで追尾などの動作を行っていく
	/// いや、Angleの場合、敵との角度にこれを足してやるか
	/// つまり、アングルで初期角度20度の場合、敵への角度から20度上に
    /// </summary>
    public enum FIREBULLET
	{
		NOHOMING,//何の誘導もなしに魔法の指定の角度に放たれる。まっすぐ
		ANGLE,//最初に角度だけtargetの方を向いて飛ぶ。それぞれの弾丸が
		HOMING,//完全追尾
		HOMING_Z,//指定した角度内に標的があるとき追尾。
		RAIN,//最初に発射地点から標的までの角度を計算してその角度で全弾撃ちおろす
		STOP,//動かない
	}

	/// <summary>
	/// 弾丸の移動ステータス
	/// </summary>
	 [Serializable]
	public struct BMoveStatus
    {

	public FIREBULLET fireType;

		[Header("弾丸の生存時間")]
		/// <summary>
		/// これだけ経つと弾丸が消える
		/// </summary>
		public float lifeTime;
	
		[Header("初期速度")]
	///<summary>
	///射出される速度
	///</summary>
	public float speedV;

	[Header("速度の加速度")]
	/// <summary>
	/// プラスにすればだんだん早く、マイナスにすれば遅く
	/// 加速度をマイナスにすれば減速
	/// </summary>
	public float speedA;

        [Header("速度の加速限界")]
        /// <summary>
        /// プラスにすればだんだん早く、マイナスにすれば遅く
		/// マイナスに限界を設定すればゼロを超えて減速し続けて、ブーメランみたいになるかも
		/// 減速の場合はゼロ及びマイナスに上限をとらえて、プラスの場合はプラス側に上限をつける
        /// </summary>
        public float speedLimit;

        [Header("射出角度")]
	///<summary>
	///　最初に放たれる角度上とか斜めとか水平とか
	///</summary>
	public float angle;


	[Header("追尾時間")]
	/// <summary>
	/// 追いかける時間。最初だけ追尾させれば方向だけ合わせる
	/// </summary>
	public float homingTime;

	[Header("追尾の強さ")]
	///<summary>
	///追尾の強さ。プラスなら追尾
	///</summary>
	public float homingAngleV;

	[Header("追尾の性能変化")]
	/// <summary>
	/// これがプラスだと時間ごとに追尾力が上がる。マイナスなら下がる
	/// </summary>
	public float homingAngleA;

        [Header("追尾の変化限界")]
        /// <summary>
        /// これがプラスだと時間ごとに追尾力が上がる。マイナスなら下がる
        /// </summary>
        public float homingAngleLimit;


        [Header("射出開始までの時間")]
	/// <summary>
	/// これがあると一定秒数待つよ
	/// </summary>
	public float waitTime;

	[Header("弾丸の回転")]
	public float rotateVt;

		[Header("回転オン")]
		/// <summary>
		/// 回転するか否か。真で回転
		/// </summary>
		public bool isRotate;

		[Header("弾丸の拡大率")]
		/// <summary>
		/// 大きくしたり小さくしたり
		/// </summary>
		public Vector2 bulletScaleV;

		[Header("弾丸の拡大の加速率")]
		/// <summary>
		/// だんだん大きくしたり
		/// </summary>
		public Vector2 bulletScaleA;

        [Header("弾丸の拡大の倍率限界")]
        /// <summary>
        /// 弾丸の変化の限界
        /// </summary>
        public Vector2 scaleChangeLimit;
    }



	//でもこれってシスターさんの判断のためにあるなら
	//Sismagicにだけ持たせておけばよくない？
    #region 魔法の性質

    /// <summary>
    /// 魔法の特性
	/// 回復魔法とか全般に関して
    /// </summary>
    [Flags]
    public enum BulletType
    {
        敵を吹き飛ばす = 1 << 0,
        貫通する = 1 << 1,
        設置攻撃 = 1 << 2,
        範囲攻撃 = 1 << 3,//雨
        追尾する = 1 << 4,
        サーチ攻撃 = 1 << 5,//位置に出現する
        爆発 = 1 << 6,
        指定なし =0

    }

    /// <summary>
    /// 弾丸の性質が当てはまるかのビット演算の結果を返してくれるメソッド
    /// </summary>
    /// <returns></returns>
    public bool BulletFeatureJudge(BulletType judgeState)
    {
        //かつで0以上になるか
        return (bulletFeature & judgeState) > 0;
    }



    /// <summary>
    /// サポート効果の一覧
    /// </summary>
    [Flags]
    public enum SupportType
    {
        攻撃強化 = 1 << 0,
        防御強化 = 1 << 1,
        ダメージカット = 1 << 2,
        ダメージアップ = 1 << 3,
        耐性アップ = 1 << 4,
        エンチャント = 1 << 4,
        アクション強化 = 1 << 5,
        バリア = 1 << 6,
        サテライト = 1 << 7,//周りにとどまって敵を攻撃
        オブジェクト設置 = 1 << 8,//火柱のようなダメージオブジェクトを配置して守る
        エリア効果 = 1 << 9,//敵だけ押し返す風とか
        一度だけ無敵 = 1 << 10,//一発だけ耐えるバリア
        召喚 = 1 << 11,//おとりや味方を召喚。敵でも味方でもない無差別攻撃の邪神的なの呼び出してもいいかも
        なし = 0
    }

    /// <summary>
    /// サポートの性質が当てはまるかのビット演算の結果を返してくれるメソッド
    /// </summary>
    /// <returns></returns>
    public bool SupportStateJudge(SupportType judgeState)
    {
        //かつで0以上になるか
        return (supportEffect &judgeState) > 0;
    }


    /// <summary>
    /// 回復の付属効果の一覧
    /// </summary>
    [Flags]
    public enum HealEffectType
    {
        リジェネ = 1 << 0,
        毒解除 = 1 << 1,
        浸食解除 = 1 << 2,
        凍結解除 = 1 << 3,//凍結は拘束
        虚弱系状態異常解除 = 1 << 4,//毒、スタミナ回復低下、ガード性能低下、アーマー低下、状態異常耐性減少、攻防低下
        妨害系状態異常解除 = 1 << 5,//浸食、凍結、沈黙、ヘイト上昇、移動速度低下、被・与ダメージ干渉
        全状態異常解除 = 1 << 6,//すべて解除
        MP回復 = 1 << 7,
        ふんばり = 1 << 8,//HP1で残る
        スタミナとアーマーリセット = 1 << 9,//スタミナとアーマー回復
        復活 = 1 << 10,//死亡時一度だけよみがえる。何度でも使えるけど総MPまで大幅に削る
        なし = 0
    }


    /// <summary>
    /// 回復の性質が当てはまるかのビット演算の結果を返してくれるメソッド
    /// </summary>
    /// <returns></returns>
    public bool HealStateJudge(HealEffectType judgeState)
    {
        //かつで0以上になるか
        return (healEffect &judgeState) > 0;
    }



    #endregion



    /// <summary>
    /// 発動モーションのタイプ
	/// 詠唱よりタイプが多い
	/// 近接系の呪文にも対応
    /// </summary>
    public enum FIRETYPE
	{
		NoCast,
		Short,
		Normal,
		Long,
		Rain,
		Set,//置く
        Swing,//振る
        Pear,//貫く
        Special
	}

	/// <summary>
	/// 詠唱モーションのタイプ
	/// </summary>
	public enum CASTTYPE
	{
		NoCast,
		Short,
		Normal,
		Long,
		Rain,
		Set,
		Special
	}



    #endregion



    #region 内部情報
    [Header("装備画面で選択できるか(子弾は無理)")]
	public bool selectable = true;



	[Header("味方に対して魔法が持つ効果")]
    /// <summary>
    /// 魔法の性質に関するデータ
	/// 味方に与える効果を出す
	/// DamageOntouchに渡す
	/// 数値いじらないから直接参照してもいい
    /// </summary>
    public EffectContainer allyMagicEvent;





    [Header("魔法の種別。回復など")]
	public MagicType mType;


	[Header("魔法の攻撃力")]
	public AttackStatus atkStatus;



	/// <summary>
	/// 魔法のアーマーとかレベルとか属性とか載ってる
	/// 魔法の性質に関するデータ
	/// 敵に対する効果とかも入ってるよ
	/// </summary>
    [Header("魔法のモーションのデータ")]
	public AttackValueBase magicData;

	//あとはここに状態異常データとか足すよ
	//倍率はコントロールアビリティから






	[Foldout("内部ステータス")]
	[Header("回復の基礎量")]
	///<summary>
	///回復量の基礎
	///触媒の倍率できまる？
	///でも触媒の倍率で上昇する上限みたいなのを魔法内につけてもいいかも
	///そうでないと低級でもやばいよな
	///</summary>
	public float recoverBase;//回復


	/// <summary>
	/// 魔法の射程距離
	/// 最初の地点からこれだけ動いたら消えるようにする？
	/// 設置系の魔法、あるいはレンジ0の魔法はチェックすらしないとか
	/// </summary>
	[Header("魔法の射程距離")]
	public float magicRange;





	/// <summary>
	/// もしかしたらEventObject的なコンポーネントに
	/// 効果時間とかは委託するかも
	/// なんでこれはソート用の情報
	/// </summary>
	[Foldout("内部ステータス")]
	[Header("効果時間")]
    public float effectTime;//効果時間





	[Foldout("発動コスト")]
	[Header("詠唱時間")]
	/// <summary>
	/// スキルや秘伝書（早口言葉）、技量で少なくなる？
	/// </summary>
	public float castTime;

	[Foldout("発動コスト")]
	[Header("消費MP")]
	public float useMP;//消費MP


	#endregion



	#region 弾丸の挙動


	[Header("弾丸の挙動")]
	public BulletData bulletData;


    [Header("弾丸の数")]
    /// <summary>
    /// 一度にばらまかれる数
    /// </summary>
    public int bulletNumber;




    #endregion




    //弾丸が持っている効果
    #region

    [Header("弾丸の追加効果")]

	
	[EnumFlags]
	[Header("攻撃の特徴")]
	//何も指定しないと指定なしになるようにUIを工夫
	public BulletType bulletFeature = BulletType.指定なし;

    [EnumFlags]
    [Header("サポート効果")]
	public SupportType supportEffect = SupportType.なし;

    [EnumFlags]
    [Header("回復エフェクト")]
	public HealEffectType healEffect = HealEffectType.なし;



    #endregion






    //サウンド、アニメーション設定
    #region
    [Header("サウンド、アニメ設定")]
	

	[Foldout("サウンド・アニメ設定")]
	[Header("発生サウンド")]
	[SoundGroup]
	/// <summary>
	// 発生時のサウンド
	/// </summary>
	public string fireSound;

	[Foldout("サウンド・アニメ設定")]
	[Header("動き始めの音")]
	[SoundGroup]
	public string moveSound;

	[Foldout("サウンド・アニメ設定")]
	[Header("ずっと鳴る音")]
	[SoundGroup]
	/// <summary>
	// 存在してる間のサウンド
	/// </summary>
	public string existSound;

	[Foldout("サウンド・アニメ設定")]
	[Header("衝突サウンド")]
	[SoundGroup]
	/// <summary>
	// 衝突時のサウンド
	/// </summary>
	public string hitSound;

	[Foldout("サウンド・アニメ設定")]
	[Header("発動モーション名")]
	/// <summary>
	// 発動のアニメ
	/// </summary>
	public FIRETYPE fireType;

	[Foldout("サウンド・アニメ設定")]
	[Header("詠唱モーション名")]
	/// <summary>
	// 詠唱のアニメ
	/// </summary>
	public CASTTYPE castType;

    #endregion





}
