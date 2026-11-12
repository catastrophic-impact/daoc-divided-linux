using DOL.GS.PacketHandler;

namespace DOL.GS.Trainer
{
    [NPCGuildScript("Pendragon Sovereign Trainer", eRealm.Albion)]
    public class PendragonTrainer : GameTrainer
    {
        public override eCharacterClass TrainedClass => eCharacterClass.PendragonSovereign;

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            if (PendragonRules.IsPendragon(player.CharacterClass.ID) && player.Level >= RegaliaSet.GrantLevel)
                SayTo(player, eChatLoc.CL_PopupWindow, $"Camelot has crowned you, sovereign. Ask me for the [{CamelotRegalia.GrantKeyword}] and I will arm you as the Pendragon.");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text))
                return false;
            if (source is GamePlayer player && text == CamelotRegalia.GrantKeyword)
                CamelotRegalia.Set.Grant(player, this);
            return true;
        }
    }
}
