using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Discord 2026-11-08: a raid inside a just-taken keep could not follow the player out (CompanionKeepDoors).</summary>
    public class UT_CompanionKeepDoors
    {
        private static readonly Vector3 Inside = new(1000, 1000, 500);
        private static readonly Vector3 Outside = new(1000, 1600, 500);

        [Test]
        public void PicksTheReachableDoorTowardTheLeader()
        {
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 700, 500), true, false),  // back side, away from the leader
                new CompanionKeepDoors.Candidate(new(1000, 1300, 500), true, false), // the gate toward the leader
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(1));
        }

        [Test]
        public void NeverGoesBackThroughTheDoorJustPassed()
        {
            var doors = new[] { new CompanionKeepDoors.Candidate(new(1000, 1300, 500), true, true) };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(-1));
        }

        [Test]
        public void SkipsUnreachableAndFarAboveDoors()
        {
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 1300, 500), false, false),
                new CompanionKeepDoors.Candidate(new(1000, 1300, 1200), true, false),
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(-1));
        }

        [Test]
        public void InnerDoorFirstThenOuterGate()
        {
            // Lord room door at 1150, outer gate at 1400: the nearer one toward the leader comes first.
            var doors = new[]
            {
                new CompanionKeepDoors.Candidate(new(1000, 1400, 500), true, false),
                new CompanionKeepDoors.Candidate(new(1000, 1150, 500), true, false),
            };
            Assert.That(CompanionKeepDoors.ChooseDoor(Inside, Outside, doors), Is.EqualTo(1));
        }
    }
}
