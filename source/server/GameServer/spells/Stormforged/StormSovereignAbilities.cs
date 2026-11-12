using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    // Keep class 64 and Stormforged.* database keys for save/client compatibility.
    public static class StormSovereignAbilities
    {
        public const int FirstId = 62400;
        public const int RR5SpellId = 62960;
        public static readonly int[] Levels = { 1, 5, 10, 15, 20, 30, 40, 50 };
        public static bool Owns(int id)
        {
            int normalized = PendragonRules.NormalizeSpell(id);
            return normalized >= FirstId && normalized < 62656 || normalized >= 62700 && normalized < 62735 ||
                normalized == RR5SpellId || id is >= 64335 and <= 64341 or >= 73335 and <= 73341 ||
                StormRemakeStyles.OwnsEffect(id) || StormRemakeSpells.Owns(id) ||
                TuathaRemakeStyles.OwnsEffect(id) || TuathaRemakeSpells.Owns(id) || TaraRegaliaSpells.Owns(id) ||
                PendragonRemakeStyles.OwnsEffect(id) || PendragonRemakeSpells.Owns(id) || PendragonRemakeSpells.IsValorIcon(id) ||
                CamelotRegaliaSpells.Owns(id);
        }
        public static int Key(int id) => (PendragonRules.NormalizeSpell(id) - FirstId) % 32;
        /// <summary>One number per ability across all its ranks: the royal key, or 100 + key for the Storm remake spells.</summary>
        public static int AbilityKey(int id) => StormRemakeSpells.Owns(id) ? 100 + StormRemakeSpells.Key(id) :
            TuathaRemakeSpells.Owns(id) ? 200 + TuathaRemakeSpells.Key(id) :
            PendragonRemakeSpells.Owns(id) ? 300 + PendragonRemakeSpells.Key(id) : Key(id);
        public static bool IsRankedAbility(int id) => PendragonRules.IsRoyalRank(id) || StormRemakeSpells.Owns(id) || TuathaRemakeSpells.Owns(id) ||
            PendragonRemakeSpells.Owns(id);
        public static bool Toggle(int id) => PendragonRules.IsRoyalRank(id) && Key(id) is 6 or 13 or 14 or 15 or 22 || StormRemakeSpells.Toggle(id) || TuathaRemakeSpells.Toggle(id) ||
            PendragonRemakeSpells.Toggle(id);
        public static bool IsHammerfall(int id) => PendragonRules.IsRoyalRank(id) && Key(id) == 21 || StormRemakeSpells.Key(id) == StormRemakeSpells.Hammerfall;
        public static bool HasVeil(GameLiving living) => living.effectListComponent.GetSpellEffects(eEffect.MasterLevel)
            .Any(effect => !effect.IsEnding && !effect.IsDisabled && PendragonRules.IsRoyalRank(effect.SpellHandler.Spell.ID) && Key(effect.SpellHandler.Spell.ID)==22);
        public static double Scale(int level) => Math.Clamp(level, 1, 50) / 50.0;
        // Style procs never scale below level 10 strength: 40 x 1/50 rounded to 0 damage through level 5 (2026-11-03 report).
        public const int StyleFloorLevel = 10;
        public static double StyleDamage(int level, bool finisher) => (finisher ? 120 : 40) * Scale(Math.Max(level, StyleFloorLevel));

        // Percent of damage dealt returned as health, power and endurance; Storm Crown multiplies it.
        public const int MeleeHealthSteal = 30, MeleeResourceSteal = 10;
        public const int SpellHealthSteal = 50, SpellResourceSteal = 20;
        public const double CrownStealMultiplier = 1.5;
        public const double CrownSpellDamage = 2.0;

        /// <summary>Returns part of every damaging hit (melee, spell, proc, DoT) to the sovereign.</summary>
        public static void Siphon(GameLiving owner, AttackData ad, int healthBefore)
        {
            if (ad?.Target == null || !owner.IsAlive || ad.Target == owner || ad.SpellHandler is StormforgedStrike)
                return;
            // Never restore on a resist, immunity, absorbed hit or overkill damage.
            int dealt = Math.Min(healthBefore, Math.Max(0, ad.Damage + ad.CriticalDamage));
            if (dealt <= 0)
                return;
            bool melee = ad.IsMeleeAttack;
            double crown = StormforgedHeroicRules.HasAvatar(owner) ? CrownStealMultiplier : 1.0;
            int health = (int)((melee ? MeleeHealthSteal : SpellHealthSteal) * crown);
            int resource = (int)((melee ? MeleeResourceSteal : SpellResourceSteal) * crown);
            int before = owner.Health;
            owner.ChangeHealth(owner, eHealthChangeType.Spell, StormforgedRules.RestorePerHit(dealt, health));
            owner.ChangeMana(owner, eManaChangeType.Spell, StormforgedRules.RestorePerHit(dealt, resource));
            owner.ChangeEndurance(owner, eEnduranceChangeType.Spell, StormforgedRules.RestorePerHit(dealt, resource));
            if (owner is GamePlayer player)
                NoteSiphon(player, owner.Health - before, GameLoop.GameLoopTime);
        }

        // The siphon was silent, so a Pendragon saw ~half of a Judgment's damage come back with no line (playtest
        // 2026-11-07). One line per second, the health of every hit in it summed.
        private sealed class SiphonNote { public long LastMessage = long.MinValue / 2; public int Pending; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GamePlayer, SiphonNote> _siphonNotes = new();
        public const int SiphonMessageIntervalMs = 1000;

        /// <summary>Adds restored health to the player's siphon line; returns the text sent now, or null while it waits.</summary>
        public static string NoteSiphon(GamePlayer player, int restored, long now)
        {
            if (restored <= 0)
                return null;
            var note = _siphonNotes.GetOrCreateValue(player);
            lock (note)
            {
                note.Pending += restored;
                if (now - note.LastMessage < SiphonMessageIntervalMs)
                    return null;
                string text = SiphonText(player, note.Pending);
                note.Pending = 0;
                note.LastMessage = now;
                player.Out?.SendMessage(text, eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
                return text;
            }
        }

        public static string SiphonText(GameLiving owner, int amount) =>
            $"Your {(PendragonRules.IsPendragon(owner) ? "holy fire" : PendragonRules.IsTuatha(owner) ? "spirit" : "lightning")} restores {amount} health.";

        public static IEnumerable<(int Id, MLAbility Ability)> Build()
        {
            foreach (var entry in BuildMidgard())
            {
                yield return entry;
                yield return (entry.Id + PendragonRules.SpellOffset, entry.Ability);
                yield return (entry.Id + PendragonRules.TuathaOffset, entry.Ability);
            }
            for (int index = 35; index < 42; index++)
            {
                // The extra weapon line: Albion Two Handed and Hibernian Large Weapons.
                yield return (64300 + index, Style(index - 35));
                yield return (73300 + index, Style(index - 35));
            }
        }

        private static IEnumerable<(int Id, MLAbility Ability)> BuildMidgard()
        {
            for (int tier = 0; tier < Levels.Length; tier++)
                for (int key = 0; key < 25; key++)
                    yield return (FirstId + tier * 32 + key, Make(key, Levels[tier]));
            for (int index = 0; index < 35; index++)
                yield return (62700 + index, Style(index));
            yield return (RR5SpellId, new MLAbility
            {
                Path = "StormSovereign", Level = 50, Positive = true, Describe = spell => spell.Description,
                Mods = new[] { MLMod.Buff(eProperty.MeleeDamage,20), MLMod.Buff(eProperty.SpellDamage,20),
                    new MLMod(eProperty.CriticalMeleeHitChance,MLCategory.Other,10), new MLMod(eProperty.CriticalSpellHitChance,MLCategory.Ability,10) },
                Run = (handler, _) =>
                {
                    handler.Hold(handler.Caster);
                    if (handler.Caster.ControlledBrain?.Body is StormWolf wolf && wolf.IsAlive &&
                        wolf.CurrentRegion == handler.Caster.CurrentRegion && handler.Caster.IsWithinRadius(wolf,2000))
                        handler.Hold(wolf);
                    foreach (var enemy in handler.Enemies(handler.Caster,handler.Spell.Radius))
                        Strike(handler,enemy,handler.Spell.Damage);
                    handler.Animate(handler.Caster);
                }
            });
        }

        private static MLMod Buff(eProperty p, int n) => MLMod.Buff(p, n);
        private static MLAbility Make(int key, int level)
        {
            int Value(int n) => Math.Max(1, (int)(n * Scale(level)));
            MLAbility Ability(bool positive, Action<MasterLevelAbilityHandler, GameLiving> run, params MLMod[] mods) =>
                new() { Path = "StormSovereign", Level = level, Positive = positive, KeepsStealth = positive, Run = run,
                    Mods = mods, Describe = s => s.Description };
            switch (key)
            {
                case 0: case 2:
                    return Ability(false, (h,t) => Strike(h, t, h.Spell.Damage));
                case 1:
                    return Ability(false, (h,t) => { Strike(h,t,h.Spell.Damage,eDamageType.Cold); h.Cast(t,1,eSpellType.SpeedDecrease, db => { db.Duration=8; db.Value=30; }); });
                case 3: case 4: case 24:
                    return Ability(false, (h,_) => { foreach (var enemy in h.Enemies(h.Caster,h.Spell.Radius)) Strike(h,enemy,h.Spell.Damage); });
                case 5:
                    return Ability(false, (h,_) => h.Field(new Point3D(h.Caster.X,h.Caster.Y,h.Caster.Z),new MasterLevelFieldOptions {
                        Name=h.Spell.Name, Model=3457, Interval=3000, SuppressEffect=true, PulseEffect=h.Spell.ClientEffect,
                        OnPulse=(f,t)=>Strike(f.Ability,t,f.Ability.Spell.Damage) }));
                case 7:
                    return Ability(true, GroupBuff, MLMod.Speed(1.15));
                case 8:
                    return Ability(true, GroupBuff, Buff(eProperty.ArmorAbsorption,10));
                case 9:
                    return Ability(true, GroupBuff, Buff(eProperty.ToHitBonus,10),Buff(eProperty.MeleeDamage,10));
                case 10:
                    return Ability(true,(h,_)=>h.Heal(h.Caster,h.Caster.MaxHealth*25/100));
                case 11:
                    return Ability(true,(h,_)=> { foreach(var ally in h.Group(h.Spell.Radius)) { h.Heal(ally,ally.MaxHealth*15/100); MasterLevelAbilityHandler.Restore(h.Caster,ally,0,20,h.Spell.Name); } });
                case 12:
                    return new MLAbility { Path="StormSovereign",Level=level,Positive=true,KeepsStealth=true,BreaksControl=true,
                        Describe=s=>s.Description,Run=(h,_)=> { MasterLevelAbilityHandler.Cure(h.Caster,eEffect.Stun,eEffect.Mez,eEffect.Snare,eEffect.MovementSpeedDebuff); h.Hold(h.Caster); } };
                case 14:
                    return Ability(true,(h,_)=>h.Hold(h.Caster),MLMod.Resists(MLCategory.Ability,Value(10)));
                case 15: case 18:
                    return Ability(true,(h,_)=>h.Hold(h.Caster));
                case 16:
                    return Ability(false,(h,t)=>h.Cast(t,500+h.Caster.Level,eSpellType.Taunt,db=>db.Value=400+h.Caster.Level*10));
                case 17:
                    return new MLAbility { Path="StormSovereign",Level=level,KeepsStealth=true,Describe=s=>s.Description,
                        Run=(h,t)=> { if(h.Caster.IsStealthed) h.Hold(h.Caster); } };
                case 21:
                    return Ability(false,(h,t)=>Hammerfall((StormSovereignHandler)h,t));
                case 22:
                    return Ability(true,(h,_)=>h.Hold(h.Caster));
                default:
                    return Ability(true,(h,_)=>h.Hold(h.Caster));
            }
        }

        private static void GroupBuff(MasterLevelAbilityHandler h, GameLiving ignored)
        {
            foreach(var member in h.Group(h.Spell.Radius)) h.Hold(member);
        }

        public static void Strike(MasterLevelAbilityHandler h, GameLiving target, double damage, eDamageType type=eDamageType.Energy, int critBonus=0)
        {
            if(target==null || !target.IsAlive) return;
            var spell = new Spell(new DbSpell { SpellID=h.Spell.ID*1000+990,Name=h.Spell.Name,Type="DirectDamage",Target="Enemy",
                Damage=damage,DamageType=(int)PendragonRules.RoyalDamage(h.Caster, type),ClientEffect=h.Spell.ClientEffect,Icon=h.Spell.Icon },50) { InternalID=h.Spell.InternalID };
            h.Caster.Stealth(false);
            new SovereignDamage(h.Caster,spell,h.SpellLine,critBonus).StartSpell(target);
        }

        internal static void Hammerfall(StormSovereignHandler h, GameLiving target)
        {
            var owner=h.Caster;
            if(target==null || !target.IsAlive || target.CurrentRegion!=owner.CurrentRegion ||
                !owner.IsWithinRadius(target,h.Spell.Range) || !h.IsEnemy(target)) return;
            var landing=StormforgedTravel.ResolveBeside(owner,target);
            if(!StormforgedTravel.TryResolvePoint(owner,landing,h.Spell.Range,out var safe)) return;
            h.Animate(target,h.Spell.ClientEffect); // Stock Thane Stormhammer target visual.
            owner.Stealth(false);
            if(!owner.MoveTo(owner.CurrentRegionID,safe.X,safe.Y,safe.Z,owner.GetHeading(target))) return;
            if(owner.ControlledBrain is DOL.AI.Brain.StormWolfBrain wolf && wolf.WalkState==DOL.AI.Brain.eWalkState.Follow)
            {
                wolf.Body.MoveInRegion(owner.CurrentRegionID,safe.X+30,safe.Y,safe.Z,owner.Heading,true);
                wolf.Body.Stealth(false);
                if(wolf.AggressionState!=DOL.AI.Brain.eAggressionState.Passive) wolf.Attack(target);
            }
            // Pendragon King's Advance: +10% damage per Valor held, and the charge itself earns 1 Valor.
            double valor=PendragonRules.IsPendragon(owner)?PendragonValor.Held(owner):1;
            Strike(h,target,h.Spell.Damage*valor,eDamageType.Energy,h.FromStealth?10:0);
            foreach(var enemy in h.Enemies(owner,h.Spell.Radius).Where(t=>t!=target))
                Strike(h,enemy,h.Spell.Damage*0.5*valor);
            if(PendragonRules.IsPendragon(owner)) PendragonValor.Add(owner,1);
        }

        private static MLAbility Style(int index)
        {
            int weapon=index/7, action=index%7;
            var mods=action==1 ? new[]{ MLMod.Debuff(eProperty.ArmorAbsorption,10) } :
                action==2 || action==5 ? new[]{ Buff(weapon==4?eProperty.BlockChance:eProperty.ParryChance,5) } : Array.Empty<MLMod>();
            return new MLAbility { Path="StormSovereign",Level=1,Mods=mods,Describe=s=>s.Description,Run=(h,t)=> {
                double damage=StyleDamage(h.Caster.Level,action==5);
                if(action==4) { h.Cast(t,1,eSpellType.StyleStun,db=>{db.Duration=weapon==4?4:3;}); return; }
                Strike(h,t,damage,weapon==2?eDamageType.Cold:eDamageType.Energy);
                if(action==1)
                {
                    if(weapon==2) h.Cast(t,500+h.Caster.Level,eSpellType.DamageOverTime,db=>{db.Duration=12;db.Frequency=30;db.Damage=10+h.Caster.Level/2;db.DamageType=(int)PendragonRules.RoyalDamage(h.Caster,eDamageType.Body);});
                    else if(weapon==1) h.Heal(h.Caster,Math.Max(1,h.Caster.MaxHealth*2/100));
                    else h.Hold(t,10000);
                }
                if(action==2 || action==5) h.Hold(h.Caster,10000);
                if(action==3) {
                    if(weapon==2) h.Cast(t,500+h.Caster.Level,eSpellType.DamageOverTime,db=>{db.Duration=12;db.Frequency=30;db.Damage=10+h.Caster.Level/2;db.DamageType=(int)PendragonRules.RoyalDamage(h.Caster,eDamageType.Body);});
                    else h.Cast(t,1,eSpellType.SpeedDecrease,db=>{db.Duration=6;db.Value=30;});
                }
                if(action==5 && (weapon==0 || weapon==3))
                    foreach(var enemy in h.Enemies(t,250).Where(enemy=>enemy!=t && enemy.InCombat)) Strike(h,enemy,damage*0.5);
                if(weapon==2 && action==5 && t.effectListComponent.ContainsEffectForEffectType(eEffect.DamageOverTime)) Strike(h,t,damage*0.5,eDamageType.Cold);
                if(weapon==1 && action==0) h.Heal(h.Caster,Math.Max(1,h.Caster.MaxHealth/100));
            }};
        }
    }

    public sealed class SovereignDamage : DirectDamageSpellHandler
    {
        private readonly int _critBonus;
        public SovereignDamage(GameLiving caster,Spell spell,SpellLine line,int critBonus=0):base(caster,spell,line) { _critBonus=critBonus; }
        public override AttackData CalculateDamageToTarget(GameLiving target)
        {
            var attack=base.CalculateDamageToTarget(target);
            // Roll only the added probability after the ordinary roll failed: +10 points, not +10% multiplicative.
            int baseline=Math.Clamp(attack.CriticalChance,0,50);
            if(attack.CriticalDamage==0 && _critBonus>0 && Caster.Chance(RandomDeckEvent.CriticalChance,100.0*_critBonus/(100-baseline)))
                attack.CriticalDamage=(int)(attack.Damage*(0.1+Caster.GetPseudoDoubleIncl(RandomDeckEvent.CriticalVariance)*(target is GamePlayer?0.4:0.9)));
            if(StormforgedHeroicRules.HasAvatar(Caster)) {
                attack.Damage=(int)(attack.Damage*StormSovereignAbilities.CrownSpellDamage);
                attack.CriticalDamage=(int)(attack.CriticalDamage*StormSovereignAbilities.CrownSpellDamage);
            }
            return attack;
        }
    }

    [SpellHandler(eSpellType.StormSovereignAbility)]
    public class StormSovereignHandler : MasterLevelAbilityHandler
    {
        public bool FromStealth { get; private set; }
        public StormSovereignHandler(GameLiving caster,Spell spell,SpellLine line):base(caster,spell,line) { }
        public override bool CheckBeginCast(GameLiving target)
        {
            FromStealth=Caster.IsStealthed;
            if(StormSovereignAbilities.Toggle(Spell.ID))
                foreach(var effect in Caster.effectListComponent.GetSpellEffects(eEffect.MasterLevel))
                    if(!effect.IsEnding && effect.SpellHandler.Spell.ID==Spell.ID) { effect.End(); return false; }
            if((PendragonRules.IsRoyalRank(Spell.ID) && StormSovereignAbilities.Key(Spell.ID)==17 || StormRemakeSpells.Key(Spell.ID)==StormRemakeSpells.HuntersMark) && !Caster.IsStealthed) {
                MessageToCaster(Spell.Name+" requires stealth.",eChatType.CT_SpellResisted); return false;
            }
            return base.CheckBeginCast(target);
        }
        public override void FinishSpellCast(GameLiving target)
        {
            Caster.Mana -= PowerCost(target);
            base.FinishSpellCast(target);
        }
        // Storm remake: with Tempest, Royal Thunder, Stormbreak, Winter's Grasp and Hammerfall are instant and cost no power.
        private bool TempestPays => StormRemakeSpells.PaysWithTempest(Spell.ID) && StormTempest.Charges(Caster) > 0;
        // Tuatha: Spirit Lance is instant on a target with 3 or more of your marks.
        private bool MarkedLance => TuathaRemakeSpells.Key(Spell.ID) == TuathaRemakeSpells.SpiritLance &&
            Caster.TargetObject is GameLiving target && TuathaMarks.Count(Caster, target) >= 3;
        // Pendragon: Crownfire Lance is instant with 3 or more Valor.
        private bool ValorLance => PendragonRemakeSpells.Key(Spell.ID) == PendragonRemakeSpells.CrownfireLance && PendragonValor.Count(Caster) >= 3;
        public override int CalculateCastingTime() => TempestPays || MarkedLance || ValorLance ? 0 : base.CalculateCastingTime();
        public override int PowerCost(GameLiving target) => TempestPays ? 0 : base.PowerCost(target);
        public override void ApplyEffectOnTarget(GameLiving target)
        {
            // A Sovereign style whose hit already killed the target still runs its whole effect: builds and spends,
            // splash onto the others, self heals and wards. Its helpers skip the corpse. (Playtest 2026-11-07: Thor's
            // Judgement did not arc or spend on a kill, a killing Pendragon Judgment did nothing.)
            if(target is { IsAlive: false } && IsSovereignStyleEffect(Spell.ID))
            {
                RunAbility(target);
                return;
            }
            base.ApplyEffectOnTarget(target);
        }
        public static bool IsSovereignStyleEffect(int spellId) =>
            StormRemakeStyles.OwnsEffect(spellId) || PendragonRemakeStyles.OwnsEffect(spellId) || TuathaRemakeStyles.OwnsEffect(spellId);
        /// <summary>
        /// An internal spell a Sovereign style effect casts: its strikes (Strike, id x 1000 + 990) and its stock sub-spells
        /// (Cast, id x 1000 + index). These are unresistable, like the style itself.
        /// </summary>
        public static bool IsSovereignStyleSubSpell(int spellId) => spellId >= 1000 && IsSovereignStyleEffect(spellId / 1000);
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams init) =>
            ECSGameEffectFactory.Create(init,static(in i)=>new StormSovereignEffect(i));
    }
}
