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

        // 爆発処理用の再利用バッファ
        private List<int> _allHashesBuffer;

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
            _allHashesBuffer = new List<int>(64);

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
        /// 飛翔体を1発生成する。targetHashでホーミング対象を指定可能（0で自動検索）。
        /// </summary>
        public ProjectileController SpawnProjectile(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 direction, int targetHash = 0)
        {
            Projectile core = _corePool.Get();
            core.Initialize(casterHash, magic.bulletProfile, position, direction);
            core.TargetHash = targetHash;

            ProjectileController controller = GetOrCreateController();
            controller.Activate(core, magic, this);
            _activeControllers.Add(controller);

            // OnActivateトリガー: 生成直後に子弾を生成
            if (ChildBulletHelper.HasChildBullet(magic)
                && magic.childBullet.trigger == ChildBulletTrigger.OnActivate)
            {
                SpawnChildProjectiles(core, magic);
            }

            return controller;
        }

        /// <summary>
        /// 複数弾を角度分散で生成する。targetHashでホーミング対象を指定可能（0で自動検索）。
        /// </summary>
        public void SpawnSpread(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle,
            int targetHash = 0)
        {
            if (count <= 1)
            {
                SpawnProjectile(casterHash, magic, position, baseDirection, targetHash);
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

                SpawnProjectile(casterHash, magic, position, dir, targetHash);
            }
        }

        private void Update()
        {
            if (_activeControllers == null || _activeControllers.Count == 0)
            {
                return;
            }

            // ホーミング弾のターゲット位置を更新
            UpdateHomingTargets();

            // Core一括移動更新
            ProjectileMovement.UpdateAll(_corePool, Time.deltaTime, Vector2.zero);

            // 特殊効果処理（重力等）+ OnTimerチェック
            // NOTE: SpawnChildProjectilesで_activeControllers末尾にAddされるが、
            // 子弾のchildBulletはnull強制のため同一フレーム内で再処理されても無害
            for (int i = 0; i < _activeControllers.Count; i++)
            {
                Projectile core = _activeControllers[i].CoreProjectile;
                if (core == null || !core.IsAlive)
                {
                    continue;
                }

                BulletFeatureProcessor.ProcessFeatures(core, Time.deltaTime);

                // OnTimerトリガー: emitInterval経過で子弾を生成
                MagicDefinition magic = _activeControllers[i].Magic;
                if (ChildBulletHelper.HasChildBullet(magic)
                    && magic.childBullet.trigger == ChildBulletTrigger.OnTimer
                    && ChildBulletHelper.ShouldEmitOnTimer(
                        core.ElapsedTime, core.LastEmitTime, core.Profile.emitInterval))
                {
                    core.LastEmitTime = core.ElapsedTime;
                    SpawnChildProjectiles(core, magic);
                }
            }

            // Transform同期 + 死亡チェック（逆順でリスト改変安全）
            // NOTE: OnDestroyで子弾が末尾にAddされるが、iは減少方向のため追加分は走査されない
            for (int i = _activeControllers.Count - 1; i >= 0; i--)
            {
                ProjectileController controller = _activeControllers[i];
                if (controller.CoreProjectile == null || !controller.CoreProjectile.IsAlive)
                {
                    // OnDestroyトリガー: 消滅時に子弾を生成
                    if (controller.CoreProjectile != null)
                    {
                        MagicDefinition magic = controller.Magic;
                        if (ChildBulletHelper.HasChildBullet(magic)
                            && magic.childBullet.trigger == ChildBulletTrigger.OnDestroy)
                        {
                            SpawnChildProjectiles(controller.CoreProjectile, magic);
                        }
                    }

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

            _allHashesBuffer.Clear();
            GameManager.Data.GetAllHashes(_allHashesBuffer);

            List<int> targets = BulletFeatureProcessor.GetExplosionTargets(
                projectile.Position, radius, _allHashesBuffer, GameManager.Data);

            for (int i = 0; i < targets.Count; i++)
            {
                int targetHash = targets[i];
                // キャスター自身は爆発ダメージ対象外
                if (targetHash == projectile.CasterHash)
                {
                    continue;
                }

                // SoAからIDamageable逆引き。未登録のキャラはスキップ
                IDamageable receiver = GameManager.Data.GetManaged(targetHash)?.Damageable;
                if (receiver == null)
                {
                    continue;
                }

                ProjectileHitProcessor.ProcessHit(
                    projectile, receiver, GameManager.Data, magic, GameManager.Events);
            }
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

            if (_pendingReturns != null)
            {
                _pendingReturns.Clear();
            }
            if (_corePool != null)
            {
                _corePool.Clear();
            }
        }

        /// <summary>
        /// ホーミング弾のターゲット位置を毎フレーム更新する。
        /// TargetHash指定時はそのキャラのSoA位置を使用、0なら陣営ベースで最寄り敵を検索。
        /// </summary>
        private void UpdateHomingTargets()
        {
            if (GameManager.Data == null)
            {
                return;
            }

            for (int i = 0; i < _activeControllers.Count; i++)
            {
                Projectile core = _activeControllers[i].CoreProjectile;
                if (core == null || !core.IsAlive)
                {
                    continue;
                }

                if (core.Profile.moveType != BulletMoveType.Homing)
                {
                    continue;
                }

                // TargetHashが指定されている場合はそのキャラの位置を直接参照
                if (core.TargetHash != 0 && GameManager.IsCharacterValid(core.TargetHash))
                {
                    ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(core.TargetHash);
                    core.TargetPosition = targetVitals.position;
                }
                else
                {
                    // TargetHash未指定 or ターゲット死亡→陣営ベースで最寄り敵を検索
                    core.TargetPosition = FindNearestEnemyPosition(core);
                }
            }
        }

        /// <summary>
        /// 射撃軌道に最も近い敵対陣営キャラクターの位置をSoAコンテナから返す。
        /// 弾丸の進行方向と敵への方向のドット積（cos角度）で最小角度の敵を選択する。
        /// 敵が見つからない場合は弾丸の現在位置を返す（直進継続）。
        /// </summary>
        private Vector2 FindNearestEnemyPosition(Projectile projectile)
        {
            SoACharaDataDic data = GameManager.Data;
            if (!data.TryGetValue(projectile.CasterHash, out int _))
            {
                return projectile.Position;
            }

            CharacterBelong casterBelong = data.GetFlags(projectile.CasterHash).Belong;
            Vector2 flyDir = projectile.Velocity.normalized;

            // 速度がほぼゼロの場合は距離ベースにフォールバック
            bool useAngle = flyDir.sqrMagnitude > 0.5f;

            _allHashesBuffer.Clear();
            data.GetAllHashes(_allHashesBuffer);

            float bestScore = -2f; // ドット積の最大値は1.0
            float closestSqrDist = float.MaxValue;
            Vector2 bestPos = projectile.Position;

            for (int i = 0; i < _allHashesBuffer.Count; i++)
            {
                int hash = _allHashesBuffer[i];
                if (hash == projectile.CasterHash)
                {
                    continue;
                }

                CharacterBelong targetBelong = data.GetFlags(hash).Belong;

                // 同陣営はスキップ
                if (targetBelong == casterBelong)
                {
                    continue;
                }

                ref CharacterVitals vitals = ref data.GetVitals(hash);
                Vector2 toTarget = vitals.position - projectile.Position;
                float sqrDist = toTarget.sqrMagnitude;

                if (sqrDist < 0.001f)
                {
                    continue;
                }

                if (useAngle)
                {
                    // 射撃方向との角度スコア（ドット積: 1.0=完全一致, -1.0=真逆）
                    float dot = Vector2.Dot(flyDir, toTarget.normalized);
                    if (dot > bestScore || (Mathf.Approximately(dot, bestScore) && sqrDist < closestSqrDist))
                    {
                        bestScore = dot;
                        closestSqrDist = sqrDist;
                        bestPos = vitals.position;
                    }
                }
                else
                {
                    // 速度ゼロ時は距離ベース
                    if (sqrDist < closestSqrDist)
                    {
                        closestSqrDist = sqrDist;
                        bestPos = vitals.position;
                    }
                }
            }

            return bestPos;
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

        /// <summary>
        /// 親弾の位置・方向から子弾を生成する。
        /// 子弾は親のCasterHash・TargetHashを継承し、childBulletは強制nullで無限再帰を防止する。
        /// </summary>
        internal void SpawnChildProjectiles(Projectile parent, MagicDefinition parentMagic)
        {
            ChildBulletConfig config = parentMagic.childBullet;
            if (config == null || config.count <= 0)
            {
                return;
            }

            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(parentMagic, config);

            // 現在の速度方向を優先。静止弾(Set等)は初期方向にフォールバック
            Vector2 direction = parent.Velocity.sqrMagnitude > 0.001f
                ? parent.Velocity.normalized
                : (parent.InitialDirection.sqrMagnitude > 0.001f
                    ? parent.InitialDirection
                    : Vector2.right);

            if (config.count <= 1 || config.spreadAngle <= 0f)
            {
                SpawnProjectile(parent.CasterHash, childMagic,
                    parent.Position, direction, parent.TargetHash);
            }
            else
            {
                SpawnSpread(parent.CasterHash, childMagic,
                    parent.Position, direction, config.count, config.spreadAngle,
                    parent.TargetHash);
            }
        }

        private void OnDestroy()
        {
            ClearAll();
        }
    }
}
