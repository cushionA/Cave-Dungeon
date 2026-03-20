using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// キャラクター（or 装備）ごとのアニメーションクリップ設定。
    /// 移動系の共通クリップ + Actionスロットへのクリップ割り当てを一元管理する。
    /// AnimatorBridge がこのProfileを受け取り、OverrideControllerを自動生成する。
    /// </summary>
    [CreateAssetMenu(fileName = "NewActionAnimProfile", menuName = "Game/ActionAnimationProfile")]
    public class ActionAnimationProfile : ScriptableObject
    {
        // ========== 移動系クリップ（持ち方で変わる） ==========

        [Header("移動系")]
        [Tooltip("待機モーション")]
        public AnimationClip idle;

        [Tooltip("走りモーション")]
        public AnimationClip run;

        [Tooltip("ダッシュモーション")]
        public AnimationClip dash;

        [Tooltip("ジャンプ（上昇中）モーション")]
        public AnimationClip jump;

        [Tooltip("落下モーション")]
        public AnimationClip fall;

        [Tooltip("着地モーション")]
        public AnimationClip landing;

        [Tooltip("ガード構えモーション")]
        public AnimationClip guard;

        // ========== Actionスロット ==========

        [Header("アクション")]
        [Tooltip("Action_N スロットへのクリップ割り当て")]
        public SlotEntry[] actionSlots;

        [Serializable]
        public struct SlotEntry
        {
            [Tooltip("人間用ラベル（斬り1段目、火球詠唱 等）")]
            public string label;

            [Tooltip("Action_N の N（0~7）")]
            [Range(0, 7)]
            public int slotIndex;

            [Tooltip("再生するアニメーションクリップ")]
            public AnimationClip clip;

            [Tooltip("詠唱・チャージ用ループフラグ")]
            public bool isLoop;
        }

        /// <summary>
        /// 移動系クリップを持っているか。
        /// 左装備ProfileはActionスロットのみ持ち、移動系はnull。
        /// </summary>
        public bool HasLocomotionClips => idle != null || run != null;
    }
}
