using System;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS.PacketHandler.Client.v168
{
    /// <summary>
    /// The settings of the client's group options window, kept per character as a custom param.
    /// Two bytes: group type (1 = casual, 0 = private) and content (1 = PvE, 0 = PvP).
    /// </summary>
    public static class GroupOptionsSettings
    {
        public const int Length = 2;
        public const string KeyName = "GroupOptionsSettings";

        public static bool IsValid(byte[] settings)
        {
            return settings != null && settings.Length == Length && settings[0] <= 1 && settings[1] <= 1;
        }

        public static byte[] Load(GamePlayer player)
        {
            return WindowSettingsParam.Load(player, KeyName, Length, IsValid);
        }

        public static void Save(GamePlayer player, byte[] settings)
        {
            WindowSettingsParam.Save(player, KeyName, settings);
        }
    }

    // Sent when the player presses OK in the group options window.
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.GroupOptionsSave, "Save group options", eClientStatus.PlayerInGame)]
    public class GroupOptionsSaveHandler : PacketHandler
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            byte[] settings = new byte[GroupOptionsSettings.Length];

            if (packet.Read(settings, 0, settings.Length) != settings.Length || !GroupOptionsSettings.IsValid(settings))
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"Invalid group options (player={client.Player.Name}, settings={Convert.ToHexString(settings)})");

                return;
            }

            GroupOptionsSettings.Save(client.Player, settings);
        }
    }

    // Sent when the group options window opens. The byte is not meaningful.
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.GroupOptionsRequest, "Request group options", eClientStatus.PlayerInGame)]
    public class GroupOptionsRequestHandler : PacketHandler
    {
        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            client.Out.SendGroupOptions(GroupOptionsSettings.Load(client.Player));
        }
    }
}
