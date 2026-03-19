using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BacktrackReward_MapIntegrationTests
    {
        [Test]
        public void BacktrackMapMarker_NotAccessible_IsHidden()
        {
            BacktrackMapMarker marker = new BacktrackMapMarker("r1", AbilityFlag.WallKick);

            BacktrackMarkerState state = marker.GetState(AbilityFlag.None, false);
            Assert.AreEqual(BacktrackMarkerState.Hidden, state);
        }

        [Test]
        public void BacktrackMapMarker_Accessible_IsVisible()
        {
            BacktrackMapMarker marker = new BacktrackMapMarker("r1", AbilityFlag.WallKick);

            BacktrackMarkerState state = marker.GetState(AbilityFlag.WallKick, false);
            Assert.AreEqual(BacktrackMarkerState.Available, state);
        }

        [Test]
        public void BacktrackMapMarker_Collected_IsCompleted()
        {
            BacktrackMapMarker marker = new BacktrackMapMarker("r1", AbilityFlag.WallKick);

            BacktrackMarkerState state = marker.GetState(AbilityFlag.WallKick, true);
            Assert.AreEqual(BacktrackMarkerState.Collected, state);
        }
    }
}
