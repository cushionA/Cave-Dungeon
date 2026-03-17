using MoreMountains.CorgiEngine;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static CharacterController.AIManager;
using static CharacterController.StatusData.BrainStatus.TriggerJudgeData;

namespace CharacterController.StatusData
{
    /// <summary>
    /// 使用場面に基づいてデータを構造体に切り分けている。<br/>
    /// 
    /// このクラスではキャラクターの種類ごとの設定データを定義している。<br/>
    /// いわゆるステータスデータ。<br/>
    /// 可変部分（座標やHP）、キャラクターの意思決定に使用する部分（判断条件等）はJobシステムで使用する前提で値型のみで構成。<br/>
    /// 他の部分はScriptableObjectだけが不変の共有データとして持っていればいいため、参照型(エフェクトのデータなど)も使う。<br/>
    /// 
    /// 管理方針
    /// 更新されるデータ：SOA構造のキャラデータ保管庫で管理
    /// 固定データ（値型）：キャラの種類ごとにキャラデータを収めた配列をScriptableで作成し、MemCpyでNativeArrayに引っ張る。
    /// 　　　　　　        シスターさんみたいな作戦が変更されるやつは、最大値で事前にバッファしておく。
    /// 固定データ（参照型）：キャラの種類ごとにキャラデータを収めた配列をScriptableで持っておく。
    /// 
    /// 共通：キャラの種類ごとにキャラデータを収めた配列にはキャラIDでアクセスする。
    /// </summary>
    [CreateAssetMenu(fileName = "BrainStatus", menuName = "Scriptable Objects/BrainStatus")]
    public partial class BrainStatus : SerializedScriptableObject
    {
        #region Enum定義

        /// <summary>
        /// 自分がターゲットを再設定やモード変更する条件。
        /// また、クールタイム解消条件にも使用する。
        /// ○○の時、攻撃・回復・支援・逃走・護衛など
        /// 対象が自分、味方、敵のどれかという区分と否定（以上が以内になったり）フラグの組み合わせで表現 - > IsInvertフラグ
        /// 味方が死んだとき、は死亡状態で数秒キャラを残すことで、死亡を条件にしたフィルターにかかるようにする。
        /// 
        /// 各条件に優先度をつけることで、現在のターゲットが敵で挑発状態か、とか命令で指定された相手か、とかの縛りを超えられるようにする
        /// </summary>
        public enum ActTriggerCondition : byte
        {
            特定の対象が一定数いる時 = 1, //    フィルターを使う。数も入れてn体以上、って形にする。
            HPが一定割合の対象がいる時,
            MPが一定割合の対象がいる時,
            対象のキャラの周囲に特定陣営が一定以上密集している時, // 認識データからそいつの敵味方の近距離数を使う
            対象のキャラの周囲に特定陣営が一定以下しかいない時,
            周囲に指定のオブジェクトや地形がある時, // 認識データからオブジェクトの種類を使う
            対象が一定数の敵に狙われている時,// 陣営フィルタリング
            対象のキャラの一定距離以内に飛び道具がある時, // 認識データから近距離探知の方の飛び道具の検知を使う。一瞬だけ味方全員守れる盾とか用意するか
            特定のイベントが発生した時, // イベントシステムで発生したイベントを確認する。確認するまでは起きたイベントは消さない。
                           // 判定値には陣営とイベントをセットする。
            条件なし = 0 // 何も当てはまらなかった時の補欠条件。
        }

        /// <summary>
        /// ターゲットを選択する際の条件
        /// 条件に当てはまる敵のヘイト値が上昇したり減少したりする。
        /// あるいは味方の支援・回復・護衛対象を決める
        /// これも否定フラグとの組み合わせで使う
        /// </summary>
        public enum TargetSelectCondition : byte
        {

            高度,
            HP割合,
            HP,
            敵に狙われてる数,//一番狙われてるか、狙われてないか
            合計攻撃力,
            合計防御力,
            斬撃攻撃力,//特定の属性の攻撃力が一番高い/低いヤツ
            刺突攻撃力,
            打撃攻撃力,
            炎攻撃力,
            雷攻撃力,
            光攻撃力,
            闇攻撃力,
            斬撃防御力,
            刺突防御力,
            打撃防御力,
            炎防御力,
            雷防御力,
            光防御力,
            闇防御力,
            自分,
            プレイヤー,
            シスターさん,
            プレイヤー陣営の密集人数,
            魔物陣営の密集人数,
            その他陣営の密集人数,
            条件を満たす対象にとって最もヘイトが高いキャラ,
            条件を満たす対象に最後に攻撃したキャラ,
            指定なし_フィルターのみ, // 基本の条件。対象の中でフィルターに当てはまった相手を選ぶ
        }

        /// <summary>
        /// ターゲットが固定された状態で行動を選ぶための条件。
        /// ○○の時、攻撃・回復・支援・逃走・護衛など
        /// 
        /// この条件でもターゲット変更とかトリガーできるようにする？
        /// </summary>
        public enum MoveSelectCondition : byte
        {
            // 対象は自分かターゲットから選べる。フラグでね
            対象がフィルターに当てはまる時 = 1, // フィルターを使う。他の条件でもフィルターは使える
            対象のHPが一定割合の時,
            対象のMPが一定割合の時,
            対象の周囲に特定陣営のキャラが一定以上密集している時, // 認識データからそいつの敵味方の近距離数を使う
            対象の周囲に特定陣営のキャラが一定以下しかいない時, // 認識データからそいつの敵味方の近距離数を使う
            対象の周囲に指定のオブジェクトや地形がある時, // 認識データからオブジェクトの種類を使う
            対象が特定の数の敵に狙われている時,// 陣営フィルタリング
            対象の一定距離以内に飛び道具がある時, // 認識データから近距離探知の方の飛び道具の検知を使う。一瞬だけ味方全員守れる盾とか用意するか
            特定のイベントが発生した時, // イベントシステムで発生したイベントを確認する。確認するまでは起きたイベントは消さない。
                           // 判 定値には陣営とイベントをセットする。対象とは関係ない判断条件も少しは入れるか
            ///モードチェンジから特定の秒数が経過した時,// モードチェンジ後の最初の行動や、n秒経過後の行動を制御。
            // コントローラーでモード系のデータを記録してイベント出すのでイベントに統合
            ターゲットが自分の場合,
            条件なし = 0 // 何も当てはまらなかった時の補欠条件。
        }

        /// <summary>
        /// 判断の結果選択される行動のタイプ。
        /// これは行動を起こした後に今何をしている、という感じで使う？
        /// </summary>
        [Flags]
        public enum ActState : byte
        {
            指定なし = 0,// ステートフィルター判断で使う。何も指定しない。
            逃走 = 1 << 0,
            攻撃 = 1 << 1,
            移動 = 1 << 2,// 攻撃後のクールタイム中など。この状態で動作する回避率を設定する？ 移動もする
            防御 = 1 << 3,// 動き出す距離を設定できるようにする？ その場で基本ガードだけど、相手がいくらか離れたら動き出す、的な
            支援 = 1 << 4,
            回復 = 1 << 5,
            ターゲット変更 = 1 << 6, // ターゲットを変更する。
            モード変更 = 1 << 7, // モードの変更
        }

        /// <summary>
        /// キャラクターの属性。
        /// ここに当てはまる分全部入れてビットフラグチェック。
        /// </summary>
        [Flags]
        public enum CharacterFeature
        {
            プレイヤー = 1 << 0,
            シスターさん = 1 << 1,
            NPC = 1 << 2,
            通常エネミー = 1 << 3,
            ボス = 1 << 4,
            兵士 = 1 << 5,//陸の雑兵
            飛行 = 1 << 6,//飛ぶやつ
            射手 = 1 << 7,//遠距離
            騎士 = 1 << 8,//盾持ち
            罠系 = 1 << 9,//待ち構えてるやつ
            強敵 = 1 << 10,// 強敵
            ザコ = 1 << 11,
            ヒーラー = 1 << 12,
            サポーター = 1 << 13,
            高速 = 1 << 14,
            指揮官 = 1 << 15,
            戦闘状態 = 1 << 16, // 戦闘中のキャラ
            指定なし = 0//指定なし
        }

        /// <summary>
        /// キャラクターが所属する陣営
        /// </summary>
        [Flags]
        public enum CharacterBelong : byte
        {
            プレイヤー = 1 << 0,// 味方
            魔物 = 1 << 1,// 一般的な敵
            その他 = 1 << 2,// それ以外
            指定なし = 0
        }

        /// <summary>
        /// 属性の列挙型
        /// 状態異常は分ける。
        /// </summary>
        [Flags]
        public enum Element : byte
        {
            斬撃属性 = 1 << 0,
            刺突属性 = 1 << 1,
            打撃属性 = 1 << 2,
            聖属性 = 1 << 3,
            闇属性 = 1 << 4,
            炎属性 = 1 << 5,
            雷属性 = 1 << 6,
            指定なし = 0
        }

        /// <summary>
        /// キャラのレベル
        /// このレベルが高いと他の敵に邪魔されない
        /// </summary>
        public enum CharacterRank : byte
        {
            ザコ,//雑魚
            主力級,//基本はこれ
            指揮官,//強モブ
            ボス//ボスだけ
        }

        /// <summary>
        /// 特殊状態
        /// </summary>
        [Flags]
        public enum SpecialEffect : byte
        {
            ヘイト増大 = 1 << 1,
            ヘイト減少 = 1 << 2,
            なし = 0,
        }

        /// <summary>
        /// 認識しているオブジェクトを示すビットフラグ
        /// </summary>
        [Flags]
        public enum RecognizeObjectType : short
        {
            何もなし = 0,
            アイテム = 1 << 0, // アイテムを認識
            プレイヤー側キャラ = 1 << 1, // プレイヤー側のキャラを認識
            魔物側キャラ = 1 << 2, // 敵側のキャラを認識
            中立側キャラ = 1 << 3, // 中立側のキャラを認識
            危険物 = 1 << 4, // 危険物を認識
            //飛び道具攻撃 = 1 << 5, // 飛び道具の検知は視界センサーに一任
            バフエリア = 1 << 5, // バフエリアを認識
            デバフエリア = 1 << 6, // デバフエリアを認識
            水場 = 1 << 7, // 水場を認識
            毒沼 = 1 << 8, // 毒沼を認識
            ダメージエリア = 1 << 9, // ダメージエリアを認識
            破壊可能オブジェクト = 1 << 10, // 破壊可能なオブジェクトを認識
            よじ登りポイント = 1 << 11, // 崖を認識
        }

