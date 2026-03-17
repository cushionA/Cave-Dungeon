using DarkTonic.MasterAudio;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.InventoryEngine;
using PathologicalGames;
using RenownedGames.Apex;
using System;
using static MoreMountains.CorgiEngine.ConditionAndEffectControllAbility;
using MoreMountains.CorgiEngine;
using static PlayerStatus;
using static MoreMountains.CorgiEngine.AtEffectCon;
using static DarkTonic.MasterAudio.PlaylistController;

/// <summary>
/// 敵のヘルスにダメージを与える武器に共通のインターフェイス。
/// </summary>
public interface IDamageWeapon
{

}


public class Equip : Item
{

    #region 定義


    public enum AttackType
    {
        Slash,//斬撃。ほどほどに通るやつが多い
        Stab,//刺突。弱点のやつと耐えるやつがいる
        Strike//打撃。弱点のやつと耐えるやつがいる。アーマーとひるませ強く
    }

    public enum GuardType
    {
        small,
        normal,
        tower,
        wall
    }

    /// <summary>
    /// モーションごとの入力タイプ
    /// 種類は4種類
    /// 
    /// ・入力時即時発動
    /// ・押している間チャージ、チャージ時間経過で発動
    /// ・押したら一定時間後に発動、しかしボタン押してる間は待機で照準が可能（魔法）
    /// ・押したら一定時間後に発動、しかしボタン押してる間は待機でチャージ攻撃が出る（待てるのは20秒まで）
    /// 
    /// 
    /// </summary>
    public enum InputType
    {
        normal,
        chargeAttack,
        waitableCharge,
        magic,//魔法にもチャージとかいろいろあるのでは？
        non//なにも入力されてないとき

    }

    /// <summary>
    /// 入力に関する情報
    /// 入力方式と溜め時間
    /// </summary>
    public struct InputData
    {
        [Header("攻撃の入力タイプ")]
        public InputType motionInput;

        [Header("モーションのチャージ時間")]
        public float chargeTime;
    }

    /// <summary>
    /// モーションとチャージ攻撃に関するデータ
    /// </summary>
    public struct MotionChargeImfo
    {
        [Header("技の値")]
        /// <summary>
        /// 攻撃のXモーション値、Y追加アーマー、Z強靭削り
        /// </summary>
        public AttackValue[] normalValue;

        [Header("チャージした技の値")]
        /// <summary>
        /// 武器固有攻撃のXモーション値、Y追加アーマー、Z強靭削り
        /// </summary>
        public AttackValue[] chargeValue;

        [Header("入力関連のデータ")]
        public InputData[] inputData;

        //コンボ数はいらない
        //各モーションにコンボ終了地点かどうかを持たせる

    }

    /// <summary>
    /// ガード時の性能
    /// ヘルスにそのまま渡す
    /// </summary>
    public struct GuardStatus
    {
        [Header("物理カット防御力")]
        public float phyCut;//カット率

        [Header("聖カット防御力")]
        public float holyCut;//光。
        [Header("闇カット防御力")]
        public float darkCut;//闇。
        [Header("炎カット防御力")]
        public float fireCut;//魔力
        [Header("雷カット防御力")]
        public float thunderCut;//魔力

        [Header("攻撃による盾削りをどれくらい減らせるか")]
        public float guardPower;//受け値


        /// <summary>
        /// 1なら前、0なら全方位
        /// -1なら背後も
        /// </summary>
        [Header("ガード方向")]
        public int guardDirection;


        [HideInInspector]
        /// <summary>
        /// エンチャによる追加のガードカット値
        /// </summary>
        public float additionalCut;

        [HideInInspector]
        /// <summary>
        /// エンチャによる追加のガード受け値
        /// </summary>
        public float additionalGuardPower;


        /// <summary>
        /// 状態異常を何パーセントカットするかという数値
        /// プレイヤーの場合、装備変更時に数値を変える
        /// </summary>
        public float badConditionCut;
    }

    /// <summary>
    /// 攻撃力のまとめ構造体
    /// </summary>
    public struct AttackStatus
    {
        [Header("合計攻撃力")]
        public float Atk;

        [Header("攻撃力")]
        public float phyAtk;
        [Header("聖攻撃力")]
        public float holyAtk;
        [Header("闇攻撃力")]
        public float darkAtk;
        [Header("炎攻撃力")]
        public float fireAtk;
        [Header("雷攻撃力")]
        public float thunderAtk;


        /// <summary>
        /// 追加のショック
        /// </summary>
        [HideInInspector]
        public float additionalShock;
    }

