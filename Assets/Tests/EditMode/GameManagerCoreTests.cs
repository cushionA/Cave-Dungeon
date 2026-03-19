using NUnit.Framework;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class GameManagerCoreTests
    {
        private GameManagerCore _core;

        [SetUp]
        public void SetUp()
        {
            _core = new GameManagerCore();
        }

        [TearDown]
        public void TearDown()
        {
            _core?.Dispose();
            _core = null;
        }

        [Test]
        public void GameManagerCore_Initialize_DataIsNotNull()
        {
            // Arrange & Act
            _core.Initialize(16);

            // Assert
            Assert.IsNotNull(_core.Data);
            Assert.IsNotNull(_core.Events);
            Assert.IsTrue(_core.IsInitialized);
        }

        [Test]
        public void GameManagerCore_RegisterCharacter_FiresEvent()
        {
            // Arrange
            _core.Initialize(16);
            int receivedHash = -1;
            _core.Events.OnCharacterRegistered.Subscribe(hash => { receivedHash = hash; });

            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CombatStats combat = default;
            CharacterFlags flags = default;
            MoveParams move = default;
            int testHash = 42;

            // Act
            _core.RegisterCharacter(testHash, vitals, combat, flags, move);

            // Assert
            Assert.AreEqual(testHash, receivedHash);
            Assert.AreEqual(1, _core.Data.Count);
        }

        [Test]
        public void GameManagerCore_SubManagers_InitializedInOrder()
        {
            // Arrange
            MockSubManager.ResetCounter();
            MockSubManager managerA = new MockSubManager { InitOrder = 30 };
            MockSubManager managerB = new MockSubManager { InitOrder = 10 };
            MockSubManager managerC = new MockSubManager { InitOrder = 20 };

            _core.RegisterSubManager(managerA);
            _core.RegisterSubManager(managerB);
            _core.RegisterSubManager(managerC);

            // Act
            _core.Initialize(16);

            // Assert — InitOrder: B(10) -> C(20) -> A(30)
            Assert.AreEqual(0, managerB.InitializedAt, "InitOrder=10 should be first");
            Assert.AreEqual(1, managerC.InitializedAt, "InitOrder=20 should be second");
            Assert.AreEqual(2, managerA.InitializedAt, "InitOrder=30 should be third");
        }

        [Test]
        public void GameManagerCore_Dispose_CleansUp()
        {
            // Arrange
            _core.Initialize(16);
            CharacterVitals vitals = new CharacterVitals { currentHp = 50, maxHp = 50 };
            _core.RegisterCharacter(1, vitals, default, default, default);

            // Act
            _core.Dispose();

            // Assert
            Assert.IsFalse(_core.IsInitialized);
            // Data has been disposed; accessing it should throw ObjectDisposedException
            Assert.Throws<System.ObjectDisposedException>(() =>
            {
                _core.Data.GetVitals(1);
            });
        }

        /// <summary>
        /// テスト用モックサブマネージャー。
        /// 初期化された順序を記録する。
        /// </summary>
        private class MockSubManager : IGameSubManager
        {
            private static int _initCounter;

            public int InitOrder { get; set; }
            public int InitializedAt { get; private set; } = -1;

            public void Initialize(SoACharaDataDic data, GameEvents events)
            {
                InitializedAt = _initCounter++;
            }

            public void Dispose()
            {
            }

            public static void ResetCounter()
            {
                _initCounter = 0;
            }
        }
    }
}