        /// <summary>
        /// bitableな真偽値
        /// Jobシステム、というよりネイティブコードと bool の相性が良くないため実装
        /// </summary>
        public enum BitableBool : byte
        {
            FALSE = 0,
            TRUE = 1
        }

        #endregion Enum定義

        #region 構造体定義

        /// <summary>
        /// 各属性に対する攻撃力または防御力の値を保持する構造体
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct ElementalStatus
        {
            /// <summary>
            /// 斬撃属性の値
            /// </summary>
            [Header("斬撃属性")]
            public short slash;

            /// <summary>
            /// 刺突属性の値
            /// </summary>
            [Header("刺突属性")]
            public short pierce;

            /// <summary>
            /// 打撃属性の値
            /// </summary>
            [Header("打撃属性")]
            public short strike;

            /// <summary>
            /// 炎属性の値
            /// </summary>
            [Header("炎属性")]
            public short fire;

            /// <summary>
            /// 雷属性の値
            /// </summary>
            [Header("雷属性")]
            public short lightning;

            /// <summary>
            /// 光属性の値
            /// </summary>
            [Header("光属性")]
            public short light;

            /// <summary>
            /// 闇属性の値
            /// </summary>
            [Header("闇属性")]
            public short dark;

            /// <summary>
            /// 属性から要素を取得するインデクサ
            /// </summary>
            /// <param name="element"></param>
            /// <returns></returns>
            /// <exception cref="IndexOutOfRangeException"></exception>
            public short this[Element element]
            {
                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                get
                {
                    // ビットが立っている位置を取得
                    int index = BrainStatus.ElementIndices[element];

                    return index switch
                    {
                        0 => this.slash,
                        1 => this.pierce,
                        2 => this.strike,
                        3 => this.light,
                        4 => this.dark,
                        5 => this.fire,
                        6 => this.lightning,
                        _ => throw new IndexOutOfRangeException("指定された属性は存在しません。")
                    };
                }

                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                set
                {
                    // ビットが立っている位置を取得
                    int index = BrainStatus.ElementIndices[element];

                    switch ( index )
                    {
                        case 0:
                            this.slash = value;
                            break;
                        case 1:
                            this.pierce = value;
                            break;
                        case 2:
                            this.strike = value;
                            break;
                        case 3:
                            this.light = value;
                            break;
                        case 4:
                            this.dark = value;
                            break;
                        case 5:
                            this.fire = value;
                            break;
                        case 6:
                            this.lightning = value;
                            break;
                        default:
                            throw new IndexOutOfRangeException("指定された属性は存在しません。");
                    }
                }
            }

            /// <summary>
            /// 合計値を返す。
            /// </summary>
            /// <returns></returns>
            public int ReturnSum()
            {
                return this.slash + this.pierce + this.strike + this.fire + this.lightning + this.light + this.dark;
            }
        }

        /// <summary>
        /// センサーを通じて獲得した周囲の認識データ。
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct RecognitionData
        {
            /// <summary>
            /// 至近距離のプレイヤー陣営キャラの数
            /// </summary>
            public byte nearlyPlayerSideCount;

            /// <summary>
            /// 至近距離の敵キャラ陣営のキャラの数
            /// </summary>
            public byte nearlyMonsterSideCount;

            /// <summary>
            /// 至近距離の中立陣営キャラの数
            /// </summary>
            public byte nearlyOtherSideCount;

            /// <summary>
            /// 現在認識しているオブジェクト情報
            /// </summary>
            public RecognizeObjectType recognizeObject;

            /// <summary>
            /// 視認した中で最も近い敵の攻撃オブジェクトの距離
            /// </summary>
            public float detectNearestAttackDistance;

            /// <summary>
            /// 自分や味方に攻撃してきた敵のハッシュ値
            /// これは攻撃スコアが一番大きい敵
            /// 自分の場合はダメージの値だけ、味方の場合はダメージの半分
            /// 回復や支援は無視する。
            /// ヘイト増大の場合はこの値に1.2倍、減少の場合は0.8倍にしたスコアで評価
            /// 攻撃された奴のハッシュと攻撃したやつのハッシュとそいつが挑発かどうかを含んだイベントを飛ばすか
            /// </summary>
            public int hateEnemyHash;

            /// <summary>
            /// 最後に自分に攻撃してきた相手のハッシュ値。
            /// </summary>
            public int attackerHash;

            public void Reset()
            {
                this.nearlyPlayerSideCount = 0;
                this.nearlyMonsterSideCount = 0;
                this.nearlyOtherSideCount = 0;
                this.recognizeObject = RecognizeObjectType.何もなし;
            }
        }

        /// <summary>
        /// キャラの行動（歩行速度とか）のステータス。
        /// 移動速度など。
        /// 横と縦どちらの距離を優先で詰めるかとかジャンプ関連のやつも入れた方がいいかもな
        /// 16Byte
        /// SoA Ok
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct MoveStatus
        {
            /// <summary>
            /// 通常の移動速度
            /// </summary>
            [Header("通常移動速度")]
            public short moveSpeed;

            /// <summary>
            /// 歩行速度。後ろ歩きも同じ
            /// </summary>
            [Header("歩行速度")]
            public short walkSpeed;

            /// <summary>
            /// ダッシュ速度
            /// </summary>
            [Header("ダッシュ速度")]
            public short dashSpeed;

            /// <summary>
            /// ジャンプの高さ。
            /// </summary>
            [Header("ジャンプの高さ")]
            public short jumpHeight;
        }

        #region 実行時キャラクター情報関連の構造体定義

        /// <summary>
        /// BaseImfo region - キャラクターの基本情報（HP、MP、位置）
        /// サイズ: 22バイト
        /// 用途: 毎フレーム更新される基本ステータス(ID以外)
        /// SoA OK
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CharacterBaseInfo
        {
            /// <summary>
            /// 最大体力
            /// </summary>
            public short maxHp;

            /// <summary>
            /// 体力
            /// </summary>
            public short currentHp;

            /// <summary>
            /// 最大魔力
            /// </summary>
            public short maxMp;

            /// <summary>
            /// 魔力
            /// </summary>
            public short currentMp;

            /// <summary>
            /// 最大スタミナ
            /// </summary>
            public short maxStamina;

            /// <summary>
            /// スタミナ
            /// </summary>
            public short currentStamina;

            /// <summary>
            /// 最大アーマー
            /// </summary>
            public short maxArmor;

            /// <summary>
            /// アーマー
            /// </summary>
            public short currentArmor;

            /// <summary>
            /// HPの割合
            /// </summary>
            public byte hpRatio;

            /// <summary>
            /// MPの割合
            /// </summary>
            public byte mpRatio;

            /// <summary>
            /// 現在位置
            /// </summary>
            public float2 nowPosition;

            /// <summary>
            /// HP/MP割合を更新する
            /// </summary>
            public void UpdateRatios()
            {
                this.hpRatio = (byte)(this.maxHp > 0 ? this.currentHp * 100 / this.maxHp : 0);
                this.mpRatio = (byte)(this.maxMp > 0 ? this.currentMp * 100 / this.maxMp : 0);
            }

            /// <summary>
            /// CharacterBaseDataから基本情報を設定
            /// </summary>
            public CharacterBaseInfo(in CharacterBaseData baseData, Vector2 initialPosition)
            {
                this.maxHp = baseData.hp;
                this.maxMp = baseData.mp;
                this.currentHp = baseData.hp;
                this.currentMp = baseData.mp;
                this.maxArmor = baseData.armor;
                this.maxStamina = baseData.stamina;
                this.currentArmor = baseData.armor;
                this.currentStamina = baseData.stamina;
                this.hpRatio = 100;
                this.mpRatio = 100;
                this.nowPosition = initialPosition;
            }
        }

        /// <summary>
        /// 常に変わらないデータを格納する構造体。
        /// BaseDataとの違いは、初期化以降頻繁に参照する必要があるか。
        /// SoA OK 20Byte
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct SolidData
        {

            /// <summary>
            /// 攻撃属性を示す列挙型
            /// ビット演算で見る
            /// NPCだけ入れる
            /// なに属性の攻撃をしてくるかというところ
            /// </summary>
            [Header("攻撃属性")]
            public Element attackElement;

            /// <summary>
            /// 弱点属性を示す列挙型
            /// ビット演算で見る
            /// NPCだけ入れる
            /// </summary>
            [Header("弱点属性")]
            public Element weakPoint;

            /// <summary>
            /// キャラの属性というか
            /// 特徴を示す。種類も包括
            /// </summary>
            [Header("キャラクター特徴")]
            public CharacterFeature feature;

            /// <summary>
            /// キャラの階級。<br/>
            /// これが上なほど味方の中で同じ敵をターゲットにしててもお控えしなくて済む、優先的に殴れる。<br/>
            /// あとランク低い味方に命令飛ばしたりできる。
            /// </summary>
            [Header("チーム内での階級")]
            public CharacterRank rank;

            /// <summary>
            /// この数値以上の敵から狙われている相手がターゲットになった場合、一旦次の判断までは待機になる
            /// その次の判断でやっぱり一番ヘイト高ければ狙う。(狙われまくってる相手へのヘイトは下がるので、普通はその次の判断でべつのやつが狙われる)
            /// 様子伺う、みたいなステート入れるか専用で
            /// 一定以上に狙われてる相手かつ、様子伺ってるキャラの場合だけヘイト下げるようにしよう。
            /// </summary>
            [Header("ターゲット上限")]
            [Tooltip("この数値以上の敵から狙われている相手が攻撃対象になった場合、一旦次の判断までは待機になる")]
            public int targetingLimit;
        }

        /// <summary>
        /// 参照頻度が少なく、加えて連続参照されないデータを集めた構造体。
        /// 50byte
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CharacterColdLog
        {
            /// <summary>
            /// キャラクターのマスタ－データ上のID
            /// </summary>
            public readonly byte characterID;

            /// <summary>
            /// キャラクターのハッシュ値を保存しておく。
            /// </summary>
            public int hashCode;

            /// <summary>
            /// 最後に判断した時間。
            /// xがターゲット判断でyが行動判断、zが移動判断の間隔。
            /// wがトリガー判断の間隔
            /// </summary>
            public float4 lastJudgeTime;

            /// <summary>
            /// 現在のモード
            /// </summary>
            public byte nowMode; // 現在のモード。モード変更時に更新される

            /// <summary>
            /// 現在のクールタイムのデータ。
            /// </summary>
            [HideInInspector]
            public CoolTimeData nowCoolTime;

            public CharacterColdLog(BrainStatus status, int hash)
            {
                this.characterID = (byte)status.characterID;
                this.hashCode = hash;
                // 最初はマイナスで10000を入れることですぐ動けるように
                this.lastJudgeTime = -10000;
                this.nowMode = 0;
                this.nowCoolTime = new CoolTimeData();
            }

        }

