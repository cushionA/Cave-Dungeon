#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionActionAnimationMockPlayTests
    {
        private const string k_Mock = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const int k_SwordId = 4201;
        private const int k_CrossbowId = 4202;
        private const int k_FlameCircleId = 4222;
        private const int k_DraughtId = 4101;
        private const float k_PoseTolerance = 1f;
        private const float k_ActiveSampleOffset = 0.003f;

        private readonly HashSet<GameObject> _existingVendorSaves = new HashSet<GameObject>();
        private Scene _mock;
        private Gamepad _pad;
        private CharacterActor3D _knight;
        private CharacterInfo _targetInfo;
        private PlayerCombatLoadout3D _loadout;
        private CharacterAnimationController _presentation;
        private ActionExecutionSystem _execution;
        private Transform _rightArm;
        private Transform _leftArm;

        [UnitySetUp]
        public IEnumerator LoadMock()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _existingVendorSaves.Clear();
            _existingVendorSaves.UnionWith(VendorSaveRoots());
            _pad = InputSystem.AddDevice<Gamepad>();

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Mock,
                new LoadSceneParameters(LoadSceneMode.Additive));
            _mock = SceneManager.GetSceneByPath(k_Mock);
            yield return new WaitForFixedUpdate();

            GameManager.Instance.enabled = false; // Input stays live while the test advances exact action times.
            _knight = FindRoot("[PLACEHOLDER] Knight").GetComponent<CharacterActor3D>();
            Assert.IsTrue(_knight.IsAlive);
            _loadout = _knight.GetComponent<PlayerCombatLoadout3D>();
            _presentation = _knight.AnimationController;
            _execution = GameManager.Actions.Execution;
            Assert.IsNotNull(_loadout);
            Assert.IsNotNull(_presentation);
            _rightArm = _knight.transform.Find("Knight pose/Right arm");
            _leftArm = _knight.transform.Find("Knight pose/Left arm");
            Assert.IsNotNull(_rightArm);
            Assert.IsNotNull(_leftArm);
            for (int step = 0; step < 12 && !_knight.Motor.IsGrounded; step++)
            {
                _knight.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime);
            }

            PlayerInput input = _knight.GetComponent<PlayerInput>();
            input.neverAutoSwitchControlSchemes = true;
            input.SwitchCurrentControlScheme("Gamepad", _pad);
            Assert.AreEqual("Player", input.currentActionMap.name);
            Assert.AreEqual(1, input.devices.Count);
            Assert.AreSame(_pad, input.devices[0]);
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator UnloadMock()
        {
            if (_mock.isLoaded) { yield return SceneManager.UnloadSceneAsync(_mock); }
            foreach (GameObject root in VendorSaveRoots())
            {
                if (!_existingVendorSaves.Contains(root)) { Object.Destroy(root); }
            }
            _existingVendorSaves.Clear();
            Object.Destroy(_targetInfo);
            TestSceneHelper.Cleanup();
            yield return null;
            if (_pad != null && _pad.added) { InputSystem.RemoveDevice(_pad); }
            _pad = null;
        }

        [UnityTest]
        public IEnumerator WeaponAnimation_WhenSwappedDuringSwordStrike_FinishesSwordThenFiresAnimatedCrossbow()
        {
            Assert.AreEqual(k_SwordId, _loadout.CurrentWeaponId);
            MotionInfo sword = _loadout.Weapons.Single(option => option.ItemId == k_SwordId).Attack.GetMotion();
            MotionInfo crossbow = _loadout.Weapons.Single(option => option.ItemId == k_CrossbowId).Attack.GetMotion();
            GameObject swordProp = _rightArm.Find("Sword").gameObject;
            GameObject crossbowProp = _rightArm.Find("Crossbow").gameObject;
            Assert.IsTrue(swordProp.activeInHierarchy);
            Assert.IsFalse(crossbowProp.activeInHierarchy);
            Quaternion restRight = _rightArm.localRotation;
            Quaternion restLeft = _leftArm.localRotation;

            _knight.transform.rotation = Quaternion.identity;
            CharacterActor3D target = CreateTarget(_knight.transform.position + Vector3.forward * 1.35f);
            int initialHp = Hp(target);
            Assert.That(sword.activeClip, Is.Not.SameAs(crossbow.activeClip));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);

            yield return Press(GamepadButton.West);
            long strike = AnticipatingInstance();
            yield return PresentAt(strike, sword.preMotionDuration - k_ActiveSampleOffset, AnimationPhase.Anticipation);
            Assert.AreEqual(initialHp, Hp(target));
            yield return PresentAt(strike, sword.preMotionDuration + k_ActiveSampleOffset, AnimationPhase.Active);
            Assert.That(Hp(target), Is.LessThan(initialHp));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            Quaternion swordStrike = _rightArm.localRotation;
            Assert.That(Quaternion.Angle(restRight, swordStrike), Is.GreaterThan(30f));

            Assert.IsTrue(_loadout.TryEquipWeapon(k_CrossbowId));
            Assert.AreEqual(k_CrossbowId, _loadout.CurrentWeaponId);
            GameManager.Actions.Present();
            yield return null;
            Assert.IsTrue(_presentation.HasMotion(strike));
            Assert.That(Quaternion.Angle(swordStrike, _rightArm.localRotation), Is.LessThan(k_PoseTolerance));
            Assert.IsTrue(swordProp.activeInHierarchy);
            Assert.IsFalse(crossbowProp.activeInHierarchy);

            yield return Finish(strike, sword);
            Assert.IsFalse(swordProp.activeInHierarchy);
            Assert.IsTrue(crossbowProp.activeInHierarchy);
            Assert.That(Quaternion.Angle(restRight, _rightArm.localRotation), Is.LessThan(k_PoseTolerance));

            yield return Press(GamepadButton.West);
            long shot = AnticipatingInstance();
            Assert.AreNotEqual(strike, shot);
            yield return PresentAt(shot, crossbow.preMotionDuration - k_ActiveSampleOffset, AnimationPhase.Anticipation);
            Assert.That(Quaternion.Angle(restLeft, _leftArm.localRotation), Is.GreaterThan(25f));
            Quaternion aim = _rightArm.localRotation;
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            yield return PresentAt(shot, crossbow.preMotionDuration + k_ActiveSampleOffset, AnimationPhase.Active);
            Assert.That(Quaternion.Angle(restLeft, _leftArm.localRotation), Is.GreaterThan(25f));
            Assert.That(Quaternion.Angle(aim, _rightArm.localRotation), Is.GreaterThan(10f));
            Assert.That(Quaternion.Angle(swordStrike, _rightArm.localRotation), Is.GreaterThan(30f));
            Assert.AreEqual(1, GameManager.Projectiles3D.ActiveCount);
            yield return Finish(shot, crossbow);
        }

        [UnityTest]
        public IEnumerator WeaponAnimation_WhenCrossbowCancelsSword_ShowsCrossbowDuringTheReplacement()
        {
            Assert.AreEqual(k_SwordId, _loadout.CurrentWeaponId);
            AttackInfo sword = Object.Instantiate(_loadout.ResolveWeaponAttack());
            MotionInfo motion = sword.GetMotion();
            GameObject swordProp = _rightArm.Find("Sword").gameObject;
            GameObject crossbowProp = _rightArm.Find("Crossbow").gameObject;
            try
            {
                sword.cancelAfter = motion.preMotionDuration * 0.5f;
                GameActionRuntime actions = GameManager.Actions;
                Assert.IsTrue(actions.Execute3D(_knight, sword, out long strike));
                yield return PresentAt(strike, motion.preMotionDuration * 0.75f, AnimationPhase.Anticipation);
                Assert.IsTrue(_execution.TryGetInstance(strike, out ActionExecution cancellable));
                Assert.IsTrue(cancellable.canCancel);

                Assert.IsTrue(_loadout.TryEquipWeapon(k_CrossbowId));
                Assert.IsTrue(swordProp.activeInHierarchy);
                Assert.IsFalse(crossbowProp.activeInHierarchy);

                AttackInfo crossbow = _loadout.ResolveWeaponAttack();
                Assert.IsTrue(actions.Execute3D(_knight, crossbow, out long shot));
                Assert.AreEqual(shot, AnticipatingInstance());
                Assert.AreNotEqual(strike, shot);
                Assert.IsFalse(_execution.TryGetInstance(strike, out _));
                Assert.IsFalse(_presentation.HasMotion(strike));
                yield return null;
                Assert.IsTrue(_execution.TryGetInstance(shot, out _));
                Assert.IsTrue(crossbowProp.activeInHierarchy);
                Assert.IsFalse(swordProp.activeInHierarchy);
            }
            finally
            {
                Object.Destroy(sword);
            }
        }

        [UnityTest]
        public IEnumerator SpellAnimation_WhenAssignedFlameCircleIsCast_AnimatesUntilTheGroundHitAndRecovers()
        {
            Assert.IsTrue(_loadout.TryAssignSpell(1, k_FlameCircleId));
            Assert.IsTrue(_loadout.TrySelectSpellSlot(1));
            AttackInfo spell = _loadout.Spells.Single(option => option.ItemId == k_FlameCircleId).Attack;
            MotionInfo motion = spell.GetMotion();
            Quaternion restLeft = _leftArm.localRotation;
            int knightHash = _knight.Actor.Hash;
            float manaBefore = GameManager.Data.GetVitals(knightHash).currentMp;
            Assert.That(manaBefore, Is.GreaterThanOrEqualTo(spell.mpCost));
            Assert.AreSame(spell, _loadout.ResolveSpellAttack());
            Assert.IsTrue(_knight.Motor.IsGrounded);
            CharacterActor3D target = CreateTarget(_knight.Motor.Ground.Point + Vector3.right * 1.3f + Vector3.up * 0.04f);
            for (int step = 0; step < 12; step++) { target.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime); }
            Assert.AreSame(_knight.Motor.Ground.Surface, target.Motor.Ground.Surface);
            int initialHp = Hp(target);

            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            _knight.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime);
            Assert.AreSame(target.Motor.Ground.Surface, _knight.Motor.Ground.Surface);
            yield return Press(GamepadButton.RightShoulder);
            long cast = AnticipatingInstance();
            yield return PresentAt(cast, motion.preMotionDuration - k_ActiveSampleOffset, AnimationPhase.Anticipation);
            Assert.That(Quaternion.Angle(restLeft, _leftArm.localRotation), Is.GreaterThan(20f));
            Quaternion gathered = _leftArm.localRotation;
            Assert.AreEqual(manaBefore - spell.mpCost, GameManager.Data.GetVitals(knightHash).currentMp);
            Assert.AreEqual(initialHp, Hp(target));
            yield return PresentAt(cast, motion.preMotionDuration + k_ActiveSampleOffset, AnimationPhase.Active);
            float releaseToRest = Quaternion.Angle(restLeft, _leftArm.localRotation);
            Assert.That(releaseToRest, Is.GreaterThan(20f));
            Assert.That(Quaternion.Angle(gathered, _leftArm.localRotation), Is.GreaterThan(10f));
            int hpAfterHit = Hp(target);
            Assert.That(hpAfterHit, Is.LessThan(initialHp));
            Assert.AreEqual(0, GameManager.Projectiles3D.ActiveCount);
            yield return PresentAt(cast, motion.preMotionDuration + motion.activeMotionDuration + motion.recoveryDuration * 0.5f,
                AnimationPhase.Recovery);
            Assert.That(Quaternion.Angle(restLeft, _leftArm.localRotation), Is.InRange(5f, releaseToRest - 5f));
            Assert.AreEqual(hpAfterHit, Hp(target));
            yield return Finish(cast, motion);
            Assert.That(Quaternion.Angle(restLeft, _leftArm.localRotation), Is.LessThan(k_PoseTolerance));
        }

        [UnityTest]
        public IEnumerator ItemAnimation_WhenDraughtIsUsedOrInterrupted_AnimatesAndCommitsOnceOnActive()
        {
            PlayerItemUse3D use = _knight.GetComponent<PlayerItemUse3D>();
            ConsumableDefinition draught = use.Items.Single(item => item.ItemId == k_DraughtId);
            NpcInteraction3D supply = FindRoot("Healing draught supplies").GetComponent<NpcInteraction3D>();
            _knight.Body.enabled = false;
            _knight.transform.position = supply.transform.position + Vector3.back * 0.7f + Vector3.up * 0.04f;
            _knight.Body.enabled = true;
            Physics.SyncTransforms();
            Assert.IsTrue(supply.CanInteract(_knight.Actor.Hash));
            supply.Interact(_knight.Actor.Hash);
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(0, k_DraughtId));
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(0));
            int initialStock = GameManager.Inventory.GetConsumableCount(k_DraughtId);
            Assert.That(initialStock, Is.GreaterThanOrEqualTo(2));

            MotionInfo motion = draught.UseAction.GetMotion();
            Transform prop = _knight.transform.Find("Item-use prop");
            Assert.IsNotNull(prop);
            Vector3 restProp = prop.localPosition;
            Quaternion restRight = _rightArm.localRotation;
            int knightHash = _knight.Actor.Hash;
            int heal = ConsumableDefinition.CalculatePower(draught.BasePower, draught.ScalingStat, draught.StatScale,
                GameManager.Data.GetAttributes(knightHash));
            int injuredHp = GameManager.Data.GetVitals(knightHash).maxHp - heal - 5;
            Assert.That(injuredHp, Is.Positive);
            GameManager.Data.GetVitals(knightHash).currentHp = injuredHp;
            GameManager.Data.GetVitals(knightHash).currentArmor = 0f;
            CharacterActor3D enemy = CreateTarget(_knight.transform.position + Vector3.right * 1.3f);

            yield return Press(GamepadButton.East);
            long interrupted = AnticipatingInstance();
            yield return PresentAt(interrupted, motion.preMotionDuration * 0.5f, AnimationPhase.Anticipation);
            Assert.That(prop.localPosition.y, Is.GreaterThan(restProp.y + 0.04f));
            AssertDraught(injuredHp, initialStock);
            Assert.IsFalse(HasEffect(draught));
            DamageResult hit = HitKnight(enemy);
            Assert.AreEqual(enemy.Actor, hit.attackerRef);
            Assert.AreEqual(_knight.Actor, hit.defenderRef);
            Assert.That(hit.totalDamage, Is.Positive);
            Assert.AreEqual(HitReaction.Flinch, hit.hitReaction);
            Assert.AreEqual(ActState.Flinch, GameManager.Data.GetFlags(knightHash).ActState);
            Assert.IsTrue(_knight.IsAlive);
            Assert.IsFalse(_execution.TryGetInstance(interrupted, out _));
            Assert.IsFalse(_presentation.HasMotion(interrupted));
            int hpAfterHit = injuredHp - hit.totalDamage;
            AssertDraught(hpAfterHit, initialStock);

            float timePastEffectAndFlinch = Mathf.Max(HitReactionLogic.k_FlinchDuration, motion.preMotionDuration)
                + k_ActiveSampleOffset;
            _execution.Tick(timePastEffectAndFlinch);
            GameManager.Actions.Present();
            yield return null;
            AssertPresentationEnded(interrupted);
            Assert.That(Quaternion.Angle(restRight, _rightArm.localRotation), Is.LessThan(k_PoseTolerance));
            Assert.That(Vector3.Distance(restProp, prop.localPosition), Is.LessThan(0.01f));
            AssertDraught(hpAfterHit, initialStock);
            Assert.IsFalse(HasEffect(draught));

            yield return Press(GamepadButton.East);
            long taken = AnticipatingInstance();
            yield return PresentAt(taken, motion.preMotionDuration - k_ActiveSampleOffset, AnimationPhase.Anticipation);
            AssertDraught(hpAfterHit, initialStock);
            Quaternion raised = _rightArm.localRotation;
            yield return PresentAt(taken, motion.preMotionDuration + k_ActiveSampleOffset, AnimationPhase.Active);
            Assert.That(Quaternion.Angle(restRight, _rightArm.localRotation), Is.GreaterThan(30f));
            Quaternion activePose = _rightArm.localRotation;
            Assert.That(Quaternion.Angle(raised, activePose), Is.GreaterThan(10f));
            AssertDraught(hpAfterHit + heal, initialStock - 1);
            Assert.IsTrue(HasEffect(draught));
            yield return PresentAt(taken, motion.preMotionDuration + motion.activeMotionDuration + motion.recoveryDuration * 0.5f,
                AnimationPhase.Recovery);
            Assert.That(Quaternion.Angle(activePose, _rightArm.localRotation), Is.GreaterThan(5f));
            AssertDraught(hpAfterHit + heal, initialStock - 1);
            yield return Finish(taken, motion);
            Assert.That(Quaternion.Angle(restRight, _rightArm.localRotation), Is.LessThan(k_PoseTolerance));
            Assert.That(Vector3.Distance(restProp, prop.localPosition), Is.LessThan(0.01f));
            AssertDraught(hpAfterHit + heal, initialStock - 1);
        }

        private IEnumerator Press(GamepadButton button)
        {
            string name = button switch
            {
                GamepadButton.West => "Attack",
                GamepadButton.RightShoulder => "Skill",
                GamepadButton.East => "Crouch",
                _ => throw new System.ArgumentOutOfRangeException(nameof(button)),
            };
            InputAction action = _knight.GetComponent<PlayerInput>().currentActionMap.FindAction(name, throwIfNotFound: true);
            var control = _pad[button];
            int performed = 0;
            void Observe(InputAction.CallbackContext context)
            {
                if (context.control == control) { performed++; }
            }

            action.performed += Observe;
            try
            {
                InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
                for (int frame = 0; frame < 6 && performed == 0; frame++)
                {
                    yield return null;
                    yield return new WaitForFixedUpdate();
                }
                Assert.AreEqual(1, performed, $"The paired gamepad must perform the authored {name} action.");
                yield return new WaitForFixedUpdate();
            }
            finally
            {
                action.performed -= Observe;
                InputSystem.QueueStateEvent(_pad, new GamepadState());
            }
            for (int frame = 0; frame < 6 && control.isPressed; frame++)
            {
                yield return null;
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(control.isPressed);
        }

        private long AnticipatingInstance()
        {
            Assert.IsTrue(_execution.GetPrimary(_knight.Actor.Hash, out ActionExecution action));
            Assert.AreEqual(_knight.Actor, action.ownerRef);
            Assert.AreEqual(AnimationPhase.Anticipation, action.phase);
            return action.instanceId;
        }

        private IEnumerator PresentAt(long instance, float elapsed, AnimationPhase phase)
        {
            Assert.IsTrue(_execution.TryGetInstance(instance, out ActionExecution before));
            Assert.That(elapsed, Is.GreaterThan(before.elapsed));
            _execution.Tick(elapsed - before.elapsed);
            Assert.IsTrue(_execution.TryGetInstance(instance, out ActionExecution current));
            Assert.AreEqual(phase, current.phase);
            GameManager.Actions.Present();
            yield return null;
            Assert.IsTrue(_presentation.HasMotion(instance));
        }

        private IEnumerator Finish(long instance, MotionInfo motion)
        {
            _execution.Tick(motion.preMotionDuration + motion.activeMotionDuration + motion.recoveryDuration);
            GameManager.Actions.Present();
            yield return null;
            AssertPresentationEnded(instance);
        }

        private void AssertPresentationEnded(long instance)
        {
            Assert.IsFalse(_execution.TryGetInstance(instance, out _));
            Assert.IsFalse(_presentation.HasMotion(instance));
            Assert.IsFalse(_presentation.HasActiveGraph);
        }

        private void AssertDraught(int expectedHp, int expectedStock)
        {
            Assert.AreEqual(expectedHp, Hp(_knight));
            Assert.AreEqual(expectedStock, GameManager.Inventory.GetConsumableCount(k_DraughtId));
        }

        private bool HasEffect(ConsumableDefinition item) => _mock.GetRootGameObjects()
            .Any(root => root.name == item.EffectPrefab.name + "(Clone)");

        private DamageResult HitKnight(CharacterActor3D enemy)
        {
            AttackMotionData motion = new AttackMotionData { motionValue = 1f, attackElement = Element.Slash };
            Assert.IsTrue(CombatDataHelper.TryCaptureAttack(enemy.ObjectHash, motion,
                GameManager.Data, GameManager.Actors, out AttackContext context));
            DamageData damage = CombatDataHelper.BuildDamageData(enemy.ObjectHash, _knight.ObjectHash, motion, context.AttackStats);
            context.ApplyTo(ref damage, _knight.Actor);
            return _knight.GetComponent<DamageReceiver>().ReceiveDamage3D(damage, enemy.transform.position);
        }

        private CharacterActor3D CreateTarget(Vector3 position)
        {
            _targetInfo = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Enemy, CharacterFeature.Minion);
            _targetInfo.baseDefense = default;
            GameObject root = new GameObject("Animation test target");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _mock);
            root.layer = LayerMask.NameToLayer("Enemy");
            root.transform.position = position;
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = Vector3.up * 0.8f;
            body.height = 1.6f;
            body.radius = 0.3f;
            CharacterActor3D actor = root.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(_targetInfo);
            root.SetActive(true);
            Physics.SyncTransforms();
            Assert.IsTrue(actor.IsAlive);
            return actor;
        }

        private static int Hp(CharacterActor3D actor) => GameManager.Data.GetVitals(actor.Actor.Hash).currentHp;

        private static IEnumerable<GameObject> VendorSaveRoots()
        {
            System.Type saveSystem = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("PixelCrushers.SaveSystem")).FirstOrDefault(type => type != null);
            if (saveSystem == null) { yield break; }
            foreach (Object save in Resources.FindObjectsOfTypeAll(saveSystem))
            {
                if (save is Component component && component.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    yield return component.transform.root.gameObject;
                }
            }
        }

        private GameObject FindRoot(string name) => _mock.GetRootGameObjects().Single(root => root.name == name);
    }
}
#endif
