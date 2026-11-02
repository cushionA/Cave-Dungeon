using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class StatusDefensePipelinePlayTests
    {
        private readonly List<GameObject> _actors = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _actors.Count - 1; i >= 0; i--)
            {
                if (_actors[i] != null) { Object.Destroy(_actors[i]); }
            }
            _actors.Clear();
            yield return null;
            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private BaseCharacter Actor(CharacterBelong belong, StatusEffectDefenseProfile defense = default)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.baseAttack = new ElementalStatus { slash = 20 };
            info.statusDefense = defense;
            _assets.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info,
                new Vector3(_actors.Count * 10f, 5f, 0f));
            _actors.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private static StatusEffectApplicationData Poison()
        {
            return new StatusEffectApplicationData
            {
                mode = StatusEffectApplicationMode.Accumulate,
                guardRule = StatusEffectGuardRule.Ignore,
                info = new StatusEffectInfo
                { effect = StatusEffectId.Poison, accumulateValue = 100f, duration = 5f },
            };
        }

        [UnityTest]
        public IEnumerator StatusDefense_DamageReceiver_UsesCharacterDefenseForTechniqueAndEquipmentEffects()
        {
            BaseCharacter source = Actor(CharacterBelong.Ally);
            BaseCharacter target = Actor(CharacterBelong.Enemy, new StatusEffectDefenseProfile
            {
                commonReduction = 0.5f,
                poison = new StatusEffectDefense { threshold = 75f },
                burn = new StatusEffectDefense { immune = true },
            });
            yield return null;
            Assert.IsTrue(GameManager.Actors.TryGet(source.ObjectHash, out ActorRef sourceRef));
            Assert.IsTrue(GameManager.Actors.TryGet(target.ObjectHash, out ActorRef targetRef));
            StatusEffectApplicationData[] technique =
            {
                Poison(),
                new StatusEffectApplicationData
                {
                    mode = StatusEffectApplicationMode.Immediate,
                    guardRule = StatusEffectGuardRule.Ignore,
                    info = new StatusEffectInfo { effect = StatusEffectId.Burn, duration = 5f },
                },
            };
            StatusEffectApplicationData[] equipment = { Poison() };
            AttackContext context = AttackContext.Capture(sourceRef,
                GameManager.Data.GetCombatStats(source.ObjectHash).attack, 1f, CharacterBelong.Ally,
                equipment, technique);
            DamageData attack = new DamageData { motionValue = 1f };
            context.ApplyTo(ref attack, targetRef);

            DamageResult result = target.DamageReceiver.ReceiveDamage(attack);

            Assert.Greater(result.totalDamage, 0);
            Assert.IsTrue(GameManager.StatusEffects.Store.TryGetEffect(target.ObjectHash,
                StatusEffectId.Poison, out ActiveEffect poison));
            Assert.AreEqual(sourceRef, poison.sourceRef);
            Assert.AreEqual(targetRef, poison.targetRef);
            Assert.IsFalse(GameManager.StatusEffects.Store.IsActive(target.ObjectHash, StatusEffectId.Burn));
        }

        [UnityTest]
        public IEnumerator StatusDefense_ProjectileSupportEffects_ReevaluateCurrentDefenseForEachApplication()
        {
            BaseCharacter source = Actor(CharacterBelong.Ally);
            BaseCharacter target = Actor(CharacterBelong.Enemy, new StatusEffectDefenseProfile
            { commonReduction = 0.5f, poison = new StatusEffectDefense { threshold = 75f } });
            yield return null;
            Assert.IsTrue(GameManager.Actors.TryGet(source.ObjectHash, out ActorRef sourceRef));
            Assert.IsTrue(GameManager.Actors.TryGet(target.ObjectHash, out ActorRef targetRef));
            StatusEffectApplicationData[] applications = { Poison() };
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Support,
                attackContext = AttackContext.Capture(sourceRef, default, 1f, CharacterBelong.Ally,
                    null, applications),
            };
            Projectile projectile = new Projectile();
            projectile.Initialize(sourceRef, new BulletProfile { lifeTime = 5f, hitLimit = 2 },
                Vector2.zero, Vector2.right);

            ProjectileHitProcessor.ProcessHit(projectile, target.DamageReceiver, GameManager.Data, magic,
                GameManager.Events, GameManager.StatusEffects, GameManager.Actors, targetRef);

            Assert.AreEqual(50f, GameManager.StatusEffects.Store.GetAccumulation(target.ObjectHash, StatusEffectId.Poison));
            Assert.IsFalse(GameManager.StatusEffects.Store.IsActive(target.ObjectHash, StatusEffectId.Poison));
            GameManager.Data.GetCombatStats(target.ObjectHash).statusDefense = new StatusEffectDefenseProfile
            { poison = new StatusEffectDefense { immune = true } };
            ProjectileHitProcessor.ProcessHit(projectile, target.DamageReceiver, GameManager.Data, magic,
                GameManager.Events, GameManager.StatusEffects, GameManager.Actors, targetRef);

            Assert.AreEqual(50f, GameManager.StatusEffects.Store.GetAccumulation(target.ObjectHash, StatusEffectId.Poison));
            Assert.IsFalse(GameManager.StatusEffects.Store.IsActive(target.ObjectHash, StatusEffectId.Poison));
        }
    }
}
