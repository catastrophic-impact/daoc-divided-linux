using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    // Offline Master Levels for Banelord, Battlemaster, Convoker, Perfecter, Sojourner, Spymaster and
    // Warlord, rebuilt on the ECS effect system. One spell type, MasterLevelAbility, dispatches on the
    // spell ID (63000 + path * 10 + ML - 1) to the table below. The database holds ranges, radii,
    // durations, damage and timers; the bonus sizes live here. Every ability works on monsters and on
    // enemy players. The old legacy handlers in this folder are left in place for other content that
    // may share their spell types; they are no longer used by Master Levels. Stormlord is in Stormlord.cs.

    public enum MLCategory { Buff, Debuff, Other, Ability, SpeedMultiplier }

    /// <summary>One property change held for the length of an effect, aura or field.</summary>
    public readonly record struct MLMod(eProperty Property, MLCategory Category, double Value)
    {
        public static MLMod Buff(eProperty p, int v) => new(p, MLCategory.Buff, v);
        public static MLMod Debuff(eProperty p, int v) => new(p, MLCategory.Debuff, v);
        public static MLMod Speed(double multiplier) => new(eProperty.MaxSpeed, MLCategory.SpeedMultiplier, multiplier);

        public static readonly eProperty[] MagicResists =
            { eProperty.Resist_Body, eProperty.Resist_Cold, eProperty.Resist_Energy, eProperty.Resist_Heat, eProperty.Resist_Matter, eProperty.Resist_Spirit };
        public static readonly eProperty[] AllResists = MagicResists.Concat(new[] { eProperty.Resist_Crush, eProperty.Resist_Slash, eProperty.Resist_Thrust }).ToArray();
        public static MLMod[] Resists(MLCategory category, int value, bool physical = false) =>
            (physical ? AllResists : MagicResists).Select(p => new MLMod(p, category, value)).ToArray();

        /// <summary>Speed multipliers need an effect key; ground fields snare through a spell instead.</summary>
        public void Apply(GameLiving living, ECSGameEffect key, bool on)
        {
            int sign = on ? 1 : -1;
            switch (Category)
            {
                case MLCategory.Buff: living.BaseBuffBonusCategory[Property] += sign * (int)Value; break;
                case MLCategory.Debuff: living.DebuffCategory[Property] += sign * (int)Value; break;
                case MLCategory.Other: living.OtherBonus[Property] += sign * (int)Value; break;
                case MLCategory.Ability: living.AbilityBonus[Property] += sign * (int)Value; break;
                case MLCategory.SpeedMultiplier when key != null:
                    if (on)
                        living.BuffBonusMultCategory1.Set((int)Property, key, Value);
                    else
                        living.BuffBonusMultCategory1.Remove((int)Property, key);
                    living.OnMaxSpeedChange();
                    break;
            }
        }

        public static void Refresh(GameLiving living)
        {
            if (living is not GamePlayer player)
                return;
            player.Out.SendCharStatsUpdate();
            player.Out.SendCharResistsUpdate();
            player.Out.SendUpdateWeaponAndArmorStats();
            player.UpdateEncumbrance();
            player.Out.SendStatusUpdate();
        }
    }

    /// <summary>A spell handler whose <see cref="MasterLevelEffect"/> holds these bonuses.</summary>
    public interface IMasterLevelMods
    {
        IReadOnlyList<MLMod> Mods { get; }
    }

    public static class MasterLevelHold
    {
        /// <summary>
        /// Starts the handler's effect on the target, or extends the one it already has, so a font, ward or
        /// storm keeps one steady icon on everyone inside instead of restarting it every pulse.
        /// </summary>
        public static void Refresh(ISpellHandler handler, GameLiving target, int duration, bool trigger = false)
        {
            foreach (ECSGameSpellEffect existing in target.effectListComponent.GetSpellEffects(eEffect.MasterLevel))
            {
                if (!existing.IsEnding && existing.SpellHandler.Spell.ID == handler.Spell.ID)
                {
                    existing.ExpireTick = Math.Max(existing.ExpireTick, GameLoop.GameLoopTime + duration);
                    return;
                }
            }
            if (trigger)
                ECSGameEffectFactory.Create(new(target, duration, 1, handler), static (in i) => new MasterLevelTriggerEffect(i));
            else if (handler is StormSovereignHandler)
                ECSGameEffectFactory.Create(new(target, duration, 1, handler), static (in i) => new StormSovereignEffect(i));
            else
                ECSGameEffectFactory.Create(new(target, duration, 1, handler), static (in i) => new MasterLevelEffect(i));
        }
    }

    public sealed class MLAbility
    {
        public string Path { get; init; }
        public int Level { get; init; }
        public Func<Spell, string> Describe { get; init; }
        public Action<MasterLevelAbilityHandler, GameLiving> Run { get; init; }
        /// <summary>Held by <see cref="MasterLevelEffect"/> when the ability applies a timed effect.</summary>
        public MLMod[] Mods { get; init; } = Array.Empty<MLMod>();
        public bool Positive { get; init; }
        public bool Ground { get; init; }
        public bool Passive { get; init; }
        public bool KeepsStealth { get; init; }
        /// <summary>Usable while stunned or mesmerized (Champion break-free abilities).</summary>
        public bool BreaksControl { get; init; }
        /// <summary>Pet growth for Summoning Mastery, in percent.</summary>
        public int Grow { get; init; }
        /// <summary>For next-spell effects: return true when the trigger was used.</summary>
        public Func<MasterLevelTriggerEffect, HealSpellHandler, GameLiving, bool> OnCast { get; init; }
    }

    [SpellHandler(eSpellType.MasterLevelAbility)]
    public class MasterLevelAbilityHandler : SpellHandler, IMasterLevelMods
    {
        private static readonly ConcurrentDictionary<(int, int), Spell> _subSpells = new();
        public MLAbility Ability { get; }
        public IReadOnlyList<MLMod> Mods => Ability?.Mods ?? Array.Empty<MLMod>();

        public MasterLevelAbilityHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line)
        {
            Ability = MasterLevelAbilities.For(spell.ID);
        }

        public override bool HasPositiveEffect => Ability is { Positive: true } or { Passive: true };
        public override bool UnstealthCasterOnStart => Ability is { KeepsStealth: false, Passive: false };
        public override bool UnstealthCasterOnFinish => Ability is { KeepsStealth: false, Passive: false };
        public override void CastSubSpells(GameLiving target) { }
        public override bool HasConflictingEffectWith(ISpellHandler compare) => Spell.ID == compare.Spell.ID;
        // Shouts target the caster; the stock spells an ability applies roll their own resists.
        public override double CalculateSpellResistChance(GameLiving target) => 0;
        public override string ShortDescription => Ability?.Describe(Spell) ?? Spell.Name;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            Ability?.OnCast != null
                ? ECSGameEffectFactory.Create(initParams, static (in i) => new MasterLevelTriggerEffect(i))
                : ECSGameEffectFactory.Create(initParams, static (in i) => new MasterLevelEffect(i));

        public override List<GameLiving> SelectTargets(GameObject castTarget)
        {
            var list = GameLoop.GetListForTick<GameLiving>();
            list.Add(castTarget as GameLiving ?? Caster);
            return list;
        }

        public override bool CheckBeginCast(GameLiving selectedTarget)
        {
            if (Ability == null)
            {
                MessageToCaster("This Master Level ability is not available yet.", eChatType.CT_SpellResisted);
                return false;
            }
            if (Ability.Passive)
            {
                MessageToCaster($"{Spell.Name} is a passive ability and is always active.", eChatType.CT_SpellResisted);
                return false;
            }
            if (Ability.Ground && !GroundPoint(out _))
            {
                MessageToCaster("Choose a visible, reachable ground target within spell range.", eChatType.CT_SpellResisted);
                return false;
            }
            return base.CheckBeginCast(selectedTarget);
        }

        public override bool CheckEndCast(GameLiving target) => (!Ability.Ground || GroundPoint(out _)) && base.CheckEndCast(target);

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target == null || !target.IsAlive)
                return;
            RunAbility(target);
        }

        /// <summary>Runs the ability on a target, logging instead of throwing. Its helpers skip dead targets.</summary>
        protected void RunAbility(GameLiving target)
        {
            try
            {
                Ability?.Run?.Invoke(this, target);
            }
            catch (Exception e)
            {
                // An exception here reaches the casting service, which kicks a player to the character screen
                // (tester 2026-10-26: Tuatha "Step Between Worlds" logged the player off). Log it and keep them in game.
                if (_log.IsErrorEnabled)
                    _log.Error($"{Spell.Name} ({Spell.ID}) failed for {Caster} on {target}", e);
                MessageToCaster($"{Spell.Name} failed.", eChatType.CT_SpellResisted);
            }
        }

        private static readonly DOL.Logging.Logger _log = DOL.Logging.LoggerManager.Create(typeof(MasterLevelAbilityHandler));

        // ---- toolkit used by the ability table ----

        public bool GroundPoint(out Point3D point)
        {
            point = null;
            if (!Caster.GroundTarget.IsValid || !Caster.GroundTargetInView)
                return false;
            return StormforgedTravel.TryResolvePoint(Caster, new Point3D(Caster.GroundTarget.X, Caster.GroundTarget.Y, Caster.GroundTarget.Z), Spell.Range, out point);
        }

        public static IEnumerable<GameLiving> Around(GameObject center, int radius) =>
            center.GetPlayersInRadius((ushort)radius).Cast<GameLiving>().Concat(center.GetNPCsInRadius((ushort)radius))
                .Where(living => living is not GameStorm && living.IsAlive && living.ObjectState == GameObject.eObjectState.Active);

        public bool IsEnemy(GameLiving living) => living != Caster && GameServer.ServerRules.IsAllowedToAttack(Caster, living, true);
        public static bool IsAlly(GameLiving owner, GameLiving living) =>
            living.Realm == owner.Realm && (living == owner || living is GamePlayer || living is IGamePlayer || living is GameNPC { Brain: IControlledBrain }) &&
            !GameServer.ServerRules.IsAllowedToAttack(owner, living, true);

        public IEnumerable<GameLiving> Enemies(GameObject center, int radius) => Around(center, radius).Where(IsEnemy).ToList();
        public IEnumerable<GameLiving> Allies(GameObject center, int radius) => Around(center, radius).Where(l => IsAlly(Caster, l)).ToList();

        public IEnumerable<GameLiving> Group(int radius)
        {
            var members = new List<GameLiving>();
            IEnumerable<GameLiving> roster = Caster.Group?.GetMembersInTheGroup() ?? (IEnumerable<GameLiving>)new[] { Caster };
            foreach (GameLiving member in roster)
            {
                if (member.IsAlive && member.CurrentRegion == Caster.CurrentRegion && Caster.IsWithinRadius(member, radius))
                    members.Add(member);
                if (member.ControlledBrain?.Body is GameNPC pet && pet.IsAlive && Caster.IsWithinRadius(pet, radius))
                    members.Add(pet);
            }
            return members;
        }

        public IEnumerable<GameNPC> Pets(int radius)
        {
            var pets = new List<GameNPC>();
            if (Caster.ControlledBrain?.Body is GameNPC main && main.IsAlive)
            {
                pets.Add(main);
                if (main.ControlledNpcList != null)
                    pets.AddRange(main.ControlledNpcList.Select(brain => brain?.Body).OfType<GameNPC>().Where(p => p.IsAlive));
            }
            pets.AddRange(AutonomousPetSupport.OwnedFieldTurrets(Caster).Select(entry => entry.Turret).Where(t => t != null && t.IsAlive));
            return pets.Distinct().Where(p => Caster.IsWithinRadius(p, radius)).ToList();
        }

        /// <summary>Starts this ability's timed effect (its <see cref="MLAbility.Mods"/>) on the target.</summary>
        public void Hold(GameLiving target, int duration = 0)
        {
            if (target == null || !target.IsAlive)
                return;
            MasterLevelHold.Refresh(this, target, duration > 0 ? duration : Spell.Duration, Ability.OnCast != null);
        }

        public void Animate(GameLiving target, ushort effect = 0)
        {
            ushort id = effect != 0 ? effect : (ushort)Spell.ClientEffect;
            foreach (GamePlayer viewer in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                viewer.Out.SendSpellEffectAnimation(Caster, target, id, 0, false, 1);
        }

        /// <summary>Casts an internal spell of a stock type from the caster, with fixed variance.</summary>
        public void Cast(GameLiving target, int index, eSpellType type, Action<DbSpell> setup)
        {
            // A style finisher still runs when its hit killed the target: its splash and self effects go on, the corpse is skipped.
            if (target == null || !target.IsAlive)
                return;
            Spell spell = _subSpells.GetOrAdd((Spell.ID, index), _ =>
            {
                var db = new DbSpell
                {
                    SpellID = Spell.ID * 1000 + index, Name = Spell.Name, Type = type.ToString(), Target = "Enemy",
                    Icon = Spell.Icon, ClientEffect = Spell.ClientEffect, DamageType = (int)eDamageType.Energy
                };
                setup(db);
                StockMessages(db);
                // Show the ability's own tooltip on the effect, not the borrowed icon's stock spell.
                return new Spell(db, 50) { InternalID = Spell.InternalID };
            });
            ScriptMgr.CreateSpellHandler(Caster, spell, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells))?.StartSpell(target);
        }

        /// <summary>
        /// The stock lines a stun, snare, root, mez or disarm prints. Internal sub-spells had no Message1-4, so a Sovereign
        /// stun or snare landed silently (playtest 2026-11-07: Binding Radiance, Unhorse, Lady of the Lake).
        /// </summary>
        public static void StockMessages(DbSpell db)
        {
            if (!string.IsNullOrEmpty(db.Message1) || !string.IsNullOrEmpty(db.Message2))
                return;
            (string, string, string, string) lines = db.Type switch
            {
                nameof(eSpellType.StyleStun) or nameof(eSpellType.Stun) =>
                    ("You are stunned!", "{0} is stunned!", "You recover from the stun.", "{0} recovers from the stun."),
                nameof(eSpellType.SpeedDecrease) when db.Value >= 99 =>
                    ("Your feet are frozen to the ground!", "{0} is rooted in place!", "You can move again.", "{0} can move again."),
                nameof(eSpellType.SpeedDecrease) or nameof(eSpellType.DamageSpeedDecrease) =>
                    ("Your movements slow!", "{0} slows down!", "You speed up again.", "{0} speeds up again."),
                nameof(eSpellType.Mesmerize) =>
                    ("You are entranced!", "{0} is entranced!", "You are no longer entranced.", "{0} is no longer entranced."),
                nameof(eSpellType.Disarm) =>
                    ("You are disarmed!", "{0} is disarmed!", "You can use your weapon again.", "{0} can use their weapon again."),
                nameof(eSpellType.CombatSpeedDebuff) =>
                    ("Your attacks slow!", "{0}'s attacks slow!", "Your attacks speed up again.", "{0}'s attacks speed up again."),
                _ => (null, null, null, null)
            };
            // Other types keep their empty lines: null made SendMessages throw on StartsWith for the Pendragon
            // ward (BothAblativeArmor) and burn (DamageOverTime), 113 exceptions in a 10-minute soak.
            if (lines.Item1 == null)
                return;
            (db.Message1, db.Message2, db.Message3, db.Message4) = lines;
        }

        public void Damage(GameLiving target, double amount, eDamageType type = eDamageType.Energy, int index = 0) =>
            Cast(target, 100 + index, eSpellType.DirectDamage, db => { db.Damage = amount; db.DamageType = (int)type; db.ClientEffect = 0; });

        /// <summary>Damage not known when the table was written (percent of health); not cached.</summary>
        public void DamageNow(GameLiving target, int amount, eDamageType type)
        {
            if (amount <= 0 || target == null || !target.IsAlive)
                return;
            var spell = new Spell(new DbSpell { SpellID = Spell.ID * 1000 + 999, Name = Spell.Name, Type = "DirectDamage", Target = "Enemy", Damage = amount, DamageType = (int)type, Icon = Spell.Icon }, 50)
                { InternalID = Spell.InternalID };
            ScriptMgr.CreateSpellHandler(Caster, spell, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells))?.StartSpell(target);
        }

        public void Heal(GameLiving target, int amount)
        {
            if (amount <= 0 || target.Health >= target.MaxHealth)
                return;
            int healed = target.ChangeHealth(Caster, eHealthChangeType.Spell, amount);
            Animate(target);
            Tell(target, healed > 0 ? $"{Spell.Name} heals {(target == Caster ? "you" : target.GetName(0, false))} for {healed} hit points." : null,
                 healed > 0 ? $"{Caster.GetName(0, true)}'s {Spell.Name} heals you for {healed} hit points." : null);
        }

        public void Report(int count, string who)
        {
            MessageToCaster(count == 0 ? $"{Spell.Name} finds no {who} in range." : $"{Spell.Name} affects {count} {who}.", eChatType.CT_Spell);
        }

        public void MessageToCasterPublic(string message) => MessageToCaster(message, eChatType.CT_Spell);

        /// <summary>Tells the caster and (if different) the target what an instant effect did.</summary>
        public void Tell(GameLiving target, string toCaster, string toTarget)
        {
            if (toCaster != null)
                MessageToCaster(toCaster, eChatType.CT_Spell);
            if (toTarget != null && target != Caster && target is GamePlayer player)
                player.Out.SendMessage(toTarget, eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
        }

        public static void Restore(GameLiving owner, GameLiving target, int percentPower, int percentEndurance, string source = null)
        {
            int power = 0, endurance = 0;
            if (percentPower > 0 && target.MaxMana > 0 && target.Mana < target.MaxMana)
                power = target.ChangeMana(owner, eManaChangeType.Spell, Math.Max(1, target.MaxMana * percentPower / 100));
            if (percentEndurance > 0 && target.Endurance < target.MaxEndurance)
                endurance = target.ChangeEndurance(owner, eEnduranceChangeType.Spell, Math.Max(1, target.MaxEndurance * percentEndurance / 100));
            if (source != null && target is GamePlayer player && power + endurance > 0)
                player.Out.SendMessage($"{source} restores {power} power and {endurance} endurance.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
        }

        public void Provoke(GameLiving target, GameLiving onto, long amount)
        {
            if (target is GameNPC { IsAlive: true, Brain: StandardMobBrain brain })
                brain.AddToAggroList(onto, amount);
        }

        public static int Cure(GameLiving target, params eEffect[] kinds)
        {
            int removed = 0;
            foreach (ECSGameEffect effect in target.effectListComponent.GetEffects())
            {
                if (kinds.Contains(effect.EffectType) && !effect.IsEnding && effect.End())
                    removed++;
            }
            if (removed > 0 && target is GamePlayer player)
                player.Out.SendMessage($"{removed} harmful effect{(removed == 1 ? " is" : "s are")} removed.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            return removed;
        }

        public static readonly eEffect[] Harmful =
        {
            eEffect.DamageOverTime, eEffect.Disease, eEffect.Nearsight, eEffect.Snare, eEffect.MovementSpeedDebuff,
            eEffect.StrengthDebuff, eEffect.DexterityDebuff, eEffect.ConstitutionDebuff, eEffect.StrConDebuff, eEffect.DexQuiDebuff,
            eEffect.WsConDebuff, eEffect.ArmorAbsorptionDebuff, eEffect.ArmorFactorDebuff, eEffect.MeleeHasteDebuff
        };

        public static bool DispelOne(GameLiving target)
        {
            ECSGameEffect buff = target.effectListComponent.GetEffects()
                .OfType<ECSGameSpellEffect>()
                .FirstOrDefault(e => !e.IsEnding && e.SpellHandler != null && e.SpellHandler.HasPositiveEffect && !e.IsConcentrationEffect() &&
                                     e.SpellHandler.Caster != target && e is not MasterLevelEffect);
            return buff != null && buff.End();
        }

        public MasterLevelField Field(Point3D at, MasterLevelFieldOptions options) => MasterLevelField.Place(Caster, this, at, options);
    }

    /// <summary>A timed Master Level buff or debuff holding the ability's property changes, with its own icon.</summary>
    public class MasterLevelEffect : ECSGameSpellEffect
    {
        private MLMod[] _mods = Array.Empty<MLMod>();
        private ushort _originalSize;

        public MasterLevelEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        private MLAbility Ability => (SpellHandler as MasterLevelAbilityHandler)?.Ability;

        public MasterLevelEffect(in ECSGameEffectInitParams initParams, bool permanent) : base(initParams)
        {
            if (permanent)
            {
                Duration = 0;
                ExpireTick = long.MaxValue;
            }
        }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            _mods = (SpellHandler as IMasterLevelMods)?.Mods.ToArray() ?? Array.Empty<MLMod>();
            foreach (MLMod mod in _mods)
                mod.Apply(Owner, this, true);
            if (Ability is { Grow: > 0 } && Owner is GameNPC npc)
            {
                _originalSize = npc.Size;
                npc.Size = (byte)Math.Min(255, npc.Size * (100 + Ability.Grow) / 100);
            }
            MLMod.Refresh(Owner);
        }

        public override void OnStopEffect()
        {
            foreach (MLMod mod in _mods)
                mod.Apply(Owner, this, false);
            if (_originalSize > 0 && Owner is GameNPC npc)
                npc.Size = (byte)_originalSize;
            MLMod.Refresh(Owner);
            base.OnStopEffect();
        }
    }

    /// <summary>"Your next spell..." abilities: Leaping Health, Rampant Healing.</summary>
    public class MasterLevelTriggerEffect : ECSGameSpellEffect
    {
        public MasterLevelTriggerEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

        public override void OnStartEffect()
        {
            base.OnStartEffect();
            GameEventMgr.AddHandler(Owner, GameLivingEvent.CastFinished, OnCastFinished);
        }

        public override void OnStopEffect()
        {
            GameEventMgr.RemoveHandler(Owner, GameLivingEvent.CastFinished, OnCastFinished);
            base.OnStopEffect();
        }

        private void OnCastFinished(DOLEvent e, object sender, EventArgs args)
        {
            if (args is not CastingEventArgs { SpellHandler: HealSpellHandler heal } cast ||
                !IsHeal(heal.Spell) || heal.ResolvedHealAmount <= 0)
                return;
            if ((SpellHandler as MasterLevelAbilityHandler)?.Ability?.OnCast?.Invoke(this, heal, cast.Target ?? Owner) == true)
                End();
        }

        public static bool IsHeal(Spell spell) => spell != null && spell.Value != 0 &&
            spell.SpellType is eSpellType.Heal or eSpellType.CombatHeal or eSpellType.OmniHeal;
    }

    public static class MasterLevelAbilities
    {
        public const int FirstId = 63000;
        public static readonly string[] Paths = { "Banelord", "Battlemaster", "Convoker", "Perfecter", "Sojourner", "Spymaster", "Stormlord", "Warlord" };
        private static readonly Dictionary<(string, int), MLAbility> _table = new();
        private static readonly Dictionary<int, MLAbility> _byId = new();

        /// <summary>
        /// Used from the casting component: a break-free ability fires at once while its user is stunned or
        /// mesmerized, which the normal cast path refuses, and then starts its own reuse timer.
        /// </summary>
        public static bool TryBreakFree(GameLiving caster, Spell spell, SpellLine line)
        {
            if (!caster.IsCrowdControlled || spell?.SpellType is not (eSpellType.MasterLevelAbility or eSpellType.StormSovereignAbility) || For(spell.ID) is not { BreaksControl: true })
                return false;
            if (caster.GetSkillDisabledDuration(spell) > 0)
            {
                (caster as GamePlayer)?.Out.SendMessage($"You must wait {caster.GetSkillDisabledDuration(spell) / 1000 + 1} seconds to use {spell.Name} again.", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                return true;
            }
            var handler = new MasterLevelAbilityHandler(caster, spell, line);
            handler.Ability.Run(handler, caster);
            caster.DisableSkill(spell, spell.RecastDelay);
            return true;
        }

        public static MLAbility For(int spellId)
        {
            if (_byId.TryGetValue(spellId, out MLAbility champion))
                return champion;
            int offset = spellId - FirstId;
            if (offset < 0 || offset >= Paths.Length * 10)
                return null;
            return _table.GetValueOrDefault((Paths[offset / 10], offset % 10 + 1));
        }

        public static MLAbility For(string path, int level) => _table.GetValueOrDefault((path, level));

        private static void Add(MLAbility ability) => _table[(ability.Path, ability.Level)] = ability;

        private static int Sec(Spell s) => s.Duration / 1000;

        static MasterLevelAbilities()
        {
            Banelord();
            Battlemaster();
            Convoker();
            Perfecter();
            Sojourner();
            Spymaster();
            Warlord();
            foreach (var (id, ability) in ChampionAbilities.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in StormSovereignAbilities.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in StormRemakeStyles.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in StormRemakeSpells.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in StormRegaliaSpells.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in TuathaRemakeStyles.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in TuathaRemakeSpells.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in TaraRegaliaSpells.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in PendragonRemakeStyles.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in PendragonRemakeSpells.Build())
                _byId[id] = ability;
            foreach (var (id, ability) in CamelotRegaliaSpells.Build())
                _byId[id] = ability;
        }

        // ---------------- Banelord: shouts that cripple enemies around you ----------------
        private static void Banelord()
        {
            Add(new MLAbility
            {
                Path = "Banelord", Level = 1, Mods = new[] { MLMod.Debuff(eProperty.CastingSpeed, 50) },
                Describe = s => $"Shout: enemies within {s.Radius} cast 50% slower for {Sec(s)} s and are interrupted.",
                Run = (h, _) =>
                {
                    foreach (GameLiving enemy in h.Enemies(h.Caster, h.Spell.Radius))
                    {
                        h.Hold(enemy);
                        enemy.StartInterruptTimer(enemy.SpellInterruptDuration, AttackData.eAttackType.Spell, h.Caster);
                        h.Provoke(enemy, h.Caster, 50);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 2,
                Describe = s => $"Shout: enemies within {s.Radius} lose 10% of their current health (up to {s.Damage} damage), power and endurance.",
                Run = (h, _) => Agony(h, 10, (int)h.Spell.Damage)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 3, Mods = new[] { MLMod.Speed(0.6), MLMod.Debuff(eProperty.MeleeSpeed, 20) },
                Describe = s => $"Shout: enemies within {s.Radius} are slowed 40% and attack 20% slower for {Sec(s)} s.",
                Run = (h, _) => HoldEnemies(h)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 4, Mods = new[] { MLMod.Debuff(eProperty.FatigueConsumption, 50), MLMod.Debuff(eProperty.MeleeDamage, 15) },
                Describe = s => $"Shout: enemies within {s.Radius} spend 50% more endurance and deal 15% less melee damage for {Sec(s)} s.",
                Run = (h, _) => HoldEnemies(h)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 5, Positive = true, Mods = new[] { MLMod.Buff(eProperty.MissHit, 15) },
                Describe = s => $"Shout: you and allies within {s.Radius} are 15% harder to hit for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 6,
                Describe = s => $"Shout: snares enemies within {s.Radius} by {s.Value}% for {Sec(s)} s. You are no longer stunned by it.",
                Run = (h, _) => CastOnEnemies(h, eSpellType.SpeedDecrease, db => { db.Value = h.Spell.Value; db.Duration = Sec(h.Spell); })
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 7, Positive = true, Mods = new[] { MLMod.Buff(eProperty.ToHitBonus, 10), MLMod.Buff(eProperty.ResistPierce, 10) },
                Describe = s => $"Shout: you and allies within {s.Radius} gain 10% to hit and 10% resist piercing for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 8,
                Describe = s => $"Shout: enemies within {s.Radius} lose 25% of their current health (up to {s.Damage} damage), power and endurance. Costs you half your current health.",
                Run = (h, _) =>
                {
                    h.Caster.ChangeHealth(h.Caster, eHealthChangeType.Spell, -h.Caster.Health / 2);
                    Agony(h, 25, (int)h.Spell.Damage);
                }
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 9, Mods = new[] { MLMod.Debuff(eProperty.MeleeDamage, 20), MLMod.Debuff(eProperty.SpellDamage, 20) },
                Describe = s => $"Shout: enemies within {s.Radius} deal 20% less melee and spell damage for {Sec(s)} s.",
                Run = (h, _) => HoldEnemies(h)
            });
            Add(new MLAbility
            {
                Path = "Banelord", Level = 10, Positive = true, Mods = new[] { MLMod.Buff(eProperty.MeleeDamage, 15), MLMod.Buff(eProperty.SpellDamage, 15) },
                Describe = s => $"Shout: you and allies within {s.Radius} deal 15% more melee and spell damage for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
        }

        private static void HoldEnemies(MasterLevelAbilityHandler h)
        {
            var enemies = h.Enemies(h.Caster, h.Spell.Radius).ToList();
            foreach (GameLiving enemy in enemies)
            {
                h.Hold(enemy);
                h.Animate(enemy);
                h.Provoke(enemy, h.Caster, 50);
            }
            h.Report(enemies.Count, "enemies");
        }

        private static void HoldAllies(MasterLevelAbilityHandler h)
        {
            var allies = h.Allies(h.Caster, h.Spell.Radius).ToList();
            foreach (GameLiving ally in allies)
                h.Hold(ally);
            h.Report(allies.Count, "allies");
        }

        /// <summary>Applies a stock effect to every enemy around the caster and reports how many.</summary>
        private static void CastOnEnemies(MasterLevelAbilityHandler h, eSpellType type, Action<DbSpell> setup)
        {
            var enemies = h.Enemies(h.Caster, h.Spell.Radius).ToList();
            foreach (GameLiving enemy in enemies)
                h.Cast(enemy, 1, type, setup);
            h.Report(enemies.Count, "enemies");
        }

        private static void Agony(MasterLevelAbilityHandler h, int percent, int cap)
        {
            foreach (GameLiving enemy in h.Enemies(h.Caster, h.Spell.Radius))
            {
                h.DamageNow(enemy, Math.Min(cap, enemy.Health * percent / 100), eDamageType.Spirit);
                if (enemy is GamePlayer)
                    enemy.ChangeMana(h.Caster, eManaChangeType.Spell, -enemy.Mana * percent / 100);
                enemy.ChangeEndurance(h.Caster, eEnduranceChangeType.Spell, -enemy.Endurance * percent / 100);
            }
        }

        // ---------------- Battlemaster: weapon tricks, usable by any class ----------------
        private static void Battlemaster()
        {
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 1,
                Describe = s => $"Strike your enemy for {s.Damage} damage; enemies within 350 of it lose 25% endurance.",
                Run = (h, t) =>
                {
                    h.Damage(t, h.Spell.Damage, eDamageType.Crush);
                    foreach (GameLiving enemy in h.Enemies(t, 350))
                        enemy.ChangeEndurance(h.Caster, eEnduranceChangeType.Spell, -enemy.MaxEndurance / 4);
                }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 2, Mods = new[] { MLMod.Debuff(eProperty.ArmorAbsorption, 20) },
                Describe = s => $"Strike a weak point for {s.Damage} damage; the enemy absorbs 20% less physical damage for {Sec(s)} s.",
                Run = (h, t) => { h.Damage(t, h.Spell.Damage, eDamageType.Slash); h.Hold(t); }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 3,
                Describe = s => $"Strike for {s.Damage} damage; enemies within 350 of the target are interrupted and lose 20% power.",
                Run = (h, t) =>
                {
                    h.Damage(t, h.Spell.Damage, eDamageType.Energy);
                    foreach (GameLiving enemy in h.Enemies(t, 350))
                    {
                        enemy.StartInterruptTimer(enemy.SpellInterruptDuration, AttackData.eAttackType.Spell, h.Caster);
                        if (enemy is GamePlayer)
                            enemy.ChangeMana(h.Caster, eManaChangeType.Spell, -enemy.MaxMana / 5);
                        h.Animate(enemy);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 4, Mods = new[] { MLMod.Debuff(eProperty.MeleeDamage, 50) },
                Describe = s => $"Grapple your enemy: it is rooted and deals half melee damage for {Sec(s)} s. You stay free.",
                Run = (h, t) =>
                {
                    h.Cast(t, 1, eSpellType.SpeedDecrease, db => { db.Value = 99; db.Duration = Sec(h.Spell); });
                    h.Hold(t);
                    h.Provoke(t, h.Caster, 100);
                }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 5, Positive = true,
                Describe = s => $"Your group's melee attacks deal {s.Damage} extra energy damage for {Sec(s)} s.",
                Run = (h, _) =>
                {
                    foreach (GameLiving member in h.Group(1500))
                        h.Cast(member, 1, eSpellType.DamageAdd, db => { db.Target = "Realm"; db.Damage = h.Spell.Damage; db.Duration = Sec(h.Spell); });
                }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 6,
                Describe = s => $"Hurl your weapon up to {s.Range} units for {s.Damage} crush damage.",
                Run = (h, t) => h.Damage(t, h.Spell.Damage, eDamageType.Crush)
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 7, Mods = MLMod.Resists(MLCategory.Debuff, 15),
                Describe = s => $"Sear your enemy and all enemies within 350 of it: -15% magic resists for {Sec(s)} s.",
                Run = (h, t) => { foreach (GameLiving enemy in h.Enemies(t, 350)) { h.Hold(enemy); h.Animate(enemy); } }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 8, Positive = true,
                Mods = new[] { MLMod.Buff(eProperty.ArmorAbsorption, 20), MLMod.Buff(eProperty.MissHit, 10) },
                Describe = s => $"Bodyguard an ally (or yourself): +20% absorption and 10% harder to hit for {Sec(s)} s.",
                Run = (h, t) => { h.Hold(t); h.Animate(t); }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 9, Mods = new[] { MLMod.Debuff(eProperty.Dexterity, 50), MLMod.Debuff(eProperty.Quickness, 50) },
                Describe = s => $"Dampen your enemy and all enemies within 350 of it: -50 dexterity and quickness for {Sec(s)} s.",
                Run = (h, t) => { foreach (GameLiving enemy in h.Enemies(t, 350)) { h.Hold(enemy); h.Animate(enemy); } }
            });
            Add(new MLAbility
            {
                Path = "Battlemaster", Level = 10,
                Describe = s => $"Shatter: your enemy and enemies within 350 of it lose one beneficial spell effect and take {s.Damage} damage.",
                Run = (h, t) =>
                {
                    foreach (GameLiving enemy in h.Enemies(t, 350))
                    {
                        MasterLevelAbilityHandler.DispelOne(enemy);
                        h.Damage(enemy, h.Spell.Damage, eDamageType.Energy);
                    }
                }
            });
        }

        // ---------------- Convoker: crystals, wards, traps and constructs ----------------
        private static void Convoker()
        {
            Add(new MLAbility
            {
                Path = "Convoker", Level = 1, Ground = true, Positive = true,
                Mods = MLMod.Resists(MLCategory.Buff, 10, physical: true).Append(MLMod.Buff(eProperty.ArmorAbsorption, 10)).ToArray(),
                Describe = s => $"Ground crystal ({Sec(s) / 60} min, {s.Radius} radius): allies inside gain 10% absorption and 10% to all resists.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Friendly = true, Model = 1905, Size = 50, Name = "Warding Crystal"
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 2, Ground = true, Mods = MLMod.Resists(MLCategory.Debuff, 10, physical: true),
                Describe = s => $"Ground node ({Sec(s) / 60} min, {s.Radius} radius): reveals hidden enemies inside, who also lose 10% of all resists.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2584, Name = "Prescience Node", PulseEffect = 7312,
                    OnPulse = (f, enemy) => enemy.Stealth(false)
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 3, Ground = true,
                Describe = s => $"Rune: the first enemy within {s.Radius} sets it off, stunning enemies nearby for 4 s, dealing {s.Damage} damage and draining 30% power.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2590, Name = "Power Trap", PulseEffect = 7253, Trap = true,
                    OnPulse = (f, enemy) =>
                    {
                        f.Ability.Cast(enemy, 1, eSpellType.Stun, db => db.Duration = 4);
                        f.Ability.Damage(enemy, f.Ability.Spell.Damage);
                        if (enemy is GamePlayer)
                            enemy.ChangeMana(f.StormOwner, eManaChangeType.Spell, -enemy.MaxMana * 30 / 100);
                    }
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 4, Ground = true,
                Describe = s => $"Ward ({Sec(s) / 60} min, {s.Radius} radius): enemies inside move at half speed.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2586, Name = "Speedwarp", PulseEffect = 7237,
                    OnPulse = (f, enemy) => f.Ability.Cast(enemy, 1, eSpellType.SpeedDecrease, db => { db.Value = 50; db.Duration = 6; db.ClientEffect = 0; })
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 5, Ground = true,
                Describe = s => $"Warcrystal ({Sec(s)} s): every 2 s strikes the nearest enemy within {s.Radius} for {s.Damage} energy damage.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 1862, Size = 60, Name = "Warcrystal", PulseEffect = 7216, Interval = 2000, SingleTarget = true,
                    OnPulse = (f, enemy) => f.Ability.Damage(enemy, f.Ability.Spell.Damage)
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 6, Ground = true,
                Describe = s => $"Battlewarder ({Sec(s)} s): every 2 s pounds the nearest enemy within {s.Radius} for {s.Damage} crush damage. You may move freely.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 951, Size = 60, Name = "Battlewarder", PulseEffect = 7232, Interval = 2000, SingleTarget = true,
                    OnPulse = (f, enemy) => f.Ability.Damage(enemy, f.Ability.Spell.Damage, eDamageType.Crush)
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 7, Ground = true,
                Describe = s => $"Rune: the first enemy within {s.Radius} sets it off for {s.Damage} energy damage to every enemy nearby.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2588, Name = "Dissonance Trap", PulseEffect = 7255, Trap = true,
                    OnPulse = (f, enemy) => f.Ability.Damage(enemy, f.Ability.Spell.Damage)
                })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 8, Positive = true,
                Describe = s => $"A brittle guardian shields you: absorbs 50% of all damage, up to {s.Value}, for {Sec(s) / 60} min.",
                Run = (h, _) => h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db => { db.Target = "Self"; db.Value = h.Spell.Value; db.Damage = 50; db.Duration = Sec(h.Spell); })
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 9, Positive = true, Grow = 30,
                Mods = new[] { MLMod.Buff(eProperty.MeleeDamage, 25), MLMod.Buff(eProperty.SpellDamage, 25), MLMod.Buff(eProperty.ArmorAbsorption, 15) }
                    .Concat(MLMod.Resists(MLCategory.Buff, 15, physical: true)).ToArray(),
                Describe = s => $"All your pets within {s.Radius} grow 30% larger and gain 25% melee and spell damage, 15% absorption and 15% resists for {Sec(s) / 60} min.",
                Run = (h, _) =>
                {
                    var pets = h.Pets(h.Spell.Radius).Where(pet => !pet.effectListComponent.ContainsEffectForEffectType(eEffect.Juggernaut)).ToList();
                    foreach (GameNPC pet in pets)
                    {
                        h.Hold(pet);
                        h.Animate(pet);
                    }
                    h.Report(pets.Count, "pets");
                }
            });
            Add(new MLAbility
            {
                Path = "Convoker", Level = 10, Ground = true,
                Describe = s => $"A crystal titan rises for {Sec(s)} s: every 3 s it stomps all enemies within {s.Radius} for {s.Damage} crush damage.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 1439, Size = 80, Name = "Crystal Titan", PulseEffect = 7232, Interval = 3000,
                    OnPulse = (f, enemy) => f.Ability.Damage(enemy, f.Ability.Spell.Damage, eDamageType.Crush)
                })
            });
        }

        /// <summary>Percent a Perfecter font restores per pulse: twice the spell's Value, the same in or out of combat (DR-258).</summary>
        public static int FontPercent(Spell spell) => (int)spell.Value * 2;

        private static void PlaceField(MasterLevelAbilityHandler h, MasterLevelFieldOptions options)
        {
            if (h.GroundPoint(out Point3D at))
                h.Field(at, options);
        }

        // ---------------- Perfecter: fonts, cures and stronger healing ----------------
        private static void Perfecter()
        {
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 1, Positive = true,
                Describe = s => $"Your group within {s.Radius} is cured of nearsight and mesmerize and healed for 10%.",
                Run = (h, _) =>
                {
                    foreach (GameLiving member in h.Group(h.Spell.Radius))
                    {
                        MasterLevelAbilityHandler.Cure(member, eEffect.Nearsight, eEffect.Mez);
                        h.Heal(member, member.MaxHealth / 10);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 2, Positive = true,
                Describe = s => $"Your group within {s.Radius} is cured of poisons, disease and damage over time.",
                Run = (h, _) =>
                {
                    foreach (GameLiving member in h.Group(h.Spell.Radius))
                    {
                        MasterLevelAbilityHandler.Cure(member, eEffect.DamageOverTime, eEffect.Disease);
                        h.Animate(member);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 3, Ground = true, Positive = true,
                // DR-258: fonts work at full strength in combat too (the old rate halved them for anyone fighting).
                Describe = s => $"Font ({Sec(s) / 60} min, {s.Radius} radius): every 4 s heals allies inside for {FontPercent(s)}% of their health, in or out of combat.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Friendly = true, Model = 2585, Name = "Sphere of Rejuvenation", PulseEffect = 7245,
                    OnPulse = (f, ally) => f.Ability.Heal(ally, ally.MaxHealth * FontPercent(f.Ability.Spell) / 100)
                })
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 4, Passive = true,
                Describe = s => "Passive: +30% concentration and +10% healing."
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 5, Ground = true, Positive = true,
                Describe = s => $"Font ({Sec(s) / 60} min, {s.Radius} radius): every 4 s restores {FontPercent(s)}% power and endurance to allies inside, in or out of combat.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Friendly = true, Model = 2583, Name = "Font of Power", PulseEffect = 7212,
                    OnPulse = (f, ally) =>
                    {
                        int percent = FontPercent(f.Ability.Spell);
                        MasterLevelAbilityHandler.Restore(f.StormOwner, ally, percent, percent, f.Ability.Spell.Name);
                    }
                })
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 6, Ground = true, Positive = true,
                Mods = new[] { MLMod.Buff(eProperty.MesmerizeDurationReduction, 50), MLMod.Buff(eProperty.StunDurationReduction, 50), MLMod.Buff(eProperty.SpeedDecreaseDurationReduction, 50) },
                Describe = s => $"Field ({Sec(s) / 60} min, {s.Radius} radius): mesmerize, stun and snare effects on allies inside last 50% shorter.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Friendly = true, Model = 2581, Name = "Determination Field", PulseEffect = 7214
                })
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 7, Positive = true,
                OnCast = (e, heal, target) =>
                {
                    if (!MasterLevelTriggerEffect.IsHeal(heal.Spell) || heal.Spell.Radius > 0)
                        return false;
                    var h = (MasterLevelAbilityHandler)e.SpellHandler;
                    foreach (GameLiving ally in h.Allies(target, h.Spell.Radius).Where(a => a != target))
                        h.Heal(ally, (int)(heal.ResolvedHealAmount * h.Spell.Value / 100));
                    return true;
                },
                Describe = s => $"For {Sec(s)} s: your next single-target heal also heals allies within {s.Radius} of the target for {s.Value}%.",
                Run = (h, t) => h.Hold(h.Caster)
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 8, Positive = true,
                Describe = s => $"Removes resurrection illness from an ally and heals them for {s.Value}% of their health.",
                Run = (h, t) =>
                {
                    MasterLevelAbilityHandler.Cure(t, eEffect.ResurrectionIllness, eEffect.RvrResurrectionIllness);
                    h.Heal(t, t.MaxHealth * (int)h.Spell.Value / 100);
                }
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 9, Ground = true,
                Describe = s => $"Ward ({Sec(s) / 60} min, {s.Radius} radius): every 4 s deals {s.Damage} spirit damage to enemies inside and drains 5% of their power.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2582, Name = "Dissonating Ward", PulseEffect = 7310,
                    OnPulse = (f, enemy) =>
                    {
                        f.Ability.Damage(enemy, f.Ability.Spell.Damage, eDamageType.Spirit);
                        if (enemy is GamePlayer)
                            enemy.ChangeMana(f.StormOwner, eManaChangeType.Spell, -enemy.MaxMana / 20);
                    }
                })
            });
            Add(new MLAbility
            {
                Path = "Perfecter", Level = 10, Positive = true,
                OnCast = (e, heal, target) =>
                {
                    if (!MasterLevelTriggerEffect.IsHeal(heal.Spell))
                        return false;
                    var h = (MasterLevelAbilityHandler)e.SpellHandler;
                    foreach (GameLiving ally in h.Allies(h.Caster, h.Spell.Radius).Where(a => a != target))
                        h.Heal(ally, heal.ResolvedHealAmount);
                    return true;
                },
                Describe = s => $"For {Sec(s)} s: your next heal also heals every ally within {s.Radius} of you for its full amount.",
                Run = (h, t) => h.Hold(h.Caster)
            });
        }

        // ---------------- Sojourner: travel, protection and supplies ----------------
        private static void Sojourner()
        {
            Add(new MLAbility { Path = "Sojourner", Level = 1, Passive = true, Describe = s => "Passive: you can carry 50% more." });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 2, Positive = true,
                Describe = s => $"Your group within {s.Radius} can breathe underwater for {Sec(s) / 60} min.",
                Run = (h, _) =>
                {
                    foreach (GameLiving member in h.Group(h.Spell.Radius))
                        h.Cast(member, 1, eSpellType.WaterBreathing, db => { db.Target = "Realm"; db.Duration = Sec(h.Spell); });
                }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 3, Positive = true,
                Describe = s => $"Your group within {s.Radius} travels {s.Value - 100}% faster for {Sec(s) / 60} min. Combat ends it.",
                Run = (h, _) =>
                {
                    foreach (GameLiving member in h.Group(h.Spell.Radius))
                        h.Cast(member, 1, eSpellType.SpeedEnhancement, db => { db.Target = "Realm"; db.Value = h.Spell.Value; db.Duration = Sec(h.Spell); });
                }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 4, Positive = true,
                Describe = s => "Unmake every snare, damage over time, disease, nearsight and stat or armor debuff on yourself.",
                Run = (h, _) => { MasterLevelAbilityHandler.Cure(h.Caster, MasterLevelAbilityHandler.Harmful); h.Animate(h.Caster); }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 5, Positive = true,
                Describe = s => $"Summon an Ancient Transmuter for {Sec(s)} s who buys your loot and sells arrows and poisons.",
                Run = (h, _) => SummonTransmuter(h)
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 6, Positive = true,
                Describe = s => "Return to your bind point. Fails in combat.",
                Run = (h, _) =>
                {
                    if (h.Caster is GamePlayer player && !player.InCombat && !GameRelic.IsPlayerCarryingRelic(player))
                        player.MoveToBind();
                }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 7, Positive = true, Mods = MLMod.Resists(MLCategory.Buff, 15),
                Describe = s => $"Your group within {s.Radius} gains 15% to all magic resists for {Sec(s) / 60} min.",
                Run = (h, _) => { foreach (GameLiving member in h.Group(h.Spell.Radius)) h.Hold(member); }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 8,
                Describe = s => "A forceful zephyr hurls your enemy 700 units away and roots it for 5 s.",
                Run = (h, t) =>
                {
                    double dx = t.X - h.Caster.X, dy = t.Y - h.Caster.Y, length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    var away = new Point3D(t.X + (int)(dx * 700 / length), t.Y + (int)(dy * 700 / length), t.Z);
                    if (StormforgedTravel.TryResolvePoint(t, away, 800, out Point3D landing))
                    {
                        if (t is GameNPC npc)
                            npc.MoveInRegion(npc.CurrentRegionID, landing.X, landing.Y, landing.Z, npc.Heading, true);
                        else
                            t.MoveTo(t.CurrentRegionID, landing.X, landing.Y, landing.Z, t.Heading);
                    }
                    h.Cast(t, 1, eSpellType.SpeedDecrease, db => { db.Value = 99; db.Duration = 5; });
                    h.Animate(t);
                }
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 9, Positive = true,
                Describe = s => $"Phase out for {Sec(s)} s: all damage you take is absorbed, up to {s.Value}.",
                Run = (h, _) => h.Cast(h.Caster, 1, eSpellType.BothAblativeArmor, db => { db.Target = "Self"; db.Value = h.Spell.Value; db.Damage = 100; db.Duration = Sec(h.Spell); })
            });
            Add(new MLAbility
            {
                Path = "Sojourner", Level = 10, Positive = true,
                Describe = s => "Return your whole group, companions included, to your bind point. Fails if anyone is in combat.",
                Run = (h, _) =>
                {
                    if (h.Caster is not GamePlayer player)
                        return;
                    var members = h.Caster.Group?.GetMembersInTheGroup().ToList() ?? new List<GameLiving> { player };
                    if (members.Any(m => m.InCombat))
                    {
                        player.Out.SendMessage("You can't teleport a group that is in combat!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        return;
                    }
                    foreach (GameLiving member in members)
                        member.MoveTo((ushort)player.BindRegion, player.BindXpos, player.BindYpos, player.BindZpos, (ushort)player.BindHeading);
                }
            });
        }

        private static void SummonTransmuter(MasterLevelAbilityHandler h)
        {
            var merchant = new GameMerchant
            {
                X = h.Caster.X + Util.Random(-40, 40), Y = h.Caster.Y + Util.Random(-40, 40), Z = h.Caster.Z,
                CurrentRegion = h.Caster.CurrentRegion, Heading = (ushort)((h.Caster.Heading + 2048) % 4096),
                Level = 50, Realm = h.Caster.Realm, Name = "Ancient Transmuter", Model = 993, Size = 50, MaxSpeedBase = 0,
                GuildName = string.Empty, TradeItems = new MerchantTradeItems("ML_transmuteritems")
            };
            merchant.Flags |= GameNPC.eFlags.PEACE;
            if (!merchant.AddToWorld())
                return;
            new ECSGameTimer(merchant, _ => { merchant.Delete(); return 0; }, Math.Max(10000, h.Spell.Duration));
        }

        // ---------------- Spymaster: decoys, runes and concealment ----------------
        private static void Spymaster()
        {
            Add(new MLAbility { Path = "Spymaster", Level = 1, Passive = true, Describe = s => "Passive: monsters you kill drop 50% more coin." });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 2, Ground = true,
                Describe = s => $"A decoy of you appears at your ground target for {Sec(s)} s; monsters within {s.Radius} fighting you or your group turn on it.",
                Run = (h, _) => SummonDecoy(h)
            });
            Add(new MLAbility { Path = "Spymaster", Level = 3, Passive = true, Describe = s => "Passive: half of your weapon poison applications don't use a charge." });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 4, Mods = new[] { MLMod.Debuff(eProperty.ArmorAbsorption, 20), MLMod.Debuff(eProperty.MeleeSpeed, 20) },
                Describe = s => $"Sabotage an enemy's gear: 20% less absorption and 20% slower attacks for {Sec(s)} s.",
                Run = (h, t) => { h.Hold(t); h.Animate(t); }
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 5, Ground = true,
                Describe = s => $"Rune: the first enemy within {s.Radius} sets it off, snaring all enemies nearby by {s.Value}% for 20 s.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2592, Name = "Tanglesnare", PulseEffect = 7220, Trap = true,
                    OnPulse = (f, enemy) => f.Ability.Cast(enemy, 1, eSpellType.SpeedDecrease, db => { db.Value = f.Ability.Spell.Value; db.Duration = 20; })
                })
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 6, Ground = true,
                Describe = s => $"Rune: the first enemy within {s.Radius} sets it off, poisoning all enemies nearby for {s.Damage} body damage every 3 s for 30 s.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2589, Name = "Poisonspike", PulseEffect = 7281, Trap = true,
                    OnPulse = (f, enemy) => f.Ability.Cast(enemy, 1, eSpellType.DamageOverTime, db =>
                        { db.Damage = f.Ability.Spell.Damage; db.DamageType = (int)eDamageType.Body; db.Frequency = 30; db.Duration = 30; })
                })
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 7, Positive = true, KeepsStealth = true, Mods = new[] { MLMod.Buff(eProperty.ToHitBonus, 10) },
                Describe = s => $"Keep watch for {Sec(s)} s: hidden enemies within {s.Radius} of you are revealed and allies near you gain 10% to hit.",
                Run = (h, _) => h.Field(new Point3D(h.Caster.X, h.Caster.Y, h.Caster.Z), new MasterLevelFieldOptions
                {
                    Name = "Lookout", FollowOwner = true, Both = true, Hidden = true,
                    OnPulse = (f, living) => { if (f.IsHostile(living)) living.Stealth(false); }
                })
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 8, Ground = true,
                Describe = s => $"Rune: the first enemy within {s.Radius} sets it off for {s.Damage} crush damage to every enemy nearby, siege engines included.",
                Run = (h, _) => PlaceField(h, new MasterLevelFieldOptions
                {
                    Model = 2591, Name = "Wrecking Rune", PulseEffect = 7301, Trap = true,
                    OnPulse = (f, enemy) => f.Ability.Damage(enemy, f.Ability.Spell.Damage, eDamageType.Crush)
                })
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 9,
                Describe = s => $"Flare: mesmerizes enemies within {s.Radius} for {Sec(s)} s.",
                Run = (h, _) => CastOnEnemies(h, eSpellType.Mesmerize, db => db.Duration = Sec(h.Spell))
            });
            Add(new MLAbility
            {
                Path = "Spymaster", Level = 10, Positive = true, KeepsStealth = true,
                Describe = s => $"Hide your group's players within {s.Radius} in stealth. Attacking or casting reveals them.",
                Run = (h, _) =>
                {
                    foreach (GamePlayer member in h.Group(h.Spell.Radius).OfType<GamePlayer>())
                        if (!member.InCombat)
                            member.Stealth(true);
                }
            });
        }

        private static void SummonDecoy(MasterLevelAbilityHandler h)
        {
            if (!h.GroundPoint(out Point3D at))
                return;
            var decoy = new MasterLevelDecoy(h.Caster.MaxHealth * 2)
            {
                X = at.X, Y = at.Y, Z = at.Z, CurrentRegion = h.Caster.CurrentRegion, Heading = h.Caster.Heading,
                Name = h.Caster.Name, Model = h.Caster.Model, Size = 50, Level = h.Caster.Level, Realm = h.Caster.Realm
            };
            if (!decoy.AddToWorld())
                return;
            decoy.Health = decoy.MaxHealth;
            var fighting = h.Group(5000).ToHashSet();
            foreach (GameNPC mob in decoy.GetNPCsInRadius((ushort)h.Spell.Radius))
            {
                if (mob.Brain is StandardMobBrain brain && mob.IsAlive && brain.HasAggro && mob.TargetObject is GameLiving victim && fighting.Contains(victim))
                    brain.AddToAggroList(decoy, 1_000_000);
            }
            new ECSGameTimer(decoy, _ => { decoy.Delete(); return 0; }, Math.Max(5000, h.Spell.Duration));
        }

        // ---------------- Warlord: rallying shouts for the whole party ----------------
        private static void Warlord()
        {
            Add(new MLAbility { Path = "Warlord", Level = 1, Passive = true, Describe = s => "Passive: +10% maximum health." });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 2, Positive = true,
                Describe = s => $"Shout: you and allies within {s.Radius} heal 20% of their health and restore 10% power and endurance.",
                Run = (h, _) =>
                {
                    foreach (GameLiving ally in h.Allies(h.Caster, h.Spell.Radius))
                    {
                        h.Heal(ally, ally.MaxHealth / 5);
                        MasterLevelAbilityHandler.Restore(h.Caster, ally, 10, 10, h.Spell.Name);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 3,
                Describe = s => $"Shout: enemies within {s.Radius} flee in terror for {Sec(s)} s.",
                Run = (h, _) => CastOnEnemies(h, eSpellType.Fear, db => db.Duration = Sec(h.Spell))
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 4, Positive = true, Mods = new[] { MLMod.Buff(eProperty.MissHit, 10) },
                Describe = s => $"Shout: you and allies within {s.Radius} are 10% harder to hit for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 5, Positive = true,
                Mods = new[] { new MLMod(eProperty.CriticalMeleeHitChance, MLCategory.Other, 10), new MLMod(eProperty.CriticalSpellHitChance, MLCategory.Ability, 10) },
                Describe = s => $"Shout: you and allies within {s.Radius} gain 10% melee and spell critical chance for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 6, Positive = true, Mods = new[] { MLMod.Buff(eProperty.FatigueConsumption, 50) },
                Describe = s => $"Shout: you and allies within {s.Radius} restore 20% endurance and spend half as much for {Sec(s)} s.",
                Run = (h, _) =>
                {
                    foreach (GameLiving ally in h.Allies(h.Caster, h.Spell.Radius))
                    {
                        h.Hold(ally);
                        MasterLevelAbilityHandler.Restore(h.Caster, ally, 0, 20, h.Spell.Name);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 7, Positive = true,
                Describe = s => $"Shout: you and allies within {s.Radius} are cleansed of damage over time, poison and disease and healed for 10%.",
                Run = (h, _) =>
                {
                    foreach (GameLiving ally in h.Allies(h.Caster, h.Spell.Radius))
                    {
                        MasterLevelAbilityHandler.Cure(ally, eEffect.DamageOverTime, eEffect.Disease);
                        h.Heal(ally, ally.MaxHealth / 10);
                    }
                }
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 8, Positive = true,
                Describe = s => $"Sacrifice half your current health to heal every other ally within {s.Radius} for 25% of their health plus {s.Value}.",
                Run = (h, _) =>
                {
                    h.Caster.ChangeHealth(h.Caster, eHealthChangeType.Spell, -h.Caster.Health / 2);
                    foreach (GameLiving ally in h.Allies(h.Caster, h.Spell.Radius).Where(a => a != h.Caster))
                        h.Heal(ally, ally.MaxHealth / 4 + (int)h.Spell.Value);
                }
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 9, Positive = true,
                Mods = new[] { MLMod.Buff(eProperty.MeleeDamage, 10), MLMod.Buff(eProperty.SpellDamage, 10), MLMod.Buff(eProperty.ToHitBonus, 10) },
                Describe = s => $"Shout: you and allies within {s.Radius} deal 10% more damage and gain 10% to hit for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
            Add(new MLAbility
            {
                Path = "Warlord", Level = 10, Positive = true,
                Mods = new[] { MLMod.Buff(eProperty.ArmorAbsorption, 25) }.Concat(MLMod.Resists(MLCategory.Buff, 15)).ToArray(),
                Describe = s => $"Shout: you and allies within {s.Radius} gain 25% absorption and 15% magic resists for {Sec(s)} s.",
                Run = (h, _) => HoldAllies(h)
            });
        }
    }

    /// <summary>Spymaster's decoy: a copy of the caster with twice their health that monsters can fight.</summary>
    public class MasterLevelDecoy : GameNPC
    {
        private readonly int _maxHealth;
        public MasterLevelDecoy(int maxHealth) : base(new AI.Brain.BlankBrain()) { _maxHealth = Math.Max(100, maxHealth); MaxSpeedBase = 0; }
        public override int MaxHealth => _maxHealth;
        public override void Die(GameObject killer) => Delete();
    }

    public sealed class MasterLevelFieldOptions
    {
        public string Name { get; init; } = "Field";
        public ushort Model { get; init; } = 3457;
        public byte Size { get; init; }
        /// <summary>Pulse every this many ms; the spell's Frequency or 4 s when 0.</summary>
        public int Interval { get; init; }
        public bool Friendly { get; init; }
        /// <summary>Affects allies and enemies; OnPulse decides, the ability's bonuses go to allies.</summary>
        public bool Both { get; init; }
        public Action<MasterLevelField, GameLiving> OnPulse { get; init; }
        /// <summary>Fires once on every enemy inside as soon as one is inside, then the field ends.</summary>
        public bool Trap { get; init; }
        /// <summary>Each pulse affects only the nearest enemy.</summary>
        public bool SingleTarget { get; init; }
        public bool FollowOwner { get; init; }
        /// <summary>The field object itself can't be seen (auras that follow the caster).</summary>
        public bool Hidden { get; init; }
        /// <summary>Nobody inside wears the ability's effect (its bonuses go elsewhere, e.g. a caster's group).</summary>
        public bool SuppressEffect { get; init; }
        /// <summary>Client effect played on each living a pulse touches; the spell's own effect when 0.</summary>
        public ushort PulseEffect { get; init; }
    }
}

namespace DOL.GS
{
    using DOL.GS.Spells;

    /// <summary>
    /// A ground object owned by a Master Level caster: fonts, wards, runes, crystals and constructs.
    /// One per caster per ability; recasting replaces it. Ends with its duration or when the owner dies,
    /// leaves the region or logs out. Everyone it works on gets the ability's effect (icon and bonuses)
    /// for a little longer than one pulse, so it fades soon after they leave.
    /// </summary>
    public class MasterLevelField : GameStorm
    {
        private static readonly Dictionary<GameLiving, List<MasterLevelField>> _byOwner = new();
        private static readonly Lock _registryLock = new();
        private ECSGameTimer _timer;

        public GameLiving StormOwner { get; }
        public MasterLevelAbilityHandler Ability { get; }
        public MasterLevelFieldOptions Options { get; }
        public long ExpireTick { get; set; }
        public int FieldRadius { get; }

        public MasterLevelField(GameLiving owner, MasterLevelAbilityHandler ability, MasterLevelFieldOptions options)
        {
            StormOwner = owner;
            Ability = ability;
            Options = options;
            Owner = owner as GamePlayer;
            Name = options.Name;
            Model = options.Model;
            if (options.Size > 0)
                Size = options.Size;
            // The storm (3457), ward and rune objects draw their look from the object's realm (objects.csv realm effect
            // sets, e.g. storm_base_alb/_mid/_hib); realm None drew the Hibernian green for everyone. PEACE keeps it untargetable.
            Realm = owner.Realm;
            Flags |= eFlags.PEACE;
            if (options.Hidden)
                Flags |= eFlags.STEALTH;
            FieldRadius = Math.Max(1, ability.Spell.Radius);
        }

        public static IReadOnlyList<MasterLevelField> OwnedBy(GameLiving owner)
        {
            lock (_registryLock)
                return _byOwner.TryGetValue(owner, out var fields) ? fields.ToList() : new List<MasterLevelField>();
        }

        public static MasterLevelField Place(GameLiving owner, MasterLevelAbilityHandler ability, Point3D at, MasterLevelFieldOptions options)
        {
            foreach (MasterLevelField old in OwnedBy(owner).Where(field => field.Ability.Spell.ID == ability.Spell.ID))
                old.Remove();
            var field = new MasterLevelField(owner, ability, options)
            {
                X = at.X, Y = at.Y, Z = at.Z, CurrentRegionID = owner.CurrentRegionID, Heading = owner.Heading,
                ExpireTick = GameLoop.GameLoopTime + Math.Max(1000, ability.Spell.Duration)
            };
            if (!field.AddToWorld())
                return null;
            lock (_registryLock)
            {
                if (!_byOwner.TryGetValue(owner, out var fields))
                    _byOwner[owner] = fields = new List<MasterLevelField>();
                fields.Add(field);
            }
            field._timer = new ECSGameTimer(field, field.Tick, field.Interval);
            if (owner is GamePlayer player)
                player.Out.SendMessage($"Your {ability.Spell.Name} takes shape.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            return field;
        }

        public int Interval => Options.Interval > 0 ? Options.Interval : Ability.Spell.Frequency > 0 ? Ability.Spell.Frequency : 4000;

        public bool IsHostile(GameLiving living) => living != StormOwner && GameServer.ServerRules.IsAllowedToAttack(StormOwner, living, true);

        public bool Affects(GameLiving living) => Options.Both
            ? IsHostile(living) || MasterLevelAbilityHandler.IsAlly(StormOwner, living)
            : Options.Friendly ? MasterLevelAbilityHandler.IsAlly(StormOwner, living) : IsHostile(living);

        /// <summary>Who wears the field's effect: allies of friendly fields, enemies of hostile fields with bonuses.</summary>
        public bool GrantsEffect(GameLiving living) =>
            Options.SuppressEffect ? false : Options.Friendly || Options.Both ? MasterLevelAbilityHandler.IsAlly(StormOwner, living) : Ability.Mods.Count > 0 && IsHostile(living);

        private int Tick(ECSGameTimer timer)
        {
            if (ObjectState != eObjectState.Active || !StormOwner.IsAlive || StormOwner.ObjectState != eObjectState.Active ||
                StormOwner.CurrentRegionID != CurrentRegionID || GameLoop.GameLoopTime >= ExpireTick)
            {
                Remove();
                return 0;
            }
            if (Options.FollowOwner)
                MoveInRegion(CurrentRegionID, StormOwner.X, StormOwner.Y, StormOwner.Z, StormOwner.Heading, true);
            var inside = MasterLevelAbilityHandler.Around(this, FieldRadius).Where(Affects).OrderBy(l => GetDistanceTo(l)).ToList();
            if (Options.Trap)
            {
                var victims = inside.Where(IsHostile).ToList();
                if (victims.Count == 0)
                    return Interval;
                Pulse(victims);
                Remove();
                return 0;
            }
            if (Options.SingleTarget)
                inside = inside.Where(IsHostile).Take(1).ToList();
            Pulse(inside);
            return Interval;
        }

        /// <summary>One pulse over the livings inside; exposed for tests.</summary>
        public void Pulse(IReadOnlyCollection<GameLiving> inside)
        {
            foreach (GameLiving living in inside)
            {
                if (GrantsEffect(living))
                    MasterLevelHold.Refresh(Ability, living, Interval + 2000);
                Options.OnPulse?.Invoke(this, living);
                if (IsHostile(living) && living is GameNPC { Brain: StandardMobBrain brain })
                    brain.AddToAggroList(StormOwner, 1);
                ushort effect = Options.PulseEffect != 0 ? Options.PulseEffect : (ushort)Ability.Spell.ClientEffect;
                if (effect != 0)
                    foreach (GamePlayer viewer in living.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                        viewer.Out.SendSpellEffectAnimation(this, living, effect, 0, false, 1);
            }
        }

        public void Remove()
        {
            _timer?.Stop();
            lock (_registryLock)
            {
                if (_byOwner.TryGetValue(StormOwner, out var fields))
                {
                    fields.Remove(this);
                    if (fields.Count == 0)
                        _byOwner.Remove(StormOwner);
                }
            }
            if (ObjectState == eObjectState.Active)
                Delete();
        }

        public override void Die(GameObject killer) => Remove();
    }
}
