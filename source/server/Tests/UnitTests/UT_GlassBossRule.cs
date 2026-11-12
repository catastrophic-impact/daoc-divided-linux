using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>DR-238: for Atlantean Glass a boss is not just any capitalised name.</summary>
[TestFixture]
public class UT_GlassBossRule
{
    [TestCase(false, true, 1, 3, true, TestName = "a lone capitalised monster is a boss")]
    [TestCase(false, true, 3, 3, true, TestName = "three of a name are still bosses")]
    [TestCase(false, true, 4, 3, false, TestName = "four of a name are trash")]
    [TestCase(false, true, 47, 3, false, TestName = "Uamhas' Brood x47 is trash")]
    [TestCase(false, false, 1, 3, false, TestName = "a lower-case monster is never a boss")]
    [TestCase(true, false, 30, 3, true, TestName = "an epic NPC is always a boss")]
    [TestCase(false, true, 47, 0, true, TestName = "limit 0 keeps the old rule")]
    public void GlassBoss(bool epic, bool capitalised, int spawns, int max, bool expected)
    {
        Assert.That(LootGeneratorAtlanteanGlass.IsGlassBoss(epic, capitalised, spawns, max), Is.EqualTo(expected));
    }
}