        /// <summary>
        /// 攻撃力のデータ
        /// サイズ: 32バイト
        /// 用途: 戦闘計算時にアクセス
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct CharacterAtkStatus
        {
            /// <summary>
            /// 各属性の基礎攻撃力
            /// </summary>
            public ElementalStatus atk;

            /// <summary>
            /// 全攻撃力の加算
            /// </summary>
            [OnValueChanged("UpdateDisplayAttack")]
            public int dispAtk;

            /// <summary>
            /// 表示用攻撃力を更新
            /// </summary>
            public void UpdateDisplayAttack()
            {
                this.dispAtk = this.atk.ReturnSum();
            }

            /// <summary>
            /// CharacterBaseDataから戦闘ステータスを設定
            /// </summary>
            public CharacterAtkStatus(in CharacterBaseData baseData)
            {
                this.atk = baseData.baseAtk;
                this.dispAtk = this.atk.ReturnSum();
            }
        }

        /// <summary>
        /// 防御力のデータ
        /// サイズ: 40バイト
        /// 用途: 戦闘計算時にアクセス
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct CharacterDefStatus
        {

            /// <summary>
            /// 各属性の基礎防御力
            /// </summary>
            [Header("防御ステータス")]
            [OnValueChanged("UpdateDisplayDefense")]
            public ElementalStatus def;

            /// <summary>
            /// 全防御力の加算
            /// </summary>
            [HideInInspector]
            public int dispDef;

            /// <summary>
            /// 盾のカット率や受け値のデータ
            /// 装備変更時に入れ替える
            /// </summary>
            [Header("盾のステータス")]
            public ShieldStatus shieldData;

            /// <summary>
            /// 表示用防御力を更新
            /// </summary>
            public void UpdateDisplayDefense()
            {
                this.dispDef = this.def.ReturnSum();
            }

            /// <summary>
            /// CharacterBaseDataから戦闘ステータスを設定
            /// </summary>
            public CharacterDefStatus(in CharacterBaseData baseData, ShieldStatus shieldStatus)
            {
                this.def = baseData.baseDef;
                this.shieldData = shieldStatus;
                this.dispDef = this.def.ReturnSum();
            }

            /// <summary>
            /// 防御と盾のデータをまとめて取得
            /// </summary>
            /// <returns></returns>
            public (ElementalStatus, ShieldStatus) GetDefAndShield()
            {
                return (this.def, this.shieldData);
            }
        }

        /// <summary>
        /// 盾のデータ
        /// サイズ: 8バイト
        /// 用途: 戦闘計算時にアクセス
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct ShieldStatus
        {
            /// <summary>
            /// 斬撃属性のカット率
            /// </summary>
            [Header("斬撃属性")]
            [Range(1, 100)]
            public byte slashCut;

            /// <summary>
            /// 刺突属性のカット率
            /// </summary>
            [Header("刺突属性")]
            [Range(1, 100)]
            public byte pierceCut;

            /// <summary>
            /// 打撃属性のカット率
            /// </summary>
            [Header("打撃属性")]
            [Range(1, 100)]
            public byte strikeCut;

            /// <summary>
            /// 炎属性のカット率
            /// </summary>
            [Header("炎属性")]
            [Range(1, 100)]
            public byte fireCut;

            /// <summary>
            /// 雷属性のカット率
            /// </summary>
            [Header("雷属性")]
            [Range(1, 100)]
            public byte lightningCut;

            /// <summary>
            /// 光属性のカット率
            /// </summary>
            [Header("光属性")]
            [Range(1, 100)]
            public byte lightCut;

            /// <summary>
            /// 闇属性のカット率
            /// </summary>
            [Header("闇属性")]
            [Range(1, 100)]
            public byte darkCut;

            /// <summary>
            /// 状態異常のカット
            /// </summary>
            [Header("状態異常カット率")]
            [Range(1, 100)]
            public byte badStatusCut;

            /// <summary>
            /// 盾削り軽減
            /// </summary>
            [Header("受け値")]
            [Range(1, 100)]
            public byte guardPower;

            /// <summary>
            /// 属性から要素を取得するインデクサ
            /// </summary>
            /// <param name="element"></param>
            /// <returns></returns>
            /// <exception cref="IndexOutOfRangeException"></exception>
            public byte this[Element element]
            {
                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                get
                {
                    // ビットが立っている位置を取得
                    int index = BrainStatus.ElementIndices[element];

                    return index switch
                    {
                        0 => this.slashCut,
                        1 => this.pierceCut,
                        2 => this.strikeCut,
                        3 => this.lightCut,
                        4 => this.darkCut,
                        5 => this.fireCut,
                        6 => this.lightningCut,
                        _ => throw new IndexOutOfRangeException("指定された属性は存在しません。")
                    };
                }

                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                set
                {
                    // ビットが立っている位置を取得
                    int index = BrainStatus.ElementIndices[element];

                    switch ( index )
                    {
                        case 0:
                            this.slashCut = value;
                            break;
                        case 1:
                            this.pierceCut = value;
                            break;
                        case 2:
                            this.strikeCut = value;
                            break;
                        case 3:
                            this.lightCut = value;
                            break;
                        case 4:
                            this.darkCut = value;
                            break;
                        case 5:
                            this.fireCut = value;
                            break;
                        case 6:
                            this.lightningCut = value;
                            break;
                        default:
                            throw new IndexOutOfRangeException("指定された属性は存在しません。");
                    }
                }
            }

        }

        /// <summary>
        /// StateImfo region - キャラクターの状態情報
        /// サイズ: 17バイト（1キャッシュラインの25%）
        /// 用途: AI判断、状態管理
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct CharacterStateInfo
        {

            /// <summary>
            /// 現在のキャラクターの所属
            /// </summary>
            public CharacterBelong belong;

            /// <summary>
            /// 現在の行動状況
            /// 判断間隔経過したら更新？
            /// 攻撃されたりしたら更新？
            /// あと仲間からの命令とかでも更新していいかも
            /// 
            /// 移動とか逃走でAIの動作が変わる。
            /// 逃走の場合は敵の距離を参照して相手が少ないところに逃げようと考えたり
            /// </summary>
            public ActState actState;

            /// <summary>
            /// バフやデバフなどの現在の効果
            /// </summary>
            public SpecialEffect nowEffect;

            /// <summary>
            /// AIが他者の行動を認識するためのイベントフラグ
            /// 列挙型AIEventFlagType　のビット演算に使う
            /// AIManagerがフラグ管理はしてくれる
            /// </summary>
            public BrainEventFlagType brainEvent;

            /// <summary>
            /// キャラの現在の状態を示すフラグ。
            /// </summary>
            public MyCharacterStates.CharacterConditions conditionState;

            /// <summary>
            /// 自分を狙ってる敵の数。
            /// ボスか指揮官は無視でよさそう
            /// 今攻撃してるやつも攻撃を終えたら別のターゲットを狙う。
            /// このタイミングで割りこめるやつが割り込む
            /// あくまでヘイト値を減らす感じで。一旦待機になって、ヘイト減るだけなので殴られたら殴り返すよ
            /// 遠慮状態以外なら遠慮になるし、遠慮中でなお一番ヘイト高いなら攻撃して、その次は遠慮になる
            /// </summary>
            public int targetingCount;

            /// <summary>
            /// 状態をリセット（初期化時用）
            /// </summary>
            public void ResetStates()
            {
                this.nowEffect = SpecialEffect.なし;
                this.brainEvent = BrainEventFlagType.None;
            }

            /// <summary>
            /// CharacterBaseDataから状態情報を設定
            /// </summary>
            public CharacterStateInfo(in CharacterBaseData baseData)
            {
                this.belong = baseData.initialBelong;
                this.actState = baseData.initialMove;
                this.nowEffect = SpecialEffect.なし;
                this.brainEvent = BrainEventFlagType.None;
                conditionState = MyCharacterStates.CharacterConditions.通常;
                this.targetingCount = 0;
            }
        }

        #endregion キャラクターデータ関連の構造体定義

        #region 判断関連(Job使用)

