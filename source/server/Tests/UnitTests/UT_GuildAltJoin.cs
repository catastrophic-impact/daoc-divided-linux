using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;
using Refusal = DOL.GS.GuildAltJoin.Refusal;

namespace DOL.GS.Tests
{
    /// <summary>/gc addalt: adding another character of your own account to your guild, online or not.</summary>
    [TestFixture, NonParallelizable]
    public class UT_GuildAltJoin
    {
        private const string Account = "natex";
        private const string LeaderId = "leader-row";

        /// <summary>Reads return nothing; SaveObject is recorded and answers <see cref="SaveResult"/>; any other write fails the test.</summary>
        public class RecordingDatabase : DispatchProxy
        {
            public static readonly List<DataObject> Saved = new();
            public static bool SaveResult = true;

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IObjectDatabase.SaveObject) && args.Length == 1 && args[0] is DataObject dataObject)
                {
                    Saved.Add(dataObject);
                    return SaveResult;
                }
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("Unexpected database call in guild alt test: " + method.Name);
                Type type = method.ReturnType;
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }

        // Guild and GameObject read GameServer in their static constructors, so every test runs with a server double.
        private static readonly IObjectDatabase TestDatabase = DispatchProxy.Create<IObjectDatabase, RecordingDatabase>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => TestDatabase; }
        private GameServer _previousServer;

        [SetUp]
        public void InstallServerDouble()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            RecordingDatabase.Saved.Clear();
            RecordingDatabase.SaveResult = true;
        }

        [TearDown]
        public void RestoreServer() => GameServer.LoadTestDouble(_previousServer);

        private static Guild TestGuild(eRealm realm = eRealm.Midgard, bool starting = false)
        {
            var guild = new Guild(new DbGuild { GuildName = "Deaths Roar", GuildID = "dr-guild", Realm = (byte)realm, IsStartingGuild = starting });
            guild.Ranks = Enumerable.Range(0, 10).Select(i => new DbGuildRank { GuildID = "dr-guild", RankLevel = (byte)i, Title = $"Rank {i}" }).ToArray();
            return guild;
        }

        private static DbCoreCharacter Character(string name, eRealm realm = eRealm.Midgard, string account = Account, string guildId = "")
        {
            return new DbCoreCharacter { Name = name, AccountName = account, Realm = (int)realm, GuildID = guildId, GuildRank = 9, GuildNote = "old note" };
        }

        [Test]
        public void Check_SameAccountSameRealmUnguilded_IsAccepted()
        {
            Assert.That(GuildAltJoin.Check(TestGuild(), true, LeaderId, Account, Character("Zecks")), Is.EqualTo(Refusal.None));
            Assert.That(GuildAltJoin.Check(TestGuild(), true, LeaderId, "NATEX", Character("Zecks")), Is.EqualTo(Refusal.None),
                "Account names compare without case.");
        }

        [Test]
        public void Check_RefusesEveryUnsafeCase()
        {
            Guild guild = TestGuild();
            DbCoreCharacter self = Character("Natex");
            self.ObjectId = LeaderId;

            Assert.Multiple(() =>
            {
                Assert.That(GuildAltJoin.Check(null, true, LeaderId, Account, Character("Zecks")), Is.EqualTo(Refusal.NotInGuild));
                Assert.That(GuildAltJoin.Check(guild, false, LeaderId, Account, Character("Zecks")), Is.EqualTo(Refusal.NoInvitePrivilege),
                    "A rank without the Invite right may not add alts either.");
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, null), Is.EqualTo(Refusal.NotFound));
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, Character("Friend", account: "friend")), Is.EqualTo(Refusal.NotFound),
                    "Another account's character is never added, and reads as not found.");
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, null, Character("Zecks")), Is.EqualTo(Refusal.NotFound));
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, self), Is.EqualTo(Refusal.Self));
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, Character("Zecks", guildId: "dr-guild")), Is.EqualTo(Refusal.AlreadyInThisGuild));
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, Character("Zecks", guildId: "vdop")), Is.EqualTo(Refusal.InAnotherGuild),
                    "An alt in another guild is not moved out of it.");
                Assert.That(GuildAltJoin.Check(guild, true, LeaderId, Account, Character("Lenoxx", eRealm.Albion)), Is.EqualTo(Refusal.OtherRealm));
            });
        }

        [Test]
        public void RefusalText_ExplainsEveryRefusal()
        {
            foreach (Refusal refusal in Enum.GetValues<Refusal>().Where(r => r != Refusal.None))
                Assert.That(GuildAltJoin.RefusalText(refusal, "Zecks", "Deaths Roar"), Is.Not.Empty, refusal.ToString());
            Assert.That(GuildAltJoin.RefusalText(Refusal.None, "Zecks", "Deaths Roar"), Is.Empty);
        }

        [Test]
        public void FindAlt_MatchesTheNameWithoutCase()
        {
            var characters = new[] { Character("Natex"), Character("Zecks") };
            Assert.Multiple(() =>
            {
                Assert.That(GuildAltJoin.FindAlt(characters, "zecks")?.Name, Is.EqualTo("Zecks"));
                Assert.That(GuildAltJoin.FindAlt(characters, " Zecks ")?.Name, Is.EqualTo("Zecks"));
                Assert.That(GuildAltJoin.FindAlt(characters, "Zeck"), Is.Null);
                Assert.That(GuildAltJoin.FindAlt(characters, ""), Is.Null);
                Assert.That(GuildAltJoin.FindAlt(null, "Zecks"), Is.Null);
            });
        }

        [Test]
        public void Eligible_ListsOnlyTheAltsThatCanJoin()
        {
            DbCoreCharacter self = Character("Natex", guildId: "dr-guild");
            self.ObjectId = LeaderId;
            var characters = new[]
            {
                self,
                Character("Zecks"),
                Character("Bjorn"),
                Character("Lenoxx", eRealm.Albion),
                Character("Guilded", guildId: "vdop"),
            };

            List<DbCoreCharacter> eligible = GuildAltJoin.Eligible(TestGuild(), true, LeaderId, Account, characters);
            Assert.That(eligible.Select(c => c.Name), Is.EqualTo(new[] { "Bjorn", "Zecks" }));
            Assert.That(GuildAltJoin.Eligible(TestGuild(), false, LeaderId, Account, characters), Is.Empty);
        }

        [TestCase(false, 9)]
        [TestCase(true, 8)]
        public void JoinRank_IsTheInviteRank(bool startingGuild, int expected)
        {
            Assert.That(GuildAltJoin.JoinRank(TestGuild(starting: startingGuild)).RankLevel, Is.EqualTo(expected));
        }

        [Test]
        public void AddOffline_SavesTheRowAndListsTheMember()
        {
            Guild guild = TestGuild();
            DbCoreCharacter alt = Character("Zecks");
            alt.Level = 37;
            Dictionary<Guild, Dictionary<string, GuildMgr.GuildMemberView>> views = MemberViews();
            views[guild] = new Dictionary<string, GuildMgr.GuildMemberView>();
            try
            {
                Assert.That(GuildAltJoin.AddOffline(guild, alt, "Natex"), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(RecordingDatabase.Saved, Is.EqualTo(new DataObject[] { alt }));
                    Assert.That(alt.GuildID, Is.EqualTo("dr-guild"));
                    Assert.That(alt.GuildRank, Is.EqualTo(9));
                    Assert.That(alt.GuildNote, Is.Empty);
                    Assert.That(views[guild].Keys, Is.EqualTo(new[] { alt.ObjectId }));
                    Assert.That(views[guild][alt.ObjectId].Name, Is.EqualTo("Zecks"));
                    Assert.That(views[guild][alt.ObjectId].Rank, Is.EqualTo("9"));
                    Assert.That(views[guild][alt.ObjectId].Level, Is.EqualTo("37"));
                });

                GuildMgr.RemoveCharacterFromGuildMemberViews(guild, alt.ObjectId);
                Assert.That(views[guild], Is.Empty, "/gc remove of an offline member takes it off the list.");
            }
            finally
            {
                views.Remove(guild);
            }
        }

        [Test]
        public void AddOffline_AFailedSaveChangesNothing()
        {
            RecordingDatabase.SaveResult = false;
            DbCoreCharacter alt = Character("Zecks");

            Assert.That(GuildAltJoin.AddOffline(TestGuild(), alt, "Natex"), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(alt.GuildID, Is.Empty);
                Assert.That(alt.GuildRank, Is.EqualTo(9));
                Assert.That(alt.GuildNote, Is.EqualTo("old note"));
            });
        }

        private static Dictionary<Guild, Dictionary<string, GuildMgr.GuildMemberView>> MemberViews()
        {
            FieldInfo field = typeof(GuildMgr).GetField("_guildMemberViews", BindingFlags.NonPublic | BindingFlags.Static);
            return (Dictionary<Guild, Dictionary<string, GuildMgr.GuildMemberView>>)field.GetValue(null);
        }
    }
}
