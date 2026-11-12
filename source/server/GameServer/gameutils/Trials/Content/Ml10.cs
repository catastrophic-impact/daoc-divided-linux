using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Trials
{
    /// <summary>
    /// Master Level 10: the five monsters of Celestius, fought on the hidden island of Aerus that the Celestius
    /// zone points return to. TrialZone has no entry for region 91, so a stone in the city lifts the party there.
    /// </summary>
    public static class Ml10
    {
        public const int RequiredMasterLevel = 9;
        public const int TimeLimit = 2400;
        public const int ReturnAtSeconds = TimeLimit - 20;
        public const int CheironSeconds = 8;
        public const int CheironCycle = 30;
        public const int ToothSeconds = 12;

        private const int LiftSeconds = 5;
        private const int Stone = 1823;

        /// <summary>The ground Celestius's own exit puts a player on (ZonePoint 143).</summary>
        public static readonly TrialPoint Isle = new(51377, 39790, 11231, 290);

        /// <summary>The city's zone-in ground (ZonePoint 140). The stones stand a little off it, in a line.</summary>
        private static readonly TrialPoint Entry = new(41979, 38173, 10341, 2257);

        public static IEnumerable<TrialDefinition> Trials()
        {
            yield return Sagittarius();
            yield return Scorpius();
            yield return Centaurus();
            yield return Leo();
            yield return Draco();
        }

        /// <summary>He wards himself for the last six seconds of every half minute.</summary>
        public static bool SagittariusWarded(int seconds) => seconds % 30 >= 24;

        public static bool CheironMortal(int seconds) => seconds % CheironCycle >= CheironCycle - CheironSeconds;

        /// <summary>How many of Leo's splits, at 80, 60, 40 and 20 percent, this much health has brought on.</summary>
        public static int LeoSplits(int healthPercent) => Math.Clamp((100 - healthPercent) / 20, 0, 4);

        private static TrialPoint Near(int dx, int dy) => new(Isle.X + dx, Isle.Y + dy, Isle.Z, Isle.Heading);

        private static TrialPoint Arena => Near(350, -175);

        private static TrialDefinition Step(int step, string name, string starter, string intro, string victory, params TrialPhase[] fight)
        {
            TrialDefinition definition = new()
            {
                ML = 10,
                Step = step,
                Name = name,
                Scope = TrialScope.Raid,
                Zone = TrialZone.Aerus,
                Anchor = new TrialPoint(Entry.X + 150 + 100 * step, Entry.Y - 150 - 100 * step, Entry.Z, Entry.Heading),
                StarterName = starter,
                StarterModel = Stone,
                Intro = intro,
                Victory = victory,
                RequiresSteps = Enumerable.Range(1, step - 1).ToArray(),
                // Refused before "<name> begins." and the stone's flare, which used to print first.
                Refuse = UnreadyReason,
                TimeLimitSeconds = TimeLimit,
                LeashRange = 14000,
                OnEnd = (run, won) =>
                {
                    if (!won)
                        MoveParty(run, run.Definition.Anchor);
                },
            };

            definition.Phases.Add(new TrialPhase
            {
                Announce = "The stone flares with cold starlight.",
                SurviveSeconds = LiftSeconds,
                OnStart = RefuseTheUnready,
            });
            definition.Phases.Add(new TrialPhase { SurviveSeconds = 1, OnStart = run => MoveParty(run, Isle) });
            definition.Phases.AddRange(fight);
            definition.Phases.Add(new TrialPhase { Announce = "The stars will send you back in a moment. Gather what you have won.", SurviveSeconds = 30 });
            definition.Phases.Add(new TrialPhase { SurviveSeconds = 1, OnStart = run => MoveParty(run, run.Definition.Anchor) });
            return definition;
        }

        public const string UnreadyText = "The stone stays dark. It answers only to those who have reached Master Level 9.";

        public static string UnreadyReason(GamePlayer leader) => leader.MLLevel < RequiredMasterLevel ? UnreadyText : null;

        /// <summary>Backstop for runs that skip TrialManager.TryStart (the GM sweep).</summary>
        private static void RefuseTheUnready(TrialRun run)
        {
            if (UnreadyReason(run.Leader) is string reason)
                run.End(false, reason);
        }

        private static void MoveParty(TrialRun run, TrialPoint point)
        {
            IEnumerable<GameLiving> group = run.Leader.Group?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>();
            int slot = 0;
            foreach (GameLiving member in run.Players.Concat(group).Distinct().Where(member => member.IsAlive).ToList())
            {
                member.MoveTo(run.Starter.CurrentRegionID, point.X + slot * 50, point.Y + slot % 2 * 50, point.Z, point.Heading);
                slot++;
            }
        }

        /// <summary>Wraps a fight's tick so a party that runs out of time isn't left on the island.</summary>
        private static Action<TrialRun> Timed(Action<TrialRun> tick) => run =>
        {
            if (Ml9.Clock(run, "elapsed") == ReturnAtSeconds)
                MoveParty(run, run.Definition.Anchor);
            tick(run);
        };

        private static TrialMobSpec Portal() => new()
        {
            Name = "Celestial Stone", Model = Stone, Level = 60, Size = 60, Health = 1000, DamageFactor = 0.05, At = Isle, AggroRange = 0, Immune = true, Required = false, Tag = "portal",
        };

        private static TrialDefinition Sagittarius() => Step(1, "Sagittarius", "Celestial Stone of the Archer",
            "This stone answers the old sky. It will carry you to the hidden isle of Aerus, where the gate to the heavens stands, and the Archer will be waiting. Do not linger by the stone when you land: it burns. He wards himself against every blow at intervals, so save your strength for when the ward falls.",
            "Sagittarius's bow falls silent, and a portal tears open behind him.",
            new TrialPhase
            {
                Announce = "Sagittarius draws his bow!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Sagittarius", Model = 1214, Level = 68, Size = 130, Health = 110000, DamageFactor = 1.4, At = Arena, MeleeDamage = eDamageType.Thrust, Hurts = TrialDamageKind.Melee | TrialDamageKind.Ranged, Tag = "boss" },
                },
                Pulses = { new TrialPulse { SourceTag = "portal", IntervalSeconds = 6, Damage = 60, Radius = 300, DamageType = eDamageType.Energy } },
                OnStart = run => Ml9.Calm(run, "portal"),
                OnTick = Timed(SagittariusTick),
            });

        private static void SagittariusTick(TrialRun run)
        {
            int seconds = Ml9.Clock(run);
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;

            boss.Immune = SagittariusWarded(seconds);
            if (seconds % 30 == 24)
                run.Tell("Sagittarius wreathes himself in starlight! Nothing can touch him!");
            if (seconds % 5 == 0 && Ml9.RandomTarget(run, boss, 3000) is { } target)
                Ml9.Hurt(run, boss, target, 180, eDamageType.Thrust);
        }

        private static TrialDefinition Scorpius() => Step(2, "Scorpius", "Celestial Stone of the Scorpion",
            "With the Archer gone, the Scorpion crawls through his gate. His plague leaps from one body to the next, so stand apart. He raises barriers around whoever he marks. And when he weakens the sky itself begins to fall, and it goes on falling after he is dead.",
            "The last of the stars burns out, and the gate opens again.",
            new TrialPhase
            {
                Announce = "Scorpius scuttles out of the gate!",
                Mobs =
                {
                    Portal(),
                    new TrialMobSpec { Name = "Scorpius", Model = 1262, Level = 69, Size = 150, Health = 120000, DamageFactor = 1.6, At = Arena, MeleeDamage = eDamageType.Thrust, Tag = "boss" },
                },
                OnStart = run => Ml9.Calm(run, "portal"),
                OnTick = Timed(ScorpiusTick),
                Done = run => !run.Mobs("boss").Any(),
            },
            new TrialPhase
            {
                Announce = "The stars are falling! Survive!",
                Pulses = { new TrialPulse { SourceTag = "portal", IntervalSeconds = 5, Damage = 280, Radius = 2500, DamageType = eDamageType.Heat, Message = "A star strikes the island!" } },
                SurviveSeconds = 20,
            });

        private static void ScorpiusTick(TrialRun run)
        {
            int seconds = Ml9.Clock(run);
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;

            Dictionary<GameLiving, int> plague = run.Get<Dictionary<GameLiving, int>>("plague") ?? new();
            run.Set("plague", plague);

            if (seconds % 20 == 0 && Ml9.RandomTarget(run, boss, 2500) is { } carrier)
                plague[carrier] = seconds;

            if (seconds % 3 == 0)
            {
                foreach (GameLiving victim in plague.Keys.Where(victim => !victim.IsAlive || seconds - plague[victim] >= 15).ToList())
                    plague.Remove(victim);

                foreach (GameLiving victim in plague.Keys.ToList())
                {
                    Ml9.Hurt(run, boss, victim, 60, eDamageType.Body);
                    foreach (GameLiving neighbour in run.Hostiles(victim, 250).Where(neighbour => !plague.ContainsKey(neighbour)))
                        plague[neighbour] = seconds;
                }
            }

            if (seconds % 45 == 0 && run.Mobs("barrier").Count() < 2 && Ml9.RandomTarget(run, boss, 2500) is { } marked)
            {
                run.Spawn(new TrialMobSpec { Name = "Celestial Barrier", Model = Stone, Level = 60, Size = 40, Health = 4000, DamageFactor = 0.5, At = Ml9.Beside(marked), Required = false, Tag = "barrier" });
                run.Tell("Scorpius throws up a barrier around his prey!");
            }

            if (boss.HealthPercent < 30 && seconds % 7 == 0)
                Ml9.Splash(run, boss, 2500, 300, 280, eDamageType.Heat, "A star falls among you!");
        }

        private static TrialDefinition Centaurus() => Step(3, "Centaurus", "Celestial Stone of the Healer",
            "The Centaur walks through the gate next, and he is too quick to outrun. Now and then he remembers the mortal he was, Cheiron, and his guard slips. The vials he drops hold the poison that once wounded him. Touch one while he is mortal and it will burn him deeply. While he is a god it is wasted.",
            "Cheiron closes his eyes, and the centaur's light goes out.",
            new TrialPhase
            {
                Announce = "Centaurus gallops out of the gate!",
                Mobs = { new TrialMobSpec { Name = "Centaurus", Model = 1213, Level = 69, Size = 130, Health = 130000, DamageFactor = 1.6, At = Arena, MeleeDamage = eDamageType.Thrust, Tag = "boss" } },
                Pulses = { new TrialPulse { SourceTag = "boss", IntervalSeconds = 9, Damage = 260, Radius = 450, DamageType = eDamageType.Energy } },
                OnTick = Timed(CentaurusTick),
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void CentaurusTick(TrialRun run)
        {
            int seconds = Ml9.Clock(run);
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;

            bool mortal = CheironMortal(seconds);
            if (mortal != run.Get("mortal", false))
            {
                run.Set("mortal", mortal);
                boss.Name = mortal ? "Cheiron" : "Centaurus";
                boss.Model = (ushort)(mortal ? 1214 : 1213);
                boss.Size = (byte)(mortal ? 80 : 130);
                run.Tell(mortal ? "The god falls away, and Cheiron stands there, mortal!" : "Centaurus blazes back into his divine form!");
            }

            if (seconds % 25 == 0 && run.Mobs("vial").Count() < 5)
            {
                run.Spawn(new TrialMobSpec
                {
                    Name = "vial of hydra's blood", Model = Stone, Level = 60, Size = 12, At = Ml9.Beside(boss, 200), Objective = true, Required = false, Tag = "vial", OnDeath = BreakVial,
                });
                run.Tell("A vial slips from Centaurus's belt and rolls across the ground!");
            }
        }

        private static void BreakVial(TrialRun run, TrialMob vial)
        {
            vial.Delete();
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;
            if (!run.Get("mortal", false))
            {
                run.Tell("The vial shatters against the god's hide and does nothing.");
                return;
            }
            boss.Health = Math.Max(1, boss.Health - boss.MaxHealth / 10);
            run.Tell("The hydra's blood sears Cheiron!");
        }

        private static TrialDefinition Leo() => Step(4, "Leo", "Celestial Stone of the Lion",
            "The Lion is next, and he will not stay whole. Each time you wound him deeply, pieces of him tear loose, and he can't be hurt until they are destroyed. His roar shakes the ground, and he hurls his prey into the sky to fall where it may.",
            "Leo's pieces fade into the night, and he roars no more.",
            new TrialPhase
            {
                Announce = "Leo springs through the gate!",
                Mobs = { new TrialMobSpec { Name = "Leo", Model = 1193, Level = 69, Size = 130, Health = 130000, DamageFactor = 1.5, At = Arena, MeleeDamage = eDamageType.Slash, Tag = "boss" } },
                Pulses = { new TrialPulse { SourceTag = "boss", IntervalSeconds = 12, Damage = 220, Radius = 700, DamageType = eDamageType.Energy, Message = "Leo roars!" } },
                OnTick = Timed(LeoTick),
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void LeoTick(TrialRun run)
        {
            int seconds = Ml9.Clock(run);
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;

            int due = LeoSplits(boss.HealthPercent);
            if (due > run.Get("splits", 0))
            {
                run.Set("splits", due);
                run.Spawn(new TrialMobSpec { Name = "shard of Leo", Model = 1193, Level = 66, Size = 70, Health = 5000, DamageFactor = 1.0, Count = 2, At = Ml9.Beside(boss, 200), MeleeDamage = eDamageType.Slash, Hurts = TrialDamageKind.Ranged | TrialDamageKind.Spell, Required = false, Tag = "shard" });
                run.Tell("Leo splits apart, and pieces of him leap at you!");
            }
            boss.Immune = run.Mobs("shard").Any();

            if (seconds % 20 == 0 && Ml9.RandomTarget(run, boss, 800) is { } prey)
            {
                prey.TakeDamage(boss, eDamageType.Crush, Math.Min(prey.MaxHealth * 3 / 10, 300), 0);
                run.Tell($"Leo flings {prey.Name} into the sky!");
            }
        }

        private static TrialDefinition Draco() => Step(5, "Draco", "Celestial Stone of the Dragon",
            "Only the Dragon remains, Ladon's old kin, and he is the end of the road. He hits harder than anything you have faced. The teeth he sheds take root: smash one at once, or within moments it will sprout minions. Fell him and the Arbiter will have one last honour for you.",
            "Draco crashes to the island, and the stars themselves go quiet. Seek out the Arbiter.",
            new TrialPhase
            {
                Announce = "Draco descends from the dark!",
                Mobs = { new TrialMobSpec { Name = "Draco", Model = 1687, Level = 70, Size = 150, Health = 150000, DamageFactor = 1.9, At = Arena, MeleeDamage = eDamageType.Slash, Tag = "boss" } },
                OnTick = Timed(DracoTick),
                Done = run => !run.Mobs("boss").Any(),
            });

        private static void DracoTick(TrialRun run)
        {
            int seconds = Ml9.Clock(run);
            TrialMob boss = Ml9.One(run, "boss");
            if (boss == null)
                return;

            if (Util.Chance(8))
                Ml9.HurtAround(run, boss, boss, 800, 280, eDamageType.Heat);

            Dictionary<TrialMob, int> teeth = run.Get<Dictionary<TrialMob, int>>("teeth") ?? new();
            run.Set("teeth", teeth);

            foreach (TrialMob tooth in run.Mobs("tooth").Where(tooth => teeth.TryGetValue(tooth, out int since) && seconds - since >= ToothSeconds).ToList())
            {
                TrialPoint root = Ml9.Beside(tooth, 0);
                tooth.Delete();
                if (run.Mobs("minion").Count() >= 6)
                    continue;
                run.Spawn(new TrialMobSpec { Name = "Ladon's minion", Model = 1687, Level = 66, Size = 45, Health = 4000, DamageFactor = 0.9, Count = 3, At = root, MeleeDamage = eDamageType.Slash, Required = false, Tag = "minion" });
                run.Tell("A tooth takes root, and Ladon's minions claw their way out!");
            }

            if (seconds % 35 == 0 && Ml9.RandomTarget(run, boss, 1500) is { } target)
            {
                teeth[run.Spawn(new TrialMobSpec
                {
                    Name = "tooth of Draco", Model = Stone, Level = 60, Size = 12, At = Ml9.Beside(target), Objective = true, Required = false, Tag = "tooth",
                    ObjectiveText = "You grind the tooth to splinters.", OnDeath = (_, tooth) => tooth.Delete(),
                })] = seconds;
                run.Tell("Draco sheds a tooth, and it bites into the ground!");
            }
        }
    }
}
