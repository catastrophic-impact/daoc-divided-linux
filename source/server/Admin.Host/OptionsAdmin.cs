using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DaocServer.Admin;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.Logging;
using OfflineDaoc.Configuration;

namespace DOL.GS.Admin
{
    /// <summary>
    /// What the Windows launcher's Options, Bot Goals and Battlegrounds tabs do (Offline DAoC 0.35),
    /// for daoc-admin. Changes apply to the running server and are saved for the next start.
    /// </summary>
    public static class OptionsAdmin
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The only server properties daoc-admin may change. Each is read live by the bot code.</summary>
        private static readonly string[] Keys =
        [
            "rvr_battleground_announcements",
            "pve_realm_event_announcements",
            "bot_use_town_teleporters",
            "bot_route_threat_awareness",
            "baf_companion_bots_count",
            "rvr_siege_staged_assault",
            "rvr_siege_defense_ratio",
            // daoc-divided-linux: battleground bracket bots
            "bracket_bots_enabled",
            "bracket_bots_auto_create",
            "bracket_bots_per_realm",
            "bracket_bots_fill_mode",
            "bracket_bots_keep_even",
            "bracket_bots_grace_seconds",
            // daoc-divided-linux: "no players, no bots" and the bot load governor
            "bots_only_with_players",
            "bots_only_with_players_grace_seconds",
            "load_governor_enabled",
            "load_governor_min_tps_percent",
            "load_governor_resume_tps_percent",
            "load_governor_min_free_mb",
            "load_governor_shed_after_seconds",
            // daoc-divided-linux: DividedRealms DR-dev-0.1g settings worth having on the Options tab (switches and whole numbers).
            // Bots
            "bots_use_custom_classes",
            "rvr_bot_perception",
            "rvr_bot_spotters",
            "rvr_group_pet_cap",
            "bot_group_invite_player",
            "bot_raid_invite_player",
            "bot_group_player_loot_weight",
            "bot_living_tales",
            "bot_living_tale_raids",
            "living_atmosphere_lines",
            // Realm points
            "rp_worth_seconds_released",
            "kill_rp_bonus_percent",
            "heal_rp_percent",
            "mez_rp_pool_percent",
            "stun_rp_pool_percent",
            "repair_rp_percent",
            "repair_rp_cap",
            "underpopulated_rp_bonus",
            "siege_contribution_keep_percent",
            "siege_contribution_tower_percent",
            // Quests, login, relics, loot
            "quest_xp_by_level",
            "login_account_notice",
            "relic_players_ignore_pad_count",
            "artifact_glass_named_max_spawns",
        ];

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static List<OptionInfo> List()
        {
            var all = Properties.AllDomainProperties;
            return Keys.Where(all.ContainsKey).Select(key => ToInfo(all[key])).ToList();
        }

        public static OptionInfo Set(string key, string value)
        {
            if (!Keys.Contains(key))
                throw new AdminException(AdminErrorCodes.Invalid, $"Unknown option '{key}'. Options: {string.Join(", ", Keys)}");
            var all = Properties.AllDomainProperties;
            if (!all.TryGetValue(key, out var property))
                throw new AdminException(AdminErrorCodes.Failed, $"Option '{key}' is not defined by this server.");

            object parsed = Parse(property.Item1.DefaultValue, value, key);
            Store(property, parsed);
            Log.Info($"Admin set {key} = {Format(parsed)}");
            return ToInfo(property);
        }

        /// <summary>Applies a value to the running server and saves it for the next start.</summary>
        private static void Store(Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty> property, object value)
        {
            (_, FieldInfo field, DbServerProperty row) = property;
            field.SetValue(null, value);
            row.Value = Format(value);
            if (row.IsPersisted)
                GameServer.Database.SaveObject(row);
            else
                GameServer.Database.AddObject(row);
        }

        /// <summary>The server rules tab: rate multipliers only, each a number the game code reads live.</summary>
        private static readonly string[] RuleKeys =
        [
            "xp_rate",
            "rvr_zones_xp_rate",
            "rp_rate",
            "bp_rate",
            "money_drop",
            "bot_xp_rate",
            "health_regen_amount_modifier",
            "mana_regen_amount_modifier",
            "endurance_regen_amount_modifier",
        ];

        private const double RuleMin = 0.1;
        private const double RuleMax = 100;

        public static List<OptionInfo> Rules()
        {
            var all = Properties.AllDomainProperties;
            return RuleKeys.Where(all.ContainsKey).Select(key => ToInfo(all[key])).ToList();
        }

        public static OptionInfo SetRule(string key, string value)
        {
            var property = RuleProperty(key);
            if (!double.TryParse(value.Trim(), NumberStyles.Float, Invariant, out double number) || number < RuleMin || number > RuleMax)
                throw new AdminException(AdminErrorCodes.Invalid, $"{key} must be a number from {RuleMin} to {RuleMax} (1 is normal, 2 is double).");
            Store(property, number);
            Log.Info($"Admin set rule {key} = {Format(number)}");
            return ToInfo(property);
        }

        /// <summary>Puts one rule, or all of them with "all", back to the value the server code defines.</summary>
        public static List<OptionInfo> ResetRules(string key)
        {
            string[] keys = key == "all" ? RuleKeys : [key];
            var changed = new List<OptionInfo>();
            foreach (string each in keys)
            {
                if (!Properties.AllDomainProperties.ContainsKey(each))
                    continue;
                var property = RuleProperty(each);
                Store(property, property.Item1.DefaultValue);
                changed.Add(ToInfo(property));
            }
            Log.Info($"Admin reset rule(s) to default: {key}");
            return changed;
        }

