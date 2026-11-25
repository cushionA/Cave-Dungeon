using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    public partial class HudController
    {
        private const float LockMarkerSize = 24f;
        private static readonly Color LockMarkerColor = new Color(1f, 0.84f, 0.4f);
        private readonly VisualElement[] _lockMarkers = new VisualElement[MultiLockTargeting.MaxTargets];
        private VisualElement _lockMarkerRoot;

        private void InitializeMultiLockMarkers(VisualElement root)
        {
            _lockMarkerRoot = root;
            for (int i = 0; i < _lockMarkers.Length; i++)
            {
                VisualElement marker = CreateMultiLockMarker();
                _lockMarkers[i] = marker;
                root.Add(marker);
            }
        }

        private void ClearMultiLockMarkers()
        {
            for (int i = 0; i < _lockMarkers.Length; i++)
            {
                _lockMarkers[i]?.RemoveFromHierarchy();
                _lockMarkers[i] = null;
            }
            _lockMarkerRoot = null;
        }

        private void UpdateMultiLockMarkers()
        {
            if (_lockMarkerRoot == null || _lockMarkerRoot.panel == null) { return; }

            PlayerCharacter player = GameManager.Data?.GetManaged(CharacterRegistry.PlayerHash) as PlayerCharacter;
            PlayerInputHandler input = player != null ? player.InputHandler : null;
            IReadOnlyList<ActorRef> targets = input != null ? input.MultiLockTargets : null;
            Camera camera = input != null ? input.GameplayCamera : null;

            for (int i = 0; i < _lockMarkers.Length; i++)
            {
                VisualElement marker = _lockMarkers[i];
                if (camera == null || targets == null || i >= targets.Count
                    || !PlayerInputHandler.TryGetMarkPosition(targets[i], out Vector3 world)
                    || !PlayerInputHandler.TryProjectMark(camera, world, out Vector2 screen))
                {
                    marker.style.display = DisplayStyle.None;
                    continue;
                }

                PlaceMultiLockMarker(_lockMarkerRoot, marker, screen);
            }
        }

        internal static VisualElement CreateMultiLockMarker()
        {
            VisualElement marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.display = DisplayStyle.None;
            marker.style.width = LockMarkerSize;
            marker.style.height = LockMarkerSize;
            marker.style.borderTopWidth = marker.style.borderBottomWidth = 2f;
            marker.style.borderLeftWidth = marker.style.borderRightWidth = 2f;
            marker.style.borderTopColor = marker.style.borderBottomColor = LockMarkerColor;
            marker.style.borderLeftColor = marker.style.borderRightColor = LockMarkerColor;
            marker.style.borderTopLeftRadius = marker.style.borderTopRightRadius = LockMarkerSize / 2f;
            marker.style.borderBottomLeftRadius = marker.style.borderBottomRightRadius = LockMarkerSize / 2f;
            return marker;
        }

        internal static void PlaceMultiLockMarker(VisualElement root, VisualElement marker, Vector2 screen)
        {
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            Vector2 position = root.WorldToLocal(panel);
            marker.style.left = position.x - LockMarkerSize / 2f;
            marker.style.top = position.y - LockMarkerSize / 2f;
            marker.style.display = DisplayStyle.Flex;
        }
    }
}
