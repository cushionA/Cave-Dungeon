using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Runtime
{
    public partial class HudController
    {
        private readonly List<ProjectileLockWarning> _projectileLockWarnings = new List<ProjectileLockWarning>(4);
        private ProjectileWarningOverlay _projectileWarningOverlay;

        private void InitializeProjectileWarnings(VisualElement root)
        {
            _projectileWarningOverlay = new ProjectileWarningOverlay();
            root.Add(_projectileWarningOverlay);
        }

        private void ClearProjectileWarnings()
        {
            _projectileWarningOverlay?.RemoveFromHierarchy();
            _projectileWarningOverlay = null;
            _projectileLockWarnings.Clear();
        }

        private void LateUpdate()
        {
            UpdateMultiLockMarkers();
            if (_projectileWarningOverlay == null) { return; }

            PlayerCharacter player = GameManager.Data?.GetManaged(CharacterRegistry.PlayerHash) as PlayerCharacter;
            Camera camera = null;
            if (player != null && player.isActiveAndEnabled)
            {
                PlayerInputHandler input = player.InputHandler;
                camera = input != null ? input.GameplayCamera : Camera.main;
            }
            ProjectileManager projectiles = GameManager.Projectiles;
            if (camera != null && projectiles != null && GameManager.Actors.TryGet(player.ObjectHash, out ActorRef target))
            {
                projectiles.CollectLockWarnings(target, _projectileLockWarnings);
            }
            else
            {
                _projectileLockWarnings.Clear();
            }

            _projectileWarningOverlay.UpdateWarnings(camera, _projectileLockWarnings);
        }
    }

    internal sealed class ProjectileWarningOverlay : VisualElement
    {
        private static readonly Color ArcColor = new Color32(247, 64, 61, 255);
        private static readonly Color ArcOutlineColor = new Color32(39, 10, 15, 230);

        private const float k_EdgeInset = 22f;
        private const float k_ArcRadius = 50f;
        private const float k_ArcHalfAngle = 20f;
        private const float k_ArcWidth = 5f;
        private const float k_ArcOutlineWidth = 9f;
        private const float k_CameraAxisTolerance = 0.000001f;

        private readonly List<Vector2> _directions = new List<Vector2>(4);
        private Rect _viewport;

        internal IReadOnlyList<Vector2> Directions => _directions;

        internal ProjectileWarningOverlay()
        {
            name = "projectile-lock-warnings";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += DrawWarnings;
        }

        internal void UpdateWarnings(Camera camera, List<ProjectileLockWarning> warnings)
        {
            int previousCount = _directions.Count;
            _directions.Clear();
            if (camera != null)
            {
                _viewport = camera.rect;
                Transform cameraTransform = camera.transform;
                foreach (ProjectileLockWarning warning in warnings)
                {
                    Vector2 direction = ProjectWarningDirection(cameraTransform, warning.FromTarget);
                    if (direction != Vector2.zero) { _directions.Add(direction); }
                }
            }
            if (previousCount > 0 || _directions.Count > 0) { MarkDirtyRepaint(); }
        }

        internal static Vector2 ProjectWarningDirection(Transform cameraTransform, Vector3 fromTarget)
        {
            Vector3 cameraBearing = cameraTransform.InverseTransformDirection(fromTarget);
            Vector2 direction = new Vector2(cameraBearing.x, -cameraBearing.y);
            if (direction.sqrMagnitude > k_CameraAxisTolerance * fromTarget.sqrMagnitude)
            {
                return direction.normalized;
            }

            // Directly towards or away from the view, keep the warning on the vertical edge.
            if (cameraBearing.z > 0f) { return Vector2.down; }
            return cameraBearing.z < 0f ? Vector2.up : Vector2.zero;
        }

        private void DrawWarnings(MeshGenerationContext context)
        {
            Rect bounds = contentRect;
            var viewport = new Rect(
                bounds.x + _viewport.x * bounds.width,
                bounds.y + (1f - _viewport.yMax) * bounds.height,
                _viewport.width * bounds.width,
                _viewport.height * bounds.height);
            Vector2 inset = viewport.size * 0.5f - Vector2.one * k_EdgeInset;
            if (inset.x <= 0f || inset.y <= 0f) { return; }

            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            foreach (Vector2 direction in _directions)
            {
                // Intersect the warning bearing with the inset viewport rectangle.
                float distanceToEdge = 1f / Mathf.Max(Mathf.Abs(direction.x) / inset.x, Mathf.Abs(direction.y) / inset.y);
                Vector2 arcCenter = viewport.center + direction * (distanceToEdge - k_ArcRadius);
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

                painter.BeginPath();
                painter.Arc(arcCenter, k_ArcRadius, angle - k_ArcHalfAngle, angle + k_ArcHalfAngle);
                painter.lineWidth = k_ArcOutlineWidth;
                painter.strokeColor = ArcOutlineColor;
                painter.Stroke();
                painter.lineWidth = k_ArcWidth;
                painter.strokeColor = ArcColor;
                painter.Stroke();
            }
        }
    }
}
