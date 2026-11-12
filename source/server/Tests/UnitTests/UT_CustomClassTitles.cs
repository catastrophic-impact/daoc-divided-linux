using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Night playtest 2026-11-04: the custom classes showed "!Tuatha Sovereign!", "!Storm Sovereign!" and
    /// "!Pendragon Sovereign!" because PlayerTitles.txt had no keys for them (CharacterClassBase.GetTitle falls back
    /// to "!name!").
    /// </summary>
    [TestFixture]
    public class UT_CustomClassTitles
    {
        private static Dictionary<string, string> LoadTitles()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                foreach (string path in new[]
                {
                    Path.Combine(dir.FullName, "GameServer", "language", "EN", "PlayerTitles.txt"),
                    Path.Combine(dir.FullName, "source", "server", "GameServer", "language", "EN", "PlayerTitles.txt"),
                })
                {
                    if (!File.Exists(path))
                        continue;

                    // Same parsing as LanguageMgr: id before the first ':', text after it.
                    return File.ReadAllLines(path)
                        .Where(line => !line.StartsWith('#') && line.Contains(':'))
                        .GroupBy(line => line[..line.IndexOf(':')])
                        .ToDictionary(g => g.Key, g => g.First()[(g.First().IndexOf(':') + 1)..].Replace("\t", " ").Trim());
                }
            }

            Assert.Fail("PlayerTitles.txt not found");
            return null;
        }

        // The CharacterClass attribute names: GetTitle builds "PlayerClass.<name>.GetTitle.<level>".
        [TestCase("Storm Sovereign")]
        [TestCase("Pendragon Sovereign")]
        [TestCase("Tuatha Sovereign")]
        [TestCase("Death Knight")]
        public void EveryTitleStepHasText(string className)
        {
            Dictionary<string, string> titles = LoadTitles();

            for (int level = 5; level <= 50; level += 5)
            {
                Assert.That(titles.TryGetValue($"PlayerClass.{className}.GetTitle.{level}", out string text), Is.True, $"{className} {level}");
                Assert.That(text, Is.Not.Empty.And.Not.StartWith("!"));
            }
        }
    }
}
