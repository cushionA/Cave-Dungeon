using System;
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
        [Header("キャラクター識別")]
        public CharacterFeature feature;      // Player / Boss / NPC / Minion 等
        public CharacterBelong belong;        // 陣営（味方・敵・中立）
        public CharacterRank rank;            // 強さランク（AI脅威度評価に使用）
        public bool canFly;                   // 飛行可能か
        public int targetingLimit;            // 同時にターゲットされる上限数

        // ─────────────────────────────────────────────
        //  基礎ステータス
        // ─────────────────────────────────────────────
        [Header("基礎ステータス")]
        public int maxHp;
        public int maxMp;
        public float maxStamina;
        public float staminaRecoveryRate;     // スタミナ回復速度（/秒）
        public float staminaRecoveryDelay;    // 消費後の回復開始までの遅延（秒）

        // ─────────────────────────────────────────────
        //  属性攻撃力・防御力（7属性）
        // ─────────────────────────────────────────────
        [Header("属性攻撃力")]
        public ElementalStatus baseAttack;    // 7属性の基礎攻撃力

        [Header("属性防御力")]
        public ElementalStatus baseDefense;   // 7属性の基礎防御力

        // ─────────────────────────────────────────────
        //  弱点・攻撃属性
        // ─────────────────────────────────────────────
        [Header("弱点属性")]
        public Element weakPoint;             // 弱点属性
        public Element attackElement;         // メイン攻撃属性

        // ─────────────────────────────────────────────
        //  アクション設定
        // ─────────────────────────────────────────────
        [Header("アクション設定")]
        public float moveSpeed;
        public float walkSpeed;
        public float dashSpeed;
        public float jumpHeight;

        // ─────────────────────────────────────────────
        //  耐性
        // ─────────────────────────────────────────────
        [Header("耐性")]
        public float statusResistance;            // 基礎状態異常耐性（蓄積閾値のグローバル倍率）
        public PhysicalResistance physicalResistance; // 物理タイプ別耐性（斬撃/打撃/刺突）

        // ─────────────────────────────────────────────
        //  初期状態
        // ─────────────────────────────────────────────
        [Header("初期状態")]
        public ActState initialActState;      // スポーン時の初期行動状態
    }
}
