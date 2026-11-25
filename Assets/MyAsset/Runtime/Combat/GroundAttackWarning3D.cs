using UnityEngine;

namespace Game.Runtime
{
    /// <summary>床上の表示を追従させ、シーン退場時にも描画リソースを解放する。</summary>
    internal sealed class GroundAttackWarning3D : MonoBehaviour
    {
        private const float k_SurfaceOffset = 0.012f;
        private GroundAttack3D _area;
        private Sprite _sprite;
        private Texture2D _texture;

        internal void Initialize(GroundAttack3D area, Sprite sprite, Texture2D texture)
        {
            _area = area;
            _sprite = sprite;
            _texture = texture;
            UpdatePose();
        }

        private void LateUpdate() => UpdatePose();

        private void UpdatePose()
        {
            if (!_area.IsValid)
            {
                AttackWarningPresentation.HideGroundImpact(gameObject);
                return;
            }
            Quaternion rotation = _area.Rotation;
            transform.SetPositionAndRotation(_area.Center + rotation * Vector3.forward * k_SurfaceOffset, rotation);
        }

        private void OnDestroy()
        {
            if (_sprite != null) { Destroy(_sprite); }
            if (_texture != null) { Destroy(_texture); }
        }
    }
}
