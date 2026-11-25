using UnityEngine;

namespace Game.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow3D : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        private Vector3 _offset;

        private void Awake()
        {
            if (_target != null) { _offset = transform.position - _target.position; }
        }

        private void LateUpdate()
        {
            if (_target != null) { transform.position = _target.position + _offset; }
        }
    }
}
