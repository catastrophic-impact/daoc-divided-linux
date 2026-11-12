using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    // Stormlord, rebuilt for offline play on the ECS effect system. Storms sit at the caster's ground
    // target and pulse on monsters and enemy players alike. Each caster keeps one storm of each kind;
    // casting it again moves it. Storms end with their duration or when the caster dies, leaves the
    // region or logs out. Classic ML2/ML6 moved drifting storms; these storms stay put, so Vacuum
    // Vortex gathers them at a new spot and Focusing Winds makes them pulse faster.

    public abstract class StormlordSpell : SpellHandler
    {
        protected StormlordSpell(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override bool UnstealthCasterOnStart => false;
        public override void CastSubSpells(GameLiving target) { }
        public override double CalculateSpellResistChance(GameLiving target) => 0;

        protected bool GetDestination(out Point3D destination)
        {
            destination = null;
            if (!Caster.GroundTarget.IsValid || !Caster.GroundTargetInView)
                return false;
            return StormforgedTravel.TryResolvePoint(Caster,
                new Point3D(Caster.GroundTarget.X, Caster.GroundTarget.Y, Caster.GroundTarget.Z), Spell.Range, out destination);
        }
    }

    public abstract class StormlordGroundSpell : StormlordSpell
    {
        protected StormlordGroundSpell(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override bool CheckBeginCast(GameLiving target)
        {
            if (!GetDestination(out _))
            {
                MessageToCaster("Choose a visible, reachable ground target within spell range.", eChatType.CT_SpellResisted);
                return false;
            }
            return base.CheckBeginCast(target);
        }

        public override bool CheckEndCast(GameLiving target) => GetDestination(out _) && base.CheckEndCast(target);
    }

    /// <summary>A storm: what it does to each living inside it on every pulse.</summary>
    public abstract class StormlordStormSpell : StormlordGroundSpell, IMasterLevelMods
    {
        protected StormlordStormSpell(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override bool HasPositiveEffect => Friendly;
        public override bool HasConflictingEffectWith(ISpellHandler compare) => false;
        /// <summary>What everyone inside wears while the storm holds them (shown as an effect icon).</summary>
        public IReadOnlyList<MLMod> Mods => HeldProperties.Select(p => Friendly ? MLMod.Buff(p, HeldValue) : MLMod.Debuff(p, HeldValue)).ToList();
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in i) => new MasterLevelEffect(i));

        /// <summary>The client effect played on each living a pulse touches.</summary>
        public abstract ushort PulseEffect { get; }
        /// <summary>Friendly storms work on the caster's realm, the rest on enemies.</summary>
        public virtual bool Friendly => false;
        /// <summary>Bonuses held while inside: allies get buff categories, enemies debuffs.</summary>
        public virtual IReadOnlyList<eProperty> HeldProperties => Array.Empty<eProperty>();
        public virtual int HeldValue => (int)Spell.Value;
        /// <summary>One-off work on each pulse (damage, drains).</summary>
        public virtual void Pulse(StormlordStorm storm, GameLiving target) { }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (GetDestination(out Point3D at))
                StormlordStorm.Place(Caster, this, at);
        }
    }

    [SpellHandler(eSpellType.DazzlingArray)]
    public class DazzlingArraySpellHandler : StormlordStormSpell
    {
        private static readonly eProperty[] Held = { eProperty.MissHit };
        public DazzlingArraySpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7210;
        public override bool Friendly => true;
        public override IReadOnlyList<eProperty> HeldProperties => Held;
        public override string ShortDescription => $"Ground-targeted storm: you, your group and pets inside are {Spell.Value}% harder to hit.";
    }

    [SpellHandler(eSpellType.EnervatingGas)]
    public class EnervatingGasSpellHandler : StormlordStormSpell
    {
        public EnervatingGasSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7273;
        public override string ShortDescription => $"Ground-targeted storm: each pulse drains {Spell.Value}% endurance from enemies inside; you gain half of it.";
        public override void Pulse(StormlordStorm storm, GameLiving target) =>
            StormlordStorm.Drain(storm.StormOwner, target, target.MaxEndurance, Spell.Value, power: false);
    }

    [SpellHandler(eSpellType.InebriatingFumes)]
    public class InebriatingFumesSpellHandler : StormlordStormSpell
    {
        private static readonly eProperty[] Held = { eProperty.Dexterity, eProperty.Quickness };
        public InebriatingFumesSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7258;
        public override IReadOnlyList<eProperty> HeldProperties => Held;
        public override string ShortDescription => $"Ground-targeted storm: enemies inside lose {Spell.Value} dexterity and quickness until they leave.";
    }

    [SpellHandler(eSpellType.MentalSiphon)]
    public class MentalSiphonSpellHandler : StormlordStormSpell
    {
        public MentalSiphonSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7303;
        public override string ShortDescription => $"Ground-targeted storm: each pulse drains {Spell.Value}% power from enemies inside; you gain half of it. Monsters' power is bottomless, so against them it only feeds you.";
        public override void Pulse(StormlordStorm storm, GameLiving target) =>
            StormlordStorm.Drain(storm.StormOwner, target, target.MaxMana, Spell.Value, power: true);
    }

    [SpellHandler(eSpellType.ChokingVapors)]
    public class ChokingVaporsSpellHandler : StormlordStormSpell
    {
        private static readonly eProperty[] Held = { eProperty.Strength, eProperty.Constitution };
        public ChokingVaporsSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7223;
        public override IReadOnlyList<eProperty> HeldProperties => Held;
        public override string ShortDescription => $"Ground-targeted storm: enemies inside lose {Spell.Value} strength and constitution until they leave.";
    }

    [SpellHandler(eSpellType.SenseDullingCloud)]
    public class SenseDullingCloudSpellHandler : StormlordStormSpell
    {
        private static readonly eProperty[] Held = { eProperty.Intelligence, eProperty.Piety, eProperty.Empathy, eProperty.Charisma };
        public SenseDullingCloudSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7305;
        public override IReadOnlyList<eProperty> HeldProperties => Held;
        public override string ShortDescription => $"Ground-targeted storm: enemies inside lose {Spell.Value} intelligence, piety, empathy and charisma until they leave.";
    }

    [SpellHandler(eSpellType.EnergyTempest)]
    public class EnergyTempestSpellHandler : StormlordStormSpell
    {
        private Spell _strike;
        public EnergyTempestSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override ushort PulseEffect => 7216;
        public override string ShortDescription => $"Ground-targeted storm: each pulse deals {Spell.Damage} energy damage to every enemy inside.";

        public override void Pulse(StormlordStorm storm, GameLiving target)
        {
            _strike ??= new Spell(new DbSpell
            {
                SpellID = Spell.ID, Name = Spell.Name, Type = eSpellType.DirectDamage.ToString(), Target = "Enemy",
                Damage = Spell.Damage, DamageType = (int)eDamageType.Energy, ClientEffect = 0, Icon = Spell.Icon
            }, 50);
            ScriptMgr.CreateSpellHandler(storm.StormOwner, _strike, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells))?.StartSpell(target);
        }
    }

    [SpellHandler(eSpellType.VacuumVortex)]
    public class VacuumVortexSpellHandler : StormlordGroundSpell
    {
        public VacuumVortexSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override string ShortDescription => "Gather all of your storms in this region at your ground target.";

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (!GetDestination(out Point3D at))
                return;
            int moved = StormlordStorm.OwnedBy(Caster).Count(storm => storm.MoveTo(at));
            MessageToCaster($"Your winds gather {moved} storm{(moved == 1 ? "" : "s")}.", eChatType.CT_Spell);
        }
    }

    [SpellHandler(eSpellType.FocusingWinds)]
    public class FocusingWindsSpellHandler : StormlordSpell
    {
        public FocusingWindsSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override string ShortDescription => $"For {Spell.Duration / 1000} seconds your storms pulse twice as often.";

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            var storms = StormlordStorm.OwnedBy(Caster);
            foreach (StormlordStorm storm in storms)
                storm.FocusedUntil = GameLoop.GameLoopTime + Spell.Duration;
            MessageToCaster(storms.Count == 0 ? "You have no storms to focus." : "Your storms quicken.", eChatType.CT_Spell);
        }
    }

    [SpellHandler(eSpellType.ArcingPower)]
    public class ArcingPowerSpellHandler : StormlordSpell
    {
        public ArcingPowerSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override string ShortDescription => $"Your next single-target damage spell within {Spell.Duration / 1000} seconds also strikes enemies within {Spell.Radius} units of its target for {Spell.Value}% of its damage.";
        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams) =>
            ECSGameEffectFactory.Create(initParams, static (in i) => new ArcingPowerEffect(i));
    }

    public class ArcingPowerEffect : ECSGameSpellEffect
    {
        public ArcingPowerEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }

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

        public static bool Arcs(Spell spell) => spell != null && spell.Damage > 0 && spell.Target == eSpellTarget.ENEMY &&
            spell.Radius == 0 && spell.SpellType != eSpellType.ArcingPower;

        private void OnCastFinished(DOLEvent e, object sender, EventArgs args)
        {
            if (args is not CastingEventArgs { SpellHandler.Spell: Spell spell, Target: GameLiving target } || !Arcs(spell) || !target.IsAlive)
                return;
            Spell arc = new(new DbSpell
            {
                SpellID = SpellHandler.Spell.ID, Name = SpellHandler.Spell.Name, Type = eSpellType.DirectDamage.ToString(), Target = "Enemy",
                Damage = spell.Damage * SpellHandler.Spell.Value / 100, DamageType = (int)spell.DamageType, ClientEffect = spell.ClientEffect, Icon = spell.Icon
            }, spell.Level);
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            foreach (GameLiving other in StormlordStorm.Nearby(target, (ushort)SpellHandler.Spell.Radius))
            {
                if (other != target && other != Owner && other.IsAlive && GameServer.ServerRules.IsAllowedToAttack(Owner, other, true))
                    ScriptMgr.CreateSpellHandler(Owner, arc, line)?.StartSpell(other);
            }
            End();
        }
    }
}

