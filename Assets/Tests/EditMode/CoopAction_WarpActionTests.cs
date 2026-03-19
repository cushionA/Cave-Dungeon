using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CoopAction_WarpActionTests
    {
        [Test]
        public void WarpAction_Behind_CalculatesPosition()
        {
            Vector2 result = WarpCoopAction.CalculateWarpPosition(new Vector2(10f, 0f), WarpTarget.Behind);
            Assert.AreEqual(8.5f, result.x, 0.01f);
        }

        [Test]
        public void WarpAction_Beside_CalculatesPosition()
        {
            Vector2 result = WarpCoopAction.CalculateWarpPosition(new Vector2(10f, 0f), WarpTarget.Beside);
            Assert.AreEqual(10f, result.x, 0.01f);
            Assert.AreEqual(1.5f, result.y, 0.01f);
        }

        [Test]
        public void WarpAction_ExecuteCombo_MovesCompanion()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { position = Vector2.zero }, default, default, default);
            data.Add(2, new CharacterVitals { position = new Vector2(10f, 0f) }, default, default, default);

            WarpCoopAction action = new WarpCoopAction(data);
            action.ExecuteCombo(0, 1, 2);

            ref CharacterVitals v = ref data.GetVitals(1);
            Assert.AreEqual(8.5f, v.position.x, 0.01f);
            data.Dispose();
        }

        [Test]
        public void WarpAction_MissingTarget_DoesNotCrash()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            WarpCoopAction action = new WarpCoopAction(data);

            Assert.DoesNotThrow(() => action.ExecuteCombo(0, 999, 888));
            data.Dispose();
        }
    }
}
