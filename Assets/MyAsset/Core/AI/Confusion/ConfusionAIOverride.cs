using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 混乱中の敵AIのターゲット選択を反転する純ロジッククラス。
    /// Belong（陣営）を反転させることで、AIBrainのコードを変更せずに
    /// 混乱状態を実現する。
    /// </summary>
    public class ConfusionAIOverride
    {
        private readonly HashSet<int> _overriddenTargets;

        public ConfusionAIOverride()
        {
            _overriddenTargets = new HashSet<int>();
        }

        /// <summary>
        /// 指定キャラのAIターゲットフィルタ反転を有効化する。
        /// </summary>
        public void ActivateOverride(int targetHash)
        {
            _overriddenTargets.Add(targetHash);
        }

        /// <summary>
        /// 指定キャラのAIターゲットフィルタ反転を無効化する。
        /// </summary>
        public void DeactivateOverride(int targetHash)
        {
            _overriddenTargets.Remove(targetHash);
        }

        /// <summary>
        /// 指定キャラにオーバーライドが有効か判定する。
        /// </summary>
        public bool IsOverrideActive(int targetHash)
        {
            return _overriddenTargets.Contains(targetHash);
        }

        /// <summary>
        /// オーバーライド対象ならBelongを反転して返す。
        /// 非対象ならそのまま返す。
        /// </summary>
        public CharacterBelong GetOverriddenBelong(int targetHash, CharacterBelong original)
        {
            if (!_overriddenTargets.Contains(targetHash))
            {
                return original;
            }

            // Ally ↔ Enemy 反転
            if ((original & CharacterBelong.Ally) != 0)
            {
                return CharacterBelong.Enemy;
            }
            if ((original & CharacterBelong.Enemy) != 0)
            {
                return CharacterBelong.Ally;
            }

            // Neutralはそのまま
            return original;
        }

        /// <summary>
        /// 全オーバーライドをクリアする。
        /// </summary>
        public void ClearAll()
        {
            _overriddenTargets.Clear();
        }
    }
}
