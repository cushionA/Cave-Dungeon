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
        // キャラクター毎のヒット回数を記録 (targetHash → hitCount)。
        // BulletProfile.hitLimit までキャラ毎に多段ヒット可能。
        private Dictionary<int, int> _hitCounts;
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
            _hitCounts = new Dictionary<int, int>();
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
            if (_hitCounts == null)
            {
                _hitCounts = new Dictionary<int, int>();
            }
            else
            {
                _hitCounts.Clear();
            }
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

            // 非キャラクター(地形等)との接触は _hitCounts 登録前にスキップして汚染を防ぐ
            IDamageable receiver = GameManager.Data != null
                ? GameManager.Data.GetManaged(targetHash)?.Damageable
                : null;
            if (receiver == null)
            {
                return;
            }

            // キャラ別ヒット回数ゲート: hitLimit 到達キャラはスキップ
            if (!TryRegisterHit(targetHash, out bool shouldDespawnFromHitLimit))
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

            // 非Pierce は hitLimit 到達で明示Kill (Core側の RegisterHit が未Killなら補完)
            if (shouldDespawnFromHitLimit && _coreProjectile.IsAlive)
            {
                _coreProjectile.Kill();
            }

            // 死亡チェック — Managerに返却を通知
            if (!_coreProjectile.IsAlive)
            {
                _manager.ReturnProjectile(this);
            }
        }

        /// <summary>
        /// 指定ターゲットへのヒットを記録する。
        /// キャラ毎 <see cref="BulletProfile.hitLimit"/> までヒット可能。
        /// </summary>
        /// <param name="targetHash">ターゲットキャラクターのハッシュ。</param>
        /// <param name="shouldDespawn">
        /// 非Pierce でこのヒットにより hitLimit 到達した場合 true (飛翔体を消滅させる)。
        /// Pierce 弾丸または上限未到達なら false。
        /// </param>
        /// <returns>ヒットを受理した場合 true。上限既到達でスキップした場合 false。</returns>
        internal bool TryRegisterHit(int targetHash, out bool shouldDespawn)
        {
            shouldDespawn = false;

            if (_coreProjectile == null)
            {
                return false;
            }

            int limit = GetEffectiveHitLimit(_coreProjectile.Profile.hitLimit);
            _hitCounts.TryGetValue(targetHash, out int current);
            if (current >= limit)
            {
                // 当該ターゲットは既に上限到達
                return false;
            }

            current++;
            _hitCounts[targetHash] = current;

            // 上限到達 + 非Pierce → 消滅
            bool hasPierce = (_coreProjectile.Profile.features & BulletFeature.Pierce) != 0;
            if (current >= limit && !hasPierce)
            {
                shouldDespawn = true;
            }

            return true;
        }

        /// <summary>
        /// 指定ターゲットへの現時点のヒット回数を返す。テスト用途。
        /// </summary>
        internal int GetHitCountForTarget(int targetHash)
        {
            if (_hitCounts == null)
            {
                return 0;
            }
            return _hitCounts.TryGetValue(targetHash, out int count) ? count : 0;
        }

        /// <summary>
        /// BulletProfile.hitLimit = 0 (未設定) を 1 に正規化する。
        /// Core <see cref="Projectile.Initialize"/> と同じ既定値ポリシー。
        /// </summary>
        private static int GetEffectiveHitLimit(int rawHitLimit)
        {
            return rawHitLimit > 0 ? rawHitLimit : 1;
        }
    }
}
