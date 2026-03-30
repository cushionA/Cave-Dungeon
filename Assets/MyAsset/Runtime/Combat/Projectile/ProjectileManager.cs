using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 全飛翔体のライフサイクルを管理するマネージャー。
    /// Core ProjectilePool + Unity GameObjectプールの二重プール同期。
    /// ProjectileMovement.UpdateAllで一括移動→各ControllerのSyncTransformで表示同期。
    /// </summary>
    public class ProjectileManager : MonoBehaviour
    {
        [SerializeField] private GameObject _defaultProjectilePrefab;
        [SerializeField] private int _preWarmCount = 32;

        private ProjectilePool _corePool;
        private Queue<ProjectileController> _goPool;
        private List<ProjectileController> _activeControllers;
        private Transform _poolParent;

        // 返却予約リスト（OnTriggerEnter2D中のリスト改変を防ぐ）
        private List<ProjectileController> _pendingReturns;

        public int ActiveCount => _activeControllers != null ? _activeControllers.Count : 0;

        /// <summary>
        /// マネージャーを初期化する。GameManagerから呼ばれる。
        /// </summary>
        public void Initialize()
        {
            _corePool = new ProjectilePool(_preWarmCount);
            _goPool = new Queue<ProjectileController>();
            _activeControllers = new List<ProjectileController>();
            _pendingReturns = new List<ProjectileController>();

            // プール用の非表示親オブジェクト
            GameObject poolObj = new GameObject("[ProjectilePool]");
            poolObj.transform.SetParent(transform);
            poolObj.SetActive(false);
            _poolParent = poolObj.transform;

            // GameObjectプレウォーム
            if (_defaultProjectilePrefab != null)
            {
                for (int i = 0; i < _preWarmCount; i++)
                {
                    ProjectileController controller = CreatePooledController();
                    _goPool.Enqueue(controller);
                }
            }
        }

        /// <summary>
        /// 飛翔体を1発生成する。
        /// </summary>
        public ProjectileController SpawnProjectile(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 direction)
        {
            Projectile core = _corePool.Get();
            core.Initialize(casterHash, magic.bulletProfile, position, direction);

            ProjectileController controller = GetOrCreateController();
            controller.Activate(core, magic, this);
            _activeControllers.Add(controller);

            return controller;
        }

        /// <summary>
        /// 複数弾を角度分散で生成する。
        /// </summary>
        public void SpawnSpread(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle)
        {
            if (count <= 1)
            {
                SpawnProjectile(casterHash, magic, position, baseDirection);
                return;
            }

            float baseAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;
            float startAngle = -spreadAngle / 2f;
            float angleStep = spreadAngle / (count - 1);

            for (int i = 0; i < count; i++)
            {
                float angle = baseAngle + startAngle + angleStep * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                SpawnProjectile(casterHash, magic, position, dir);
            }
        }

        private void Update()
        {
            if (_activeControllers == null || _activeControllers.Count == 0)
            {
                return;
            }

            // Core一括移動更新
            Vector2 defaultTarget = Vector2.zero;
            ProjectileMovement.UpdateAll(_corePool, Time.deltaTime, defaultTarget);

            // 特殊効果処理（重力等）
            for (int i = 0; i < _activeControllers.Count; i++)
            {
                Projectile core = _activeControllers[i].CoreProjectile;
                if (core != null && core.IsAlive)
                {
                    BulletFeatureProcessor.ProcessFeatures(core, Time.deltaTime);
                }
            }

            // Transform同期 + 死亡チェック（逆順でリスト改変安全）
            for (int i = _activeControllers.Count - 1; i >= 0; i--)
            {
                ProjectileController controller = _activeControllers[i];
                if (controller.CoreProjectile == null || !controller.CoreProjectile.IsAlive)
                {
                    ReturnControllerInternal(controller, i);
                    continue;
                }

                controller.SyncTransform();
            }

            // OnTriggerEnter2D中の返却予約を処理
            ProcessPendingReturns();

            // CorePool側の死亡弾丸クリーンアップ
            _corePool.ReturnAllDead();
        }

        /// <summary>
        /// 飛翔体を返却する。OnTriggerEnter2Dから呼ばれる。
        /// Update中のリスト改変を避けるため予約リストに追加する。
        /// </summary>
        public void ReturnProjectile(ProjectileController controller)
        {
            if (!_pendingReturns.Contains(controller))
            {
                _pendingReturns.Add(controller);
            }
        }

        /// <summary>
        /// 爆発処理を実行する。爆発範囲内の全キャラクターにダメージを与える。
        /// </summary>
        public void ProcessExplosion(Projectile projectile, MagicDefinition magic)
        {
            if (GameManager.Data == null)
            {
                return;
            }

            float radius = projectile.Profile.explodeRadius;
            if (radius <= 0f)
            {
                return;
            }

            // TODO: SoACharaDataDicにGetAllHashes実装後、
            // BulletFeatureProcessor.GetExplosionTargetsで範囲内ターゲットを取得し
            // ProjectileHitProcessor.ProcessHitで各ターゲットにダメージを適用する
        }

        /// <summary>
        /// 全飛翔体をプールに返却する。シーン遷移時に呼ぶ。
        /// </summary>
        public void ClearAll()
        {
            if (_activeControllers == null)
            {
                return;
            }

            for (int i = _activeControllers.Count - 1; i >= 0; i--)
            {
                ReturnControllerInternal(_activeControllers[i], i);
            }

            _pendingReturns.Clear();
            _corePool.Clear();
        }

        private void ProcessPendingReturns()
        {
            for (int i = 0; i < _pendingReturns.Count; i++)
            {
                ProjectileController controller = _pendingReturns[i];
                int index = _activeControllers.IndexOf(controller);
                if (index >= 0)
                {
                    ReturnControllerInternal(controller, index);
                }
            }
            _pendingReturns.Clear();
        }

        private void ReturnControllerInternal(ProjectileController controller, int index)
        {
            controller.Deactivate();
            controller.transform.SetParent(_poolParent);
            _goPool.Enqueue(controller);

            // スワップ除去で高速削除
            int lastIndex = _activeControllers.Count - 1;
            if (index < lastIndex)
            {
                _activeControllers[index] = _activeControllers[lastIndex];
            }
            _activeControllers.RemoveAt(lastIndex);
        }

        private ProjectileController GetOrCreateController()
        {
            if (_goPool.Count > 0)
            {
                ProjectileController controller = _goPool.Dequeue();
                controller.transform.SetParent(null);
                return controller;
            }

            return CreatePooledController();
        }

        private ProjectileController CreatePooledController()
        {
            GameObject go;
            if (_defaultProjectilePrefab != null)
            {
                go = Instantiate(_defaultProjectilePrefab, _poolParent);
            }
            else
            {
                go = new GameObject("[Projectile]");
                go.transform.SetParent(_poolParent);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<CircleCollider2D>();
                go.AddComponent<ProjectileController>();
            }

            ProjectileController controller = go.GetComponent<ProjectileController>();
            if (controller == null)
            {
                controller = go.AddComponent<ProjectileController>();
            }

            go.SetActive(false);
            return controller;
        }

        private void OnDestroy()
        {
            ClearAll();
        }
    }
}
