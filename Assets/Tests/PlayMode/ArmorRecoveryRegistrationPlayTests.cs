using System;
using System.Collections;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class ArmorRecoveryRegistrationPlayTests
    {
        private sealed class ArmorEquipment : IEquippable, IEquipmentStatsProvider
        {
            public EquipSlot Slot => EquipSlot.Core;
            public int Weight => 0;
            public AbilityFlag GrantedFlags => AbilityFlag.None;
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public EquipmentStatModifiers StatModifiers => new EquipmentStatModifiers { bonusArmor = 40f };
            public void OnEquip(int ownerHash) { }
            public void OnUnequip(int ownerHash) { }
        }

        private GameObject _actor;
        private Game.Core.CharacterInfo _info;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_actor != null) { Object.Destroy(_actor); }
            yield return null;
            if (_info != null) { Object.Destroy(_info); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArmorRecovery_EquipmentDuringRegistration_UsesDerivedMaximumAfterStartAndPoolAcquire()
        {
            _info = TestSceneHelper.CreateTestCharacterInfo();
            _info.maxArmor = 20f;
            _info.armorRecoveryRate = 10f;
            _info.armorRecoveryDelay = 0f;
            using IDisposable registration = GameManager.Events.OnCharacterRegistered.Subscribe(hash =>
            {
                GameManager.Equipment.Equip(hash, new ArmorEquipment());
                // Registration already captured recovery settings; later asset edits must not replace them.
                _info.armorRecoveryRate = 0f;
            });
            _actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(_info, Vector3.zero);
            yield return null;
            BaseCharacter character = _actor.GetComponent<BaseCharacter>();

            Assert.AreEqual(60f, GameManager.Data.GetVitals(character.ObjectHash).maxArmor);
            GameManager.Data.GetVitals(character.ObjectHash).currentArmor = 0f;
            character.DamageReceiver.TickRecovery(100f);
            Assert.AreEqual(60f, GameManager.Data.GetVitals(character.ObjectHash).currentArmor);

            character.OnPoolReturn();
            _info.armorRecoveryRate = 10f;
            character.OnPoolAcquire();
            Assert.AreEqual(60f, GameManager.Data.GetVitals(character.ObjectHash).maxArmor);
            GameManager.Data.GetVitals(character.ObjectHash).currentArmor = 0f;
            character.DamageReceiver.TickRecovery(100f);
            Assert.AreEqual(60f, GameManager.Data.GetVitals(character.ObjectHash).currentArmor);
        }
    }
}
