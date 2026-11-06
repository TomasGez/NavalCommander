using NUnit.Framework;
using UnityEngine;

namespace NavalCommander.Progression.Tests
{
    public class PlayerProgressionTests
    {
        private const string ProfileA = "progression-test-a";
        private const string ProfileB = "progression-test-b";

        [SetUp]
        public void SetUp()
        {
            DeleteKeys(ProfileA);
            DeleteKeys(ProfileB);
            PlayerProgression.UseProfileForTests(ProfileA);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteKeys(ProfileA);
            DeleteKeys(ProfileB);
            PlayerProgression.UseProfileForTests(null);
        }

        [Test]
        public void NewProfileStartsAtLevel1WithoutGain()
        {
            Assert.That(PlayerProgression.Level, Is.EqualTo(1));
            Assert.That(PlayerProgression.Xp, Is.EqualTo(0));
            Assert.That(PlayerProgression.XpForNextLevel, Is.EqualTo(100));
            Assert.That(PlayerProgression.LastXpGain, Is.EqualTo(0));
        }

        [Test]
        public void AddXpLevelsUpAndNotifies()
        {
            int notifications = 0;
            void Count() => notifications++;
            PlayerProgression.Changed += Count;

            PlayerProgression.AddXp(170);
            PlayerProgression.Changed -= Count;

            Assert.That(PlayerProgression.Level, Is.EqualTo(2));
            Assert.That(PlayerProgression.Xp, Is.EqualTo(70));
            Assert.That(PlayerProgression.XpForNextLevel, Is.EqualTo(200));
            Assert.That(PlayerProgression.LastXpGain, Is.EqualTo(170));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void ProgressIsStoredPerProfile()
        {
            PlayerProgression.AddXp(170);

            PlayerProgression.UseProfileForTests(ProfileB);
            Assert.That(PlayerProgression.Level, Is.EqualTo(1));

            PlayerProgression.UseProfileForTests(ProfileA);
            Assert.That(PlayerProgression.Level, Is.EqualTo(2));
            Assert.That(PlayerProgression.Xp, Is.EqualTo(70));
        }

        [Test]
        public void MatchBracketFollowsTheLevel()
        {
            string startBracket = PlayerProgression.MatchBracket;
            Assert.That(startBracket, Is.Not.Empty);

            PlayerProgression.AddXp(1500); // nivel 6: primer nivel del rango siguiente

            Assert.That(PlayerProgression.Level, Is.EqualTo(6));
            Assert.That(PlayerProgression.MatchBracket, Is.Not.EqualTo(startBracket));
        }

        [Test]
        public void DisplayNameIsStoredPerProfileAndFallsBackWhenEmpty()
        {
            PlayerProgression.DisplayName = "  Almirante  ";
            Assert.That(PlayerProgression.DisplayName, Is.EqualTo("Almirante"));

            PlayerProgression.UseProfileForTests(ProfileB);
            Assert.That(PlayerProgression.DisplayName, Is.EqualTo("Jugador"));

            PlayerProgression.UseProfileForTests(ProfileA);
            PlayerProgression.DisplayName = "   ";
            Assert.That(PlayerProgression.DisplayName, Is.EqualTo("Jugador"));
        }

        private static void DeleteKeys(string profile)
        {
            PlayerPrefs.DeleteKey("NavalCommander.Progression." + profile + ".TotalXp");
            PlayerPrefs.DeleteKey("NavalCommander.Progression." + profile + ".DisplayName");
        }
    }
}
