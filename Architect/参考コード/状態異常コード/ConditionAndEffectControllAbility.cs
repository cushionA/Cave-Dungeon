using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Tools;
using MoreMountains.Feedbacks;
using UnityEngine.AddressableAssets;
using Rewired.Integration.CorgiEngine;
using DarkTonic.MasterAudio;
using System;
using Cysharp.Threading.Tasks;
using static UnityEngine.Rendering.DebugUI;
using Micosmo.SensorToolkit.Example;
using MyCode;
using System.Linq;
using static Rewired.ComponentControls.Data.CustomControllerElementSelector;

namespace MoreMountains.CorgiEngine // you might want to use your own namespace here
{
    /// <summary>
    /// 状態異常とかバフを管理するメソッド
    /// 非同期でやるか？
    /// 毒系の蓄積は1以上蓄積した時点で状態異常に追加
    /// その後四秒に10くらい減りながら蓄積するのを待つ
    /// 蓄積しきると発症。新規蓄積されると減衰開始カウントがリセット、蓄積値加算、同時に毒ダメージの効果も上書き（ダメージ2の毒と4の毒とかあるはず）
    /// 状態異常蓄積が100を超えると発症
    /// 状態異常耐性は蓄積値カット。でも基本的二パーセントはかなり低い。攻撃を何度食らうと毒になるか、という回数が何回減るかを意識して設定
    /// シスターさんの状態異常防壁は例外的にものすごく倍率が高い
    /// 
    /// エフェクトコントローラーと連携したい
    /// 
    /// 
    /// 状態には三種類ある
    /// ・即座に効果発揮して消える（回復とか）
    /// ・一定時間継続して効果を発揮する（毒とかリジェネ）
    /// ・一定条件で効果発揮して消える（次の攻撃強化とか？）（この場合でも時間制限はある？　攻撃か60秒経過か…みたいな）
    /// ・装備品などの永続強化（移動速度強化装備とか）
    /// 
    /// 必要な定義は
    /// ・効果タイプ
    /// ・効果量の数値
    /// ・消滅条件
    /// ・効果発現時のエフェクトとサウンドの種別（エフェクトには発動エフェクトと継続エフェクト（バフデバフ分ける？）とエンチャエフェクトがある）
    /// 
    /// 仕様
    /// ・効果受取先はヘルス（体力変動、耐性値変更）
    /// ・ダメージオンタッチ（バフやエンチャの攻撃力変更）
    /// ・移動速度変更などでコントロールアビリティも（アニメ速度連動させる機能作らないとな）
    /// ・ここで扱うのはアニメーションを伴わないエフェクトと状態効果だけ
    /// 
    /// 
    /// 問題
    /// ・複数の効果が出る時、エフェクトや音声の処理はどうするか（回復と強化とか）
    /// ・いっそ発現エフェクトは全部出す？　でも継続エフェクト間では優劣関係があるようにするか
    /// ・一度使ったら系はどうするか。そのフラグを使った先で報告させる？　〇回使用したとかで消滅
    /// 
    /// 機能
    /// ・エフェクト出す
    /// ・効果を管理、終了させる
    /// 
    /// 
    /// </summary>
    [AddComponentMenu("Corgi Engine/Character/Abilities/TODO_REPLACE_WITH_ABILITY_NAME")]
    public class ConditionAndEffectControllAbility : MyAbilityBase
    {

        /// 
        /// 状態異常とバフをまとめたい
        /// 
        /// デバフ（攻撃やアイテムで与えられるのと装備品のデメリットで与えられるのがある）
        /// ・毒
        /// ・猛毒（大きなダメージとスタミナ回復低下。効果時間短め）
        /// ・凍結（停止。被ダメージ増大）
        /// ・拘束（停止）
        /// ・沈黙（魔法封印）
        /// ・虚弱（被ダメージ上昇、スタミナ（アーマー）回復速度低下、状態異常蓄積解消速度減少）
        /// ・めまいor呪縛（与ダメージ減少、ガード性能超劣化。手が震える。呪縛の方が効果上）
        /// ・刻印（ヘイト上昇、移動速度低下）
        /// ・被ダメージ増大（これは装備品とかで与えられるやつ）（特定属性に分ける？）
        /// ・与ダメージ減少（これは装備品とかで与えられるやつ）
        /// ・移動速度低下（これは装備品とかで与えられるやつ）
        /// ・ヘイト上昇（これは装備品とかで与えられるやつ）
        /// ・アイテムの効果減少
        /// 
        /// バフ
        /// ・リジェネ
        /// ・活性（被ダメージ低下、スタミナ（アーマー）回復加速、状態異常蓄積減少速度増加）
        /// ・祝福（与ダメージ増大、ガード性能強化）
        /// ・隠密（ヘイト減少、移動速度加速、足音消滅）
        /// ・被ダメージ減少（これは装備品とかで与えられるやつ）（でも魔法で単体で与えてもいいかも）
        /// ・与ダメージ増加（これは装備品とかで与えられるやつ）（でも魔法で単体で与えてもいいかも）
        /// ・アクション強化（移動速度加速や二段ジャンプできるとか特殊回避とか全部これに入れる。エフェクトやアイコンも統一）（これは装備品とかで与えられるやつ）（でも魔法で単体で与えてもいいかも）
        /// ・特定攻撃強化（魔法、カウンター、各種属性）
        /// ・アイテムの効果増強（アイテム効果増強でまとめてどのアイテムが増強なのかはenumで管理する？）（基本的に装備品で）
        /// ・バリア
        /// ・エンチャント
        /// ・リザオ〇ル（復活）
        /// 
        /// 単純な効果
        /// ・回復
        /// ・MP回復
        /// ・状態異常消去（どの状態異常解除するのかはenumで？　いくつ回復するかでエフェクトが変わるとか）
        /// ・バフ削除
        /// ・スタミナとアーマーリセット
        /// 
        /// ///
        /// 

        /// 
        /// やること
        /// 
        /// ・大原則として、フラグ系はこっちに直接問いかければいいだけじゃん
        /// ・外から聞かれる可能性があるやつ、あるいはアイテムとかの倍率を持っとけばいいよね
        /// 
        /// 
        /// ・状態変化に合わせてエフェクトやアイコンを出す機能
        /// ・状態異常の蓄積量低下をどう差別化するか
        /// ・プレイヤーのだけ蓄積量を表示する
        /// ・定期的にくる効果（毒とか）を乗せる方法考える。特定のユニークイベントならとか？　特定のユニークイベント（毒）のHP操作は定期的にやる？
        /// ・エンチャントエフェクトどうしよう（個別ルートに移動して武器スプライトにアクセスするか）
        /// ・エフェクト出すのはユニークエフェクトだけでいい？
        /// ・エフェクトはシェーダーで作ったやつをキャラにつけてenable切り替えで表示する感じで
        /// 
        /// 
        /// エフェクト出す（エフェクト要請）、スプライトエフェクトを出す（スプライト切り替え）
        /// この二つに分けるか
        /// エフェクト要請は回復とか状態異常発症とかそういうの？
        /// あとは凍結とかはオーバーレイスプライトで対処するか？
        /// 
        /// 武器ごとにエフェクトのスケールつけてエフェクトを纏わせるようにする？
        /// この武器は1.2と2.8とかで
        /// 
        /// 攻撃時回復とかの効果は重複できた方が嬉しいよね
        /// 攻撃時回復効果魔法と、攻撃時回復アクセが一緒だと嬉しい
        /// なんでアクション時の回復とかバフとかの効果は数値にしよ
        /// あと出血ダメージとかは発症時処理でやろう
        /// 回復とかそういう即時効果のやつのエフェクト出すのは別の機能でやろう
        /// ヘルス経由で？
        /// 
        /// 
        /// ///



        #region　定義





        #region　エフェクト区分

        /// <summary>
        ///  イベントの種類を記述
        ///  ダメージなど
        ///  コンディションの具体的な効果はこれになる
        ///  
        /// 具体的なバフやデバフからこれを飛ばす？
        /// 先に定義しといて
        /// 
        /// enumで識別必要なデータ
        /// ・ステータス
        /// ・強化属性
        /// ・アクション強化
        /// ・どのアイテムを強化するか
        /// ・状態異常蓄積
        /// 
        /// アクション強化について
        /// ・どのアクション変化なのかはenumで
        /// ・特殊モーションに変化とかはない。エフェクトだけ変えるか？それとも特殊モーション先に用意しとくか
        /// ・それぞれ違うけど二段ジャンプとかすごい回避出せる状態。パリィ時スタミナ回復とかも。
        /// ・別にエフェクトは出さない？
        /// ・どのアクションかっていうEnumはintメモリーに持たせるか？
        /// 
        /// 
        /// こいつさぁ
        /// ターゲットと操作を組み合わせる形にしない？
        /// 
        /// たとえばよ
        /// enumTarget 筋力
        /// enumEffect 加算
        /// 
        /// とか
        /// 
        /// </summary>
        [Flags]
        public enum EventType
        {



            /// 
            /// ここでステータスとか持っておく？
            /// 倍率と加算した数を持っておいて
            /// それで、それをステータスに反映する形
            /// ステータスが持つのは単一の数値で、ここはenumで区分分けした数値を持っておく
            /// アリだね
            /// どこで使用するかで区分分けようね
            /// ヘルスで使うやつとかはある程度まとめてビット投げれるようにしよう
            /// 
            ヘルスイベント,//回復とか？
            基礎ステータス効果,//体力、最大体力、スタミナ、アーマー
            プレイヤーステータス効果,//筋力とか　実装
            シスターステータス効果,//なんかあるはず、信仰とか、聖女的なレベルが
            攻撃力変動効果,//　実装
            防御力変動効果,//属性防御とかも包括　実装
            ガード性能変動効果,
            与ダメージ倍率変動,//属性で分けるよね　実装
            被ダメージ倍率変動,//　実装
            アクション変化,

            攻撃ヒットイベント,//ここからトリガー
            ジャストガードイベント,
            瀕死イベント,
            体力マックス時イベント,//体力変動時に入れるか、あと瀕死時とかそうでないときとかの状態の移り変わりを報告できるようにしないとな
            //回避時イベント,　//これは流石に強すぎ
            敵撃破時イベント,
            被弾時イベント,//ここまでトリガー

            アイテム効果変動,
            アイテム使用禁止,
            特殊バフ,//音消滅、バフ効果時間延長、復活など。カウンター攻撃倍率とかもこれにするか
            特殊デバフ,//停止とか。停止はスタンの終わりをアニメの終わりではなく状態異常の終わりまでとする。魔法禁止も？
            状態異常蓄積,//毒とかそういうのがたまっていく過程
            バリア,
            右手エンチャント,//これは属性強化
            左手エンチャント,//属性強化とガード強化をどう分ける？　両手持ち時はどうする？　発動無理にするか、シンプルに
            イベント解除

        }


        #region イベントタイプごとの効果の対象選択（筋力とか。ジェネリックする）


        #region キャラクター共通の要素


        //状態異常耐性倍率も付けるか？


        /// <summary>
        /// 即時発動する効果
        /// 発動後、継続して効果を記録する必要がないもの
        /// HP回復など
        /// 基本的には発動回数で管理する？
        /// 後は時間もか
        /// 外で管理する必要ない
        /// </summary>
        public enum HealthEventSelect
        {
            HP変動,//回復やダメージ。ある限り変動し続ける。2秒ごとに。それかユニークエフェクトで変える？　即時ならその場限り
            MP変動,
            HP割合変動,//100から1までの％で
            MP割合変動,
            アーマー割合回復,
            停止,//動けなくなる
            凍結//停止より長い
        }



