using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.Quests
{
    /// <summary>
    /// Some quests send the player to talk to, whisper, or hand items to a monster: Tiomoid the Siabra Gurite in Lough
    /// Gur (Lost Townfolk of Bran Llyr), Trefor and Curmie (Occupation of Lough Gur), Shiake (Mother's Children), the
    /// XP-item turn-in NPCs of the Shrouded Isles tribes. Their faction is kill-on-sight by default, or they have no
    /// faction and attack anyone, so the player was attacked on arrival or turned away with a dirty look and the quest
    /// could not go on (quest audit 2026-10-13: 67 such NPCs). While a player is on a step that talks to, whispers to
    /// or hands items to one of them, or can take one of its quests, that NPC does not aggro the player and talks to
    /// them. Everyone else meets the NPC as before, and a player who attacks it gets a fight.
    /// </summary>
    public static class QuestTalkTruce
    {
        private static readonly Lazy<HashSet<string>> _questNpcNames = new(LoadQuestNpcNames);
        private static volatile HashSet<string> _botSparedNames;

        /// <summary>
        /// Night 2026-11-04: autonomous gamebots killed Keeper Rasa (quest 20312 talk and hand-in steps) 398 times and Bjame
        /// (21175-21185) 327 times. Both are aggressive level 26 monsters in the database; bots walking to them for their
        /// quest step pulled them as route threats, and the next bots on the quest found "Keeper Rasa was not there".
        /// A data quest's giver or talk, whisper or hand-in character is left alone by gamebots, and does not aggro them,
        /// unless some quest also asks for it to be killed (then it is a quest monster, see AutonomousBotQuests.MayHunt).
        /// </summary>
        public static bool SparesBots(GameNPC npc) =>
            npc != null && npc.Realm == eRealm.None && SparesBots(npc.Name);

        /// <summary>The name check of <see cref="SparesBots(GameNPC)"/>.</summary>
        public static bool SparesBots(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            HashSet<string> names = _botSparedNames;
            if (names == null)
            {
                try
                {
                    names = GameServer.Database == null ? new(StringComparer.OrdinalIgnoreCase) : BotSparedNames(GameServer.Database.SelectAllObjects<DbDataQuest>());
                }
                catch (Exception)
                {
                    names = new(StringComparer.OrdinalIgnoreCase);
                }
                _botSparedNames = names;
            }
            return names.Contains(name);
        }

        /// <summary>Tests only: the names gamebots spare, as if built from these quests.</summary>
        public static void SetBotSparedNamesForTests(IEnumerable<DbDataQuest> quests) =>
            _botSparedNames = quests == null ? null : BotSparedNames(quests);

        /// <summary>Givers and talk targets of every quest, less every quest's kill targets. Pure.</summary>
        public static HashSet<string> BotSparedNames(IEnumerable<DbDataQuest> quests)
        {
            HashSet<string> talk = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> kill = new(StringComparer.OrdinalIgnoreCase);
            foreach (DbDataQuest quest in quests)
            {
                if (!string.IsNullOrEmpty(quest.StartName))
                    talk.Add(quest.StartName);
                string[] types = (quest.StepType ?? string.Empty).Split('|');
                string[] targets = (quest.TargetName ?? string.Empty).Split('|');
                for (int i = 0; i < Math.Min(types.Length, targets.Length); i++)
                {
                    if (!byte.TryParse(types[i], out byte type))
                        continue;
                    string name = targets[i].Split(';')[0];
                    if (name.Length == 0)
                        continue;
                    var step = (DataQuest.eStepType) type;
                    if (TalksTo(step))
                        talk.Add(name);
                    else if (step is DataQuest.eStepType.Kill or DataQuest.eStepType.KillFinish)
                        kill.Add(name);
                }
            }
            talk.ExceptWith(kill);
            return talk;
        }

        /// <summary>Called from mob aggro checks: cheap for every NPC that is not a data quest's talk target.</summary>
        public static bool Holds(GameNPC npc, GamePlayer player)
        {
            if (npc == null || player == null || npc.Realm != eRealm.None || !_questNpcNames.Value.Contains(npc.Name))
                return false;

            foreach (var pair in player.QuestList) // not .Keys: that copies the keys on every aggro check
            {
                if (pair.Key is DataQuest dataQuest && TalksTo(dataQuest.StepType) &&
                    QuestNames.Same(dataQuest.TargetName, npc.Name) && (dataQuest.TargetRegion == 0 || dataQuest.TargetRegion == npc.CurrentRegionID))
                    return true;
            }

            // A quest it gives. Level range only: the full qualification check reads the database.
            List<DataQuest> given = npc.DataQuestList;
            for (int i = 0; i < given.Count; i++)
            {
                DataQuest quest = i < given.Count ? given[i] : null;
                if (quest?.DBDataQuest == null || quest.StartType == DataQuest.eStartType.KillComplete)
                    continue;
                if (player.Level >= quest.DBDataQuest.MinLevel && player.Level <= quest.DBDataQuest.MaxLevel)
                    return true;
            }

            return false;
        }

        public static bool TalksTo(DataQuest.eStepType step) => step is DataQuest.eStepType.Deliver or DataQuest.eStepType.DeliverFinish or
            DataQuest.eStepType.Interact or DataQuest.eStepType.InteractFinish or DataQuest.eStepType.Whisper or
            DataQuest.eStepType.WhisperFinish or DataQuest.eStepType.Collect or DataQuest.eStepType.CollectFinish;

        /// <summary>Start NPCs and talk/whisper/hand-in targets of every data quest (names compared without case, as DataQuest does).</summary>
        private static HashSet<string> LoadQuestNpcNames()
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

            foreach (DbDataQuest quest in EpicChainLinks.Link(GameServer.Database.SelectAllObjects<DbDataQuest>()))
            {
                if (!string.IsNullOrEmpty(quest.StartName))
                    names.Add(quest.StartName);

                string[] types = (quest.StepType ?? string.Empty).Split('|');
                string[] targets = (quest.TargetName ?? string.Empty).Split('|');

                for (int i = 0; i < Math.Min(types.Length, targets.Length); i++)
                {
                    if (!byte.TryParse(types[i], out byte type) || !TalksTo((DataQuest.eStepType) type))
                        continue;

                    string name = targets[i].Split(';')[0];
                    if (name.Length > 0)
                        names.Add(name);
                }
            }

            return names;
        }
    }
}
