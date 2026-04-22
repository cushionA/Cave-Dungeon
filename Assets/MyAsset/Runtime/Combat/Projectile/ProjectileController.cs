using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 飛翔体GameObjectのMonoBehaviour橋渡し。
    /// Core Projectileの位置をTransformに同期し、OnTriggerEnter2Dで衝突検知する。
    /// Update駆動はProjectileManagerが一括管理する（自身のUpdate不使用）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class ProjectileController : MonoBehaviour
    {
        private Projectile _coreProjectile;
        private MagicDefinition _magic;
        private CircleCollider2D _triggerCollider;
        private SpriteRenderer _spriteRenderer;
        private Rigidbody2D _rb;
        private HashSet<int> _hitTargets;
        private bool _isActive;
        private ProjectileManager _manager;

        public Projectile CoreProjectile => _coreProjectile;
        public MagicDefinition Magic => _magic;
        public bool IsActive => _isActive;

        private void Awake()
        {
            _triggerCollider = GetComponent<CircleCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;

            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _hitTargets = new HashSet<int>();
        }

        /// <summary>
        /// 飛翔体を有効化する。CoreデータとMagicDefinitionを紐付ける。
        /// </summary>
        public void Activate(Projectile projectile, MagicDefinition magic, ProjectileManager manager)
        {
            _coreProjectile = projectile;
            _magic = magic;
            _manager = manager;
            _isActive = true;
            _hitTargets.Clear();
            _triggerCollider.enabled = true;

            transform.position = new Vector3(projectile.Position.x, projectile.Position.y, 0f);
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 飛翔体を無効化してプール返却可能にする。
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _triggerCollider.enabled = false;
            _coreProjectile = null;
            _manager = null;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// CoreのPositionをTransformに同期し、速度方向にスプライトを回転する。
        /// ProjectileManagerから毎フレーム呼ばれる。
        /// </summary>
        public void SyncTransform()
        {
            if (_coreProjectile == null)
            {
                return;
            }

            Vector2 pos = _coreProjectile.Position;
            transform.position = new Vector3(pos.x, pos.y, 0f);

            Vector2 vel = _coreProjectile.Velocity;
            if (vel.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isActive)
            {
                return;
            }

            // アーキテクチャ準拠: 毎衝突でのGetComponentを避け、GameObject.GetHashCode から
            // SoA逆引き(GameManager.Data.GetManaged)で IDamageable を取得する。
            int targetHash = other.gameObject.GetHashCode();

            // 自分自身にはダメージを与えない
            if (targetHash == _coreProjectile.CasterHash)
            {
                return;
            }

            // 非キャラクター(地形等)との接触は _hitTargets 登録前にスキップして汚染を防ぐ
            IDamageable receiver = GameManager.Data != null
                ? GameManager.Data.GetManaged(targetHash)?.Damageable
                : null;
            if (receiver == null)
            {
                return;
            }

            // 同一飛翔体で同じターゲットに多重ヒットしない (キャラ衝突のみ登録)
            if (!_hitTargets.Add(targetHash))
            {
                return;
            }

            // Core経由でダメージ処理(IDamageable経由でガード/無敵/HitReaction/イベント発火を共通化)
            ProjectileHitProcessor.ProcessHit(
                _coreProjectile, receiver,
                GameManager.Data, _magic, GameManager.Events);

            // 爆発処理
            if (BulletFeatureProcessor.ShouldExplode(_coreProjectile.Profile.features))
            {
                _manager.ProcessExplosion(_coreProjectile, _magic);
            }

            // OnHitトリガー: ヒット時に子弾を生成
            if (ChildBulletHelper.HasChildBullet(_magic)
                && _magic.childBullet.trigger == ChildBulletTrigger.OnHit)
            {
                _manager.SpawnChildProjectiles(_coreProjectile, _magic);
            }

            // 死亡チェック — Managerに返却を通知
            if (!_coreProjectile.IsAlive)
            {
                _manager.ReturnProjectile(this);
            }
        }
    }
}