        /// <summary>
        /// 基礎ステータスに変動効果を与える時
        /// その対象を選ぶ
        /// 外で管理する必要あり
        /// </summary>
        public enum BaseStatusChangeSelect
        {

            MP回復速度変動,//これはシスターさんかな
            最大HP変動,
            最大MP変動,
            最大スタミナ変動,//ここまで乗算加算含む
            スタミナ回復速度変動,
            最大アーマー変動,
            アーマー回復速度変動,
            MP回復速度上昇,//シスターさん専
            ヘイト倍率変動,//プラスされるヘイトを減らす
            バフ効果時間変化,
            デバフ効果時間変化,

        }


        /// <summary>
        /// 攻撃力と防御力、あとはシールドの倍率変動
        /// 被ダメージ、与ダメージの属性ごとの倍率や削りの倍率にも使う
        /// 武器とか関係なくそもそもダメージ計算や攻撃力計算で
        /// 乗算倍率や加算分として加えられるように数値を持つ
        /// 
        /// 攻撃か防御かはイベントタイプでわかれる
        /// 外に反映する必要あり
        /// </summary>
        public enum DamageStatusSelect
        {
            全体,
            物理,
            斬撃,
            打撃,
            刺突,
            炎,
            雷,
            聖,
            闇,
            アーマー削り,//これはシールドだとシールドの削り抵抗になる。また全体に含まない
                   //被ダメージや与ダメージ倍率では触れない
                   //アーマーは倍率ステータスでだけ使う
                   //攻撃力変化では使わない
        }



        /// <summary>
        /// アクションに変化が起こる時の対象の選び方
        /// 外に反映する必要あり
        /// </summary>
        public enum ActionChangeSelect
        {
            移動速度倍率変動,
            回避距離＿時間変動,
            ジャンプ回数変動,

        }


        /// <summary>
        /// 特殊バフの効果選択
        /// 外で管理する必要なし
        /// 音消滅フラグとかは行動の音を出すアビリティで参照させるか
        /// </summary>
        public enum SpecialBuffSelect
        {
            音消滅,
            復活,//死亡時このイベントがあれば蘇生


        }

        /// <summary>
        /// 特別なデバフフラグ？
        /// 外で管理する必要なし
        /// </summary>
        public enum SpecialDebuffSelect
        {

            魔法禁止,

        }

        //ステータスにエンチャントのデータを記録できるようにする
        //武器切り替えなどで初期化
        //そしてエンチャントステータスで武器についてるオーバーレイスプライトを起動する
        //配列に側面などすべての差分を入れておく


        /// <summary>
        /// 装備に使用するエンチャント
        /// タイプ別に固定強化
        /// でもエンチャデータは数値で渡したほうが良くね
        /// 炎攻撃60加算とかすべきだし
        /// 大強化とかじゃなくて強化レベルとかで表すか？
        /// 効果時間は終了条件で
        /// 効果量は強化レベルで判断
        /// </summary>
        public enum EquipEnchantSelect
        {
            なし,
            物理強化,
            炎強化,
            雷強化,
            聖強化,
            闇強化,
            毒強化,
            猛毒強化,
            凍結強化,
            ガード強化,//ガード受け値少し上がる、各属性受け値も
            状態異常遮断,//ガード時に完全に状態異常の影響を受けなくなる
            //最初はエンチャしてないからね
        }



        /// <summary>
        /// 数値が蓄積して発症する系統
        /// </summary>
        public enum RestoreEffect
        {
            毒蓄積,
            猛毒蓄積,
            凍結蓄積
        }

        /// <summary>
        /// 使用禁止になったり効果が高まったりするアイテムの一覧
        /// ここで管理するか
        /// </summary>
        public enum ItemTypeSelect
        {
            HP回復アイテム,
            MP回復アイテム,
            バフアイテム,
            攻撃アイテム,
            状態異常付与アイテム,

        }


        /// <summary>
        /// 何らかの特殊なきっかけで発動するイベント
        /// いや、回復やMP回復などのイベントをこちらに入れて
        /// 撃破や攻撃ヒットなどの条件をイベントタイプにする？
        /// いや、撃破回復などのそれぞれのイベント時に対応するイベント状態があるかを確認して呼び出したほうがいい
        /// 
        /// これ数値イベントがいいよな
        /// </summary>
        public enum TriggerEventSelect
        {
            HP回復,
            ダメージ,
            MP回復,
            シスターさんのMP割合回復,
            スタミナ割合回復,
            与ダメージ倍率変化,
            被ダメージ倍率変化,
            移動速度変化,
            シスターさんの特殊アクションクールタイム割合減少,

        }



        /// <summary>
        /// 状態解除
        /// バフもデバフも
        /// 
        /// 状態異常耐性変動指定にも使う
        /// </summary>
        public enum EventCancelCondition
        {
            全て,
            バフ,
            デバフ,
            毒,
            猛毒,
            毒系統,
            停止,//動けなくなる
            凍結,
        }


        /// <summary>
        /// 状態異常の中でも蓄積する物の蓄積値上限
        /// ここで管理
        /// </summary>
        public enum RestoreAmountSelect
        {
            毒耐性変動,
            凍結耐性変動,
        }




        /// <summary>
        /// 展開しているバリアやエフェクトの種類の保存
        /// 常に展開できるのは一つ
        /// gameobjectに展開中のオブジェクトを保存しておいて消すか？
        /// 次のに上書きされたら
        /// 攻撃オブジェクト（オービット）とバリアは別にしたいよね、どうする？
        /// 
        /// バリアはダメージカットじゃなくて完全無敵か弾を受け流すやつ
        /// 使用回数とかで差別化できる
        /// </summary>
        public enum BarrierSelect
        {
            なし,
            物理バリア,
            魔法バリア,

        }




        #endregion

        #region プレイヤー固有の要素



        /// <summary>
        /// プレイヤーステータスが変動した場合に
        /// 対象の識別に利用する
        /// </summary>
        public enum PlayerStatusSelect
        {
            全能力値,
            生命力,
            持久力,
            魔力,
            筋力,
            技量,
            賢さ

        }



        /// <summary>
        /// アイテム使用時に効力が変動する
        /// その対象を決める
        /// </summary>
        public enum ItemEffectSelect
        {
            回復アイテム,
            攻撃アイテム
        }



        /// <summary>
        /// 武器や魔法で効果を保存する用のもの
        /// Enumは数値確認用、いつかコメントアウトする
        /// 敵との接触時と味方との接触時で二つ付ける？
        /// 
        /// 攻撃時はダメージオンタッチに渡して、敵ヘルスからえふぇこん受け取る
        /// 味方衝突時は衝突オブジェクトからコンバットAI通じてコントロールアビリティからえふぇこん受け取る
        /// structにするのは値型のコピーによって別インスタンスを生み出しておきたいから
        /// </summary>
        public struct EffectContainer
        {
            /*
            public enum SelectEvent
            {
                BaseStatusChangeSelect = 1 << 1,
                DamageStatusSelect = 1 << 2,
                RestoreEffect = 1 << 3,
                PlayerStatusSelect = 1 << 4,
                ActionChangeSelect = 1 << 5,
                ItemEffectSelect = 1 << 6,
                BarrierSelect = 1 << 7,
                EventCancelCondition = 1 << 8,
                SpecialBuffSelect = 1 << 9,
                SpecialDebuffSelect = 1 << 10,
                TriggerEventSelect = 1 << 11,
            }


            [EnumFlags]
            [Header("この武器や魔法が持っているモード")]
            public SelectEvent selectCondition;


            /// <summary>
            /// HPやスタミナ、アーマー、ヘイトなどの基礎的な情報を変化させる
            /// </summary>
            ConditionDataContainerFloat<BaseStatusChangeSelect>[] statusChangeEffect;

            /// <summary>
            /// 被ダメージ、与ダメージ含むダメージ倍率の変化
            /// イベントタイプで区別する
            /// </summary>
            ConditionDataContainerFloat<DamageStatusSelect>[] damageChangeEffect;

            /// <summary>
            /// 状態異常その他の蓄積昇華のエフェクト
            /// </summary>
            [SerializeField]
            ConditionDataContainerFloat<RestoreEffect>[] restoreEfffect;

            /// <summary>
            /// プレイヤーのステータス変化
            /// 力や生命力などのプレイヤーだけの物
            /// </summary>
            [SerializeField]
            ConditionDataContainerFloat<PlayerStatusSelect>[] playerStatusEffect;


            /// <summary>
            /// 移動速度や回避距離などを編集できる
            /// </summary>
            [SerializeField]
            ConditionDataContainerFloat<ActionChangeSelect>[] actionChangeEffect;

            /// <summary>
            /// アイテムの効果を変化させる
            /// </summary>
            [SerializeField]
            ConditionDataContainerFloat<ItemEffectSelect>[] itemValueEffect;


            /// <summary>
            /// 展開中のバリア
            /// </summary>
            [SerializeField]
            ConditionDataContainerBoolean<BarrierSelect>[] barriarContainer;


            /// <summary>
            /// 効果を打ち消すエフェクト
            /// これは記録しないけど、ここで受け取って打ち消す
            /// </summary>
            [SerializeField]
            ConditionDataContainerBoolean<EventCancelCondition>[] cancelEffect;


            /// <summary>
            /// 復活や無音などの特殊なバフ
            /// </summary>
            [SerializeField]
            ConditionDataContainerBoolean<SpecialBuffSelect>[] specialBuff;

            /// <summary>
            /// 行動禁止、魔法禁止、スキル禁止、などの特殊デバフ
            /// </summary>
            [SerializeField]
            ConditionDataContainerBoolean<SpecialDebuffSelect>[] specialDebuff;


            /// <summary>
            /// 攻撃時、などの引き金で起こるイベント
            /// </summary>
            [SerializeField]
            ConditionDataContainerFloat<TriggerEventSelect>[] triggerEventEffect;


            */


            [SerializeField]
            BaseStatusChangeSelect baseStatusSelect;

            [SerializeField]
            DamageStatusSelect damageSelect;
            [SerializeField]
            RestoreEffect restoreSelect;

            [SerializeField]
            PlayerStatusSelect playerStatusSelect;

            [SerializeField]
            ActionChangeSelect actionChnageSelect;

            [SerializeField]
            ItemEffectSelect itemEffectSelect;

            [SerializeField]
            BarrierSelect barrierSelect;

            [SerializeField]
            EventCancelCondition EventCancelSelect;

            [SerializeField]
            SpecialBuffSelect specialBuffSelect;

            [SerializeField]
            SpecialDebuffSelect specialDebuffSelect;

            [SerializeField]
            TriggerEventSelect triggerEvent;


            [SerializeField]
            bool isFlagEvent;


            /// <summary>
            /// フラグ系の条件
            /// </summary>
            [SerializeField]
            ConditionDataBase[] boolConditions;



            [SerializeField]
            bool isValueEvent;

            /// <summary>
            /// 数値系のイベント
            /// </summary>
            [SerializeField]
            ConditionDataValue[] valueConditions;


            /// <summary>
            /// イベントが存在するかを確認できる
            /// フラグか数値どちらかあるなら
            /// </summary>
            /// <returns></returns>
            public bool CheckEventExist()
            {
                return (isValueEvent || isFlagEvent);
            }


