using System.Collections;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ruin.Tests
{
    public class StageOneFlowTests
    {
        private GameObject root;
        private StageOneApp app;
        [UnitySetUp] public IEnumerator Create()
        {
            root = new GameObject("TestApp"); app = root.AddComponent<StageOneApp>(); app.AutomationMode = true;
            yield return null; yield return null;
        }
        [UnityTearDown] public IEnumerator Destroy()
        {
            Object.Destroy(root);
            foreach (var e in Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>()) Object.Destroy(e.gameObject);
            yield return null;
        }

        // Clicks the real Button instead of calling the app method, so a broken onClick wiring
        // (a closure over a shared loop variable, for example) cannot pass the test.
        private void Click(string name)
        {
            var button = Object.FindObjectsOfType<Button>().First(b => b.name == name);
            Assert.IsTrue(button.interactable, "按钮 " + name + " 应当可以点击");
            button.onClick.Invoke();
        }

        [UnityTest] public IEnumerator SceneBuildsChineseUIAndActionTargets()
        {
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.text == "界限之间"));
            app.DebugForceRoom(NodeType.Battle); yield return null;
            Assert.NotNull(app.Battle); Assert.AreEqual(4, app.Run.Heroes.Count);
            // 行动区只有当前形态的技能加一个换位，所以按下标找第一个真的能点的技能。
            int skillIndex = Enumerable.Range(0, app.Battle.SkillsFor(app.Battle.Current).Length)
                .First(i => app.Battle.SkillDisabledReason(i) == null && app.Battle.Targets(i).Any());
            app.SelectSkill(skillIndex); yield return null;
            Assert.AreEqual(skillIndex, app.PendingSkill);
            string target = app.Battle.Targets(skillIndex).First().Id;
            app.SelectActor(target);
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;
            Assert.IsFalse(app.Busy); Assert.IsTrue(app.Battle.Events.Any(e => e.Kind == "action" || e.Kind == "miss"));
        }

        [UnityTest] public IEnumerator DebugPresetAndFeedbackContinueThroughUI()
        {
            app.NewRun(85); yield return null;
            Assert.IsTrue(app.Run.Heroes.All(h => h.Sanity == 85));
            app.DebugForceRoom(NodeType.Battle); yield return null;
            app.DebugMental(MentalState.Composed); app.DebugSetSanity(50);
            var selected = app.Battle.Find(app.InspectedId);
            app.DebugSettle(); yield return null;
            Assert.IsTrue(Object.FindObjectsOfType<Button>().Any(b => b.name == "ContinueBattle"));
            app.ContinueBattle(); yield return null;
            Assert.AreEqual(50, selected.Sanity); Assert.AreEqual(MentalState.Composed, selected.Mental);
            app.DebugFeedbackExample(); yield return null;
            Assert.AreEqual(47.5, app.Run.PlayerSanity);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "PlayerResult" && t.text.Contains("47.5")));
        }

        [UnityTest] public IEnumerator TheRouteOpensOnTheBuffRoomAndThenOffersCandidates()
        {
            var opening = app.Map.Current;
            Assert.AreEqual(1, opening.Floor);
            Assert.AreEqual(0, opening.Index);
            Assert.IsTrue(opening.Fixed, "开局是固定房间，不抽随机");
            Assert.AreEqual(NodeType.Opening, opening.Candidates[0].Type);
            Assert.IsTrue(Object.FindObjectsOfType<Button>().Any(b => b.name == "Node_0"));
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "MapOddsHeading"), "路线页要展示房间概率");
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "RouteCaption"), "路线要画成一条线");

            Click("Node_0"); yield return null;
            Assert.AreEqual(RoomView.Buff, app.Room);
            Assert.IsNull(app.Battle);
            Click("Buff_Toughness"); yield return null;
            Assert.IsTrue(app.Map.BuffChosen);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "BuffResult" && t.text.Contains("坚韧")));

            Click("LeaveRoom"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.IsTrue(app.AwaitingNode);
            var drawn = app.Map.Current;
            Assert.IsFalse(drawn.Fixed, "增益房之后是可抽位置");
            Assert.That(drawn.Candidates.Count, Is.InRange(1, 3));
            Assert.That(drawn.Candidates.All(c => c.Type == NodeType.Battle || c.Type == NodeType.EliteBattle
                || c.Type == NodeType.Altar || c.Type == NodeType.Event || c.Type == NodeType.Treasure),
                Is.True, "可抽位置只有这五种房间");
            var distinct = drawn.Candidates.Select(c => c.Type).Where(t => t != NodeType.Event).ToList();
            Assert.That(distinct.Distinct().Count(), Is.EqualTo(distinct.Count), "同一个位置不能出现两个相同的非事件房间");
            Assert.IsTrue(opening.Resolved, "走过的位置会标记为已通过");

            // 已经抽出来的候选不重抽：重新渲染既不改房间也不消耗随机序列。
            var types = drawn.Candidates.Select(n => n.Type).ToArray();
            uint state = app.Run.Random.State;
            app.Refresh(); yield return null;
            Assert.That(app.Map.Current.Candidates.Select(n => n.Type), Is.EqualTo(types));
            Assert.AreEqual(state, app.Run.Random.State);
        }

        [UnityTest] public IEnumerator ABattleResolvesOnePositionAndThenTheRunContinues()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            Assert.IsFalse(app.AwaitingNode); Assert.NotNull(app.Battle);
            app.DebugSettle(); yield return null;
            Click("ContinueBattle"); yield return null;
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.AreEqual(1, app.Map.Floor, "打赢或撤退都只往前走一个位置，不换层");
            Assert.AreEqual(1, app.Map.Current.Index);
        }

        [UnityTest] public IEnumerator TheSupplyRoomRepairsAnInjuredLimb()
        {
            app.DebugInjureLimb(LimbPart.Arm); yield return null;
            var hero = app.Run.Heroes[0];
            Assert.AreEqual(LimbCondition.Injured, LimbSystem.ConditionOf(hero, LimbPart.Arm));

            app.DebugForceRoom(NodeType.Supply); yield return null;
            Assert.AreEqual(RoomView.Supply, app.Room);
            Assert.IsNull(app.Battle);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "SupplyHeading"));

            Click("Repair_" + hero.Id + "_Arm"); yield return null;
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Arm));
            Assert.AreEqual(LimbSystem.Get(hero, LimbPart.Arm).MaxHp, LimbSystem.Get(hero, LimbPart.Arm).Hp);

            Click("SupplyHeal"); yield return null;
            Assert.IsTrue(app.Map.SupplyHealed);
            Assert.IsFalse(Object.FindObjectsOfType<Button>().First(b => b.name == "SupplyHeal").interactable, "治疗每次补给站只能用一次");

            Click("LeaveRoom"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.AreEqual(1, app.Map.Floor, "离开补给站只是继续前进，不换层");
        }

        [UnityTest] public IEnumerator TheSupplyRoomInstallsAProstheticAndConsumesItFromTheStock()
        {
            app.DebugSeverLimb(LimbPart.Arm); yield return null;
            var hero = app.Run.Heroes[0];
            Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, LimbPart.Arm));

            // 只发一件腿部义肢：手臂那一格应当提示缺少该部位的义肢。
            app.DebugGrantProsthetic(LimbPart.Leg); yield return null;
            app.DebugForceRoom(NodeType.Supply); yield return null;
            Assert.AreEqual(RoomView.Supply, app.Room);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "SupplyStock" && t.text.Contains("义肢腿")));
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "Part_Arm_Need"));
            Assert.IsFalse(Object.FindObjectsOfType<Button>().Any(b => b.name == "Install_" + hero.Id + "_Arm"));

            app.DebugGrantProsthetic(LimbPart.Arm); yield return null;
            Click("Install_" + hero.Id + "_Arm"); yield return null;
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Arm));
            Assert.IsTrue(LimbSystem.HasProsthetic(hero, LimbPart.Arm));
            Assert.IsFalse(app.Run.Prosthetics.Any(p => p.Part == LimbPart.Arm), "安装后从仓库扣除");
            Assert.IsTrue(app.Run.Prosthetics.Any(p => p.Part == LimbPart.Leg), "没用到的义肢留在仓库");
        }

        [UnityTest] public IEnumerator AnEventRoomWalksItsSubStepsAndStaysShutUntilTheScriptEnds()
        {
            // EVT-004 的第二步要挑人选，正好覆盖「选人子步骤」和多步闸门。
            app.DebugForceEvent("EVT-004"); yield return null;
            Assert.AreEqual(RoomView.Event, app.Room);
            var node = app.ActiveNode;
            Assert.AreEqual("EVT-004", node.EventId);
            Assert.IsFalse(app.EventCanLeave, "第一步还没选，房间不能离开");
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "EventTitle" && t.text.Contains("步骤 1/2")));

            // 越界的下标被拒绝，而且不改动任何状态。
            double before = app.Run.PlayerSanity;
            app.ChooseEventOption(9); yield return null;
            Assert.AreEqual(before, app.Run.PlayerSanity);
            Assert.IsFalse(app.EventCanLeave);
            Assert.IsTrue(Object.FindObjectsOfType<Button>().First(b => b.name == "LeaveRoom").interactable == false,
                "事件没走完时离开按钮不可点");

            Click("EventOption_0"); yield return null;
            Assert.AreEqual(1, node.EventStep, "喝下药剂后进入选人子步骤");
            Assert.IsFalse(app.EventCanLeave, "还没选人，房间仍然关着");
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "EventTitle" && t.text.Contains("步骤 2/2")));

            Click("EventOption_0"); yield return null;
            Assert.AreEqual(0, app.Run.Heroes[0].Sanity, "蓝药剂把选中队员的理智清零");
            Assert.IsTrue(app.EventCanLeave);

            Click("LeaveRoom"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
        }

        [UnityTest] public IEnumerator AnEventFightComesBackToTheEventAndPaysItsReward()
        {
            // EVT-001 挖到上限会打一场，胜利之后奖励要回到事件页发，路线本身不前进。
            app.DebugForceEvent("EVT-001"); yield return null;
            var node = app.ActiveNode;
            int stepBefore = app.Map.StepIndex;

            int guard = 0;
            while (app.AwaitingNode && guard++ < 10) { Click("EventOption_0"); yield return null; }
            Assert.IsFalse(app.AwaitingNode, "挖到上限会切到战斗页面");
            Assert.NotNull(app.Battle);
            Assert.AreEqual(node, app.EventBattle, "战斗记住自己是这个事件发起的");

            int supplies = app.Map.Supplies;
            int stock = app.Run.Prosthetics.Count;
            app.DebugWinBattle(); yield return null;
            Click("ContinueBattle"); yield return null;

            Assert.AreEqual(RoomView.Event, app.Room, "结算回到事件页，而不是继续前进");
            Assert.AreEqual(stepBefore, app.Map.StepIndex, "事件没结束时路线不推进");
            Assert.IsNull(app.Battle);
            Assert.AreEqual(supplies + 2, app.Map.Supplies, "胜利奖励由事件发放");
            Assert.AreEqual(stock, app.Run.Prosthetics.Count, "矿道的奖励是矿石而不是义体");
            Assert.IsTrue(app.EventCanLeave, "结算之后事件才结束");

            Click("LeaveRoom"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.AreEqual(stepBefore + 1, app.Map.StepIndex, "离开事件才前进一格");
        }

        [UnityTest] public IEnumerator TheElevatorGoesDownOneFloorWithoutHealing()
        {
            // 电梯井是本层的末端，所以先站到那个位置上，再从卡片走进房间。
            app.DebugSkipToFloorEnd(); yield return null;
            Assert.IsTrue(app.Map.Current.Fixed);
            Assert.AreEqual(NodeType.Elevator, app.Map.Current.Candidates[0].Type, "第1层的末端是电梯井");

            Click("Node_0"); yield return null;
            Assert.AreEqual(RoomView.Elevator, app.Room);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "ElevatorHeading"));

            int before = app.Run.Heroes.Where(h => !h.Dead).Sum(h => h.Hp);
            Click("ElevatorDown"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.AreEqual(2, app.Map.Floor, "电梯井把队伍带到第二层");
            Assert.IsTrue(app.Map.Current.Fixed);
            Assert.AreEqual(NodeType.Supply, app.Map.Current.Candidates[0].Type, "第二层从补给站开始");
            Assert.AreEqual(before, app.Run.Heroes.Where(h => !h.Dead).Sum(h => h.Hp), "换层不会整备生命");
        }

        [UnityTest] public IEnumerator TheBossRoomEndsTheRunEitherWay()
        {
            app.DebugEnterFloor(app.Catalog.MapRules().Floors); yield return null;
            app.DebugSkipToFloorEnd(); yield return null;
            Assert.IsTrue(app.Map.Current.Fixed);
            Assert.AreEqual(NodeType.Boss, app.Map.Current.Candidates[0].Type, "最后一层的末端是首领房");
            Assert.AreEqual(1, app.Map.Current.Candidates.Count);

            Click("Node_0"); yield return null;
            Assert.NotNull(app.Battle);
            Assert.IsTrue(app.Battle.Enemies.All(e => e.Name.StartsWith("首领")), "首领房里敌人被放大成首领");
            app.DebugSettle(); yield return null;
            Click("ContinueBattle"); yield return null;
            Assert.IsTrue(app.Map.Finished, "首领房无论如何都会结束本局");
            Assert.IsFalse(app.Map.Cleared, "撤退不算通关");
            Assert.IsNotNull(app.RunEndNote);

            Click("MapNewRun"); yield return null;
            Assert.IsFalse(app.Map.Finished);
            Assert.AreEqual(1, app.Map.Floor);
            Assert.AreEqual(NodeType.Opening, app.Map.Current.Candidates[0].Type, "新开一局回到初始增益房");
        }

        [UnityTest] public IEnumerator TheChestRoomGrantsOneAccessoryOnlyOnce()
        {
            // 宝箱房是饰品的获取来源：概率面板上有它，进去发一件，节点上的已领取标记只翻一次。
            var specs = MapGenerator.Resolve(app.Catalog.MapRules()).NodeOdds;
            int chest = System.Array.FindIndex(specs, s => s.Type == NodeType.Treasure);
            Assert.That(chest, Is.GreaterThanOrEqualTo(0), "宝箱房已经登记进房间类型表");
            Assert.AreEqual(0.10, MapGenerator.TypeOdds(app.Catalog.MapRules(), 50)[chest], 0.0005, "理智50 时宝箱房占 10%");
            // 面板画的是当前理智下的小数点后一位，按同一套数值反推，改数值时这条断言不用跟着改。
            string percent = (MapGenerator.TypeOdds(app.Catalog.MapRules(), app.Run.PlayerSanity)[chest] * 100).ToString("0.0") + "%";
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "宝箱房Percent" && t.text == percent),
                "概率面板上要画出宝箱房这一行（期望 " + percent + "）");

            int before = app.Run.AccessoryStock.Count;
            app.DebugForceRoom(NodeType.Treasure); yield return null;
            Assert.AreEqual(RoomView.Treasure, app.Room);
            Assert.IsNull(app.Battle);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "TreasureHeading"));
            Assert.AreEqual(before + 1, app.Run.AccessoryStock.Count, "宝箱房发放一件饰品");
            Assert.IsFalse(string.IsNullOrEmpty(app.RoomNote), "房间页要说出拿到了什么");
            Assert.AreEqual(1, app.ActiveNode.EventFlag, "已领取标记已经写下");

            int supplies = app.Map.Supplies;
            Click("LeaveRoom"); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room, "离开宝箱房要能回到路线页");
            Assert.IsTrue(app.AwaitingNode);
            Assert.AreEqual(supplies, app.Map.Supplies, "宝箱房只发饰品，不发补给");
        }

        [UnityTest] public IEnumerator TheSupplyRoomEquipsAndUnequipsAnAccessory()
        {
            app.DebugForceRoom(NodeType.Supply); yield return null;
            app.DebugGrantAllAccessories(); yield return null;
            Assert.GreaterOrEqual(app.Run.AccessoryStock.Count, 8);

            Click("SupplyAccessories"); yield return null;
            Assert.IsTrue(app.AccessoryEditorOpen, "管理饰品按钮要打开同一套面板");
            var gunner = app.Run.Heroes.First(h => h.Definition.Silhouette == "gunner");
            Click("AccessoryHero_" + gunner.Id); yield return null;

            int stock = app.Run.AccessoryStock.Count;
            Click("Equip_0"); yield return null;
            Assert.IsTrue(gunner.EquippedAccessories.Any(a => a.Id == "acc_gutter"), "枪手装备了穿肠弹");
            Assert.AreEqual(stock - 1, app.Run.AccessoryStock.Count);

            Click("Unequip_" + gunner.Id + "_acc_gutter"); yield return null;
            Assert.IsFalse(gunner.EquippedAccessories.Any(a => a.Id == "acc_gutter"));
            Assert.AreEqual(stock, app.Run.AccessoryStock.Count, "卸下后回到仓库");

            Click("CloseAccessories"); yield return null;
            Assert.IsFalse(app.AccessoryEditorOpen);
            Assert.AreEqual(RoomView.Supply, app.Room, "关掉面板仍在补给站");
        }

        [UnityTest] public IEnumerator TheConsumablePanelHealsOutsideBattleAndStaysOpen()
        {
            var hero = app.Run.Heroes[0];
            hero.Hp = 10;
            app.DebugGrantAllConsumables(); yield return null;
            Assert.AreEqual(3, app.Run.Consumables.Count);

            Click("Consumables"); yield return null;
            Assert.IsTrue(app.ConsumableEditorOpen, "标题栏的道具按钮要打开面板");
            int expected = System.Math.Min(hero.Definition.MaxHp,
                10 + (int)System.Math.Ceiling(hero.Definition.MaxHp * ConsumableSystem.HealPercent / 100.0));
            // 默认顺序是理智锁定、血量恢复、手术包。
            Click("UseConsumable_1"); yield return null;
            Assert.AreEqual(expected, hero.Hp);
            Assert.AreEqual(2, app.Run.Consumables.Count, "使用后扣除一件");
            Assert.IsTrue(app.ConsumableEditorOpen, "战斗外使用后面板保持打开");

            Click("CloseConsumables"); yield return null;
            Assert.IsFalse(app.ConsumableEditorOpen);
        }

        [UnityTest] public IEnumerator TheBattleConsumablePanelLocksSanityAndConsumesTheTurn()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            Assert.NotNull(app.Battle);
            Assert.IsTrue(app.Battle.Current != null && app.Battle.Current.Player);
            var hero = app.Battle.Current;
            app.DebugGrantAllConsumables(); yield return null;

            Click("BattleConsumables"); yield return null;
            Assert.IsTrue(app.ConsumableEditorOpen, "行动区的道具按钮要打开面板");
            Click("ConsumableHero_" + hero.Id); yield return null;
            int stock = app.Run.Consumables.Count;
            // 第一件是理智锁定药剂。
            Click("UseConsumable_0"); yield return null;
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;

            Assert.IsTrue(hero.SanityLocked, "战斗中直接使用会立刻锁定本次战斗");
            Assert.AreEqual(stock - 1, app.Run.Consumables.Count);
            Assert.IsFalse(app.ConsumableEditorOpen, "战斗中成功使用后会关掉面板并结算行动");
            Assert.IsTrue(app.Battle.Events.Any(e => e.Kind == "sanity" && e.Message.Contains("理智锁定")));
        }

        [UnityTest] public IEnumerator TheSurgeryKitIsVisibleButCannotBeUsed()
        {
            app.DebugGrantAllConsumables(); yield return null;
            Click("Consumables"); yield return null;
            Assert.IsTrue(app.ConsumableEditorOpen);

            var use = Object.FindObjectsOfType<Button>().First(b => b.name == "UseConsumable_2");
            Assert.IsFalse(use.interactable, "手术包的使用按钮永远禁用");
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "UseConsumable_2Title" && t.text == "功能待定"));
            Assert.AreEqual(3, app.Run.Consumables.Count, "禁用按钮不会消耗库存");
        }

        [UnityTest] public IEnumerator TheConsumablePanelBlocksBattleCommandsAndKeepsTheRandomState()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            app.DebugGrantAllConsumables(); yield return null;
            uint state = app.Run.Random.State;
            string inspected = app.InspectedId;

            Click("BattleConsumables"); yield return null;
            Assert.AreEqual(state, app.Run.Random.State, "打开面板不消耗随机序列");

            // 面板还开着时，技能、换位和选人都不能开始一个新的行动。
            app.SelectSkill(0);
            app.SelectSwap();
            app.SelectActor(app.Battle.Enemies[0].Id);
            Assert.IsFalse(app.Choosing);
            Assert.AreEqual(inspected, app.InspectedId);

            Click("CloseConsumables"); yield return null;
            Assert.IsFalse(app.ConsumableEditorOpen);
        }

        [UnityTest] public IEnumerator DebugForceRoomReplacesTheCurrentCandidates()
        {
            app.DebugForceRoom(NodeType.Supply); yield return null;
            Assert.AreEqual(RoomView.Supply, app.Room);
            Click("LeaveRoom"); yield return null;

            // 走到下一个位置后再强制一次，房间类型仍然由工具说了算。
            app.DebugForceRoom(NodeType.Elevator); yield return null;
            Assert.AreEqual(RoomView.Elevator, app.Room);
        }
    }
}
