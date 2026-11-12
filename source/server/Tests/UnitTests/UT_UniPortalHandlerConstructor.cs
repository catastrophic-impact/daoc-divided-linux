using System;
using DOL;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// DR-037 (2026-11-08): every boot logged "Couldn't find a SpellHandler constructor for UniPortal / UniPortalKeep",
/// because ScriptMgr compiles the standard (caster, spell, line) constructor and the portals had only the
/// teleporters' four-argument one.
/// </summary>
[TestFixture]
public sealed class UT_UniPortalHandlerConstructor
{
    [TestCase(typeof(UniPortal))]
    [TestCase(typeof(UniPortalKeep))]
    public void ScriptMgrCanCompileTheStandardConstructor(Type handler)
    {
        Delegate made = null;
        Assert.That(() => made = CompiledConstructorFactory.CompileConstructor(handler, [typeof(GameLiving), typeof(Spell), typeof(SpellLine)]), Throws.Nothing);
        Assert.That(made, Is.InstanceOf<Func<GameLiving, Spell, SpellLine, ISpellHandler>>());
    }
}
