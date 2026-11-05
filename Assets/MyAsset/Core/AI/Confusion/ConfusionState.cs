using System;

namespace Game.Core
{

    /// <summary>
    /// 混乱状態の追跡データ。ConfusionEffectProcessorが内部管理に使用。
    /// </summary>
    public struct ConfusionState
    {
        public int targetHash;
        public int controllerHash;        // 混乱をかけたキャラのハッシュ
        public float remainingDuration;
        public CharacterBelong originalBelong;  // 復帰用に保存
        public float accumulatedDamage;    // 混乱中に受けたダメージ（解除閾値用）
    }
}
