using Game.Core;
using NUnit.Framework;
using R3;
using UnityEditor;
using UnityEngine;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.EditMode
{
    public class Integration_CharacterRegistrationTests
    {
        private GameManagerCore _core;

        [SetUp]
        public void SetUp()
        {
            _core = new GameManagerCore();
            _core.Initialize(8);
        }

        [TearDown]
        public void TearDown() => _core?.Dispose();

        private static CharacterRegistration Registration(CharacterBelong belong, CharacterFeature feature) => new CharacterRegistration
        {
            baseStats = new CharacterBaseStats
            {
                maxHp = 100, maxMp = 50, maxStamina = 80f, maxArmor = 30f,
                staminaRecoveryRate = 12f, staminaRecoveryDelay = 0.6f,
                attributes = new StatModifier { str = 4, intel = 6, vit = 7, mnd = 8 },
                combat = new CombatStats { attack = new ElementalStatus { slash = 15 }, defense = new ElementalStatus { fire = 3 } },
                movement = new MoveParams { moveSpeed = 6f, jumpForce = 12f },
                abilityFlags = AbilityFlag.DoubleJump,
            },
            growth = new CharacterGrowth { level = 3, allocatedStats = new StatModifier { vit = 2, mnd = 1 } },
            flags = CharacterFlags.Pack(belong, feature, ActState.Neutral, AbilityFlag.None),
            defenseState = new CharacterDefenseState { armorRecoveryRate = 2f, armorRecoveryDelay = 1f },
        };

        [Test]
        public void CharacterRegistration_AllFactions_ExposeCompleteStateBeforeRegistrationNotification()
        {
            int notifications = 0;
            CharacterVitals observedVitals = default;
            CharacterBaseStats observedBasis = default;
            CharacterGrowth observedGrowth = default;
            StatModifier observedAttributes = default;
            using System.IDisposable subscription = _core.Events.OnCharacterRegistered.Subscribe(hash =>
            {
                notifications++;
                observedVitals = _core.Data.GetVitals(hash);
                observedBasis = _core.Data.GetBaseStats(hash);
                observedGrowth = _core.Data.GetGrowth(hash);
                observedAttributes = _core.Data.GetAttributes(hash);
            });
            CharacterFeature[] features = { CharacterFeature.Player, CharacterFeature.Minion, CharacterFeature.Companion };

            for (int i = 0; i < features.Length; i++)
            {
                CharacterBelong belong = i == 1 ? CharacterBelong.Enemy : CharacterBelong.Ally;
                CharacterRegistration registration = Registration(belong, features[i]);
                _core.RegisterCharacter(i + 1, registration);

                Assert.AreEqual(i + 1, notifications);
                Assert.AreEqual(120, observedVitals.currentHp);
                Assert.AreEqual(55f, observedVitals.currentMp);
                Assert.AreEqual(3, observedVitals.level);
                Assert.AreEqual(100, observedBasis.maxHp);
                Assert.AreEqual(2, observedGrowth.allocatedStats.vit);
                Assert.AreEqual(9, observedAttributes.vit);
                Assert.AreEqual(belong, _core.Data.GetFlags(i + 1).Belong);
                Assert.AreEqual(features[i], _core.Data.GetFlags(i + 1).Feature);
                Assert.AreEqual(AbilityFlag.DoubleJump, _core.Data.GetFlags(i + 1).AbilityFlags);
                Assert.AreEqual(15, _core.Data.GetCombatStats(i + 1).attack.slash);
                Assert.AreEqual(6f, _core.Data.GetMoveParams(i + 1).moveSpeed);
                Assert.AreEqual(2f, _core.Data.GetDefenseState(i + 1).armorRecoveryRate);
            }
        }

        [Test]
        public void CharacterRegistration_GrowthUpdates_CommitBeforeNotificationWithoutHealingOrAccumulating()
        {
            _core.RegisterCharacter(1, Registration(CharacterBelong.Ally, CharacterFeature.Player));
            _core.RegisterCharacter(2, Registration(CharacterBelong.Enemy, CharacterFeature.Minion));
            ref CharacterVitals current = ref _core.Data.GetVitals(1);
            current.currentHp = 27;
            current.currentMp = 13.5f;
            current.position = new Vector2(2f, 3f);
            int notifications = 0;
            int notifiedHash = 0;
            CharacterGrowth notifiedGrowth = default;
            CharacterVitals notifiedVitals = default;
            using System.IDisposable subscription = _core.Events.OnCharacterStatsChanged.Subscribe(hash =>
            {
                notifiedHash = hash;
                notifications++;
                notifiedGrowth = _core.Data.GetGrowth(hash);
                notifiedVitals = _core.Data.GetVitals(hash);
            });
            CharacterGrowth growth = new CharacterGrowth { level = 5, allocatedStats = new StatModifier { vit = 4, mnd = 3, intel = 2 } };

            _core.ApplyCharacterGrowth(1, growth);
            _core.ApplyCharacterGrowth(1, growth);

            Assert.AreEqual(2, notifications);
            Assert.AreEqual(1, notifiedHash);
            Assert.AreEqual(5, notifiedGrowth.level);
            Assert.AreEqual(4, notifiedGrowth.allocatedStats.vit);
            Assert.AreEqual(140, notifiedVitals.maxHp);
            Assert.AreEqual(65, notifiedVitals.maxMp);
            Assert.AreEqual(27, notifiedVitals.currentHp);
            Assert.AreEqual(13.5f, notifiedVitals.currentMp);
            Assert.AreEqual(new Vector2(2f, 3f), notifiedVitals.position);
            Assert.AreEqual(8, _core.Data.GetAttributes(1).intel);
            Assert.AreEqual(120, _core.Data.GetVitals(2).maxHp);
            Assert.AreEqual(3, _core.Data.GetGrowth(2).level);
            Assert.AreEqual(100, _core.Data.GetBaseStats(1).maxHp);
            Assert.AreEqual(15, _core.Data.GetCombatStats(1).attack.slash);
        }

        [Test]
        public void CharacterRegistration_RefundAndReregister_ClampThenRestoreOriginalInput()
        {
            CharacterRegistration registration = Registration(CharacterBelong.Ally, CharacterFeature.Companion);
            _core.RegisterCharacter(1, registration);
            _core.ApplyCharacterGrowth(1, new CharacterGrowth { level = 1 });

            Assert.AreEqual(100, _core.Data.GetVitals(1).currentHp);
            Assert.AreEqual(50f, _core.Data.GetVitals(1).currentMp);
            Assert.AreEqual(7, _core.Data.GetAttributes(1).vit);
            _core.UnregisterCharacter(1);
            _core.RegisterCharacter(1, registration);

            Assert.AreEqual(120, _core.Data.GetVitals(1).maxHp);
            Assert.AreEqual(120, _core.Data.GetVitals(1).currentHp);
            Assert.AreEqual(55f, _core.Data.GetVitals(1).currentMp);
            Assert.AreEqual(3, _core.Data.GetGrowth(1).level);
            Assert.AreEqual(9, _core.Data.GetAttributes(1).vit);
        }

        [Test]
        public void CharacterRegistration_ExistingAssets_KeepAuthoredPowerAndMovement()
        {
            string[] names = { "PlayerInfo", "BasicEnemyInfo", "CompanionInfo" };
            for (int i = 0; i < names.Length; i++)
            {
                CharacterInfo info = AssetDatabase.LoadAssetAtPath<CharacterInfo>("Assets/MyAsset/Data/" + names[i] + ".asset");
                Assert.IsNotNull(info);
                CharacterRegistration registration = info.CreateRegistration(9.81f);
                _core.RegisterCharacter(i + 1, registration);

                Assert.AreEqual(info.maxHp, _core.Data.GetVitals(i + 1).currentHp);
                Assert.AreEqual(info.maxMp, _core.Data.GetVitals(i + 1).currentMp);
                Assert.AreEqual(info.maxStamina, _core.Data.GetVitals(i + 1).currentStamina);
                Assert.AreEqual(info.initialLevel, _core.Data.GetVitals(i + 1).level);
                Assert.AreEqual(info.baseAttributes, _core.Data.GetAttributes(i + 1));
                Assert.AreEqual(info.baseAttack, _core.Data.GetCombatStats(i + 1).attack);
                Assert.AreEqual(info.baseDefense, _core.Data.GetCombatStats(i + 1).defense);
                Assert.AreEqual(info.moveSpeed, _core.Data.GetMoveParams(i + 1).moveSpeed);
                Assert.AreEqual(Mathf.Sqrt(2f * info.jumpHeight * 9.81f * GameConstants.k_GravityScale), _core.Data.GetMoveParams(i + 1).jumpForce, 0.0001f);
                Assert.AreEqual(15, _core.Data.GetCombatStats(i + 1).attack.slash);
            }
        }

        [Test]
        public void CharacterRegistration_LowLevelInitialState_StoresBasisWithoutFillingResources()
        {
            CharacterVitals initial = new CharacterVitals
            {
                currentHp = 31, maxHp = 80, currentMp = 17.5f, maxMp = 40,
                currentStamina = 20f, maxStamina = 60f, currentArmor = 4f, maxArmor = 15f, level = 7,
            };
            CombatStats combat = new CombatStats { attack = new ElementalStatus { fire = 12 } };
            CharacterFlags flags = CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.WallKick);
            _core.RegisterCharacter(1, initial, combat, flags, new MoveParams { moveSpeed = 4f });

            Assert.AreEqual(80, _core.Data.GetBaseStats(1).maxHp);
            Assert.AreEqual(40, _core.Data.GetBaseStats(1).maxMp);
            Assert.AreEqual(12, _core.Data.GetBaseStats(1).combat.attack.fire);
            Assert.AreEqual(AbilityFlag.WallKick, _core.Data.GetBaseStats(1).abilityFlags);
            Assert.AreEqual(7, _core.Data.GetGrowth(1).level);
            Assert.AreEqual(31, _core.Data.GetVitals(1).currentHp);
            Assert.AreEqual(17.5f, _core.Data.GetVitals(1).currentMp);
            _core.ApplyCharacterGrowth(1, new CharacterGrowth { level = 8, allocatedStats = new StatModifier { vit = 1 } });
            Assert.AreEqual(90, _core.Data.GetVitals(1).maxHp);
            Assert.AreEqual(31, _core.Data.GetVitals(1).currentHp);
            _core.Data.Add(2, new CharacterVitals { maxHp = 30 }, default, default, default);
            Assert.AreEqual(1, _core.Data.GetVitals(2).level);
            Assert.AreEqual(_core.Data.GetVitals(2).level, _core.Data.GetGrowth(2).level);
        }
    }
}
