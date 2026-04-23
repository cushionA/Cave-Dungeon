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
        // BulletProfile.perTargetHitLimit までキャラ毎に多段ヒット可能。
        // 総ヒット数 (hitLimit) は Projectile.RegisterHit 側で別途管理 (二段管理)。
        private Dictionary<int, int> _hitCounts;
        private bool _isActive;
        private ProjectileManager _manager;

        // スポーン遅延中は表示・当たり判定を切る状態。遅延明けに復活させるために保持。
        private bool _isSpawnDelayedLastFrame;

        // scaleTime>0 の弾丸で localScale を操作した直後に true。
        // Deactivate 時にデザイナー設定スケールへ戻すために利用。
        private Vector3 _baseLocalScale;
        private bool _baseLocalScaleCaptured;
        private bool _localScaleModified;

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

            // プレハブ/デザイナー設定のスケールを保持（scaleTime>0 のスケール補間で上書き→戻す際のベース）
            _baseLocalScale = transform.localScale;
            _baseLocalScaleCaptured = true;
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
            // テスト等で Awake 未実行または Collider が未取得のケースに備えたフォールバック
            if (_triggerCollider == null)
            {
                _triggerCollider = GetComponent<CircleCollider2D>();
            }
            if (_triggerCollider != null)
            {
                _triggerCollider.enabled = true;
            }

            transform.position = new Vector3(projectile.Position.x, projectile.Position.y, 0f);

            // scaleTime>0 のみ startScale を適用。<=0 はデザイナー設定（または Deactivate で復元済みのベース）を維持。
            if (projectile.Profile.scaleTime > 0f)
            {
                float initialScale = projectile.GetCurrentScale();
                Vector3 baseScale = _baseLocalScaleCaptured ? _baseLocalScale : Vector3.one;
                transform.localScale = new Vector3(
                    baseScale.x * initialScale,
                    baseScale.y * initialScale,
                    baseScale.z);
                _localScaleModified = true;
            }

            // スポーン遅延中は当たり判定と可視性を切る
            _isSpawnDelayedLastFrame = projectile.IsSpawnDelayed;
            _triggerCollider.enabled = !_isSpawnDelayedLastFrame;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.enabled = !_isSpawnDelayedLastFrame;
            }

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
            _isSpawnDelayedLastFrame = false;

            // scaleTime>0 で localScale を上書きした弾丸はデザイナー設定のベーススケールに戻す。
            // 次回この Controller が scaleTime=0 の弾丸に再利用されても前回のスケールが残らない。
            if (_localScaleModified && _baseLocalScaleCaptured)
            {
                transform.localScale = _baseLocalScale;
            }
            _localScaleModified = false;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.enabled = true;
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// CoreのPositionをTransformに同期し、速度方向にスプライトを回転する。
        /// スポーン遅延の状態遷移と、現在スケール(startScale→endScale Lerp)も反映する。
        /// ProjectileManagerから毎フレーム呼ばれる。
        /// </summary>
        public void SyncTransform()
        {
            if (_coreProjectile == null)
            {
                return;
            }

            // スポーン遅延の遷移: 遅延→終了フレームで当たり判定と可視性を復帰
            bool isDelayedNow = _coreProjectile.IsSpawnDelayed;
            if (_isSpawnDelayedLastFrame && !isDelayedNow)
            {
                _triggerCollider.enabled = true;
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.enabled = true;
                }
            }
            _isSpawnDelayedLastFrame = isDelayedNow;

            Vector2 pos = _coreProjectile.Position;
            transform.position = new Vector3(pos.x, pos.y, 0f);

            // scaleTime>0 のみ毎フレーム補間を反映（ベーススケールに乗算）。<=0 ではベーススケールを維持。
            if (_coreProjectile.Profile.scaleTime > 0f)
            {
                float scale = _coreProjectile.GetCurrentScale();
                Vector3 baseScale = _baseLocalScaleCaptured ? _baseLocalScale : Vector3.one;
                transform.localScale = new Vector3(
                    baseScale.x * scale,
                    baseScale.y * scale,
                    baseScale.z);
                _localScaleModified = true;
            }

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

            // スポーン遅延中は当たり判定を無効化（念のため二重チェック）
            if (_coreProjectile != null && _coreProjectile.IsSpawnDelayed)
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

            // キャラ別ヒット回数ゲート: perTargetHitLimit 到達キャラはスキップし、総数にもカウントしない
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

            // 非Pierce は総数到達で明示Kill (TryRegisterHit の RegisterHit 経由で既に Kill 済みの想定、念のため補完)
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
        /// 二段管理: ターゲット別上限 (<see cref="BulletProfile.perTargetHitLimit"/>) で重複ヒットを抑制し、
        /// 総ヒット数 (<see cref="BulletProfile.hitLimit"/>) は <see cref="Projectile.RegisterHit"/> 経由で消費する。
        /// ターゲット別上限に達したターゲットは総数にもカウントされない (加算スキップ)。
        /// </summary>
        /// <param name="targetHash">ターゲットキャラクターのハッシュ。</param>
        /// <param name="shouldDespawn">
        /// 非 Pierce でこのヒットにより総ヒット数が尽きた場合 true (飛翔体を消滅させる)。
        /// Pierce 弾丸、または総数未到達なら false。
        /// </param>
        /// <returns>ヒットを受理した場合 true。ターゲット別上限到達でスキップした場合 false。</returns>
        internal bool TryRegisterHit(int targetHash, out bool shouldDespawn)
        {
            shouldDespawn = false;

            if (_coreProjectile == null || !_coreProjectile.IsAlive)
            {
                return false;
            }

            if (_hitCounts == null)
            {
                _hitCounts = new Dictionary<int, int>();
            }

            // ターゲット別上限ゲート: 到達済みは総数にもカウントせずスキップ
            int perTargetLimit = _coreProjectile.Profile.GetEffectivePerTargetHitLimit();
            _hitCounts.TryGetValue(targetHash, out int current);
            if (current >= perTargetLimit)
            {
                return false;
            }

            current++;
            _hitCounts[targetHash] = current;

            // 総ヒット数を Projectile 側で消費 (既存経路)。
            // RegisterHit は非 Pierce で RemainingHits<=0 になった際に内部で Kill() する。
            _coreProjectile.RegisterHit();
            shouldDespawn = !_coreProjectile.IsAlive;
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
    }
}
