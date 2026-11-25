using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    /// <summary>3Dの騎士に向かう明示ロック弾を、操作カメラの画面端に表示する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProjectileLockWarningHud3D : MonoBehaviour
    {
        [SerializeField] private CharacterActor3D character;
        [SerializeField, Min(0f)] private float projectileWarningRadius = ProjectileSpatialIndex.DefaultQueryRadius;

        private readonly List<ProjectileLockWarning> _warnings = new List<ProjectileLockWarning>(4);
        private UIDocument _document;
        private PlayerCharacterInput3D _input;
        private ProjectileWarningOverlay _overlay;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _input = character != null ? character.GetComponent<PlayerCharacterInput3D>() : null;
        }

        private void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null) { return; }

            _overlay = new ProjectileWarningOverlay();
            root.Add(_overlay);
        }

        private void OnDisable()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _warnings.Clear();
        }

        private void LateUpdate()
        {
            if (_overlay == null) { return; }

            ActorRef target = character != null && character.isActiveAndEnabled ? character.Actor : default;
            Camera camera = _input != null ? _input.GameplayCamera : Camera.main;
            ProjectileManager3D projectiles = GameManager.Projectiles3D;
            if (target.IsValid && camera != null && projectiles != null)
            {
                projectiles.CollectLockWarnings(target, _warnings, projectileWarningRadius);
            }
            else
            {
                _warnings.Clear();
            }

            _overlay.UpdateWarnings(camera, _warnings);
        }
    }
}