namespace DOL.GS
{
    using DOL.GS.Spells;

    public class StormlordStorm : GameStorm
    {
        private static readonly Dictionary<GameLiving, List<StormlordStorm>> _byOwner = new();
        private static readonly Lock _registryLock = new();
        private ECSGameTimer _timer;

        public GameLiving StormOwner { get; }
        public StormlordStormSpell Handler { get; }
        public long ExpireTick { get; set; }
        public long FocusedUntil { get; set; }
        public ushort StormRadius { get; }

        public StormlordStorm(GameLiving owner, StormlordStormSpell handler)
        {
            StormOwner = owner;
            Handler = handler;
            Owner = owner as GamePlayer;
            Name = handler.Spell.Name;
            Realm = owner.Realm;   // model 3457 draws its storm by realm; None drew the Hibernian green for every caster
            Flags |= eFlags.PEACE;
            StormRadius = (ushort)Math.Max(1, handler.Spell.Radius);
        }

        public static IReadOnlyList<StormlordStorm> OwnedBy(GameLiving owner)
        {
            lock (_registryLock)
                return _byOwner.TryGetValue(owner, out var storms) ? storms.ToList() : new List<StormlordStorm>();
        }

        public static StormlordStorm Place(GameLiving owner, StormlordStormSpell handler, Point3D at)
        {
            foreach (StormlordStorm old in OwnedBy(owner).Where(storm => storm.Handler.Spell.SpellType == handler.Spell.SpellType))
                old.Remove();

            var storm = new StormlordStorm(owner, handler)
            {
                X = at.X, Y = at.Y, Z = at.Z, CurrentRegionID = owner.CurrentRegionID, Heading = owner.Heading,
                ExpireTick = GameLoop.GameLoopTime + handler.Spell.Duration
            };
            if (!storm.AddToWorld())
                return null;
            lock (_registryLock)
            {
                if (!_byOwner.TryGetValue(owner, out var storms))
                    _byOwner[owner] = storms = new List<StormlordStorm>();
                storms.Add(storm);
            }
            storm._timer = new ECSGameTimer(storm, storm.Tick, storm.Interval);
            return storm;
        }

