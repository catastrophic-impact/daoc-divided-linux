using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Settings for battleground bracket bots (daoc-divided-linux addition, not part of DividedRealms): roster bots
    /// that stay in a level bracket and are logged in only while a player is inside that battleground.
    /// </summary>
    public static class BracketBotProperties
    {
        public const int MinimumPerRealm = 8; // a battleground wave needs 8 bots of one realm (AutonomousBattlegroundWave.MinimumWave)

        [ServerProperty("bracket_bots", "bracket_bots_enabled", "Bring in bracket bots while a player is inside a battleground, and log them out after the last player leaves. Off by default: switch it on to fill the battlegrounds (the roster is created automatically the first time a player enters one).", false)]
        public static bool ENABLED;

        [ServerProperty("bracket_bots", "bracket_bots_auto_create", "When a player is inside a battleground and its roster has fewer bracket bots than the per-realm number, create the missing ones (a new server starts with none). Off = only 'daoc-admin bracket create' makes them.", true)]
        public static bool AUTO_CREATE;

        [ServerProperty("bracket_bots", "bracket_bots_per_realm", "Bots per realm in a battleground while a player is inside (minimum 8, the size of a battleground wave). Always per realm.", 12)]
        public static int PER_REALM;

        [ServerProperty("bracket_bots", "bracket_bots_fill_mode", "fill = top each realm up to the per-realm number, counting players and ordinary bots already there. extra = always bring in the full per-realm number of bracket bots.", "fill")]
        public static string FILL_MODE;

        [ServerProperty("bracket_bots", "bracket_bots_keep_even", "Keep the realms even: top every realm up toward the biggest realm in the battleground (fill mode).", true)]
        public static bool KEEP_EVEN;

        [ServerProperty("bracket_bots", "bracket_bots_grace_seconds", "How long bracket bots stay after the last player leaves a battleground before they log out.", 120)]
        public static int GRACE_SECONDS;

        [ServerProperty("bracket_bots", "bracket_bots_debug_presence_region", "Testing only: pretend an Albion player is inside this battleground region (250 to 253). 0 = off.", 0)]
        public static int DEBUG_PRESENCE_REGION;

        /// <summary>The per-realm number in use: never below the wave minimum.</summary>
        public static int EffectivePerRealm => System.Math.Max(MinimumPerRealm, PER_REALM);

        public static bool FillIn => !string.Equals(FILL_MODE, "extra", System.StringComparison.OrdinalIgnoreCase);
    }
}
