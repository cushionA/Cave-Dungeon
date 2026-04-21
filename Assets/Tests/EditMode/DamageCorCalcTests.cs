using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class DamageCorCalcTests
    {
        // --- 基本ダメージ計算 ---

        [Test]
        public void DamageCalculator_CalculateBaseDamage_AppliesFormula()
        {
            // 新式: (atk² × motionValue) / (atk + def)
            // atk=100, motionValue=1.2, def=30 => (10000*1.2)/(130) = 92
            int result = DamageCalculator.CalculateBaseDamage(100, 1.2f, 30);

            Assert.AreEqual(92, result);
        }

        [Test]
        public void DamageCalculator_CalculateBaseDamage_MinDamageGuarantee()
        {
            // def > atk*motionValue => 最小ダメージ保証
            int result = DamageCalculator.CalculateBaseDamage(10, 0.5f, 100);

            Assert.AreEqual(DamageCalculator.k_MinDamage, result);
        }

        // --- 属性チャネルダメージ ---

        [Test]
        public void DamageCalculator_CalculateChannelDamage_ReturnsBaseDamage()
        {
            // 弱点倍率は仕様外。チャネルダメージは CalculateBaseDamage と等価。
            int channelResult = DamageCalculator.CalculateChannelDamage(100, 1.0f, 0);
            int baseResult = DamageCalculator.CalculateBaseDamage(100, 1.0f, 0);

            Assert.AreEqual(baseResult, channelResult);
        }

        [Test]
        public void DamageCalculator_CalculateChannelDamage_ZeroAttack_ReturnsZero()
        {
            int result = DamageCalculator.CalculateChannelDamage(0, 1.0f, 10);

            Assert.AreEqual(0, result);
        }

        // --- 属性別ガードカット (新仕様) ---

        [Test]
        public void DamageCalculator_CalculateTotalDamageWithElementalCut_ApplyCutsFalse_EqualsOldApi()
        {
            // applyCuts=false は既存 CalculateTotalDamage と同一結果を返す
            ElementalStatus attack = new ElementalStatus { fire = 50, slash = 30 };
            ElementalStatus defense = new ElementalStatus { fire = 10, slash = 5 };
            GuardStats cuts = new GuardStats
            {
                fireCut = 0.5f,
                slashCut = 0.9f
            };

            int oldApi = DamageCalculator.CalculateTotalDamage(attack, 1.0f, defense);
            int newApiNoCut = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cuts, applyCuts: false);

            Assert.AreEqual(oldApi, newApiNoCut);
        }

        [Test]
        public void DamageCalculator_CalculateTotalDamageWithElementalCut_SingleElement_AppliesCut()
        {
            // fireだけで50%カットすると、カットなしの半分以下になる(丸め誤差含む)
            ElementalStatus attack = new ElementalStatus { fire = 100 };
            ElementalStatus defense = new ElementalStatus { fire = 10 };
            GuardStats cuts = new GuardStats { fireCut = 0.5f };

            int full = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cuts, applyCuts: false);
            int cut = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cuts, applyCuts: true);

            // 50%カット: 約半分(整数化の誤差で一致しない可能性があるため範囲比較)
            Assert.Less(cut, full);
            Assert.GreaterOrEqual(cut, (int)(full * 0.45f));
            Assert.LessOrEqual(cut, (int)(full * 0.55f) + 1);
        }

        [Test]
        public void DamageCalculator_CalculateTotalDamageWithElementalCut_MultiElement_AppliesEachChannelCut()
        {
            // Fire+Slash混在攻撃: それぞれのチャネルに別々のカット率を適用して合算
            ElementalStatus attack = new ElementalStatus { fire = 50, slash = 50 };
            ElementalStatus defense = new ElementalStatus { fire = 0, slash = 0 };
            GuardStats cutsOnlyFire = new GuardStats { fireCut = 1.0f };    // fire完全カット
            GuardStats cutsOnlySlash = new GuardStats { slashCut = 1.0f };  // slash完全カット

            int noCut = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, default, applyCuts: false);
            int fireOnly = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cutsOnlyFire, applyCuts: true);
            int slashOnly = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cutsOnlySlash, applyCuts: true);

            // Fire完全カット時 ≒ slashチャネル分のみ
            // Slash完全カット時 ≒ fireチャネル分のみ
            // 両方合計 ≒ noCut
            Assert.Less(fireOnly, noCut);
            Assert.Less(slashOnly, noCut);
            Assert.GreaterOrEqual(fireOnly + slashOnly, noCut - 2);
            Assert.LessOrEqual(fireOnly + slashOnly, noCut + 2);
        }

        [Test]
        public void DamageCalculator_CalculateTotalDamageWithElementalCut_CutClampedTo1()
        {
            // カット率1.0以上でも完全0にクランプ(負ダメージ防止)
            ElementalStatus attack = new ElementalStatus { fire = 50 };
            ElementalStatus defense = default;
            GuardStats cuts = new GuardStats { fireCut = 1.5f };

            int result = DamageCalculator.CalculateTotalDamageWithElementalCut(
                attack, 1.0f, defense, cuts, applyCuts: true);

            Assert.AreEqual(DamageCalculator.k_MinDamage, result);
        }
    }
}
