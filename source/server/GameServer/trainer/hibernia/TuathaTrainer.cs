using DOL.GS.PacketHandler;

namespace DOL.GS.Trainer
{
    [NPCGuildScript("Tuatha Sovereign Trainer", eRealm.Hibernia)]
    public class TuathaTrainer : GameTrainer
    {
        public override eCharacterClass TrainedClass => eCharacterClass.TuathaSovereign;

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            if (PendragonRules.IsTuatha(player.CharacterClass.ID) && player.Level >= RegaliaSet.GrantLevel)
                SayTo(player, eChatLoc.CL_PopupWindow, $"Tir na Nog has crowned you, sovereign. Ask me for the [{TaraRegalia.GrantKeyword}] and I will dress you as a king of the Sidhe.");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text))
                return false;
            if (source is GamePlayer player && text == TaraRegalia.GrantKeyword)
                TaraRegalia.Set.Grant(player, this);
            return true;
        }
    }
}
