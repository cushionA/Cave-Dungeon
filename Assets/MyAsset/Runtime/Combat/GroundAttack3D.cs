using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>予備動作から着弾まで、実際に支えていた床と接触面を保持する。</summary>
    internal readonly struct GroundAttack3D
    {
        private const float k_PlaneTolerance = 0.08f;
        private const float k_MinimumNormalAlignment = 0.9f;
        private const int k_WarningResolution = 64;
        private const int k_WarningSortingOrder = 5;

        private readonly Collider _surface;
        private readonly PhysicsScene _world;
        private readonly Vector3 _localCenter;
        private readonly Quaternion _localRotation;
        private readonly float _radius;

        private GroundAttack3D(Collider surface, PhysicsScene world, Vector3 point, Vector3 normal, float radius)
        {
            Transform frame = surface.transform;
            _surface = surface;
            _world = world;
            _localCenter = frame.InverseTransformPoint(point);
            Vector3 up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (up.sqrMagnitude < 0.001f) { up = Vector3.ProjectOnPlane(Vector3.right, normal); }
            _localRotation = Quaternion.Inverse(frame.rotation) * Quaternion.LookRotation(normal, up);
            _radius = radius;
        }

        public Collider Surface => _surface;
        public Vector3 Center => _surface != null ? _surface.transform.TransformPoint(_localCenter) : default;
        public Vector3 Normal => Rotation * Vector3.forward;
        internal Quaternion Rotation => _surface != null ? _surface.transform.rotation * _localRotation : Quaternion.identity;
        internal bool IsValid => _surface != null && _surface.enabled && _surface.gameObject.activeInHierarchy
            && _world.IsValid() && _surface.gameObject.scene.GetPhysicsScene() == _world;

        public static bool TryCreate(CharacterActor3D caster, float radius, out GroundAttack3D floor)
        {
            floor = default;
            if (caster == null || caster.Motor == null || !caster.IsAlive || !(radius > 0f) || float.IsInfinity(radius))
            {
                return false;
            }
            CharacterMotor3D.GroundContact ground = caster.Motor.Ground;
            PhysicsScene world = caster.gameObject.scene.GetPhysicsScene();
            if (!ground.IsGrounded || ground.Normal.sqrMagnitude < 0.01f
                || ground.Surface.gameObject.scene.GetPhysicsScene() != world)
            {
                return false;
            }
            floor = new GroundAttack3D(ground.Surface, world, ground.Point, ground.Normal.normalized, radius);
            return floor.IsValid;
        }

        public static bool TryCreateAhead(CharacterActor3D caster, float aimRange, float radius, out GroundAttack3D impact)
        {
            impact = default;
            if (!(aimRange > 0f) || float.IsInfinity(aimRange) || !TryCreate(caster, radius, out GroundAttack3D floor))
            {
                return false;
            }

            Vector3 normal = floor.Normal;
            Vector3 forward = Vector3.ProjectOnPlane(caster.Motor.Facing, normal).normalized;
            if (forward.sqrMagnitude == 0f) { return false; }
            Ray ray = ProbeRay(floor.Center + forward * aimRange, normal);
            if (!TryProbeSurface(floor.Surface, ray, normal, out RaycastHit hit))
            {
                // 照準面の近くで高さと傾きが合う床だけを候補にする。
                int layer = 1 << floor.Surface.gameObject.layer;
                if (!floor._world.Raycast(ray.origin, ray.direction, out hit, 2f * k_PlaneTolerance,
                    layer, QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal, normal) <= k_MinimumNormalAlignment)
                {
                    return false;
                }
            }

            impact = new GroundAttack3D(hit.collider, floor._world, hit.point, hit.normal, radius);
            return impact.IsValid;
        }

        public bool BelongsTo(PhysicsScene world) => _world == world && IsValid;

        public bool Contains(CharacterActor3D target)
        {
            if (target == null || !target.IsAlive || target.Motor == null
                || !BelongsTo(target.gameObject.scene.GetPhysicsScene()))
            {
                return false;
            }

            CharacterMotor3D.GroundContact ground = target.Motor.Ground;
            return ground.Surface == _surface && ContainsPoint(ground.Point, ground.Normal);
        }

        internal bool IsLandingContact(in RaycastHit hit) =>
            IsValid && hit.collider == _surface && ContainsPoint(hit.point, hit.normal);

        private bool ContainsPoint(Vector3 point, Vector3 surfaceNormal)
        {
            Vector3 delta = point - Center;
            Vector3 normal = Normal;
            float height = Vector3.Dot(delta, normal);
            return Mathf.Abs(height) <= k_PlaneTolerance
                && Vector3.Dot(surfaceNormal, normal) > k_MinimumNormalAlignment
                && (delta - normal * height).sqrMagnitude <= _radius * _radius;
        }

        public GameObject ShowWarning()
        {
            if (!IsValid) { return null; }

            Texture2D texture = CreateWarningTexture();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, k_WarningResolution, k_WarningResolution),
                new Vector2(0.5f, 0.5f), k_WarningResolution, 0, SpriteMeshType.FullRect);
            GameObject warning = new GameObject("[Ground Impact Warning 3D]");
            SceneManager.MoveGameObjectToScene(warning, _surface.gameObject.scene);
            warning.transform.localScale = Vector3.one * (2f * _radius);
            SpriteRenderer renderer = warning.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = k_WarningSortingOrder;
            warning.AddComponent<GroundAttackWarning3D>().Initialize(this, sprite, texture);
            return warning;
        }

        private static Ray ProbeRay(Vector3 point, Vector3 normal) =>
            new Ray(point + normal * k_PlaneTolerance, -normal);

        private static bool TryProbeSurface(Collider surface, Ray ray, Vector3 normal, out RaycastHit hit) =>
            surface.Raycast(ray, out hit, 2f * k_PlaneTolerance) && Vector3.Dot(hit.normal, normal) > k_MinimumNormalAlignment;

        private Texture2D CreateWarningTexture()
        {
            int resolution = k_WarningResolution;
            bool[] supportedPixels = new bool[resolution * resolution];
            Vector3 center = Center;
            Quaternion rotation = Rotation;
            Vector3 normal = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            for (int y = 0; y < resolution; y++)
            {
                float v = (y + 0.5f) * (2f / resolution) - 1f;
                for (int x = 0; x < resolution; x++)
                {
                    float u = (x + 0.5f) * (2f / resolution) - 1f;
                    if (u * u + v * v > 1f) { continue; }

                    Vector3 point = center + (right * u + up * v) * _radius;
                    supportedPixels[y * resolution + x] = TryProbeSurface(_surface, ProbeRay(point, normal), normal, out _);
                }
            }

            Color32[] pixels = new Color32[supportedPixels.Length];
            Color32 fill = new Color32(255, 37, 34, 55);
            Color32 outline = new Color32(255, 37, 34, 215);
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = y * resolution + x;
                    if (!supportedPixels[index]) { continue; }
                    bool hasOutline = x == 0 || x == resolution - 1 || y == 0 || y == resolution - 1
                        || !supportedPixels[index - 1] || !supportedPixels[index + 1]
                        || !supportedPixels[index - resolution] || !supportedPixels[index + resolution];
                    pixels[index] = hasOutline ? outline : fill;
                }
            }

            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Ground Impact Warning 3D",
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
