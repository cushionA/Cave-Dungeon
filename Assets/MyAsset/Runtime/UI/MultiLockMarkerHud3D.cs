using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MultiLockMarkerHud3D : MonoBehaviour
    {
        [SerializeField] private CharacterActor3D character;

        private readonly VisualElement[] _markers = new VisualElement[ActionTargetSelection.MaxTargets];
        private UIDocument _document;
        private VisualElement _root;
        private PlayerCharacterInput3D _input;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            if (character != null) { _input = character.GetComponent<PlayerCharacterInput3D>(); }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _markers.Length; i++)
            {
                _markers[i]?.RemoveFromHierarchy();
                _markers[i] = null;
            }
            _root = null;
        }

        private void LateUpdate()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null || root.panel == null) { return; }
            if (_root != root)
            {
                _root = root;
                for (int i = 0; i < _markers.Length; i++)
                {
                    _markers[i]?.RemoveFromHierarchy();
                    _markers[i] = HudController.CreateMultiLockMarker();
                    _root.Add(_markers[i]);
                }
            }

            Camera camera = _input != null ? _input.GameplayCamera : null;
            IReadOnlyList<ActorRef> targets = _input != null ? _input.MultiLockTargets : null;
            for (int i = 0; i < _markers.Length; i++)
            {
                VisualElement marker = _markers[i];
                if (camera == null || targets == null || i >= targets.Count
                    || !PlayerCharacterInput3D.TryGetMarkPosition(targets[i], out Vector3 world)
                    || !PlayerInputHandler.TryProjectMark(camera, world, out Vector2 screen))
                {
                    marker.style.display = DisplayStyle.None;
                    continue;
                }
                HudController.PlaceMultiLockMarker(_root, marker, screen);
            }
        }
    }
}
