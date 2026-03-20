using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// キャラクターの「何者であるか」と「何ができるか」を定義するScriptableObject。
    /// ボス・雑魚・NPC・プレイヤーなど、すべてのキャラクターがこのクラスで定義される。
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterInfo", menuName = "Game/CharacterInfo")]
    public class CharacterInfo : ScriptableObject
    {
        // ─────────────────────────────────────────────
        //  キャラクター識別
        // ─────────────────────────────────────────────
        [TitleGroup("キャラクター識別")]
        [EnumToggleButtons]
        public CharacterFeature feature;

        [TitleGroup("キャラクター識別")]
        [EnumToggleButtons]
        public CharacterBelong belong;

        [TitleGroup("キャラクター識別")]
        public CharacterRank rank;

        [TitleGroup("キャラクター識別")]
        public bool canFly;

        [TitleGroup("キャラクター識別")]
        [Tooltip("同時にターゲットされる上限数")]
        [MinValue(0)]
        public int targetingLimit;

        // ─────────────────────────────────────────────
        //  基礎ステータス
        // ─────────────────────────────────────────────
        [TitleGroup("基礎ステータス")]
        [MinValue(1)]
        public int maxHp;

        [TitleGroup("基礎ステータス")]
        [MinValue(0)]
        public int maxMp;

        [TitleGroup("基礎ステータス")]
        [MinValue(0)]
        public float maxStamina;

        [TitleGroup("基礎ステータス")]
        [Tooltip("スタミナ回復速度（/秒）")]
        [MinValue(0)]
        public float staminaRecoveryRate;

        [TitleGroup("基礎ステータス")]
        [Tooltip("消費後の回復開始までの遅延（秒）")]
        [MinValue(0)]
        public float staminaRecoveryDelay;

        // ─────────────────────────────────────────────
        //  属性攻撃力・防御力（7属性）
        // ─────────────────────────────────────────────
        [TitleGroup("属性攻撃力")]
        public ElementalStatus baseAttack;

        [TitleGroup("属性防御力")]
        public ElementalStatus baseDefense;

        // ─────────────────────────────────────────────
        //  弱点・攻撃属性
        // ─────────────────────────────────────────────
        [TitleGroup("弱点・属性")]
        [EnumToggleButtons]
        public Element weakPoint;

        [TitleGroup("弱点・属性")]
        [EnumToggleButtons]
        public Element attackElement;

        // ─────────────────────────────────────────────
        //  アクション設定
        // ─────────────────────────────────────────────
        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float moveSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float walkSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float dashSpeed;

        [TitleGroup("アクション設定")]
        [MinValue(0)]
        public float jumpHeight;

        // ─────────────────────────────────────────────
        //  耐性
        // ─────────────────────────────────────────────
        [TitleGroup("耐性")]
        [Tooltip("基礎状態異常耐性（蓄積閾値のグローバル倍率）")]
        [Range(0f, 1f)]
        public float statusResistance;

        [TitleGroup("耐性")]
        public PhysicalResistance physicalResistance;

        // ─────────────────────────────────────────────
        //  初期状態
        // ─────────────────────────────────────────────
        [TitleGroup("初期状態")]
        public ActState initialActState;
    }
}