        /// <summary>
        /// AI設定データのJob System用構造体
        /// 
        /// キャラクターのAI判断に必要なデータをJob Systemで効率的に使用できるよう、
        /// ジャグ配列をフラット化して保持します。
        /// 各キャラクターのデータはID順（1ベース）でマッピングされています。
        /// シスターさんだけ可変なので別管理
        /// 
        /// 使用方法：
        /// - 初期化時にCharacterModeDataのジャグ配列を渡す
        /// - 各Jobで必要なデータをGetメソッドで取得
        /// - ReadOnlyでの使用を推奨
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BrainDataForJob : IDisposable
        {
            #region 判断間隔データ

            /// <summary>
            /// 全キャラクターの判断間隔データ（フラット化済み）
            /// x: ターゲット判断間隔
            /// y: 行動判断間隔
            /// z: 移動判断間隔
            /// </summary>
            private NativeArray<float3> _intervalData;

            /// <summary>
            /// 各キャラクターの判断間隔データ開始位置
            /// インデックス = キャラクターID - 1
            /// 値 = _intervalDataIndexRangeData内での開始インデックス
            /// </summary>
            private NativeArray<int> _intervalDataIndexRangeStart;

            /// <summary>
            /// 各キャラクター・各モードの判断間隔データ位置情報
            /// x: _intervalData内での開始インデックス
            /// y: データ長（通常は1）
            /// </summary>
            private NativeArray<int2> _intervalDataIndexRangeData;

            /// <summary>
            /// シスター専用：判断間隔データ（最大5モード分）
            /// </summary>
            private NativeArray<float3> _sisIntervalData;

            #endregion

            #region トリガー行動データ

            /// <summary>
            /// 全キャラクターのトリガー行動条件データ（フラット化済み）
            /// 優先度順に格納（最初の要素ほど優先度が高い）
            /// </summary>
            private NativeArray<TriggerJudgeData> _triggerCondition;

            /// <summary>
            /// 各キャラクターのトリガーデータ開始位置
            /// インデックス = キャラクターID - 1
            /// 値 = _triggerDataIndexRangeData内での開始インデックス
            /// </summary>
            private NativeArray<int> _triggerDataIndexRangeStart;

            /// <summary>
            /// 各キャラクター・各モードのトリガーデータ位置情報
            /// x: _triggerCondition内での開始インデックス
            /// y: データ長
            /// </summary>
            private NativeArray<int2> _triggerDataIndexRangeData;

            /// <summary>
            /// シスター専用：トリガー行動条件データ（最大5モード分）
            /// </summary>
            private NativeArray<TriggerJudgeData> _sisTriggerCondition;

            /// <summary>
            /// シスター専用：各モードの終了インデックス（累積）
            /// 最大5モードまで対応。
            /// x: モード1の終了位置（0から開始）
            /// y: モード2の終了位置（xから開始）
            /// z: モード3の終了位置（yから開始）
            /// w: モード4の終了位置（zから開始）
            /// モード5: wから配列長まで
            /// </summary>
            private int4 _sisTriggerIndexRange;

            #endregion

            #region ターゲット判断データ

            /// <summary>
            /// 全キャラクターのターゲット選択条件データ（フラット化済み）
            /// デフォルトはヘイトベース、条件指定時は行動もセットで決定
            /// </summary>
            private NativeArray<TargetJudgeData> _targetCondition;

            /// <summary>
            /// 各キャラクターのターゲットデータ開始位置
            /// インデックス = キャラクターID - 1
            /// 値 = _tDataIndexRangeData内での開始インデックス
            /// </summary>
            private NativeArray<int> _tDataIndexRangeStart;

            /// <summary>
            /// 各キャラクター・各モードのターゲットデータ位置情報
            /// x: _targetCondition内での開始インデックス
            /// y: データ長
            /// </summary>
            private NativeArray<int2> _tDataIndexRangeData;

            /// <summary>
            /// シスター専用：ターゲット選択条件データ（最大5モード分）
            /// </summary>
            private NativeArray<TargetJudgeData> _sisTargetCondition;

            /// <summary>
            /// シスター専用：各モードの終了インデックス（累積）
            /// 構造は_sisTriggerIndexRangeと同様（最大5モード対応）
            /// </summary>
            private int4 _sisTargetIndexRange;

            #endregion

            #region 行動判断データ

            /// <summary>
            /// 全キャラクターの行動選択条件データ（フラット化済み）
            /// 優先度順に格納（最初の要素ほど優先度が高い）
            /// </summary>
            private NativeArray<ActJudgeData> _actCondition;

            /// <summary>
            /// 各キャラクターの行動データ開始位置
            /// インデックス = キャラクターID - 1
            /// 値 = _actDataIndexRangeData内での開始インデックス
            /// </summary>
            private NativeArray<int> _actDataIndexRangeStart;

            /// <summary>
            /// 各キャラクター・各モードの行動データ位置情報
            /// x: _actCondition内での開始インデックス
            /// y: データ長
            /// </summary>
            private NativeArray<int2> _actDataIndexRangeData;

            /// <summary>
            /// シスター専用：行動選択条件データ（最大5モード分）
            /// </summary>
            private NativeArray<ActJudgeData> _sisActCondition;

            /// <summary>
            /// シスター専用：各モードの終了インデックス（累積）
            /// 構造は_sisTriggerIndexRangeと同様（最大5モード対応）
            /// </summary>
            private int4 _sisActIndexRange;

            #endregion

            /// <summary>
            /// CharacterModeDataのジャグ配列からJob用のフラット化データを構築
            /// 
            /// データ構造：
            /// - sourceData[0～n-2]: 通常キャラクターのデータ
            /// - sourceData[n-1]: シスターのデータ（特別処理）
            /// 
            /// フラット化の流れ：
            /// 1. 各キャラクターの各モードのデータを一次元配列に展開
            /// 2. インデックス管理用の配列で各データの位置を記録
            /// 3. GetSubArrayで高速アクセス可能な構造を実現
            /// </summary>
            /// <param name="sourceData">キャラクターモードデータのジャグ配列（値型構造体の配列）</param>
            /// <param name="allocator">メモリアロケータ（デフォルト: Persistent）</param>
            public BrainDataForJob(CharacterModeData[][] sourceData, Allocator allocator = Allocator.Persistent)
            {
                // 最後の要素はシスターのデータとして特別扱い
                int normalCharCount = sourceData.Length - 1;

                #region 判断間隔データの初期化

                // ===== 一時的なコンテナの準備 =====
                // 想定される最大サイズで初期化（キャラ数 × 最大モード数6）
                var intervalDataContainer = new UnsafeList<float3>(normalCharCount * 6, Allocator.Temp);
                var intervalDataRangeStartContainer = new UnsafeList<int>(normalCharCount, Allocator.Temp);
                var intervalDataRangeContainer = new UnsafeList<int2>(normalCharCount * 6, Allocator.Temp);

                // ===== 通常キャラクターの判断間隔データをフラット化 =====
                for ( int charId = 0; charId < normalCharCount; charId++ )
                {
                    // このキャラクターのモード範囲情報の開始位置を記録
                    // 例: charId=0なら0、charId=1で前キャラが3モードなら3
                    intervalDataRangeStartContainer.Add(intervalDataRangeContainer.Length);

                    // 各モードのデータを順次追加
                    for ( int mode = 0; mode < sourceData[charId].Length; mode++ )
                    {
                        // 現在のデータ位置と長さ（判断間隔は1要素のみ）を記録
                        intervalDataRangeContainer.Add(new int2(intervalDataContainer.Length, 1));

                        // 実際のデータを追加
                        intervalDataContainer.Add(sourceData[charId][mode].judgeInterval);
                    }
                }

                // ===== 一時コンテナから永続的なNativeArrayに変換 =====
                // ToArray()でUnsafeListの内部配列への参照を取得し、NativeArrayにコピー
                _intervalData = new NativeArray<float3>(intervalDataContainer.ToArray(), allocator);
                _intervalDataIndexRangeStart = new NativeArray<int>(intervalDataRangeStartContainer.ToArray(), allocator);
                _intervalDataIndexRangeData = new NativeArray<int2>(intervalDataRangeContainer.ToArray(), allocator);

                // ===== シスターの判断間隔データを設定 =====
                // シスターは特別扱いのため、独立した配列で管理
                var sisIntervalList = new List<float3>();
                for ( int mode = 0; mode < sourceData[normalCharCount].Length; mode++ )
                {
                    sisIntervalList.Add(sourceData[normalCharCount][mode].judgeInterval);
                }
                _sisIntervalData = new NativeArray<float3>(sisIntervalList.ToArray(), allocator);

                #endregion

                #region トリガー行動データの初期化

                // ===== 一時的なコンテナの準備 =====
                // トリガーデータは可変長のため、大きめに初期化
                var triggerDataContainer = new UnsafeList<TriggerJudgeData>(normalCharCount * 10, Allocator.Temp);
                var triggerDataRangeStartContainer = new UnsafeList<int>(normalCharCount, Allocator.Temp);
                var triggerDataRangeContainer = new UnsafeList<int2>(normalCharCount * 6, Allocator.Temp);

                // ===== 通常キャラクターのトリガーデータをフラット化 =====
                for ( int charId = 0; charId < normalCharCount; charId++ )
                {
                    // このキャラクターのインデックス範囲情報の開始位置を記録
                    triggerDataRangeStartContainer.Add(triggerDataRangeContainer.Length);

                    // 各モードのトリガー条件を処理
                    for ( int mode = 0; mode < sourceData[charId].Length; mode++ )
                    {
                        // このモードのトリガーデータ開始位置を記録
                        int startIndex = triggerDataContainer.Length;

                        // モード内の全トリガー条件を追加
                        var triggerConditions = sourceData[charId][mode].triggerCondition;
                        foreach ( var trigger in triggerConditions )
                        {
                            triggerDataContainer.Add(trigger);
                        }

                        // このモードのデータ範囲（開始位置と要素数）を記録
                        triggerDataRangeContainer.Add(new int2(startIndex, triggerConditions.Length));
                    }
                }

                // ===== NativeArrayに変換 =====
                _triggerCondition = new NativeArray<TriggerJudgeData>(triggerDataContainer.ToArray(), allocator);
                _triggerDataIndexRangeStart = new NativeArray<int>(triggerDataRangeStartContainer.ToArray(), allocator);
                _triggerDataIndexRangeData = new NativeArray<int2>(triggerDataRangeContainer.ToArray(), allocator);

                // ===== シスターのトリガーデータを累積インデックス方式で設定 =====
                var sisTriggerList = new List<TriggerJudgeData>();
                var sisTriggerRanges = new int4();
                int currentEndIndex = 0;

                // 最大5モードまで処理（int4の制限による）
                for ( int mode = 0; mode < sourceData[normalCharCount].Length && mode < 4; mode++ )
                {
                    var triggers = sourceData[normalCharCount][mode].triggerCondition;
                    sisTriggerList.AddRange(triggers);

                    // 累積終了位置を更新
                    currentEndIndex += triggers.Length;

                    // int4の各要素に累積終了インデックスを設定
                    // これにより、モード間の境界を効率的に管理
                    switch ( mode )
                    {
                        case 0:
                            sisTriggerRanges.x = currentEndIndex;
                            break;  // モード1: 0～x
                        case 1:
                            sisTriggerRanges.y = currentEndIndex;
                            break;  // モード2: x～y
                        case 2:
                            sisTriggerRanges.z = currentEndIndex;
                            break;  // モード3: y～z
                        case 3:
                            sisTriggerRanges.w = currentEndIndex;
                            break;  // モード4: z～w
                                    // モード5: w～配列長（自動的に決定）
                    }
                }

                _sisTriggerCondition = new NativeArray<TriggerJudgeData>(sisTriggerList.ToArray(), allocator);
                _sisTriggerIndexRange = sisTriggerRanges;

                #endregion

                #region ターゲット判断データの初期化

                // ===== 一時的なコンテナの準備 =====
                var targetDataContainer = new UnsafeList<TargetJudgeData>(normalCharCount * 10, Allocator.Temp);
                var targetDataRangeStartContainer = new UnsafeList<int>(normalCharCount, Allocator.Temp);
                var targetDataRangeContainer = new UnsafeList<int2>(normalCharCount * 6, Allocator.Temp);

                // ===== 通常キャラクターのターゲットデータをフラット化 =====
                // トリガーデータと同じパターンで処理
                for ( int charId = 0; charId < normalCharCount; charId++ )
                {
                    targetDataRangeStartContainer.Add(targetDataRangeContainer.Length);

                    for ( int mode = 0; mode < sourceData[charId].Length; mode++ )
                    {
                        int startIndex = targetDataContainer.Length;
                        var targetConditions = sourceData[charId][mode].targetCondition;

                        // 全ターゲット条件をフラット配列に追加
                        foreach ( var target in targetConditions )
                        {
                            targetDataContainer.Add(target);
                        }

                        targetDataRangeContainer.Add(new int2(startIndex, targetConditions.Length));
                    }
                }

                // ===== NativeArrayに変換 =====
                _targetCondition = new NativeArray<TargetJudgeData>(targetDataContainer.ToArray(), allocator);
                _tDataIndexRangeStart = new NativeArray<int>(targetDataRangeStartContainer.ToArray(), allocator);
                _tDataIndexRangeData = new NativeArray<int2>(targetDataRangeContainer.ToArray(), allocator);

                // ===== シスターのターゲットデータを設定 =====
                var sisTargetList = new List<TargetJudgeData>();
                var sisTargetRanges = new int4();
                currentEndIndex = 0;

                for ( int mode = 0; mode < sourceData[normalCharCount].Length && mode < 4; mode++ )
                {

                    var targets = sourceData[normalCharCount][mode].targetCondition;
                    sisTargetList.AddRange(targets);
                    currentEndIndex += targets.Length;

                    switch ( mode )
                    {
                        case 0:
                            sisTargetRanges.x = currentEndIndex;
                            break;
                        case 1:
                            sisTargetRanges.y = currentEndIndex;
                            break;
                        case 2:
                            sisTargetRanges.z = currentEndIndex;
                            break;
                        case 3:
                            sisTargetRanges.w = currentEndIndex;
                            break;
                    }
                }

                _sisTargetCondition = new NativeArray<TargetJudgeData>(sisTargetList.ToArray(), allocator);
                _sisTargetIndexRange = sisTargetRanges;

                #endregion

                #region 行動判断データの初期化

                // ===== 一時的なコンテナの準備 =====
                var actDataContainer = new UnsafeList<ActJudgeData>(normalCharCount * 10, Allocator.Temp);
                var actDataRangeStartContainer = new UnsafeList<int>(normalCharCount, Allocator.Temp);
                var actDataRangeContainer = new UnsafeList<int2>(normalCharCount * 6, Allocator.Temp);

                // ===== 通常キャラクターの行動データをフラット化 =====
                for ( int charId = 0; charId < normalCharCount; charId++ )
                {
                    actDataRangeStartContainer.Add(actDataRangeContainer.Length);

                    for ( int mode = 0; mode < sourceData[charId].Length; mode++ )
                    {
                        int startIndex = actDataContainer.Length;
                        var actConditions = sourceData[charId][mode].actCondition;

                        // 全行動条件をフラット配列に追加
                        foreach ( var act in actConditions )
                        {
                            actDataContainer.Add(act);
                        }

                        actDataRangeContainer.Add(new int2(startIndex, actConditions.Length));
                    }
                }

                // ===== NativeArrayに変換 =====
                _actCondition = new NativeArray<ActJudgeData>(actDataContainer.ToArray(), allocator);
                _actDataIndexRangeStart = new NativeArray<int>(actDataRangeStartContainer.ToArray(), allocator);
                _actDataIndexRangeData = new NativeArray<int2>(actDataRangeContainer.ToArray(), allocator);

                // ===== シスターの行動データを設定 =====
                var sisActList = new List<ActJudgeData>();
                var sisActRanges = new int4();
                currentEndIndex = 0;

                for ( int mode = 0; mode < sourceData[normalCharCount].Length && mode < 4; mode++ )
                {
                    var acts = sourceData[normalCharCount][mode].actCondition;
                    sisActList.AddRange(acts);
                    currentEndIndex += acts.Length;

                    switch ( mode )
                    {
                        case 0:
                            sisActRanges.x = currentEndIndex;
                            break;
                        case 1:
                            sisActRanges.y = currentEndIndex;
                            break;
                        case 2:
                            sisActRanges.z = currentEndIndex;
                            break;
                        case 3:
                            sisActRanges.w = currentEndIndex;
                            break;
                    }
                }

                _sisActCondition = new NativeArray<ActJudgeData>(sisActList.ToArray(), allocator);
                _sisActIndexRange = sisActRanges;

                #endregion

                // ===== 一時的なコンテナを解放 =====
                // Allocator.Tempで確保したメモリは明示的に解放
                intervalDataContainer.Dispose();
                intervalDataRangeStartContainer.Dispose();
                intervalDataRangeContainer.Dispose();
                triggerDataContainer.Dispose();
                triggerDataRangeStartContainer.Dispose();
                triggerDataRangeContainer.Dispose();
                targetDataContainer.Dispose();
                targetDataRangeStartContainer.Dispose();
                targetDataRangeContainer.Dispose();
                actDataContainer.Dispose();
                actDataRangeStartContainer.Dispose();
                actDataRangeContainer.Dispose();
            }

            /// <summary>
            /// 指定されたキャラクターID・モードの判断間隔データを取得
            /// </summary>
            /// <param name="id">キャラクターID（1ベース）</param>
            /// <param name="mode">モード番号（1ベース）</param>
            /// <returns>判断間隔データ（x:ターゲット, y:行動, z:移動）</returns>
            public float3 GetIntervalData(byte id, byte mode)
            {
                id--;
                mode--;

                // シスターの場合
                if ( id >= _intervalDataIndexRangeStart.Length )
                {
                    if ( mode >= 0 && mode < _sisIntervalData.Length )
                    {
                        return _sisIntervalData[mode];
                    }
                    return float3.zero;
                }

                // 通常キャラクターの場合
                if ( id >= 0 && id < _intervalDataIndexRangeStart.Length )
                {
                    int2 indexData = _intervalDataIndexRangeData[_intervalDataIndexRangeStart[id] + mode];
                    return _intervalData[indexData.x];
                }

                return float3.zero;
            }

            /// <summary>
            /// 指定されたキャラクターID・モードのトリガー判断データ配列を取得
            /// </summary>
            /// <param name="id">キャラクターID（1ベース）</param>
            /// <param name="mode">モード番号（1ベース）</param>
            /// <returns>トリガー判断データの配列（優先度順）</returns>
            public NativeArray<TriggerJudgeData> GetTriggerJudgeDataArray(int id, int mode)
            {
                id--;
                mode--;

                // シスターの場合（最大5モード対応）
                if ( id >= _triggerDataIndexRangeStart.Length )
                {
                    int startIndex = 0;
                    int endIndex = 0;

                    switch ( mode )
                    {
                        case 0:
                            startIndex = 0;
                            endIndex = _sisTriggerIndexRange.x;
                            break;
                        case 1:
                            startIndex = _sisTriggerIndexRange.x;
                            endIndex = _sisTriggerIndexRange.y;
                            break;
                        case 2:
                            startIndex = _sisTriggerIndexRange.y;
                            endIndex = _sisTriggerIndexRange.z;
                            break;
                        case 3:
                            startIndex = _sisTriggerIndexRange.z;
                            endIndex = _sisTriggerIndexRange.w;
                            break;
                        case 4:
                            startIndex = _sisTriggerIndexRange.w;
                            endIndex = _sisTriggerCondition.Length;
                            break;
                        default:
                            return new NativeArray<TriggerJudgeData>();
                    }

                    return _sisTriggerCondition.GetSubArray(startIndex, endIndex - startIndex);
                }

                // 通常キャラクターの場合
                if ( id >= 0 && id < _triggerDataIndexRangeStart.Length )
                {
                    int2 indexData = _triggerDataIndexRangeData[_triggerDataIndexRangeStart[id] + mode];
                    return _triggerCondition.GetSubArray(indexData.x, indexData.y);
                }

                return new NativeArray<TriggerJudgeData>();
            }

            /// <summary>
            /// 指定されたキャラクターID・モードのターゲット判断データ配列を取得
            /// </summary>
            /// <param name="id">キャラクターID（1ベース）</param>
            /// <param name="mode">モード番号（1ベース）</param>
            /// <returns>ターゲット判断データの配列</returns>
            public NativeArray<TargetJudgeData> GetTargetJudgeDataArray(int id, int mode)
            {
                id--;
                mode--;

                // シスターの場合（最大5モード対応）
                if ( id >= _tDataIndexRangeStart.Length )
                {
                    int startIndex;
                    int endIndex;

                    switch ( mode )
                    {
                        case 0:
                            startIndex = 0;
                            endIndex = _sisTargetIndexRange.x;
                            break;
                        case 1:
                            startIndex = _sisTargetIndexRange.x;
                            endIndex = _sisTargetIndexRange.y;
                            break;
                        case 2:
                            startIndex = _sisTargetIndexRange.y;
                            endIndex = _sisTargetIndexRange.z;
                            break;
                        case 3:
                            startIndex = _sisTargetIndexRange.z;
                            endIndex = _sisTargetIndexRange.w;
                            break;
                        case 4:
                            startIndex = _sisTargetIndexRange.w;
                            endIndex = _sisTargetCondition.Length;
                            break;
                        default:
                            return new NativeArray<TargetJudgeData>();
                    }

                    return _sisTargetCondition.GetSubArray(startIndex, endIndex - startIndex);
                }

                // 通常キャラクターの場合
                if ( id >= 0 && id < _tDataIndexRangeStart.Length )
                {
                    int2 indexData = _tDataIndexRangeData[_tDataIndexRangeStart[id] + mode];
                    return _targetCondition.GetSubArray(indexData.x, indexData.y);
                }

                return new NativeArray<TargetJudgeData>();
            }

            /// <summary>
            /// 指定されたキャラクターID・モードの行動判断データ配列を取得
            /// </summary>
            /// <param name="id">キャラクターID（1ベース）</param>
            /// <param name="mode">モード番号（1ベース）</param>
            /// <returns>行動判断データの配列（優先度順）</returns>
            public NativeArray<ActJudgeData> GetActJudgeDataArray(int id, int mode)
            {
                id--;
                mode--;

                // シスターの場合（最大5モード対応）
                if ( id >= _actDataIndexRangeStart.Length )
                {
                    int startIndex = 0;
                    int endIndex = 0;

                    switch ( mode )
                    {
                        case 0:
                            startIndex = 0;
                            endIndex = _sisActIndexRange.x;
                            break;
                        case 1:
                            startIndex = _sisActIndexRange.x;
                            endIndex = _sisActIndexRange.y;
                            break;
                        case 2:
                            startIndex = _sisActIndexRange.y;
                            endIndex = _sisActIndexRange.z;
                            break;
                        case 3:
                            startIndex = _sisActIndexRange.z;
                            endIndex = _sisActIndexRange.w;
                            break;
                        case 4:
                            startIndex = _sisActIndexRange.w;
                            endIndex = _sisActCondition.Length;
                            break;
                        default:
                            return new NativeArray<ActJudgeData>();
                    }

                    return _sisActCondition.GetSubArray(startIndex, endIndex - startIndex);
                }

                // 通常キャラクターの場合
                if ( id >= 0 && id < _actDataIndexRangeStart.Length )
                {
                    int2 indexData = _actDataIndexRangeData[_actDataIndexRangeStart[id] + mode];
                    return _actCondition.GetSubArray(indexData.x, indexData.y);
                }

                return new NativeArray<ActJudgeData>();
            }

            /// <summary>
            /// 全てのNativeArrayを解放
            /// アプリケーション終了時またはデータ不要時に必ず呼び出すこと
            /// </summary>
            public void Dispose()
            {
                // 判断間隔データの解放
                if ( _intervalData.IsCreated )
                    _intervalData.Dispose();
                if ( _intervalDataIndexRangeStart.IsCreated )
                    _intervalDataIndexRangeStart.Dispose();
                if ( _intervalDataIndexRangeData.IsCreated )
                    _intervalDataIndexRangeData.Dispose();
                if ( _sisIntervalData.IsCreated )
                    _sisIntervalData.Dispose();

                // トリガー行動データの解放
                if ( _triggerCondition.IsCreated )
                    _triggerCondition.Dispose();
                if ( _triggerDataIndexRangeStart.IsCreated )
                    _triggerDataIndexRangeStart.Dispose();
                if ( _triggerDataIndexRangeData.IsCreated )
                    _triggerDataIndexRangeData.Dispose();
                if ( _sisTriggerCondition.IsCreated )
                    _sisTriggerCondition.Dispose();

                // ターゲット判断データの解放
                if ( _targetCondition.IsCreated )
                    _targetCondition.Dispose();
                if ( _tDataIndexRangeStart.IsCreated )
                    _tDataIndexRangeStart.Dispose();
                if ( _tDataIndexRangeData.IsCreated )
                    _tDataIndexRangeData.Dispose();
                if ( _sisTargetCondition.IsCreated )
                    _sisTargetCondition.Dispose();

                // 行動判断データの解放
                if ( _actCondition.IsCreated )
                    _actCondition.Dispose();
                if ( _actDataIndexRangeStart.IsCreated )
                    _actDataIndexRangeStart.Dispose();
                if ( _actDataIndexRangeData.IsCreated )
                    _actDataIndexRangeData.Dispose();
                if ( _sisActCondition.IsCreated )
                    _sisActCondition.Dispose();
            }
        }