        public int Interval
        {
            get
            {
                int frequency = Handler.Spell.Frequency > 0 ? Handler.Spell.Frequency : 4000;
                return GameLoop.GameLoopTime < FocusedUntil ? Math.Max(500, frequency / 2) : frequency;
            }
        }

        public bool MoveTo(Point3D at)
        {
            if (ObjectState != eObjectState.Active || CurrentRegionID != StormOwner.CurrentRegionID)
                return false;
            return MoveInRegion(CurrentRegionID, at.X, at.Y, at.Z, Heading, true);
        }

        public static IEnumerable<GameLiving> Nearby(GameObject center, ushort radius) =>
            center.GetPlayersInRadius(radius).Cast<GameLiving>().Concat(center.GetNPCsInRadius(radius))
                .Where(living => living is not GameStorm && living.IsAlive && living.ObjectState == eObjectState.Active);

        public bool Affects(GameLiving living) => Handler.Friendly
            ? living.Realm == StormOwner.Realm && (living == StormOwner || living is GamePlayer || living is GameNPC { Brain: IControlledBrain }) &&
              !GameServer.ServerRules.IsAllowedToAttack(StormOwner, living, true)
            : living != StormOwner && GameServer.ServerRules.IsAllowedToAttack(StormOwner, living, true);

