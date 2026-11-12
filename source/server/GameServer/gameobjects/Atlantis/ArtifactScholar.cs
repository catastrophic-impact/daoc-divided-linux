using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Hands out artifacts. Encounter credit gives a level 10 artifact, a credit bought with Atlantean
    /// Glass gives a level 0 one, and an artifact already owned can be swapped to another version,
    /// keeping its level, or replaced if lost.
    /// </summary>
    public class ArtifactScholar : GameNPC
    {
        private const string PendingKey = "ArtifactScholarPending";

        private sealed record Pending(string ArtifactId, List<DbItemTemplate> Versions);

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            TurnTo(player, 10000);

            List<string> ready = ReadyArtifacts(player);
            StringBuilder text = new($"Greetings, {player.Name}. ");

            if (ready.Count == 0)
                text.Append("You have nothing for me to work on yet. Finish an artifact's encounter, or bring me a credit from Aphaestia, and I will prepare the artifact for you.\n\n");
            else
            {
                text.Append("Here is what I can do for you. Choose an artifact to see its versions:\n\n");

                foreach (string artifactId in ready)
                    text.Append($"[{ArtifactCatalog.NameOf(artifactId)}] - {Describe(player, artifactId)}\n");

                text.Append('\n');
            }

            text.Append("I can also tell you [where] the artifacts are found.");
            SendPopup(player, text.ToString());
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            // Each link here is a click in a menu, so this skips the normal NPC whisper throttle,
            // which would drop a version choice made straight after picking the artifact.
            if (source is not GamePlayer player || !player.IsWithinRadius(this, WorldMgr.WHISPER_DISTANCE))
                return false;

            text = text.Trim();

            if (text.Equals("where", StringComparison.OrdinalIgnoreCase))
            {
                ShowLocations(player);
                return true;
            }

            if (int.TryParse(text, out int choice))
            {
                Pending pending = player.TempProperties.GetProperty<Pending>(PendingKey);

                if (pending != null && choice >= 1 && choice <= pending.Versions.Count)
                    Deliver(player, pending.ArtifactId, pending.Versions[choice - 1]);

                return true;
            }

            string artifactId = ArtifactCatalog.FindByName(text);

            if (artifactId == null)
                return true;

            ArtifactEncounterDef encounter = ArtifactEncounters.Find(artifactId);

            if (ReadyArtifacts(player).Contains(artifactId))
                ShowVersions(player, artifactId);
            else if (encounter != null)
                SayTo(player, $"{encounter.Name}: {encounter.Hint}");

            return true;
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source is not GamePlayer player || item == null)
                return false;

            string artifactId = ArtifactCatalog.ArtifactOfCredit(item.Id_nb) ?? ArtifactCatalog.ArtifactOf(item.Id_nb);

            if (artifactId == null)
                return base.ReceiveItem(source, item);

            // The item stays with the player until a version is chosen.
            ShowVersions(player, artifactId);
            return true;
        }

        private static IEnumerable<DbInventoryItem> Owned(GamePlayer player)
        {
            return player.Inventory.AllItems.Where(item => item.SlotPosition != 0);
        }

        private static DbInventoryItem FindArtifact(GamePlayer player, string artifactId)
        {
            return Owned(player).FirstOrDefault(item => ArtifactCatalog.ArtifactOf(item.Id_nb) == artifactId);
        }

        private static DbInventoryItem FindCredit(GamePlayer player, string artifactId)
        {
            return player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .FirstOrDefault(item => ArtifactCatalog.ArtifactOfCredit(item.Id_nb) == artifactId);
        }

        /// <summary>Artifacts this character can collect, change or replace here.</summary>
        private static List<string> ReadyArtifacts(GamePlayer player)
        {
            HashSet<string> ready = [];

            foreach (OfflineArtifactProgress row in ArtifactProgress.For(player).Values)
            {
                if (row.EncounterDone || row.Claimed)
                    ready.Add(row.ArtifactID);
            }

            foreach (DbInventoryItem item in Owned(player))
            {
                string artifactId = ArtifactCatalog.ArtifactOf(item.Id_nb) ?? ArtifactCatalog.ArtifactOfCredit(item.Id_nb);

                if (artifactId != null)
                    ready.Add(artifactId);
            }

            return ready.OrderBy(ArtifactCatalog.NameOf).ToList();
        }

        private static string Describe(GamePlayer player, string artifactId)
        {
            OfflineArtifactProgress row = ArtifactProgress.Get(player, artifactId);
            DbInventoryItem held = FindArtifact(player, artifactId);

            if (row?.EncounterDone == true && (held as ArtifactItem)?.ArtifactLevel != ArtifactLevels.MaxLevel)
                return "encounter complete, ready at level 10";

            if (held != null)
                return "change to another version";

            return row?.Claimed == true ? "replace a lost artifact" : "ready at level 0";
        }

        private void ShowVersions(GamePlayer player, string artifactId)
        {
            List<DbItemTemplate> versions = ArtifactCatalog.VersionsFor(player, artifactId);

            if (versions.Count == 0)
            {
                SayTo(player, $"I am sorry, {ArtifactCatalog.NameOf(artifactId)} has no version your class can use.");
                return;
            }

            player.TempProperties.SetProperty(PendingKey, new Pending(artifactId, versions));

            StringBuilder text = new($"{ArtifactCatalog.NameOf(artifactId)} can be prepared for you as:\n\n");

            for (int i = 0; i < versions.Count; i++)
                text.Append($"[{i + 1}] {DescribeVersion(versions[i])}\n");

            SendPopup(player, text.ToString());
        }

        public static string DescribeVersion(DbItemTemplate template)
        {
            string hand = template.Item_Type == Slot.LEFTHAND ? ", left hand" : string.Empty;
            string bonuses = string.Join(", ", new[]
                {
                    (template.Bonus1Type, template.Bonus1), (template.Bonus2Type, template.Bonus2), (template.Bonus3Type, template.Bonus3)
                }
                .Where(bonus => bonus.Item1 != 0 && bonus.Item2 != 0)
                .Select(bonus => $"{SkillBase.GetPropertyName((eProperty) bonus.Item1)} {bonus.Item2}"));

            return $"{template.Name} ({(eObjectType) template.Object_Type}{hand}): {bonuses}";
        }

        private void ShowLocations(GamePlayer player)
        {
            StringBuilder text = new("Each artifact has a story of its own. Ask me about any of them:\n\n");

            foreach (string artifactId in ArtifactCatalog.ArtifactIds.OrderBy(ArtifactCatalog.NameOf))
            {
                ArtifactEncounterDef encounter = ArtifactEncounters.Find(artifactId);
                text.Append($"[{ArtifactCatalog.NameOf(artifactId)}]{(encounter == null ? string.Empty : $" - {encounter.Zone}")}\n");
            }

            SendPopup(player, text.ToString());
        }

        // One message packet holds at most 2048 bytes, so a long reply goes out in pieces, split
        // between lines; the client adds each piece to the same popup window.
        private const int MaxPopupChunk = 1500;

        private static void SendPopup(GamePlayer player, string text)
        {
            StringBuilder chunk = new();

            foreach (string line in text.Split('\n'))
            {
                if (chunk.Length > 0 && chunk.Length + line.Length + 1 > MaxPopupChunk)
                {
                    player.Out.SendMessage(chunk.ToString(), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
                    chunk.Clear();
                }

                if (chunk.Length > 0)
                    chunk.Append('\n');

                chunk.Append(line);
            }

            if (chunk.Length > 0)
                player.Out.SendMessage(chunk.ToString(), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
        }

        private void Deliver(GamePlayer player, string artifactId, DbItemTemplate template)
        {
            player.TempProperties.RemoveProperty(PendingKey);

            OfflineArtifactProgress row = ArtifactProgress.Get(player, artifactId);
            DbInventoryItem held = FindArtifact(player, artifactId);
            DbInventoryItem credit = held == null && row?.Claimed != true && row?.EncounterDone != true ? FindCredit(player, artifactId) : null;

            if (held == null && credit == null && row?.Claimed != true && row?.EncounterDone != true)
                return;

            if (held != null && player.Inventory.EquippedItems.Contains(held))
            {
                SayTo(player, $"Take off your {held.Name} first, then ask me again.");
                return;
            }

            long experience = row?.EncounterDone == true ? ArtifactLevels.MaxXp : Math.Max(held?.Experience ?? 0, row?.Experience ?? 0);
            GameInventoryItem item = GameInventoryItem.Create(template);
            item.Experience = experience;

            lock (player.Inventory.Lock)
            {
                if (held != null && !player.Inventory.RemoveItem(held))
                    return;

                eInventorySlot slot = player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

                if (slot == eInventorySlot.Invalid || !player.Inventory.AddItem(slot, item))
                {
                    SayTo(player, "You have no room to carry it. Make some space and ask me again.");
                    return;
                }

                if (credit != null)
                    player.Inventory.RemoveCountFromStack(credit, 1);
            }

            InventoryLogging.LogInventoryAction(this, player, eInventoryActionType.Quest, template, 1);
            ArtifactProgress.Update(player, artifactId, progress =>
            {
                progress.Claimed = true;
                progress.Experience = Math.Max(progress.Experience, experience);
            });

            SayTo(player, $"It is done. Your {item.Name} is at artifact level {ArtifactLevels.LevelFor(experience)}.");
        }
    }
}
