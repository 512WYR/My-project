using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 房间内容：开局增益、敌人缩放、事件与祭坛的选项可见性、补给站治疗与义肢掉落。
    // 这些规则全部住在 NodeContent.cs，不碰战斗模型。
    public class ExplorationRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private MapRules Rules() { return catalog.MapRules(); }
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity, catalog.Rules()); }

        // ---------------- 开局增益 ----------------

        [Test] public void ToughnessRaisesMaxHealthAndLeavesTheCatalogAlone()
        {
            var before = catalog.Heroes.Select(h => h.ToSpec()).ToArray();
            var maxBefore = before.Select(s => s.MaxHp).ToArray();
            var run = Run(50);

            // 目录交出的每份 spec 都是全新副本，增益只能写在这份副本上。
            OpeningBuffs.Apply(GrowthBuff.Toughness, run, catalog.Rules().Limbs);
            for (int i = 0; i < run.Heroes.Count; i++)
                Assert.AreEqual(maxBefore[i] + OpeningBuffs.ToughnessHealth, run.Heroes[i].Definition.MaxHp, "全队最大生命 +8");
            Assert.That(catalog.Heroes.Select(h => h.ToSpec()).Select(s => s.MaxHp), Is.EqualTo(maxBefore),
                "增益不会写回角色资产");
            Assert.That(catalog.Heroes.Select(h => h.ToSpec()).Select(s => s.Attack),
                Is.EqualTo(before.Select(s => s.Attack)), "增益不改动目录里的攻击力");
        }

        [Test] public void ToughnessRebuildsTheLimbCapsAroundTheNewHealth()
        {
            var run = Run(50);
            var torsoBefore = run.Heroes.Select(h => LimbSystem.Get(h, LimbPart.Torso).MaxHp).ToArray();
            OpeningBuffs.Apply(GrowthBuff.Toughness, run, catalog.Rules().Limbs);
            for (int i = 0; i < run.Heroes.Count; i++)
            {
                var torso = LimbSystem.Get(run.Heroes[i], LimbPart.Torso);
                Assert.That(torso.MaxHp, Is.GreaterThan(torsoBefore[i]), "部位上限由最大生命派生，跟着增益一起变");
                Assert.AreEqual(torso.MaxHp, torso.Hp, "增益房在路线最前面，这时队伍是满状态");
            }
        }

        [Test] public void SharpnessRaisesAttackAndClarityRaisesSanity()
        {
            var sharp = Run(50);
            var attackBefore = sharp.Heroes.Select(h => h.Definition.Attack).ToArray();
            OpeningBuffs.Apply(GrowthBuff.Sharpness, sharp, catalog.Rules().Limbs);
            for (int i = 0; i < sharp.Heroes.Count; i++)
                Assert.AreEqual(attackBefore[i] + OpeningBuffs.SharpnessAttack, sharp.Heroes[i].Definition.Attack, "全队基础攻击 +2");
            Assert.That(sharp.Heroes.Select(h => h.Definition.MaxHp),
                Is.EqualTo(Run(50).Heroes.Select(h => h.Definition.MaxHp)), "锋锐不动生命");

            var clear = Run(95);
            OpeningBuffs.Apply(GrowthBuff.Clarity, clear, catalog.Rules().Limbs);
            Assert.That(clear.Heroes.All(h => h.Sanity == 100), "清明加的理智夹在 0..100 之内");

            var mid = Run(50);
            OpeningBuffs.Apply(GrowthBuff.Clarity, mid, catalog.Rules().Limbs);
            Assert.That(mid.Heroes.All(h => h.Sanity == 50 + OpeningBuffs.ClaritySanity), "清明给全队加 10 点理智");

            var none = Run(50);
            OpeningBuffs.Apply(GrowthBuff.None, none, catalog.Rules().Limbs);
            Assert.That(none.Heroes.All(h => h.Sanity == 50 && h.Definition.MaxHp > 0), "没有增益就什么都不改");
        }

        [Test] public void TheOfferedBuffsAreTheDocumentedThree()
        {
            var buffs = MapGenerator.Resolve(Rules()).OpeningBuffs;
            Assert.That(buffs, Is.EquivalentTo(new[] { GrowthBuff.Toughness, GrowthBuff.Sharpness, GrowthBuff.Clarity }));
            Assert.AreEqual("坚韧", OpeningBuffs.Name(GrowthBuff.Toughness));
            Assert.AreEqual("全队最大生命 +" + OpeningBuffs.ToughnessHealth, OpeningBuffs.Describe(GrowthBuff.Toughness));
            Assert.AreEqual("全队初始理智 +" + OpeningBuffs.ClaritySanity, OpeningBuffs.Describe(GrowthBuff.Clarity));
        }

        // ---------------- 敌人缩放 ----------------

        private static ActorSpec Foe(string id, int hp, int attack)
        {
            var skill = new SkillSpec { Id = "swing", Name = "挥砍", Effect = SkillEffect.Attack, Target = TargetSide.Enemy, Multiplier = 1.0 };
            return new ActorSpec { Id = id, Name = id, MaxHp = hp, Attack = attack, Speed = 5,
                BasicSkills = new[] { skill }, PrimalSkills = new[] { skill }, TechnicalSkills = new[] { skill } };
        }

        [Test] public void EliteAndBossAreStrictlyStrongerAndNeverWriteBack()
        {
            var source = new[] { Foe("cleaver", 32, 10) };
            var elite = Encounter.Scale(source, NodeType.EliteBattle, Rules());
            var boss = Encounter.Scale(source, NodeType.Boss, Rules());
            var plain = Encounter.Scale(source, NodeType.Battle, Rules());

            Assert.AreEqual(32, plain[0].MaxHp);
            Assert.AreEqual(10, plain[0].Attack);
            Assert.AreEqual("cleaver", plain[0].Name, "普通战斗原样放行");
            Assert.That(elite[0].MaxHp, Is.GreaterThan(plain[0].MaxHp));
            Assert.That(elite[0].Attack, Is.GreaterThan(plain[0].Attack));
            Assert.That(boss[0].MaxHp, Is.GreaterThan(elite[0].MaxHp), "首领比精英更强");
            Assert.That(boss[0].Attack, Is.GreaterThan(elite[0].Attack));
            Assert.AreEqual("精英·cleaver", elite[0].Name);
            Assert.AreEqual("首领·cleaver", boss[0].Name);

            Assert.AreEqual(32, source[0].MaxHp, "缩放绝不写回源 spec");
            Assert.AreEqual(10, source[0].Attack);
            Assert.AreEqual("cleaver", source[0].Name);
            Assert.AreNotSame(source[0], elite[0], "缩放交出的是一份新副本");
            Assert.AreSame(source[0].BasicSkills[0], elite[0].BasicSkills[0], "技能引用保持共享，不复制");
        }

        [Test] public void SuppliesOnlyComeFromWonFights()
        {
            var rules = Rules();
            Assert.AreEqual(2, Encounter.SuppliesFor(rules, NodeType.Battle));
            Assert.AreEqual(4, Encounter.SuppliesFor(rules, NodeType.EliteBattle));
            Assert.AreEqual(8, Encounter.SuppliesFor(rules, NodeType.Boss));
            Assert.AreEqual(0, Encounter.SuppliesFor(rules, NodeType.Event));
            Assert.AreEqual(0, Encounter.SuppliesFor(rules, NodeType.Supply));
        }

        [Test] public void AnEliteDropIsAFreshCopyOfTheSharedTable()
        {
            var random = new RunRandom(11);
            var drop = Encounter.DropProsthetic(catalog.Rules().Limbs, random);
            Assert.NotNull(drop, "精英战斗必须掉得出一件义肢");
            var table = LimbSystem.Resolve(catalog.Rules().Limbs).Prosthetics;
            Assert.That(table.Any(p => p.Id == drop.Id && p.Part == drop.Part), "掉落来自义肢表");
            Assert.That(table.All(p => !ReferenceEquals(p, drop)), "掉落是副本，不会污染共享表");
        }

        // ---------------- 事件与祭坛 ----------------

        [Test] public void TheDefaultPoolsHoldTwoEventsAndTwoAltars()
        {
            var rules = Rules();
            Assert.AreEqual(2, Exploration.Pool(rules, NodeType.Event).Count);
            Assert.AreEqual(2, Exploration.Pool(rules, NodeType.Altar).Count);
            Assert.AreEqual(0, Exploration.Pool(rules, NodeType.Battle).Count, "战斗房间没有内容池");
            Assert.That(Exploration.Pool(rules, NodeType.Event).All(e => e.Options.Length == 3), "每条内容都有三个选项");
            Assert.That(Exploration.Pool(rules, NodeType.Altar).All(e => e.Options[0].AppearanceBaseline == 1), "基础选项恒可用");
        }

        [Test] public void RollingFreezesTheContentAndTheOptionMask()
        {
            var rules = Rules();
            var node = new NodeState { Type = NodeType.Event };
            var random = new RunRandom(7);
            Exploration.RollEvent(rules, node, random, 50, NodeType.Event);
            uint state = random.State;

            Assert.NotNull(node.EventId);
            Assert.That(node.OptionMask, Is.Not.Null);
            Assert.IsTrue(node.OptionMask[0], "基础选项永远在");
            Assert.AreEqual(-1, node.OptionChosen);

            // 再渲染一次页面不会重抽：内容与掩码都冻在节点上。
            Assert.NotNull(Exploration.Find(rules, node.EventId));
            Assert.AreEqual(state, random.State, "抽完就不动随机序列");
        }

        [Test] public void ALowSanityPartySeesTheDangerOptionsMoreOften()
        {
            // 单个种子说明不了问题：跨种子统计可见性才站得住。
            var rules = Rules();
            const int seeds = 400;
            int lowDanger = 0, highDanger = 0, lowLucid = 0, highLucid = 0;
            for (uint seed = 0; seed < seeds; seed++)
            {
                var low = new NodeState { Type = NodeType.Altar };
                Exploration.RollEvent(rules, low, new RunRandom(seed), 15, NodeType.Altar);
                var high = new NodeState { Type = NodeType.Altar };
                Exploration.RollEvent(rules, high, new RunRandom(seed), 85, NodeType.Altar);

                lowDanger += CountVisible(rules, low, -1);
                highDanger += CountVisible(rules, high, -1);
                lowLucid += CountVisible(rules, low, 1);
                highLucid += CountVisible(rules, high, 1);
            }
            Assert.That(lowDanger, Is.GreaterThan(highDanger), "低理智更容易看到危险的扩展选项");
            Assert.That(highLucid, Is.GreaterThan(lowLucid), "高理智更容易看到清醒的扩展选项");
        }

        // 统计出这条内容里倾向为正/负的选项被翻出来的次数。
        private static int CountVisible(MapRules rules, NodeState node, double sign)
        {
            var spec = Exploration.Find(rules, node.EventId);
            if (spec == null || node.OptionMask == null) return 0;
            int count = 0;
            for (int i = 0; i < spec.Options.Length && i < node.OptionMask.Length; i++)
                if (node.OptionMask[i] && Math.Sign(spec.Options[i].AppearanceAffinity) == Math.Sign(sign)) count++;
            return count;
        }

        [Test] public void AHiddenOptionIsRefusedAndChangesNothing()
        {
            var rules = Rules();
            var node = new NodeState { Type = NodeType.Event };
            // 先抽出内容，再把第二个选项强行藏起来，检验守卫本身。
            Exploration.RollEvent(rules, node, new RunRandom(3), 50, NodeType.Event);
            var run = Run(50);
            run.Map.Supplies = 10;
            node.OptionMask[1] = false;
            double sanity = run.PlayerSanity;
            string note;

            Assert.IsFalse(Exploration.ApplyOption(rules, node, 1, run, out note), "隐藏选项不能被选");
            Assert.AreEqual(-1, node.OptionChosen);
            Assert.AreEqual(sanity, run.PlayerSanity);
            Assert.AreEqual(10, run.Map.Supplies, "被拒绝的选项不改动任何状态");
        }

        [Test] public void AnOutOfRangeOrRepeatedOptionIsRefused()
        {
            var rules = Rules();
            var node = new NodeState { Type = NodeType.Event };
            Exploration.RollEvent(rules, node, new RunRandom(5), 50, NodeType.Event);
            var spec = Exploration.Find(rules, node.EventId);
            var run = Run(50);
            string note;

            Assert.IsFalse(Exploration.ApplyOption(rules, node, -1, run, out note));
            Assert.IsFalse(Exploration.ApplyOption(rules, node, spec.Options.Length, run, out note));
            Assert.IsFalse(Exploration.ApplyOption(rules, null, 0, run, out note));
            Assert.AreEqual(-1, node.OptionChosen);

            Assert.IsTrue(Exploration.ApplyOption(rules, node, 0, run, out note), "基础选项一定可以选");
            Assert.IsFalse(Exploration.ApplyOption(rules, node, 0, run, out note), "一个房间只能结算一次");
        }

        [Test] public void AltarOptionsTradeSanityForSuppliesOrHealing()
        {
            var rules = Rules();
            var node = new NodeState { Type = NodeType.Altar };
            Exploration.RollEvent(rules, node, new RunRandom(23), 15, NodeType.Altar);
            var spec = Exploration.Find(rules, node.EventId);
            Assert.AreEqual(NodeType.Altar, spec.Kind, "祭坛房只会抽到祭坛内容");
            // 低理智的祭坛一定有代价换收获的选项，至少基础选项要付出理智。
            Assert.That(spec.Options.Any(o => o.SuppliesDelta > 0 || o.HealPercent > 0), "祭坛内容必须有收获项");
        }

        // ---------------- 补给站 ----------------

        [Test] public void ASupplyHealRestoresAFixedShareAndNeverOverheals()
        {
            var rules = Rules();
            var hero = Run(50).Heroes[0];
            int cap = hero.Definition.MaxHp;
            int expected = (int)Math.Ceiling(cap * MapGenerator.Resolve(rules).SupplyHealRatio - 1e-9);

            hero.Hp = 1;
            Assert.AreEqual(expected, Exploration.SupplyHeal(rules, hero), "恢复量按最大生命的固定比例");
            Assert.AreEqual(1 + expected, hero.Hp);

            hero.Hp = cap - 1;
            Assert.AreEqual(1, Exploration.SupplyHeal(rules, hero), "接近满血时只补上缺的那一点");
            Assert.AreEqual(cap, hero.Hp, "治疗不会溢出上限");
        }

        [Test] public void ADeadMemberIsNeverHealed()
        {
            var run = Run(50);
            run.Heroes[1].Hp = 0;
            Assert.IsTrue(run.Heroes[1].Dead);
            Assert.AreEqual(0, Exploration.SupplyHeal(Rules(), run.Heroes[1]));
            Assert.AreEqual(0, Exploration.Heal(run.Heroes[1], 30));
            Assert.That(run.Heroes[1].Hp, Is.EqualTo(0), "阵亡队员不会在补给站复活");
        }

        [Test] public void SupplyCalmRaisesTheSharedSanityWithinBounds()
        {
            var run = Run(95);
            double before = run.PlayerSanity;
            double gained = Exploration.SupplyCalm(run);
            Assert.That(gained, Is.GreaterThan(0));
            Assert.That(run.PlayerSanity, Is.LessThanOrEqualTo(100));
            Assert.That(run.PlayerSanity, Is.EqualTo(Math.Min(100, before + Exploration.CalmSanity)).Within(1e-9));

            var flush = Run(100);
            Assert.AreEqual(0, Exploration.SupplyCalm(flush), "理智已经满了就加不动");
        }

        [Test] public void HealPartyCoversEveryLivingMember()
        {
            var run = Run(50);
            run.Heroes[0].Hp = 1;
            run.Heroes[1].Hp = 0;
            int healed = Exploration.HealParty(run, 30);
            Assert.That(healed, Is.GreaterThan(0));
            Assert.That(run.Heroes[1].Hp, Is.EqualTo(0), "阵亡队员不参与群体治疗");
            Assert.That(run.Heroes[0].Hp, Is.LessThanOrEqualTo(run.Heroes[0].Definition.MaxHp));
        }
    }
}