        /// <summary>
        /// トリガー判断データ
        /// </summary>
        [Serializable]
        public partial struct TriggerJudgeData
        {
            /// <summary>
            /// トリガーされる行動のタイプ
            /// </summary>
            public enum TriggerEventType : byte
            {
                モード変更 = 0,
                ターゲット変更 = 1,// ターゲット変更の際は優先するターゲット条件を指定できる。
                個別行動 = 2,
            }

#if UNITY_EDITOR
            [InfoBox("@GetConditionDescription()")]
#endif
            [Header("行動判定の条件")]
            public ActTriggerCondition judgeCondition;

            [Range(1, 100)]
            [Tooltip("1から100で表現する行動を実行する可能性")]
            public byte actRatio;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetLowerValueLabel()")]
            [ShowIf("@ShowLowerValue()")]
            [HideIf("@IsLowerValueEnum()")]
#endif
            [Header("基準となる値1")]
            public int judgeLowerValue;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetUpperValueLabel()")]
            [ShowIf("@ShowUpperValue()")]
            [HideIf("@IsUpperValueEnum()")]
#endif
            [Header("基準となる値2")]
            public int judgeUpperValue;

            public TriggerEventType triggerEventType;

#if UNITY_EDITOR
            [ShowIf("@ShowNormalTriggerNum()")]
#endif
            public byte triggerNum;

