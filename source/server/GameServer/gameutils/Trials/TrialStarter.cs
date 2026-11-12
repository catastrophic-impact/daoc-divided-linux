using DOL.GS.PacketHandler;

namespace DOL.GS.Trials
{
    /// <summary>The NPC at a trial's anchor. Talking to it explains the trial and starts a run.</summary>
    public class TrialStarter : GameNPC
    {
        public TrialDefinition Definition { get; }
        public TrialRun Run { get; set; }
        public long AvailableAt { get; set; }

        public TrialStarter(TrialDefinition definition)
        {
            Definition = definition;
            Name = definition.StarterName;
            GuildName = "Master Level Trial";
            Model = definition.StarterModel;
            Level = 50;
            Size = 50;
            Flags = eFlags.PEACE;
            Realm = eRealm.None;
            RespawnInterval = -1;
        }

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            SayTo(player, eChatLoc.CL_PopupWindow, $"{Definition.Intro}\n\nTrial {Definition.Key}: {Definition.Name} ({TrialManager.ScopeName(Definition.Scope)}).\n\n{Status(player)}");
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source is not GamePlayer player)
                return false;
            if (text.Equals("begin", System.StringComparison.OrdinalIgnoreCase) && !TrialManager.TryStart(this, player, out string reason))
                SayTo(player, eChatLoc.CL_PopupWindow, reason);
            return true;
        }

        private string Status(GamePlayer player)
        {
            if (Run != null)
                return "This trial is under way.";
            if (TrialManager.MissingSteps(player, Definition) is { Length: > 0 } missing)
                return $"You must first finish step{(missing.Length > 1 ? "s" : "")} {string.Join(", ", missing)} of Master Level {Definition.ML}.";
            if (Definition.Refuse?.Invoke(player) is string refused)
                return refused;
            return player.MLLevel + 1 == Definition.ML && !player.HasFinishedMLStep(Definition.ML, Definition.Step)
                ? "When your party is ready, say [begin]."
                : "You can't earn credit here, but you may help others. Say [begin] when your party is ready.";
        }
    }
}