        private static Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty> RuleProperty(string key)
        {
            if (!RuleKeys.Contains(key))
                throw new AdminException(AdminErrorCodes.Invalid, $"Unknown rule '{key}'. Rules: {string.Join(", ", RuleKeys)}");
            return Properties.AllDomainProperties.TryGetValue(key, out var property)
                ? property
                : throw new AdminException(AdminErrorCodes.Failed, $"Rule '{key}' is not defined by this server.");
        }

        public static BotGoalsInfo Goals() => ToInfo(AutonomousBotGoalPolicy.Settings, AutonomousBotGoalPolicy.IsConfigured);

        public static BotGoalsInfo SetGoals(string bracket, int solo, int group, int rvr, int battlegrounds)
        {
            var row = new BotGoalWeights(solo, group, rvr, battlegrounds);
            BotGoalSettings current = AutonomousBotGoalPolicy.Settings;
            BotGoalSettings updated = bracket switch
            {
                "1-19" => current with { Levels1To19 = row },
                "20-49" => current with { Levels20To49 = row },
                "50" => current with { Level50 = row },
                _ => throw new AdminException(AdminErrorCodes.Invalid, $"Unknown level bracket '{bracket}'. Use 1-19, 20-49 or 50."),
            };
            try
            {
                updated.Validate();
            }
            catch (InvalidDataException exception)
            {
                throw new AdminException(AdminErrorCodes.Invalid, exception.Message);
            }
            AutonomousBotGoalPolicy.Apply(updated);
            Log.Info($"Admin set bot goals for levels {bracket}: solo {solo}, group {group}, RvR {rvr}, battlegrounds {battlegrounds}");
            return ToInfo(updated, true);
        }

        public static RvrInfo Rvr()
        {
            AutonomousRvrDashboard.Snapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<AutonomousRvrDashboard.Snapshot>(File.ReadAllText(AutonomousRvrDashboard.FilePath));
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                throw new AdminException(AdminErrorCodes.NotFound, "No RvR snapshot yet; the server writes one within 30 seconds of starting.");
            }
            return new RvrInfo(
                snapshot.UpdatedUtc,
                (snapshot.Objectives ?? []).Select(o => new RvrObjectiveInfo(o.Kind, o.Name, o.Owner, o.State, o.Location, o.Forces)).ToList(),
                (snapshot.Battlegrounds ?? []).Select(b => new BattlegroundInfo(b.Name, b.MinLevel, b.MaxLevel, b.CentralKeep, b.Owner,
                    b.AlbionInside, b.MidgardInside, b.HiberniaInside, b.AlbionTravelling, b.MidgardTravelling, b.HiberniaTravelling)).ToList());
        }

        private static object Parse(object defaultValue, string value, string key)
        {
            value = value.Trim();
            switch (defaultValue)
            {
                case bool:
                    return value.ToLowerInvariant() switch
                    {
                        "on" or "true" or "yes" or "1" => true,
                        "off" or "false" or "no" or "0" => false,
                        _ => throw new AdminException(AdminErrorCodes.Invalid, $"{key} is a switch: use on or off."),
                    };
                case double:
                    // A share of the attacker cap; above 1 defenders would outnumber the attackers' cap.
                    return double.TryParse(value, NumberStyles.Float, Invariant, out double number) && number is >= 0 and <= 1
                        ? number
                        : throw new AdminException(AdminErrorCodes.Invalid, $"{key} must be a number from 0 to 1 (for example 0.5).");
                case int:
                {
                    if (!int.TryParse(value, NumberStyles.Integer, Invariant, out int whole) || whole < 0 || whole > 100_000)
                        throw new AdminException(AdminErrorCodes.Invalid, $"{key} must be a whole number.");
                    if (key == "bracket_bots_per_realm" && whole < BracketBotProperties.MinimumPerRealm)
                        throw new AdminException(AdminErrorCodes.Invalid, $"{key} must be at least {BracketBotProperties.MinimumPerRealm} (a battleground wave needs 8 bots of one realm).");
                    return whole;
                }
                case string:
                    if (key == "bracket_bots_fill_mode")
                        return value.ToLowerInvariant() is "fill" or "extra" ? value.ToLowerInvariant()
                            : throw new AdminException(AdminErrorCodes.Invalid, "bracket_bots_fill_mode is fill or extra.");
                    throw new AdminException(AdminErrorCodes.Failed, $"{key} has a type daoc-admin cannot set.");
                default:
                    throw new AdminException(AdminErrorCodes.Failed, $"{key} has a type daoc-admin cannot set.");
            }
        }

        private static string Format(object value) => value switch
        {
            bool flag => flag ? "True" : "False",
            double number => number.ToString(Invariant),
            _ => Convert.ToString(value, Invariant),
        };

        private static OptionInfo ToInfo(Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty> property)
        {
            (ServerPropertyAttribute attribute, FieldInfo field, _) = property;
            object value = field.GetValue(null);
            return new OptionInfo(attribute.Key, value is bool ? "switch" : "number", Display(value), Display(attribute.DefaultValue), attribute.Description);
        }

        private static string Display(object value) => value is bool flag ? (flag ? "on" : "off") : Format(value);

        private static BotGoalsInfo ToInfo(BotGoalSettings settings, bool saved) => new(
            [
                Row("1-19", settings.Levels1To19),
                Row("20-49", settings.Levels20To49),
                Row("50", settings.Level50),
            ],
            saved);

        private static GoalRow Row(string bracket, BotGoalWeights w) => new(bracket, w.SoloPve, w.GroupPve, w.RvR, w.Battlegrounds);
    }
}