            [Header("チェック対象の条件")]
            public TargetFilter filter;
        }

        /// <summary>
        /// クールタイムデータ
        /// </summary>
        [Serializable]
        public partial struct CoolTimeData
        {
#if UNITY_EDITOR
            [InfoBox("@GetSkipConditionDescription()")]
#endif
            [Header("行動判定をスキップする条件")]
            public ActTriggerCondition skipCondition;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetLowerValueLabel()")]
            [ShowIf("@ShowLowerValue()")]
            [HideIf("@IsLowerValueEnum()")]
#endif
            [Header("基準となる値")]
            public int judgeLowerValue;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetUpperValueLabel()")]
            [ShowIf("@ShowUpperValue()")]
            [HideIf("@IsUpperValueEnum()")]
#endif
            [Header("基準となる値")]
            public int judgeUpperValue;

            public float coolTime;

            [Header("チェック対象の条件")]
            public TargetFilter filter;
        }

        /// <summary>
        /// 行動判断データ
        /// </summary>
        [Serializable]
        public partial struct ActJudgeData
        {
#if UNITY_EDITOR
            [InfoBox("@GetConditionDescription()")]
#endif
            [Header("行動判定の条件")]
            public MoveSelectCondition judgeCondition;

            [Range(1, 100)]
            [Tooltip("1から100で表現する行動を実行する可能性")]
            public byte actRatio;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetLowerValueLabel()")]
            [ShowIf("@ShowLowerValue()")]
            [HideIf("@IsLowerValueEnum()")]
#endif
            [Header("基準となる値1")]
            public int judgeLowerValue;

#if UNITY_EDITOR
            [ShowInInspector]
            [LabelText("@GetUpperValueLabel()")]
            [ShowIf("@ShowUpperValue()")]
            [HideIf("@IsUpperValueEnum()")]
#endif
            [Header("基準となる値2")]
            public int judgeUpperValue;

            public TriggerEventType triggerEventType;

#if UNITY_EDITOR
            [ShowIf("@ShowNormalTriggerNum()")]
#endif
            public byte triggerNum;

            [Tooltip("このフラグが真ならクールタイム中でも判断を行う")]
            public bool isCoolTimeIgnore;

            [Tooltip("このフラグが真なら判断は自分に対して行う")]
            public bool isSelfJudge;

            /// <summary>
            /// ターゲットが条件に当てはまるなら行動を実施
            /// </summary>
            [Header("チェック対象の条件")]
            public TargetFilter filter;
        }

        /// <summary>
        /// ターゲット判断データ
        /// </summary>
        [Serializable]
        public partial struct TargetJudgeData
        {
#if UNITY_EDITOR
            [InfoBox("@GetConditionDescription()")]
#endif
            [Header("ターゲット判断基準")]
            public TargetSelectCondition judgeCondition;

#if UNITY_EDITOR
            [InfoBox("@GetInvertDescription()")]
#endif
            [Header("基準反転フラグ")]
            public BitableBool isInvert;

            [Header("チェック対象の条件")]
            public TargetFilter filter;
        }

