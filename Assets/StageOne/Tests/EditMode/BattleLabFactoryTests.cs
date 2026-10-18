using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // Battle Lab 的配置/工厂层：克隆隔离、站位、重复 Id、义肢与饰品应用。
    public class BattleLabFactoryTests
    {
        private GameCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog");
            Assert.NotNull(catalog, "Generate catalog before running tests");
        }

        private BattleLabConfig Defaults()
        {
            var cfg = BattleLabFactory.CreateDefault(catalog);
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                cfg.Heroes[i].Enabled = i == 0;
                cfg.Enemies[i].Enabled = false;
                cfg.Enemies[i].StartRank = -1;
            }
            cfg.Heroes[0].AccessoryIds.Clear();
            cfg.Heroes[0].ConsumableCounts = new int[ConsumableSystem.Defaults.Length];
            cfg.Heroes[0].Limbs = new[] { LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact };
            cfg.Heroes[0].ProstheticIds = new string[4];
            cfg.Heroes[0].ForceAccessories = false;
            cfg.Normalize();
            return cfg;
        }

        private RunState Build(BattleLabConfig cfg)
        {
            Assert.IsTrue(BattleLabFactory.TryBuildRun(cfg, catalog, out RunState run, out List<string> errors),
                string.Join("\n", errors));
            return run;
        }

        [Test]
        public void CloneDoesNotWriteBackToTheCatalog()
        {
            var source = catalog.Heroes[0].ToSpec();
            int originalHp = source.MaxHp;
            int originalAttack = source.Attack;

            var copy = BattleLabFactory.Clone(source);
            copy.MaxHp = 1;
            copy.Attack = 999;
            var clonedSkill = copy.BasicSkills.FirstOrDefault();
            if (clonedSkill != null) clonedSkill.Multiplier = 42;

            Assert.AreEqual(originalHp, catalog.Heroes[0].ToSpec().MaxHp, "克隆不能污染角色资产");
            Assert.AreEqual(originalAttack, catalog.Heroes[0].ToSpec().Attack);
            var originalSkill = catalog.Heroes[0].ToSpec().BasicSkills.FirstOrDefault();
            if (originalSkill != null) Assert.AreNotEqual(42, originalSkill.Multiplier);
            if (clonedSkill != null) Assert.AreNotSame(source.BasicSkills[0], copy.BasicSkills[0], "技能也必须深拷贝");
        }

        [Test]
        public void DuplicateHeroIdsAreSuffixed()
        {
            var cfg = Defaults();
            cfg.Heroes[1].Enabled = true;
            BattleLabFactory.ApplyHeroSource(cfg.Heroes[1], catalog.Heroes[0]);
            cfg.Normalize();

            var run = Build(cfg);
            Assert.AreEqual(2, run.Heroes.Count);
            Assert.AreEqual(2, run.Heroes.Select(h => h.Id).Distinct().Count(), "BattleEngine 不接受重复英雄 Id");
            StringAssert.EndsWith("_2", run.Heroes[1].Id, "第二个同名英雄自动加后缀");
        }

        [Test]
        public void ExplicitHeroRanksAreAppliedInSlotOrder()
        {
            var cfg = Defaults();
            cfg.Heroes[0].Rank = 3;
            cfg.Heroes[1].Enabled = true;
            BattleLabFactory.ApplyHeroSource(cfg.Heroes[1], catalog.Heroes[1]);
            cfg.Heroes[1].Rank = 1;
            cfg.Normalize();

            var run = Build(cfg);
            Assert.AreEqual(3, run.Heroes[0].Rank);
            Assert.AreEqual(1, run.Heroes[1].Rank);
        }

        [Test]
        public void AutoHeroRanksFillTheLowestFreeSlots()
        {
            var cfg = Defaults();
            cfg.Heroes[0].Rank = 2;
            cfg.Heroes[1].Enabled = true;
            BattleLabFactory.ApplyHeroSource(cfg.Heroes[1], catalog.Heroes[1]);
            cfg.Heroes[1].Rank = -1;
            cfg.Normalize();

            var run = Build(cfg);
            Assert.AreEqual(2, run.Heroes[0].Rank);
            Assert.AreEqual(0, run.Heroes[1].Rank, "自动站位从最低空位开始");
        }

        [Test]
        public void EnemyRanksHonorStartRankAndWidth()
        {
            var cfg = Defaults();
            cfg.Enemies[0].Enabled = true;
            cfg.Enemies[0].SourceId = "lab_dummy";
            cfg.Enemies[0].Spec = BattleLabFactory.DummySpec();
            cfg.Enemies[0].Spec.RankWidth = 2;
            cfg.Enemies[0].StartRank = 0;
            cfg.Enemies[1].Enabled = true;
            cfg.Enemies[1].Spec = BattleLabFactory.DummySpec();
            cfg.Enemies[1].Spec.Id = "dummy_2";
            cfg.Enemies[1].StartRank = 2;
            cfg.Normalize();

            var errors = BattleLabFactory.Validate(cfg, catalog);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            var specs = BattleLabFactory.BuildEnemySpecs(cfg, out List<string> buildErrors);
            Assert.IsEmpty(buildErrors);
            Assert.AreEqual(2, specs.Count);
            Assert.AreEqual(0, specs[0].StartRank);
            Assert.AreEqual(2, specs[1].StartRank);
        }

        [Test]
        public void OverlappingEnemyRanksAreRejected()
        {
            var cfg = Defaults();
            cfg.Enemies[0].Enabled = true;
            cfg.Enemies[0].Spec = BattleLabFactory.DummySpec();
            cfg.Enemies[0].Spec.RankWidth = 2;
            cfg.Enemies[0].StartRank = 0;
            cfg.Enemies[1].Enabled = true;
            cfg.Enemies[1].Spec = BattleLabFactory.DummySpec();
            cfg.Enemies[1].Spec.Id = "dummy_2";
            cfg.Enemies[1].StartRank = 1;
            cfg.Normalize();

            var errors = BattleLabFactory.Validate(cfg, catalog);
            Assert.That(errors.Any(e => e.Contains("重叠")), Is.True, "重叠站位必须被拒绝：" + string.Join("\n", errors));
        }

        [Test]
        public void ProstheticRequiresASeveredLimb()
        {
            var cfg = Defaults();
            cfg.Heroes[0].ProstheticIds[(int)LimbPart.Arm] = "prosthetic_arm";
            cfg.Heroes[0].Limbs[(int)LimbPart.Arm] = LabLimbState.Intact;
            var errors = BattleLabFactory.Validate(cfg, catalog);
            Assert.That(errors.Any(e => e.Contains("未断肢")), Is.True, string.Join("\n", errors));

            cfg.Heroes[0].Limbs[(int)LimbPart.Arm] = LabLimbState.Severed;
            var run = Build(cfg);
            Assert.AreEqual("prosthetic_arm", LimbSystem.Get(run.Heroes[0], LimbPart.Arm).Prosthetic);
        }

        [Test]
        public void AccessoriesAndConsumablesAreAppliedToTheRun()
        {
            var cfg = Defaults();
            cfg.Heroes[0].ForceAccessories = true;
            cfg.Heroes[0].AccessoryIds.Add("acc_apple");
            cfg.Heroes[0].ConsumableCounts[(int)ConsumableKind.HpRestore] = 2;
            int baseMaxHp = cfg.Heroes[0].Spec.MaxHp;

            var run = Build(cfg);
            var hero = run.Heroes[0];
            Assert.That(hero.EquippedAccessories.Any(a => a != null && a.Id == "acc_apple"), Is.True);
            Assert.AreEqual(baseMaxHp + AccessorySystem.AppleMaxHpBonus, hero.Definition.MaxHp, "金丝苹果 +10 MaxHp");
            Assert.AreEqual(hero.Definition.MaxHp, hero.Hp, "满血开局使用应用饰品后的最大值");
            Assert.AreEqual(2, run.Consumables.Count(c => c != null && c.Kind == ConsumableKind.HpRestore));
        }

        [Test]
        public void ConfigJsonRoundTripKeepsIdentityAndEquipment()
        {
            var cfg = Defaults();
            cfg.SeedText = "0xDEADBEEF";
            cfg.Heroes[0].Sanity = 73.5;
            cfg.Heroes[0].AccessoryIds.Add("acc_apple");
            cfg.Heroes[0].ForceAccessories = true;
            cfg.Heroes[0].ConsumableCounts[0] = 3;
            cfg.Normalize();

            string json = BattleLabJson.ToJson(cfg);
            var imported = BattleLabJson.ImportConfig(json, catalog, out List<string> errors);
            Assert.NotNull(imported);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual("0xDEADBEEF", imported.SeedText);
            Assert.AreEqual(73.5, imported.Heroes[0].Sanity, 0.0001);
            Assert.That(imported.Heroes[0].AccessoryIds, Does.Contain("acc_apple"));
            Assert.AreEqual(3, imported.Heroes[0].ConsumableCounts[0]);
            Assert.IsEmpty(BattleLabFactory.Validate(imported, catalog));
        }

        [Test]
        public void InvalidSeedAndUnknownAccessoryAreRejected()
        {
            var cfg = Defaults();
            cfg.SeedText = "not-a-seed";
            cfg.Heroes[0].AccessoryIds.Add("acc_missing");
            var errors = BattleLabFactory.Validate(cfg, catalog);
            Assert.That(errors.Any(e => e.Contains("种子")), Is.True);
            Assert.That(errors.Any(e => e.Contains("acc_missing")), Is.True);
            Assert.IsFalse(BattleLabFactory.TryBuildRun(cfg, catalog, out _, out _));
        }
    }
}
