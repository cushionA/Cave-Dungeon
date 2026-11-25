using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class BossEncounter3DPlayTests
    {
        private const string k_BossId = "bound-sentinel";
        private const string k_AidSpecies = "sentinel-aid";
        private const string k_Scout = "scout";
        private static readonly ExpeditionSquad.Selection[] s_OneScout = { new ExpeditionSquad.Selection(k_Scout, 1) };

        private readonly List<ScriptableObject> _assets = new();
        private Scene _room;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _room = SceneManager.CreateScene(nameof(BossEncounter3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));

            ExpeditionSquadRules rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            _assets.Add(rules);
            SetField(rules, "maxCount", 1);
            SetField(rules, "maxCost", 1);
            SetField(rules, "species", new[] { new ExpeditionSquad.Species(k_Scout, 1), new ExpeditionSquad.Species(k_AidSpecies, 1) });
            GameObject world = new GameObject("Boss test world");
            world.SetActive(false);
            ExpeditionSquadManager squad = world.AddComponent<ExpeditionSquadManager>();
            SetField(squad, "rules", rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossDefeat_AfterRealDamage_AdvancesPhaseAndGrantsAidOnlyInAnEligibleCycle()
        {
            CharacterActor3D knight = CreateActor("Knight", CharacterBelong.Ally, CharacterFeature.Player, Vector3.back * 4f);
            CharacterActor3D boss = CreateBoss();
            BossEncounter3D encounter = CreateEncounter(knight, boss);
            List<ChallengeState> endings = new();
            encounter.Ended += endings.Add;
            yield return null;

            Assert.IsTrue(encounter.IsBossBattle);
            Assert.IsFalse(encounter.IsGroupBattle);
            Assert.IsTrue(encounter.TryStartEncounter());
            Assert.IsFalse(encounter.TryStartEncounter());
            Assert.AreEqual(0, encounter.CurrentPhase);
            encounter.CompleteEncounter();
            Assert.IsTrue(encounter.IsRunning, "Explicit completion still needs a confirmed boss death.");

            DamageResult wound = Strike(knight, boss, motionValue: 9f);
            Assert.IsFalse(wound.isKill);
            Assert.That(wound.totalDamage, Is.GreaterThanOrEqualTo(50));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, encounter.CurrentPhase);

            Assert.IsTrue(Strike(knight, boss).isKill);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, encounter.Result.state);
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked(k_AidSpecies));
            CollectionAssert.AreEqual(new[] { ChallengeState.Completed }, endings);

            Assert.IsTrue(GameManager.Playthrough.TryBeginNextPlaythrough(1));
            Recycle(boss);
            Assert.IsTrue(encounter.TryStartEncounter());
            Assert.AreEqual(0, encounter.CurrentPhase);
            ActorRef defeated = boss.Actor;
            Assert.IsTrue(Strike(knight, boss).isKill);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_AidSpecies));
            GameManager.Events.FireCharacterDeath(defeated, knight.Actor);
            encounter.CompleteEncounter();
            CollectionAssert.AreEqual(new[] { ChallengeState.Completed, ChallengeState.Completed }, endings);
            Assert.IsTrue(GameManager.GetSubManager<ExpeditionSquadManager>().Squad.TryConfirm(
                new[] { new ExpeditionSquad.Selection(k_AidSpecies, 1) }));
        }

        [UnityTest]
        public IEnumerator BossDefeat_OutsideTheCurrentAttemptOrGeneration_LeavesAidLocked()
        {
            CharacterActor3D knight = CreateActor("Knight", CharacterBelong.Ally, CharacterFeature.Player, Vector3.back * 4f);
            CharacterActor3D boss = CreateBoss();
            CharacterActor3D guard = CreateActor("Guard", CharacterBelong.Enemy, CharacterFeature.Minion, Vector3.right * 4f);
            BossEncounter3D encounter = CreateEncounter(knight, boss);
            List<ChallengeState> endings = new();
            encounter.Ended += endings.Add;
            Assert.IsTrue(GameManager.Playthrough.TryBeginNextPlaythrough(1));
            GameManager.Saves.Save(0);
            yield return null;

            Assert.IsTrue(Strike(knight, boss).isKill);
            Assert.IsFalse(encounter.TryStartEncounter());
            Assert.IsEmpty(endings);

            Recycle(boss);
            Assert.IsTrue(encounter.TryStartEncounter());
            GameManager.Events.FireCharacterDeath(boss.Actor, knight.Actor);
            Assert.IsTrue(encounter.IsRunning, "A death notification for the current but living boss does not end the encounter.");
            Assert.IsTrue(Strike(knight, guard).isKill);
            Assert.IsTrue(encounter.IsRunning);
            encounter.AbortEncounter();
            Assert.IsTrue(Strike(knight, boss).isKill);
            CollectionAssert.AreEqual(new[] { ChallengeState.Failed }, endings);

            Recycle(boss);
            Assert.IsTrue(encounter.TryStartEncounter());
            ActorRef previous = Recycle(boss);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(encounter.IsRunning);
            Assert.IsTrue(encounter.TryStartEncounter());
            GameManager.Events.FireCharacterDeath(previous, knight.Actor);
            Assert.IsTrue(encounter.IsRunning);
            Assert.IsFalse(GameManager.Saves.Load(2));
            Assert.IsTrue(encounter.IsRunning);
            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.IsFalse(encounter.IsRunning);
            Assert.IsTrue(Strike(knight, boss).isKill);
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked(k_AidSpecies));
            CollectionAssert.AreEqual(new[] { ChallengeState.Failed, ChallengeState.Failed, ChallengeState.Failed }, endings);
        }

        [UnityTest]
        public IEnumerator GroupBossDefeat_ByAnActiveReinforcement_ClosesItsBudgetAndKeepsTheNormalSquad()
        {
            Assert.IsTrue(GameManager.ExpeditionAid.TryComplete("scout.helped",
                new[] { new ExpeditionAidUnlocks.Completion("scout.helped", k_Scout) }));
            ExpeditionSquad squad = GameManager.GetSubManager<ExpeditionSquadManager>().Squad;
            Assert.IsTrue(squad.TryConfirm(s_OneScout));
            Assert.IsTrue(GameManager.Playthrough.TryBeginNextPlaythrough(1));

            CharacterActor3D knight = CreateActor("Knight", CharacterBelong.Ally, CharacterFeature.Player, Vector3.back * 4f);
            CharacterActor3D boss = CreateBoss();
            BossEncounter3D encounter = CreateEncounter(knight, boss, EncounterTraits.Boss | EncounterTraits.GroupBattle);
            ExpeditionSummonDeployment3D deployment = CreateDeployment();
            GameObject budgetRoot = At("Reinforcement budget", Vector3.zero);
            budgetRoot.SetActive(false);
            ExpeditionEncounterBudget3D budget = budgetRoot.AddComponent<ExpeditionEncounterBudget3D>();
            budget.ConfigureForTest(encounter, deployment, 1, 1);
            budgetRoot.SetActive(true);
            yield return null;

            Assert.IsTrue(deployment.TryDeployConfirmed());
            Assert.IsFalse(budget.IsOpen);
            Assert.IsTrue(encounter.TryStartEncounter());
            Assert.IsTrue(budget.IsOpen);
            Assert.IsTrue(budget.TryDeployReinforcements(s_OneScout));
            List<ExpeditionSummonDeployment3D.DeployedSummon> summons = new();
            deployment.CopyActiveSummons(summons);
            Assert.That(summons, Has.Count.EqualTo(2));
            Assert.IsFalse(summons[0].IsReinforcement);
            Assert.IsTrue(summons[1].IsReinforcement);
            ActorRef reinforcement = summons[1].Actor;
            CharacterActor3D source = GameManager.Data.GetManaged(reinforcement.Hash) as CharacterActor3D;
            Assert.IsNotNull(source);

            DamageResult fatalHit = Strike(source, boss);
            Assert.IsTrue(fatalHit.isKill);
            Assert.AreEqual(reinforcement, fatalHit.attackerRef);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(ChallengeState.Completed, encounter.Result.state);
            Assert.IsFalse(budget.IsOpen);
            Assert.AreEqual(0, budget.AdditionalCountLimit);
            Assert.AreEqual(1, deployment.ActiveCount);
            Assert.IsFalse(GameManager.Actors.IsCurrent(reinforcement));
            Assert.IsTrue(GameManager.Actors.IsCurrent(summons[0].Actor));
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked(k_AidSpecies));
        }

        private BossEncounter3D CreateEncounter(CharacterActor3D knight, CharacterActor3D boss,
            EncounterTraits traits = EncounterTraits.Boss)
        {
            ChallengeDefinition challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _assets.Add(challenge);
            challenge.SetForTest(k_BossId, "Sentinel encounter", ChallengeType.BossRush);
            BossEncounter3D encounter = At("Boss encounter", Vector3.zero).AddComponent<BossEncounter3D>();
            encounter.ConfigureForTest(knight, challenge, traits);
            BossPhaseData[] phases =
            {
                new BossPhaseData
                {
                    phaseName = "Guard",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.HpThreshold, threshold = 0.5f },
                },
                new BossPhaseData { phaseName = "Expose" },
            };
            encounter.ConfigureBossForTest(boss, k_BossId, phases,
                new[] { new ExpeditionAidUnlocks.BossRule(k_BossId, k_AidSpecies, 2) });
            return encounter;
        }

        private ExpeditionSummonDeployment3D CreateDeployment()
        {
            CharacterActor3D sister = CreateActor("Sister", CharacterBelong.Ally, CharacterFeature.Companion, new Vector3(4f, 0f, -4f));
            CharacterActor3D template = CreateActor("Scout prefab", CharacterBelong.Ally, CharacterFeature.Summon, Vector3.zero, activate: false);
            ExpeditionSummonDeployment3D deployment = sister.gameObject.AddComponent<ExpeditionSummonDeployment3D>();
            Transform[] points =
            {
                At("Normal summon point", new Vector3(-2f, 0f, -1f)).transform,
                At("Reinforcement point", new Vector3(2f, 0f, -1f)).transform,
            };
            deployment.ConfigureForTest(new[] { new ExpeditionSummonDeployment3D.SpeciesPrefab(k_Scout, template) },
                points, Physics.DefaultRaycastLayers);
            return deployment;
        }

        private CharacterActor3D CreateBoss()
        {
            CharacterActor3D boss = CreateActor("Sentinel", CharacterBelong.Enemy, CharacterFeature.Boss, Vector3.forward * 4f);
            boss.transform.rotation = Quaternion.LookRotation(Vector3.back);
            return boss;
        }

        private CharacterActor3D CreateActor(string name, CharacterBelong belong, CharacterFeature feature, Vector3 position,
            bool activate = true)
        {
            GameObject body = At(name, position);
            body.SetActive(false);
            CharacterController collider = body.AddComponent<CharacterController>();
            collider.center = Vector3.up * 0.8f;
            collider.height = 1.6f;
            collider.radius = 0.3f;
            CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _assets.Add(character);
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(character);
            body.SetActive(activate);
            return actor;
        }

        private GameObject At(string name, Vector3 position)
        {
            GameObject instance = new GameObject(name);
            SceneManager.MoveGameObjectToScene(instance, _room);
            instance.transform.position = position;
            return instance;
        }

        private static ActorRef Recycle(CharacterActor3D actor)
        {
            ActorRef previous = actor.Actor;
            actor.gameObject.SetActive(false);
            actor.gameObject.SetActive(true);
            Assert.IsTrue(actor.Actor.IsValid);
            Assert.AreEqual(previous.Hash, actor.Actor.Hash);
            Assert.AreNotEqual(previous, actor.Actor);
            return previous;
        }

        private static DamageResult Strike(CharacterActor3D source, CharacterActor3D target, float motionValue = 100f)
        {
            AttackMotionData motion = new AttackMotionData { motionValue = motionValue, attackElement = Element.Slash };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(source.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext context));
            DamageData damage = CombatDataHelper.BuildDamageData(
                source.ObjectHash, target.ObjectHash, motion, context.AttackStats);
            context.ApplyTo(ref damage, target.Actor);
            return target.GetComponent<DamageReceiver>().ReceiveDamage3D(damage, source.transform.position);
        }

        private static void SetField(Object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
