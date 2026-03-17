using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static CharacterStatus;
using static Equip;

public struct DefenseData
{

    #region 定義

    /// <summary>
    /// 防御状態
    /// すぐ変わるものを集める
    /// </summary>
    public enum DefState
    {
        攻撃中 = 1 << 0,
        アーマー付き = 1 << 1,
        スーパーアーマー = 1 << 2,
        被ダメージ増大 = 1 << 3,
        ガード中 = 1 << 4,
        ジャスガ中 = 1 << 5,
        スタン禁止 = 1 << 6,//起き上がり中など、スタン連続しないようにするため。これの時は処理を行わない
        運搬可能 = 1 << 7,// 運んでいける状態。怯み中、ガードブレイク中だけ。その時に運搬技食らうと拘束スタン状態が始まる？
    }

    /// <summary>
    /// 防御倍率計算に使う数値
    /// </summary>
    public struct DefMultipler
    {
        /// <summary>
        /// 全防御に対する倍率
        /// </summary>
        public float allDefMultipler;

        /// <summary>
        /// 防御倍率
        /// </summary>
        public float phyDefMultipler;

        /// <summary>
        /// 斬撃防御倍率
        /// </summary>
        public float slashDefMultipler;

        /// <summary>
        /// 刺突防御倍率
        /// </summary>
        public float pierDefMultipler;

        /// <summary>
        /// 打撃防御倍率
        /// </summary>
        public float strikeDefMultipler;

        /// <summary>
        /// 聖防御倍率
        /// </summary>
        public float holyDefMultipler;
        /// <summary>
        /// 闇防御倍率
        /// </summary>
        public float darkDefMultipler;
        /// <summary>
        /// 炎防御倍率
        /// </summary>
        public float fireDefMultipler;

        /// <summary>
        /// 雷防御倍率
        /// </summary>
        public float thunderDefMultipler;

        /// <summary>
        /// 衝撃倍率
        /// </summary>
        public float shockMultipler;
    }

    /// <summary>
    /// 入れ替えるデータ
    /// エンチャントの内容やガード方向など
    /// 入れ替えタイミングは装備変更とエンチャ効果切れ時
    /// </summary>
    public struct TemporaryData
    {
        /// <summary>
        /// エンチャなどによるガード時の水増しカット率
        /// </summary>
        public float additionalGuardCut;


        /// <summary>
        /// 追加のガード受け値
        /// </summary>
        public float additionalGuardPower;


        /// <summary>
        /// ガード方向
        /// 1が普通
        /// -1が背後
        /// 0が全身
        /// 背後ガード技とかあるだろうし、攻撃時にもやるか？
        /// </summary>
        public float guardDirection;

        /// <summary>
        /// ガード時の状態異常カット
        /// </summary>
        public float guardConditionCut;


        /// <summary>
        /// ガードデータからセット
        /// </summary>
        /// <param name="data"></param>
        public void DataSet(in GuardStatus data)
        {
            additionalGuardCut = data.additionalCut;

            additionalGuardPower = data.additionalGuardPower;

            guardDirection = data.guardDirection;

            guardConditionCut = data.badConditionCut;
        }

    }


    #endregion

    /// <summary>
    /// 防御力
    /// </summary>
    [HideInInspector]
    public DefStatus status;

    /// <summary>
    /// 防御倍率
    /// </summary>
    [HideInInspector]
    public DefMultipler multipler;

    /// <summary>
    /// ガードステータス
    /// </summary>
    [HideInInspector]
    public GuardStatus guardStatus;


    /// <summary>
    /// プレイヤーのみかな
    /// </summary>
    public bool nowParry;

    /// <summary>
    /// 防御関連の状態
    /// 頻繫に移り変わる
    /// </summary>
    public DefState state;


    /// <summary>
    /// 装備いれ替え、エンチャなどで変更されるデータ
    /// ガードに使用する
    /// </summary>
    public TemporaryData tempData;

    /// <summary>
    /// 条件を含むかをチェック
    /// </summary>
    /// <param name="checkCondition"></param>
    /// <returns></returns>
    public bool CheckFeature(DefState checkCondition)
    {
        return (state & checkCondition) > 0;
    }
}