    /// <summary>
    /// エンチャントにより追加されたデータを保存する
    /// </summary>
    public struct EnchantData
    {

        /// <summary>
        /// 追加攻撃力
        /// </summary>
        public float additionalATK;

        /// <summary>
        /// 追加の削り
        /// </summary>
        public int additionalShock;


        /// <summary>
        /// 追加のガードカット値
        /// </summary>
        public float additionalGuardCut;

        /// <summary>
        /// 追加のガード受け値
        /// </summary>
        public float additionalGuardPower;


        /// <summary>
        /// 現在付与されてるエンチャ
        /// 武器属性加算に利用したりする
        /// </summary>
        public EquipEnchantSelect nowEnchant;


        /// <summary>
        /// エンチャで与えられる蓄積とかの数値系のイベント
        /// エンチャ時に入れる
        /// </summary>
        [HideInInspector]
        public ConditionDataValue enchantEvent;
    }





    #endregion

    //画像
    #region


    [Foldout("スプライト")]
    [Header("表示する正面のスプライト")]
    public Sprite[] front = new Sprite[2];

    [Foldout("スプライト")]
    [Header("表示する側面のスプライト")]
    public Sprite[] Side = new Sprite[2];

    [Foldout("スプライト")]
    [Header("表示する斜めのスプライト")]
    public Sprite[] Naname = new Sprite[2];

    #endregion

    //内部データ
    //こいつらは武器レベルとか能力値で算出するから表示しない
    #region
    /// <summary>
    /// 装備レベル
    /// 強化によって変動
    /// </summary>
    [HideInInspector] public int wLevel = 0;


    /// <summary>
    /// バフとかの影響を受けるよね
    /// こっちは変えていい
    /// ベース値から書き変えてるので
    /// </summary>
    [Header("攻撃ステータス")]
    public AttackStatus atStatus;






    /// <summary>
    /// ヘルスにはこれを渡す。
    /// 装備強化とかで武器レベルを参照して入れ替える
    /// </summary>
    [Header("ガード性能")]
    public GuardStatus guardStatus;

    [HideInInspector]
    /// <summary>
    /// エンチャントの情報を入れる
    /// </summary>
    public EnchantData enchantData;


    [Header("敵に対して武器攻撃が持つ効果")]
    /// <summary>
    /// 例えば毒とか
    /// 防御低下とかね
    /// </summary>
    public EffectContainer attackWeaponEvent;


    [Header("装備した際に発動する効果")]
    [SerializeField]
    /// <summary>
    /// 例えば攻撃力増加とか
    /// </summary>
    public EffectContainer equipConditionEffects;

    #endregion


    //攻撃力と補正値
    #region
    [Foldout("基礎攻撃力設定")]
    public float[] phyBase;//物理攻撃。これが1以上ならモーションにアニメイベントとかで斬撃打撃の属性つける
    [Foldout("基礎攻撃力設定")]
    public float[] holyBase;//光。筋力と賢さが関係。生命力だから
    [Foldout("基礎攻撃力設定")]
    public float[] darkBase;//闇。魔力と技量が関係
    [Foldout("基礎攻撃力設定")]
    public float[] fireBase;//魔力

    [Foldout("基礎攻撃力設定")]
    public float[] thunderBase;//魔力


    //カーブはまず、武器レベルごとの最大補正と最低補正を考えて緩急つけていく
    //最初の伸びがいいとかそういうコンセプトで
    //そしてカーブをコピーして次の武器レベル作る

    [Foldout("基礎攻撃力設定")]
    public AnimationCurve[] powerCurve;

    [Foldout("基礎攻撃力設定")]
    public AnimationCurve[] skillCurve;

    [Foldout("基礎攻撃力設定")]
    public AnimationCurve[] intCurve;

    #endregion







    //
    #region ガード関連

    [Foldout("ガード関連")]
    [Header("物理カット率")]
    public float[] phyCutSet;//カット率

    [Foldout("ガード関連")]
    [Header("光カット率")]
    public float[] holyCutSet;//光。

    [Foldout("ガード関連")]
    [Header("闇カット率")]
    public float[] darkCutSet;//闇。

    [Foldout("ガード関連")]
    [Header("炎カット率")]
    public float[] fireCutSet;//魔力

    [Foldout("ガード関連")]
    [Header("雷カット率")]
    public float[] thunderCutSet;//魔力


    /// <summary>
    /// この数値でガードの音変わる
    /// 35まで小盾、70から大盾
    /// </summary>
    [Header("ガード力")]
    [Foldout("ガード関連")]
    public float[] guardPowerSet;//受け値

