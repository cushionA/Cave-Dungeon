using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// GameManagerCoreのMonoBehaviourラッパー。
    /// Singleton。static Data/Eventsでゲーム全体からアクセスする。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour, ISimulationTickWorld
    {
        /// <summary>
        /// SoAコンテナ初期容量。同時登録キャラクター数（プレイヤー+仲間+敵+召喚+混乱+その他）を包含する。
        /// 自動生成コンテナは固定容量で超過時に InvalidOperationException を投げるため、
        /// ボス戦 + 弾幕 + 召喚獣の最大同時存在数を考慮して余裕を持たせる。
        /// </summary>
        private const int k_InitialContainerCapacity = 256;

        /// <summary>SoA 容量使用率警告閾値（開発ビルドのみ）。</summary>
        private const float k_CapacityWarningThreshold = 0.8f;

        /// <summary>警告発報済みフラグ（同一閾値で連続ログを出さない）。</summary>
        private bool _capacityWarningFired;

        private GameManagerCore _core;
        private GameActionRuntime _actions;
        private AIObservationCoordinator _observations;
        private readonly List<int> _simulationHashes = new List<int>();
        private readonly List<ActorRef> _simulationActors = new List<ActorRef>();
        private readonly SimulationTickRunner _simulation = new SimulationTickRunner();
        [SerializeField] private StatusEffectCatalog _statusEffectCatalog;
        private StatusEffectPresentationController _statusEffectPresentation;

        /// <summary>
        /// 登録された IGameSubManager を型別にインデックスする。
        /// SubManager 追加時に GameManager の編集を不要にするための中央カタログ。
        /// Key は具象型 (例: <see cref="ProjectileManager"/>)、値は同じインスタンスを IGameSubManager として保持する。
        /// </summary>
        private readonly Dictionary<Type, IGameSubManager> _subManagers = new Dictionary<Type, IGameSubManager>();

        public static GameManager Instance { get; private set; }
        public static SoACharaDataDic Data => Instance != null ? Instance._core.Data : null;
        public static GameEvents Events => Instance != null ? Instance._core.Events : null;
        public static StatusEffectSystem StatusEffects => Instance != null ? Instance._core.StatusEffects : null;
        public static EquipmentStore Equipment => Instance != null ? Instance._core.Equipment : null;
        public static ActorRegistry Actors => Instance != null ? Instance._core.Actors : null;
        public static CharacterStatsSystem Stats => Instance != null ? Instance._core.Stats : null;
        public static AIObservationContext Observation => Instance != null ? Instance._observations?.Observation : null;
        public static AITargetRelations TargetRelations => Instance != null ? Instance._observations?.Targets : null;
        public static GameActionRuntime Actions => Instance != null ? Instance._actions : null;
        public static StatusEffectCatalog StatusEffectCatalog => Instance != null ? Instance._statusEffectCatalog : null;

        /// <summary>
        /// ProjectileManager への薄いプロキシ。型別辞書からの解決を維持しつつ後方互換 API を提供する。
        /// </summary>
        public static ProjectileManager Projectiles => GetSubManager<ProjectileManager>();

        /// <summary>
        /// EnemySpawnerManager への薄いプロキシ。
        /// </summary>
        public static EnemySpawnerManager EnemySpawner => GetSubManager<EnemySpawnerManager>();

        /// <summary>
        /// LevelStreamingController への薄いプロキシ。
        /// </summary>
        public static LevelStreamingController LevelStreaming => GetSubManager<LevelStreamingController>();

        /// <summary>
        /// 登録済み IGameSubManager を型指定で取得する汎用アクセサ。
        /// Instance 未初期化時、または該当型が未登録の場合は null を返す。
        /// </summary>
        /// <typeparam name="T">取得したい IGameSubManager 実装型 (MonoBehaviour 継承想定)</typeparam>
        /// <returns>登録済みインスタンス、未登録なら null</returns>
        public static T GetSubManager<T>() where T : class, IGameSubManager
        {
            if (Instance == null)
            {
                return null;
            }

            if (Instance._subManagers.TryGetValue(typeof(T), out IGameSubManager mgr))
            {
                return mgr as T;
            }

            return null;
        }

        /// <summary>
        /// SoAコンテナにキャラクターが存在するか検証する共通ヘルパー。
        /// "Data == null || !Data.TryGetValue" パターンの一元化。
        /// </summary>
        public static bool IsCharacterValid(int hash)
        {
            return Data != null && Data.TryGetValue(hash, out int _);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _core = new GameManagerCore();
            _core.Initialize(k_InitialContainerCapacity);
            _actions = new GameActionRuntime(_core.Data, _core.Events, _core.Actions);
            _observations = new AIObservationCoordinator(_core.Data, _core.Actors, _core.Events);
            _statusEffectPresentation = new StatusEffectPresentationController(_core.Data, _core.StatusEffects.Store, _statusEffectCatalog);

            InitializeSubManagers();
        }

        private void FixedUpdate() => SimulationTick(Time.fixedDeltaTime, Time.fixedTime);

        /// <summary>物理観測→入力公開→共有実行/観測→全キャラ制御→物理適用を同じ固定tickで実行する。</summary>
        public void SimulationTick(float deltaTime, float currentTime)
        {
            if (_core == null || _core.Data == null) { return; }
            _simulation.Run(this, deltaTime, currentTime);
        }

        void ISimulationTickWorld.CaptureSimulationActors()
        {
            _simulationHashes.Clear();
            _core.Data.GetAllHashes(_simulationHashes);
            _simulationActors.Clear();
            for (int i = 0; i < _simulationHashes.Count; i++)
            {
                if (_core.Actors.TryGet(_simulationHashes[i], out ActorRef actor)) { _simulationActors.Add(actor); }
            }
        }

        void ISimulationTickWorld.CaptureSimulationPhysics()
        {
            for (int i = 0; i < _simulationActors.Count; i++)
            {
                if (TryGetSimulationCharacter(i, false, out BaseCharacter character)) { character.CaptureSimulationPhysics(); }
            }
        }

        void ISimulationTickWorld.PrepareSimulationInputs(float currentTime)
        {
            for (int i = 0; i < _simulationActors.Count; i++)
            {
                if (TryGetSimulationCharacter(i, true, out BaseCharacter character)) { character.PrepareSimulationInput(currentTime); }
            }
        }

        void ISimulationTickWorld.AdvanceSharedSimulation(float deltaTime, float currentTime)
        {
            _observations.BeginTick(currentTime);
            _core.StatusEffects.Tick(deltaTime);
            _core.Actions.Tick(deltaTime);
            _actions.Tick();
            ProjectileManager projectiles = Projectiles;
            if (projectiles != null) { projectiles.SimulationTick(deltaTime); }
            for (int i = 0; i < _simulationActors.Count; i++)
            {
                if (TryGetSimulationCharacter(i, false, out BaseCharacter character)) { character.PrepareObservationTick(deltaTime); }
            }
        }

        void ISimulationTickWorld.CaptureSimulationObservation(long tick, float currentTime) => _observations.Capture(tick, currentTime);

        void ISimulationTickWorld.SimulateCharacters(float deltaTime, float currentTime)
        {
            for (int i = 0; i < _simulationActors.Count; i++)
            {
                if (TryGetSimulationCharacter(i, true, out BaseCharacter character)) { character.SimulateCharacterTick(deltaTime, currentTime); }
            }
        }

        void ISimulationTickWorld.ApplySimulationPhysics()
        {
            for (int i = 0; i < _simulationActors.Count; i++)
            {
                if (TryGetSimulationCharacter(i, true, out BaseCharacter character)) { character.ApplySimulationPhysics(); }
            }
        }

        private bool TryGetSimulationCharacter(int index, bool requireEnabled, out BaseCharacter character)
        {
            character = null;
            ActorRef actor = _simulationActors[index];
            if (!_core.Actors.IsCurrent(actor) || !_core.Data.TryGetValue(actor.Hash, out _)) { return false; }
            character = _core.Data.GetManaged(actor.Hash) as BaseCharacter;
            return character != null && character.gameObject.activeInHierarchy && (!requireEnabled || character.isActiveAndEnabled);
        }

        public static bool TryGetObservation(int ownerHash, out AIObservationContext observation)
        {
            observation = Observation;
            return observation != null && Actors != null && Actors.TryGet(ownerHash, out ActorRef actor)
                && observation.Contains(actor);
        }

        public static Vector2 ObservedPosition(int hash, Vector2 fallback)
        {
            AIObservationContext observation = Observation;
            return observation != null && observation.TryGetActor(hash, out AIActorObservation actor)
                ? actor.vitals.position : fallback;
        }

        private void LateUpdate()
        {
            _statusEffectPresentation.Sync();
            _actions.Present();
        }

        /// <summary>
        /// 子オブジェクトの IGameSubManager 実装を一括取得し、InitOrder 昇順で初期化する。
        /// Priority 設計: Streaming(100) → Enemy(200) → Projectile(300) の順。
        /// 取得したインスタンスは <see cref="_subManagers"/> に具象型をキーとして登録し、
        /// <see cref="GetSubManager{T}"/> / <see cref="Projectiles"/> / <see cref="EnemySpawner"/> /
        /// <see cref="LevelStreaming"/> からアクセスできる。SubManager 追加時は IGameSubManager を実装するだけでよい。
        /// </summary>
        private void InitializeSubManagers()
        {
            // 1回の走査で全 IGameSubManager を取得
            IGameSubManager[] subManagers = GetComponentsInChildren<IGameSubManager>(true);

            // InitOrder 昇順ソート
            Array.Sort(subManagers, (a, b) => a.InitOrder.CompareTo(b.InitOrder));

            _subManagers.Clear();

            for (int i = 0; i < subManagers.Length; i++)
            {
                IGameSubManager mgr = subManagers[i];

                // 具象型をキーとして登録。同一型で複数登録された場合は最初のインスタンスを優先
                // (GetComponentsInChildren の走査順に依存) し、警告を出す。
                Type concreteType = mgr.GetType();
                if (_subManagers.ContainsKey(concreteType))
                {
                    Debug.LogWarning(
                        $"[GameManager] IGameSubManager of type {concreteType.Name} already registered. Ignoring duplicate on {((MonoBehaviour)mgr).gameObject.name}.");
                }
                else
                {
                    _subManagers.Add(concreteType, mgr);
                }

                mgr.Initialize(_core.Data, _core.Events);
            }
        }

        /// <summary>
        /// CharacterInfoからSoA構造体を生成してキャラクター登録する。
        /// BaseCharacter 参照を持たない呼び出し元（テスト等）向けのオーバーロード。
        /// このオーバーロードで登録したキャラは ManagedCharacter を持たないため、
        /// GetManaged(hash) が null を返す。飛翔体/ヒット判定で .Damageable が nullとなり
        /// ダメージ処理がスキップされる点に注意。ゲームコードでは必ず
        /// <see cref="RegisterCharacter(BaseCharacter, CharacterInfo)"/> を使用すること。
        /// </summary>
        [System.Obsolete("テスト専用。ゲームコードは RegisterCharacter(BaseCharacter, CharacterInfo) を使用してください。")]
        public int RegisterCharacter(int hash, CharacterInfo info)
        {
            return RegisterCharacterInternal(hash, info, null);
        }

        /// <summary>
        /// CharacterInfoからSoA構造体を生成してキャラクター登録する。
        /// </summary>
        public int RegisterCharacter(BaseCharacter chara, CharacterInfo info)
        {
            return RegisterCharacterInternal(chara.ObjectHash, info, chara);
        }

        private int RegisterCharacterInternal(int hash, CharacterInfo info, BaseCharacter chara)
        {
            CharacterRegistration registration = info.CreateRegistration(Physics2D.gravity.magnitude);
            int result = _core.RegisterCharacter(hash, registration, chara);
            WarnIfCapacityUsageHigh();
            return result;
        }

        public void ApplyCharacterGrowth(int hash, in CharacterGrowth growth)
        {
            _core.ApplyCharacterGrowth(hash, growth);
        }

        public void UnregisterCharacter(int hash)
        {
            if (_core != null && _core.IsInitialized)
            {
                _core.UnregisterCharacter(hash);
                // 下回ったら再警告できるようフラグ復帰
                if (_capacityWarningFired && _core.Data != null
                    && (float)_core.Data.Count / k_InitialContainerCapacity < k_CapacityWarningThreshold)
                {
                    _capacityWarningFired = false;
                }
            }
        }

        /// <summary>
        /// SoA コンテナの使用率が閾値を超えていたら開発ビルドで警告を出す。
        /// 自動生成コンテナは固定容量で、超過すると InvalidOperationException を投げるため、
        /// 超過前にチューニング機会を与える目的。
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void WarnIfCapacityUsageHigh()
        {
            if (_capacityWarningFired || _core == null || _core.Data == null)
            {
                return;
            }

            int count = _core.Data.Count;
            float ratio = (float)count / k_InitialContainerCapacity;
            if (ratio >= k_CapacityWarningThreshold)
            {
                _capacityWarningFired = true;
                Debug.LogWarning(
                    $"[SoACharaDataDic] 容量逼迫: {count}/{k_InitialContainerCapacity} ({ratio * 100f:F0}%). 超過すると InvalidOperationException が発生します。k_InitialContainerCapacity の引き上げを検討してください。");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                _observations?.Dispose();
                _statusEffectPresentation?.Dispose();
                _actions?.Dispose();
                _core?.Dispose();
                _subManagers.Clear();
                Instance = null;
            }
        }
    }
}
