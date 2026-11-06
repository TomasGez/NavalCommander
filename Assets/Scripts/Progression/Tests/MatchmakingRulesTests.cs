using NUnit.Framework;

namespace NavalCommander.Progression.Tests
{
    public class MatchmakingRulesTests
    {
        private static readonly string[] NoFailures = new string[0];

        [Test]
        public void HostStaysWhenNoOtherRoomIsVisible()
        {
            Assert.That(MatchmakingRules.RoomToJoin("m", new string[0], NoFailures), Is.Null);
        }

        [Test]
        public void HostIgnoresItsOwnRoomInTheResults()
        {
            Assert.That(MatchmakingRules.RoomToJoin("m", new[] { "m" }, NoFailures), Is.Null);
        }

        [Test]
        public void HostWithTheLowestIdStays()
        {
            Assert.That(MatchmakingRules.RoomToJoin("a", new[] { "a", "b", "c" }, NoFailures), Is.Null);
        }

        [Test]
        public void HostMovesToTheLowestVisibleRoom()
        {
            Assert.That(MatchmakingRules.RoomToJoin("m", new[] { "z", "c", "b", "m" }, NoFailures), Is.EqualTo("b"));
        }

        [Test]
        public void ExactlyOneOfTwoHostsWhoSeeEachOtherMoves()
        {
            string[] rooms = { "room-A", "room-B" };

            Assert.That(MatchmakingRules.RoomToJoin("room-A", rooms, NoFailures), Is.Null);
            Assert.That(MatchmakingRules.RoomToJoin("room-B", rooms, NoFailures), Is.EqualTo("room-A"));
        }

        [Test]
        public void RoomsThatAlreadyFailedAreSkipped()
        {
            Assert.That(MatchmakingRules.RoomToJoin("m", new[] { "a", "b" }, new[] { "a" }), Is.EqualTo("b"));
            Assert.That(MatchmakingRules.RoomToJoin("m", new[] { "a" }, new[] { "a" }), Is.Null);
        }

        [Test]
        public void ComparisonIgnoresTheMachineCulture()
        {
            Assert.That(MatchmakingRules.RoomToJoin("a", new[] { "B" }, NoFailures), Is.EqualTo("B"));
        }
    }
}
