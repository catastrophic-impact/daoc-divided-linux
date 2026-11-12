using DOL.GS.PacketHandler;

namespace DOL.GS.Trainer
{
    [NPCGuildScript("Storm Sovereign Trainer", eRealm.Midgard)]
    public class StormforgedTrainer : GameTrainer
    {
        public override eCharacterClass TrainedClass => eCharacterClass.Stormforged;

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            if (StormforgedRules.IsStormforged(player.CharacterClass.ID) && player.Level >= StormRegalia.GrantLevel)
                SayTo(player, eChatLoc.CL_PopupWindow, $"The storm has crowned you, sovereign. Ask me for the [{StormRegalia.GrantKeyword}] and I will arm you as a king of Midgard.");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text))
                return false;
            if (source is GamePlayer player && text == StormRegalia.GrantKeyword)
                StormRegalia.Grant(player, this);
            return true;
        }
    }
}