    [Foldout("ガード関連")]
    [Header("ジャスガ開始時間")]
    public float parryStart;

    [Foldout("ガード関連")]
    [Header("ジャスガ受付時間")]
    public float parryTime;

    [Foldout("ガード関連")]
    [Header("パリィでのスタミナ回復量")]
    public float parryRecover;//パリィでのスタミナ回復量

    [Foldout("ガード関連")]
    [Header("盾の種類")]
    public GuardType shieldType;

    [Foldout("ガード関連")]
    [Header("金属装備か")]
    public bool isMetal;

    #endregion



    //
    #region 装備負荷など


    [Foldout("装備負荷関連")]
    [Header("必要筋力")]
    public int needPower;//

    [Foldout("装備負荷関連")]
    [Header("必要技量")]
    public int needSkill;//

    [Foldout("装備負荷関連")]
    [Header("必要な賢さ")]
    public int needInt;//

    [Foldout("装備負荷関連")]
    [Header("固有技の消費MP")]
    public int[] artsMP;

    [Foldout("装備負荷関連")]
    [Header("重量")]
    public int _weight;


    #endregion


    //
    #region 音とエフェクト

    /// <summary>
    /// 武器の存在音は盾に優先する
    /// if(盾存在あるなら)とかで先に盾を初期化
    /// その後武器の存在音を出してあるなら上書き
    /// </summary>
    [Foldout("サウンドとエフェクト")]
    [Header("常に鳴る音")]
    [SoundGroup]
    public string ExistSound;

    [Foldout("サウンドとエフェクト")]
    [Header("通常ムーブエフェクトの設定")]
    public Dictionary<EffectControllAbility.SelectState, EffectCondition> _useList;

    [Foldout("サウンドとエフェクト")]
    [Header("通常ムーブエフェクトのプレハブ")]
    public PrefabPool[] usePrefab;


    [Foldout("サウンドとエフェクト")]
    [Header("固有エフェクトとサウンドリスト")]
    public MoreMountains.CorgiEngine.AtEffectCon.EffectAndSound[] AttackEffect;

    /// <summary>
    /// 子のプレハブには弾丸コントローラーも含もうね
    /// </summary>
    [Foldout("サウンドとエフェクト")]
    [Header("攻撃エフェクトのプレハブたち")]
    public PrefabPool[] AttackPrefab;

    #endregion



    /// <summary>
    /// 武器の補正値を計算しただけのATKを返すメソッド
    /// 素の攻撃力を
    /// </summary>
    /// <param name="equip"></param>
    /// <param name="baseStatus"></param>
    /// <param name="addStatus"></param>
    /// <param name="element"></param>
    /// <returns></returns>
    public float ReturnNeutralAtk(in PlayerStatusData baseStatus, in PlayerStatusData addStatus, DamageStatusSelect element)
    {



        float returnValue = 0;

        if ( element == DamageStatusSelect.物理 )
        {
            if ( phyBase[wLevel] > 0 )
            {
                returnValue = phyBase[wLevel] + (powerCurve[wLevel].Evaluate(baseStatus.power + addStatus.power)) +
                               skillCurve[wLevel].Evaluate(baseStatus.skill + addStatus.skill);
            }
        }

        else if ( element == DamageStatusSelect.炎 )
        {
            if ( fireBase[wLevel] > 0 )
            {
                returnValue = fireBase[wLevel] + intCurve[wLevel].Evaluate(baseStatus._int + addStatus._int);

            }
        }
        else if ( element == DamageStatusSelect.雷 )
        {
            if ( thunderBase[wLevel] > 0 )
            {
                returnValue = thunderBase[wLevel] + intCurve[wLevel].Evaluate(baseStatus._int + addStatus._int);

            }
        }
        else if ( element == DamageStatusSelect.聖 )
        {
            if ( holyBase[wLevel] > 0 )
            {
                returnValue = holyBase[wLevel] + powerCurve[wLevel].Evaluate(baseStatus.power + addStatus.power) +
                                   intCurve[wLevel].Evaluate(baseStatus._int + addStatus._int);

            }
        }
        else if ( element == DamageStatusSelect.闇 )
        {
            if ( darkBase[wLevel] >= 1 )
            {
                returnValue = darkBase[wLevel] + intCurve[wLevel].Evaluate(baseStatus._int + addStatus._int) +
                                   skillCurve[wLevel].Evaluate(baseStatus.skill + addStatus.skill);

            }
        }

        return returnValue;

    }



}
