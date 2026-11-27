using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>連携時の騎士の正面に、飛翔体だけが接触する時限の盾を置く。</summary>
    internal sealed class ProjectileReflectionShield3D : MonoBehaviour
    {
        internal const float ForwardOffset = 0.95f;
        private const int k_SpriteSize = 96;
        private static readonly List<ProjectileReflectionShield3D> s_Active = new();
        private static Texture2D s_Texture;
        private static Sprite s_Sprite;

        private CharacterActor3D _source;
        private CharacterActor3D _bearer;
        private ActorRef _sourceRef;
        private ActorRef _bearerRef;
        private PhysicsScene _world;
        private Vector3 _center;
        private Vector3 _normal;
        private float _radius;
        private double _expiresAt;

        internal static bool CanActivate(CharacterActor3D source, CharacterActor3D bearer, float duration, float radius)
            => TryGetPlacement(source, bearer, duration, radius, out _, out _, out _, out _, out _);

        internal static bool TryActivate(CharacterActor3D source, CharacterActor3D bearer, float duration, float radius)
        {
            if (!TryGetPlacement(source, bearer, duration, radius, out ActorRef sourceRef, out ActorRef bearerRef,
                out PhysicsScene world, out Vector3 center, out Vector3 normal)) { return false; }

            Scene scene = bearer.gameObject.scene;
            var instance = new GameObject("[Reflection Shield]");
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetPositionAndRotation(center, Quaternion.LookRotation(normal));
            instance.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            instance.AddComponent<SpriteRenderer>().sprite = ShieldSprite();

            var shield = instance.AddComponent<ProjectileReflectionShield3D>();
            shield._source = source;
            shield._bearer = bearer;
            shield._sourceRef = sourceRef;
            shield._bearerRef = bearerRef;
            shield._world = world;
            shield._center = center;
            shield._normal = normal;
            shield._radius = radius;
            shield._expiresAt = Time.timeAsDouble + duration;

            s_Active.Add(shield);
            for (int i = s_Active.Count - 2; i >= 0; i--)
            {
                if (s_Active[i]._source == source) { s_Active[i].Retire(); }
            }
            return true;
        }

        internal static bool TryFindContact(in AttackContext context, PhysicsScene physicsScene, Vector3 start,
            Vector3 direction, float distance, float radius, out float hitDistance, out Vector3 normal,
            out CharacterActor3D reflector, out ActorRef bearer)
        {
            hitDistance = distance;
            normal = default;
            reflector = null;
            bearer = default;
            for (int i = 0; i < s_Active.Count; i++)
            {
                ProjectileReflectionShield3D shield = s_Active[i];
                if (shield._world != physicsScene || !shield.IsLive(Time.timeAsDouble)
                    || !shield.TrySweep(start, direction, hitDistance, radius, out float contact)
                    || !shield.CanReflect(context)) { continue; }

                hitDistance = contact;
                normal = shield._normal;
                reflector = shield._source;
                bearer = shield._bearerRef;
            }
            return bearer.IsValid;
        }

        private bool TrySweep(Vector3 start, Vector3 direction, float distance, float bulletRadius, out float contact)
        {
            contact = 0f;
            Vector3 offset = start - _center;
            float depth = Vector3.Dot(offset, _normal);
            float approach = -Vector3.Dot(direction, _normal);
            if (approach <= 0f || depth < 0f) { return false; }

            contact = Mathf.Max(0f, (depth - bulletRadius) / approach);
            float end = Mathf.Min(distance, depth / approach);
            if (contact > end) { return false; }

            Vector3 inPlane = offset - _normal * depth;
            Vector3 planarDirection = direction + _normal * approach;
            Vector3 atContact = inPlane + planarDirection * contact;
            if (atContact.sqrMagnitude <= _radius * _radius) { return true; }

            // 縁をかすめる場合は、球の中心と円盤の最短距離が弾の半径に達する時点を探す。
            float planarSpeedSquared = planarDirection.sqrMagnitude;
            float nearestRadial = planarSpeedSquared > 0f
                ? Mathf.Clamp(-Vector3.Dot(inPlane, planarDirection) / planarSpeedSquared, contact, end) : contact;
            float reach = _radius + bulletRadius;
            if ((inPlane + planarDirection * nearestRadial).sqrMagnitude > reach * reach) { return false; }

            float radiusSquared = bulletRadius * bulletRadius;
            float low = nearestRadial;
            float high = end;
            // 円盤までの距離の二乗は凸なので、最小点の左側で最初の接触を絞り込める。
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * 0.5f;
                Vector3 lateral = inPlane + planarDirection * middle;
                float radial = lateral.magnitude;
                float slope = -(depth - approach * middle) * approach;
                if (radial > _radius) { slope += (1f - _radius / radial) * Vector3.Dot(lateral, planarDirection); }
                if (slope < 0f) { low = middle; }
                else { high = middle; }
            }

            float closest = (low + high) * 0.5f;
            if (DistanceSquared(closest) > radiusSquared) { return false; }
            if (DistanceSquared(contact) <= radiusSquared) { return true; }
            low = contact;
            high = closest;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * 0.5f;
                if (DistanceSquared(middle) > radiusSquared) { low = middle; }
                else { high = middle; }
            }
            contact = high;
            return true;

            float DistanceSquared(float along)
            {
                float height = depth - approach * along;
                float outside = Mathf.Max(0f, (inPlane + planarDirection * along).magnitude - _radius);
                return height * height + outside * outside;
            }
        }

        private bool CanReflect(in AttackContext context)
        {
            SoACharaDataDic data = GameManager.Data;
            CharacterBelong sourceBelong = data.GetFlags(_sourceRef.Hash).Belong;
            CharacterBelong bearerBelong = data.GetFlags(_bearerRef.Hash).Belong;
            return AttackTargetRules.CanAffect(context.Owner, context.Belong, _sourceRef, sourceBelong,
                    AttackTargetRelation.Hostile)
                && AttackTargetRules.CanAffect(context, _bearerRef, bearerBelong);
        }

        private static bool TryGetPlacement(CharacterActor3D source, CharacterActor3D bearer, float duration, float radius,
            out ActorRef sourceRef, out ActorRef bearerRef, out PhysicsScene world, out Vector3 center, out Vector3 normal)
        {
            sourceRef = default;
            bearerRef = default;
            world = default;
            center = default;
            normal = default;
            if (source == null || bearer == null || !source.IsAlive || !bearer.IsAlive
                || bearer.Body == null || !bearer.Body.enabled || !float.IsFinite(duration) || duration <= 0f
                || !float.IsFinite(radius) || !float.IsFinite(radius * radius) || radius <= 0f) { return false; }

            Scene scene = bearer.gameObject.scene;
            if (!scene.isLoaded) { return false; }
            world = scene.GetPhysicsScene();
            if (!world.IsValid() || source.gameObject.scene.GetPhysicsScene() != world) { return false; }

            sourceRef = source.Actor;
            bearerRef = bearer.Actor;
            normal = Vector3.ProjectOnPlane(bearer.transform.forward, Vector3.up).normalized;
            center = bearer.Body.bounds.center + normal * ForwardOffset;
            return normal != Vector3.zero && float.IsFinite(center.x) && float.IsFinite(center.y)
                && float.IsFinite(center.z);
        }

        private bool IsLive(double time)
            => time < _expiresAt && _source != null && _bearer != null
                && _source.Actor == _sourceRef && _bearer.Actor == _bearerRef && _source.IsAlive && _bearer.IsAlive
                && _source.gameObject.scene.GetPhysicsScene() == _world && _bearer.gameObject.scene.GetPhysicsScene() == _world;

        private void LateUpdate()
        {
            if (!IsLive(Time.timeAsDouble)) { Retire(); }
        }

        private void Retire()
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            s_Active.Remove(this);
            if (s_Active.Count == 0) { ClearSprite(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Active.Clear();
            ClearSprite();
        }

        private static void ClearSprite()
        {
            if (s_Sprite != null) { Destroy(s_Sprite); }
            if (s_Texture != null) { Destroy(s_Texture); }
            s_Sprite = null;
            s_Texture = null;
        }

        private static Sprite ShieldSprite()
        {
            if (s_Sprite != null) { return s_Sprite; }
            s_Texture = new Texture2D(k_SpriteSize, k_SpriteSize, TextureFormat.RGBA32, false)
            {
                name = "Reflection Shield",
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[k_SpriteSize * k_SpriteSize];
            const float radius = k_SpriteSize / 2f;
            for (int y = 0; y < k_SpriteSize; y++)
            {
                for (int x = 0; x < k_SpriteSize; x++)
                {
                    float dx = (x + 0.5f - radius) / radius;
                    float dy = (y + 0.5f - radius) / radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance > 1f) { continue; }
                    bool rim = distance >= 0.93f;
                    bool diamond = Mathf.Abs(Mathf.Abs(dx) + Mathf.Abs(dy) - 0.56f) < 0.028f;
                    pixels[y * k_SpriteSize + x] = rim || diamond
                        ? new Color32(151, 251, 255, 240) : new Color32(49, 157, 207, 88);
                }
            }
            s_Texture.SetPixels32(pixels);
            s_Texture.Apply(false, true);
            s_Sprite = Sprite.Create(s_Texture, new Rect(0f, 0f, k_SpriteSize, k_SpriteSize),
                new Vector2(0.5f, 0.5f), k_SpriteSize, 0, SpriteMeshType.FullRect);
            return s_Sprite;
        }
    }
}
