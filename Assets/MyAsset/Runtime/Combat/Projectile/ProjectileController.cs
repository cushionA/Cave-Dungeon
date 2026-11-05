using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>Coreの位置とUnity表示・接触を接続する。寿命とプール所有権はManagerが管理する。</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class ProjectileController : MonoBehaviour
    {
        private Projectile _coreProjectile;
        private MagicDefinition _magic;
        private CircleCollider2D _triggerCollider;
        private SpriteRenderer _spriteRenderer;
        private Rigidbody2D _rb;
        private Dictionary<ActorRef, int> _hitCounts;
        private HashSet<ActorRef> _sweptTargets;
        private List<RaycastHit2D> _sweepHits;
        private ChildBulletConfig _childSnapshot;
        private bool _componentsInitialized;
        private bool _isActive;
        private bool _hasSimulated;
        private ulong _leaseId;
        private ProjectileManager _manager;
        private Vector3 _baseLocalScale;
        private bool _localScaleModified;

        public Projectile CoreProjectile => _coreProjectile;
        /// <summary>照会用の独立コピー。可変の子弾設定・付与配列を内部へ露出しない。</summary>
        public MagicDefinition Magic => ChildBulletHelper.DeepCopyForInspection(_magic);
        /// <summary>生成・通知境界で保持する値snapshot。定常照会は必要項目だけを読む。</summary>
        internal MagicDefinition CapturedMagic => _magic;
        public bool IsActive => _isActive;
        internal ulong LeaseId => _leaseId;
        internal int ActiveIndex { get; set; } = -1;
        internal GameObject PoolPrefab { get; private set; }

        internal bool HasChildTrigger(ChildBulletTrigger trigger) =>
            ChildBulletHelper.HasChildBullet(_magic) && _magic.childBullet.trigger == trigger;

        // This gate dispatches no callbacks and never lends the controller's mutable field to callers.
        internal bool CanAffectTarget(Projectile projectile, ActorRef target) =>
            ProjectileHitProcessor.CanAffect(projectile, in _magic, GameManager.Data, GameManager.Actors, target);

        private void Awake() => EnsureComponents();

        // Cold pool creation and EditMode tests may run before Unity invokes Awake.
        private void EnsureComponents()
        {
            if (_componentsInitialized) { return; }
            _triggerCollider = GetComponent<CircleCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;
            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            _hitCounts = new Dictionary<ActorRef, int>();
            _sweptTargets = new HashSet<ActorRef>();
            _sweepHits = new List<RaycastHit2D>(16);
            _baseLocalScale = transform.localScale;
            _componentsInitialized = true;
        }

        internal void PrepareForPool(GameObject prefab)
        {
            PoolPrefab = prefab;
            EnsureComponents();
        }

        /// <summary>状態を先に設定し、OnEnableが完全な個体を観測できる順序で有効化する。</summary>
        public void Activate(Projectile projectile, MagicDefinition magic, ProjectileManager manager)
        {
            EnsureComponents();
            if (magic.childBullet != null)
            {
                if (_childSnapshot == null) { _childSnapshot = new ChildBulletConfig(); }
                _childSnapshot.trigger = magic.childBullet.trigger;
                _childSnapshot.profile = magic.childBullet.profile;
                _childSnapshot.count = magic.childBullet.count;
                _childSnapshot.spreadAngle = magic.childBullet.spreadAngle;
                magic.childBullet = _childSnapshot;
            }
            _coreProjectile = projectile;
            _leaseId = projectile.LeaseId;
            _magic = magic;
            _manager = manager;
            _isActive = true;
            _hasSimulated = false;
            _hitCounts.Clear();
            _sweptTargets.Clear();
            _sweepHits.Clear();
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _triggerCollider.enabled = !projectile.IsSpawnDelayed;
            SyncTransform();
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            _isActive = false;
            ActiveIndex = -1;
            _triggerCollider.enabled = false;
            _coreProjectile = null;
            _magic = default;
            _manager = null;
            _leaseId = 0;
            _hasSimulated = false;
            _hitCounts.Clear();
            _sweptTargets.Clear();
            _sweepHits.Clear();
            if (_localScaleModified) { transform.localScale = _baseLocalScale; }
            _localScaleModified = false;
            if (_spriteRenderer != null) { _spriteRenderer.enabled = true; }
            // OnDisable may clear the world or spawn another projectile. Ownership is already detached.
            gameObject.SetActive(false);
        }

        internal bool Matches(Projectile projectile, ulong leaseId) => this != null && _isActive
            && _coreProjectile == projectile && _leaseId == leaseId && projectile.LeaseId == leaseId;

        internal void StopCollision()
        {
            if (_triggerCollider != null) { _triggerCollider.enabled = false; }
        }

        public void SyncTransform()
        {
            if (_coreProjectile == null) { return; }
            Vector2 position = _coreProjectile.Position;
            _rb.position = position;
            transform.position = new Vector3(position.x, position.y, 0f);
            Vector2 velocity = _coreProjectile.Velocity;
            if (velocity.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                _rb.rotation = angle;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
            else if (!_hasSimulated)
            {
                _rb.rotation = 0f;
                transform.rotation = Quaternion.identity;
            }
            if (_coreProjectile.CapturedProfile.scaleTime > 0f)
            {
                float scale = _coreProjectile.GetCurrentScale();
                transform.localScale = new Vector3(_baseLocalScale.x * scale, _baseLocalScale.y * scale, _baseLocalScale.z);
                _localScaleModified = true;
            }
            bool visible = !_coreProjectile.IsSpawnDelayed;
            _triggerCollider.enabled = visible && _coreProjectile.IsAlive;
            if (_spriteRenderer != null) { _spriteRenderer.enabled = visible; }
        }

        private float WorldRadius()
        {
            Vector3 scale = transform.lossyScale;
            return _triggerCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        /// <summary>旧位置から新位置までを円で走査し、薄い敵を跨ぐ高速弾も接触順に処理する。</summary>
        internal void SweepToCorePosition(Projectile projectile, ulong leaseId)
        {
            if (!Matches(projectile, leaseId)) { return; }
            _sweptTargets.Clear();
            Vector2 startPosition = transform.position;
            Vector2 startCenter = transform.TransformPoint(_triggerCollider.offset);
            float radius = WorldRadius();
            Vector2 destination = projectile.Position;
            SyncTransform();
            if (projectile.IsSpawnDelayed || !projectile.IsAlive) { return; }
            Vector2 endCenter = transform.TransformPoint(_triggerCollider.offset);
            radius = Mathf.Max(radius, WorldRadius());
            Vector2 displacement = endCenter - startCenter;
            float distance = displacement.magnitude;
            if (distance > 0.000001f && radius > 0f)
            {
                ContactFilter2D filter = new ContactFilter2D();
                filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
                filter.useTriggers = true;
                _sweepHits.Clear();
                Physics2D.CircleCast(startCenter, radius, displacement / distance, filter, _sweepHits, distance);
                for (int i = 0; i < _sweepHits.Count; i++)
                {
                    if (!Matches(projectile, leaseId) || !projectile.IsAlive) { break; }
                    RaycastHit2D hit = _sweepHits[i];
                    // Subsequent casts begin inside an existing contact. OnTriggerEnter owns re-entry;
                    // treating that overlap as a fresh hit every tick would turn Stay into multihit.
                    if (_hasSimulated && hit.distance <= 0f) { continue; }
                    if (!TryResolveTarget(hit.collider, out ActorRef target) || _sweptTargets.Contains(target)) { continue; }
                    Vector2 impact = Vector2.Lerp(startPosition, destination, hit.fraction);
                    projectile.Position = impact;
                    bool accepted = ProcessTarget(target, projectile, leaseId);
                    if (!Matches(projectile, leaseId)) { return; }
                    if (accepted) { _sweptTargets.Add(target); }
                    if (projectile.IsAlive) { projectile.Position = destination; }
                }
            }
            if (!Matches(projectile, leaseId)) { return; }
            _hasSimulated = true;
            SyncTransform();
        }

        private static bool TryResolveTarget(Collider2D other, out ActorRef target)
        {
            target = default;
            if (other == null || GameManager.Actors == null) { return false; }
            if (GameManager.Actors.TryGet(other.gameObject.GetHashCode(), out target)) { return true; }
            Rigidbody2D body = other.attachedRigidbody;
            return body != null && GameManager.Actors.TryGet(body.gameObject.GetHashCode(), out target);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Projectile projectile = _coreProjectile;
            ulong leaseId = _leaseId;
            if (!_isActive || projectile == null || projectile.IsSpawnDelayed || !projectile.IsAlive
                || !TryResolveTarget(other, out ActorRef target) || _sweptTargets.Contains(target)) { return; }
            ProcessTarget(target, projectile, leaseId);
        }

        private bool ProcessTarget(ActorRef target, Projectile projectile, ulong leaseId)
        {
            if (!Matches(projectile, leaseId) || !projectile.IsAlive
                || !CanAffectTarget(projectile, target)) { return false; }
            IDamageable receiver = GameManager.Data.GetManaged(target.Hash)?.Damageable;
            if (receiver == null || !TryRegisterHit(target, out bool shouldDespawn)) { return false; }
            MagicDefinition magic = _magic;
            ProjectileManager manager = _manager;
            ProjectileHitProcessor.ProcessHit(projectile, receiver,
                GameManager.Data, magic, GameManager.Events, GameManager.StatusEffects, GameManager.Actors, target);
            // ReceiveDamage dispatches synchronous events and can clear/reuse both pools.
            if (!Matches(projectile, leaseId) || manager == null || !manager.IsCurrent(this, projectile, leaseId)) { return true; }
            if (BulletFeatureProcessor.ShouldExplode(projectile.CapturedProfile.features)) { manager.ProcessExplosion(projectile, magic); }
            if (!Matches(projectile, leaseId) || !manager.IsCurrent(this, projectile, leaseId)) { return true; }
            if (ChildBulletHelper.HasChildBullet(magic) && magic.childBullet.trigger == ChildBulletTrigger.OnHit)
            {
                manager.SpawnChildProjectiles(projectile, magic);
            }
            if (!Matches(projectile, leaseId) || !manager.IsCurrent(this, projectile, leaseId)) { return true; }
            if (shouldDespawn && projectile.IsAlive) { projectile.Kill(); }
            if (!projectile.IsAlive) { manager.ReturnProjectile(this); }
            return true;
        }

        internal bool TryRegisterHit(int targetHash, out bool shouldDespawn)
            => TryRegisterHit(new ActorRef(targetHash, 0), out shouldDespawn);

        internal bool TryRegisterHit(ActorRef target, out bool shouldDespawn)
        {
            shouldDespawn = false;
            if (_coreProjectile == null || !_coreProjectile.IsAlive) { return false; }
            int perTargetLimit = _coreProjectile.CapturedProfile.GetEffectivePerTargetHitLimit();
            _hitCounts.TryGetValue(target, out int current);
            if (current >= perTargetLimit) { return false; }
            _hitCounts[target] = current + 1;
            _coreProjectile.RegisterHit();
            shouldDespawn = !_coreProjectile.IsAlive;
            return true;
        }

        internal int GetHitCountForTarget(int targetHash)
        {
            if (_hitCounts == null) { return 0; }
            int count = 0;
            foreach (KeyValuePair<ActorRef, int> pair in _hitCounts)
            {
                if (pair.Key.Hash == targetHash) { count += pair.Value; }
            }
            return count;
        }
    }
}
