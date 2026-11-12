using System;
using System.Collections.Generic;

namespace DOL.GS.Trials
{
    /// <summary>Who a trial is built for. Sets the party size scaling aims at, and who gets credit.</summary>
    public enum TrialScope
    {
        /// <summary>The starter alone: companions, bots and other players do no damage to its monsters.</summary>
        Solo,
        /// <summary>Tuned for a full group of eight.</summary>
        Group,
        /// <summary>Tuned for two groups.</summary>
        Raid,
    }

    /// <summary>A trial zone. Each realm has its own copy with the same coordinates.</summary>
    public enum TrialZone
    {
        Oceanus,
        CetusPit,
        Sobekite,
        Twilight,
        Maati,
        Pyramid,
        Volcanus,
        Aerus,
    }

    [Flags]
    public enum TrialDamageKind
    {
        Melee = 1,
        Ranged = 2,
        Spell = 4,
        All = Melee | Ranged | Spell,
    }

    public readonly record struct TrialPoint(int X, int Y, int Z, ushort Heading = 0);

    /// <summary>One monster type in a phase. Set either <see cref="At"/> or leave it spread around the anchor.</summary>
    public sealed class TrialMobSpec
    {
        public string Name;
        public ushort Model;
        public byte Level = 60;
        public byte Size = 60;
        /// <summary>Health for a party of the trial's full size, before scaling.</summary>
        public int Health = 5000;
        /// <summary>Melee strength for a party of the trial's full size, before scaling.</summary>
        public double DamageFactor = 1.0;
        public int Count = 1;
        /// <summary>World position; null spreads the monsters around the trial anchor at <see cref="Spread"/>.</summary>
        public TrialPoint? At;
        public int Spread = 300;
        public eDamageType MeleeDamage = eDamageType.Crush;
        public int AggroRange = 1500;
        /// <summary>Takes no damage until the run clears it (<see cref="TrialRun.SetImmune"/>).</summary>
        public bool Immune;
        /// <summary>Damage this monster can be hurt by; the rest does nothing.</summary>
        public TrialDamageKind Hurts = TrialDamageKind.All;
        /// <summary>Returns this share of damage taken to the attacker, 0 to 1.</summary>
        public double Reflect;
        /// <summary>False leaves this monster out of "kill them all".</summary>
        public bool Required = true;
        /// <summary>A peaceful thing to click instead of a monster. Clicking completes it.</summary>
        public bool Objective;
        public string ObjectiveText;
        /// <summary>Lets several specs be found together with <see cref="TrialRun.Mobs(string)"/>.</summary>
        public string Tag;
        public Action<TrialRun, TrialMob> OnDeath;
    }

    /// <summary>Area damage on a timer, from each living monster with the given tag or, without one, the anchor.</summary>
    public sealed class TrialPulse
    {
        public int IntervalSeconds = 10;
        public int Damage = 100;
        public int Radius = 500;
        public string SourceTag;
        public eDamageType DamageType = eDamageType.Heat;
        public string Message;
    }

    /// <summary>
    /// One stage of a run. It ends when <see cref="Done"/> says so, or else when <see cref="SurviveSeconds"/>
    /// have passed, or else when its required monsters and objectives are gone.
    /// </summary>
    public sealed class TrialPhase
    {
        public string Announce;
        public List<TrialMobSpec> Mobs = new();
        public List<TrialPulse> Pulses = new();
        public int SurviveSeconds;
        /// <summary>Kill only this many; 0 means all required monsters.</summary>
        public int KillCount;
        public Action<TrialRun> OnStart;
        /// <summary>Runs once a second.</summary>
        public Action<TrialRun> OnTick;
        public Func<TrialRun, bool> Done;
    }

    /// <summary>One trial. Hooks run on the run's own timer tick, one at a time, so they may spawn and remove freely.</summary>
    public sealed class TrialDefinition
    {
        public int ML;
        public int Step;
        public string Name;
        public TrialScope Scope = TrialScope.Group;
        public TrialZone Zone;
        /// <summary>Where the starter stands and the run is centred.</summary>
        public TrialPoint Anchor;
        public string StarterName;
        public ushort StarterModel = 1903;
        /// <summary>Popup text when a player talks to the starter.</summary>
        public string Intro;
        /// <summary>Told to the run on success.</summary>
        public string Victory;
        /// <summary>Steps of the same Master Level to finish first. Step 10 of ML1-9 also needs 1-9 unless this is set.</summary>
        public int[] RequiresSteps;
        /// <summary>A reason the leader may not start this trial at all, or null. Checked before the run begins.</summary>
        public Func<GamePlayer, string> Refuse;
        /// <summary>Monster names imported from the world database that this trial replaces. Removed at runtime only.</summary>
        public string[] Replaces = Array.Empty<string>();
        /// <summary>Removes <see cref="Replaces"/> only this close to the anchor; 0 means the whole region.</summary>
        public int ReplaceRadius;
        /// <summary>
        /// Aggressive world monsters (realm 0) this close to the anchor are removed at runtime, so the starter doesn't
        /// stand in a camp (playtest 2026-11-07: 29 of 86 anchors had one within 500). 0 keeps the camp.
        /// </summary>
        public int ClearRadius = TrialManager.DefaultClearRadius;
        public int TimeLimitSeconds = 1800;
        public int ResetSeconds = 120;
        /// <summary>Participants this far away count as having left.</summary>
        public int LeashRange = 8000;
        /// <summary>Runs as a run ends, won or lost, before monsters leave. The flag is true on success.</summary>
        public Action<TrialRun, bool> OnEnd;
        public List<TrialPhase> Phases = new();
        public string Key => $"{ML}.{Step}";
    }
}
