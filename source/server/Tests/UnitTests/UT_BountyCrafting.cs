using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_BountyCrafting
{
    // The real craft, without the translations its constructor reads.
    private static BountyCrafting Craft()
    {
        var craft = (BountyCrafting)RuntimeHelpers.GetUninitializedObject(typeof(BountyCrafting));
        craft.eSkill = eCraftingSkill.BountyCrafting;
        return craft;
    }

    [Test]
    public void Bountycraft_IsTradeFiveLikeTheClient()
    {
        // tdl.crf (1.130) lists Bountycraft as trade 5; the client names trade skill lines by this id.
        Assert.That((int)eCraftingSkill.BountyCrafting, Is.EqualTo(5));
        Assert.That(Enum.GetValues<eCraftingSkill>().Count(s => (int)s == 5), Is.EqualTo(1));
        Assert.That((int)eCraftingSkill.BountyCrafting, Is.LessThan((int)eCraftingSkill._Last),
            "CraftingMgr keeps skills in an array of _Last entries");
    }

    [Test]
    public void Bountycraft_NeverFailsAndNeverGainsSkill()
    {
        BountyCrafting craft = Craft();
        Assert.That(craft.CalculateChanceToMakeItem(null, 0), Is.EqualTo(100));
        Assert.That(craft.CalculateChanceToMakeItem(null, 1000), Is.EqualTo(100));
        Assert.DoesNotThrow(() => craft.GainCraftingSkillPoints(null, null));
        Assert.That(craft.GetSecondaryCraftingSkillMinimumLevel(null), Is.EqualTo(0));
    }

    [TestCase(0, 1)]   // realm rank 1L0
    [TestCase(9, 1)]   // 1L9
    [TestCase(10, 2)]  // 2L0
    [TestCase(49, 5)]  // 5L9
    [TestCase(120, 13)]
    public void Molds_AreBoughtInStacksOfTheRealmRank(int realmLevel, int max)
    {
        Assert.That(BountyCraftItems.MaxMoldsPerPurchase(realmLevel), Is.EqualTo(max));
        var mold = new DbItemTemplate { Id_nb = BountyCraftItems.ArmorMold };
        Assert.That(BountyCraftItems.AllowedCount(mold, 50, realmLevel), Is.EqualTo(max));
        Assert.That(BountyCraftItems.AllowedCount(mold, 1, realmLevel), Is.EqualTo(1));
    }

    [Test]
    public void OtherBountyItems_HaveNoStackLimit()
    {
        var recipe = new DbItemTemplate { Id_nb = "bounty_recipe_curse" };
        Assert.That(BountyCraftItems.IsMold(recipe), Is.False);
        Assert.That(BountyCraftItems.AllowedCount(recipe, 7, 0), Is.EqualTo(7));
        Assert.That(BountyCraftItems.IsMold(null), Is.False);
        Assert.That(BountyCraftItems.IsMold(new DbItemTemplate { Id_nb = "BOUNTY_WEAPON_MOLD" }), Is.True);
    }

    [Test]
    public void ComponentBox_GivesTwoToFiveOfOneOfEightComponents()
    {
        Assert.That(BountyCraftItems.BoxComponents.Distinct().Count(), Is.EqualTo(8));
        var rng = new Random(1125);
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 2000; i++)
        {
            (string component, int count) = BountyCraftItems.RollBox(rng);
            Assert.That(BountyCraftItems.BoxComponents, Does.Contain(component));
            Assert.That(count, Is.InRange(BountyCraftItems.BoxMinCount, BountyCraftItems.BoxMaxCount));
            seen.Add(component);
        }
        Assert.That(seen.Count, Is.EqualTo(8));
    }

    [TestCase(-10, 100, 10)]
    [TestCase(-18, 100, 18)]
    [TestCase(-10, 250, 25)]
    [TestCase(-100, 80, 80)]
    public void EnduranceHeal_PercentIsOfTheTargetsEndurance(double value, int max, int restored)
    {
        // the Bountycraft omni heals: Heal + PowerHeal + EnduranceHeal at -10 / -18 (percent)
        Assert.That(DOL.GS.Spells.EnduranceHealSpellHandler.PercentOf(value, max), Is.EqualTo(restored));
    }

    [TestCase(eProperty.Skill_Sword, true)]
    [TestCase(eProperty.Skill_Critical_Strike, true)]
    [TestCase(eProperty.Skill_Celtic_Dual, true)]
    [TestCase(eProperty.Strength, false)]
    [TestCase(eProperty.Resist_Body, false)]
    [TestCase(eProperty.Undefined, false)]
    public void SpecSkillBuff_OnlyRaisesSpecializationSkills(eProperty property, bool allowed)
    {
        // the official "Skill buff: Sword, Bonus 10%" item procs; a stray BuffProperty must never buff a stat or resist
        Assert.That(DOL.GS.Spells.SpecSkillBuff.IsSkill(property), Is.EqualTo(allowed));
    }

    [Test]
    public void SpecSkillBuff_WithoutASpellHasNoProperty()
    {
        Assert.That(DOL.GS.Spells.SpecSkillBuff.PropertyOf(null), Is.EqualTo(eProperty.Undefined));
    }

    [Test]
    public void TryUse_IgnoresEverythingButTheBox()
    {
        Assert.That(BountyCraftItems.TryUse(null, null), Is.False);
        Assert.That(BountyCraftItems.TryUse(null, new DbInventoryItem { Id_nb = "stardust" }), Is.False);
    }
}
