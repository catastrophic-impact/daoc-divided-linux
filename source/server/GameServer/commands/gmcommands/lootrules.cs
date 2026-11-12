using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute(
        "&lootrules",
        ePrivLevel.GM,
        "Show or reload the loot tuning in loot-rules.json (beside bot-goals.json).",
        "'/lootrules' shows the rules in use and where they come from.",
        "'/lootrules reload' reads the file now instead of within 10 seconds of a change.")]
    public class LootRulesCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length > 1 && args[1].ToLower() != "reload")
            {
                DisplaySyntax(client);
                return;
            }

            LootRules rules = args.Length > 1 ? LootRulesFile.Refresh(true) : LootRulesFile.Current;
            string path = LootRulesFile.FilePath;
            bool exists = path != null && System.IO.File.Exists(path);

            var text = new List<string>
            {
                exists ? "From " + path : "No " + LootRules.FileName + " (looked for " + (path ?? "no server folder") + "): the built-in defaults are in use.",
            };

            if (LootRulesFile.LastError != null)
            {
                text.Add("");
                text.Add("The file has a problem, so the last good rules are still in use:");
                text.Add(LootRulesFile.LastError);
            }

            text.Add("");
            foreach (string line in rules.ToJson().Split('\n'))
                text.Add(line.TrimEnd('\r'));
            client.Out.SendCustomTextWindow("Loot Rules", text);

            if (args.Length > 1)
                client.Out.SendMessage(LootRulesFile.LastError == null ? "Loot rules reloaded." : "Loot rules not reloaded: " + LootRulesFile.LastError,
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