            /// <summary>
            /// 任意のエフェクトコントローラにデータを送る
            /// </summary>
            /// <param name="conditionContainer"></param>
            public void SendData(ConditionAndEffectControllAbility conditionContainer)
            {
                int count;

                if ( isFlagEvent )
                {
                    count = boolConditions.Length;
                    for ( int i = 0; i < count; i++ )
                    {
                        conditionContainer.ConditionSetting(new ConditionDataBase());
                    }
                }


                if ( isValueEvent )
                {

                    count = valueConditions.Length;
                    for ( int i = 0; i < count; i++ )
                    {
                        conditionContainer.ConditionSetting(valueConditions[i]);
                    }
                }


            }


            /// <summary>
            /// 状態異常系コンディションデータを攻撃を介してやり取りする
            /// ガードしてるか、カット率はどんなものかで分かれる
            /// </summary>
            public void AttackDataSend(ConditionAndEffectControllAbility conditionContainer, bool isGuard, float cutRate)
            {
                int count;

                //ガードしてないならそのまま受け取る
                //通常の状態異常耐性は上限値を引き上げることで対応してるから
                if ( !isGuard )
                {

                    if ( isFlagEvent )
                    {
                        count = boolConditions.Length;
                        for ( int i = 0; i < count; i++ )
                        {
                            conditionContainer.ConditionSetting(new ConditionDataBase());
                        }
                    }


                    if ( isValueEvent )
                    {

                        count = valueConditions.Length;
                        for ( int i = 0; i < count; i++ )
                        {
                            conditionContainer.ConditionSetting(valueConditions[i]);
                        }
                    }

                }

                else
                {
                    //数値イベントだけ
                    //それも蓄積だけ
                    if ( isValueEvent )
                    {

                        count = valueConditions.Length;
                        for ( int i = 0; i < count; i++ )
                        {
                            if ( valueConditions[i].eventType == EventType.状態異常蓄積 )
                            {
                                //新しいデータ作って蓄積量を減らす
                                //そのまま減らすと元のデータが変わる 
                                ConditionDataValue cutData = new ConditionDataValue(valueConditions[i]);

                                //カット率で蓄積量を減らす
                                cutData.changeValue *= (100 - cutRate) / 100;

                                conditionContainer.ConditionSetting(cutData);
                            }
                        }
                    }
                }
            }

        }





        #endregion




        #endregion





        /// <summary>
        /// 重ね掛け不可能の固有エフェクト
        /// 効果が複数あるやつで、なおかつ重複もできないもの（毒とかは単一でもいい？）
        /// かけられると、具体的にはこれがなし以外だと登録される
        /// なし以外の時同じ効果が発動されてないかを走査する
        /// そしてキャンセルする
        /// これでエフェクトとかも管理する？
        /// 
        /// これがある時はこれ、これがないならイベントタイプでエフェクトやアイコンを出す
        /// 
        /// 列挙子に入れた数値はエフェクト表示などの優先度
        /// 中に入ってる数値同じでもちゃんと識別できるから安心して
        /// 
        /// ソートリストはUIマネージャーの方に置くか
        /// いやでも入れ替えがあるからな…
        /// ビット演算は32までなので注意。Long使うのはあり
        /// </summary>
        [Flags]
        public enum UniqueEffect
        {
            毒 = 1 << 25,
            猛毒 = 1 << 26,
            凍結 = 1 << 28,
            拘束 = 1 << 27,
            沈黙 = 1 << 24,
            虚弱 = 1 << 23,
            めまい = 1 << 21,
            呪縛 = 1 << 22,
            刻印 = 1 << 12,
            与ダメージデバフ = 1 << 17,//これはすごく重要だからエフェクト必要
            被ダメージデバフ = 1 << 18,//これはすごく重要だからエフェクト必要
            最大HP低下 = 1 << 16,
            ステータス低下 = 1 << 7,
            攻撃力低下 = 1 << 10,
            防御力低下 = 1 << 11,
            特殊弱体 = 1 << 4,//アクションや移動速度系
            アイテム効果低下_禁止 = 1 << 2,

            リジェネ = 1 << 18,
            MPリジェネ = 1 << 17,
            活性 = 1 << 20,
            祝福 = 1 << 19,
            隠密 = 1 << 5,
            与ダメージバフ = 1 << 15,//これはすごく重要だからエフェクト必要
            被ダメージバフ = 1 << 14,//これはすごく重要だからエフェクト必要
            最大HP上昇 = 1 << 13,
            ステータス上昇 = 1 << 6,
            攻撃力上昇 = 1 << 9,
            防御力上昇 = 1 << 8,
            特殊強化 = 1 << 3,//アクションや移動速度系
            アイテム効果向上 = 1 << 1,
            バリア = 1 << 29,
            エンチャント = 1 << 30,

            なし = 0
        }




        #endregion



        #region 終了条件


        /// <summary>
        /// 効果が終了する条件
        /// 
        /// これコンテナと制限数を個別に持ったクラスにして複数条件組み合わせられるようにする？
        /// あとこれとは別に発動条件も大事じゃない？
        /// 時間ごととか即時とか委任（攻撃時発動とか）色々あるじゃんよ
        /// 
        /// </summary>
        public enum EventEndCondition
        {
            即時,//すぐに消える
            時間,
            使用回数,
            体力割合,//未実装
            付与回数,//何回同じ系統の効果追加できるかの制限
            数値減少,//毒の蓄積とかは時間経過で減り続ける。加算値がゼロになれば消滅
                 // 武器変更,
                 // 盾変更,
                 //コア変更,
            装備変更,//統一
            永続//死ぬか休憩まで永続
        }


        /// <summary>
        /// イベントの終了条件
        /// どれか一つでも発動すると終わり
        /// 
        /// 時間と数値蓄積は必ず配列の0に配置
        /// </summary>
        public class EventEndJudge
        {
            /// <summary>
            /// 終わる条件
            /// </summary>
            [Header("終了条件")]
            public EventEndCondition endCondition;


            /// <summary>
            /// 使用回数、あるいは継続時間など
            /// 終了条件によっては使わない
            /// 
            /// 毒蓄積とか蓄積数値系はここに何秒に一回減るかを入れる
            /// </summary>
            [Header("効果終了する制限。あるいは数値蓄積量")]
            public float effectLimit;

            /// <summary>
            /// 制限を数える
            /// 何個まで同じ効果重複できるかとか
            /// 蓄積系の場合、ここに蓄積値を入れる
            /// </summary>
            [HideInInspector]
            public float limitCounter;


            /// <summary>
            /// 数値加算系が終了したかを確認する
            /// また、装備変更系なら問答無用で全てを変える
            /// </summary>
            /// <returns></returns>
            public bool ConditionCheck()
            {
                //使用回数は攻撃三回分バフとか
                //付与回数は同じバフいくつまでとか
                if ( endCondition == EventEndCondition.使用回数 || endCondition == EventEndCondition.付与回数 )
                {
                    limitCounter++;

                    //終了回数に達してるかを確認
                    //超えてたら終了する
                    return limitCounter >= effectLimit;
                }

                //装備変更系の確認なら全て消すよ
                //判断するまでもない
                return true;
            }


            /// <summary>
            /// 効果時間超えてるかを返すやつ
            /// </summary>
            /// <param name="nowTime"></param>
            /// <returns></returns>
            public bool TimeCheck(float nowTime)
            {
                //0なら初期化から
                if ( limitCounter == 0 )
                {
                    limitCounter = nowTime;
                }

                return (nowTime - limitCounter) >= effectLimit;
            }


            /// <summary>
            /// 数値蓄積系の条件管理に使う
            /// 数値変化に加えて終了かどうかを見る
            /// </summary>
            /// <param name="isAdd"></param>
            /// <param name="changeValue"></param>
            public bool RestoreConditionController(bool isAdd, float changeValue)
            {

                //時間減少
                if ( !isAdd )
                {

                    limitCounter -= changeValue;

                    return limitCounter <= 0;
                }
                else
                {
                    limitCounter += changeValue;

                    return limitCounter >= 100;
                }
            }


        }


        #endregion




        #region　このクラスでのエフェクト記録、管理用


        /// 
        /// 挙動としてはベースクラスとして終了条件を共通管理
        /// 実行時に挙動が多態性で変化するように
        /// 値の取得などもそう
        /// イベントタイプごとに一つ変数を持つ
        /// 毒の蓄積とかはずっと加算していく
        /// そして加算しきったら毒に変化
        /// 加算値が蓄積量で乗算値が発症時のダメージ量
        /// 
        /// 
        /// 祝福とかの複数効果を持つ重ね掛け禁止のやつはどうしよう
        /// 
        /// 
        /// ちょい待てよ
        /// タイプと条件や終了条件だけ保存するやつと
        /// イベントタイプと数値やビットだけを持つやつ
        /// 二つ保存が必要なのでは
        /// 
        /// 特にフラグ系なんてイベントタイプと対象選択数値だけ持ってればいいし
        /// いやでも数値はそれぞれの終了条件と倍率とか加算を持ってないとダメか
        /// 
        /// 処理分けるか
        /// フラグ系はイベントタイプにつき変数一つ
        /// これはビット演算でフラグ管理
        /// いやそんなことない
        /// イベントタイプごとに全てのフラグにそれぞれいつ効果が切れるかを記録しないと
        /// じゃあフラグ一つごとに終了条件を記録するんだ
        /// 
        /// 
        /// 数値系はイベントと対象一つごとに数値の変動まで記録する
        /// 
        /// 
        /// フラグ系
        /// イベントタイプごとにタイプのフラグ数だけ終了条件を記録する
        /// なおフラグ再発行時は前の条件と入れ替えで効果延長
        /// 
        /// 数値系
        /// イベントタイプごとに対象の数だけリストを持つ可能性がある？
        /// それともすべて一つのリストにまとめるか、そうしよう
        /// その上で対象ごとの倍率が今現在どうなってるかを記録する
        /// 
        /// イベントタイプごとに終了条件と数値と対象をつめたリスト一つ
        /// それぞれの対象の数値記録が対象の数
        /// 
        /// ///


        #region　保存するエフェクトデータ

