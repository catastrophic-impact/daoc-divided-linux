using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    // Witchcraft group healing-potency aura (1.123, values lowered by the 2018-03-28 hotfix): a pulsing group buff
    // that raises the healing done by every grouped ally.
    [SpellHandler(eSpellType.WarlockHealingAura)]
    public sealed class WarlockHealingAuraHandler(GameLiving caster, Spell spell, SpellLine line) : SingleStatBuff(caster, spell, line)
    {
        public override eProperty Property1 => eProperty.HealingEffectiveness;
        public override string ShortDescription => $"Increases the healing done by all grouped allies by {Spell.Value}%.";
        protected override double CalculateBuffDebuffEffectiveness() => 1;
    }

    // Hexing single-pet fear (1.122B, durations 1.123B, cast time 1.129): the pet flees for the duration and its
    // master loses control; a 60-second immunity follows once the fear has run its course.
    [SpellHandler(eSpellType.WarlockPetFear)]
    public sealed class WarlockPetFearHandler : SpellHandler
    {
        public const int ImmunityMs = 60000;
        public WarlockPetFearHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
        public override string ShortDescription => "Sends an enemy pet running in fear, causing its master to lose control. 60-second immunity afterwards.";
        // A gamebot's BotBrain is an IControlledBrain too, but a gamebot stands in for a player, not a pet (DR-199).
        public static bool IsEnemyPet(GameLiving caster, GameLiving target) => target is not GameBot &&
            target is GameNPC { Brain: IControlledBrain } && GameServer.ServerRules.IsAllowedToAttack(caster, target, true);
        public static bool IsImmune(GameLiving target) => target.effectListComponent.ContainsEffectForEffectType(eEffect.WarlockPetFearImmunity) ||
            target.effectListComponent.ContainsEffectForEffectType(eEffect.WarlockPetFear);
        public override bool CheckBeginCast(GameLiving target, bool quiet)
        {
            if (target is not GameLiving living || !IsEnemyPet(Caster, living))
            {
                if (!quiet) MessageToCaster("You can only cast this on an enemy pet.", eChatType.CT_SpellResisted);
                return false;
            }
            return base.CheckBeginCast(target, quiet);
        }
        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target is not GameNPC pet || !IsEnemyPet(Caster, pet)) return;
            if (IsImmune(pet))
            {
                MessageToCaster("Your target is immune to this effect!", eChatType.CT_SpellResisted);
                return;
            }
            var caster = Caster;
            ECSGameEffectFactory.Create(new(pet, Spell.Duration, 1), (in ECSGameEffectInitParams i) => new WarlockPetFearEffect(i, caster));
        }
    }

    public sealed class WarlockPetFearEffect : ECSGameAbilityEffect
    {
        private readonly GameLiving _caster;
        private readonly bool _grantsImmunity;
        // Area fears respect the immunity but do not grant it (1.124b).
        public WarlockPetFearEffect(in ECSGameEffectInitParams init, GameLiving caster, bool grantsImmunity = true) : base(init)
        {
            _caster = caster; _grantsImmunity = grantsImmunity; EffectType = eEffect.WarlockPetFear; PulseFreq = 1000; NextTick = StartTick;
        }
        public override string Name => "Fear";
        public override ushort Icon => 15197;
        public override bool HasPositiveEffect => false;
        public override void OnEffectPulse() => Flee(Owner as GameNPC, _caster);
        // A monster's own brain re-engaged between the once-a-second pulses, so Malefic Horror scared nothing
        // (playtest 2026-11-07). Like the stock fear spell, a monster runs on a fear brain for the duration; a pet keeps
        // its controlled brain (its master loses control through the pulse).
        private FleeBrain _fleeBrain;
        public override void OnStartEffect()
        {
            if (Owner is GameNPC npc && npc.Brain is not IControlledBrain && _caster != null)
            {
                _fleeBrain = new FleeBrain(_caster);
                npc.AddBrain(_fleeBrain);
                _fleeBrain.Think();
            }
        }
        /// <summary>Runs from one living (the caster) and never attacks, until removed.</summary>
        public sealed class FleeBrain(GameObject from) : StandardMobBrain
        {
            public GameObject From { get; } = from;
            public override int ThinkInterval => 1000;
            public override void Think() => Flee(Body, From);
        }
        public static void Flee(GameNPC pet, GameObject from)
        {
            if (pet == null || !pet.IsAlive || from == null) return;
            pet.attackComponent.StopAttack();
            pet.StopFollowing();
            double angle = System.Math.Atan2(pet.Y - from.Y, pet.X - from.X);
            pet.WalkTo(new Point3D(pet.X + (int)(System.Math.Cos(angle) * 600), pet.Y + (int)(System.Math.Sin(angle) * 600), pet.Z), pet.MaxSpeed);
        }
        public override void OnStopEffect()
        {
            if (_fleeBrain != null && Owner is GameNPC npc)
            {
                npc.RemoveBrain(_fleeBrain);
                _fleeBrain = null;
            }
            if (!Owner.IsAlive || !_grantsImmunity) return;
            ECSGameEffectFactory.Create(new(Owner, WarlockPetFearHandler.ImmunityMs, 1),
                static (in ECSGameEffectInitParams i) => new WarlockPetFearImmunityEffect(i));
        }
    }

    public sealed class WarlockPetFearImmunityEffect : ECSGameAbilityEffect
    {
        public WarlockPetFearImmunityEffect(in ECSGameEffectInitParams init) : base(init) { EffectType = eEffect.WarlockPetFearImmunity; }
        public override string Name => "Fear Immunity";
        public override ushort Icon => 15197;
        public override bool HasPositiveEffect => true;
    }

    // Cursing level 50 Spellbinder's Torrent (1.126) as changed by the 2020-02-12 live hotfix: 365 damage, no snare,
    // and it only hits enemy realm players, pets and realm guards. Ordinary monsters are never affected.
    [SpellHandler(eSpellType.WarlockTorrent)]
    public sealed class WarlockTorrentHandler(GameLiving caster, Spell spell, SpellLine line) : DirectDamageSpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Deals {Spell.Damage} damage to enemy realm players, pets and realm guards within {Spell.Radius}.";
        public static bool IsRealmTarget(GameLiving target) => target is GamePlayer or GameBot or GameKeepGuard ||
            target is GameNPC { Brain: IControlledBrain };
        public override List<GameLiving> SelectTargets(GameObject castTarget) => base.SelectTargets(castTarget).Where(IsRealmTarget).ToList();
    }

    // Bainshee level 50 Ethereal Shriek (1.121, 1.123): every enemy pet within the radius flees. Respects fear immunity
    // without granting it.
    [SpellHandler(eSpellType.PetScareArea)]
    public sealed class PetScareAreaHandler(GameLiving caster, Spell spell, SpellLine line) : SpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Sends every enemy pet within {Spell.Radius} running in fear for {Spell.Duration / 1000} seconds.";
        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target is not GameNPC pet || !WarlockPetFearHandler.IsEnemyPet(Caster, pet) || WarlockPetFearHandler.IsImmune(pet)) return;
            var caster = Caster;
            ECSGameEffectFactory.Create(new(pet, Spell.Duration, 1), (in ECSGameEffectInitParams i) => new WarlockPetFearEffect(i, caster, false));
        }
    }

    // Bainshee level 50 Resounding Aura (1.126 with the 2020-02-12 hotfix): the pulsing PBAoE only hits enemy realm
    // players, pets and realm guards.
    [SpellHandler(eSpellType.BainsheeRealmPulse)]
    public sealed class BainsheeRealmPulseHandler(GameLiving caster, Spell spell, SpellLine line) : BainsheePulseDmgSpellHandler(caster, spell, line)
    {
        public override string ShortDescription => $"Pulses {Spell.Damage} body damage every {Spell.Frequency / 1000.0:0.#} seconds to enemy realm players, pets and realm guards within {Spell.Radius}.";
        public override List<GameLiving> SelectTargets(GameObject castTarget) =>
            base.SelectTargets(castTarget).Where(WarlockTorrentHandler.IsRealmTarget).ToList();
    }
}
