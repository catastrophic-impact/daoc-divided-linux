using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute(
        "&tempest",
        ePrivLevel.GM,
        "Tests the Storm Sovereign Tempest orbs (the Warlock chamber orbs over your head)",
        "/tempest - show your Tempest charges",
        "/tempest cycle - show each stock orb colour for 5 seconds",
        "/tempest show <code> - light all five orbs in one colour code (0x66 or 102)",
        "/tempest set <0-5> - set your Tempest charges",
        "/tempest color <code> - colour used for charges until the server restarts",
        "/tempest clear - put the orbs out")]
    public class TempestCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (player == null)
                return;
            if (args.Length < 2)
            {
                Say(player, $"Tempest {StormTempest.Charges(player)}/{StormTempest.MaxCharges}.");
                DisplaySyntax(client);
                return;
            }

            byte code;
            switch (args[1].ToLowerInvariant())
            {
                case "cycle":
                    StormTempest.Cycle(player);
                    return;
                case "show":
                    if (args.Length < 3 || !StormTempest.TryParseCode(args[2], out code))
                        break;
                    StormTempest.Preview(player, code);
                    Say(player, $"All five orbs lit with colour 0x{code:X2}. /tempest clear to put them out.");
                    return;
                case "set":
                    if (args.Length < 3 || !int.TryParse(args[2], out int charges))
                        break;
                    StormTempest.Clear(player);
                    StormTempest.Set(player, charges);
                    Say(player, $"Tempest {StormTempest.Charges(player)}/{StormTempest.MaxCharges} (colour 0x{StormTempest.OrbCode:X2}); charges fade after 30 seconds.");
                    return;
                case "color":
                case "colour":
                    if (args.Length < 3 || !StormTempest.TryParseCode(args[2], out code))
                        break;
                    StormTempest.OrbCode = code;
                    Say(player, $"Tempest charges now use colour 0x{code:X2} until the server restarts.");
                    return;
                case "clear":
                    StormTempest.Clear(player);
                    Say(player, "Tempest orbs cleared.");
                    return;
            }
            DisplaySyntax(client);
        }

        private static void Say(GamePlayer player, string text) =>
            player.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
