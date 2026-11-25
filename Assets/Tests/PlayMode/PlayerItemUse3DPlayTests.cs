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
    public sealed class PlayerItemUse3DPlayTests
    {
        private const int k_ActorLayer = 8;
        private const int k_PotionId = 500;
        private const int k_FireItemId = 501;
        private const int k_AlternatePotionId = 502;
        private const float k_Windup = 0.2f;
        private const float k_EffectLifetime = 0.08f;
        private static readonly Vector3 s_EffectOffset = new Vector3(0.18f, 1.1f, 0.25f);
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly List<DamageResult> _hits = new List<DamageResult>();
        private Scene _room;
        private GameEvents _events;

        private ActionExecutionSystem Execution => GameManager.Actions.Execution;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager().GetComponent<GameManager>().enabled = false;
            _room = SceneManager.CreateScene(nameof(PlayerItemUse3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _events = GameManager.Events;
            _events.OnDamageDealtEvent += CaptureHit;
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_events != null) { _events.OnDamageDealtEvent -= CaptureHit; }
            _hits.Clear();
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecoveryItem_EntersActive_ConsumesOnceAndClampsScaledHealingWithAnEffect()
        {
            GameObject effectPrefab = CreateEffect("Potion effect");
            ConsumableDefinition potion = CreateItem(k_PotionId, CreateAction(k_Windup), 12, StatType.Mnd, 2.25f, effectPrefab);
            ConsumableDefinition alternate = CreateItem(k_AlternatePotionId, CreateAction(k_Windup), 2, StatType.Mnd, 0f, effectPrefab);
            SetField(alternate, "consumeOnUse", false);
            SetField(alternate, "mpCost", 7);
            PlayerItemUse3D use = CreateUser(potion, alternate);
            CharacterActor3D knight = use.GetComponent<CharacterActor3D>();
            GameManager.Equipment.Equip(knight.Actor.Hash, CreateGear(EquipSlot.Core,
                new EquipmentStatModifiers { attributes = new StatModifier { mnd = 3 } }));
            Assert.AreEqual(7, GameManager.Data.GetAttributes(knight.Actor.Hash).mnd);
            EquipItem(k_PotionId, count: 2);
            Assert.AreEqual(1, GameManager.Inventory.Add(k_AlternatePotionId, ItemCategory.Consumable, 1, maxStack: 8));
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(1, k_AlternatePotionId));
            Hp(knight) = 55;
            yield return null;

            Assert.IsTrue(use.TryUseActive(out long action));
            Assert.IsFalse(use.TryUseActive(out _));
            AssertPhase(action, AnimationPhase.Anticipation);
            Execution.Tick(0.1f);
            Assert.AreEqual(55, Hp(knight));
            Assert.AreEqual(2, Stock(k_PotionId));
            Assert.IsNull(FindEffect(effectPrefab));
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(1));

            Execution.Tick(0.15f);
            AssertPhase(action, AnimationPhase.Active);
            Assert.AreEqual(83, Hp(knight), "12 + (4 base MND + 3 equipment MND) × 2.25 rounds to 28.");
            Assert.AreEqual(1, Stock(k_PotionId));
            Assert.AreEqual(1, Stock(k_AlternatePotionId), "An accepted use keeps the item selected at its start.");
            GameObject effect = FindEffect(effectPrefab);
            Assert.IsNotNull(effect);
            Assert.AreEqual(_room, effect.scene);
            Assert.That(Vector3.Distance(effect.transform.position, knight.transform.TransformPoint(s_EffectOffset)), Is.LessThan(0.001f));
            Assert.IsNotNull(effect.GetComponent<MeshRenderer>());

            Execution.Tick(0.05f);
            Assert.AreSame(effect, FindEffect(effectPrefab));
            Assert.AreEqual(83, Hp(knight));
            Assert.AreEqual(1, Stock(k_PotionId));
            yield return new WaitForSeconds(0.16f);
            Assert.IsTrue(effect == null, "The transient effect is destroyed after its authored lifetime.");
            Execution.Tick(0.5f);

            Hp(knight) = 95;
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(0));
            Assert.IsTrue(use.TryUseActive(out _));
            Execution.Tick(k_Windup);
            Assert.AreEqual(100, Hp(knight));
            Assert.AreEqual(0, Stock(k_PotionId));
            Execution.Tick(0.5f);
            Assert.IsFalse(use.TryUseActive(out _));

            Hp(knight) = 40;
            Mp(knight) = 7f;
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(1));
            Assert.IsTrue(use.TryUseActive(out _));
            Assert.AreEqual(7f, Mp(knight));
            Execution.Tick(k_Windup);
            Assert.AreEqual(42, Hp(knight));
            Assert.AreEqual(0f, Mp(knight));
            Assert.AreEqual(1, Stock(k_AlternatePotionId));

            Execution.Tick(0.5f);
            Assert.AreEqual(1, GameManager.Inventory.Remove(k_AlternatePotionId, 1));
            Mp(knight) = 7f;
            Assert.IsFalse(use.TryUseActive(out _), "MP-only use still requires owning the item.");
            Assert.AreEqual(7f, Mp(knight));
        }

        [UnityTest]
        public IEnumerator AttackItem_WithZeroWindup_HitsOnceWithItsOwnElementAndStatusInsteadOfWeaponBonuses()
        {
            GameObject effectPrefab = CreateEffect("Fire item effect");
            AttackInfo attack = CreateAttack();
            attack.statusEffects = new[] { BuildUp(StatusEffectId.Burn, 40f) };
            ConsumableDefinition fire = CreateItem(k_FireItemId, attack, 10, StatType.Intel, 2.2f, effectPrefab);
            SetField(fire, "mpCost", 7);
            PlayerItemUse3D use = CreateUser(fire);
            CharacterActor3D knight = use.GetComponent<CharacterActor3D>();
            CharacterActor3D target = CreateActor("Hostile with fire defense", CharacterBelong.Enemy, new Vector3(0f, 0f, 1.55f),
                new ElementalStatus { fire = 25 });
            CharacterActor3D ally = CreateActor("Nearer ally", CharacterBelong.Ally, new Vector3(-0.6f, 0f, 0.8f));
            CharacterActor3D far = CreateActor("Outside volume", CharacterBelong.Enemy, new Vector3(0f, 0f, 4f));
            target.transform.rotation = Quaternion.LookRotation(Vector3.back);
            GameManager.Equipment.Equip(knight.Actor.Hash, CreateGear(EquipSlot.Weapon,
                new EquipmentStatModifiers
                {
                    attackBonus = new ElementalStatus { fire = 200, slash = 200 },
                    attributes = new StatModifier { intel = 4 },
                }, new[] { BuildUp(StatusEffectId.Poison, 90f) }));
            Assert.AreEqual(7, GameManager.Data.GetAttributes(knight.Actor.Hash).intel);
            Assert.AreEqual(200, GameManager.Data.GetCombatStats(knight.Actor.Hash).attack.fire);
            EquipItem(k_FireItemId, count: 1);
            Mp(knight) = 7f;
            yield return null;
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);

            Assert.IsTrue(use.TryUseActive(out long action));
            AssertPhase(action, AnimationPhase.Active);
            Assert.AreEqual(0, Stock(k_FireItemId));
            Assert.AreEqual(0f, Mp(knight));
            Assert.That(_hits, Has.Count.EqualTo(1));
            DamageResult result = _hits[0];
            Assert.AreEqual(action, result.actionInstanceId);
            Assert.AreEqual(target.Actor, result.defenderRef);
            Assert.AreEqual(12, result.totalDamage, "Rounded item power is 25, then the target's 25 fire defense reduces it.");
            Assert.AreEqual(88, Hp(target));
            Assert.AreEqual(100, Hp(ally));
            Assert.AreEqual(100, Hp(far));
            Assert.AreEqual(40f, GameManager.StatusEffects.Store.GetAccumulation(target.Actor.Hash, StatusEffectId.Burn));
            Assert.AreEqual(0f, GameManager.StatusEffects.Store.GetAccumulation(target.Actor.Hash, StatusEffectId.Poison));
            Assert.IsNotNull(FindEffect(effectPrefab));

            Execution.Tick(0.5f);
            Assert.IsFalse(use.TryUseActive(out _));
            Assert.AreEqual(0, Stock(k_FireItemId));
            Assert.That(_hits, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ItemUse_WithMpAndStockCost_RechecksAtActiveAndOnlyChargesForTheEffect()
        {
            const int cost = 12;
            GameObject effect = CreateEffect("MP draught effect");
            ConsumableDefinition potion = CreateItem(k_PotionId, CreateAction(k_Windup), 20, StatType.Mnd, 0f, effect);
            SetField(potion, "mpCost", cost);
            PlayerItemUse3D use = CreateUser(potion);
            CharacterActor3D knight = use.GetComponent<CharacterActor3D>();
            EquipItem(k_PotionId, count: 2);
            Hp(knight) = 40;
            Mp(knight) = cost - 1;
            yield return null;

            Assert.IsFalse(use.TryUseActive(out _));
            Assert.AreEqual(cost - 1, Mp(knight));
            Assert.AreEqual(2, Stock(k_PotionId));
            Assert.AreEqual(40, Hp(knight));

            Mp(knight) = cost;
            Assert.IsTrue(use.TryUseActive(out long drained));
            Execution.Tick(0.1f);
            AssertPhase(drained, AnimationPhase.Anticipation);
            Assert.AreEqual(cost, Mp(knight));
            Assert.AreEqual(2, Stock(k_PotionId));
            Mp(knight) = cost - 1;
            Execution.Tick(0.15f);
            Assert.AreEqual(cost - 1, Mp(knight));
            Assert.AreEqual(2, Stock(k_PotionId));
            Assert.AreEqual(40, Hp(knight));
            Assert.IsNull(FindEffect(effect));
            Execution.Tick(0.5f);

            Mp(knight) = cost;
            Assert.IsTrue(use.TryUseActive(out long interrupted));
            Assert.IsTrue(Execution.TryGetGroupId(interrupted, out long group));
            Assert.IsTrue(Execution.CancelGroup(group));
            AssertNoUse(knight, interrupted, effect);
            Assert.AreEqual(cost, Mp(knight));

            Assert.IsTrue(use.TryUseActive(out _));
            Execution.Tick(k_Windup);
            Assert.AreEqual(0f, Mp(knight));
            Assert.AreEqual(1, Stock(k_PotionId));
            Assert.AreEqual(60, Hp(knight));
            Assert.IsNotNull(FindEffect(effect));
            Execution.Tick(0.05f);
            Assert.AreEqual(0f, Mp(knight));
            Assert.AreEqual(1, Stock(k_PotionId));
            Assert.AreEqual(60, Hp(knight));
        }

        [UnityTest]
        public IEnumerator ItemUse_WhenInterruptedLoadedOrReRegisteredDuringWindup_DoesNotConsumeOrApply()
        {
            GameObject effectPrefab = CreateEffect("Interrupted use effect");
            ConsumableDefinition potion = CreateItem(k_PotionId, CreateAction(k_Windup), 20, StatType.Mnd, 0f, effectPrefab);
            PlayerItemUse3D use = CreateUser(potion);
            CharacterActor3D knight = use.GetComponent<CharacterActor3D>();
            EquipItem(k_PotionId, count: 2);
            GameManager.Saves.Save(1);
            Hp(knight) = 40;
            yield return null;

            Assert.IsTrue(use.TryUseActive(out long interrupted));
            Assert.IsTrue(Execution.TryGetGroupId(interrupted, out long group));
            Execution.Tick(0.1f);
            Assert.IsTrue(Execution.CancelGroup(group));
            AssertNoUse(knight, interrupted, effectPrefab);

            Assert.IsTrue(use.TryUseActive(out long loaded));
            Execution.Tick(0.1f);
            Assert.IsFalse(GameManager.Saves.Load(2));
            AssertPhase(loaded, AnimationPhase.Anticipation);
            Assert.IsTrue(GameManager.Saves.Load(1));
            Assert.AreEqual(k_PotionId, GameManager.ConsumableBelt.ActiveItemId);
            AssertNoUse(knight, loaded, effectPrefab);

            Assert.IsTrue(use.TryUseActive(out long otherSlot));
            Execution.Tick(0.1f);
            GameManager.Saves.SetActiveSlot(0);
            AssertNoUse(knight, otherSlot, effectPrefab);

            Assert.IsTrue(use.TryUseActive(out long oldGeneration));
            ActorRef previous = knight.Actor;
            knight.enabled = false;
            knight.enabled = true;
            Assert.AreNotEqual(previous, knight.Actor);
            Hp(knight) = 40;
            AssertNoUse(knight, oldGeneration, effectPrefab);

            Assert.IsTrue(use.TryUseActive(out _));
            Execution.Tick(k_Windup);
            Assert.AreEqual(60, Hp(knight));
            Assert.AreEqual(1, Stock(k_PotionId));
        }

        private void AssertNoUse(CharacterActor3D knight, long action, GameObject effectPrefab)
        {
            Execution.Tick(0.5f);
            Assert.IsFalse(Execution.TryGetInstance(action, out _));
            Assert.AreEqual(40, Hp(knight));
            Assert.AreEqual(2, Stock(k_PotionId));
            Assert.IsNull(FindEffect(effectPrefab));
        }

        private void AssertPhase(long instanceId, AnimationPhase phase)
        {
            Assert.IsTrue(Execution.TryGetInstance(instanceId, out ActionExecution instance));
            Assert.AreEqual(phase, instance.phase);
        }

        private void CaptureHit(DamageResult result, int attacker, int defender) => _hits.Add(result);
        private static int Stock(int itemId) => GameManager.Inventory.GetCount(itemId);
        private static ref int Hp(CharacterActor3D actor) => ref GameManager.Data.GetVitals(actor.Actor.Hash).currentHp;
        private static ref float Mp(CharacterActor3D actor) => ref GameManager.Data.GetVitals(actor.Actor.Hash).currentMp;

        private static void EquipItem(int itemId, int count)
        {
            Assert.AreEqual(count, GameManager.Inventory.Add(itemId, ItemCategory.Consumable, count, maxStack: 8));
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(0, itemId));
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(0));
        }

        private PlayerItemUse3D CreateUser(params ConsumableDefinition[] items)
        {
            CharacterActor3D actor = CreateActor("Knight", CharacterBelong.Ally, Vector3.zero, feature: CharacterFeature.Player, activate: false);
            PlayerItemUse3D use = actor.gameObject.AddComponent<PlayerItemUse3D>();
            SetField(use, "items", items);
            actor.gameObject.SetActive(true);
            return use;
        }

        private CharacterActor3D CreateActor(string name, CharacterBelong belong, Vector3 position, ElementalStatus defense = default,
            CharacterFeature feature = CharacterFeature.Minion, bool activate = true)
        {
            GameObject root = At(name, position);
            root.SetActive(false);
            root.layer = k_ActorLayer;
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = Vector3.up * 0.8f;
            body.height = 1.6f;
            body.radius = 0.3f;
            CharacterActor3D actor = root.AddComponent<CharacterActor3D>();
            CharacterInfo definition = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            definition.baseAttributes = new StatModifier { mnd = 4, intel = 3 };
            definition.baseDefense = defense;
            _assets.Add(definition);
            actor.SetCharacterInfoForTest(definition);
            root.SetActive(activate);
            return actor;
        }

        private ConsumableDefinition CreateItem(int itemId, ActionDefinition action, int power, StatType stat,
            float scale, GameObject effect)
        {
            ConsumableDefinition item = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _assets.Add(item);
            SetField(item, "itemId", itemId);
            SetField(item, "displayName", effect.name);
            SetField(item, "useAction", action);
            SetField(item, "basePower", power);
            SetField(item, "scalingStat", stat);
            SetField(item, "statScale", scale);
            SetField(item, "effectPrefab", effect);
            SetField(item, "effectOffset", s_EffectOffset);
            SetField(item, "effectLifetime", k_EffectLifetime);
            return item;
        }

        private ActionDefinition CreateAction(float windup)
        {
            ActionDefinition action = ScriptableObject.CreateInstance<ActionDefinition>();
            _assets.Add(action);
            action.motion = CreateMotion(windup);
            return action;
        }

        private AttackInfo CreateAttack()
        {
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(attack);
            attack.motion = CreateMotion(0f);
            attack.attackElement = Element.Fire;
            attack.damageMultiplier = 1f;
            attack.equipmentSource = AttackEquipmentSource.Weapon;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.8f, 1.35f),
                halfExtents = new Vector3(1f, 0.8f, 1f),
                targetLayers = 1 << k_ActorLayer,
            };
            return attack;
        }

        private MotionDefinition CreateMotion(float windup)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            _assets.Add(motion);
            motion.info = new MotionInfo { preMotionDuration = windup, activeMotionDuration = 0.2f, recoveryDuration = 0.15f };
            return motion;
        }

        private EquipmentDefinition CreateGear(EquipSlot slot, EquipmentStatModifiers stats, StatusEffectApplication[] onHit = null)
        {
            EquipmentDefinition equipment = ScriptableObject.CreateInstance<EquipmentDefinition>();
            _assets.Add(equipment);
            SetField(equipment, "_slot", slot);
            SetField(equipment, "_statModifiers", stats);
            if (onHit != null) { SetField(equipment, "_onHitStatusEffects", onHit); }
            return equipment;
        }

        private StatusEffectApplication BuildUp(StatusEffectId id, float amount)
        {
            StatusEffectDefinition definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            _assets.Add(definition);
            definition.effect = id;
            return new StatusEffectApplication { definition = definition, mode = StatusEffectApplicationMode.Accumulate, accumulateValue = amount };
        }

        private GameObject CreateEffect(string label)
        {
            GameObject effect = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            effect.name = label;
            SceneManager.MoveGameObjectToScene(effect, _room);
            effect.transform.position = new Vector3(25f, 25f, 25f);
            effect.GetComponent<Collider>().enabled = false;
            return effect;
        }

        private GameObject FindEffect(GameObject prefab)
        {
            GameObject found = null;
            foreach (GameObject root in _room.GetRootGameObjects())
            {
                if (root.name != prefab.name + "(Clone)") { continue; }
                Assert.IsNull(found, "An item use produced duplicate effects.");
                found = root;
            }
            return found;
        }

        private GameObject At(string name, Vector3 position)
        {
            GameObject instance = new GameObject(name);
            SceneManager.MoveGameObjectToScene(instance, _room);
            instance.transform.position = position;
            return instance;
        }

        private static void SetField(Object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
