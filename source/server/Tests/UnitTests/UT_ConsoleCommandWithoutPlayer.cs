using System;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// DR-042 (2026-11-08): a command typed at the server console runs with no player, and handlers that read
/// client.Player (e.g. /stats) threw a NullReferenceException that ScriptMgr logged as a HandleCommand error.
/// </summary>
[TestFixture]
public sealed class UT_ConsoleCommandWithoutPlayer
{
    private sealed class ReadsThePlayer : ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) => _ = client.Player.Name;
    }

    private sealed class Works : ICommandHandler
    {
        public bool Ran;
        public void OnCommand(GameClient client, string[] args) => Ran = true;
    }

    private sealed class Breaks : ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) => throw new InvalidOperationException("broken");
    }

    [Test]
    public void ACommandThatNeedsAPlayerAnswersInsteadOfThrowing()
    {
        GameClient console = new(null);

        bool ran = ScriptMgr.RunCommandHandler(new ReadsThePlayer(), console, ["&stats"], out string refusal);

        Assert.That(ran, Is.False);
        Assert.That(refusal, Does.StartWith("stats needs a character"));
    }

    [Test]
    public void ACommandThatNeedsNoPlayerStillRuns()
    {
        Works handler = new();

        Assert.That(ScriptMgr.RunCommandHandler(handler, new GameClient(null), ["&serverproperties"], out string refusal), Is.True);
        Assert.That(handler.Ran, Is.True);
        Assert.That(refusal, Is.Null);
    }

    [Test]
    public void OtherFailuresAreNotHidden()
    {
        Assert.That(() => ScriptMgr.RunCommandHandler(new Breaks(), new GameClient(null), ["&x"], out _), Throws.InvalidOperationException);
    }
}
