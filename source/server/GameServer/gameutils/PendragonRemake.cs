using System;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Pendragon Sovereign (class 65) Valor: earned by defending (blocks, parries, melee hits taken, guarded blows and the
    /// shield and reaction styles), spent by Judgments (+20% damage per point, a party heal or ward, a 2-second stun at 5).
    /// Shown as a gold buff icon named "Valor n" (spell rows 65809-65815) and a holy flash on the Pendragon at 3 and at 5
    /// (owner's choice, 2026-11-06). Code of Chivalry (RA) raises the cap to 6/6/7 and slows the decay to one point per
    /// 8/12/16 seconds out of combat. See docs/PENDRAGON-SOVEREIGN-REMAKE.md.
    /// </summary>
    public static class PendragonValor
    {
        public const int BaseMax = 5;
        public const double BonusPerPoint = 0.20;
        public const int HitGainCooldownMs = 3000;
        public const int DecayStepMs = 8000;
        public const int TickMs = 2000;
        // Albion holy effects: Aura of Guarding at 3, Holy Barrier at 5.
        public const ushort FlashAtThree = 1465, FlashAtFive = 1506;
        private const string StateKey = "PendragonValor.State";
        private static readonly DOL.Logging.Logger _log = DOL.Logging.LoggerManager.Create(typeof(PendragonValor));

        private sealed class State
        {
            public int Valor;
            public int Shown;
            public long LastChange;
            public long LastHitGain = long.MinValue / 2;
            public ECSGameTimer Timer;
            public readonly object Lock = new();
        }

        public static int Count(GameLiving living) => Get(living)?.Valor ?? 0;

        public const int AbsoluteMax = 7;

        /// <summary>The cap: 5, raised to 6/6/7 by Code of Chivalry.</summary>
        public static int Max(GameLiving living) => BaseMax + Chivalry(living) switch { >= 3 => 2, >= 1 => 1, _ => 0 };

        /// <summary>Out of combat, one point fades every 8 seconds (12/16 with Code of Chivalry rank 2/3).</summary>
        public static int DecayFor(GameLiving living) => DecayStepMs + 4000 * Math.Clamp(Chivalry(living) - 1, 0, 2);

        private static int Chivalry(GameLiving living) => Math.Clamp(RealmAbilityLevel(living, RealmAbilities.PendragonCodeOfChivalry.KeyName), 0, 3);

        /// <summary>Damage multiplier for a Judgment spending <paramref name="valor"/>: +20% each.</summary>
        public static double Bonus(int valor) => 1 + BonusPerPoint * Math.Max(0, valor);

        /// <summary>What held Valor adds to every other Pendragon spell (owner 2026-11-09: all its spells should use Valor).</summary>
        public const double HeldBonusPerPoint = 0.10;

        /// <summary>
        /// Multiplier for the spells that keep Valor instead of spending it: +10% per point held (Crownfire Lance, Guard of
        /// the Crown, Rally, Consecrated Armament, Decree, Binding Radiance, Field of Judgement, King's Advance).
        /// </summary>
        public static double Held(GameLiving living) => 1 + HeldBonusPerPoint * Count(living);

        public static void Add(GameLiving living, int amount = 1)
        {
            if (living == null || amount <= 0 || !living.IsAlive)
                return;
            var state = GetOrCreate(living);
            int now;
            lock (state.Lock)
            {
                state.Valor = Math.Min(Max(living), state.Valor + amount);
                state.LastChange = GameLoop.GameLoopTime;
                now = state.Valor;
            }
            Trace(living, $"+{amount}", now);
            Refresh(living, state);
        }

        /// <summary>A melee hit taken earns 1 Valor, at most once every three seconds.</summary>
        public static void AddFromHit(GameLiving living)
        {
            var state = GetOrCreate(living);
            lock (state.Lock)
            {
                if (GameLoop.GameLoopTime - state.LastHitGain < HitGainCooldownMs)
                    return;
                state.LastHitGain = GameLoop.GameLoopTime;
            }
            Add(living, 1);
        }

        /// <summary>
        /// Spends all Valor and returns how much there was. In the Lion of Albion form Judgments spend nothing (the
        /// form fills Valor), as the Storm Crown keeps Tempest.
        /// </summary>
        public static int Spend(GameLiving living)
        {
            var state = Get(living);
            if (state == null)
                return 0;
            int spent;
            lock (state.Lock)
            {
                spent = state.Valor;
                if (spent == 0 || StormforgedHeroicRules.HasAvatar(living))
                    return spent;
                state.Valor = 0;
                state.LastChange = GameLoop.GameLoopTime;
            }
            Trace(living, $"spent {spent}", 0);
            Refresh(living, state);
            return spent;
        }

        public static void Set(GameLiving living, int valor)
        {
            if (living == null)
                return;
            var state = GetOrCreate(living);
            lock (state.Lock)
            {
                state.Valor = Math.Clamp(valor, 0, Max(living));
                state.LastChange = GameLoop.GameLoopTime;
            }
            Refresh(living, state);
        }

        /// <summary>Forgets the Valor (tests, and a living leaving the world).</summary>
        public static void Reset(GameLiving living)
        {
            var state = Get(living);
            if (state == null)
                return;
            state.Timer?.Stop();
            living.TempProperties.RemoveProperty(StateKey);
        }

        /// <summary>The party heal (sword and shield) or ward (polearm and shield guard Judgments) a Judgment of <paramref name="valor"/> brings.</summary>
        public static void OnJudgment(MasterLevelAbilityHandler h, int valor, bool ward)
        {
            if (valor <= 0)
                return;
            double grail = 1 + 0.25 * Math.Clamp(RealmAbilityLevel(h.Caster, RealmAbilities.PendragonGrailBlessing.KeyName), 0, 3);
            foreach (GameLiving member in h.Group(1500))
            {
                int amount = (int)(member.MaxHealth * 3 * valor / 100.0 * grail);
                if (ward)
                    PendragonRemakeStyles.Ward(h, member, amount);
                else
                    h.Heal(member, amount);
            }
        }

        private static int RealmAbilityLevel(GameLiving living, string key) => living is GamePlayer player ? player.GetAbilityLevel(key) : 0;

        private static void Trace(GameLiving living, string change, int valor)
        {
            if (living is GamePlayer && _log.IsInfoEnabled)
                _log.Info($"VALOR {living.Name} {change} -> {valor}/{Max(living)}");
        }

        private static State Get(GameLiving living) => living?.TempProperties.GetProperty<State>(StateKey);

        private static State GetOrCreate(GameLiving living)
        {
            var state = Get(living);
            if (state == null)
            {
                state = new State();
                living.TempProperties.SetProperty(StateKey, state);
            }
            return state;
        }

        private static void Refresh(GameLiving living, State state)
        {
            int valor = state.Valor;
            if (valor != state.Shown)
            {
                if (valor >= 5 && state.Shown < 5)
                    Flash(living, FlashAtFive);
                else if (valor >= 3 && state.Shown < 3)
                    Flash(living, FlashAtThree);
                ShowIcon(living, valor);
                state.Shown = valor;
            }
            if (valor > 0 && state.Timer?.IsAlive != true && living.CurrentRegion != null)
                state.Timer = new ECSGameTimer(living, _ => Tick(living, state), TickMs);
        }

        private static int Tick(GameLiving living, State state)
        {
            bool gone = living.ObjectState != GameObject.eObjectState.Active || !living.IsAlive;
            lock (state.Lock)
            {
                if (gone)
                    state.Valor = 0;
                else if (!living.InCombat && state.Valor > 0 && GameLoop.GameLoopTime - state.LastChange >= DecayFor(living))
                {
                    state.Valor--;
                    state.LastChange = GameLoop.GameLoopTime;
                }
            }
            Refresh(living, state);
            return state.Valor > 0 ? TickMs : 0;
        }

        private static void Flash(GameLiving living, ushort effect)
        {
            if (living.CurrentRegion == null)
                return;
            foreach (GamePlayer viewer in living.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                viewer.Out.SendSpellEffectAnimation(living, living, effect, 0, false, 1);
        }

        /// <summary>The buff-bar icon: one effect per count (spell rows 65809-65815, "Valor 1" to "Valor 7").</summary>
        private static void ShowIcon(GameLiving living, int valor)
        {
            if (living.effectListComponent == null)
                return;
            foreach (var effect in living.effectListComponent.GetSpellEffects(eEffect.MasterLevel)
                         .Where(e => !e.IsEnding && PendragonRemakeSpells.IsValorIcon(e.SpellHandler?.Spell?.ID ?? 0)).ToList())
                effect.End();
            if (valor <= 0 || living.CurrentRegion == null)
                return;
            Spell icon = SkillBase.GetSpellByID(PendragonRemakeSpells.ValorIconId(valor));
            SpellLine line = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
            if (icon == null || line == null)
                return;
            new StormSovereignHandler(living, icon, line).Hold(living, 3600000);
        }
    }

    /// <summary>The Regalia of Camelot: the Pendragon Sovereign's armor set (tools/sovereign/camelot_regalia_edits.py).</summary>
    public static class CamelotRegalia
    {
        public const string Prefix = "camelot_regalia_";
        public const string GrantKeyword = "Regalia of Camelot";

        public static RegaliaSet Set { get; } = new("Regalia of Camelot", Prefix, GrantKeyword, PendragonRules.IsPendragon,
            "Wear the crown of Camelot, sovereign. All seven pieces together hold the Pendragon's full majesty.", new (int, string, MLMod[])[]
            {
                (2, "+5% to all resistances", MLMod.Resists(MLCategory.Ability, 5, physical: true)),
                (4, "+10% melee and style damage", new[] { new MLMod(eProperty.MeleeDamage, MLCategory.Ability, 10), new MLMod(eProperty.StyleDamage, MLCategory.Ability, 10) }),
                (6, "+10% block and parry", new[] { new MLMod(eProperty.BlockChance, MLCategory.Ability, 10), new MLMod(eProperty.ParryChance, MLCategory.Ability, 10) }),
                (7, "+10% maximum health, and every block or parry earns 2 Valor", new[] { new MLMod(eProperty.Of_Toughness, MLCategory.Ability, 10) }),
            });

        public static bool IsPiece(DOL.Database.DbInventoryItem item) => Set.IsPiece(item);

        /// <summary>Valor a block or parry earns: 1, or 2 with the full set.</summary>
        public static int DefenseValor(GamePlayer player) => player != null && Set.HasFullSet(player) ? 2 : 1;
    }
}