        /// <summary>
        /// これはこのアビリティが管理する
        /// ステータスなどの変動を記録して、ここから筋力とか個別に参照する
        /// プレイヤーキャラクターならステータスとかアイテムフラグとかの変化を受け入れられるように事前に用意しておく、これを
        /// ConditionDataBase<CharaStatus>的なの作ってね 
        /// 
        /// 必要なのは原本の数値にアクセスする機能
        /// そして
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class ConditionDataBase
        {

            /// <summary>
            /// コンストラクタだな
            /// </summary>
            /// <param name="uniqueType"></param>
            /// <param name="eventType"></param>
            /// <param name="selectCondition"></param>
            /// <param name="endCondition"></param>
            /// <param name="effectLimit"></param>
            public ConditionDataBase()
            {


                endConditionNum = endCondition.Length;
            }

            /// <summary>
            /// 複数効果を持つユニークエフェクトの一部であるか
            /// もしそうなら管理方式が変わる
            /// 終了条件では重ねがけを許しつつも個別に監視する
            /// 
            /// ユニークイベントがある場合for文の中でイベントタイプでエフェクトを飛ばさない
            /// 最後にユニークイベントでエフェクトやアイコンを出す
            /// </summary>
            public UniqueEffect uniqueType;


            /// <summary>
            /// イベントのタイプ
            /// ユニークイベントが定義されていなければこれでアイコンとか出す
            /// </summary>
            public EventType eventType;


            /// <summary>
            /// 特定のタイプのイベントの中で
            /// 選択した条件をenumからintに変換して補完する
            /// eventTypeに対するselectCondition
            /// </summary>
            public int selectCondition;


            /// <summary>
            /// 終わる条件
            /// </summary>
            public EventEndJudge[] endCondition;

            /// <summary>
            /// 終了条件の数
            /// </summary>
            protected int endConditionNum;


            /// <summary>
            /// これがデバフかどうか
            /// </summary>
            public bool isDebuff;


            /// <summary>
            /// この条件が削除されてるか
            /// 削除されてるなら時限待ちや時限効果など全部打ち切る
            /// </summary>
            [HideInInspector]
            public bool isDelete;

            /// <summary>
            /// 特定のイベントが特定の終了条件を満たしたかどうか
            /// 確認する
            /// 真偽値は真の場合
            /// 
            /// isAllなら全てのイベント
            /// </summary>
            public bool ConditionCheck(int selectType, EventEndCondition checkCondition, bool isAll = false)
            {
                //全て指定でないならビット演算にする
                //ビットは複数指定できるように
                if ( !isAll && (selectType & selectCondition) > 0 )
                {
                    return false;
                }



                for ( int i = 0; i < endConditionNum; i++ )
                {
                    //使用回数が使用条件に含まれてるなら返す
                    if ( endCondition[i].endCondition == checkCondition )
                    {
                        //使用を反映
                        //そしてリミット超えてたらこのイベントを消す
                        return endCondition[i].ConditionCheck();

                    }
                }

                return false;
            }



            /// <summary>
            /// このイベントの時間が経過してるかどうか
            /// アビリティ本体から定期的に呼び出す
            /// </summary>
            public bool TimeCheck(int nowTime)
            {


                for ( int i = 0; i < endConditionNum; i++ )
                {
                    //使用回数が使用条件に含まれてるなら返す
                    if ( endCondition[i].endCondition == EventEndCondition.時間 )
                    {
                        //使用を反映
                        //そしてリミット超えてたらこのイベントを消す
                        return endCondition[i].TimeCheck(nowTime);

                    }
                }

                return false;
            }



        }

        /// <summary>
        /// 数値操作系の状態変化を管理するためのクラス
        /// これに数値情報を格納しておく
        /// </summary>
        public class ConditionDataValue : ConditionDataBase
        {

            /// <summary>
            /// 普通のコンストラクタ
            /// </summary>
            public ConditionDataValue()
            {
                endConditionNum = endCondition.Length;
            }
            /// <summary>
            /// コピー用のコンストラクタ
            /// </summary>
            public ConditionDataValue(ConditionDataValue originalData)
            {
                uniqueType = originalData.uniqueType;
                eventType = originalData.eventType;
                selectCondition = originalData.selectCondition;
                endCondition = originalData.endCondition;
                endConditionNum = endCondition.Length;
                isDebuff = originalData.isDebuff;
                isAdd = originalData.isAdd;

                //changeValueは自分で決めなよ
            }


            /// <summary>
            /// このイベントでどの数値がどう変わってるかの記録
            /// 
            /// 
            /// yは加算された数値
            /// これが変更されたらヘルスとかの数値の本体を持つ
            /// 機能にアクセス
            /// 
            /// xは乗算された数値
            /// 今目的の数値が何倍であるか、という風に使う
            /// これが変化するたびにヘルスとかにアクセスする
            /// addValueの数値を加味しつつ数値を変える
            /// 0倍になる訳にはいかないので基本1に
            /// </summary>
            public float changeValue;

            /// <summary>
            /// これが真なら加算値
            /// </summary>
            public bool isAdd;


        }


        #endregion


        #region　データを保持する入れ物






        /// <summary>
        /// イベントタイプ一つごとに持つ
        /// 
        /// イベントタイプごとに終了条件と数値と対象をつめたリスト一つ
        /// それぞれの対象の数値記録が対象の数だけ
        /// 
        /// </summary>
        public class ValueEventHolder
        {
            /// <summary>
            /// イベントタイプごとに終了条件と数値と対象をつめたリスト
            /// </summary>
            public List<ConditionDataValue> events;


            /// <summary>
            /// これは対象の各項目の数値がどうなってるかの記録用
            /// 
            /// 
            /// yは加算された数値
            /// これが変更されたらヘルスとかの数値の本体を持つ
            /// 機能にアクセス
            /// 
            /// xは乗算された数値
            /// 今目的の数値が何倍であるか、という風に使う
            /// これが変化するたびにヘルスとかにアクセスする
            /// addValueの数値を加味しつつ数値を変える
            /// 0倍になる訳にはいかないので基本1に
            /// インスペクタから手動でxに1を入れとこう
            /// </summary>
            public Vector2[] valueArray;


            /// <summary>
            /// 保持しているイベント数
            /// </summary>
            int eventCount;

            /// <summary>
            /// エフェクト管理アビリティ
            /// </summary>
            public ConditionAndEffectControllAbility myConditionAbility;


            /// <summary>
            /// 最初からenumをintに変換して引数に与えよう
            /// ここで追加報告をして追加個数条件のイベントを消す
            /// </summary>
            ///<param name="reset">ゲーム再会指示などに改めて設定するためのフラグ</param>
            public void SetValue(ConditionDataValue data, bool reset = false)
            {
                //加算操作でないなら
                //乗算
                if ( !data.isAdd )
                {
                    if ( valueArray[data.selectCondition].x != 0 )
                    {
                        //倍率に掛け算をする
                        valueArray[data.selectCondition].x *= data.changeValue;
                    }
                    else
                    {
                        //倍率に掛け算をする
                        //でも0倍ならまずは数値をそのまま入れる
                        valueArray[data.selectCondition].x = data.changeValue;
                    }
                }
                else
                {
                    //足し算で加算分を足す
                    valueArray[data.selectCondition].y += data.changeValue;
                }

                //リストに追加するより先にやる
                //三つ同じイベントがある状態で三個までしか付与できないのが追加されたらどうなるんだろう？
                //それは四つ目で追加していい
                //しかしさらに追加されたら消える
                EndConditionCheck(data.selectCondition, EventEndCondition.付与回数);


                //ここで時間計測開始？
                if ( data.endCondition[0].endCondition == EventEndCondition.時間 )
                {

                }

                if ( !reset )
                {
                    //リストに追加
                    events.Add(data);
                    eventCount++;
                }


                //ユニークイベントがあるなら追加する
                //ユニークイベントは各魔法や武器が含む効果の中で象徴的な一つにだけつけるように
                //攻撃とガードを上げるような効果でも、攻撃アップ部分にだけユニークイベントを持たせる
                if ( data.uniqueType != UniqueEffect.なし )
                {
                    myConditionAbility.EffectAndIconAdd(data.uniqueType, data.selectCondition);
                }


            }


            /// <summary>
            /// 必要な数値を返す
            /// ちゃんと倍率と加算分どちらも処理してくれる
            /// originalValueは1とかの、元となる倍率
            /// 攻撃力求める時は攻撃力
            /// 
            /// 現在の倍率を反映した選択対象ごとの数値を返す
            /// 効果切れの時ヘルスから呼び出す
            /// </summary>
            /// <param name="selectCondition"></param>
            /// <param name="originalValue"></param>
            public float GetValue(int selectCondition, float originalValue)
            {

                if ( valueArray[selectCondition].x != 0 )
                {
                    //倍率をかける
                    originalValue *= valueArray[selectCondition].x;
                }

                //加算する
                return originalValue + valueArray[selectCondition].y;


            }



            #region 終了管理系

