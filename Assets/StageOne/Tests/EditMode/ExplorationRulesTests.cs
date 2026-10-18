using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 房间内容：开局增益、敌人缩放、补给站治疗与义肢掉落，以及 EventScripts 里十条事件的流程。
    // 这些规则住在 NodeContent.cs 与 EventScripts.cs，不碰战斗模型。
    public class ExplorationRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private MapRules Rules() { return catalog.MapRules(); }
        // The preset only seeds each hero's own sanity. The shared player sanity is a separate field and is
        // what the map and every event gate reads, so it is set here too — otherwise a "low sanity" run
        // still reads 50 and every sanity branch quietly takes the high road.
        private RunState Run(double sanity = 50, uint seed = 44972)
        {
            var run = RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity, catalog.Rules());
            run.PlayerSanity = sanity;
            return run;
        }

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

        // 把房间钉在某一条事件上，其余步骤仍然走真实的代码路径。
        private NodeState Room(string eventId)
        {
            var node = new NodeState { Type = eventId == "EVT-008" ? NodeType.Altar : NodeType.Event };
            EventScripts.Roll(node, Run(50), Rules(), eventId);
            return node;
        }

        private bool Pick(NodeState node, int index, RunState run, out EventSession session)
        {
            return EventScripts.Choose(node, index, run, Rules(), out session);
        }

        [Test] public void TheSanityGatesKeepTheTwoSpecialEventsInTheirOwnHalf()
        {
            Assert.AreEqual(8, EventScripts.Pool(NodeType.Event, Run(80)).Count, "高理智的事件池含 EVT-H001");
            Assert.That(EventScripts.Pool(NodeType.Event, Run(80)), Does.Contain("EVT-H001"));
            Assert.That(EventScripts.Pool(NodeType.Event, Run(80)), Does.Not.Contain("EVT-L001"));

            Assert.AreEqual(8, EventScripts.Pool(NodeType.Event, Run(20)).Count, "低理智的事件池含 EVT-L001");
            Assert.That(EventScripts.Pool(NodeType.Event, Run(20)), Does.Contain("EVT-L001"));
            Assert.That(EventScripts.Pool(NodeType.Event, Run(20)), Does.Not.Contain("EVT-H001"));

            Assert.AreEqual(1, EventScripts.Pool(NodeType.Altar, Run(50)).Count);
            Assert.AreEqual(0, EventScripts.Pool(NodeType.Battle, Run(50)).Count, "战斗房间没有内容池");
        }

        [Test] public void RollingPinsTheEventAndTheStepDrawsNothingAfterwards()
        {
            var rules = Rules();
            var run = Run(50);
            var node = new NodeState { Type = NodeType.Event };
            EventScripts.Roll(node, run, rules);

            Assert.NotNull(node.EventId);
            Assert.AreEqual(0, node.EventStep);
            Assert.AreEqual(-1, node.OptionChosen);

            // 页面是纯读：反复渲染既不动随机序列，也不改节点上的任何进度。
            uint state = run.Random.State;
            int step = node.EventStep;
            var first = EventScripts.Page(node, run, rules);
            var second = EventScripts.Page(node, run, rules);
            Assert.AreEqual(first.Title, second.Title);
            Assert.AreEqual(first.Choices.Count, second.Choices.Count);
            Assert.AreEqual(state, run.Random.State, "渲染页面不抽随机");
            Assert.AreEqual(step, node.EventStep, "渲染页面不改进度");
        }

        [Test] public void AnOutOfRangeOrUnansweredOptionIsRefused()
        {
            var node = Room("EVT-002");
            var run = Run(50);
            EventSession session;

            Assert.IsFalse(Pick(node, -1, run, out session));
            Assert.IsFalse(Pick(node, 99, run, out session));
            Assert.IsFalse(EventScripts.Choose(null, 0, run, Rules(), out session));
            Assert.AreEqual(-1, node.OptionChosen);

            Assert.IsTrue(Pick(node, 1, run, out session), "「远离」永远可选");
            Assert.IsTrue(EventScripts.CanLeave(node), "单步事件选完就结束");
            Assert.IsFalse(Pick(node, 1, run, out session), "结束之后不能再选");
        }

        [Test] public void TheAbandonedMineDrainsSanityUntilTheCapThenFights()
        {
            // 矿道在进房时就发矿石，所以这里不能用那个扔掉 run 的辅助，得让 Roll 落在被测的 run 上。
            var run = Run(50);
            var node = new NodeState { Type = NodeType.Event };
            EventScripts.Roll(node, run, Rules(), "EVT-001");
            Assert.AreEqual(3, run.Map.Supplies, "进矿道先拿到 3 点矿石");
            Assert.That(node.EventLimit, Is.InRange(2, 5), "挖矿上限落在文档给的 2..5");

            double sanity = run.Heroes[0].Sanity;
            EventSession session = null;
            for (int i = 0; i < node.EventLimit; i++)
            {
                Assert.IsTrue(Pick(node, 0, run, out session), "第 " + (i + 1) + " 次挖矿应该可以选");
                if (i < node.EventLimit - 1) Assert.IsFalse(session.WantsBattle, "只有最后一次才触发战斗");
            }

            Assert.IsTrue(session.WantsBattle, "挖到上限会引来战斗");
            Assert.AreEqual(NodeType.Battle, session.BattleKind);
            Assert.AreEqual(3 + 4 * node.EventLimit, run.Map.Supplies, "每次挖矿 +4 矿石");
            Assert.AreEqual(sanity - 3 * node.EventLimit, run.Heroes[0].Sanity, 0.001, "每次挖矿全队理智 -3");
            Assert.IsTrue(EventScripts.CanLeave(node));
        }

        [Test] public void TheMedicineChestIsOnlyForALowSanityParty()
        {
            var low = Room("EVT-004");
            var lowRun = Run(10);
            var page = EventScripts.Page(low, lowRun, Rules());
            Assert.AreEqual(4, page.Choices.Count);
            Assert.IsTrue(page.Choices[2].Shown, "低理智才看得到「全部喝下」");

            var high = Room("EVT-004");
            var highRun = Run(90);
            Assert.IsFalse(EventScripts.Page(high, highRun, Rules()).Choices[2].Shown);
            EventSession refused;
            Assert.IsFalse(Pick(high, 2, highRun, out refused), "看不见的选项选不了");
            Assert.AreEqual(-1, high.OptionChosen, "被拒绝的选项不改动任何状态");
        }

        [Test] public void TheBlueAndRedPotionsSetTheChosenMembersSanity()
        {
            var blue = Room("EVT-004");
            var run = Run(50);
            var hero = run.Heroes[0];
            hero.Sanity = 60;
            EventSession session;
            Assert.IsTrue(Pick(blue, 0, run, out session), "喝蓝药剂");
            Assert.AreEqual(1, blue.EventStep, "进入选人子步骤");
            Assert.IsTrue(Pick(blue, 0, run, out session), "选中第一名队员");
            Assert.AreEqual(0, hero.Sanity);
            Assert.IsTrue(EventScripts.CanLeave(blue));

            var red = Room("EVT-004");
            var run2 = Run(50);
            run2.Heroes[0].Sanity = 40;
            Assert.IsTrue(Pick(red, 1, run2, out session), "喝红药剂");
            Assert.IsTrue(Pick(red, 0, run2, out session));
            Assert.AreEqual(100, run2.Heroes[0].Sanity);
        }

        [Test] public void DrinkingEverythingIsAThirtyPercentGambleThatRewiresTheHero()
        {
            // 30/70 是文档直接给的：跨种子统计成功率，别只看一个种子。
            int unlocked = 0;
            const int seeds = 400;
            for (uint seed = 0; seed < seeds; seed++)
            {
                var node = new NodeState { Type = NodeType.Event };
                var run = RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, 10, catalog.Rules());
                run.PlayerSanity = 10;
                EventScripts.Roll(node, run, Rules(), "EVT-004");
                EventSession session;
                var hero = run.Heroes[0];
                if (!Pick(node, 2, run, out session)) continue;
                if (!Pick(node, 0, run, out session)) continue;
                if (hero.SanitySkillsOnly) unlocked++;
            }
            Assert.That(unlocked, Is.GreaterThan(seeds * 0.20), "成功率不该明显低于 30%");
            Assert.That(unlocked, Is.LessThan(seeds * 0.40), "成功率不该明显高于 30%");
        }

        [Test] public void TheHiddenForkBuysAProstheticAndAnElevatorPassage()
        {
            var node = Room("EVT-H001");
            var run = Run(80);
            EventSession session;
            Assert.IsTrue(Pick(node, 1, run, out session), "探索岔道");
            Assert.IsTrue(session.WantsBattle);
            Assert.AreEqual(NodeType.EliteBattle, session.BattleKind, "岔道的守备是精英");
            Assert.IsFalse(EventScripts.CanLeave(node), "打完才轮到通道的选项");

            int before = run.AccessoryStock.Count;
            EventScripts.ResolveBattleReturn(node, run, Rules(), true);
            Assert.AreEqual(before + 1, run.AccessoryStock.Count, "胜利才发饰品");
            Assert.IsTrue(EventScripts.CanLeave(node));
            Assert.IsTrue(Pick(node, 0, run, out session), "进入通道");
            Assert.IsTrue(node.EventJump, "通道直达本层电梯井");
        }

        [Test] public void TheFifthPersonOnlyShowsUpAtLowSanityAndRecordsItsPlaceholders()
        {
            var node = Room("EVT-L001");
            var run = Run(20);
            EventSession session;
            Assert.IsTrue(Pick(node, 0, run, out session), "无视第五个人");
            Assert.IsTrue(run.Map.NextBattleBonusActions, "占位：下次战斗行动数 +1");
            Assert.IsTrue(EventScripts.CanLeave(node));

            var fought = Room("EVT-L001");
            var run2 = Run(20);
            int before = run2.AccessoryStock.Count;
            Assert.IsTrue(Pick(fought, 1, run2, out session), "查清状况");
            Assert.IsTrue(session.WantsBattle);
            EventScripts.ResolveBattleReturn(fought, run2, Rules(), true);
            Assert.AreEqual(before + 1, run2.AccessoryStock.Count);
            Assert.IsTrue(run2.Map.BattleNodeBonus, "占位：后续战斗节点概率上升");
        }

        [Test] public void ARetreatFromAnEventFightPaysNothing()
        {
            var node = Room("EVT-003");
            var run = Run(20);
            EventSession session;
            Assert.IsTrue(Pick(node, 0, run, out session), "靠近对话");
            Assert.AreEqual(1, node.EventStep);
            Assert.IsTrue(Pick(node, 0, run, out session), "开门");
            Assert.IsTrue(session.WantsBattle, "低理智开门会打起来");
            Assert.IsFalse(EventScripts.CanLeave(node), "战斗还没结算");

            int supplies = run.Map.Supplies;
            int stock = run.AccessoryStock.Count;
            EventScripts.ResolveBattleReturn(node, run, Rules(), false);
            Assert.AreEqual(supplies, run.Map.Supplies, "撤退不发补给");
            Assert.AreEqual(stock, run.AccessoryStock.Count, "撤退不发饰品");
            Assert.AreEqual(2, node.EventFlag, "记下这是一次撤退");
            Assert.IsTrue(EventScripts.CanLeave(node));
        }

        [Test] public void TheRestEventScramblesRanksOnlyOnTheLowSanityBranch()
        {
            var low = Room("EVT-006");
            var lowRun = Run(20);
            var ranksBefore = lowRun.Heroes.Select(h => h.Rank).OrderBy(r => r).ToArray();
            EventSession session;
            Assert.IsTrue(Pick(low, 0, lowRun, out session), "选守夜者");
            Assert.IsTrue(session.WantsBattle, "休整都会以战斗收尾");
            Assert.That(lowRun.Heroes.Select(h => h.Rank).OrderBy(r => r).ToArray(), Is.EqualTo(ranksBefore),
                "打乱的是站位，不是队伍成员");

            var high = Room("EVT-006");
            var highRun = Run(80);
            var highRanks = highRun.Heroes.Select(h => h.Rank).ToArray();
            Assert.IsTrue(Pick(high, 0, highRun, out session), "高理智选守夜者");
            Assert.IsTrue(session.WantsBattle);
            Assert.That(highRun.Heroes.Select(h => h.Rank).ToArray(), Is.EqualTo(highRanks), "高理智分支不动站位");
        }

        [Test] public void TheRunawayMachineHurtsSomeoneOnTheLowSanityBranch()
        {
            var high = Room("EVT-002");
            var highRun = Run(80);
            var highHp = highRun.Heroes.Select(h => h.Hp).ToArray();
            EventSession session;
            Assert.IsTrue(Pick(high, 0, highRun, out session), "专业修理");
            Assert.That(highRun.Heroes.Select(h => h.Hp).ToArray(), Is.EqualTo(highHp), "高理智修理不伤人");
            Assert.AreEqual(1, highRun.Prosthetics.Count, "两种手法都给一件义体");

            var low = Room("EVT-002");
            var lowRun = Run(20);
            var lowHp = lowRun.Heroes.Select(h => h.Hp).ToArray();
            Assert.IsTrue(Pick(low, 0, lowRun, out session), "暴力拆解");
            Assert.That(lowRun.Heroes.Select(h => h.Hp).ToArray(), Is.Not.EqualTo(lowHp), "低理智会伤到一名队员");
            Assert.That(lowRun.Heroes.All(h => h.Hp >= 1), "事件伤害不会在战斗之外打死人");
            Assert.AreEqual(1, lowRun.Prosthetics.Count);
        }

        [Test] public void TheInfirmaryHealsFirstThenHandsOutWhateverItsSanityFound()
        {
            var node = Room("EVT-005");
            var run = Run(80);
            run.Heroes[0].Hp = 1;
            EventSession session;
            Assert.IsTrue(Pick(node, 0, run, out session), "选第一名队员治疗");
            Assert.That(run.Heroes[0].Hp, Is.GreaterThan(1), "先恢复 40% 生命");
            Assert.AreEqual(1, node.EventStep);

            int stock = run.Prosthetics.Count;
            Assert.IsTrue(Pick(node, 0, run, out session), "高理智看到的是义体");
            Assert.AreEqual(stock + 1, run.Prosthetics.Count);

            var low = Room("EVT-005");
            var lowRun = Run(20);
            double sanity = lowRun.PlayerSanity;
            Assert.IsTrue(Pick(low, 0, lowRun, out session));
            Assert.IsTrue(Pick(low, 0, lowRun, out session), "低理智看到的是稳定剂");
            Assert.That(lowRun.PlayerSanity, Is.GreaterThan(sanity), "稳定剂抬的是玩家理智");
        }

        [Test] public void TheStoryEventChangesNothingAtAll()
        {
            var node = Room("EVT-007");
            var run = Run(50);
            run.Map.Supplies = 7;
            var sanity = run.Heroes.Select(h => h.Sanity).ToArray();
            EventSession session;
            Assert.AreEqual(1, EventScripts.Page(node, run, Rules()).Choices.Count, "只描述剧情，只有一个继续");
            Assert.IsTrue(Pick(node, 0, run, out session));
            Assert.AreEqual(7, run.Map.Supplies);
            Assert.That(run.Heroes.Select(h => h.Sanity).ToArray(), Is.EqualTo(sanity));
            Assert.IsFalse(session.WantsBattle);
        }

        [Test] public void TheWorkshopTradesSanityForSupplies()
        {
            var node = Room("EVT-008");
            var run = Run(50);
            double sanity = run.Heroes[0].Sanity;
            EventSession session;
            Assert.IsTrue(Pick(node, 1, run, out session), "议价");
            Assert.AreEqual(NodeType.Altar, node.Type, "祭坛房只会抽到祭坛内容");
            Assert.AreEqual(4, run.Map.Supplies);
            Assert.That(run.Heroes[0].Sanity, Is.LessThan(sanity), "议价要付出理智");

            var calm = Room("EVT-008");
            var calmRun = Run(50);
            calmRun.Heroes[0].Sanity = 40;
            Assert.IsTrue(Pick(calm, 2, calmRun, out session), "适配方案");
            Assert.That(calmRun.Heroes[0].Sanity, Is.GreaterThan(40), "适配方案抬的是全队理智");
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
