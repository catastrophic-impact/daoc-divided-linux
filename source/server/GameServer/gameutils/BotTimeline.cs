using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// A short history per autonomous gamebot for the Bot Watch admin view: every few seconds a sampler compares each
    /// bot with its last snapshot and records what changed (goal, activity, region or zone, level, deaths, alive,
    /// fights, going stalled). It reads only; nothing in the AI is hooked. Started with the admin endpoints
    /// (bots/bot_watch_admin), so release worlds never run it.
    /// </summary>
    public static class BotTimeline
    {
        public const int MaxEvents = 60;
        public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(15);

        public sealed record Snapshot(string Goal, string Activity, ushort Region, string Zone, int Level, int Deaths,
            bool Alive, bool InCombat, string Target, bool Stalled);

        public sealed record Event(DateTime Utc, string Kind, string Text);

        private static readonly ConcurrentDictionary<long, Snapshot> Last = new();
        private static readonly ConcurrentDictionary<long, Queue<Event>> Events = new();
        private static Timer _timer;
        private static int _sampling;

        /// <summary>What changed between two snapshots, oldest-first order of importance. Pure: unit tested.</summary>
        public static List<Event> Compare(Snapshot before, Snapshot now, DateTime utc)
        {
            var events = new List<Event>();
            if (now == null)
                return events;
            if (before == null)
            {
                events.Add(new Event(utc, "seen", $"level {now.Level} in {Where(now)}; goal: {Or(now.Goal)}"));
                return events;
            }
            if (now.Deaths > before.Deaths || (before.Alive && !now.Alive))
                events.Add(new Event(utc, "death", $"died in {Where(now)}" + (string.IsNullOrEmpty(before.Target) ? "" : $" fighting {before.Target}")));
            else if (!before.Alive && now.Alive)
                events.Add(new Event(utc, "alive", $"alive again in {Where(now)}"));
            if (now.Level > before.Level)
                events.Add(new Event(utc, "level", $"reached level {now.Level}"));
            if (now.Region != before.Region || now.Zone != before.Zone)
                events.Add(new Event(utc, "move", $"{Where(before)} -> {Where(now)}"));
            if (now.Goal != before.Goal)
                events.Add(new Event(utc, "goal", $"goal: {Or(now.Goal)}"));
            if (!before.InCombat && now.InCombat)
                events.Add(new Event(utc, "fight", $"fight started" + (string.IsNullOrEmpty(now.Target) ? "" : $" with {now.Target}")));
            else if (before.InCombat && !now.InCombat && now.Alive)
                events.Add(new Event(utc, "fight", "fight ended"));
            if (!before.Stalled && now.Stalled)
                events.Add(new Event(utc, "stalled", $"no progress for {StallAfter.TotalMinutes:0}+ min ({Or(now.Activity)})"));
            else if (before.Stalled && !now.Stalled)
                events.Add(new Event(utc, "stalled", "making progress again"));
            if (now.Activity != before.Activity && !string.IsNullOrEmpty(now.Activity))
                events.Add(new Event(utc, "activity", now.Activity));
            return events;
        }

        public static void Record(long botId, IEnumerable<Event> events)
        {
            Queue<Event> q = Events.GetOrAdd(botId, _ => new Queue<Event>());
            lock (q)
            {
                foreach (Event e in events)
                {
                    q.Enqueue(e);
                    while (q.Count > MaxEvents)
                        q.Dequeue();
                }
            }
        }

        public static Event[] For(long botId)
        {
            if (!Events.TryGetValue(botId, out Queue<Event> q))
                return [];
            lock (q)
                return q.Reverse().ToArray();     // newest first
        }

        public static void Forget(long botId)
        {
            Last.TryRemove(botId, out _);
            Events.TryRemove(botId, out _);
        }

        private static string Where(Snapshot s) => string.IsNullOrEmpty(s.Zone) ? $"region {s.Region}" : s.Zone;

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "(none)" : s;

        // ------------------------------------------------------------ sampler

        public static void Start()
        {
            _timer ??= new Timer(_ => Sample(), null, SampleEvery, SampleEvery);
        }

        public static void Stop()
        {
            Interlocked.Exchange(ref _timer, null)?.Dispose();
        }

        private static void Sample()
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1)
                return;                                   // the last sample is still running
            try
            {
                DateTime now = DateTime.UtcNow;
                var seen = new HashSet<long>();
                foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
                {
                    try
                    {
                        OfflineWorldBotRecord rec = bot?.PersistentRecord;
                        if (rec == null || bot.ObjectState != GameObject.eObjectState.Active)
                            continue;
                        seen.Add(rec.BotId);
                        var snap = new Snapshot(rec.CurrentGoal ?? "", rec.Activity ?? "", bot.CurrentRegionID,
                            bot.CurrentZone?.Description ?? "", bot.Level, rec.DeathCount, bot.IsAlive, bot.InCombat,
                            bot.TargetObject?.Name ?? "", BotWatchAdmin.IsStalled(rec, bot.IsAlive, now, StallAfter));
                        Last.TryGetValue(rec.BotId, out Snapshot before);
                        List<Event> events = Compare(before, snap, now);
                        if (events.Count > 0)
                            Record(rec.BotId, events);
                        Last[rec.BotId] = snap;
                    }
                    catch (Exception e) when (e is NullReferenceException or InvalidOperationException) { }
                }
                foreach (long id in Last.Keys.Where(id => !seen.Contains(id)).ToList())
                {
                    Last.TryRemove(id, out _);            // logged out or retired; its events stay until restart
                }
            }
            finally
            {
                Volatile.Write(ref _sampling, 0);
            }
        }
    }
}
