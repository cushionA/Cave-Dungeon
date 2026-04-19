using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 飛翔体ダメージ(ProjectileHitProcessor) + HitboxLogic二重ヒット防止。
    /// 新仕様: ProcessHit(Projectile, IDamageable, ...) で IDamageable 経由。
    /// </summary>
    public class DamageProjectileHitProcessorTests
    {
        private SoACharaDataDic _data;
        private int _casterHash;
        private int _targetHash;
        private int _allyHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _casterHash = 100;
            _targetHash = 200;
            _allyHash = 300;

            CharacterVitals casterVitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100, position = Vector2.zero
            };
            CombatStats casterCombat = new CombatStats
            {
                attack = new ElementalStatus { fire = 50 }
            };
            CharacterFlags casterFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);

            CharacterVitals targetVitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100, currentArmor = 0f, maxArmor = 0f,
                position = new Vector2(5f, 0f)
            };
            CombatStats targetCombat = new CombatStats
            {
                defense = new ElementalStatus { fire = 10 }
            };
            CharacterFlags targetFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);

            CharacterVitals allyVitals = new CharacterVitals
            {
                currentHp = 80, maxHp = 100, position = new Vector2(3f, 0f)
            };
            CharacterFlags allyFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);

            _data.Add(_casterHash, casterVitals, casterCombat, casterFlags, default);
            _data.Add(_targetHash, targetVitals, targetCombat, targetFlags, default);
            _data.Add(_allyHash, allyVitals, default, allyFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        private Projectile CreateProjectile(int casterHash)
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { speed = 10f, lifeTime = 5f, hitLimit = 1 };
            p.Initialize(casterHash, profile, Vector2.zero, Vector2.right);
            return p;
        }

        // --- ProjectileHitProcessor: Attack (IDamageable経由) ---

        [Test]
        public void ProjectileHitProcessor_AttackMagic_DealsDamageViaIDamageable()
        {
            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1.0f,
                attackElement = Element.Fire,
            };
            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, receiver, _data, magic);

            Assert.Greater(result.damage, 0, "飛翔体Attack魔法はダメージを与えるべき");
            Assert.AreEqual(MagicType.Attack, result.magicType);

            ref CharacterVitals v = ref _data.GetVitals(_targetHash);
            Assert.Less(v.currentHp, 100, "ターゲットHPが減少するべき");
        }

        [Test]
        public void ProjectileHitProcessor_AttackMagic_PassesIsProjectileTrue()
        {
            // IDamageable.ReceiveDamage が受け取る DamageData.isProjectile が true であることを検証
            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1.0f,
                attackElement = Element.Fire,
            };
            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);

            ProjectileHitProcessor.ProcessHit(projectile, receiver, _data, magic);

            Assert.IsTrue(receiver.LastReceived.isProjectile,
                "DamageData.isProjectile=true がDamageReceiverに伝わる");
            Assert.AreEqual(_casterHash, receiver.LastReceived.attackerHash);
            Assert.AreEqual(_targetHash, receiver.LastReceived.defenderHash);
        }

        [Test]
        public void ProjectileHitProcessor_AttackMagic_KillsTarget_WhenHpReachesZero()
        {
            ref CharacterVitals v = ref _data.GetVitals(_targetHash);
            v.currentHp = 1;

            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 2.0f,
                attackElement = Element.Fire,
            };
            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, receiver, _data, magic);

            Assert.IsTrue(result.isKill, "HP1のターゲットにダメージ→キル判定");
            ref CharacterVitals after = ref _data.GetVitals(_targetHash);
            Assert.AreEqual(0, after.currentHp, "HPは0にクランプされるべき");
        }

        [Test]
        public void ProjectileHitProcessor_NullReceiver_ReturnsZeroDamage()
        {
            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack, motionValue = 1.0f
            };

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, null, _data, magic);

            Assert.AreEqual(0, result.damage, "receiver=null時はダメージ0");
        }

        [Test]
        public void ProjectileHitProcessor_ReturnValue_IsCopiedFromDamageResult()
        {
            // Stubで返り値を制御し、ProjectileHitProcessorがDamageResultの totalDamage/isKill を転写することを検証
            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack, motionValue = 1.0f
            };
            StubDamageable stub = new StubDamageable
            {
                ObjectHash = _targetHash,
                ReturnValue = new DamageResult { totalDamage = 42, isKill = true }
            };

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, stub, _data, magic);

            Assert.AreEqual(42, result.damage);
            Assert.IsTrue(result.isKill);
            Assert.AreEqual(1, stub.ReceiveCount, "ReceiveDamageが1回呼ばれる");
        }

        // --- ProjectileHitProcessor: Recover ---

        [Test]
        public void ProjectileHitProcessor_RecoverMagic_HealsTarget()
        {
            ref CharacterVitals v = ref _data.GetVitals(_targetHash);
            v.currentHp = 50;

            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Recover,
                healAmount = 30,
            };
            // Recover は ObjectHash だけ使用。IDamageable の実装は何でもOK
            StubDamageable stub = new StubDamageable { ObjectHash = _targetHash };

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, stub, _data, magic);

            Assert.AreEqual(30, result.healAmount);
            ref CharacterVitals after = ref _data.GetVitals(_targetHash);
            Assert.AreEqual(80, after.currentHp, "HP50+回復30=HP80");
        }

        [Test]
        public void ProjectileHitProcessor_RecoverMagic_ClampsToMaxHp()
        {
            ref CharacterVitals v = ref _data.GetVitals(_targetHash);
            v.currentHp = 90;

            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Recover,
                healAmount = 50,
            };
            StubDamageable stub = new StubDamageable { ObjectHash = _targetHash };

            ProjectileHitProcessor.ProcessHit(projectile, stub, _data, magic);

            ref CharacterVitals after = ref _data.GetVitals(_targetHash);
            Assert.AreEqual(100, after.currentHp, "maxHPを超えない");
        }

        // --- ProjectileHitProcessor: Invalid hashes ---

        [Test]
        public void ProjectileHitProcessor_InvalidCasterHash_ReturnsZeroDamage()
        {
            Projectile projectile = CreateProjectile(9999);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack, motionValue = 1.0f,
            };
            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, receiver, _data, magic);

            Assert.AreEqual(0, result.damage, "無効なキャスターハッシュではダメージ0");
        }

        [Test]
        public void ProjectileHitProcessor_InvalidTargetHash_ReturnsZeroDamage()
        {
            Projectile projectile = CreateProjectile(_casterHash);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack, motionValue = 1.0f,
            };
            StubDamageable stub = new StubDamageable { ObjectHash = 9999 };

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(projectile, stub, _data, magic);

            Assert.AreEqual(0, result.damage, "無効なターゲットハッシュではダメージ0");
        }

        // --- HitboxLogic: 二重ヒット防止 ---

        [Test]
        public void HitboxLogic_TryRegisterHit_PreventsDuplicateHit()
        {
            HitboxLogic logic = new HitboxLogic(5);

            Assert.IsTrue(logic.TryRegisterHit(100), "初回ヒットは成功");
            Assert.IsFalse(logic.TryRegisterHit(100), "同一ターゲット二重ヒットは拒否");
            Assert.AreEqual(1, logic.HitCount, "ヒット数は1のまま");
        }

        [Test]
        public void HitboxLogic_TryRegisterHit_RespectsMaxHitCount()
        {
            HitboxLogic logic = new HitboxLogic(2);

            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsTrue(logic.TryRegisterHit(200));
            Assert.IsFalse(logic.TryRegisterHit(300), "上限到達後は新ターゲットもfalse");
            Assert.IsTrue(logic.IsExhausted);
        }

        [Test]
        public void HitboxLogic_Reset_ClearsStateForNewAttack()
        {
            HitboxLogic logic = new HitboxLogic(1);
            logic.TryRegisterHit(100);
            Assert.IsTrue(logic.IsExhausted);

            logic.Reset(3);

            Assert.IsFalse(logic.IsExhausted);
            Assert.AreEqual(0, logic.HitCount);
            Assert.IsTrue(logic.TryRegisterHit(100), "リセット後は同一ターゲットに再ヒット可能");
        }
    }
}