        /// <summary>
        /// 行動条件や対象設定条件で検査対象をフィルターするための構造体
        /// 19Byte
        /// SoA OK
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct TargetFilter : IEquatable<TargetFilter>
        {
            /// <summary>
            /// 各フィルター条件がAnd判断、つまり指定した全条件が当てはまるかどうかを判断するかをビットフラグで持つための列挙型。
            /// </summary>
            [Flags]
            public enum FilterBitFlag : byte
            {
                特徴フィルター_And判断 = 1 << 0,
                特殊効果フィルター_And判断 = 1 << 1,
                行動状態フィルター_And判断 = 1 << 2,
                イベントフィルター_And判断 = 1 << 3,
                弱点属性フィルター_And判断 = 1 << 4,
                使用属性フィルター_And判断 = 1 << 5,
                自分を対象にする = 1 << 6,
                プレーヤーを対象にする = 1 << 7,
            }

            /// <summary>
            /// 各フィルター条件のAND/OR判定を管理するビットフラグ
            /// </summary>
            [Header("フィルター判定方法")]
            [SerializeField]
            private FilterBitFlag _filterFlags;

            /// <summary>
            /// 対象の陣営区分
            /// 複数指定あり
            /// </summary>
            [Header("対象の陣営")]
            [SerializeField]
            private CharacterBelong _targetType;

            /// <summary>
            /// 対象の特徴
            /// 複数指定あり
            /// intEnum
            /// </summary>
            [Header("対象の特徴")]
            [SerializeField]
            private CharacterFeature _targetFeature;

            /// <summary>
            /// 対象の状態（バフ、デバフ）
            /// 複数指定あり
            /// </summary>
            [Header("対象が持つ特殊効果")]
            [SerializeField]
            private SpecialEffect _targetEffect;

            /// <summary>
            /// 対象の状態（逃走、攻撃など）
            /// 複数指定あり
            /// intEnum
            /// </summary>
            [Header("対象の行動状態")]
            [SerializeField]
            private ActState _targetState;

            /// <summary>
            /// 対象のイベント状況（大ダメージを与えた、とか）でフィルタリング
            /// 複数指定あり
            /// </summary>
            [Header("対象のイベント")]
            [SerializeField]
            private BrainEventFlagType _targetEvent;

            /// <summary>
            /// 対象の弱点属性でフィルタリング
            /// 複数指定あり
            /// </summary>
            [Header("対象の弱点")]
            [SerializeField]
            private Element _targetWeakPoint;

            /// <summary>
            /// 対象が使う属性でフィルタリング
            /// 複数指定あり
            /// </summary>
            [Header("対象の使用属性")]
            [SerializeField]
            private Element _targetUseElement;

            /// <summary>
            /// 対象の状態でフィルタリング
            /// 単体指定のみ
            /// </summary>
            [Header("対象の状態")]
            [SerializeField]
            private CharacterStates.CharacterConditions _targetCondition;

            /// <summary>
            /// 対象の距離でフィルタリング
            /// </summary>
            [Header("対象の距離範囲")]
            [SerializeField]
            private float2 _distanceRange;

            /// <summary>
            /// 自分を対象にするかどうか
            /// </summary>
            public bool SelfTarget
            {
                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                get => (this._filterFlags & FilterBitFlag.自分を対象にする) != 0;
            }

            /// <summary>
            /// プレイヤーを対象にするか
            /// </summary>
            public bool PlayerTarget
            {
                [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
                get => (this._filterFlags & FilterBitFlag.プレーヤーを対象にする) != 0;
            }

            /// <summary>
            /// 検査対象キャラクターの条件に当てはまるかをチェックする。
            /// </summary>
            /// <param name="solidData"></param>
            /// <param name="stateInfo"></param>
            /// <returns></returns>
            [BurstCompile]
            public byte IsPassFilter(in SolidData solidData, in CharacterStateInfo stateInfo, float2 nowPosition, float2 targetPosition)
            {

                // すべての条件を2つのuint4にパック
                uint4 masks1 = new(
                    (uint)this._targetFeature,
                    (uint)this._targetEffect,
                    (uint)this._targetEvent,
                    (uint)this._targetType
                );

                uint4 values1 = new(
                    (uint)solidData.feature,
                    (uint)stateInfo.nowEffect,
                    (uint)stateInfo.brainEvent,
                    (uint)stateInfo.belong
                );

                uint4 masks2 = new(
                    (uint)this._targetState,
                    (uint)this._targetWeakPoint,
                    (uint)this._targetUseElement,
                    (uint)this._targetCondition
                );

                uint4 values2 = new(
                    (uint)stateInfo.actState,
                    (uint)solidData.weakPoint,
                    (uint)solidData.attackElement,
                    (uint)stateInfo.conditionState
                );

                // FilterBitFlagからAND/OR判定タイプを取得
                bool4 checkTypes1 = new(
                    (this._filterFlags & FilterBitFlag.特徴フィルター_And判断) != 0,
                    (this._filterFlags & FilterBitFlag.特殊効果フィルター_And判断) != 0,
                    (this._filterFlags & FilterBitFlag.イベントフィルター_And判断) != 0,
                    false // targetTypeは常にOR判定
                );

                bool4 checkTypes2 = new(
                    (this._filterFlags & FilterBitFlag.行動状態フィルター_And判断) != 0,
                    (this._filterFlags & FilterBitFlag.弱点属性フィルター_And判断) != 0,
                    (this._filterFlags & FilterBitFlag.使用属性フィルター_And判断) != 0,
                    false // targetConditionは常にOR判定
                );

                // SIMD演算
                uint4 and1 = masks1 & values1;
                uint4 and2 = masks2 & values2;

                // 条件判定
                bool4 pass1 = EvaluateConditions(masks1, and1, checkTypes1);
                bool4 pass2 = EvaluateConditions(masks2, and2, checkTypes2);

                // すべての条件が満たされているかをチェック
                if ( math.all(pass1) && math.all(pass2) )
                {
                    // 条件が満たされている場合、距離チェックを行う
                    if ( math.any(this._distanceRange != float2.zero) )
                    {
                        // 距離チェック
                        float distance = math.distancesq(nowPosition, targetPosition);
                        bool2 distanceCheck = new bool2(
                            this._distanceRange.x == 0 || distance >= math.pow(this._distanceRange.x, 2),
                            this._distanceRange.y == 0 || distance <= math.pow(this._distanceRange.y, 2)
                        );

                        // 距離条件をANDで結合
                        return math.all(distanceCheck) ? (byte)1 : (byte)0;
                    }

                    // 距離チェック不要ですべての条件が満たされている場合は1を返す
                    return 1;
                }

                // 条件が満たされていない場合は0を返す
                return (byte)0;
            }

            /// <summary>
            /// 条件評価をSIMDで実行するヘルパーメソッド
            /// </summary>
            [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
            [BurstCompile]
            private static bool4 EvaluateConditions(uint4 masks, uint4 andResults, bool4 isAndCheck)
            {
                bool4 zeroMasks = masks == 0u;
                bool4 andConditions = andResults == masks;
                bool4 orConditions = andResults > 0u;

                // ビット演算で条件選択を実現
                // isAndCheck が true の場合は andConditions、false の場合は orConditions
                bool4 selectedConditions = (isAndCheck & andConditions) | (!isAndCheck & orConditions);

                return zeroMasks | selectedConditions;
            }

            #region デバッグ用

            public CharacterBelong GetTargetType()
            {
                return this._targetType;
            }

            public bool Equals(TargetFilter other)
            {
                return this._targetType == other._targetType &&
                       this._targetFeature == other._targetFeature &&
                       this._targetEffect == other._targetEffect &&
                       this._targetState == other._targetState &&
                       this._targetEvent == other._targetEvent &&
                       this._targetWeakPoint == other._targetWeakPoint &&
                       this._targetUseElement == other._targetUseElement &&
                       this._filterFlags == other._filterFlags;
            }

            /// <summary>
            /// デバッグ用のデコンストラクタ。
            /// var (type, feature, effect, state, eventType, weakPoint, useElement, filterFlags) = filter;
            /// </summary>
            public void Deconstruct(
                out CharacterBelong targetType,
                out CharacterFeature targetFeature,
                out SpecialEffect targetEffect,
                out ActState targetState,
                out BrainEventFlagType targetEvent,
                out Element targetWeakPoint,
                out Element targetUseElement,
                out FilterBitFlag filterFlags)
            {
                targetType = this._targetType;
                targetFeature = this._targetFeature;
                targetEffect = this._targetEffect;
                targetState = this._targetState;
                targetEvent = this._targetEvent;
                targetWeakPoint = this._targetWeakPoint;
                targetUseElement = this._targetUseElement;
                filterFlags = this._filterFlags;
            }

            /// <summary>
            /// 互換性のためのデコンストラクタ（BitableBool形式）
            /// </summary>
            public void Deconstruct(
                out CharacterBelong targetType,
                out CharacterFeature targetFeature,
                out bool isAndFeatureCheck,
                out SpecialEffect targetEffect,
                out bool isAndEffectCheck,
                out ActState targetState,
                out BrainEventFlagType targetEvent,
                out bool isAndEventCheck,
                out Element targetWeakPoint,
                out Element targetUseElement)
            {
                targetType = this._targetType;
                targetFeature = this._targetFeature;
                isAndFeatureCheck = (this._filterFlags & FilterBitFlag.特徴フィルター_And判断) != 0;
                targetEffect = this._targetEffect;
                isAndEffectCheck = (this._filterFlags & FilterBitFlag.特殊効果フィルター_And判断) != 0;
                targetState = this._targetState;
                targetEvent = this._targetEvent;
                isAndEventCheck = (this._filterFlags & FilterBitFlag.イベントフィルター_And判断) != 0;
                targetWeakPoint = this._targetWeakPoint;
                targetUseElement = this._targetUseElement;
            }

            /// <summary>
            /// IsPassFilterのデバッグ用メソッド。失敗した条件の詳細を返す
            /// </summary>
            public string DebugIsPassFilter(in SolidData solidData, in CharacterStateInfo stateInfo)
            {
                System.Text.StringBuilder failedConditions = new();

                // 1. 特徴条件判定
                if ( this._targetFeature != 0 )
                {
                    bool featureFailed = false;
                    string failureReason = "";
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.特徴フィルター_And判断) != 0;

                    if ( isAndCheck )
                    {
                        // AND条件：全ての特徴が必要
                        if ( (this._targetFeature & solidData.feature) != this._targetFeature )
                        {
                            featureFailed = true;
                            CharacterFeature missingFeatures = this._targetFeature & ~solidData.feature;
                            failureReason = $"AND条件失敗 - 必要な特徴が不足: {missingFeatures}";
                        }
                    }
                    else
                    {
                        // OR条件：いずれかの特徴が必要
                        if ( (this._targetFeature & solidData.feature) == 0 )
                        {
                            featureFailed = true;
                            failureReason = "OR条件失敗 - 一致する特徴なし";
                        }
                    }

                    if ( featureFailed )
                    {
                        _ = failedConditions.AppendLine($"[特徴条件で失敗]");
                        _ = failedConditions.AppendLine($"  フィールド: targetFeature");
                        _ = failedConditions.AppendLine($"  期待値: {this._targetFeature} (0x{this._targetFeature:X})");
                        _ = failedConditions.AppendLine($"  実際の値: {solidData.feature} (0x{solidData.feature:X})");
                        _ = failedConditions.AppendLine($"  判定方法: {(isAndCheck ? "AND" : "OR")}");
                        _ = failedConditions.AppendLine($"  理由: {failureReason}");
                        _ = failedConditions.AppendLine();
                        return failedConditions.ToString();
                    }
                }

                // 2. 特殊効果判断
                if ( this._targetEffect != 0 )
                {
                    bool effectFailed = false;
                    string failureReason = "";
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.特殊効果フィルター_And判断) != 0;

                    if ( isAndCheck )
                    {
                        // AND条件：全ての効果が必要
                        if ( (this._targetEffect & stateInfo.nowEffect) != this._targetEffect )
                        {
                            effectFailed = true;
                            SpecialEffect missingEffects = this._targetEffect & ~stateInfo.nowEffect;
                            failureReason = $"AND条件失敗 - 必要な効果が不足: {missingEffects}";
                        }
                    }
                    else
                    {
                        // OR条件：いずれかの効果が必要
                        if ( (this._targetEffect & stateInfo.nowEffect) == 0 )
                        {
                            effectFailed = true;
                            failureReason = "OR条件失敗 - 一致する効果なし";
                        }
                    }

                    if ( effectFailed )
                    {
                        _ = failedConditions.AppendLine($"[特殊効果条件で失敗]");
                        _ = failedConditions.AppendLine($"  フィールド: targetEffect");
                        _ = failedConditions.AppendLine($"  期待値: {this._targetEffect} (0x{this._targetEffect:X})");
                        _ = failedConditions.AppendLine($"  実際の値: {stateInfo.nowEffect} (0x{stateInfo.nowEffect:X})");
                        _ = failedConditions.AppendLine($"  判定方法: {(isAndCheck ? "AND" : "OR")}");
                        _ = failedConditions.AppendLine($"  理由: {failureReason}");
                        _ = failedConditions.AppendLine();
                        return failedConditions.ToString();
                    }
                }

                // 3. イベント判断
                if ( this._targetEvent != 0 )
                {
                    bool eventFailed = false;
                    string failureReason = "";
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.イベントフィルター_And判断) != 0;

                    if ( isAndCheck )
                    {
                        // AND条件：全てのイベントが必要
                        if ( (this._targetEvent & stateInfo.brainEvent) != this._targetEvent )
                        {
                            eventFailed = true;
                            BrainEventFlagType missingEvents = this._targetEvent & ~stateInfo.brainEvent;
                            failureReason = $"AND条件失敗 - 必要なイベントが不足: {missingEvents}";
                        }
                    }
                    else
                    {
                        // OR条件：いずれかのイベントが必要
                        if ( (this._targetEvent & stateInfo.brainEvent) == 0 )
                        {
                            eventFailed = true;
                            failureReason = "OR条件失敗 - 一致するイベントなし";
                        }
                    }

                    if ( eventFailed )
                    {
                        _ = failedConditions.AppendLine($"[イベント条件で失敗]");
                        _ = failedConditions.AppendLine($"  フィールド: targetEvent");
                        _ = failedConditions.AppendLine($"  期待値: {this._targetEvent} (0x{this._targetEvent:X})");
                        _ = failedConditions.AppendLine($"  実際の値: {stateInfo.brainEvent} (0x{stateInfo.brainEvent:X})");
                        _ = failedConditions.AppendLine($"  判定方法: {(isAndCheck ? "AND" : "OR")}");
                        _ = failedConditions.AppendLine($"  理由: {failureReason}");
                        _ = failedConditions.AppendLine();
                        return failedConditions.ToString();
                    }
                }

                // 4. 残りの条件（個別チェック）
                List<string> remainingFailures = new();

                // 陣営チェック
                if ( this._targetType != 0 && (this._targetType & stateInfo.belong) == 0 )
                {
                    remainingFailures.Add($"  - targetType: 期待値={this._targetType} (0x{this._targetType:X}), 実際の値={stateInfo.belong} (0x{stateInfo.belong:X})");
                }

                // 状態チェック（AND/OR判定対応）
                if ( this._targetState != 0 )
                {
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.行動状態フィルター_And判断) != 0;
                    bool statePassed = isAndCheck ?
                        (this._targetState & stateInfo.actState) == this._targetState :
                        (this._targetState & stateInfo.actState) != 0;

                    if ( !statePassed )
                    {
                        remainingFailures.Add($"  - targetState: 期待値={this._targetState} (0x{this._targetState:X}), 実際の値={stateInfo.actState} (0x{stateInfo.actState:X}), 判定={(isAndCheck ? "AND" : "OR")}");
                    }
                }

