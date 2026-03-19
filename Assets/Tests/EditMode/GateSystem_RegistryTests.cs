using NUnit.Framework;
using System.Collections.Generic;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class GateSystem_RegistryTests
    {
        [Test]
        public void GateRegistry_Register_Tracks()
        {
            GateRegistry registry = new GateRegistry();
            registry.Register("gate_01");
            Assert.IsFalse(registry.IsOpen("gate_01"));
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void GateRegistry_Open_UpdatesState()
        {
            GateRegistry registry = new GateRegistry();
            registry.Register("gate_01");
            registry.Open("gate_01");
            Assert.IsTrue(registry.IsOpen("gate_01"));
        }

        [Test]
        public void GateRegistry_Serialize_Roundtrips()
        {
            GateRegistry registry = new GateRegistry();
            registry.Register("gate_01");
            registry.Open("gate_01");
            registry.Register("gate_02");

            Dictionary<string, bool> data = registry.SerializeAll();

            GateRegistry restored = new GateRegistry();
            restored.DeserializeAll(data);

            Assert.IsTrue(restored.IsOpen("gate_01"));
            Assert.IsFalse(restored.IsOpen("gate_02"));
        }
    }
}
