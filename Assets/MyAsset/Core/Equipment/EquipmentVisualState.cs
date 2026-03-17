namespace Game.Core
{
    /// <summary>
    /// 装備変更によるビジュアル状態の追跡。
    /// AnimatorController IDと盾スプライト IDの変更を検知する。
    /// 実際のAnimator/Sprite操作はMonoBehaviour側で行う。
    /// </summary>
    public class EquipmentVisualState
    {
        private int _currentWeaponAnimId;
        private int _currentShieldSpriteId;
        private bool _weaponAnimDirty;
        private bool _shieldSpriteDirty;

        public int CurrentWeaponAnimId => _currentWeaponAnimId;
        public int CurrentShieldSpriteId => _currentShieldSpriteId;
        public bool IsWeaponAnimDirty => _weaponAnimDirty;
        public bool IsShieldSpriteDirty => _shieldSpriteDirty;

        public EquipmentVisualState()
        {
            _currentWeaponAnimId = 0;
            _currentShieldSpriteId = 0;
            _weaponAnimDirty = false;
            _shieldSpriteDirty = false;
        }

        /// <summary>
        /// 武器アニメーション更新。IDが変わったらdirtyフラグを立てる。
        /// </summary>
        public void SetWeaponAnim(int animId)
        {
            if (_currentWeaponAnimId == animId)
            {
                return;
            }

            _currentWeaponAnimId = animId;
            _weaponAnimDirty = true;
        }

        /// <summary>
        /// 盾スプライト更新。IDが変わったらdirtyフラグを立てる。
        /// </summary>
        public void SetShieldSprite(int spriteId)
        {
            if (_currentShieldSpriteId == spriteId)
            {
                return;
            }

            _currentShieldSpriteId = spriteId;
            _shieldSpriteDirty = true;
        }

        /// <summary>
        /// dirtyフラグをクリアする（描画更新完了後に呼ぶ）。
        /// </summary>
        public void ClearDirtyFlags()
        {
            _weaponAnimDirty = false;
            _shieldSpriteDirty = false;
        }
    }
}
