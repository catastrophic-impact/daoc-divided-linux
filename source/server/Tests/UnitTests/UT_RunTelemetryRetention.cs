using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit.Gameserver
{
    [TestFixture]
    public class UT_RunTelemetryRetention
    {
        private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Files_older_than_the_kept_days_expire()
        {
            Assert.That(RunTelemetry.IsExpired("run-20261031.jsonl", Now, 7), Is.False);
            Assert.That(RunTelemetry.IsExpired("run-20261030.jsonl", Now, 7), Is.True);
            Assert.That(RunTelemetry.IsExpired("run-20261107.jsonl", Now, 7), Is.False);
        }

        [Test]
        public void Other_files_and_zero_days_are_kept()
        {
            Assert.That(RunTelemetry.IsExpired("run-20200101.jsonl", Now, 0), Is.False);
            Assert.That(RunTelemetry.IsExpired("notes.jsonl", Now, 7), Is.False);
            Assert.That(RunTelemetry.IsExpired("run-2020.jsonl", Now, 7), Is.False);
            Assert.That(RunTelemetry.IsExpired("run-20201301.jsonl", Now, 7), Is.False);
        }
    }
}
