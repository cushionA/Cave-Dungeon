using System;
using UnityEngine;

namespace Game.Core
{
    public class Projectile
    {
        public int CasterHash { get; private set; }
        public ActorRef CasterRef { get; private set; }
        public ActorRef TargetRef { get; private set; }
        public int RemainingHits { get; private set; }
        public BulletProfile Profile { get; private set; }
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
            CasterRef = default;
            TargetRef = default;
            _targetHash = 0;
            CasterHash = casterHash;
            Profile = profile;
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
            return actors != null && actors.IsCurrent(target) && target != CasterRef
                && (!TargetRef.IsValid || target.Hash != TargetRef.Hash || target == TargetRef);
        }

        public void Tick(float deltaTime)
        {
            if (!IsAlive)
            {
                return;
            }

            // スポーン遅延中は ElapsedTime を進めず寿命を消費しない。
            if (SpawnDelayRemaining > 0f)
            {
                SpawnDelayRemaining -= deltaTime;
                if (SpawnDelayRemaining <= 0f)
                {
                    SpawnDelayRemaining = 0f;
                    // 遅延終了時点でElapsedTime=0から寿命カウント開始
                    ElapsedTime = 0f;
                }
                return;
            }

            ElapsedTime += deltaTime;
            if (Profile.lifeTime > 0f && ElapsedTime >= Profile.lifeTime)
            {
                Kill();
                return;
            }
        }

        /// <summary>現在のスケールを BulletProfile から算出する。scaleTime<=0 なら 1f。</summary>
        public float GetCurrentScale()
        {
            BulletProfile p = Profile;
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
            BulletProfile p = Profile;
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

            RemainingHits--;
            bool hasPierce = (Profile.features & BulletFeature.Pierce) != 0;

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