            /// <summary>
            /// 使用イベントが飛んで条件チェック
            /// 使用した対象はセレクトコンディションで見る
            /// イベントコンディションは状態管理機能自体が受け取ってここにイベントを飛ばす
            /// 
            /// 攻撃時の使用では複数属性での使用なども考えられる
            /// のでselectConditionは複数ビット演算可能
            /// 
            /// 道筋としては
            /// コントロール→状態管理スクリプト→イベントタイプからホルダーを割り出す→ホルダーから使用イベント飛ばす
            /// 
            /// 対応してるのは
            /// 装備、使用回数、付与回数
            /// </summary>
            /// <param name="useCondition">使用したイベント。複数ビット入れてヨシ</param>
            public void EndConditionCheck(int useCondition, EventEndCondition endCondition)
            {

                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに使用イベントを伝えていく
                    if ( events[i].ConditionCheck(useCondition, endCondition) )
                    {
                        //終了条件を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }

            /// <summary>
            /// 全ての効果について特定の終了条件を報告する
            /// 
            /// </summary>
            /// <param name="useCondition">使用したイベント。複数ビット入れてヨシ</param>
            public void AllEndConditionCheck(EventEndCondition endCondition)
            {

                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに使用イベントを伝えていく
                    if ( events[i].ConditionCheck(0, endCondition, true) )
                    {
                        //終了条件を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }


            ///　蓄積減少とかのチェックは分ける
            ///　蓄積イベントは固有でなければならないので、処理の時点で分ける



            /// <summary>
            /// 一秒に一回程度時間条件が満たされているかを確認する
            /// </summary>
            /// <param name="nowTime"></param>
            public void TimeCheck(int nowTime)
            {
                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに追加イベントを伝えていく
                    if ( events[i].TimeCheck(nowTime) )
                    {
                        //終了条件の追加回数を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }

            /// <summary>
            /// 特定の効果を一気に削除する
            /// デバフとかバフとか
            /// useBitの指定がなければ全てを全削除
            /// </summary>
            /// <param name="useBit">使うビット。デバフとかいろいろ設定可能</param>
            public void AllCancell(int useBit)
            {
                for ( int i = 0; i < eventCount; i++ )
                {
                    //ユニークイベントが削除対象か指定がないか
                    if ( useBit == 0 || (((int)events[i].uniqueType & useBit) > 0) )
                    {
                        EventEnd(events[i]);
                    }
                }
            }

            #endregion


            /// <summary>
            /// イベントの終了メソッド
            /// 
            /// 蓄積イベントの発症などはここではやらないことになった
            /// </summary>
            /// <param name="data"></param>
            public void EventEnd(ConditionDataValue data)
            {
                //データが削除された状態にする
                data.isDelete = true;

                //イベント削除
                events.Remove(data);

                eventCount--;


                //数値を元に戻す
                //乗算なら
                if ( !data.isAdd )
                {
                    //倍率に割り算をする
                    //最初にxの値を1にする手段を用意しないとね
                    valueArray[data.selectCondition].x /= data.changeValue;
                }
                else
                {
                    //足し算で加算分を戻す
                    valueArray[data.selectCondition].y -= data.changeValue;
                }

                //状態を反映
                myConditionAbility._character.characterController.ConditionValueChange(data);

                //ユニークイベントがあるなら
                if ( data.uniqueType != UniqueEffect.なし )
                {
                    bool isUniqueEnd = true;

                    for ( int i = 0; i < eventCount; i++ )
                    {
                        //まだ同じユニークイベント残ってるか
                        if ( events[i].uniqueType == data.uniqueType )
                        {
                            isUniqueEnd = false;
                            break;
                        }
                    }

                    //もうそのユニークイベントが残ってないなら
                    if ( isUniqueEnd )
                    {
                        myConditionAbility.ConditionDelete(data.uniqueType, data.selectCondition);
                    }
                }


            }



            #region 数値蓄積系管理


            /// <summary>
            /// 数値を蓄積していく
            /// そして発動する系の状態異常やらのチェック
            /// 
            /// 状態異常回復はその効果を受け取った時
            /// 終了条件とは独立した形で消す
            /// </summary>
            public bool SetRestoreCondition(ConditionDataValue data)
            {
                //すでにあるか
                //負ならすでにある
                int isContain = -1;

                if ( eventCount > 0 )
                {

                    for ( int i = 0; i < eventCount; i++ )
                    {
                        //イベントに追加イベントを伝えていく
                        if ( events[i].selectCondition == data.selectCondition )
                        {
                            isContain = i;
                            break;
                        }
                    }
                }

                //当てはまる状態異常蓄積がまだないなら
                //新たに登録
                if ( isContain == -1 )
                {
                    //蓄積値加算

                    //もし蓄積値を満たしたなら発症
                    bool isOver = data.endCondition[0].RestoreConditionController(true, data.endCondition[0].effectLimit);

                    if ( isOver )
                    {
                        //発症
                        RestoreEffectStart(data.selectCondition);

                        //一発発症なら蓄積追加はせずに戻る
                        return false;
                    }

                    //さらにイベントを加える
                    events.Add(data);
                    eventCount++;

                    return true;

                }
                //当てはまる状態異常蓄積が既にあるなら
                //そこに数値を追加
                //さらに効果を上書きする
                else
                {
                    //効果を上書き
                    events[isContain].changeValue = data.changeValue;

                    //さらに数値を加える
                    //もし蓄積値を満たしたなら発症
                    bool isOver = events[isContain].endCondition[0].RestoreConditionController(true, data.endCondition[0].effectLimit);

                    if ( isOver )
                    {

                        RestoreEffectStart(events[isContain].selectCondition);

                        //蓄積イベント終了
                        EventEnd(events[isContain]);
                    }
                    return false;
                }



            }


            ///　時間経過で蓄積減少するやつ作るぞ
            ///　
            /// 必要なもの
            /// 
            /// 時間間隔ごとに効果を発揮するやつ
            /// 時間間隔ごとに数値が減少していくやつ
            /// 
            /// 案１、時間管理系のイベントを一括で入れるリストに格納して一秒ごとにfor文を回す
            /// 案２、再帰関数の非同期メソッドを飛ばして時間計測、すでに消えてたら終了
            /// 
            /// ２かな。それぞれタイミングも違うし
            /// 
            /// 
            /// ///





            /// <summary>
            /// 数値蓄積イベントの発症
            /// </summary>
            void RestoreEffectStart(int selectCondition)
            {

                //毒発症
                if ( selectCondition == (int)RestoreEffect.毒蓄積 )
                {
                    //毒イベント発生

                }

            }




            #endregion

        }



        /// <summary>
        /// フラグ管理用のデータ格納クラス
        /// 特定のイベントタイプのフラグをすべてビットで管理
        /// </summary>
        public class BooleanEventHolder
        {


            /// <summary>
            /// イベントタイプごとに終了条件と対象をつめたリスト
            /// </summary>
            public List<ConditionDataBase> events;


            /// <summary>
            /// イベントタイプが含むフラグはビットで管理しようね
            /// </summary>
            public int valueBit;


            /// <summary>
            /// 保持しているイベント数
            /// </summary>
            int eventCount;


            /// <summary>
            /// エフェクト管理アビリティ
            /// </summary>
            public ConditionAndEffectControllAbility myConditionAbility;

            /// <summary>
            /// 最初からenumをintに変換して引数に与えよう
            /// </summary>
            /// <param name="selectCondition"></param>
            /// <param name="reset">ゲーム再起動時などに再設定するときのフラグ</param>
            public void SetValue(ConditionDataBase data, bool reset = false)
            {

                /*
                //開始ならフラグを真にする
                if (isStart)
                {
                    valueBit |= 1 << selectCondition;
                }
                //終了なら反転ビットでかつすることで消し去れる
                else
                {
                    valueBit &= ~(1 << selectCondition);
                }
                */

                //フラグを真にする
                valueBit |= 1 << data.selectCondition;


                //リストに追加するより先にやる
                //三つ同じイベントがある状態で三個までしか付与できないのが追加されたらどうなるんだろう？
                //それは四つ目で追加していい
                //しかしさらに追加されたら消える
                //これもう一個付与するとバリアは消えるな？
                EndConditionCheck(data.selectCondition, EventEndCondition.付与回数);


                if ( !reset )
                {
                    //リストに追加
                    events.Add(data);
                    eventCount++;
                }


                //ユニークイベントがあるなら追加する
                //ユニークイベントは各魔法や武器が含む効果の中で象徴的な一つにだけつけるように
                //攻撃とガードを上げるような効果でも、攻撃アップ部分にだけユニークイベントを持たせる
                //やっぱり全部に持たせていい。大体次のインデックスにユニークあるから
                //他のユニーク効果をまとめて消せるような管理上のメリットのがでかい
                if ( data.uniqueType != UniqueEffect.なし )
                {
                    myConditionAbility.EffectAndIconAdd(data.uniqueType, data.selectCondition);
                }



            }


            /// <summary>
            /// 最初からenumをintに変換して引数に与えよう
            /// 
            /// 現在の倍率を反映した数値を返す
            /// 効果切れの時ヘルスから呼び出す
            /// </summary>
            /// <param name="addSet"></param>
            /// <param name="selectCondition"></param>
            /// <param name="value"></param>
            public bool GetValue(int selectCondition)
            {
                //今のビットがそれを含んでいるか
                return (valueBit & 1 << selectCondition) > 0;
            }



            #region 終了管理系

            /// <summary>
            /// 使用イベントが飛んで条件チェック
            /// 使用した対象はセレクトコンディションで見る
            /// イベントコンディションは状態管理機能自体が受け取ってここにイベントを飛ばす
            /// 
            /// 攻撃時の使用では複数属性での使用なども考えられる
            /// のでselectConditionは複数ビット演算可能
            /// 
            /// 道筋としては
            /// コントロール→状態管理スクリプト→イベントタイプからホルダーを割り出す→ホルダーから使用イベント飛ばす
            /// 
            /// 対応してるのは
            /// 装備、使用回数、付与回数
            /// </summary>
            /// <param name="useCondition">使用したイベント。複数ビット入れてヨシ</param>
            public void EndConditionCheck(int useCondition, EventEndCondition endCondition)
            {

                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに使用イベントを伝えていく
                    if ( events[i].ConditionCheck(useCondition, endCondition) )
                    {
                        //終了条件を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }
            /// <summary>
            /// 全ての効果について特定の終了条件を報告する
            /// 
            /// </summary>
            /// <param name="useCondition">使用したイベント。複数ビット入れてヨシ</param>
            public void AllEndConditionCheck(EventEndCondition endCondition)
            {

                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに使用イベントを伝えていく
                    if ( events[i].ConditionCheck(0, endCondition, true) )
                    {
                        //終了条件を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }

            ///　蓄積減少とかのチェックは分ける
            ///　蓄積イベントは固有でなければならないので、処理の時点で分ける

            /// <summary>
            /// 一秒に一回程度時間条件が満たされているかを確認する
            /// </summary>
            /// <param name="nowTime"></param>
            public void TimeCheck(int nowTime)
            {
                for ( int i = 0; i < eventCount; i++ )
                {
                    //イベントに追加イベントを伝えていく
                    if ( events[i].TimeCheck(nowTime) )
                    {
                        //終了条件の追加回数を超えてたら消す
                        EventEnd(events[i]);
                    }
                }
            }


            ///　やること
            ///　蓄積減少系の処理作成（一秒に一回。またイベント追加時に処理分ける。蓄積がアクティブなら既存のイベントに数値を足すような形。耐性はヘルスで蓄積値を減らしてこっちに飛ばす）
            ///　（そういえば毒耐性強化イベントとかあったっけ？）
            ///　あとは全体管理システムを作る
            ///　攻撃時とかに使用時とかのイベント飛ばすのはイベントタイプごとにする
            ///　使用、付与、などは個別で使用先から通知
            ///　時間と蓄積に関しては頑張る
            ///　



            /// <summary>
            /// 特定の効果を一気に削除する
            /// デバフとかバフとか
            /// useBitの指定がなければ全てを全削除
            /// </summary>
            /// <param name="useBit">使うビット。デバフとかいろいろ設定可能</param>
            public void AllCancell(int useBit)
            {
                for ( int i = 0; i < eventCount; i++ )
                {
                    //ユニークイベントが削除対象か指定がないか
                    if ( useBit == 0 || (((int)events[i].uniqueType & useBit) > 0) )
                    {
                        EventEnd(events[i]);
                    }
                }
            }



            #endregion


            /// <summary>
            /// イベントの終了メソッド
            /// ここで毒蓄積とかは毒に変わる
            /// </summary>
            /// <param name="data"></param>
            public void EventEnd(ConditionDataBase data)
            {
                //データが削除された状態にする
                data.isDelete = true;

                //イベント削除
                events.Remove(data);
                eventCount--;


                //ユニークイベントがあるなら
                if ( data.uniqueType != UniqueEffect.なし )
                {
                    bool isUniqueEnd = true;

                    for ( int i = 0; i < eventCount; i++ )
                    {
                        //まだ同じユニークイベント残ってるか
                        if ( events[i].uniqueType == data.uniqueType )
                        {
                            isUniqueEnd = false;
                            break;
                        }
                    }

                    //もうそのユニークイベントが残ってないなら
                    if ( isUniqueEnd )
                    {
                        myConditionAbility.ConditionDelete(data.uniqueType, data.selectCondition);
                    }
                }
            }


            #region 数値蓄積系管理


            /// <summary>
            /// 数値を蓄積していく
            /// そして発動する系の状態異常やらのチェック
            /// 
            /// 状態異常回復はその効果を受け取った時
            /// 終了条件とは独立した形で消す
            /// </summary>
            public bool SetRestoreCondition(ConditionDataBase data)
            {
                //すでにあるか
                //負ならすでにある
                int isContain = -1;

                if ( eventCount > 0 )
                {

                    for ( int i = 0; i < eventCount; i++ )
                    {
                        //イベントに追加イベントを伝えていく
                        if ( events[i].selectCondition == data.selectCondition )
                        {
                            isContain = i;
                            break;
                        }
                    }
                }

                //当てはまる状態異常蓄積がまだないなら
                //新たに登録
                if ( isContain == -1 )
                {
                    //蓄積値加算

                    //もし蓄積値を満たしたなら発症
                    bool isOver = data.endCondition[0].RestoreConditionController(true, data.endCondition[0].effectLimit);

                    if ( isOver )
                    {
                        //発症
                        RestoreEffectStart(data.selectCondition);

                        //一発発症なら蓄積追加はせずに戻る
                        return false;
                    }

                    //さらにイベントを加える
                    events.Add(data);
                    eventCount++;

                    return true;

                }
                //当てはまる状態異常蓄積が既にあるなら
                //そこに数値を追加
                //さらに効果を上書きする
                else
                {

                    //さらに数値を加える
                    //もし蓄積値を満たしたなら発症
                    bool isOver = events[isContain].endCondition[0].RestoreConditionController(true, data.endCondition[0].effectLimit);

                    if ( isOver )
                    {

                        RestoreEffectStart(events[isContain].selectCondition);

                        //蓄積イベント終了
                        EventEnd(events[isContain]);
                    }
                    return false;
                }



            }


            ///　時間経過で蓄積減少するやつ作るぞ
            ///　
            /// 必要なもの
            /// 
            /// 時間間隔ごとに効果を発揮するやつ
            /// 時間間隔ごとに数値が減少していくやつ
            /// 
            /// 案１、時間管理系のイベントを一括で入れるリストに格納して一秒ごとにfor文を回す
            /// 案２、再帰関数の非同期メソッドを飛ばして時間計測、すでに消えてたら終了
            /// 
            /// ２かな。それぞれタイミングも違うし
            /// 
            /// 
            /// ///





            /// <summary>
            /// 数値蓄積イベントの発症
            /// </summary>
            void RestoreEffectStart(int selectCondition)
            {

                //毒発症
                if ( selectCondition == (int)RestoreEffect.毒蓄積 )
                {
                    //毒イベント発生

                }

            }




            #endregion



        }


        #endregion


        /// <summary>
        /// 状態異常蓄積の低減率
        /// あるいは発症までの数値を管理？
        /// 状態異常はこのクラスで管理する
        /// 
        /// 状態異常のenumを入れてアクセスできるよん
        /// </summary>
        public Dictionary<RestoreEffect, int> resistDictionary;



        #endregion

        #region 武器や魔法、アイテムなどの外部クラスで効果を管理するための定義



        /// <summary>
        /// 起こったイベントを管理するための物
        /// 記録して解除するために
        /// 
        /// 回数上限がある場合は使用報告を受ける
        /// 装備外れた時にバフ解除を受ける
        /// 時間切れデバフが切れる
        /// 
        /// 必要なもの
        /// ・解除条件
        /// ・記録用変数
        /// ・イベントタイプ eventType
        /// ・作用する対象 selectCondition
        /// ・作用内容（数値の場合は加算か倍率か見分けるフラグを持つ） 
        /// 
        /// 変更点はこのアビリティでデータを持つこと
        /// これは設定で使うためにある
        /// 武器の上とかで効力とかをこれで決める
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class ConditionDataContainerBoolean<T> where T : struct, IComparable, IConvertible, IFormattable
        {


            /// <summary>
            /// 複数効果を持つユニークエフェクトの一部であるか
            /// もしそうなら管理方式が変わる
            /// 終了条件では重ねがけを許しつつも個別に監視する
            /// 
            /// ユニークイベントがある場合for文の中でイベントタイプでエフェクトを飛ばさない
            /// 最後にユニークイベントでエフェクトやアイコンを出す
            /// </summary>
            [Header("特殊な効果の所属設定")]
            public UniqueEffect uniqueType;


            /// <summary>
            /// イベントのタイプ
            /// ユニークイベントが定義されていなければこれでアイコンとか出す
            /// </summary>
            [Header("効果の分類")]
            public EventType _type;


            /// <summary>
            /// 特定のタイプのイベントの中で
            /// 選択した条件をenumからintに変換してする
            /// </summary>
            [Header("選択する効果")]
            public T selectCondition;

            /// <summary>
            /// 終わる条件
            /// </summary>
            [Header("終了条件。時間と蓄積は配列の0に")]
            public EventEndJudge[] endCondition;



        }

        /// <summary>
        /// 起こったイベントを管理するための物
        /// 記録して解除するために
        /// 
        /// 回数上限がある場合は使用報告を受ける
        /// 装備外れた時にバフ解除を受ける
        /// 時間切れデバフが切れる
        /// 
        /// 必要なもの
        /// ・解除条件
        /// ・記録用変数
        /// ・イベントタイプ eventType
        /// ・作用する対象 selectCondition
        /// ・作用内容（数値の場合は加算か倍率か見分けるフラグを持つ） 
        /// 
        /// 変更点はこのアビリティでデータを持つこと
        /// これは設定で使うためにある
        /// 武器の上とかで効力とかをこれで決める
        /// 
        /// 
        /// 数値変更を司るこちらでは効果量などが追加される
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class ConditionDataContainerFloat<T> : ConditionDataContainerBoolean<T> where T : struct, IComparable, IConvertible, IFormattable
        {
            /// <summary>
            /// 乗算効果であるかどうか
            /// </summary>
            public bool multipler;

            /// <summary>
            /// 具体的な効果量
            /// </summary>
            public float effectAmount;

        }

        #endregion



        #endregion
        /// このメソッドは、ヘルプボックスのテキストを表示するためにのみ使用されます。
        /// 能力のインスペクタの冒頭にある
        public override string HelpBoxText() { return "状態異常含むデバフやバフを管理する"; }


        #region イベント管理用の変数

        /// <summary>
        /// イベントホルダーのディクショナリー
        /// 必要に応じて要素をカスタム
        /// オーディンでシリアライズしようね
        /// </summary>
        [SerializeField]
        Dictionary<EventType, ValueEventHolder> valueEvents;

        /// <summary>
        /// イベントホルダーのディクショナリー
        /// 必要に応じて要素をカスタム
        /// オーディンでシリアライズしようね
        /// </summary>
        [SerializeField]
        Dictionary<EventType, BooleanEventHolder> booleanEvents;




        /// <summary>
        /// スプライトシェーダーのエフェクト
        /// 必要に応じてSetActive
        /// オーディンでシリアライズしようね
        /// </summary>
        [SerializeField]
        Dictionary<UniqueEffect, GameObject> effectDictionary;

        /// <summary>
        /// 要素がゼロ以上の数値系のイベントの数
        /// </summary>
        int valueEventsCount;

        /// <summary>
        /// 要素がゼロ以上のフラグ系のイベントの数
        /// </summary>
        int booleanEventsCount;

        /// <summary>
        /// バフの効果時間の倍率
        /// </summary>
        public float buffTimeMultipler = 1;

        /// <summary>
        /// デバフの効果時間の倍率
        /// </summary>
        public float debuffTimeMultipler = 1;


        #endregion

        #region ユニークエフェクト関連の変数

        /// <summary>
        /// これでビット演算する
        /// これにエフェクトを加えたり引いたりする
        /// これを使って現在そのエフェクトがあるかを見れる
        /// </summary>
        int uniqueSetChecker;

        /// <summary>
        /// ユニークエフェクトの数
        /// </summary>
        readonly int uniqueEffectCount = 28;

        /// <summary>
        /// いま最も優先度が高く表示されているエフェクト
        /// </summary>
        UniqueEffect nowMainEffect;





        #endregion





        /// <summary>
        ///　ここで、パラメータを初期化する必要があります。
        /// </summary>
        protected override void Initialization()
        {
            base.Initialization();


            //ホルダーにアビリティへの参照を与える
            foreach ( ValueEventHolder holder in valueEvents.Values )
            {
                holder.myConditionAbility = this;
            }

            foreach ( BooleanEventHolder holder in booleanEvents.Values )
            {
                holder.myConditionAbility = this;
            }

        }

        //効果消去系イベントは個別にイベント受け取りやるか
        //そもそもそのホルダーがあるかどうかのチェックとかも
        //あとデータ削除時そのデータをもとに戻すやつとかもやらないと

        /// 
        /// HPやMPの回復、アイテム効果発動などは発動エフェクトが必要
        /// あとHPやMPの回復はリジェネじゃないなら別処理にする？
        /// これはエフェクトマネ―ジャーが必要？
        /// あとサウンドも必要だよね
        /// 音系統はエフェクトの音みたいなのをマネージャーに持たせておいて、エフェクト（スプライト含む）
        /// 発動時に引き出す感じでいいか
        /// 
        /// ///



        #region コンディション追加・削除

        /// <summary>
        /// イベントを設定するためのメソッド
        /// フラグ管理のためのオーバーロード
        /// 効果のエフェクトを要求するのは別の処理
        /// </summary>
        /// <param name="uniqueType"></param>
        /// <param name="eventType"></param>
        /// <param name="selectCondition"></param>
        /// <param name="endCondition"></param>
        /// <param name="efectLimit"></param>
        /// <param name="value"></param>
        /// <param name="isAdd"></param>
        public void ConditionSetting(ConditionDataBase addData)//,float value,bool isAdd)
        {

            if ( addData.eventType == EventType.イベント解除 )
            {
                EventCancel(addData.selectCondition);
                return;
            }




            //ここから終了条件に応じて時間カウントとかを始める

            //終了条件に徐々に数値が減るやつが含まれてるなら
            if ( addData.endCondition[0].endCondition == EventEndCondition.数値減少 )
            {
                if ( booleanEvents[addData.eventType].SetRestoreCondition(addData) )
                {
                    EffectValueDecrease(addData, MyCode.SoundManager.instance.restoreDicreaceDict[(RestoreEffect)addData.selectCondition], false).Forget();
                }
                return;
            }
            //終了時間に時間カウントあるなら制限時間開始
            else if ( addData.endCondition[0].endCondition == EventEndCondition.時間 )
            {
                LimitWait(addData, true).Forget();
            }


            //こっからイベントタイプごとに分けたBooleanEventHolderを持ってきて
            //値をリスト（フラグの場合配列でもいい）に格納して、setValueまで
            booleanEvents[addData.eventType].SetValue(addData);
        }

        /// <summary>
        /// イベントを設定するためのメソッド
        /// 数値管理のためのオーバーロード
        /// </summary>
        /// <param name="uniqueType"></param>
        /// <param name="eventType"></param>
        /// <param name="selectCondition"></param>
        /// <param name="endCondition"></param>
        /// <param name="efectLimit"></param>
        /// <param name="value"></param>
        /// <param name="isAdd"></param>
        public void ConditionSetting(ConditionDataValue addData)
        {


            if ( addData.eventType == EventType.イベント解除 )
            {
                EventCancel(addData.selectCondition);
                return;
            }



            if ( addData.endCondition[0].endCondition == EventEndCondition.数値減少 )
            {
                if ( valueEvents[addData.eventType].SetRestoreCondition(addData) )
                {
                    EffectValueDecrease(addData, MyCode.SoundManager.instance.restoreDicreaceDict[(RestoreEffect)addData.selectCondition], false).Forget();
                }
                return;
            }
            //制限時間開始
            else if ( addData.endCondition[0].endCondition == EventEndCondition.時間 )
            {
                LimitWait(addData, true).Forget();
            }
            //即時効果というか、ヘルスイベントなら効果発揮してリターン
            //即時効果はHPやMPの変動だけという前提
            else if ( addData.endCondition[0].endCondition == EventEndCondition.即時 )
            {
                //ヘルスイベント実行
                _character.characterController.InstantEffect((HealthEventSelect)addData.selectCondition, addData.changeValue);
                return;
            }

            //ここではヘルスイベントの継続効果を開始する
            if ( addData.eventType == EventType.ヘルスイベント )
            {
                //効果繰り返し開始
                EffectTimeWait(addData).Forget();
            }


            //こっからイベントタイプごとに分けたHolderを持ってきて
            //値をリストに格納して、setValueまで
            valueEvents[addData.eventType].SetValue(addData);

            _character.characterController.ConditionValueChange(addData);

        }


        /// <summary>
        /// そのユニークエフェクトが今現在あるかを確認する
        /// でもこれはキャラデータに入れる？
        /// </summary>
        /// <param name="effect"></param>
        /// <returns></returns>
        public bool UniqueConditionCheck(UniqueEffect effect)
        {
            return (uniqueSetChecker & (int)effect) > 0;
        }

        //　やること、体力変動処理を書く。フラグは基本ここで管理。数値の初期値を管理する方法考える


        /// <summary>
        /// ゲーム再開時などに状態変化を再現するメソッド
        /// セーブロードとも関わるか
        /// プレイヤーの場合ステータス設定前に必ずやるよ
        /// 最初のセーブロード処理に挟み込むか
        /// </summary>
        public void ConditionRestoration()
        {
            int count;

            //フラグイベント再点火
            foreach ( BooleanEventHolder holder in booleanEvents.Values )
            {
                count = holder.events.Count;

                for ( int i = 0; i < count; i++ )
                {
                    holder.SetValue(holder.events[i], true);
                }
            }

            //数値イベント再点火
            foreach ( ValueEventHolder holder in valueEvents.Values )
            {
                count = holder.events.Count;

                for ( int i = 0; i < count; i++ )
                {
                    holder.SetValue(holder.events[i], true);
                }
            }


        }



        /// <summary>
        /// イベントタイプ状態異常解除で発生
        /// フラグタイプのイベントで発生して、バリューとブール両方の
        /// 
        /// ここでイベントキャンセルエフェクトやキャンセル音も出す
        /// 引数でどのイベントのどの効果を削除するか決定可能
        /// </summary>
        void EventCancel(int selectCondition)
        {
            if ( selectCondition == (int)EventCancelCondition.全て )
            {
                EventCancelExe(0, 0);
            }
            else if ( selectCondition == (int)EventCancelCondition.バフ )
            {
                EventCancelExe((int)GManager.instance.gameData.positiveEffect, 0);
            }
            else if ( selectCondition == (int)EventCancelCondition.デバフ )
            {
                EventCancelExe((int)GManager.instance.gameData.negativeEffect, 0);
            }
        }

        /// <summary>
        /// イベントタイプ解除の実行
        /// 指定のビットに当てはまるイベントを全て消す
        /// 攻撃力上昇削除とかステータス上昇削除とかは数値だけでいいのでselectTypeで指定な
        /// </summary>
        /// <param name="useBit">使用するビット。攻撃強化、などの特定の効果を指定して消去</param>
        /// <param name="selectType">どんな条件で照会するか。0なら両方、1なら数値効果、2ならbool効果だけ</param>
        void EventCancelExe(int useBit, int selectType)
        {
            if ( selectType == 0 || selectType == 1 )
            {
                foreach ( ValueEventHolder events in valueEvents.Values )
                {
                    events.AllCancell(useBit);
                }
            }

            if ( selectType == 0 || selectType == 2 )
            {
                foreach ( BooleanEventHolder events in booleanEvents.Values )
                {
                    events.AllCancell(useBit);
                }
            }


        }



        /// <summary>
        /// 使用回数や装備変更など、終了条件が進行した時に外で呼ぶ
        /// たとえば一回攻撃するまでの攻撃バフとかなら、攻撃ヒット後に飛ばすみたいな
        /// イベントタイプと条件だけでやる？
        /// セレクトコンディションも付け加えるか？
        /// 
        /// 問題点
        /// 攻撃ヒットするたびに攻撃回数条件があるかを確認する、とかだったら攻撃するたびにfor回すのか？
        /// 
        /// 回数条件で見るやつがあるかを保管しておいてビット演算する
        /// 非同期処理に投げてメインスレッドの負荷軽くする？
        /// てかここのアビリティのループ処理はUnityAPI使わないから非同期し放題なんだよね
        /// </summary>
        public void EndConditionTrigger(EventEndCondition checkCondition, EventType useType, int selectCondition)
        {
            valueEvents[useType].EndConditionCheck(selectCondition, checkCondition);
        }


        /// <summary>
        /// 数値コンディションとフラグコンディション
        /// 全てについて特定の終了条件をレポート
        /// </summary>
        /// <param name="condition"></param>
        public void AllEventConditionTrigger(EventEndCondition condition)
        {

            foreach ( ValueEventHolder events in valueEvents.Values )
            {
                events.AllEndConditionCheck(condition);
            }


            foreach ( BooleanEventHolder events in booleanEvents.Values )
            {
                events.AllEndConditionCheck(condition);
            }


        }



        #endregion


        #region 外部からの値獲得


        /// 
        /// ステータスセット系の数値変更は個別に用意する？
        /// それかイベントタイプがステータスだったらコントロールに処理が飛ぶようにするか
        /// 
        /// ///


        /// <summary>
        /// イベントタイプとセレクトコンディションから数値をゲットする
        /// </summary>
        public float GetValueData(EventType eventType, int selectCondition, float originalValue)
        {
            return valueEvents[eventType].GetValue(selectCondition, originalValue);
        }


        /// <summary>
        /// イベントタイプとセレクトコンディションからフラグをゲットする
        /// </summary>
        public bool GetBooleanData(EventType eventType, int selectCondition)
        {
            return booleanEvents[eventType].GetValue(selectCondition);
        }


        /// <summary>
        /// トリガーイベントが使用されるかどうかを見る
        /// 攻撃後、ジャスガ時などトリガーイベントで使える
        /// </summary>
        /// <returns></returns>
        public void CheckTriggerEvent(EventType triggerType)
        {
            for ( int i = 0; i < 9; i++ )
            {
                if ( GetBooleanData(triggerType, i) )
                {
                    ConditionSetting(GManager.instance.gameData.triggerData[(TriggerEventSelect)i]);
                }
            }
        }




        /// <summary>
        /// 数値データ変動時に
        /// ステータスが変動していないかを確認し反映していく
        /// 
        /// ステータスデータを詰め合わせたクラスに、イベントタイプとせれこんで値を変える機能入れとかないとだな
        /// 
        ///              これいらない
        /// </summary>
        /// <param name="type"></param>
        void StatusDataUpdate(EventType type, int selectCondition)
        {

            if ( type == EventType.基礎ステータス効果 )
            {
                /*BaseStatusChangeSelect
            HP変動,//乗算加算含む。でも最大じゃないなら乗算する意味ある？
            MP変動,
            MP回復速度変動,//これはシスターさんかな
            最大HP変動,
            最大MP変動,
            最大スタミナ変動,//ここまで乗算加算含む
            スタミナ回復速度変動,
            スタミナ消費倍率,//ガードスタミナだけは含まない
            最大アーマー変動,
            アーマー回復速度変動,
            ヘイト倍率変動,
            バフ効果時間変化,
            デバフ効果時間変化,
                        アーマー削り倍率 //盾も含もう
                アーマー削られ倍率
                 */



            }
            else if ( type == EventType.与ダメージ倍率変動 || type == EventType.被ダメージ倍率変動 ||
                type == EventType.攻撃力変動効果 || type == EventType.防御力変動効果 || type == EventType.ガード性能変動効果 )
            {
                /*
            全体,
            物理,
            斬撃,
            打撃,
            刺突,
            炎,
            雷,
            聖,
            闇,

                */

            }
            else if ( type == EventType.アクション変化 )
            {
                /*
             移動速度倍率変動,
            回避距離＿時間変動,
            ジャンプ回数変動,
                 */

            }
            else if ( type == EventType.プレイヤーステータス効果 )
            {
                /*
                             全能力値,
            生命力,
            持久力,
            魔力,
            筋力,
            技量,
            賢さ
                 */

            }
            else if ( type == EventType.アイテム効果変動 )
            {
                /*
                 
                 */

            }
            else if ( type == EventType )
            {
                /*
                 
                 */

            }
            else if ( type == EventType )
            {
                /*
                 
                 */

            }

        }



        #endregion 



        #region アイコン、エフェクト管理



        /// <summary>
        /// エフェクトとアイコン追加
        /// エフェクトはディクショナリーで管理
        /// アイコンはisPlayerなら起動
        /// UIスクリプトにUniqueEventを渡して決定
        /// 
        /// 
        /// 
        /// </summary>
        void EffectAndIconAdd(UniqueEffect addEffect, int selectCondition)
        {

            //エンチャントを消す
            //エンチャがあるかどうかはそのほかのエフェクトの状態に何ら影響を与えない
            //あとエンチャとバリアはアイコン出さない、見ればわかるので
            if ( addEffect == UniqueEffect.エンチャント )
            {
                //ここでエンチャントディクショナリから引きだす感じ？
                //エンチャント処理は武器参照したりあるよね
                //ただ武器にかぶせてるテクスチャへの参照を持たせる？

            }
            //バリア召喚
            //バリアもプールするか？
            //魔法エフェクトプールに含むか
            //バリアは魔法エフェクト出現で出てくるのよ
            //そして効果時間満了で勝手に消えるわけ
            //ならだよ、魔法の方に出現後ターゲットの子オブジェクトになるオプション入れるか
            //バリアの音は魔法の発生音でいい
            //エンチャには同じことできるかな？
            //少なくとも音は魔法発生の音でいい
            else if ( addEffect == UniqueEffect.バリア )
            {
                _character.characterController.BarrierSet((BarrierSelect)selectCondition);
            }
            else
            {

                //ここで音鳴らす
                MyCode.SoundManager.instance.ConditionEffectSound(addEffect, _character.characterPosition);


                //エフェクトが既に含まれているなら
                if ( ((int)addEffect & uniqueSetChecker) > 0 )
                {
                    return;
                }

            }

            //エフェクトを追加
            uniqueSetChecker |= (int)addEffect;

            //そしてエフェクトをセット
            //結局アイコン更新で全部見るから処理は共通でいい
            //その時の一番優先度が高いやつとか見なくていい
            EffectAndIconSet();


        }


        /// <summary>
        /// ユニークエフェクトの消滅時におこなう処理
        /// まずアイコンを消す
        /// そしてアイコンの中で新しく有効になるものを探す
        /// 
        /// エフェクトも消す
        /// そして一番優先度が高いエフェクトを探す
        /// 優先度は基本デバフが一つ上になるようにする？
        /// 
        /// アイコン一つ消して一つ入れる
        /// なのでアイコンもエフェクトも同じ現在一番優先度高いエフェクトを求めてる
        /// あと高いのを入れた時入れ替える機能もつけないとな
        /// 
        /// イべントを消した後、同じユニークイベントがなければこれ呼んで削除する
        /// </summary>
        void ConditionDelete(UniqueEffect deleteEffect, int selectCondition)
        {

            //エンチャントを消す
            //エンチャがあるかどうかはそのほかのエフェクトの状態に何ら影響を与えない
            //あとエンチャとバリアはアイコン出さない、見ればわかるので
            //音の処理も個別にするか
            if ( deleteEffect == UniqueEffect.バリア )
            {
                _character.characterController.BarrierSet(BarrierSelect.なし);
            }



            //反転ビットでエフェクトを削除
            uniqueSetChecker &= ~(int)deleteEffect;

            EffectAndIconSet();
        }




        /// <summary>
        /// エフェクトとアイコン管理
        /// エフェクトはディクショナリーで管理
        /// アイコンはisPlayerなら起動
        /// UIスクリプトにUniqueEventを渡して決定
        /// </summary>
        void EffectAndIconSet()
        {

            int maxBitPoint;

            //ユニークエフェクトがなしじゃないなら
            if ( uniqueSetChecker > 0 )
            {
                //優先度最大のビットは何桁目か
                maxBitPoint = MaxBitCheck(0);

                //ビットでエフェクトをセットする
                EffectSetExe((UniqueEffect)(1 << maxBitPoint));

            }
            else
            {
                maxBitPoint = -1;
            }


            //ここから先はプレイヤーだけ
            if ( !isPlayer )
            {
                return;
            }


            //アイコンは八個までとする
            for ( int i = 0; i < 8; i++ )
            {
                //返り値が真で、まだ次のアイコンをセットする余地があるなら
                if ( MainUICon.instance.ConditionIconSet(maxBitPoint, i) )
                {
                    //1足すことで一つずらす
                    maxBitPoint = MaxBitCheck(uniqueEffectCount - maxBitPoint + 1);
                }
                else
                {
                    break;
                }
            }


        }


        /// <summary>
        /// まとう効果エフェクトを実際につける
        /// 
        /// </summary>
        void EffectSetExe(UniqueEffect effect)
        {
            //今のメインと同じか、対応するエフェクトがないなら戻る
            if ( effect == nowMainEffect || !effectDictionary.ContainsKey(effect) )
            {
                return;
            }

            //前のを消して
            effectDictionary[nowMainEffect].SetActive(false);

            //今のをつける
            effectDictionary[effect].SetActive(true);

            nowMainEffect = effect;

        }




        /// <summary>
        /// スタートポジションから探して、今何桁目に最大のビットがあるのかを知るメソッド
        /// </summary>
        /// <param name="startPoint"></param>
        /// <returns></returns>
        int MaxBitCheck(int startPoint)
        {
            //ループする数をチェック
            int length = uniqueEffectCount - startPoint;

            if ( length < 0 )
            {
                return -1;
            }

            //最初に使うビット
            int useBit = (1 << startPoint);

            for ( int i = 0; i < length; i-- )
            {
                //一つずつビットをずらしていく
                if ( ((useBit << i) & uniqueSetChecker) > 0 )
                {
                    //あてはまるビットが何桁目にあるかを返す
                    return (uniqueEffectCount - (i + startPoint));
                }

            }

            return -1;
        }





        #endregion



        #region 時間管理


        /// <summary>
        /// 制限時間待ち
        /// 効果終了してたらやめる
        /// これは全部共通
        /// </summary>
        async UniTaskVoid LimitWait(ConditionDataBase data, bool isValue)
        {



            float startTime = GManager.instance.nowTime;

            //時間待つ
            //時間系のやつは必ず最初の条件にする
            //デバフ倍率かバフ倍率をかける。倍率変動にも対応するように計算式埋め込み
            await UniTask.WaitUntil(() => (GManager.instance.nowTime - startTime > (data.isDebuff ? data.endCondition[0].effectLimit * debuffTimeMultipler : data.endCondition[0].effectLimit * buffTimeMultipler))
            );

            //削除されてるなら
            if ( data.isDelete || !this.gameObject.activeSelf )
            {
                //要素を破棄して戻る
                data = null;

                return;

            }

            //子クラスから親クラスへとキャストしたとしても
            //元の子クラスの要素への参照は変数から消えてない
            //参照型だから。なんでリストからの削除に使える

            //数値データなら
            if ( isValue )
            {
                valueEvents[data.eventType].EventEnd((ConditionDataValue)data);
            }
            //フラグデータなら
            else
            {
                booleanEvents[data.eventType].EventEnd(data);
            }

        }

        /// <summary>
        /// 時限で数値蓄積量を減らす
        /// 毒とかの蓄積量な
        /// 
        /// こちらは蓄積量なので、数値データ以外の存在も想定していく
        /// どうやって蓄積の終了条件を配列から読み取るか
        /// 0に固定する？
        /// </summary>
        public async UniTaskVoid EffectValueDecrease(ConditionDataBase data, float waitTime, bool isValue)
        {

            while ( true )
            {

                //時間待つ
                await UniTask.Delay(TimeSpan.FromSeconds(waitTime), cancellationToken);

                //削除されてるなら
                if ( data.isDelete || !gameObject.activeSelf )
                {
                    //要素を破棄して戻る
                    data = null;

                    return;

                }


                //蓄積が限度超えてたら
                //何もせず戻る
                if ( data.endCondition[0].limitCounter >= 100 )
                {
                    return;
                }


                //値を減らす。
                //減らす量も考慮するか、あるいは減らす時間の変化だけで対応するか
                data.endCondition[0].limitCounter -= 10;



                //数値がゼロ以下になったら廃棄
                if ( data.endCondition[0].limitCounter <= 0 )
                {
                    //数値データなら
                    if ( isValue )
                    {
                        valueEvents[data.eventType].EventEnd((ConditionDataValue)data);
                    }
                    //フラグデータなら
                    else
                    {
                        booleanEvents[data.eventType].EventEnd(data);
                    }

                    return;
                }

            }
        }



        /// <summary>
        /// なんで数値データだけなのか
        /// それは、時限効果で何度も使って意味があるのは数値データしかないから
        /// 一定時間ごとに効果発生するのを待つ
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        async UniTaskVoid EffectTimeWait(ConditionDataValue data)
        {
            while ( true )
            {
                //時間待つ
                //これ破棄されない可能性あるな、データ削除前にキャラが死んだら
                await UniTask.Delay(TimeSpan.FromSeconds(data.endCondition[0].effectLimit));

                //削除されたなら
                if ( data.isDelete || gameObject.activeSelf == false )
                {
                    //要素を破棄して戻る
                    data = null;

                    return;

                }

                //ここで効果発生
                //リジェネとかどうしようか
            }

        }


        #endregion





















        #region ギミック

        /// <summary>
        /// アイテムの効果を記す
        /// </summary>
        /// <param name="data"></param>
        void GimickAct(EventData data, Collider2D collision)
        {
            Debug.Log($"tttttt{data.type}");
            //即時効果
            if ( data.effectTime == 0 || data.type == EventType.scoreGet )
            {
                if ( data.type == EventType.damage )
                {
                    //無敵なら
                    if ( _myStatus.invincible )
                    {
                        //無敵の音鳴らす？

                        return;
                    }

                    //スコアも10へる
                    ScoreManager.instance.ScoreChange(-100);
                    bool isDie = ScoreManager.instance.LifeChange(true);

                    StopAction();

                    gameObject.layer = 8;

                    if ( isDie )
                    {
                        ScoreManager.instance.isDie = true;
                        _myStatus.Die = true;
                        efCon.ActionChange(CharacterController.CharacterState.none);
                        efCon.ConditionChange(CharacterCondition.die);
                    }
                    else
                    {
                        efCon.ConditionChange(CharacterCondition.damage);
                    }
                }
                else if ( data.type == EventType.recover )
                {
                    efCon.PlaySound("Recover", ScoreManager.instance.PlayerPosi);
                    ScoreManager.instance.LifeChange();
                    //回復エフェクト
                    efCon.ConditionChange(CharacterCondition.heal);
                }

                else if ( data.type == EventType.scoreGet )
                {
                    efCon.PlaySound("ScoreUp", ScoreManager.instance.PlayerPosi);
                    ScoreManager.instance.ScoreChange((int)data.effectTime);
                }
                else if ( data.type == EventType.Random )
                {

                }
            }

            //時間持続効果
            else
            {
                //無敵ならバッドステータスは受け付けない
                if ( _myStatus.invincible && data.bad )
                {
                    //無敵の音鳴らす？
                    return;
                }

                ConditionContraller(data);
            }


        }

        void ConditionContraller(EventData data)
        {



            if ( _effectData.Any() )
            {
                for ( int i = 0; i < _effectData.Count; i++ )
                {
                    if ( data.type == _effectData[i].type )
                    {
                        //   Debug.Log($"あdsasdasdwewer{data.type}{_effectData[i].type}{_effectData.Count}");
                        return;
                    }
                }
            }

            if ( !data.bad )
            {
                //  Debug.Log("wsdaewer");
                if ( data.type == EventType.invincible )
                {
                    _myStatus.invincible = true;
                    SpriteEffect.SetActive(true);
                    //  Debug.Log($"あer{_myStatus.invincible}");
                    //無敵の音鳴らす?
                    MasterAudio.PlaySound3DAtVector3AndForget("Incivle", transform.position);
                }
                else if ( data.type == EventType.boostJump )
                {
                    _myStatus.boostJump = true;
                    efCon.BuffChange(ExtraCondition.boostJump);
                }


                /*
                             else if (data.type == EventType)
                {

                }
                 */
            }
            else
            {
                if ( data.type == EventType.badSight )
                {
                    MasterAudio.PlaySound3DAtVector3AndForget("BadSight", transform.position);
                    BeautifySettings.settings.blurIntensity.value = 1.2f;
                }
            }

            data.timer = Time.time;
            //  Debug.Log($"あああ{data.type}{data.timer}");
            _effectData.Add(data);

        }


        /// <summary>
        /// 起動時に状態を元に戻す
        /// </summary>
        void ConditionRecovery(EventData[] dataArray)//, GimickCondition visualData)
        {


            for ( int i = 0; i < dataArray.Length; i++ )
            {
                if ( !dataArray[i].bad )
                {
                    //  Debug.Log("wsdaewer");
                    if ( dataArray[i].type == EventType.invincible )
                    {
                        _myStatus.invincible = true;
                        SpriteEffect.SetActive(true);
                        //  Debug.Log($"あer{_myStatus.invincible}");
                        //無敵の音鳴らす?
                        MasterAudio.PlaySound3DAtVector3AndForget("Incivle", transform.position);
                    }
                    else if ( dataArray[i].type == EventType.boostJump )
                    {
                        _myStatus.boostJump = true;
                        efCon.BuffChange(ExtraCondition.boostJump);
                    }

                }
                else
                {
                    if ( dataArray[i].type == EventType.badSight )
                    {
                        MasterAudio.PlaySound3DAtVector3AndForget("BadSight", transform.position);
                        BeautifySettings.settings.blurIntensity.value = 1.2f;
                    }
                }
                dataArray[i].timer = Time.time;
                //  Debug.Log($"あああ{dataArray[i].type}{dataArray[i].timer}");
                _effectData.Add(dataArray[i]);
            }


        }




        /// <summary>
        /// 状態の時間をはかる
        /// </summary>
        void ConditionTimer()
        {
            if ( _effectData.Any() )
            {
                for ( int i = 0; i < _effectData.Count; i++ )
                {

                    //時間超えたら消す
                    //または今無敵で状態わるいやつなら
                    if ( Time.time - _effectData[i].timer > _effectData[i].effectTime || _myStatus.invincible && _effectData[i].bad )
                    {
                        //   Debug.Log($"wwwdwd{_effectData[i].type}{Time.time - _effectData[i].timer > _effectData[i].effectTime}{_effectData[i].timer}");
                        ConditionEnd(_effectData[i]);
                        _effectData.Remove(_effectData[i]);
                    }

                }
            }

        }

        /// <summary>
        /// 特殊状態を終わらせる
        /// </summary>
        void ConditionEnd(EventData data)
        {

            if ( data.bad )
            {
                if ( data.type == EventType.badSight )
                {
                    BeautifySettings.settings.blurIntensity.value = 0f;
                }
            }
            else
            {
                if ( data.type == EventType.invincible )
                {
                    _myStatus.invincible = false;
                    SpriteEffect.SetActive(false);
                    gameObject.layer = 0;

                }
                else if ( data.type == EventType.boostJump )
                {
                    _myStatus.boostJump = false;

                }
            }


        }

        public async UniTask DieRecover()
        {

            if ( _effectData.Any() )
            {
                int count = _effectData.Count;
                for ( int i = 0; i < count; i++ )
                {
                    ConditionEnd(_effectData[i]);
                }
            }
            efCon.ConditionChange(CharacterCondition.none);
            await UniTask.Delay(TimeSpan.FromSeconds(1f));
            this.gameObject.layer = 0;
            _myStatus.Die = false;
        }


        #endregion

    }
}