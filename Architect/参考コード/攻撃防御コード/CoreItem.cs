using System;
using UnityEngine;
using MoreMountains.InventoryEngine;
using System.Collections.Generic;
using PathologicalGames;
using static Equip;
using static CharacterStatus;
using static PlayerStatus;
using static MoreMountains.CorgiEngine.ConditionAndEffectControllAbility;
using MoreMountains.CorgiEngine;
using RenownedGames.Apex;

[Serializable]
[CreateAssetMenu(fileName = "CoreItem", menuName = "CreateCore")]
public class CoreItem : InventoryItem
{
    //まぁコア的な処理を

    //アーマー、HP、MP、詠唱時間、各種攻撃力、各種防御力、装備重量、時には移動速度、ジャンプ時間も？、あとは盾受けした時のダメージ削減倍率や
    //ものによっては特殊効果つけてもいいかも
    //一例（二段ジャンプ、空中回避で短距離ワープ（いわゆる空中回避。ワープなのはモーションとかコード作りたくないから）、エフェクト出てガードが強化される、パリィ持続時間が倍率強化される、パリィ時数秒攻撃力増加、カウンターダメージが上昇）
    //さらに（アーマー削りが強くなる、魔法扱いの追加エフェクトが攻撃エフェクトに置き換わる、）
    //あるいは特定装備を身に着けてる時、特定攻撃属性だけ特殊アクションとか特殊効果とか
    //ノーマルアニメスピードみたいな変数用意して移動速度の倍率とかを入れる？装備重量なども合わせて
    //    
    //特殊効果はスキルセットとして、スキルセットがあるかの条件判断を装備時にやる
    //数値いじったりフラグ動かしたりの処理。数値はもちろんフラグ立ってたらアクションが入れ替わる
    //




    /// <summary>
    /// 最大HPの追加分
    /// </summary>
    public int additionalHp;

    /// <summary>
    /// 最大MPの追加分
    /// </summary>
    public int additionalMp;



    /// <summary>
    /// 　最大スタミナの追加分
    /// </summary>
    public int additionalStamina;

    /// <summary>
    /// 装備可能重量の追加分
    /// </summary>
    public int additionalWeight;


    /// <summary>
    /// プレイヤーのステータス
    /// ここにあるのは追加ステータス
    /// 元のステータスに足していく
    /// </summary>
    public PlayerStatusData status;


    /// <summary>
    /// 攻撃力関連のステータス
    /// </summary>
    [HideInInspector]
    public AttackStatus attackStatus;


    [Header("防御ステータス")]
    public DefStatus deffStatus;


    [Header("装備した際に発動する効果")]
    [SerializeField]
    /// <summary>
    /// 例えば攻撃力増加とか
    /// </summary>
    public EffectContainer equipConditionEffects;


    //isGuardの時使う

    /// <summary>
    /// アーマー
    /// これの分だけ怯まない
    /// 平常時アーマー
    /// </summary>
    public float baseArmor;


    /// <summary>
    /// 各ムーブで使用するエフェクト
    /// 基本はコアにだけ入れる
    /// 
    /// </summary>
    [Header("通常ムーブエフェクトの設定")]
    public Dictionary<EffectControllAbility.SelectState, EffectCondition> _useList;

    /// <summary>
    /// 使用するエフェクトのプレハブ
    /// </summary>
    public List<PrefabPool> _usePrefab;
    //  public enum 

}
