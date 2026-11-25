using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class PlayerProgression3DPlayTests
    {
        private readonly List<CharacterInfo> _definitions = new();
        private Scene _room;
        private string _savePath;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager().GetComponent<GameManager>().enabled = false;
            _room = SceneManager.CreateScene(nameof(PlayerProgression3DPlayTests));
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (CharacterInfo definition in _definitions) { Object.Destroy(definition); }
            _definitions.Clear();
            TestSceneHelper.Cleanup();
            if (_savePath != null && Directory.Exists(_savePath)) { Directory.Delete(_savePath, true); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Discovery_WhenEnteredTwiceAndRestored_GrantsExperienceAndStatsOnce()
        {
            GameObject floor = NewObject("Survey floor", Vector3.down * 0.25f);
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.AddComponent<BoxCollider>().size = new Vector3(8f, 0.5f, 12f);
            CharacterActor3D knight = CreateActor("Knight", CharacterBelong.Ally, CharacterFeature.Player,
                new Vector3(0f, 0.04f, -4f));
            PlayerProgression3D growth = knight.gameObject.AddComponent<PlayerProgression3D>();
            GameObject location = NewObject("Survey", Vector3.zero);
            ExplorationExperience3D discovery = location.AddComponent<ExplorationExperience3D>();
            SetField(discovery, "rewardId", "test-native-survey");
            SetField(discovery, "experience", 108);
            BoxCollider trigger = location.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up;
            trigger.size = Vector3.one * 2f;
            Rigidbody anchor = location.GetComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.useGravity = false;
            yield return new WaitForFixedUpdate();

            List<(int playerHash, int value)> awarded = new();
            List<(int playerHash, int value)> levelUps = new();
            using IDisposable experience = GameManager.Events.OnExpGained.Subscribe(value => awarded.Add(value));
            using IDisposable levels = GameManager.Events.OnLevelUp.Subscribe(value => levelUps.Add(value));
            SaveManager saves = GameManager.Saves;
            saves.SetSlotData(0, new SaveSlotData(0));
            yield return WalkTo(knight, 0f);

            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(8, growth.CurrentExp);
            Assert.AreEqual(200, growth.ExpForNextLevel);
            Assert.AreEqual(3, growth.AvailablePoints);
            Assert.AreEqual(2, GameManager.Data.GetVitals(knight.Actor.Hash).level);
            CollectionAssert.AreEqual(new[] { (knight.Actor.Hash, 108) }, awarded);
            CollectionAssert.AreEqual(new[] { (knight.Actor.Hash, 2) }, levelUps);

            Assert.IsTrue(growth.TryAllocatePoint(StatType.Vit));
            Assert.AreEqual(110, GameManager.Data.GetVitals(knight.Actor.Hash).maxHp);
            Assert.AreEqual(1, growth.AllocatedStats.vit);
            _savePath = Path.Combine(Path.GetTempPath(), "PlayerGrowth3D-" + Guid.NewGuid().ToString("N"));
            SaveDataStore disk = new SaveDataStore(_savePath);
            Assert.IsTrue(disk.TryWriteToDisk(saves.Save(1)));

            Assert.IsTrue(saves.Load(0));
            Assert.AreEqual(1, growth.Level);
            Assert.AreEqual(100, GameManager.Data.GetVitals(knight.Actor.Hash).maxHp);

            SaveSlotData restored = disk.ReadFromDisk(1);
            Assert.IsNotNull(restored);
            saves.SetSlotData(1, restored);
            Assert.IsTrue(saves.Load(1));
            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(8, growth.CurrentExp);
            Assert.AreEqual(2, growth.AvailablePoints);
            Assert.AreEqual(110, GameManager.Data.GetVitals(knight.Actor.Hash).maxHp);
            yield return WalkTo(knight, -4f);
            yield return WalkTo(knight, 0f);
            Assert.AreEqual(8, growth.CurrentExp);
            Assert.That(awarded, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DefeatExperience_WhenDamageResolves_CreditsOnlyThePartyAndCurrentDefeatedGeneration()
        {
            CharacterActor3D knight = CreateActor("Knight", CharacterBelong.Ally, CharacterFeature.Player, Vector3.zero);
            PlayerProgression3D growth = knight.gameObject.AddComponent<PlayerProgression3D>();
            CharacterActor3D sister = CreateActor("Sister", CharacterBelong.Ally, CharacterFeature.Companion, Vector3.left * 2f);
            SetField(growth, "companion", sister);
            CharacterActor3D outsider = CreateActor("Unaffiliated ally", CharacterBelong.Ally, CharacterFeature.Minion, Vector3.right * 2f);
            CharacterActor3D target = CreateActor("Sentinel", CharacterBelong.Enemy, CharacterFeature.Boss, Vector3.forward * 2f);
            DefeatExperience3D reward = target.gameObject.AddComponent<DefeatExperience3D>();
            SetField(reward, "experience", 25);
            CharacterActor3D separateTarget = CreateActor("Separate target", CharacterBelong.Enemy, CharacterFeature.Minion, Vector3.forward * 4f);
            DefeatExperience3D separateReward = separateTarget.gameObject.AddComponent<DefeatExperience3D>();
            SetField(separateReward, "experience", 40);
            yield return null;

            ActorRef defeated = target.Actor;
            GameManager.Events.FireCharacterDeath(defeated, knight.Actor);
            Assert.AreEqual(0, growth.CurrentExp, "An alive actor has no defeat reward.");
            Assert.IsTrue(Strike(outsider, separateTarget).isKill);
            GameManager.Events.FireCharacterDeath(separateTarget.Actor, knight.Actor);
            Assert.AreEqual(0, growth.CurrentExp, "An unaffiliated kill gives no player experience.");

            Assert.IsTrue(Strike(sister, target).isKill);
            GameManager.Events.FireCharacterDeath(defeated, knight.Actor);
            Assert.AreEqual(25, growth.CurrentExp);

            target.gameObject.SetActive(false);
            target.gameObject.SetActive(true);
            Assert.AreNotEqual(defeated, target.Actor);
            GameManager.Events.FireCharacterDeath(defeated, knight.Actor);
            Assert.AreEqual(25, growth.CurrentExp);
            Assert.IsTrue(Strike(knight, target).isKill);
            Assert.AreEqual(50, growth.CurrentExp);
        }

        private CharacterActor3D CreateActor(string title, CharacterBelong belong, CharacterFeature feature, Vector3 position)
        {
            GameObject owner = NewObject(title, position);
            owner.SetActive(false);
            if (feature == CharacterFeature.Player) { owner.layer = LayerMask.NameToLayer("Player"); }
            CharacterController body = owner.AddComponent<CharacterController>();
            body.height = 1.6f;
            body.center = Vector3.up * 0.8f;
            body.radius = 0.3f;
            CharacterInfo definition = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _definitions.Add(definition);
            CharacterActor3D actor = owner.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(definition);
            owner.SetActive(true);
            return actor;
        }

        private GameObject NewObject(string title, Vector3 position)
        {
            GameObject root = new GameObject(title);
            SceneManager.MoveGameObjectToScene(root, _room);
            root.transform.position = position;
            return root;
        }

        private static IEnumerator WalkTo(CharacterActor3D actor, float destinationZ)
        {
            float direction = Mathf.Sign(destinationZ - actor.transform.position.z);
            for (int step = 0; step < 100 && direction * (destinationZ - actor.transform.position.z) > 0.1f; step++)
            {
                actor.Motor.Simulate(Vector3.forward * direction, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(actor.transform.position.z, Is.EqualTo(destinationZ).Within(0.12f));
            yield return new WaitForFixedUpdate();
        }

        private static DamageResult Strike(CharacterActor3D source, CharacterActor3D defender)
        {
            AttackMotionData motion = new AttackMotionData { attackElement = Element.Slash, motionValue = 100f };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(source.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext captured));
            DamageData hit = CombatDataHelper.BuildDamageData(source.ObjectHash, defender.ObjectHash, motion, captured.AttackStats);
            captured.ApplyTo(ref hit, defender.Actor);
            return defender.GetComponent<DamageReceiver>().ReceiveDamage3D(hit, source.transform.position);
        }

        private static void SetField(object owner, string field, object value) => owner.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    }
}
