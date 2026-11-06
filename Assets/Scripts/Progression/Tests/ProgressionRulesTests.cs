using System;
using NUnit.Framework;

namespace NavalCommander.Progression.Tests
{
    public class ProgressionRulesTests
    {
        private const int Winner = 2;
        private const int Loser = 0;

        [Test]
        public void WinnerRollCoversTheWholeRangeAndNeverLeavesIt()
        {
            var rng = new Random(1234);
            bool sawMin = false, sawMax = false;

            for (int i = 0; i < 5000; i++)
            {
                int xp = ProgressionRules.RollMatchXp(Winner, Winner, rng);
                Assert.That(xp, Is.InRange(80, 120));
                sawMin |= xp == 80;
                sawMax |= xp == 120;
            }

            Assert.That(sawMin, Is.True, "80 debe poder salir");
            Assert.That(sawMax, Is.True, "120 debe poder salir");
        }

        [Test]
        public void LoserGetsNothing()
        {
            Assert.That(ProgressionRules.RollMatchXp(Loser, Winner, new Random(1)), Is.EqualTo(0));
        }

        [Test]
        public void MatchWithoutWinnerGivesNothing()
        {
            Assert.That(ProgressionRules.RollMatchXp(Loser, -1, new Random(1)), Is.EqualTo(0));
            Assert.That(ProgressionRules.RollMatchXp(-1, -1, new Random(1)), Is.EqualTo(0));
        }

        [TestCase(1, 100)]
        [TestCase(2, 200)]
        [TestCase(20, 2000)]
        public void LevelNNeeds100TimesNXp(int level, int expected)
        {
            Assert.That(ProgressionRules.XpToNextLevel(level), Is.EqualTo(expected));
        }

        [TestCase(0, 1, 0)]
        [TestCase(99, 1, 99)]
        [TestCase(100, 2, 0)]
        [TestCase(299, 2, 199)]
        [TestCase(300, 3, 0)]
        public void TotalXpMapsToLevelAndRemainder(int totalXp, int level, int remainder)
        {
            Assert.That(ProgressionRules.LevelForTotalXp(totalXp), Is.EqualTo(level));
            Assert.That(ProgressionRules.XpIntoLevel(totalXp), Is.EqualTo(remainder));
        }

        [Test]
        public void OneWinCanCrossALevelBoundary()
        {
            int beforeWin = 90;
            int afterWin = beforeWin + ProgressionRules.MinWinXp;

            Assert.That(ProgressionRules.LevelForTotalXp(beforeWin), Is.EqualTo(1));
            Assert.That(ProgressionRules.LevelForTotalXp(afterWin), Is.EqualTo(2));
            Assert.That(ProgressionRules.XpIntoLevel(afterWin), Is.EqualTo(70));
        }

        [Test]
        public void OneGainCanCrossSeveralLevels()
        {
            Assert.That(ProgressionRules.LevelForTotalXp(1000), Is.EqualTo(5));
            Assert.That(ProgressionRules.XpIntoLevel(1000), Is.EqualTo(0));
        }

        [Test]
        public void Level1AndLevel20GetDifferentBrackets()
        {
            Assert.That(ProgressionRules.BracketForLevel(1), Is.Not.EqualTo(ProgressionRules.BracketForLevel(20)));
        }

        [TestCase(1, 5)]
        [TestCase(6, 10)]
        [TestCase(16, 20)]
        public void LevelsInsideABracketMatch(int low, int high)
        {
            Assert.That(ProgressionRules.BracketForLevel(low), Is.Not.Empty);
            Assert.That(ProgressionRules.BracketForLevel(low), Is.EqualTo(ProgressionRules.BracketForLevel(high)));
        }

        [TestCase(5, 6)]
        [TestCase(10, 11)]
        public void LevelsAcrossABracketBoundaryDoNotMatch(int below, int above)
        {
            Assert.That(ProgressionRules.BracketForLevel(below), Is.Not.EqualTo(ProgressionRules.BracketForLevel(above)));
        }
    }
}
