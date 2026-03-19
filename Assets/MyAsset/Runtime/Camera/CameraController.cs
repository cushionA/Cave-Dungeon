using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CameraFollowLogicのMonoBehaviourラッパー。
    /// LateUpdateでプレイヤーを追従する。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _smoothTime = 0.15f;
        [SerializeField] private float _deadZoneRadius = 0.5f;
        [SerializeField] private Rect _bounds = new Rect(-100, -100, 200, 200);

        private CameraFollowLogic _logic;
        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 7f;
            transform.position = new Vector3(0, 0, -10f);

            _logic = new CameraFollowLogic(_smoothTime, _deadZoneRadius, _bounds);
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_target != null)
            {
                Vector2 snapped = _logic.SnapToTarget((Vector2)_target.position);
                transform.position = new Vector3(snapped.x, snapped.y, -10f);
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector2 currentPos = (Vector2)transform.position;
            Vector2 targetPos = (Vector2)_target.position;

            Vector2 newPos = _logic.CalculatePosition(currentPos, targetPos, Time.deltaTime);
            transform.position = new Vector3(newPos.x, newPos.y, -10f);
        }
    }
}
