using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 全飛翔体のライフサイクルを管理するマネージャー。
    /// CoreとGameObjectを同じleaseとして確保・返却し、固定tick開始時の集合だけを進める。
    /// Controllerは表示とUnity接触を担当し、生成・寿命・派生・回収はここを通る。
    /// IGameSubManager を実装し、GameManager から Priority 順に初期化される。
    /// </summary>
    public class ProjectileManager : MonoBehaviour, IGameSubManager
    {
        /// <summary>InitOrder: 飛翔体は Enemy スポナーより後に初期化する。</summary>
        private const int k_InitOrder = 300;

        [SerializeField] private GameObject _defaultProjectilePrefab;
        [SerializeField] private int _preWarmCount = 32;

        private ProjectilePool _corePool;
        private Queue<ProjectileController> _goPool;
        private List<ProjectileStep> _activeProjectiles;
        private Transform _poolParent;

        private Dictionary<GameObject, Queue<ProjectileController>> _prefabPools;
        private List<ProjectileStep> _tickSnapshot;
        private List<int> _allHashesBuffer;
        private List<ExplosionQuery> _explosionQueries;
        private int _explosionDepth;
        private bool _isTicking;
        private bool _isClearing;
        private uint _clearVersion;

        private readonly struct ProjectileStep
        {
            public readonly ProjectileController controller;
            public readonly Projectile core;
            public readonly ulong lease;
            public ProjectileStep(ProjectileController controller, Projectile core, ulong lease)
            {
                this.controller = controller;
                this.core = core;
                this.lease = lease;
            }
        }

        private sealed class ExplosionQuery
        {
            public readonly List<int> hashes = new List<int>(64);
            public readonly List<ActorRef> targets = new List<ActorRef>(64);
        }

        private readonly struct ChildSpawn
        {
            public readonly MagicDefinition magic;
            public readonly ActorRef caster;
            public readonly ActorRef target;
            public readonly Vector2 position;
            public readonly Vector2 direction;
            public readonly int count;
            public readonly float spread;
            public ChildSpawn(Projectile parent, in MagicDefinition parentMagic)
            {
                ChildBulletConfig config = parentMagic.childBullet;
                magic = ChildBulletHelper.CreateChildMagic(parentMagic, config);
                caster = parent.CasterRef;
                target = parent.TargetRef;
                position = parent.Position;
                direction = parent.Velocity.sqrMagnitude > 0.001f ? parent.Velocity.normalized
                    : (parent.InitialDirection.sqrMagnitude > 0.001f ? parent.InitialDirection : Vector2.right);
                count = config.count;
                spread = config.spreadAngle;
            }
        }

        public int ActiveCount => _activeProjectiles != null ? _activeProjectiles.Count : 0;

        /// <summary>IGameSubManager 初期化順。数値が小さいほど先。</summary>
        public int InitOrder => k_InitOrder;

        /// <summary>
        /// IGameSubManager 実装。パラメータは未使用だが、
        /// 将来 SoA/GameEvents 参照時に差し替え可能なよう保持。
        /// </summary>
        void IGameSubManager.Initialize(SoACharaDataDic data, GameEvents events)
        {
            Initialize();
        }

        /// <summary>
        /// IGameSubManager 実装。ClearAllは繰り返し呼んでも同じleaseを二重返却しない。
        /// </summary>
        void IGameSubManager.Dispose()
        {
            ClearAll();
        }

        /// <summary>
        /// マネージャーを初期化する。GameManagerから呼ばれる。
        /// </summary>
        public void Initialize()
        {
            if (_corePool != null) { return; }
            int preWarm = Mathf.Max(0, _preWarmCount);
            _corePool = new ProjectilePool(preWarm);
            _goPool = new Queue<ProjectileController>();
            _activeProjectiles = new List<ProjectileStep>(preWarm);
            _prefabPools = new Dictionary<GameObject, Queue<ProjectileController>>();
            _tickSnapshot = new List<ProjectileStep>(preWarm);
            _allHashesBuffer = new List<int>(64);
            _explosionQueries = new List<ExplosionQuery> { new ExplosionQuery() };

            // プール用の非表示親オブジェクト
            GameObject poolObj = new GameObject("[ProjectilePool]");
            poolObj.transform.SetParent(transform);
            poolObj.SetActive(false);
            _poolParent = poolObj.transform;

            // GameObjectプレウォーム
            if (_defaultProjectilePrefab != null)
            {
                Queue<ProjectileController> pool = GetControllerPool(_defaultProjectilePrefab);
                for (int i = 0; i < preWarm; i++)
                {
                    pool.Enqueue(CreatePooledController(_defaultProjectilePrefab));
                }
            }
        }

        /// <summary>
        /// hashから現在個体を解決する互換入口。遅延命令はActorRef版を使う。
        /// targetHashでホーミング対象を指定可能（0で自動検索）。
        /// BulletProfile.spawnOffset は発射方向ローカル(x=forward, y=up)に変換して position に加算する。
        /// </summary>
        public ProjectileController SpawnProjectile(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 direction, int targetHash = 0)
        {
            ActorRef caster = magic.attackContext.Owner;
            if (!caster.IsValid && (GameManager.Actors == null || !GameManager.Actors.TryGet(casterHash, out caster))) { return null; }
            if (caster.Hash != casterHash) { return null; }
            ActorRef target = default;
            if (targetHash != 0 && GameManager.Actors != null) { GameManager.Actors.TryGet(targetHash, out target); }
            return SpawnProjectile(caster, magic, position, direction, target);
        }

        public ProjectileController SpawnProjectile(ActorRef caster, MagicDefinition magic,
            Vector2 position, Vector2 direction, ActorRef target = default)
        {
            if (!CanBeginPublicShot(caster)) { return null; }
            if (!TryCaptureAttack(caster, ref magic)) { return null; }
            return SpawnCapturedProjectile(caster, magic, position, direction, target);
        }

        private ProjectileController SpawnCapturedProjectile(ActorRef caster, in MagicDefinition magic,
            Vector2 position, Vector2 direction, ActorRef target)
        {
            if (_corePool == null || _isClearing || !magic.attackContext.IsCaptured
                || magic.attackContext.Owner != caster) { return null; }
            Vector2 spawnPosition = ApplyLocalSpawnOffset(position, direction, magic.bulletProfile.spawnOffset);
            GameObject prefab = magic.projectilePrefab != null ? magic.projectilePrefab : _defaultProjectilePrefab;
            uint clearVersion = _clearVersion;
            // Reparenting a designer prefab can invoke OnTransformParentChanged. Do it before leasing Core.
            ProjectileController controller = GetOrCreateController(prefab);
            if (controller == null) { return null; }
            if (_isClearing || clearVersion != _clearVersion)
            {
                controller.transform.SetParent(_poolParent, false);
                if (controller != null) { GetControllerPool(prefab).Enqueue(controller); }
                return null;
            }
            Projectile core = _corePool.Get();
            ulong lease = core.LeaseId;
            core.Initialize(caster, magic.bulletProfile, spawnPosition, direction);
            if (ProjectileHitProcessor.CanAffect(core, magic, GameManager.Data, GameManager.Actors, target))
            {
                core.TrySetTarget(target, GameManager.Actors);
            }
            controller.ActiveIndex = _activeProjectiles.Count;
            _activeProjectiles.Add(new ProjectileStep(controller, core, lease));
            try { controller.Activate(core, magic, this); }
            catch
            {
                ReturnControllerInternal(controller, core, lease, false);
                throw;
            }
            if (!IsCurrent(controller, core, lease))
            {
                ReturnControllerInternal(controller, core, lease, false);
                return null;
            }
            if (controller.HasChildTrigger(ChildBulletTrigger.OnActivate))
            {
                SpawnChildProjectiles(core, controller.CapturedMagic);
            }
            return IsCurrent(controller, core, lease) ? controller : null;
        }

        private bool CanBeginPublicShot(ActorRef caster) => GameManager.Actors != null
            && GameManager.Actors.IsCurrent(caster) && GameManager.IsCharacterValid(caster.Hash)
            && !GameManager.Data.GetFlags(caster.Hash).IsVanished;

        private bool TryCaptureAttack(ActorRef caster, ref MagicDefinition magic, bool acceptedExecution = false)
        {
            if (magic.attackContext.IsCaptured) { return magic.attackContext.Owner == caster; }
            if (GameManager.Actors == null || !GameManager.Actors.IsCurrent(caster)
                || !GameManager.IsCharacterValid(caster.Hash)) { return false; }
            if (!acceptedExecution && GameManager.Data.GetFlags(caster.Hash).IsVanished) { return false; }
            if (magic.equipmentStatusEffects == null)
            {
                magic.equipmentStatusEffects = GameManager.Equipment.GetOnHitEffects(caster.Hash, magic.equipmentSource);
            }
            magic.attackContext = AttackContext.Capture(caster,
                GameManager.Data.GetCombatStats(caster.Hash).attack,
                GameManager.Data.GetStatusEffects(caster.Hash).AttackMultiplier,
                GameManager.Data.GetFlags(caster.Hash).Belong,
                magic.equipmentStatusEffects, magic.statusEffects, magic.targetPolicy.Resolve(magic.magicType));
            return true;
        }

        /// <summary>
        /// spawnOffset を発射方向ローカル座標で解釈してワールド座標のスポーン位置を算出する。
        /// x = forward (発射方向), y = up (forward を 90度反時計回りに回転した方向)。
        /// たとえば direction=(1,0) のとき localOffset=(1,0.5) → worldPosition+(1, 0.5)。
        /// offset==zero または direction==zero の場合は元の位置を返す。
        /// </summary>
        internal static Vector2 ApplyLocalSpawnOffset(Vector2 worldPosition, Vector2 direction, Vector2 localOffset)
        {
            if (localOffset.sqrMagnitude < 0.0000001f)
            {
                return worldPosition;
            }

            Vector2 forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            // up = forward を +90 度(反時計回り)回転: (x,y) → (-y, x)
            Vector2 up = new Vector2(-forward.y, forward.x);

            return worldPosition + forward * localOffset.x + up * localOffset.y;
        }

        /// <summary>
        /// 複数弾を角度分散で生成する。targetHashでホーミング対象を指定可能（0で自動検索）。
        /// </summary>
        public void SpawnSpread(int casterHash, MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle,
            int targetHash = 0)
        {
            ActorRef caster = magic.attackContext.Owner;
            if (!caster.IsValid && (GameManager.Actors == null || !GameManager.Actors.TryGet(casterHash, out caster))) { return; }
            if (caster.Hash != casterHash) { return; }
            ActorRef target = default;
            if (targetHash != 0 && GameManager.Actors != null) { GameManager.Actors.TryGet(targetHash, out target); }
            SpawnSpread(caster, magic, position, baseDirection, count, spreadAngle, target);
        }

        public void SpawnSpread(ActorRef caster, MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle, ActorRef target = default)
        {
            if (!CanBeginPublicShot(caster)) { return; }
            if (!TryCaptureAttack(caster, ref magic)) { return; }
            SpawnCapturedSpread(caster, magic, position, baseDirection, count, spreadAngle, target);
        }

        internal void SpawnSpreadFromExecution(in ActionExecution source, MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle, ActorRef target = default)
        {
            if (GameManager.Actions == null || GameManager.Actors == null
                || !GameManager.Actors.IsCurrent(source.ownerRef)
                || !GameManager.Actions.Execution.TryGetInstance(source.instanceId, out ActionExecution current)
                || current.groupId != source.groupId || current.ownerRef != source.ownerRef
                || magic.actionInstanceId != current.instanceId
                || !AttackContext.CanFinishAcceptedCast(source.ownerRef, current)
                || !GameManager.Actions.Execution.CanApplyExecutionEffects(current.instanceId)) { return; }
            if (!TryCaptureAttack(current.ownerRef, ref magic, true)) { return; }
            SpawnCapturedSpread(current.ownerRef, magic, position, baseDirection, count, spreadAngle, target);
        }

        private void SpawnCapturedSpread(ActorRef caster, in MagicDefinition magic,
            Vector2 position, Vector2 baseDirection, int count, float spreadAngle, ActorRef target)
        {
            if (count <= 0) { return; }
            if (count == 1)
            {
                SpawnCapturedProjectile(caster, magic, position, baseDirection, target);
                return;
            }

            float baseAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;
            float startAngle = -spreadAngle / 2f;
            float angleStep = spreadAngle / (count - 1);

            uint clearVersion = _clearVersion;
            for (int i = 0; i < count; i++)
            {
                if (_isClearing || clearVersion != _clearVersion) { break; }
                float angle = baseAngle + startAngle + angleStep * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                SpawnCapturedProjectile(caster, magic, position, dir, target);
            }
        }

        public void SimulationTick(float deltaTime)
        {
            if (_isTicking || _isClearing || _activeProjectiles == null || _activeProjectiles.Count == 0) { return; }
            _isTicking = true;
            _tickSnapshot.Clear();
            for (int i = 0; i < _activeProjectiles.Count; i++)
            {
                _tickSnapshot.Add(_activeProjectiles[i]);
            }
            try
            {
                // Queries observe current character bodies. Projectile sweep origins are explicit world positions.
                Physics2D.SyncTransforms();
                UpdateHomingTargets();
                for (int i = 0; i < _tickSnapshot.Count; i++)
                {
                    ProjectileStep step = _tickSnapshot[i];
                    if (!IsCurrent(step.controller, step.core, step.lease) || step.controller == null)
                    {
                        ReturnControllerInternal(step.controller, step.core, step.lease, false);
                        continue;
                    }
                    if (step.core.IsAlive)
                    {
                        ProjectileMovement.UpdateProjectile(step.core, deltaTime, Vector2.zero, true);
                        step.controller.SweepToCorePosition(step.core, step.lease);
                        if (!IsCurrent(step.controller, step.core, step.lease)) { continue; }
                        step.core.CompleteSimulationStep();
                        if (step.core.IsAlive && !step.core.IsSpawnDelayed
                            && step.controller.HasChildTrigger(ChildBulletTrigger.OnTimer)
                            && ChildBulletHelper.ShouldEmitOnTimer(step.core.ElapsedTime,
                                step.core.LastEmitTime, step.core.CapturedProfile.emitInterval))
                        {
                            step.core.LastEmitTime = step.core.ElapsedTime;
                            SpawnChildProjectiles(step.core, step.controller.CapturedMagic);
                        }
                    }
                    if (IsCurrent(step.controller, step.core, step.lease) && !step.core.IsAlive)
                    {
                        ReturnControllerInternal(step.controller, step.core, step.lease, true);
                    }
                }
            }
            finally
            {
                _tickSnapshot.Clear();
                _isTicking = false;
            }
        }

        /// <summary>接触中は死亡と判定停止だけを確定し、両poolへの返却は固定tickの単一経路で行う。</summary>
        public void ReturnProjectile(ProjectileController controller)
        {
            if (controller == null) { return; }
            Projectile core = controller.CoreProjectile;
            if (core == null || !IsCurrent(controller, core, controller.LeaseId)) { return; }
            core.Kill();
            controller.StopCollision();
        }

        internal bool IsCurrent(ProjectileController controller, Projectile core, ulong lease)
        {
            if (controller == null || core == null || !controller.Matches(core, lease)
                || _corePool == null || !_corePool.IsActive(core, lease)) { return false; }
            int index = controller.ActiveIndex;
            return MatchesEntry(index, controller, core, lease);
        }

        /// <summary>
        /// 爆発処理を実行する。爆発範囲内の全キャラクターにダメージを与える。
        /// </summary>
        public void ProcessExplosion(Projectile projectile, MagicDefinition magic)
        {
            if (projectile == null || GameManager.Data == null || _corePool == null) { return; }
            ulong lease = projectile.LeaseId;
            if (!_corePool.IsActive(projectile, lease)) { return; }
            float radius = projectile.CapturedProfile.explodeRadius;
            if (radius <= 0f) { return; }
            Vector2 center = projectile.Position;
            if (_explosionDepth == _explosionQueries.Count) { _explosionQueries.Add(new ExplosionQuery()); }
            ExplosionQuery query = _explosionQueries[_explosionDepth++];
            query.hashes.Clear();
            query.targets.Clear();
            try
            {
                GameManager.Data.GetAllHashes(query.hashes);
                float radiusSquared = radius * radius;
                for (int i = 0; i < query.hashes.Count; i++)
                {
                    int hash = query.hashes[i];
                    if (GameManager.Actors == null || !GameManager.Actors.TryGet(hash, out ActorRef target)
                        || !ProjectileHitProcessor.CanAffect(projectile, magic, GameManager.Data,
                            GameManager.Actors, target)) { continue; }
                    if ((GameManager.Data.GetVitals(hash).position - center).sqrMagnitude <= radiusSquared)
                    {
                        query.targets.Add(target);
                    }
                }
                // Each recursion depth owns its buffer; damage callbacks cannot overwrite this observation.
                for (int i = 0; i < query.targets.Count; i++)
                {
                    if (!_corePool.IsActive(projectile, lease)) { break; }
                    ActorRef target = query.targets[i];
                    if (!ProjectileHitProcessor.CanAffect(projectile, magic, GameManager.Data,
                        GameManager.Actors, target)) { continue; }
                    IDamageable receiver = GameManager.Data.GetManaged(target.Hash)?.Damageable;
                    if (receiver == null) { continue; }
                    ProjectileHitProcessor.ProcessHit(projectile, receiver, GameManager.Data, magic,
                        GameManager.Events, GameManager.StatusEffects, GameManager.Actors, target);
                }
            }
            finally
            {
                query.hashes.Clear();
                query.targets.Clear();
                _explosionDepth--;
            }
        }

        /// <summary>
        /// 全飛翔体をプールに返却する。シーン遷移時に呼ぶ。
        /// </summary>
        public void ClearAll()
        {
            if (_activeProjectiles == null || _isClearing) { return; }
            _isClearing = true;
            _clearVersion++;
            try
            {
                while (_activeProjectiles.Count > 0)
                {
                    ProjectileStep step = _activeProjectiles[_activeProjectiles.Count - 1];
                    ReturnControllerInternal(step.controller, step.core, step.lease, false);
                }
                _corePool.Clear();
            }
            finally { _isClearing = false; }
        }

        /// <summary>
        /// ホーミング弾のターゲット位置を毎フレーム更新する。
        /// 明示対象も現在の対象条件を照合し、対象外なら同じ条件で進行方向に近い候補を検索する。
        /// </summary>
        private void UpdateHomingTargets()
        {
            if (GameManager.Data == null)
            {
                return;
            }

            _allHashesBuffer.Clear();
            GameManager.Data.GetAllHashes(_allHashesBuffer);
            for (int i = 0; i < _tickSnapshot.Count; i++)
            {
                ProjectileStep step = _tickSnapshot[i];
                Projectile core = step.core;
                if (core == null || !core.IsAlive || !IsCurrent(step.controller, core, step.lease))
                {
                    continue;
                }

                if (core.CapturedProfile.moveType != BulletMoveType.Homing)
                {
                    continue;
                }

                // TargetHashが指定されている場合はそのキャラの位置を直接参照
                bool currentTarget = core.RefreshTarget(GameManager.Actors);
                if (currentTarget && (!step.controller.CanAffectTarget(core, core.TargetRef)
                    || GameManager.Data.GetVitals(core.TargetHash).currentHp <= 0))
                {
                    core.TrySetTarget(default, GameManager.Actors);
                    currentTarget = false;
                }
                if (currentTarget && GameManager.IsCharacterValid(core.TargetHash))
                {
                    ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(core.TargetHash);
                    core.TargetPosition = targetVitals.position;
                }
                else
                {
                    core.TargetPosition = FindPreferredTargetPosition(core, step.controller);
                }
            }
        }

        /// <summary>
        /// 対象条件に合うキャラクターのうち、射撃軌道に最も近い位置をSoAから返す。
        /// 弾丸の進行方向と敵への方向のドット積（cos角度）で最小角度の敵を選択する。
        /// 候補が見つからない場合は弾丸の現在位置を返す（直進継続）。
        /// </summary>
        private Vector2 FindPreferredTargetPosition(Projectile projectile, ProjectileController controller)
        {
            SoACharaDataDic data = GameManager.Data;
            Vector2 flyDir = projectile.Velocity.normalized;

            // 速度がほぼゼロの場合は距離ベースにフォールバック
            bool useAngle = flyDir.sqrMagnitude > 0.5f;

            float bestScore = -2f; // ドット積の最大値は1.0
            float closestSqrDist = float.MaxValue;
            Vector2 bestPos = projectile.Position;

            for (int i = 0; i < _allHashesBuffer.Count; i++)
            {
                int hash = _allHashesBuffer[i];
                ActorRef target = default;
                if (GameManager.Actors == null || !GameManager.Actors.TryGet(hash, out target)
                    || !controller.CanAffectTarget(projectile, target)
                    || data.GetVitals(hash).currentHp <= 0) { continue; }

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

        private bool MatchesEntry(int index, ProjectileController controller, Projectile core, ulong lease)
        {
            if (index < 0 || index >= _activeProjectiles.Count) { return false; }
            ProjectileStep entry = _activeProjectiles[index];
            return ReferenceEquals(entry.controller, controller) && entry.core == core && entry.lease == lease;
        }

        private void ReturnControllerInternal(ProjectileController controller, Projectile core, ulong lease, bool emitDestroy)
        {
            // The manager retains Core independently of the Unity object's lifetime/binding.
            int index = ReferenceEquals(controller, null) ? -1 : controller.ActiveIndex;
            if (!MatchesEntry(index, controller, core, lease))
            {
                index = -1;
                for (int i = 0; i < _activeProjectiles.Count; i++)
                {
                    if (!MatchesEntry(i, controller, core, lease)) { continue; }
                    index = i;
                    break;
                }
                if (index < 0) { return; }
            }
            bool emit = emitDestroy && _corePool.IsActive(core, lease) && !ReferenceEquals(controller, null)
                && controller.HasChildTrigger(ChildBulletTrigger.OnDestroy);
            // Own the child inputs before pool return/OnDisable can reuse the controller.
            ChildSpawn children = emit ? new ChildSpawn(core, controller.CapturedMagic) : default;
            uint clearVersion = _clearVersion;
            int lastIndex = _activeProjectiles.Count - 1;
            if (index != lastIndex)
            {
                ProjectileStep last = _activeProjectiles[lastIndex];
                _activeProjectiles[index] = last;
                if (!ReferenceEquals(last.controller, null)) { last.controller.ActiveIndex = index; }
            }
            _activeProjectiles.RemoveAt(lastIndex);
            if (!ReferenceEquals(controller, null)) { controller.ActiveIndex = -1; }
            _corePool.TryReturn(core, lease);
            if (controller != null)
            {
                controller.Deactivate();
                if (controller != null)
                {
                    controller.transform.SetParent(_poolParent, false);
                    if (controller != null) { GetControllerPool(controller.PoolPrefab).Enqueue(controller); }
                }
            }
            if (emit && clearVersion == _clearVersion && !_isClearing) { SpawnChildren(children); }
        }

        private Queue<ProjectileController> GetControllerPool(GameObject prefab)
        {
            if (ReferenceEquals(prefab, null)) { return _goPool; }
            if (!_prefabPools.TryGetValue(prefab, out Queue<ProjectileController> pool))
            {
                pool = new Queue<ProjectileController>();
                _prefabPools.Add(prefab, pool);
            }
            return pool;
        }

        private ProjectileController GetOrCreateController(GameObject prefab)
        {
            Queue<ProjectileController> pool = GetControllerPool(prefab);
            ProjectileController controller = null;
            while (pool.Count > 0 && controller == null) { controller = pool.Dequeue(); }
            if (controller == null) { controller = CreatePooledController(prefab); }
            controller.transform.SetParent(transform, false);
            return controller;
        }

        private ProjectileController CreatePooledController(GameObject prefab)
        {
            GameObject go;
            if (prefab != null) { go = Instantiate(prefab, _poolParent); }
            else
            {
                go = new GameObject("[Projectile]");
                go.transform.SetParent(_poolParent, false);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<CircleCollider2D>();
            }
            ProjectileController controller = go.GetComponent<ProjectileController>();
            if (controller == null) { controller = go.AddComponent<ProjectileController>(); }
            go.SetActive(false);
            controller.PrepareForPool(prefab);
            return controller;
        }

        /// <summary>個体・攻撃snapshotを継承し、子自身のchildBulletを切って再帰生成を防ぐ。</summary>
        internal void SpawnChildProjectiles(Projectile parent, MagicDefinition parentMagic)
        {
            if (parent == null || !ChildBulletHelper.HasChildBullet(parentMagic)) { return; }
            SpawnChildren(new ChildSpawn(parent, parentMagic));
        }

        private void SpawnChildren(in ChildSpawn children)
        {
            if (_isClearing || children.count <= 0 || !children.caster.IsValid) { return; }
            if (children.magic.attackContext.IsCaptured)
            {
                SpawnCapturedSpread(children.caster, children.magic, children.position,
                    children.direction, children.count, children.spread, children.target);
            }
            else
            {
                SpawnSpread(children.caster, children.magic, children.position,
                    children.direction, children.count, children.spread, children.target);
            }
        }

        private void OnDestroy()
        {
            ClearAll();
        }
    }
}
