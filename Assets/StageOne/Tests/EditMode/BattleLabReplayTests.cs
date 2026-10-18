using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 随机覆盖、录制重放与 fuzz。
    public class BattleLabReplayTests
    {
        private GameCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog");
            Assert.NotNull(catalog, "Generate catalog before running tests");
        }

        private BattleLabConfig DummyConfig()
        {
            var cfg = BattleLabFactory.CreateDefault(catalog);
            BattleLabFactory.ApplyPreset(cfg, catalog, BattleLabPreset.Dummy);
            cfg.AutoEnemy = false;
            return cfg;
        }

        [Test]
        public void DefaultRandomSequenceIsUnchangedByDebugFields()
        {
            var a = new RunRandom(44972);
            var b = new RunRandom(44972);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUnit(), b.NextUnit(), 0, "默认路径逐值一致");
        }

        [Test]
        public void ForcedDrawConsumesStateSoTheRemainderStaysAligned()
        {
            var forced = new RunRandom(12345);
            var reference = new RunRandom(12345);
            forced.ForceNextDraw(0.42, 1, true);
            Assert.AreEqual(0.42, forced.NextUnit(), 1e-12);
            reference.ForceNextDraw(0.42, 1, true);
            reference.NextUnit();
            for (int i = 0; i < 32; i++)
                Assert.AreEqual(reference.NextUnit(), forced.NextUnit(), 0, "推进 State 后序列继续对齐");
        }

        [Test]
        public void ForcedValueIsClampedAndCanSkipStateAdvance()
        {
            var random = new RunRandom(7);
            uint before = random.State;
            random.ForceNextDraw(2.0, 1, false);
            Assert.AreEqual(0.999999999, random.NextUnit(), 1e-12);
            Assert.AreEqual(before, random.State, "consumeState=false 时只看不推进");

            random.ForceNextDraw(double.NaN, 1, false);
            Assert.AreEqual(0.0, random.NextUnit(), 1e-12);
        }

        [Test]
        public void LockKeepsReturningTheSameValueUntilCleared()
        {
            var random = new RunRandom(999);
            random.LockDraw(0.25, true);
            for (int i = 0; i < 5; i++) Assert.AreEqual(0.25, random.NextUnit(), 1e-12);
            Assert.IsTrue(random.DebugOverrideActive);

            random.ClearDebugOverrides();
            var reference = new RunRandom(999);
            for (int i = 0; i < 5; i++) reference.NextUnit();
            Assert.AreEqual(reference.NextUnit(), random.NextUnit(), 0, "清除覆盖后接回原序列");
        }

        [Test]
        public void ReplayReproducesRecordedActionsWithoutDivergence()
        {
            var cfg = DummyConfig();
            var session = new BattleLabSession(catalog, cfg);
            Assert.IsTrue(session.StartBattle(out string startError), startError);
            for (int i = 0; i < 4 && session.Active; i++)
                Assert.IsTrue(session.Step(out string note), note);

            var report = BattleLabReplay.Run(catalog, cfg, session.RecordedSteps.ToList(), session.RecordingInvalid);
            Assert.IsTrue(report.Success, report.ToString());
            Assert.IsNull(report.Divergence);
            Assert.AreEqual(session.RecordedSteps.Count, report.StepsApplied);
        }

        [Test]
        public void ReplayReportsTheFirstDivergence()
        {
            var cfg = DummyConfig();
            var session = new BattleLabSession(catalog, cfg);
            Assert.IsTrue(session.StartBattle(out string startError), startError);
            Assert.IsTrue(session.Step(out string stepError), stepError);

            var steps = session.RecordedSteps.ToList();
            Assert.IsNotEmpty(steps);
            steps[0].RandomAfter ^= 1u;

            var report = BattleLabReplay.Run(catalog, cfg, steps, false);
            Assert.IsFalse(report.Success);
            StringAssert.Contains("RNG", report.Divergence);
        }

        [Test]
        public void ReplayFileJsonRoundTrips()
        {
            var cfg = DummyConfig();
            var session = new BattleLabSession(catalog, cfg);
            Assert.IsTrue(session.StartBattle(out _));
            Assert.IsTrue(session.Step(out _));

            var file = new BattleLabReplayFile
            {
                ConfigJson = BattleLabJson.ToJson(cfg),
                Steps = session.RecordedSteps.ToList()
            };
            var parsed = BattleLabReplay.FromJson(BattleLabReplay.ToJson(file));
            Assert.NotNull(parsed);
            Assert.AreEqual(file.Steps.Count, parsed.Steps.Count);
            Assert.AreEqual(file.Steps[0].Kind, parsed.Steps[0].Kind);
            Assert.AreEqual(file.Steps[0].RandomBefore, parsed.Steps[0].RandomBefore);
        }

        [Test]
        public void FuzzRunsSeveralIndependentBattlesWithoutFailures()
        {
            var settings = new BattleLabFuzzSettings
            {
                Battles = 8,
                MaxSteps = 240,
                Seed = 20261017,
                RandomizeStats = true,
                RandomizeEquipment = true,
                RandomizeRules = false,
                StopOnFailure = false
            };
            var progress = BattleLabFuzz.Run(catalog, settings);
            Assert.AreEqual(0, progress.Failed, progress.ReportText());
            Assert.AreEqual(8, progress.Completed);
        }
    }
}