        private int Tick(ECSGameTimer timer)
        {
            if (ObjectState != eObjectState.Active || !StormOwner.IsAlive || StormOwner.ObjectState != eObjectState.Active ||
                StormOwner.CurrentRegionID != CurrentRegionID || GameLoop.GameLoopTime >= ExpireTick)
            {
                Remove();
                return 0;
            }
            Pulse(Nearby(this, StormRadius).Where(Affects).ToList());
            return Interval;
        }

        /// <summary>
        /// One pulse over the livings currently inside; exposed for tests. Storms with bonuses give everyone
        /// inside the storm's effect for a little longer than one pulse, so it fades soon after they leave.
        /// </summary>
        public void Pulse(IReadOnlyCollection<GameLiving> inside)
        {
            foreach (GameLiving living in inside)
            {
                if (Handler.HeldProperties.Count > 0)
                    MasterLevelHold.Refresh(Handler, living, Interval + 2000);
                Handler.Pulse(this, living);
                if (!Handler.Friendly)
                    Provoke(living);
                foreach (GamePlayer viewer in living.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    viewer.Out.SendSpellEffectAnimation(this, living, Handler.PulseEffect, 0, false, 1);
            }
        }

        private void Provoke(GameLiving living)
        {
            if (living is GameNPC { Brain: StandardMobBrain brain } && StormOwner.IsAlive)
                brain.AddToAggroList(StormOwner, 1);
        }

        public static void Drain(GameLiving owner, GameLiving target, int maximum, double percent, bool power)
        {
            int amount = (int)Math.Min(power ? target.Mana : target.Endurance, maximum * percent / 100);
            if (amount <= 0)
                return;
            if (power)
            {
                target.ChangeMana(owner, eManaChangeType.Spell, -amount);
                owner.ChangeMana(owner, eManaChangeType.Spell, amount / 2);
            }
            else
            {
                target.ChangeEndurance(owner, eEnduranceChangeType.Spell, -amount);
                owner.ChangeEndurance(owner, eEnduranceChangeType.Spell, amount / 2);
            }
        }

        public void Remove()
        {
            _timer?.Stop();
            lock (_registryLock)
            {
                if (_byOwner.TryGetValue(StormOwner, out var storms))
                {
                    storms.Remove(this);
                    if (storms.Count == 0)
                        _byOwner.Remove(StormOwner);
                }
            }
            if (ObjectState == eObjectState.Active)
                Delete();
        }

        public override void Die(GameObject killer) => Remove();
    }
}
