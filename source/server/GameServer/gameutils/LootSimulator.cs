using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Rolls a mob's loot many times without dropping or saving anything, so drop rates can be measured
    /// before and after a change. Generators with side effects (one-time drops write the character's record,
    /// timed XP items move the player's timer) check <see cref="IsSimulating"/> and stay out of the roll.
    /// </summary>
    public static class LootSimulator
    {
        // Runs on the command's thread; random items are costly to build, so keep a run to a few seconds.
        public const int MaxKills = 20_000;

        [ThreadStatic]
        private static bool _simulating;

        public static bool IsSimulating => _simulating;

        public sealed class Result
        {
            public int Kills;
            public int KillsWithItem;
            public long Coin;
            public int RandomItems;
            public double RandomItemUtility;
            public double RandomItemBestUtility;
            public int RandomItemBonusLines;
            public readonly Dictionary<string, int> Items = new(StringComparer.OrdinalIgnoreCase);

            public double RandomItemRate => Kills == 0 ? 0 : RandomItems * 100.0 / Kills;
            public double AverageUtility => RandomItems == 0 ? 0 : RandomItemUtility / RandomItems;
            public double AverageBonusLines => RandomItems == 0 ? 0 : (double)RandomItemBonusLines / RandomItems;
            public long CoinPerKill => Kills == 0 ? 0 : Coin / Kills;
        }

        /// <summary>Rolls <paramref name="mob"/>'s loot as if <paramref name="killer"/> killed it, <paramref name="kills"/> times.</summary>
        public static Result Run(GameNPC mob, GameObject killer, int kills)
        {
            return Run(() => LootMgr.GetLoot(mob, killer), kills);
        }

        public static Result Run(Func<DbItemTemplate[]> roll, int kills)
        {
            kills = Math.Clamp(kills, 1, MaxKills);
            var result = new Result();
            bool outer = _simulating;
            _simulating = true;
            try
            {
                for (int i = 0; i < kills; i++)
                    Tally(result, roll());
            }
            finally
            {
                _simulating = outer;
            }

            return result;
        }

        private static void Tally(Result result, DbItemTemplate[] drops)
        {
            result.Kills++;
            bool anyItem = false;
            foreach (DbItemTemplate drop in drops ?? [])
            {
                if (drop == null)
                    continue;

                if (GameMoney.IsItemMoney(drop.Name))
                {
                    result.Coin += drop.Price;
                    continue;
                }

                anyItem = true;
                if (drop is GeneratedUniqueItem rog)
                {
                    double utility = rog.BountyUtility;
                    result.RandomItems++;
                    result.RandomItemUtility += utility;
                    result.RandomItemBestUtility = Math.Max(result.RandomItemBestUtility, utility);
                    result.RandomItemBonusLines += BonusLines(rog);
                    continue;
                }

                result.Items[drop.Name] = result.Items.TryGetValue(drop.Name, out int count) ? count + 1 : 1;
            }

            if (anyItem)
                result.KillsWithItem++;
        }

        private static int BonusLines(DbItemTemplate item)
        {
            int[] types = [item.Bonus1Type, item.Bonus2Type, item.Bonus3Type, item.Bonus4Type, item.Bonus5Type, item.Bonus6Type,
                item.Bonus7Type, item.Bonus8Type, item.Bonus9Type, item.Bonus10Type, item.ExtraBonusType];
            int[] values = [item.Bonus1, item.Bonus2, item.Bonus3, item.Bonus4, item.Bonus5, item.Bonus6,
                item.Bonus7, item.Bonus8, item.Bonus9, item.Bonus10, item.ExtraBonus];
            return types.Where((type, i) => type != 0 && values[i] != 0).Count();
        }

        /// <summary>The report the GM command shows.</summary>
        public static List<string> Report(string mobName, string killerName, Result result, int topItems = 30)
        {
            var text = new List<string>
            {
                $"{result.Kills:N0} simulated kills of {mobName} by {killerName}",
                "One-time drops and timed XP items are left out.",
                "",
                $"Kills with any item: {Percent(result.KillsWithItem, result.Kills)}",
                $"Random items: {result.RandomItems:N0} ({result.RandomItemRate:0.##} per 100 kills)",
            };

            if (result.RandomItems > 0)
            {
                text.Add($"  average utility {result.AverageUtility:0.#}, best {result.RandomItemBestUtility:0.#}, " +
                         $"average bonus lines {result.AverageBonusLines:0.##}");
            }

            text.Add($"Coin per kill: {Coin(result.CoinPerKill)}");
            text.Add("");

            if (result.Items.Count == 0)
            {
                text.Add("No table items dropped.");
                return text;
            }

            text.Add("Table items (per 100 kills):");
            foreach (var pair in result.Items.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(topItems))
                text.Add($"  {pair.Key}: {pair.Value:N0} ({pair.Value * 100.0 / result.Kills:0.##})");

            if (result.Items.Count > topItems)
                text.Add($"  ...and {result.Items.Count - topItems} more");

            return text;
        }

        // Money.GetString needs the language tables; this report is also built in tests without them.
        private static string Coin(long copper)
        {
            int platinum = Money.GetPlatinum(copper) + Money.GetMithril(copper) * 1000;
            string text = $"{Money.GetGold(copper)}g {Money.GetSilver(copper)}s {Money.GetCopper(copper)}c";
            return platinum > 0 ? $"{platinum}p {text}" : text;
        }

        private static string Percent(int part, int whole) => whole == 0 ? "0%" : $"{part * 100.0 / whole:0.##}%";
    }
}
