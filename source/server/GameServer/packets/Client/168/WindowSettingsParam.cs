using System;
using DOL.Database;

namespace DOL.GS.PacketHandler.Client.v168
{
    /// <summary>
    /// Stores the few bytes of settings a client window sends (group finder, group options) per character,
    /// as a hex string in a character custom param.
    /// </summary>
    public static class WindowSettingsParam
    {
        /// <summary>
        /// Returns the stored settings, or zeros if none are stored or the stored value isn't valid.
        /// </summary>
        public static byte[] Load(GamePlayer player, string keyName, int length, Func<byte[], bool> isValid)
        {
            DbCoreCharacterXCustomParam param = Select(player, keyName);

            if (param == null)
                return new byte[length];

            try
            {
                byte[] settings = Convert.FromHexString(param.Value);
                return isValid(settings) ? settings : new byte[length];
            }
            catch (FormatException)
            {
                return new byte[length];
            }
        }

        public static void Save(GamePlayer player, string keyName, byte[] settings)
        {
            DbCoreCharacterXCustomParam param = Select(player, keyName);
            string value = Convert.ToHexString(settings);

            if (param == null)
                GameServer.Database.AddObject(new DbCoreCharacterXCustomParam(player.ObjectId, keyName, value));
            else if (param.Value != value)
            {
                param.Value = value;
                GameServer.Database.SaveObject(param);
            }
        }

        private static DbCoreCharacterXCustomParam Select(GamePlayer player, string keyName)
        {
            return DOLDB<DbCoreCharacterXCustomParam>.SelectObject(DB.Column("DOLCharactersObjectId").IsEqualTo(player.ObjectId).And(DB.Column("KeyName").IsEqualTo(keyName)));
        }
    }
}
