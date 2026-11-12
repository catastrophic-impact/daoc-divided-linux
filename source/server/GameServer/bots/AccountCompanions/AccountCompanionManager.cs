using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Commands;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Summons and dismisses account companions and keeps one live bot per character.
    /// </summary>
    public static class AccountCompanionManager
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<string, GameBot> Active = new(StringComparer.OrdinalIgnoreCase);

        public static string InternalIdFor(string characterId) => $"companion:{characterId}";

        public static bool IsSummoned(string characterId) =>
            !string.IsNullOrWhiteSpace(characterId) && Active.ContainsKey(characterId);

        public static IReadOnlyList<GameBot> ActiveFor(GamePlayer owner) =>
            owner == null ? Array.Empty<GameBot>() : Active.Values.Where(bot => bot.Owner == owner).OrderBy(bot => bot.Name).ToArray();

        internal static void Unregister(GameBot companion)
        {
            string id = companion?.CompanionCharacterId;
            if (id != null && Active.TryGetValue(id, out GameBot current) && ReferenceEquals(current, companion))
                Active.TryRemove(id, out _);
        }

        /// <summary>The characters on the owner's account, other than the one being played.</summary>
        public static IReadOnlyList<DbCoreCharacter> AccountCharacters(GamePlayer owner)
        {
            DbCoreCharacter[] characters = owner?.Client?.Account?.Characters;
            if (characters == null && owner?.Client?.Account != null)
            {
                characters = DOLDB<DbCoreCharacter>.SelectObjects(
                    DB.Column("AccountName").IsEqualTo(owner.Client.Account.Name)).ToArray();
            }
            return (characters ?? Array.Empty<DbCoreCharacter>())
                .Where(character => character != null && character.ObjectId != owner.ObjectId)
                .OrderBy(character => character.AccountSlot)
                .ToArray();
        }

        public static DbCoreCharacter FindCharacter(GamePlayer owner, string name) =>
            string.IsNullOrWhiteSpace(name) ? null :
            AccountCharacters(owner).FirstOrDefault(character => string.Equals(character.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool HasAdvancedClass(DbCoreCharacter character) =>
            ScriptMgr.FindCharacterClass(character.Class)?.HasAdvancedFromBaseClass() == true;

        public static bool IsClassSupported(eRealm realm, int classId) =>
            TemporaryGroupClassCatalog.ForRealm(realm).Any(entry => (int)entry.CharacterClass == classId) &&
            BotSpec.GetSpec((eCharacterClass)classId) != null;

        public static AccountCompanionRules.SummonRefusal Check(GamePlayer owner, DbCoreCharacter character)
        {
            bool groupFull = owner.Group != null && owner.Group.MemberCount >= owner.Group.MaximumMemberCount;
            return AccountCompanionRules.Check(
                character.ObjectId == owner.ObjectId,
                (int)owner.Realm,
                character.Realm,
                HasAdvancedClass(character),
                IsClassSupported(owner.Realm, character.Class),
                IsSummoned(character.ObjectId),
                groupFull);
        }

        /// <summary>Advanced classes this base-class character may choose, for its race.</summary>
        public static IReadOnlyList<eCharacterClass> PromotionChoices(DbCoreCharacter character)
        {
            ICharacterClass current = ScriptMgr.FindCharacterClass(character.Class);
            if (current == null || current.HasAdvancedFromBaseClass())
                return Array.Empty<eCharacterClass>();

            return TemporaryGroupClassCatalog.ForRealm((eRealm)character.Realm)
                .Select(entry => entry.CharacterClass)
                .Where(candidate =>
                {
                    if (BotSpec.GetSpec(candidate) == null)
                        return false;
                    ICharacterClass advanced = ScriptMgr.FindCharacterClass((int)candidate);
                    if (advanced == null || ScriptMgr.FindCharacterBaseClass((int)candidate)?.ID != current.ID)
                        return false;
                    return advanced.EligibleRaces == null || advanced.EligibleRaces.Any(race => (int)race.ID == character.Race);
                })
                .ToArray();
        }

        /// <summary>
        /// Gives a base-class character its advanced class, the choice a trainer offers at level 5.
        /// A companion needs one to know how to fight. The character is saved at once.
        /// </summary>
        public static bool Promote(GamePlayer owner, DbCoreCharacter character, eCharacterClass chosen, out string message)
        {
            if (IsSummoned(character.ObjectId))
            {
                message = $"Dismiss {character.Name} before changing its class.";
                return false;
            }
            if (!PromotionChoices(character).Contains(chosen))
            {
                string choices = string.Join(", ", PromotionChoices(character));
                message = choices.Length == 0
                    ? $"{character.Name} has already chosen a class."
                    : $"{character.Name} can become: {choices}.";
                return false;
            }

            character.Class = (int)chosen;
            if (!GameServer.Database.SaveObject(character))
            {
                message = $"{character.Name} could not be saved. Its class was not changed.";
                return false;
            }
            message = $"{character.Name} is now a {chosen}.";
            log.Info($"ACCOUNT_COMPANION_PROMOTED owner=\"{owner?.Name}\" character=\"{character.Name}\" class={chosen}");
            return true;
        }

        public static AccountCompanionRecord LoadRecord(string characterId) =>
            DOLDB<AccountCompanionRecord>.SelectObject(DB.Column("CharacterId").IsEqualTo(characterId))
            ?? new AccountCompanionRecord { CharacterId = characterId };

        public static bool SetStyle(GamePlayer owner, DbCoreCharacter character, string style, out string message)
        {
            IReadOnlyList<eSpecType> choices = BotSpec.GetSpecializationChoices((eCharacterClass)character.Class);
            eSpecType wanted = choices.FirstOrDefault(choice => string.Equals(choice.ToString(), style?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (choices.Count <= 1 || !choices.Contains(wanted) || string.IsNullOrWhiteSpace(style))
            {
                message = choices.Count <= 1
                    ? $"{character.Name}'s class has one companion build."
                    : $"Builds for {character.Name}: {string.Join(", ", choices)}.";
                return false;
            }
            if (IsSummoned(character.ObjectId))
            {
                message = $"Dismiss {character.Name} before changing its build.";
                return false;
            }

            AccountCompanionRecord record = LoadRecord(character.ObjectId);
            record.Style = wanted.ToString();
            record.BuildPlan = string.Empty; // Chosen again, near what the character has trained.
            record.Dirty = true;
            bool saved = record.IsPersisted ? GameServer.Database.SaveObject(record) : GameServer.Database.AddObject(record);
            message = saved
                ? $"{character.Name} will train the {wanted} build from now on. Points already spent stay where they are."
                : $"{character.Name}'s build could not be saved.";
            return saved;
        }

        public static GameBot Summon(GamePlayer owner, DbCoreCharacter character, out string message)
        {
            AccountCompanionRules.SummonRefusal refusal = Check(owner, character);
            if (refusal != AccountCompanionRules.SummonRefusal.None)
            {
                message = AccountCompanionRules.RefusalMessage(refusal, character.Name);
                return null;
            }

            GameBot companion;
            try
            {
                companion = new GameBot(owner, character, LoadRecord(character.ObjectId));
            }
            catch (Exception exception)
            {
                log.Error($"ACCOUNT_COMPANION_LOAD_FAILED owner=\"{owner.Name}\" character=\"{character.Name}\"", exception);
                message = $"{character.Name} could not be summoned: {exception.Message}";
                return null;
            }

            if (!Active.TryAdd(character.ObjectId, companion))
            {
                companion.Delete(); // Never joined: releases its owner event handlers, saves nothing.
                message = AccountCompanionRules.RefusalMessage(AccountCompanionRules.SummonRefusal.AlreadySummoned, character.Name);
                return null;
            }

            PlaceBeside(owner, companion);
            if (!companion.AddToWorld())
            {
                companion.Delete(); // Never joined: releases the character and its owner event handlers.
                message = $"{character.Name} could not enter this region.";
                return null;
            }

            if (owner.Group == null)
            {
                var group = new Group(owner);
                GroupMgr.AddGroup(group);
                group.AddMember(owner);
            }
            if (!owner.Group.AddMember(companion))
            {
                // Never joined: Delete saves nothing and releases the character.
                companion.Delete();
                message = $"{character.Name} could not join your group.";
                return null;
            }
            companion.MarkCompanionJoined();

            companion.EnterPlayerLedGroup(owner);
            companion.Follow(owner, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            if (companion.Brain is BotBrain brain)
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);

            if (companion.CompanionAutoSpend)
                companion.AutoTrainCompanion();
            companion.CompanionRecord.LastSummonedUtc = DateTime.UtcNow.ToString("O");
            companion.MarkCompanionDirty();
            companion.SaveAccountCompanion("summon");
            companion.StartCompanionAutosave();

            log.Info($"ACCOUNT_COMPANION_SUMMONED owner=\"{owner.Name}\" character=\"{character.Name}\" " +
                     $"class={(eCharacterClass)character.Class} level={companion.Level}");
            message = AccountCompanionRules.JoinMessage(companion.Name, companion.Level, companion.ClassName,
                companion.CompanionAutoSpend, companion.CompanionUnspentSpecPoints());
            return companion;
        }

        public static void Dismiss(GameBot companion)
        {
            if (companion?.IsAccountCompanion == true && companion.ObjectState is not GameObject.eObjectState.Deleted)
                companion.Delete();
        }

        /// <summary>Gives a dropped item to the group companion it upgrades the most.</summary>
        public static bool TryGiveUpgrade(Group group, WorldInventoryItem item, out GameBot taker)
        {
            taker = null;
            if (group == null || item?.Item == null || item.Item.IsStackable)
                return false;

            GameBot best = null;
            int bestGain = 0;
            foreach (GameBot companion in group.GetMembersInTheGroup().OfType<GameBot>())
            {
                if (!companion.IsAccountCompanion || !companion.IsAlive ||
                    companion.ObjectState is not GameObject.eObjectState.Active ||
                    companion.CurrentRegion != item.CurrentRegion ||
                    !companion.IsWithinRadius(item, WorldMgr.VISIBILITY_DISTANCE))
                    continue;
                int gain = GameBot.CompanionUpgradeValue(companion, item.Item);
                if (gain > bestGain)
                {
                    best = companion;
                    bestGain = gain;
                }
            }

            if (best == null || !best.TryTakeCompanionUpgrade(item.Item))
                return false;
            taker = best;
            return true;
        }

        private static void PlaceBeside(GamePlayer owner, GameBot companion)
        {
            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            int distance = Random.Shared.Next(110, 221);
            Vector3 current = new(owner.X, owner.Y, owner.Z);
            Vector3 desired = new(
                owner.X + (float)(Math.Cos(angle) * distance),
                owner.Y + (float)(Math.Sin(angle) * distance),
                owner.Z);
            Vector3 spawn = PathfindingProvider.Instance.GetMoveAlongSurface(
                owner.CurrentZone, current, desired, PathfindingProvider.Instance.DefaultFilters) ?? current;
            companion.X = (int)Math.Round(spawn.X);
            companion.Y = (int)Math.Round(spawn.Y);
            companion.Z = (int)Math.Round(spawn.Z);
            companion.Heading = owner.Heading;
            companion.CurrentRegionID = owner.CurrentRegionID;
        }
    }
}
