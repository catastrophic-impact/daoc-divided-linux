namespace DOL.GS
{
    /// <summary>
    /// The royal classes (Storm, Pendragon and Tuatha Sovereign) learn their spells from their careers, not from
    /// spec lines, so a plan only trains weapons, Shields and Parry. Stealth is left untrained so a Sovereign bot
    /// fights in the open as a tank.
    /// </summary>
    public class SovereignBotSpec : BotSpec
    {
        public SovereignBotSpec(eCharacterClass characterClass, eSpecType spec)
        {
            SpecName = "SovereignBotSpec";

            eObjectType[] oneHanded = characterClass switch
            {
                eCharacterClass.PendragonSovereign => [eObjectType.SlashingWeapon],
                eCharacterClass.TuathaSovereign => [eObjectType.Blades],
                _ => [eObjectType.Hammer, eObjectType.Axe],
            };
            eObjectType[] twoHanded = characterClass switch
            {
                eCharacterClass.PendragonSovereign => [eObjectType.PolearmWeapon],
                eCharacterClass.TuathaSovereign => [eObjectType.Scythe],
                _ => [],
            };
            // Storm Sovereign's two-handed set-up is a two-handed hammer or axe, trained in the same line.
            bool storm = twoHanded.Length == 0;

            WeaponOneType = oneHanded[Util.Random(oneHanded.Length - 1)];

            bool twoHand = spec switch
            {
                eSpecType.TwoHanded => true,
                eSpecType.OneHandAndShield => false,
                _ => Util.Random(1) == 0,
            };

            if (twoHand)
            {
                SpecType = eSpecType.TwoHanded;
                Is2H = true;
                if (storm)
                {
                    // A two-handed item of the same type: the bot equips WeaponTwoType in both hands.
                    WeaponTwoType = WeaponOneType;
                    Add(ObjToSpec(WeaponOneType), 50, 1.0f);
                }
                else
                {
                    WeaponTwoType = twoHanded[Util.Random(twoHanded.Length - 1)];
                    Add(ObjToSpec(WeaponTwoType), 50, 1.0f);
                    Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                }
                Add(Specs.Parry, 50, 0.6f);
                Add(Specs.Shields, 50, 0.3f);
            }
            else
            {
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 1.0f);
                Add(Specs.Shields, 50, 0.9f);
                Add(Specs.Parry, 50, 0.6f);
            }
        }
    }
}
