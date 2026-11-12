using System;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Storm Sovereign Tempest charges, shown to everyone nearby as the classic Warlock chamber orbs over the head
    /// (VisualEffect sub-code 3, five slots: one lit orb per charge). Nothing in the client is changed.
    /// Charges are built by weapon styles, blocks and parries, and spent by finishers and two spells.
    /// </summary>
    public static class StormTempest
    {
        public const int MaxCharges = 5;
        public const int DecayMs = 30000;
        public const int RefreshMs = 2000;
        public const int CycleMs = 5000;
        public const double BonusPerCharge = 0.20;
        private const string StateKey = "StormTempest.State";
        private static readonly DOL.Logging.Logger _log = DOL.Logging.LoggerManager.Create(typeof(StormTempest));

        // The chamber colour codes the stock Warlock packet used (primary only, then one per secondary spell type).
        public static readonly byte[] StockCodes = { 0x03, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 };

        // Lit-orb colour: 0x77, a blue lightning ring around gold runes (owner's pick in game, 2026-11-04).
        public static byte OrbCode = 0x77;

        private sealed class State
        {
            public int Charges;
            public long LastGain;
            public byte? Preview;
            public int CycleIndex = -1;
            public long NextCycle;
            public ECSGameTimer Timer;
        }

        public static int Charges(GameLiving living) => Get(living)?.Charges ?? 0;

        /// <summary>Damage multiplier for a finisher spending <paramref name="charges"/>: +20% each.</summary>
        public static double Bonus(int charges) => 1 + BonusPerCharge * Math.Clamp(charges, 0, MaxCharges);

        /// <summary>Adds charges (capped at five) and restarts the decay timer.</summary>
        public static void Add(GameLiving living, int amount = 1)
        {
            if (living == null || amount <= 0 || !living.IsAlive)
                return;
            var state = GetOrCreate(living);
            state.Charges = Math.Min(MaxCharges, state.Charges + amount);
            state.LastGain = GameLoop.GameLoopTime;
            Trace(living, $"+{amount}", state.Charges);
            Refresh(living, state);
        }

        /// <summary>
        /// Spends every charge and returns how many there were. During Storm Crown finishers cost nothing:
        /// the charges stay lit and the full count is returned. Eye of Tempest refunds part of the cost.
        /// </summary>
        public static int Spend(GameLiving living)
        {
            var state = Get(living);
            if (state == null || state.Charges == 0)
                return 0;
            int spent = state.Charges;
            if (StormforgedHeroicRules.HasAvatar(living))
                return spent;
            state.Charges = Math.Min(spent, Refund(living));
            Trace(living, $"spent {spent}", state.Charges);
            Refresh(living, state);
            return spent;
        }

        /// <summary>Spends up to <paramref name="amount"/> charges (Winter's Grasp, Hammerfall) and returns how many; free in Storm Crown.</summary>
        public static int Take(GameLiving living, int amount)
        {
            var state = Get(living);
            if (state == null || state.Charges == 0 || amount <= 0)
                return 0;
            int taken = Math.Min(amount, state.Charges);
            if (StormforgedHeroicRules.HasAvatar(living))
                return taken;
            state.Charges -= taken;
            Trace(living, $"spent {taken}", state.Charges);
            Refresh(living, state);
            return taken;
        }

        public static void Set(GameLiving living, int charges)
        {
            if (living == null)
                return;
            var state = GetOrCreate(living);
            state.Charges = Math.Clamp(charges, 0, MaxCharges);
            state.LastGain = GameLoop.GameLoopTime;
            Refresh(living, state);
        }

        /// <summary>Lights all five orbs in one colour code until cleared (GM colour test).</summary>
        public static void Preview(GamePlayer player, byte code)
        {
            var state = GetOrCreate(player);
            state.CycleIndex = -1;
            state.Preview = code;
            Refresh(player, state);
        }

        /// <summary>Shows each stock colour code for five seconds in turn, naming it in chat.</summary>
        public static void Cycle(GamePlayer player)
        {
            var state = GetOrCreate(player);
            state.CycleIndex = 0;
            ShowCycleStep(player, state);
            Refresh(player, state);
        }

        public static void Clear(GameLiving living)
        {
            var state = Get(living);
            if (state == null)
                return;
            state.Charges = 0;
            state.Preview = null;
            state.CycleIndex = -1;
            Refresh(living, state);
        }

        /// <summary>Forgets the charges and stops the orb timer (tests, and a living leaving the world).</summary>
        public static void Reset(GameLiving living)
        {
            var state = Get(living);
            if (state == null)
                return;
            state.Timer?.Stop();
            living.TempProperties.RemoveProperty(StateKey);
        }

        /// <summary>The five slot bytes: lit orbs first, the rest empty.</summary>
        public static byte[] Slots(int charges, byte code)
        {
            var slots = new byte[MaxCharges];
            for (int i = 0; i < Math.Clamp(charges, 0, MaxCharges); i++)
                slots[i] = code;
            return slots;
        }

        public static bool TryParseCode(string text, out byte code)
        {
            code = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out code);
            return byte.TryParse(text, out code);
        }

        // Players only: one log line per change, to check the orbs against the real count in game.
        private static void Trace(GameLiving living, string change, int charges)
        {
            if (living is GamePlayer && _log.IsInfoEnabled)
                _log.Info($"TEMPEST {living.Name} {change} -> {charges}/{MaxCharges}");
        }

        // Eye of Tempest (RA): finishers refund 1/1/2 charges and charges last 30/45/60 seconds.
        public static int Refund(GameLiving living) => EyeOfTempestLevel(living) switch { >= 3 => 2, >= 1 => 1, _ => 0 };
        public static int DecayFor(GameLiving living) => DecayMs + 15000 * Math.Clamp(EyeOfTempestLevel(living), 0, 2);
        private static int EyeOfTempestLevel(GameLiving living) =>
            living is GamePlayer player ? player.GetAbilityLevel(RealmAbilities.SovereignEyeOfTempest.KeyName) : 0;

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

        private static void ShowCycleStep(GamePlayer player, State state)
        {
            byte code = StockCodes[state.CycleIndex];
            state.Preview = code;
            state.NextCycle = GameLoop.GameLoopTime + CycleMs;
            player.Out.SendMessage($"Tempest orb colour {state.CycleIndex + 1}/{StockCodes.Length}: 0x{code:X2}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static void Refresh(GameLiving living, State state)
        {
            Send(living, state);
            bool showing = state.Charges > 0 || state.Preview != null;
            if (showing && state.Timer?.IsAlive != true)
                state.Timer = new ECSGameTimer(living, _ => Tick(living, state), RefreshMs);
        }

        private static int Tick(GameLiving living, State state)
        {
            bool gone = living.ObjectState != GameObject.eObjectState.Active || !living.IsAlive ||
                living is GamePlayer { Client.ClientState: not GameClient.eClientState.Playing };
            if (gone)
            {
                state.Charges = 0;
                state.Preview = null;
                state.CycleIndex = -1;
                Send(living, state);
                return 0;
            }
            if (state.CycleIndex >= 0 && living is GamePlayer player && GameLoop.GameLoopTime >= state.NextCycle)
            {
                if (++state.CycleIndex >= StockCodes.Length)
                {
                    state.CycleIndex = -1;
                    state.Preview = null;
                    player.Out.SendMessage("Tempest colour cycle finished. /tempest show <code> to look at one again.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
                else
                    ShowCycleStep(player, state);
            }
            if (state.Preview == null && state.Charges > 0 && GameLoop.GameLoopTime - state.LastGain >= DecayFor(living))
                state.Charges = 0;
            // Re-send while lit: a player who walks into view needs the orbs too (Warlocks re-sent on every move).
            Send(living, state);
            return state.Charges > 0 || state.Preview != null ? RefreshMs : 0;
        }

        private static void Send(GameLiving living, State state)
        {
            if (living.CurrentRegion == null)
                return;
            byte[] slots = state.Preview is byte code ? Slots(MaxCharges, code) : Slots(state.Charges, OrbCode);
            foreach (GamePlayer viewer in living.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                (viewer.Out as PacketLib168)?.SendChamberOrbs(living, slots);
        }
    }
}