                // 弱点チェック（AND/OR判定対応）
                if ( this._targetWeakPoint != 0 )
                {
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.弱点属性フィルター_And判断) != 0;
                    bool weakPointPassed = isAndCheck ?
                        (this._targetWeakPoint & solidData.weakPoint) == this._targetWeakPoint :
                        (this._targetWeakPoint & solidData.weakPoint) != 0;

                    if ( !weakPointPassed )
                    {
                        remainingFailures.Add($"  - targetWeakPoint: 期待値={this._targetWeakPoint} (0x{this._targetWeakPoint:X}), 実際の値={solidData.weakPoint} (0x{solidData.weakPoint:X}), 判定={(isAndCheck ? "AND" : "OR")}");
                    }
                }

                // 使用属性チェック（AND/OR判定対応）
                if ( this._targetUseElement != 0 )
                {
                    bool isAndCheck = (this._filterFlags & FilterBitFlag.使用属性フィルター_And判断) != 0;
                    bool useElementPassed = isAndCheck ?
                        (this._targetUseElement & solidData.attackElement) == this._targetUseElement :
                        (this._targetUseElement & solidData.attackElement) != 0;

                    if ( !useElementPassed )
                    {
                        remainingFailures.Add($"  - targetUseElement: 期待値={this._targetUseElement} (0x{this._targetUseElement:X}), 実際の値={solidData.attackElement} (0x{solidData.attackElement:X}), 判定={(isAndCheck ? "AND" : "OR")}");
                    }
                }

                if ( remainingFailures.Count > 0 )
                {
                    _ = failedConditions.AppendLine($"[その他の条件で失敗]");
                    foreach ( string failure in remainingFailures )
                    {
                        _ = failedConditions.AppendLine(failure);
                    }

                    return failedConditions.ToString();
                }

                // 全条件パスした場合
                return "全ての条件をパスしました";
            }

            /// <summary>
            /// より詳細なビット解析を含むバージョン
            /// </summary>
            /// <param name="solidData"></param>
            /// <param name="stateInfo"></param>
            /// <returns></returns>
            public string DebugIsPassFilterDetailed(in SolidData solidData, in CharacterStateInfo stateInfo)
            {
                string result = this.DebugIsPassFilter(solidData, stateInfo);

                if ( result != "全ての条件をパスしました" )
                {
                    System.Text.StringBuilder details = new();
                    _ = details.AppendLine("=== 詳細なビット解析 ===");
                    _ = details.AppendLine($"FilterFlags: {this._filterFlags} (0x{(int)this._filterFlags:X})");

                    // フィルターフラグの詳細
                    _ = details.AppendLine("フィルター設定:");
                    _ = details.AppendLine($"  特徴フィルター: {((this._filterFlags & FilterBitFlag.特徴フィルター_And判断) != 0 ? "AND" : "OR")}");
                    _ = details.AppendLine($"  特殊効果フィルター: {((this._filterFlags & FilterBitFlag.特殊効果フィルター_And判断) != 0 ? "AND" : "OR")}");
                    _ = details.AppendLine($"  行動状態フィルター: {((this._filterFlags & FilterBitFlag.行動状態フィルター_And判断) != 0 ? "AND" : "OR")}");
                    _ = details.AppendLine($"  イベントフィルター: {((this._filterFlags & FilterBitFlag.イベントフィルター_And判断) != 0 ? "AND" : "OR")}");
                    _ = details.AppendLine($"  弱点属性フィルター: {((this._filterFlags & FilterBitFlag.弱点属性フィルター_And判断) != 0 ? "AND" : "OR")}");
                    _ = details.AppendLine($"  使用属性フィルター: {((this._filterFlags & FilterBitFlag.使用属性フィルター_And判断) != 0 ? "AND" : "OR")}");

                    result += "\n" + details.ToString();
                }

                return result;
            }
            #endregion
        }

        #endregion 判断関連

        #region SoA対象外構造体

        #region キャラクター基幹データ（非Job）

        /// <summary>
        /// キャラクターのモードがどのようなものであるかを定義するための構造体。
        /// AIで使用するデータを入れる。
        /// </summary>
        [Serializable]
        public class CharacterModeData
        {
            /// <summary>
            /// モードごとの判断の間隔
            /// xがターゲット判断でyが行動判断、zが移動判断の間隔。
            /// </summary>
            public float3 judgeInterval;

            /// <summary>
            /// 攻撃以外の行動条件データ.
            /// 最初の要素ほど優先度高いので重点。
            /// </summary>
            [Header("トリガー行動判断条件")]
            public TriggerJudgeData[] triggerCondition;

            /// <summary>
            /// ターゲット判断用のデータ。
            /// </summary>
            [Header("ターゲット判断条件")]
            public TargetJudgeData[] targetCondition;

            /// <summary>
            /// 攻撃以外の行動条件データ.
            /// 最初の要素ほど優先度高いので重点。
            /// </summary>
            [Header("行動判断条件")]
            public ActJudgeData[] actCondition;

        }

        /// <summary>
        /// 送信するデータ、不変の物
        /// 大半ビットでまとめれそう
        /// 空飛ぶ騎士の敵いるかもしれないしタイプは組み合わせ可能にする
        /// 初期化以降では、ステータスバフやデバフが切れた時に元に戻すくらいしかない
        /// Jobシステムで使用しないのでメモリレイアウトは最適化
        /// SOA対象外
        /// </summary>
        [Serializable]
        [StructLayout(LayoutKind.Auto)]
        public struct CharacterBaseData
        {
            /// <summary>
            /// 最大HP
            /// </summary>
            [Header("HP")]
            public short hp;

            /// <summary>
            /// 最大MP
            /// </summary>
            [Header("MP")]
            public short mp;

            /// <summary>
            /// 最大スタミナ
            /// </summary>
            [Header("スタミナ")]
            public short stamina;

            /// <summary>
            /// 基本アーマー
            /// </summary>
            [Header("基礎アーマー")]
            public short armor;

            /// <summary>
            /// 各属性の基礎攻撃力
            /// </summary>
            [Header("基礎属性攻撃力")]
            public ElementalStatus baseAtk;

            /// <summary>
            /// 各属性の基礎防御力
            /// </summary>
            [Header("基礎属性防御力")]
            public ElementalStatus baseDef;

            /// <summary>
            /// キャラの初期状態。
            /// </summary>
            [Header("最初にどんな行動をするのかの設定")]
            public ActState initialMove;

            /// <summary>
            /// デフォルトのキャラクターの所属
            /// </summary>
            public CharacterBelong initialBelong;
        }

        #endregion

        #endregion SoA対象外

        #endregion 構造体定義

        #region 初期化用のデコンストラクタ

        /// <summary>
        /// デコンストラクタによりすべてのデータリストをタプルとして返す
        /// coldDataについてはあとでオブジェクトから作る
        /// </summary>
        public void Deconstruct(
            out CharacterBaseInfo characterBaseInfo,
            out CharacterAtkStatus characterAtkStatus,
            out CharacterDefStatus characterDefStatus,
            out SolidData solidData,
            out CharacterStateInfo characterStateInfo,
            out MoveStatus moveStatus)
        {
            characterBaseInfo = new CharacterBaseInfo(this.baseData, Vector2.zero);
            characterAtkStatus = new CharacterAtkStatus(this.baseData);
            characterDefStatus = new CharacterDefStatus(this.baseData);
            solidData = this.solidData;
            characterStateInfo = new CharacterStateInfo(this.baseData);
            moveStatus = this.moveStatus;
        }

        #endregion

        #region 定数

        /// <summary>
        /// 属性でジャンプテーブルを使うための辞書
        /// ElementalStatusのインデクサで使用
        /// </summary>
        public static readonly Dictionary<Element, int> ElementIndices = new()
{
    { Element.斬撃属性, 0 },
    { Element.刺突属性, 1 },
    { Element.打撃属性, 2 },
    { Element.聖属性, 3 },
    { Element.闇属性, 4 },
    { Element.炎属性, 5 },
    { Element.雷属性, 6 },
    // 指定なしは辞書に含めない
};

        #endregion

        // ここから下で各データを設定。キャラの種類ごとのステータス。

        /// <summary>
        /// キャラのID
        /// </summary>
        public int characterID;

        /// <summary>
        /// キャラのベース、固定部分のデータ。
        /// これは直接仕様はせず、コピーして各キャラに渡してあげる。
        /// </summary>
        [Header("キャラクターの基本データ")]
        public CharacterBaseData baseData;

        /// <summary>
        /// 固定のデータ。
        /// </summary>
        [Header("固定データ")]
        public SolidData solidData;

        /// <summary>
        /// キャラクターのモードごとのAI設定。
        /// </summary>
        [Header("AI設定")]
        public CharacterModeData[] characterModeSetting;

        /// <summary>
        /// 移動速度などのデータ
        /// </summary>
        [Header("移動ステータス")]
        public MoveStatus moveStatus;

        /// <summary>
        /// 行動の設定。
        /// Jobに入れないので攻撃エフェクト等も持たせていい。
        /// </summary>
        [Header("行動データ一覧")]
        public ActionData[] actData;
    }
}