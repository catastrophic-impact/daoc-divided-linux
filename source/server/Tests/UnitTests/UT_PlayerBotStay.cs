using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>/bots stay (DR-248): when a stay order still holds and when a bot walks back to its spot.</summary>
[TestFixture]
public sealed class UT_PlayerBotStay
{
    [Test]
    public void TheOrderHoldsWhileTheSameLeaderAndTheBotStayInItsRegion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayerBotStay.StillHolds(true, 1, 1, 1, true), Is.True);
            Assert.That(PlayerBotStay.StillHolds(false, 1, 1, 1, true), Is.False, "another player leads the bot now");
            Assert.That(PlayerBotStay.StillHolds(true, 1, 2, 1, true), Is.False, "the bot left the region (teleported along)");
            Assert.That(PlayerBotStay.StillHolds(true, 1, 1, 2, true), Is.False, "the player left: bots follow through as usual");
            Assert.That(PlayerBotStay.StillHolds(true, 1, 1, 1, false), Is.False, "the player logged out");
        });
    }

    [Test]
    public void ABotWalksBackOnlyWhenItLeftItsSpot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayerBotStay.ShouldWalkBack(0), Is.False);
            Assert.That(PlayerBotStay.ShouldWalkBack(PlayerBotStay.ArrivalTolerance), Is.False);
            Assert.That(PlayerBotStay.ShouldWalkBack(PlayerBotStay.ArrivalTolerance + 1), Is.True, "chased a monster off its spot");
            Assert.That(PlayerBotStay.ShouldWalkBack(2_000), Is.True);
        });
    }

    [Test]
    public void TheCommandListsStay()
    {
        var attribute = (CmdAttribute)System.Attribute.GetCustomAttribute(
            typeof(DOL.GS.Commands.PlayerBotOrdersCommand), typeof(CmdAttribute));
        Assert.That(attribute, Is.Not.Null);
        Assert.That(string.Join("\n", attribute.Usage), Does.Contain("/bots stay"));
    }
}
