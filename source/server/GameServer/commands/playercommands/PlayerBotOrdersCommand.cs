using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.Events;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /bots hunt: the bots in the player's group find their own fights around the player (owner 2026-10-30: "the tanks and
    /// dps would go and attack stuff while the healer healed"). Unlike /grind it takes any bot in the group, gamebots too, and
    /// the player is free to move, heal and stand back. It only picks the next monster: each pull goes through the /pull
    /// dispatch, so tanks lead, attackers follow and healers stay with the player.
    /// </summary>
    public static class PlayerBotHunt
    {
        public const int HuntRadius = 1500;
        public const int ContactTimeout = 35_000;
        public const int MinHealthPercent = 70;
        public const int MinPowerPercent = 30;
        private static readonly ConcurrentDictionary<GamePlayer, Session> Sessions = new();
        private static System.Threading.Timer _timer;

        private sealed class Session
        {
            public GamePlayer Player;
            public Group Group;
            public int Queued;
            public GameNPC Target;
            public long ContactDeadline;
            public long NextSearch;
            public string Detail = "Looking for a fight";
            public readonly Dictionary<GameNPC, long> FailedTargets = new();
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ =>
            {
                foreach (Session session in Sessions.Values)
                    if (Interlocked.Exchange(ref session.Queued, 1) == 0)
                        NpcService.Instance.Post(Tick, session);
            }, null, 1000, 1000);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose(); _timer = null; Sessions.Clear();
        }

        public static bool IsActive(GamePlayer player) => player != null && Sessions.ContainsKey(player);

        public static string Status(GamePlayer player) =>
            Sessions.TryGetValue(player, out Session session) ? session.Detail : null;

        /// <summary>The bots that take this player's orders: those in the group that follow the player.</summary>
        public static GameBot[] BotsOf(GamePlayer player) =>
            player?.Group?.GetMembersInTheGroup().OfType<GameBot>().Where(bot => bot.PlayerGroupLeader == player).ToArray() ?? [];

        /// <summary>Grey gives nothing; one bot stops at yellow, a pair at orange and a bigger party at red. Pure.</summary>
        public static bool IsHuntTarget(int conLevel, int hunters) => conLevel > -3 && conLevel <= (hunters >= 3 ? 2 : hunters == 2 ? 1 : 0);

        /// <summary>Whether a member can go into the next fight (a song class never fills its power, so it doesn't count that). Pure.</summary>
        public static bool ReadyForPull(bool alive, int healthPercent, int maxPower, int powerPercent, bool songClass) =>
            alive && healthPercent >= MinHealthPercent && (maxPower <= 0 || songClass || powerPercent >= MinPowerPercent);

        private static bool Ready(GameLiving member) => ReadyForPull(member.IsAlive, member.HealthPercent, member.MaxMana, member.ManaPercent,
            member is GameBot { CharacterClass: not null } bot && PlayerCompanionGrind.IsSongUpkeepClass((eCharacterClass)bot.CharacterClass.ID));

        public static bool TryStart(GamePlayer player, out string message)
        {
            if (BotsOf(player).Length == 0)
            { message = "You have no bots following you. Invite some to your group first."; return false; }
            if (PlayerCompanionGrind.IsActive(player))
            { message = "Use /grind stop before /bots hunt."; return false; }
            if (!Sessions.TryAdd(player, new Session { Player = player, Group = player.Group }))
            { message = "Your bots are already hunting."; return false; }
            PlayerBotStay.Release(BotsOf(player));      // hunting means roaming with you: a stay order ends
            message = $"Your bots are hunting within {HuntRadius} units of you: tanks pull, attackers join, healers stay with you. " +
                "They start the next fight when everyone is ready. /bots follow calls them back.";
            return true;
        }

        public static bool Stop(GamePlayer player) => player != null && Sessions.TryRemove(player, out _);

        /// <summary>Stop hunting and drop any fight: every bot comes back to the player.</summary>
        public static void CallBack(GamePlayer player)
        {
            Stop(player);
            PlayerBotStay.Release(BotsOf(player));
            PlayerLedPullCoordinator.OnGroupThreat(player);
            foreach (GameBot bot in BotsOf(player))
            {
                if (bot.Brain is BotBrain brain)
                    brain.PrepareForTankPull();
                bot.EnterPlayerLedGroup(player);
                PlayerLedStragglers.RegroupNow(bot, player);
            }
        }

        private static void Tick(Session session)
        {
            GamePlayer player = session.Player;
            try
            {
                if (!Sessions.TryGetValue(player, out Session current) || current != session)
                    return;
                if (player.Client?.ClientState != GameClient.eClientState.Playing || player.ObjectState != GameObject.eObjectState.Active)
                { Stop(player); return; }
                GameBot[] bots = BotsOf(player);
                if (!player.IsAlive || player.Group != session.Group || bots.Length == 0)
                {
                    Stop(player);
                    ChatUtil.SendSystemMessage(player, !player.IsAlive ? "You died: your bots stop hunting." : "Your group changed: your bots stop hunting.");
                    return;
                }
                long now = GameLoop.GameLoopTime;
                GameLiving[] members = player.Group.GetMembersInTheGroup().ToArray();
                bool fighting = members.Any(PlayerCompanionGrind.Fighting);
                if (session.Target != null)
                {
                    if (!session.Target.IsAlive || session.Target.ObjectState != GameObject.eObjectState.Active)
                        session.Target = null;
                    else if (fighting)
                        session.ContactDeadline = now + ContactTimeout;
                    else if (now >= session.ContactDeadline)
                    {
                        session.FailedTargets[session.Target] = now + 120_000;
                        session.Target = null;
                    }
                }
                if (fighting || session.Target != null || members.Any(PlayerCompanionGrind.CombatIntent))
                { session.Detail = session.Target == null ? "Fighting" : $"Fighting {session.Target.Name} ({session.Target.Level})"; return; }
                if (player.Steed != null || player.IsOnHorse)
                { session.Detail = "Waiting: you are riding"; return; }
                GameLiving notReady = members.FirstOrDefault(member => !Ready(member));
                if (notReady != null)
                { session.Detail = $"Waiting for {notReady.Name} to recover"; return; }
                GameBot[] hunters = bots.Where(bot => PlayerLedPullCoordinator.Available(bot, player) && !BotPartyRoles.IsSupport(bot)).ToArray();
                GameBot puller = PlayerCompanionGrind.SelectPuller(hunters);
                if (puller == null)
                { session.Detail = "Waiting: no tank or attacker is near you"; return; }
                if (now < session.NextSearch)
                    return;
                session.NextSearch = now + 3000;
                GameNPC target = PlayerCompanionGrind.FindNearbyPull(player, puller, HuntRadius, session.FailedTargets, now,
                    npc => IsHuntTarget(player.GetConLevel(npc), hunters.Length));
                if (target == null)
                { session.Detail = "No suitable monsters near you. Move on and they will follow."; return; }
                session.Target = target;
                session.ContactDeadline = now + ContactTimeout;
                session.Detail = PlayerLedPullCoordinator.Begin(player, target);
            }
            catch (Exception exception)
            {
                Stop(player);
                ChatUtil.SendSystemMessage(player, $"Your bots stop hunting ({exception.GetType().Name}).");
            }
            finally
            {
                Volatile.Write(ref session.Queued, 0);
            }
        }
    }

    [CmdAttribute("&bots", ePrivLevel.Player, "Orders for the bots in your group",
        "/bots hunt - tanks and attackers find their own fights near you, healers stay with you",
        "/bots attack - they attack your target now",
        "/bots follow - stop hunting, drop the fight and come back to you",
        "/bots stay - hold the spot they stand on (they still defend themselves); /bots follow brings them back",
        "/bots status - who is following you and what they are doing",
        "/bots invites on|off - whether bots invite you to groups")]
    public sealed class PlayerBotOrdersCommand : AbstractCommandHandler, ICommandHandler
    {
        private static string BotsStatus(GamePlayer player, GameBot[] bots)
        {
            string[] with = bots.Where(bot => PlayerLedStragglers.IsWith(bot, player)).Select(bot => bot.Name).ToArray();
            string[] away = bots.Where(bot => !PlayerLedStragglers.IsWith(bot, player)).Select(bot => bot.Name).ToArray();
            return (with.Length > 0 ? $"Following you: {string.Join(", ", with)}. " : "") +
                   (away.Length > 0 ? $"On the way to you: {string.Join(", ", away)}. " : "") +
                   (PlayerBotStay.Names(player, bots) is { Length: > 0 } staying ? $"Staying put: {staying}. " : "") +
                   (PlayerBotHunt.Status(player) is string status ? $"Hunting: {status}." : "Not hunting.");
        }

        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            switch (args.Length < 2 ? "" : args[1].ToLowerInvariant())
            {
                case "hunt":
                    PlayerBotHunt.TryStart(player, out string message);
                    DisplayMessage(client, message);
                    break;
                case "attack":
                case "assist":
                    if (player.TargetObject is not GameLiving { IsAlive: true } target)
                        DisplayMessage(client, "Select a living enemy first, then type /bots attack.");
                    else if (GameServer.ServerRules.IsAllowedToAttack(player, target, false))
                        DisplayMessage(client, PlayerLedPullCoordinator.Begin(player, target));
                    break;
                case "follow":
                case "stop":
                    PlayerBotHunt.CallBack(player);
                    DisplayMessage(client, "Your bots stop what they are doing and come back to you.");
                    break;
                case "stay":
                case "hold":
                    PlayerBotHunt.Stop(player);
                    int held = PlayerBotStay.Hold(player, PlayerBotHunt.BotsOf(player));
                    DisplayMessage(client, held == 0
                        ? "You have no bots following you. Invite some to your group first."
                        : $"Your {(held == 1 ? "bot holds its" : $"{held} bots hold their")} spot. They still fight anything that attacks them " +
                          "and still take /bots attack. /bots follow brings them back; leaving the region ends it.");
                    break;
                case "status":
                    GameBot[] bots = PlayerBotHunt.BotsOf(player);
                    DisplayMessage(client, bots.Length == 0 ? "No bots are following you." : BotsStatus(player, bots));
                    break;
                case "invites":
                    bool off = args.Length > 2 && args[2].Equals("off", StringComparison.OrdinalIgnoreCase);
                    AutonomousLivingServer.SetBotInvitesMuted(player, off);
                    DisplayMessage(client, off ? "Bots won't invite you to groups." : "Bots may invite you to groups.");
                    break;
                default:
                    DisplaySyntax(client);
                    break;
            }
        }
    }
}
