using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BossSystem_ArenaTests
    {
        [Test]
        public void BossArenaLogic_Constructor_StartsOpen()
        {
            BossArenaLogic arena = new BossArenaLogic("clear_gate_01");
            Assert.AreEqual(ArenaState.Open, arena.State);
        }

        [Test]
        public void BossArenaLogic_Lock_ChangesToLocked()
        {
            BossArenaLogic arena = new BossArenaLogic("clear_gate_01");
            arena.LockArena();
            Assert.AreEqual(ArenaState.Locked, arena.State);
        }

        [Test]
        public void BossArenaLogic_Unlock_ChangesToCleared()
        {
            BossArenaLogic arena = new BossArenaLogic("clear_gate_01");
            arena.LockArena();

            string openedGateId = null;
            arena.OnClearGateOpen += (id) => openedGateId = id;

            arena.UnlockArena();

            Assert.AreEqual(ArenaState.Cleared, arena.State);
            Assert.AreEqual("clear_gate_01", openedGateId);
        }

        [Test]
        public void BossArenaLogic_UnlockWithoutLock_StillClears()
        {
            BossArenaLogic arena = new BossArenaLogic("clear_gate_01");
            arena.UnlockArena();
            Assert.AreEqual(ArenaState.Cleared, arena.State);
        }
    }
}
