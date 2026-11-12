using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// How artifact levels work. Any experience earned while the artifact is worn counts, and realm
    /// points count too. A level 0 artifact keeps its full armor or weapon values; bonuses grow with
    /// each level and the abilities open at levels 5 and 10.
    /// </summary>
    public static class ArtifactLevels
    {
        public const int MaxLevel = 10;
        public const int FirstAbilityLevel = 5;
        public const int SecondAbilityLevel = 10;

        /// <summary>About what one even-con level 50 kill is worth before the server's rates.</summary>
        public const long XpPerKill = 40000000;
        public const long XpPerRealmPoint = XpPerKill / 25;

        private const int BaseBonusPercent = 40;
        private const int BonusPercentPerLevel = 6;

        /// <summary>Total experience needed for each level: 100 even kills for level 1, 280 for level 10.</summary>
        private static readonly long[] TotalXp = BuildTotals();

        public static long MaxXp => TotalXp[MaxLevel];

        private static long[] BuildTotals()
        {
            long[] totals = new long[MaxLevel + 1];

            for (int level = 1; level <= MaxLevel; level++)
                totals[level] = totals[level - 1] + (80 + 20 * level) * XpPerKill;

            return totals;
        }

        public static long XpForLevel(int level)
        {
            return TotalXp[Math.Clamp(level, 0, MaxLevel)];
        }

        public static int LevelFor(long xp)
        {
            for (int level = MaxLevel; level > 0; level--)
            {
                if (xp >= TotalXp[level])
                    return level;
            }

            return 0;
        }

        public static int ScaleBonus(int value, int level)
        {
            if (value == 0)
                return 0;

            int scaled = (int) Math.Ceiling(Math.Abs(value) * (BaseBonusPercent + BonusPercentPerLevel * level) / 100.0);
            return value < 0 ? -scaled : scaled;
        }

        /// <summary>The level that opens an ability, given its place among the item's abilities.</summary>
        public static int AbilityUnlockLevel(int rank)
        {
            return rank == 0 ? FirstAbilityLevel : SecondAbilityLevel;
        }

        public static void AddXp(GamePlayer player, long xp)
        {
            if (player == null || xp <= 0)
                return;

            foreach (DbInventoryItem item in player.Inventory.EquippedItems)
            {
                if (item is ArtifactItem artifact)
                    artifact.SetExperience(player, artifact.Experience + xp);
            }
        }
    }

    /// <summary>A levelling artifact. Templates opt in through ItemTemplate.ClassType.</summary>
    public class ArtifactItem : GameInventoryItem
    {
        public ArtifactItem() : base() { }
        public ArtifactItem(DbItemTemplate template) : base(template) { }
        public ArtifactItem(DbItemUnique template) : base(template) { }
        public ArtifactItem(DbInventoryItem item) : base(item) { }

        public int ArtifactLevel => ArtifactLevels.LevelFor(Experience);

        public override int ExtraBonus { get => Scale(Template.ExtraBonus); set => Template.ExtraBonus = value; }
        public override int Bonus1 { get => Scale(Template.Bonus1); set => Template.Bonus1 = value; }
        public override int Bonus2 { get => Scale(Template.Bonus2); set => Template.Bonus2 = value; }
        public override int Bonus3 { get => Scale(Template.Bonus3); set => Template.Bonus3 = value; }
        public override int Bonus4 { get => Scale(Template.Bonus4); set => Template.Bonus4 = value; }
        public override int Bonus5 { get => Scale(Template.Bonus5); set => Template.Bonus5 = value; }
        public override int Bonus6 { get => Scale(Template.Bonus6); set => Template.Bonus6 = value; }
        public override int Bonus7 { get => Scale(Template.Bonus7); set => Template.Bonus7 = value; }
        public override int Bonus8 { get => Scale(Template.Bonus8); set => Template.Bonus8 = value; }
        public override int Bonus9 { get => Scale(Template.Bonus9); set => Template.Bonus9 = value; }
        public override int Bonus10 { get => Scale(Template.Bonus10); set => Template.Bonus10 = value; }

        public override int ProcSpellID { get => Unlocked(Template.ProcSpellID); set => Template.ProcSpellID = value; }
        public override int SpellID { get => Unlocked(Template.SpellID); set => Template.SpellID = value; }
        public override int ProcSpellID1 { get => Unlocked(Template.ProcSpellID1); set => Template.ProcSpellID1 = value; }
        public override int SpellID1 { get => Unlocked(Template.SpellID1); set => Template.SpellID1 = value; }

        private int Scale(int value)
        {
            return ArtifactLevels.ScaleBonus(value, ArtifactLevel);
        }

        /// <summary>Procs come first, so the mists and the like open before a once-a-while use ability.</summary>
        private int[] AbilityIds()
        {
            return new[] { Template.ProcSpellID, Template.SpellID, Template.ProcSpellID1, Template.SpellID1 }.Where(id => id != 0).ToArray();
        }

        private int Unlocked(int spellId)
        {
            if (spellId == 0)
                return 0;

            return ArtifactLevel >= UnlockLevelOf(spellId) ? spellId : 0;
        }

        private int UnlockLevelOf(int spellId) => ArtifactLevels.AbilityUnlockLevel(Array.IndexOf(AbilityIds(), spellId));

        /// <summary>
        /// Why a use of this artifact does nothing yet: the ability behind use 1 (type 0 or 1) or use 2 (type 2) is
        /// still locked. Null when it is open or there is none. Used to fail silently with "You attempt to use...".
        /// </summary>
        public string LockedUseMessage(int type)
        {
            int spellId = type == 2 ? Template.SpellID1 : Template.SpellID;

            if (spellId == 0)
                return null;

            int unlock = UnlockLevelOf(spellId);
            int level = ArtifactLevel;

            if (level >= unlock)
                return null;

            Spell spell = SkillBase.FindSpell(spellId, SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects));
            return LockedText(Name, spell?.Name, unlock, level);
        }

        public static string LockedText(string artifact, string ability, int unlock, int level)
        {
            return $"{(string.IsNullOrEmpty(ability) ? "This ability" : ability)} on your {artifact} unlocks at artifact level {unlock}. It is level {level}: wear it while you earn experience to raise it.";
        }

        /// <summary>The line a credit item gives when used: credits are only turned in.</summary>
        public static string CreditUseText(string artifactName)
        {
            return $"This credit is not used directly. Give it to an Artifact Scholar to receive {artifactName}.";
        }

        private (int Type, int Value)[] CurrentBonuses()
        {
            return
            [
                (Template.Bonus1Type, Bonus1), (Template.Bonus2Type, Bonus2), (Template.Bonus3Type, Bonus3),
                (Template.Bonus4Type, Bonus4), (Template.Bonus5Type, Bonus5), (Template.Bonus6Type, Bonus6),
                (Template.Bonus7Type, Bonus7), (Template.Bonus8Type, Bonus8), (Template.Bonus9Type, Bonus9),
                (Template.Bonus10Type, Bonus10), (Template.ExtraBonusType, ExtraBonus)
            ];
        }

        /// <summary>Every experience change goes through here, so a worn item's bonuses stay in step.</summary>
        public void SetExperience(GamePlayer player, long experience)
        {
            int oldLevel = ArtifactLevel;
            (int Type, int Value)[] before = CurrentBonuses();

            Experience = Math.Clamp(experience, 0, ArtifactLevels.MaxXp);

            int newLevel = ArtifactLevel;

            if (newLevel == oldLevel)
                return;

            if (player == null)
                return;

            if (player.Inventory.EquippedItems.Contains(this))
            {
                (int Type, int Value)[] after = CurrentBonuses();

                for (int i = 0; i < before.Length; i++)
                {
                    int change = after[i].Value - before[i].Value;

                    if (change != 0)
                        player.ItemBonus[(eProperty) before[i].Type] += change;
                }

                player.RefreshEquipmentStats();
            }

            if (newLevel > oldLevel)
                player.Out.SendMessage($"{Name} has reached artifact level {newLevel}!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

            ArtifactProgress.RememberLevel(player, this);
        }

        public override void Delve(List<string> delve, GamePlayer player)
        {
            int level = ArtifactLevel;

            delve.Add($"Artifact level {level} of {ArtifactLevels.MaxLevel}");

            if (level < ArtifactLevels.MaxLevel)
            {
                long floor = ArtifactLevels.XpForLevel(level);
                long ceiling = ArtifactLevels.XpForLevel(level + 1);
                delve.Add($"{(Experience - floor) * 100 / (ceiling - floor)}% of the way to level {level + 1}");

                int[] abilities = AbilityIds();

                for (int rank = 0; rank < abilities.Length; rank++)
                {
                    int unlock = ArtifactLevels.AbilityUnlockLevel(rank);

                    if (level < unlock)
                    {
                        Spell spell = SkillBase.FindSpell(abilities[rank], SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects));
                        delve.Add($"Unlocks at level {unlock}: {spell?.Name ?? "a new ability"}");
                    }
                }
            }

            delve.Add(" ");
            base.Delve(delve, player);
        }
    }
}
