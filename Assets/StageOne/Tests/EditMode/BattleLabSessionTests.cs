using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // Session 层：开战、行动、跳过、结算、实时修改与不变量。
    public class BattleLabSessionTests
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
            cfg.FullHealOnStart = true;
            return cfg;
        }

        private BattleLabSession Started(out BattleLabConfig cfg)
        {
            cfg = DummyConfig();
            var session = new BattleLabSession(catalog, cfg);
            Assert.IsTrue(session.NewRun(out string runError), runError);
            Assert.IsTrue(session.StartBattleWithCurrentRun(out string battleError), battleError);
            return session;
        }

        [Test]
        public void BattleStartsOnceAndPassesInvariants()
        {
            var session = Started(out _);
            Assert.NotNull(session.Battle);
            Assert.AreEqual(1, session.Run.Battles, "开战只增加一场");
            Assert.AreEqual(BattleOutcome.Active, session.Battle.Outcome);
            Assert.IsEmpty(session.CheckInvariants(), string.Join("\n", session.CheckInvariants()));
            Assert.IsNull(session.CheckQueryPurity(), "查询 API 不能消耗随机或改动状态");
        }

        [Test]
        public void SkipTurnAdvancesWithoutDrawingRandom()
        {
            var session = Started(out _);
            uint before = session.RandomState;
            string current = session.Current == null ? null : session.Current.Id;

            Assert.IsTrue(session.SkipTurn(out string note), note);
            Assert.AreEqual(before, session.RandomState, "跳过不抽随机");
            Assert.AreNotEqual(current, session.Current == null ? null : session.Current.Id, "跳过应推进行动者");
            Assert.IsEmpty(session.CheckInvariants(), string.Join("\n", session.CheckInvariants()));
        }

        [Test]
        public void FinishIsIdempotent()
        {
            var session = Started(out _);
            var first = session.Finish(BattleOutcome.Victory);
            Assert.NotNull(first);
            Assert.AreEqual(BattleOutcome.Victory, first.Outcome);
            Assert.IsNull(session.Current);
            Assert.IsNull(session.CheckQueryPurity());
            Assert.IsEmpty(session.CheckInvariants(), string.Join("\n", session.CheckInvariants()));

            var second = session.Finish(BattleOutcome.Defeat);
            Assert.AreSame(first, second, "结算只能发生一次");
            Assert.AreEqual(BattleOutcome.Victory, session.Battle.Outcome);
        }

        [Test]
        public void SetHpClampsAndDeathDoorIsNotDeath()
        {
            var session = Started(out _);
            var hero = session.Heroes.First();
            session.SetHp(hero, 999999, out _);
            Assert.AreEqual(hero.Definition.MaxHp, hero.Hp, "HP 上限夹在 MaxHp");

            session.SetHp(hero, 0, out _);
            Assert.AreEqual(0, hero.Hp);
            Assert.IsFalse(hero.Dead, "我方 HP<=0 进入死门，不等于死亡");
            Assert.IsTrue(hero.DeathDoor);

            session.Kill(hero, out _);
            Assert.IsTrue(hero.Dead);
        }

        [Test]
        public void KillingTheLastEnemySettlesTheBattle()
        {
            var session = Started(out _);
            Assert.IsTrue(session.Battle.Outcome == BattleOutcome.Active);
            var enemy = session.Enemies.First();
            session.Kill(enemy, out string note);
            Assert.AreEqual(BattleOutcome.Victory, session.Battle.Outcome, note);
            Assert.IsNull(session.Current);
            Assert.NotNull(session.Battle.Settlement);
            Assert.IsEmpty(session.CheckInvariants(), string.Join("\n", session.CheckInvariants()));
        }

        [Test]
        public void SanityLockBlocksChangeSanityButNotDirectAssignment()
        {
            var session = Started(out _);
            var hero = session.Heroes.First();
            hero.SanityLocked = true;
            double before = hero.Sanity;
            session.ChangeSanity(hero, -10, out _);
            Assert.AreEqual(before, hero.Sanity, "锁定的理智不能被 ChangeSanity 改动");

            session.SetSanity(hero, 20, out _);
            Assert.AreEqual(20, hero.Sanity, "SetSanity 是调试用的直接赋值");
        }

        [Test]
        public void StepMakesProgressAndKeepsStateLegal()
        {
            var session = Started(out _);
            uint before = session.RandomState;
            Assert.IsTrue(session.Step(out string note), note);
            Assert.That(session.RandomState != before || session.Current != null, "一步至少推进回合或消耗随机");
            Assert.IsEmpty(session.CheckInvariants(), string.Join("\n", session.CheckInvariants()));
            Assert.IsNull(session.CheckQueryPurity());
        }

        [Test]
        public void RestartingWithTheSameRunIncrementsBattleNumber()
        {
            var session = Started(out _);
            session.Finish(BattleOutcome.Retreat);
            Assert.IsTrue(session.StartBattleWithCurrentRun(out string error), error);
            Assert.AreEqual(2, session.Run.Battles);
            Assert.AreEqual(BattleOutcome.Active, session.Battle.Outcome);
        }

        [Test]
        public void RebootingTheRunResetsBattlesButKeepsSeed()
        {
            var session = Started(out _);
            uint seed = session.Run.Seed;
            Assert.IsTrue(session.NewRun(out string error), error);
            Assert.AreEqual(0, session.Run.Battles);
            Assert.AreEqual(seed, session.Run.Seed);
            Assert.IsNull(session.Battle);
        }
    }
}
