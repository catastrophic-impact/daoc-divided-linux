using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS {

    /// <summary>
    /// ROGMobGenerator
    /// At the moment this generator only adds ROGs to the loot
    /// </summary>
    public class ROGMobGenerator : LootGeneratorBase {

        //base chance in % is RandomItemLootRules.BaseChance (loot-rules.json)
        private static readonly Logger Log = LoggerManager.Create(typeof(ROGMobGenerator));
        private static long _nextErrorLogTick;


        /// <summary>
        /// Generate loot for given mob
        /// </summary>
        /// <param name="mob"></param>
        /// <param name="killer"></param>
        /// <returns></returns>
        public override LootList GenerateLoot(GameNPC mob, GameObject killer)
        {
            return GenerateLoot(LootContext.For(mob, killer));
        }

        public override LootList GenerateLoot(LootContext context)
        {
            GameNPC mob = context.Mob;
            GameObject killer = context.Killer;
            LootList loot = base.GenerateLoot(mob, killer);

            try
            {
                // BotBrain implements IControlledBrain but is NOT ControlledMobBrain.
                // More importantly a persistent bot's class pet has no human owner.
                // The context resolved the actual player-like owner before any class/realm rolls.
                GameLiving owner = context.Owner;
                if (owner == null)
                {
                    return loot;
                }

                int killedcon = owner.GetConLevel(mob);

                //grey con dont drop loot
                if (killedcon <= -3)
                {
                    return loot;
                }

                eCharacterClass classForLoot = GetLootClass(owner);
                // allow the leader to decide the loot realm
                if (owner.Group?.LivingLeader is GameLiving leader && IsLootParticipant(leader))
                    owner = leader;

                // chance to get a RoG Item; every number comes from loot-rules.json (defaults are the old constants)
                RandomItemLootRules rules = LootRulesFile.Current.RandomItems;
                bool named = context.IsNamed;
                bool frontier = context.IsFrontier;
                double chance = rules.BaseChance + ((killedcon < 0 ? killedcon + 1 : killedcon) * rules.ChancePerCon);

                //chance = 100;

                BattleGroup bg = (owner as GamePlayer)?.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);

                if (bg != null)
                {
                    var maxDropCap = bg.PlayerCount / rules.BattlegroupPlayersPerDrop;
                    if (maxDropCap < 1) maxDropCap = 1;
                    if (mob is GameEpicNPC)
                        maxDropCap *= rules.EpicBattlegroupDropMultiplier;
                    chance = rules.Scale(rules.BattlegroupChance, named, frontier);

                    if (rules.PersonalLoot)
                    {
                        // each member in sight of the mob rolls for an item of their own class (owner 2026-11-05)
                        var inSight = bg.Members.Keys.OfType<GamePlayer>().Where(member => member.GetDistance(mob) <= WorldMgr.VISIBILITY_DISTANCE);
                        RollPersonal(inSight, chance, maxDropCap, 0, mob, killedcon, loot, rules);
                        return loot;
                    }

                    int numDrops = 0;
                    foreach (GamePlayer bgMember in bg.Members.Keys)
                    {
                        if(bgMember.GetDistance(owner) > WorldMgr.VISIBILITY_DISTANCE)
                            continue;
                        
                        if (RollDropChance(chance) && numDrops < maxDropCap)
                        {
                            classForLoot = GetRandomClassFromBattlegroup(bg);
                            var item = GenerateItemTemplate(owner, classForLoot, (byte)(mob.Level + 1), killedcon);
                            loot.AddFixed(item, 1);
                            numDrops++;
                        }
                    }
                }
                //players below level 50 will always get loot for their class, 
                //or a valid class for one of their groupmates
                else if (owner.Group != null)
                {
                    var MaxDropCap = Math.Round((decimal) (owner.Group.MemberCount)/rules.GroupMembersPerDrop);
                    if (MaxDropCap < 1) MaxDropCap = 1;
                    if (MaxDropCap > rules.GroupMaxDrops) MaxDropCap = rules.GroupMaxDrops;
                    if (mob.Level > rules.GroupExtraDropAboveLevel) MaxDropCap++; //increase drop cap beyond lvl 60
                    int guaranteedDrop = mob.Level > rules.GroupGuaranteedDropAboveLevel ? 1 : 0; //guarantee a drop for very high level mobs

                    chance = rules.Scale(chance - rules.GroupLevelPenalty(mob.Level), named, frontier);

                    if (rules.PersonalLoot)
                    {
                        // each member in sight of the mob rolls for an item of their own class (owner 2026-11-05)
                        RollPersonal(context.Participants, chance, (int)MaxDropCap, guaranteedDrop, mob, killedcon, loot, rules);
                        return loot;
                    }

                    int numDrops = 0;
                    //roll for an item for each player in the group
                    foreach (GameLiving groupPlayer in GetLootParticipants(owner.Group))
                    {
                        if(groupPlayer.GetDistance(owner) > WorldMgr.VISIBILITY_DISTANCE)
                            continue;
                        
                        if (RollDropChance(chance) && numDrops < MaxDropCap)
                        {
                            classForLoot = GetRandomClassFromGroup(owner.Group);
                            var item = GenerateItemTemplate(owner, classForLoot, (byte)(mob.Level + 1), killedcon);
                            loot.AddFixed(item, 1);
                            numDrops++;
                        }
                    }

                    //if we're under the cap, add in the guaranteed drop
                    if (numDrops < MaxDropCap && guaranteedDrop > 0)
                    {
                        classForLoot = GetRandomClassFromGroup(owner.Group);
                        var item = GenerateItemTemplate(owner, classForLoot, (byte)(mob.Level + 1), killedcon);
                        loot.AddFixed(item, 1);
                    }

                    //classForLoot = GetRandomClassFromGroup(player.Group);
                    //chance += 4 * player.Group.GetPlayersInTheGroup().Count; //4% extra drop chance per group member
                }
                else
                {
                    int tmpChance = owner is GamePlayer player ? Math.Max(0, player.OutOfClassROGPercent) : 0;
                    if (owner.Level == 50 && Util.Chance(tmpChance))
                    {
                        classForLoot = GetRandomClassFromRealm(owner.Realm);
                    }

                    chance = rules.Scale(chance + rules.SoloBonus, named, frontier); //solo drop bonus

                    if (RollWithBadLuck(owner, chance, rules))
                    {
                        DbItemTemplate item = GenerateItemTemplate(owner, classForLoot, (byte)(mob.Level + 1), killedcon, false);
                        loot.AddFixed(item, 1, owner);
                    }
                    //tmp.GenerateItemQuality(killedcon);
                    //tmp.CapUtility(mob.Level + 1);
                    
                    /*
                    if (mob.Level < 5)
                    {
                        chance += 50;
                        loot.AddRandom(chance, item, 1);
                    }
                    else if (mob.Level < 10)
                        loot.AddRandom(chance + (100 - mob.Level * 10), item, 1);
                    //25% bonus drop rate at lvl 5, down to normal chance at level 10
                    else
                        loot.AddRandom(chance, item, 1);
                        */
                }

                //chance = 100;

               

            }
            catch (Exception exception)
            {
                // Do not hide a generator failure as an unlucky roll forever.
                // One global error per minute, no per-kill disk/database traffic.
                long now = Environment.TickCount64;
                long next = Interlocked.Read(ref _nextErrorLogTick);
                if (Log.IsErrorEnabled && now >= next && Interlocked.CompareExchange(ref _nextErrorLogTick, now + 60_000, next) == next)
                    Log.Error($"ROG_LOOT_FAILED mob=\"{mob?.Name}\" killer=\"{killer?.Name}\" killerType={killer?.GetType().Name}", exception);
                return loot;
            }

            return loot;
        }


        /// <param name="chance">Percent, 0 to 100 (rules may make it fractional).</param>
        protected virtual bool RollDropChance(double chance)
        {
            return Util.Chance(chance / 100.0); // Util.Chance(double) takes a fraction
        }

        /// <summary>Percent added to this participant's next roll for the rolls they missed (phase 5 bad-luck protection).</summary>
        public const string BadLuckProperty = "LOOT_RANDOM_ITEM_BAD_LUCK";

        public static double BadLuck(GameLiving participant) => participant?.TempProperties.GetProperty<double>(BadLuckProperty) ?? 0;

        /// <summary>One roll with the participant's bad-luck bonus: a hit clears it, a miss adds BadLuckStep (up to BadLuckMax).</summary>
        protected bool RollWithBadLuck(GameLiving participant, double chance, RandomItemLootRules rules)
        {
            double bonus = rules.BadLuckStep > 0 ? BadLuck(participant) : 0;
            bool hit = RollDropChance(Math.Min(100, chance + bonus));
            if (rules.BadLuckStep > 0 && participant != null)
            {
                participant.TempProperties.SetProperty(BadLuckProperty,
                    hit ? 0.0 : Math.Min(rules.BadLuckMax, bonus + rules.BadLuckStep));
            }
            return hit;
        }

        /// <summary>
        /// Personal loot: members roll in a random order so the drop cap favours nobody. A member who rolls an item gets one
        /// made for their own class and realm, meant for them; a hit past the cap gives nothing and leaves their bad luck as
        /// it was. A guaranteed drop goes to a random member who got nothing.
        /// </summary>
        private void RollPersonal(IEnumerable<GameLiving> members, double chance, int cap, int guaranteed, GameNPC mob,
            int killedcon, LootList loot, RandomItemLootRules rules)
        {
            List<GameLiving> order = members.Where(IsLootParticipant).Distinct().OrderBy(_ => Util.Random(int.MaxValue - 1)).ToList();
            var empty = new List<GameLiving>();
            int drops = 0;
            byte lootLevel = (byte)(mob.Level + 1);

            foreach (GameLiving member in order)
            {
                if (drops >= cap)
                {
                    // past the cap a roll still happens (it always did), but can neither drop nor count as bad luck
                    RollDropChance(chance);
                    empty.Add(member);
                    continue;
                }

                if (RollWithBadLuck(member, chance, rules))
                {
                    loot.AddFixed(GenerateItemTemplate(member, GetLootClass(member), lootLevel, killedcon), 1, member);
                    drops++;
                }
                else
                    empty.Add(member);
            }

            if (drops < cap && guaranteed > 0 && empty.Count > 0)
            {
                GameLiving lucky = empty[Util.Random(empty.Count - 1)];
                if (rules.BadLuckStep > 0)
                    lucky.TempProperties.SetProperty(BadLuckProperty, 0.0);
                loot.AddFixed(GenerateItemTemplate(lucky, GetLootClass(lucky), lootLevel, killedcon), 1, lucky);
            }
        }

        public static GameLiving ResolveEquipmentLootOwner(GameObject killer) => LootContext.ResolveOwner(killer);

        private static bool IsLootParticipant(GameLiving living) => LootContext.IsLootParticipant(living);

        private static IEnumerable<GameLiving> GetLootParticipants(Group group) => LootContext.GroupLootParticipants(group);

        private static eCharacterClass GetLootClass(GameLiving participant)
        {
            // IGamePlayer is the bot adapter, not an interface on GamePlayer.
            return (eCharacterClass)(participant is GamePlayer player
                ? player.CharacterClass.ID : ((GameBot)participant).CharacterClass.ID);
        }

        protected virtual DbItemTemplate GenerateItemTemplate(GameLiving owner, eCharacterClass classForLoot, byte lootLevel, int killedcon, bool adjustQuality = true)
        {
            DbItemTemplate item = null;
                
                
            GeneratedUniqueItem tmp = AtlasROGManager.GenerateMonsterLootROG(owner.Realm, classForLoot, lootLevel, owner.CurrentZone?.IsOF ?? false);
            if (adjustQuality)
                tmp.GenerateItemQuality(killedcon);
            //tmp.CapUtility(mob.Level + 1);
            item = tmp;
            item.MaxCount = 1;

            return item;
        }

        private eCharacterClass GetRandomClassFromGroup(Group group)
        {
            List<eCharacterClass> validClasses = new List<eCharacterClass>();

            foreach (GameLiving member in GetLootParticipants(group))
            {
                validClasses.Add(GetLootClass(member));
            }
            eCharacterClass ranClass = validClasses[Util.Random(validClasses.Count - 1)];

            return ranClass;
        }
        
        private eCharacterClass GetRandomClassFromBattlegroup(BattleGroup battlegroup)
        {
            List<eCharacterClass> validClasses = new List<eCharacterClass>();

            foreach (GamePlayer player in battlegroup.Members.Keys)
            {
                validClasses.Add((eCharacterClass)player.CharacterClass.ID);
            }
            eCharacterClass ranClass = validClasses[Util.Random(validClasses.Count - 1)];

            return ranClass;
        }

        private eCharacterClass GetRandomClassFromRealm(eRealm realm)
        {
            List<eCharacterClass> classesForRealm = RealmLootClasses(realm);
            return classesForRealm[Util.Random(classesForRealm.Count - 1)];
        }

        // Expansion, live and royal classes join the out-of-class pool while their server switch is on
        // (the same switches as character creation and bots). The Death Knight rolls Necromancer items.
        private static readonly Dictionary<eRealm, eCharacterClass[]> ModernLootClasses = new()
        {
            [eRealm.Albion] = [eCharacterClass.Heretic, eCharacterClass.MaulerAlb, eCharacterClass.DeathKnight, eCharacterClass.PendragonSovereign],
            [eRealm.Midgard] = [eCharacterClass.Valkyrie, eCharacterClass.Warlock, eCharacterClass.MaulerMid, eCharacterClass.Stormforged],
            [eRealm.Hibernia] = [eCharacterClass.Bainshee, eCharacterClass.Vampiir, eCharacterClass.MaulerHib, eCharacterClass.TuathaSovereign],
        };

        /// <summary>The classes a level 50 solo player's out-of-class ROG drop can be rolled for.</summary>
        public static List<eCharacterClass> RealmLootClasses(eRealm realm)
        {
            List<eCharacterClass> classesForRealm = new List<eCharacterClass>();
            switch (realm)
            {
                case eRealm.Albion:
                    classesForRealm.Add(eCharacterClass.Armsman);
                    classesForRealm.Add(eCharacterClass.Cabalist);
                    classesForRealm.Add(eCharacterClass.Cleric);
                    classesForRealm.Add(eCharacterClass.Friar);
                    classesForRealm.Add(eCharacterClass.Infiltrator);
                    classesForRealm.Add(eCharacterClass.Mercenary);
                    classesForRealm.Add(eCharacterClass.Necromancer);
                    classesForRealm.Add(eCharacterClass.Paladin);
                    classesForRealm.Add(eCharacterClass.Reaver);
                    classesForRealm.Add(eCharacterClass.Scout);
                    classesForRealm.Add(eCharacterClass.Sorcerer);
                    classesForRealm.Add(eCharacterClass.Theurgist);
                    classesForRealm.Add(eCharacterClass.Wizard);
                    break;
                case eRealm.Midgard:
                    classesForRealm.Add(eCharacterClass.Berserker);
                    classesForRealm.Add(eCharacterClass.Bonedancer);
                    classesForRealm.Add(eCharacterClass.Healer);
                    classesForRealm.Add(eCharacterClass.Hunter);
                    classesForRealm.Add(eCharacterClass.Runemaster);
                    classesForRealm.Add(eCharacterClass.Savage);
                    classesForRealm.Add(eCharacterClass.Shadowblade);
                    classesForRealm.Add(eCharacterClass.Skald);
                    classesForRealm.Add(eCharacterClass.Spiritmaster);
                    classesForRealm.Add(eCharacterClass.Thane);
                    classesForRealm.Add(eCharacterClass.Warrior);
                    break;
                case eRealm.Hibernia:
                    classesForRealm.Add(eCharacterClass.Animist);
                    classesForRealm.Add(eCharacterClass.Bard);
                    classesForRealm.Add(eCharacterClass.Blademaster);
                    classesForRealm.Add(eCharacterClass.Champion);
                    classesForRealm.Add(eCharacterClass.Druid);
                    classesForRealm.Add(eCharacterClass.Eldritch);
                    classesForRealm.Add(eCharacterClass.Enchanter);
                    classesForRealm.Add(eCharacterClass.Hero);
                    classesForRealm.Add(eCharacterClass.Mentalist);
                    classesForRealm.Add(eCharacterClass.Nightshade);
                    classesForRealm.Add(eCharacterClass.Ranger);
                    classesForRealm.Add(eCharacterClass.Valewalker);
                    classesForRealm.Add(eCharacterClass.Warden);
                    break;
            }

            if (ModernLootClasses.TryGetValue(realm, out eCharacterClass[] modern))
                classesForRealm.AddRange(modern.Where(AutonomousBotIdentityGenerator.IsClassAvailable));

            return classesForRealm;
        }
    }
}
