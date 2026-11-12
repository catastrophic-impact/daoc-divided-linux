using System;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS.PacketHandler.Client.v168
{
    /// <summary>
    /// The settings of the client's group finder window, kept per character as a custom param.
    /// Four bytes: 1 if a role is picked (0/1), "queue for PvE group" (0/1), and up to two roles
    /// (0 = none, 1 = healer, 2 = crowd control, 3 = support, 4 = damage). The client only offers the
    /// roles that fit the character's class, and none for a base class.
    /// </summary>
    public static class GroupFinderSettings
    {
        public const int Length = 4;
        public const string KeyName = "GroupFinderSettings";

        public static bool IsValid(byte[] settings)
        {
            return settings != null && settings.Length == Length && settings[0] <= 1 && settings[1] <= 1 && settings[2] <= 4 && settings[3] <= 4;
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

    // Sent when the player presses Set in the group finder window (the /cgf command).
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.GroupFinderSave, "Save group finder settings", eClientStatus.PlayerInGame)]
    public class GroupFinderSaveHandler : PacketHandler
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            byte[] settings = new byte[GroupFinderSettings.Length];

            if (packet.Read(settings, 0, settings.Length) != settings.Length || !GroupFinderSettings.IsValid(settings))
            {
                if (Log.IsWarnEnabled)
                    Log.Warn($"Invalid group finder settings (player={client.Player.Name}, settings={Convert.ToHexString(settings)})");

                return;
            }

            GroupFinderSettings.Save(client.Player, settings);
        }
    }

    // Sent when the group finder window opens. The byte is not meaningful. The client shows the window only
    // after this reply.
    [PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.GroupFinderRequest, "Request group finder settings", eClientStatus.PlayerInGame)]
    public class GroupFinderRequestHandler : PacketHandler
    {
        protected override void HandlePacketInternal(GameClient client, GSPacketIn packet)
        {
            client.Out.SendGroupFinderSettings(GroupFinderSettings.Load(client.Player));
        }
    }
}
