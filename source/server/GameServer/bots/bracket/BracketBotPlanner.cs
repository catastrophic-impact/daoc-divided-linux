using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>The pure counting rules of bracket bots, kept apart from the world so they can be tested.</summary>
    public static class BracketBotPlanner
    {
        public static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };

        /// <summary>No realm gets more than this many bots of one class in a battleground, so a wave is not five of the same class.</summary>
        public const int MaxPerClass = 2;

        /// <summary>Bots are made one per realm per tick until a realm has the wanted number (counting bots still waiting to log in).</summary>
        public static bool NeedsAnother(int wanted, int existing) => existing < wanted;

        public static bool ClassAllowed(int sameClassAlready) => sameClassAlready < MaxPerClass;

        /// <summary>
        /// How many bracket bots each realm should have online in a battleground.
        /// present = players and ordinary bots of that realm already inside (bracket bots not counted).
        /// fill: top each realm up to perRealm in total; with keepEven also up toward the biggest realm.
        /// extra: always perRealm bracket bots per realm.
        /// </summary>
        public static Dictionary<eRealm, int> Plan(IReadOnlyDictionary<eRealm, int> present, int perRealm, bool fillIn, bool keepEven)
        {
            perRealm = Math.Max(BracketBotProperties.MinimumPerRealm, perRealm);
            int Present(eRealm realm) => present.TryGetValue(realm, out int count) ? Math.Max(0, count) : 0;
            var target = new Dictionary<eRealm, int>();
            if (!fillIn)
            {
                foreach (eRealm realm in Realms)
                    target[realm] = perRealm;
                return target;
            }

            int total = perRealm;
            if (keepEven)
                total = Math.Max(total, Realms.Max(Present));
            foreach (eRealm realm in Realms)
                target[realm] = Math.Max(0, total - Present(realm));
            return target;
        }
    }
}
