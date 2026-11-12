using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Owner 2026-11-10 (player report): after one pull that brought friends, every monster came alone until the player
/// stopped for two minutes (a level-up "reset" it). The lock now lasts only while that pull's monsters still fight.
/// </summary>
[TestFixture]
public class UT_BafFightLock
{
    [Test]
    public void ChainPullingANewCampBringsFriendsOnceTheLastFightIsOver() => Assert.Multiple(() =>
    {
        // the last fight's monsters are all dead 40 s after it started: the next pull (any time later) brings friends
        Assert.That(BafFriends.StillInFight(false, true, true, 40_000, 0), Is.False);
        Assert.That(BafFriends.StillInFight(false, false, false, 41_000, 0), Is.False, "dead and gone from the world");
        // a monster of that pull that gave up (reset, evaded) no longer holds it
        Assert.That(BafFriends.StillInFight(true, true, false, 41_000, 0), Is.False);
    });

    [Test]
    public void AFightStillUnderWayBringsNoNewRoundOfAdds() => Assert.Multiple(() =>
    {
        // Keltoi Fogou (owner 2026-10-29): a respawned group joins a fight already under way
        Assert.That(BafFriends.StillInFight(true, true, true, 30_000, 0), Is.True);
        Assert.That(BafFriends.StillInFight(true, true, true, 300_000, 0), Is.True, "a long fight is still the same fight");
    });

    [Test]
    public void TheLockNeverOutlastsTheSafetyCap() => Assert.Multiple(() =>
    {
        Assert.That(BafFriends.StillInFight(true, true, true, BafFriends.BafFightMaxMilliseconds - 1, 0), Is.True);
        Assert.That(BafFriends.StillInFight(true, true, true, BafFriends.BafFightMaxMilliseconds, 0), Is.False);
    });

    [Test]
    public void NoPlayerNeverLocks()
    {
        Assert.That(BafFriends.InBafFight(null), Is.False);
        Assert.That(() => BafFriends.MarkBafFight(null, null, null), Throws.Nothing);
    }
}
