using System;
using UnityEngine;

namespace Game.Core
{
    public class Projectile
    {
        /// <summary>Pool からの取得ごとに変わる識別子。遅延した返却・接触通知の照合に使う。</summary>
        public ulong LeaseId { get; private set; }
        internal ProjectilePool PoolOwner { get; private set; }
        internal int ActiveIndex { get; set; } = -1;
        public int CasterHash { get; private set; }
        public ActorRef CasterRef { get; private set; }
        public ActorRef TargetRef { get; private set; }
        public int RemainingHits { get; private set; }
        private BulletProfile _profile;
        public BulletProfile Profile => _profile;
        // Borrow only for immediate calculations. Pool reset/reuse still requires a value snapshot.
        internal ref readonly BulletProfile CapturedProfile => ref _profile;
        public float ElapsedTime { get; private set; }
        public bool IsAlive { get; private set; }
        private int _targetHash;
        /// <summary>互換入口。世代付き経路はTrySetTargetを使う。</summary>
        public int TargetHash
        {
            get => _targetHash;
            set { _targetHash = value; TargetRef = default; }
        }
        public float LastEmitTime { get; set; }
        public Vector2 InitialDirection { get; private set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 TargetPosition { get; set; }

        /// <summary>スポーン遅延の残り秒数。0 以下で遅延終了（移動・当たり判定が有効）。</summary>
        public float SpawnDelayRemaining { get; private set; }

        /// <summary>遅延中は true。Controller 側で可視性・当たり判定を制御するために参照する。</summary>
        public bool IsSpawnDelayed => SpawnDelayRemaining > 0f;

        /// <summary>世代を持たない呼出しの互換入口。RuntimeはActorRef版で初期化する。</summary>
        public void Initialize(int casterHash, BulletProfile profile, Vector2 position, Vector2 direction)
        {
            Reset();
            CasterHash = casterHash;
            _profile = profile;
            RemainingHits = profile.hitLimit > 0 ? profile.hitLimit : 1;
            ElapsedTime = 0f;
            IsAlive = true;
            Position = position;
            InitialDirection = direction.normalized;
            Velocity = InitialDirection * profile.speed;
            SpawnDelayRemaining = profile.spawnDelay > 0f ? profile.spawnDelay : 0f;
        }

        public void Initialize(ActorRef caster, BulletProfile profile, Vector2 position, Vector2 direction)
        {
            if (!caster.IsValid) { throw new ArgumentException("A projectile needs an actor generation.", nameof(caster)); }
            Initialize(caster.Hash, profile, position, direction);
            CasterRef = caster;
        }

        public bool TrySetTarget(ActorRef target, ActorRegistry actors)
        {
            if (!target.IsValid && target != default) { return false; }
            if (target.IsValid && (actors == null || !actors.IsCurrent(target))) { return false; }
            TargetRef = target;
            _targetHash = target.Hash;
            return true;
        }

        public bool RefreshTarget(ActorRegistry actors)
        {
            if (!TargetRef.IsValid) { return false; }
            if (actors != null && actors.IsCurrent(TargetRef)) { return true; }
            TargetRef = default;
            _targetHash = 0;
            return false;
        }

        public bool CanHit(ActorRef target, ActorRegistry actors)
        {
            // 対象条件（自身を含めるか等）は AttackTargetRules に集約し、ここは世代だけを照合する。
            return actors != null && actors.IsCurrent(target)
                && (!TargetRef.IsValid || target.Hash != TargetRef.Hash || target == TargetRef);
        }

        public void Tick(float deltaTime)
        {
            AdvanceTime(deltaTime);
            CompleteSimulationStep();
        }

        /// <summary>
        /// 遅延と寿命を除いた、このステップ内で実際に移動できる秒数を返す。
        /// 寿命終了の確定は接触解決後にも実行できるよう分離している。
        /// </summary>
        internal float AdvanceTime(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Simulation time must be finite and non-negative.");
            }
            if (!IsAlive || deltaTime == 0f) { return 0f; }

            float delayTime = Mathf.Min(SpawnDelayRemaining, deltaTime);
            SpawnDelayRemaining -= delayTime;
            float activeTime = deltaTime - delayTime;
            if (_profile.lifeTime > 0f)
            {
                activeTime = Mathf.Min(activeTime, Mathf.Max(0f, _profile.lifeTime - ElapsedTime));
                ElapsedTime = Mathf.Min(_profile.lifeTime, ElapsedTime + activeTime);
            }
            else { ElapsedTime += activeTime; }
            return activeTime;
        }

        /// <summary>移動した最後の区間への接触解決を終えた後、寿命切れを確定する。</summary>
        public void CompleteSimulationStep()
        {
            if (_profile.lifeTime > 0f && ElapsedTime >= _profile.lifeTime) { Kill(); }
        }

        internal void Acquire(ProjectilePool owner, int activeIndex, ulong leaseId)
        {
            PoolOwner = owner;
            ActiveIndex = activeIndex;
            LeaseId = leaseId;
        }

        /// <summary>現在のスケールを BulletProfile から算出する。scaleTime<=0 なら 1f。</summary>
        public float GetCurrentScale()
        {
            ref readonly BulletProfile p = ref _profile;
            if (p.scaleTime <= 0f)
            {
                return 1f;
            }
            float t = Mathf.Clamp01(ElapsedTime / p.scaleTime);
            return Mathf.Lerp(p.GetEffectiveStartScale(), p.GetEffectiveEndScale(), t);
        }

        /// <summary>現在の追尾力を BulletProfile から算出する（homingStrength + homingAcceleration*elapsedTime）。</summary>
        public float GetCurrentHomingStrength()
        {
            ref readonly BulletProfile p = ref _profile;
            float baseStrength = p.GetEffectiveHomingStrength();
            float strength = baseStrength + p.homingAcceleration * ElapsedTime;
            return strength < 0f ? 0f : strength;
        }

        public bool RegisterHit()
        {
            if (!IsAlive)
            {
                return false;
            }

            // Pierce は旧仕様どおり寿命まで貫通する。hitLimit は非 Pierce の総命中数。
            // 無制限貫通中もカウンターを負数へ減らし続けない。
            if (RemainingHits > 0) { RemainingHits--; }
            bool hasPierce = (_profile.features & BulletFeature.Pierce) != 0;

            if (RemainingHits <= 0 && !hasPierce)
            {
                Kill();
            }

            return true;
        }

        public void Kill()
        {
            IsAlive = false;
        }

        public void Reset()
        {
            IsAlive = false;
            _profile = default;
            CasterHash = 0;
            CasterRef = default;
            TargetHash = 0;
            LastEmitTime = 0f;
            RemainingHits = 0;
            ElapsedTime = 0f;
            SpawnDelayRemaining = 0f;
            Position = Vector2.zero;
            InitialDirection = Vector2.zero;
            Velocity = Vector2.zero;
            TargetPosition = Vector2.zero;
        }
    }
}
