using System;

namespace Game.Core
{
    /// <summary>
    /// 属性ゲートの判定ロジック（Pure Logic）。
    /// MonoBehaviourのElementalGateInteractorから呼ばれる。
    /// </summary>
    public class ElementalGateLogic
    {
        private readonly ElementalRequirement _requiredElement;
        private readonly float _minDamage;
        private readonly bool _multiHitRequired;
        private readonly int _requiredHitCount;

        private int _currentHitCount;
        private bool _isOpened;

        public bool IsOpened => _isOpened;
        public int CurrentHitCount => _currentHitCount;
        public int RequiredHitCount => _multiHitRequired ? _requiredHitCount : 1;

        public event Action OnGateOpened;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="requiredElement">必要な属性</param>
        /// <param name="minDamage">必要最低ダメージ（0なら属性攻撃で触れるだけでOK）</param>
        /// <param name="multiHitRequired">複数回ヒットが必要か</param>
        /// <param name="requiredHitCount">必要ヒット数（multiHitRequired時のみ有効）</param>
        public ElementalGateLogic(ElementalRequirement requiredElement, float minDamage,
            bool multiHitRequired, int requiredHitCount)
        {
            _requiredElement = requiredElement;
            _minDamage = minDamage;
            _multiHitRequired = multiHitRequired;
            _requiredHitCount = multiHitRequired ? requiredHitCount : 1;
            _currentHitCount = 0;
            _isOpened = false;
        }

        /// <summary>
        /// 属性攻撃がヒットした時の処理。
        /// 属性一致＋ダメージ閾値を満たしたらゲートを開放する。
        /// </summary>
        /// <returns>このヒットでゲートが開いたらtrue</returns>
        public bool OnElementalHit(Element attackElement, float damage)
        {
            if (_isOpened)
            {
                return false;
            }

            // 属性一致チェック
            if (!ElementalRequirementMapper.MatchesElement(_requiredElement, attackElement))
            {
                return false;
            }

            // ダメージ閾値チェック
            if (damage < _minDamage)
            {
                return false;
            }

            _currentHitCount++;

            if (_multiHitRequired && _currentHitCount < _requiredHitCount)
            {
                return false;
            }

            _isOpened = true;
            OnGateOpened?.Invoke();
            return true;
        }

        /// <summary>
        /// ヒットカウントをリセットする（マルチヒットの時間切れ等で使用）。
        /// </summary>
        public void ResetHitCount()
        {
            _currentHitCount = 0;
        }
    }
}
