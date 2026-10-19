using System.Collections;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ruin.Tests
{
    // The formal UI is prefab-authored, so these tests drive the state machine directly. Widget wiring and
    // layout are covered by the editor verification scripts, not by finding generated buttons here.
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

        [UnityTest] public IEnumerator TheRunStartsOnTheRouteAndExecutesASkill()
        {
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
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

        // Group skills have no single main target, so every living actor on their side confirms the cast.
        // Selecting the skill must not fire it; clicking any highlighted actor does. Blocked slots must
        // still open the description instead of swallowing clicks.
        [UnityTest] public IEnumerator GroupSkillsReleaseFromAnyTargetAndBlockedSkillsStayReadable()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            var battle = app.Battle;
            Assert.NotNull(battle);

            // Turn order is speed-based, so skip forward until a hero owns a castable group skill.
            int guard = 0;
            while (guard++ < 40 && battle.Outcome == BattleOutcome.Active)
            {
                var actor = battle.Current;
                if (actor == null) break;
                var actorSkills = battle.SkillsFor(actor);
                if (actor.Player && Enumerable.Range(0, actorSkills.Length)
                        .Any(i => actorSkills[i].Targeting == SkillTargeting.All && battle.SkillDisabledReason(i) == null))
                    break;
                if (actor.Player) battle.SkipCurrentTurn(out _); else battle.ExecuteEnemy();
                app.Refresh(); yield return null;
            }
            var current = battle.Current;
            Assert.NotNull(current); Assert.IsTrue(current.Player, "没有走到可释放群体技能的我方回合");
            var skills = battle.SkillsFor(current);
            int group = Enumerable.Range(0, skills.Length)
                .First(i => skills[i].Targeting == SkillTargeting.All && battle.SkillDisabledReason(i) == null);

            int before = battle.Events.Count;
            app.SelectSkill(group); yield return null;
            Assert.AreEqual(group, app.PendingSkill, "点击技能只应选中");
            Assert.AreEqual(group, app.InspectedSkill);
            Assert.AreEqual(before, battle.Events.Count, "选中群体技能不能直接释放");
            var clickTarget = battle.Targets(group).FirstOrDefault();
            Assert.NotNull(clickTarget, "群体技能应把整侧存活目标列为可点击项");

            app.SelectActor(clickTarget.Id); yield return null;
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;
            Assert.Greater(battle.Events.Count, before, "点击任意高亮目标应释放群体技能");

            // Find a rank where this form has a blocked skill, then check it still opens the description.
            while (!app.Busy && battle.Outcome == BattleOutcome.Active && battle.Current != null && !battle.Current.Player)
            {
                battle.ExecuteEnemy(); app.Refresh(); yield return null;
            }
            current = battle.Current;
            Assert.NotNull(current); Assert.IsTrue(current.Player, "确认释放后没有回到我方回合");
            int savedRank = current.Rank; int blocked = -1;
            // Keep the rank that makes the skill blocked while clicking: SelectSkill reads the reason
            // synchronously, then the original rank is restored.
            for (int rank = 0; rank < 4 && blocked < 0; rank++)
            {
                current.Rank = rank;
                for (int i = 0; i < battle.SkillsFor(current).Length; i++)
                    if (battle.SkillDisabledReason(i) != null) { blocked = i; break; }
            }
            Assert.GreaterOrEqual(blocked, 0, "当前形态应至少有一个受站位限制的技能可用于检查说明");
            int mark = battle.Events.Count;
            app.SelectSkill(blocked); yield return null;
            current.Rank = savedRank;
            Assert.AreEqual(blocked, app.InspectedSkill, "不可用技能也应显示说明");
            Assert.IsNull(app.PendingSkill, "不可用技能不能进入待释放状态");
            Assert.AreEqual(mark, battle.Events.Count, "查看说明不应改变战斗");
            Assert.IsNotEmpty(app.Hint);
        }

        [UnityTest] public IEnumerator DebugPresetAndFeedbackContinueThroughTheSettlementPage()
        {
            app.NewRun(85); yield return null;
            Assert.IsTrue(app.Run.Heroes.All(h => h.Sanity == 85));
            app.DebugForceRoom(NodeType.Battle); yield return null;
            app.DebugSetSanity(50);
            var selected = app.Battle.Find(app.InspectedId);
            app.DebugSettle(); yield return null;
            // The settlement is the event page now, so its visibility comes from RoomPages.
            Assert.IsTrue(RoomPages.Visible(app));
            app.ContinueBattle(); yield return null;
            Assert.AreEqual(50, selected.Sanity);
            app.DebugFeedbackExample(); yield return null;
            Assert.AreEqual(47.5, app.Run.PlayerSanity);
            Assert.IsTrue(RoomPages.Visible(app));
        }

        [UnityTest] public IEnumerator TheRouteOpensOnTheBuffRoomAndThenOffersCandidates()
        {
            var opening = app.Map.Current;
            Assert.AreEqual(1, opening.Floor);
            Assert.AreEqual(0, opening.Index);
            Assert.IsTrue(opening.Fixed, "开局是固定房间，不抽随机");
            Assert.AreEqual(NodeType.Opening, opening.Candidates[0].Type);

            app.SelectNode(0); yield return null;
            Assert.AreEqual(RoomView.Buff, app.Room);
            Assert.IsNull(app.Battle);
            app.ChooseBuff(GrowthBuff.Toughness); yield return null;
            Assert.IsTrue(app.Map.BuffChosen);

            app.LeaveRoom(); yield return null;
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
            app.ContinueBattle(); yield return null;
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

            app.RepairLimb(hero.Id, LimbPart.Arm); yield return null;
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Arm));
            Assert.AreEqual(LimbSystem.Get(hero, LimbPart.Arm).MaxHp, LimbSystem.Get(hero, LimbPart.Arm).Hp);

            int wounded = Mathf.Max(1, hero.Definition.MaxHp - 10);
            hero.Hp = wounded;
            app.SupplyHeal(); yield return null;
            Assert.IsTrue(app.Map.SupplyHealed);
            Assert.Greater(hero.Hp, wounded);
            int healed = hero.Hp;
            app.SupplyHeal(); yield return null;
            Assert.AreEqual(healed, hero.Hp, "治疗每次补给站只能用一次");

            app.LeaveRoom(); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.AreEqual(1, app.Map.Floor, "离开补给站只是继续前进，不换层");
        }

        [UnityTest] public IEnumerator TheSupplyRoomInstallsAProstheticAndConsumesItFromTheStock()
        {
            app.DebugSeverLimb(LimbPart.Arm); yield return null;
            var hero = app.Run.Heroes[0];
            Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, LimbPart.Arm));

            // 只发一件腿部义肢：手臂那一格没有可安装的东西。
            app.DebugGrantProsthetic(LimbPart.Leg); yield return null;
            app.DebugForceRoom(NodeType.Supply); yield return null;
            Assert.AreEqual(RoomView.Supply, app.Room);

            app.DebugGrantProsthetic(LimbPart.Arm); yield return null;
            string itemId = app.Run.Prosthetics.First(p => p != null && p.Part == LimbPart.Arm).Id;
            app.Map.Supplies = 5;
            int money = app.Map.Supplies;
            app.InstallProsthetic(hero.Id, itemId); yield return null;
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Arm));
            Assert.IsTrue(LimbSystem.HasProsthetic(hero, LimbPart.Arm));
            Assert.IsFalse(app.Run.Prosthetics.Any(p => p.Part == LimbPart.Arm), "安装后从仓库扣除");
            Assert.IsTrue(app.Run.Prosthetics.Any(p => p.Part == LimbPart.Leg), "没用到的义肢留在仓库");
            Assert.AreEqual(money - SupplyShop.InstallPrice, app.Map.Supplies, "安装义肢扣一份矿石");
        }

        [UnityTest] public IEnumerator TheSupplyRoomChargesForLaterFloorsAndLaterRepairs()
        {
            app.DebugForceRoom(NodeType.Supply); yield return null;
            var hero = app.Run.Heroes[0];

            // 每座补给站的第一次修复免费，第二次开始收 1 矿石。
            app.DebugInjureLimb(LimbPart.Arm); app.DebugInjureLimb(LimbPart.Leg); yield return null;
            app.Map.Supplies = 1;
            app.RepairLimb(hero.Id, LimbPart.Arm); yield return null;
            Assert.AreEqual(1, app.Map.Supplies, "第一次修复免费");
            app.RepairLimb(hero.Id, LimbPart.Leg); yield return null;
            Assert.AreEqual(0, app.Map.Supplies, "第二次修复收 1 矿石");

            // 第三层起治疗收 2 矿石；矿石不足时既不治疗，也不消耗本次机会。
            app.Map.Floor = 3;
            hero.Hp = 1;
            app.Map.Supplies = 1; app.Map.SupplyHealed = false;
            int before = hero.Hp;
            app.SupplyHeal(); yield return null;
            Assert.AreEqual(before, hero.Hp, "矿石不足时不治疗");
            Assert.IsFalse(app.Map.SupplyHealed, "矿石不足时不消耗本次机会");
            app.Map.Supplies = 2;
            app.SupplyHeal(); yield return null;
            Assert.Greater(hero.Hp, before, "付得起时正常治疗");
            Assert.IsTrue(app.Map.SupplyHealed);
            Assert.AreEqual(0, app.Map.Supplies, "第三层治疗收 2 矿石");

            // 静养同样按层收费。
            app.Run.PlayerSanity = 20;
            app.Map.Supplies = 2; app.Map.SupplyCalmed = false;
            app.SupplyCalm(); yield return null;
            Assert.AreEqual(30, app.Run.PlayerSanity, "静养恢复玩家理智");
            Assert.AreEqual(0, app.Map.Supplies, "第三层静养收 2 矿石");
        }

        [UnityTest] public IEnumerator AnEventRoomWalksItsSubStepsAndStaysShutUntilTheScriptEnds()
        {
            // EVT-004 的第二步要挑人选，正好覆盖「选人子步骤」和多步闸门。
            app.DebugForceEvent("EVT-004"); yield return null;
            Assert.AreEqual(RoomView.Event, app.Room);
            var node = app.ActiveNode;
            Assert.AreEqual("EVT-004", node.EventId);
            Assert.IsFalse(app.EventCanLeave, "第一步还没选，房间不能离开");

            // 越界的下标被拒绝，而且不改动任何状态。
            double before = app.Run.PlayerSanity;
            app.ChooseEventOption(9); yield return null;
            Assert.AreEqual(before, app.Run.PlayerSanity);
            Assert.IsFalse(app.EventCanLeave, "事件没走完时不能离开");

            app.ChooseEventOption(0); yield return null;
            Assert.AreEqual(1, node.EventStep, "喝下药剂后进入选人子步骤");
            Assert.IsFalse(app.EventCanLeave, "还没选人，房间仍然关着");

            app.ChooseEventOption(0); yield return null;
            Assert.AreEqual(0, app.Run.Heroes[0].Sanity, "蓝药剂把选中队员的理智清零");
            Assert.IsTrue(app.EventCanLeave);

            app.LeaveRoom(); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
        }

        [UnityTest] public IEnumerator AnEventFightComesBackToTheEventAndPaysItsReward()
        {
            // EVT-001 挖到上限会打一场，胜利之后奖励要回到事件页发，路线本身不前进。
            app.DebugForceEvent("EVT-001"); yield return null;
            var node = app.ActiveNode;
            int stepBefore = app.Map.StepIndex;

            int guard = 0;
            while (app.AwaitingNode && guard++ < 10) { app.ChooseEventOption(0); yield return null; }
            Assert.IsFalse(app.AwaitingNode, "挖到上限会切到战斗页面");
            Assert.NotNull(app.Battle);
            Assert.AreEqual(node, app.EventBattle, "战斗记住自己是这个事件发起的");

            int supplies = app.Map.Supplies;
            int stock = app.Run.Prosthetics.Count;
            app.DebugWinBattle(); yield return null;
            app.ContinueBattle(); yield return null;

            Assert.AreEqual(RoomView.Event, app.Room, "结算回到事件页，而不是继续前进");
            Assert.AreEqual(stepBefore, app.Map.StepIndex, "事件没结束时路线不推进");
            Assert.IsNull(app.Battle);
            Assert.AreEqual(supplies + 2, app.Map.Supplies, "胜利奖励由事件发放");
            Assert.AreEqual(stock, app.Run.Prosthetics.Count, "矿道的奖励是矿石而不是义体");
            Assert.IsTrue(app.EventCanLeave, "结算之后事件才结束");

            app.LeaveRoom(); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room);
            Assert.AreEqual(stepBefore + 1, app.Map.StepIndex, "离开事件才前进一格");
        }

        [UnityTest] public IEnumerator TheElevatorGoesDownOneFloorWithoutHealing()
        {
            // 电梯井是本层的末端，所以先站到那个位置上，再从卡片走进房间。
            app.DebugSkipToFloorEnd(); yield return null;
            Assert.IsTrue(app.Map.Current.Fixed);
            Assert.AreEqual(NodeType.Elevator, app.Map.Current.Candidates[0].Type, "第1层的末端是电梯井");

            app.SelectNode(0); yield return null;
            Assert.AreEqual(RoomView.Elevator, app.Room);

            int before = app.Run.Heroes.Where(h => !h.Dead).Sum(h => h.Hp);
            app.AdvanceFloor(); yield return null;
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

            app.SelectNode(0); yield return null;
            Assert.NotNull(app.Battle);
            Assert.IsTrue(app.Battle.Enemies.All(e => e.Name.StartsWith("首领")), "首领房里敌人被放大成首领");
            app.DebugSettle(); yield return null;
            app.ContinueBattle(); yield return null;
            Assert.IsTrue(app.Map.Finished, "首领房无论如何都会结束本局");
            Assert.IsFalse(app.Map.Cleared, "撤退不算通关");
            Assert.IsNotNull(app.RunEndNote);

            app.NewRun(); yield return null;
            Assert.IsFalse(app.Map.Finished);
            Assert.AreEqual(1, app.Map.Floor);
            Assert.AreEqual(NodeType.Opening, app.Map.Current.Candidates[0].Type, "新开一局回到初始增益房");
        }

        [UnityTest] public IEnumerator TheChestRoomGrantsOneAccessoryOnlyOnce()
        {
            // 宝箱房是饰品的获取来源：进去发一件，节点上的已领取标记只翻一次。
            var specs = MapGenerator.Resolve(app.Catalog.MapRules()).NodeOdds;
            int chest = System.Array.FindIndex(specs, s => s.Type == NodeType.Treasure);
            Assert.That(chest, Is.GreaterThanOrEqualTo(0), "宝箱房已经登记进房间类型表");
            Assert.AreEqual(0.10, MapGenerator.TypeOdds(app.Catalog.MapRules(), 50)[chest], 0.0005, "理智50 时宝箱房占 10%");

            int before = app.Run.AccessoryStock.Count;
            app.DebugForceRoom(NodeType.Treasure); yield return null;
            Assert.AreEqual(RoomView.Treasure, app.Room);
            Assert.IsNull(app.Battle);
            Assert.AreEqual(before + 1, app.Run.AccessoryStock.Count, "宝箱房发放一件饰品");
            Assert.IsFalse(string.IsNullOrEmpty(app.RoomNote), "房间页要说出拿到了什么");
            Assert.AreEqual(1, app.ActiveNode.EventFlag, "已领取标记已经写下");

            int supplies = app.Map.Supplies;
            app.LeaveRoom(); yield return null;
            Assert.AreEqual(RoomView.Map, app.Room, "离开宝箱房要能回到路线页");
            Assert.IsTrue(app.AwaitingNode);
            Assert.AreEqual(supplies, app.Map.Supplies, "宝箱房只发饰品，不发矿石");
        }

        [UnityTest] public IEnumerator TheCharacterPanelEquipsAndUnequipsAnAccessory()
        {
            app.DebugGrantAllAccessories(); yield return null;
            Assert.GreaterOrEqual(app.Run.AccessoryStock.Count, 8);

            var gunner = app.Run.Heroes.First(h => h.Definition.Silhouette == "gunner");
            int index = app.Run.Heroes.FindIndex(h => h.Id == gunner.Id);
            app.OpenCharacterPanel(index); yield return null;
            Assert.IsTrue(app.CharacterPanelOpen, "路线页头像要能打开角色面板");
            Assert.AreEqual(gunner.Id, app.CharacterHeroId);

            int stock = app.Run.AccessoryStock.Count;
            app.CharacterEquipAccessory("acc_gutter"); yield return null;
            Assert.IsTrue(gunner.EquippedAccessories.Any(a => a.Id == "acc_gutter"), "枪手装备了穿肠弹");
            Assert.AreEqual(stock - 1, app.Run.AccessoryStock.Count);

            app.CharacterUnequipAccessory("acc_gutter"); yield return null;
            Assert.IsFalse(gunner.EquippedAccessories.Any(a => a.Id == "acc_gutter"));
            Assert.AreEqual(stock, app.Run.AccessoryStock.Count, "卸下后回到仓库");

            app.CloseCharacterPanel(); yield return null;
            Assert.IsFalse(app.CharacterPanelOpen);
            Assert.AreEqual(RoomView.Map, app.Room, "关掉面板仍在路线页");
        }

        [UnityTest] public IEnumerator TheCharacterPanelHealsOutsideBattleAndConsumesTheItem()
        {
            var hero = app.Run.Heroes[0];
            hero.Hp = 10;
            app.DebugGrantAllConsumables(); yield return null;
            Assert.AreEqual(3, app.Run.Consumables.Count);

            app.OpenCharacterPanel(0); yield return null;
            Assert.IsTrue(app.CharacterPanelOpen);
            int expected = System.Math.Min(hero.Definition.MaxHp,
                10 + (int)System.Math.Ceiling(hero.Definition.MaxHp * ConsumableSystem.HealPercent / 100.0));
            app.CharacterUsePotion(ConsumableKind.HpRestore); yield return null;
            Assert.AreEqual(expected, hero.Hp);
            Assert.AreEqual(2, app.Run.Consumables.Count, "使用后扣除一件");
            Assert.IsTrue(app.CharacterPanelOpen, "战斗外使用后角色面板保持打开");

            app.CloseCharacterPanel(); yield return null;
            Assert.IsFalse(app.CharacterPanelOpen);
        }

        [UnityTest] public IEnumerator TheCharacterPanelLocksSanityInBattleAndConsumesTheTurn()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            Assert.NotNull(app.Battle);
            Assert.IsTrue(app.Battle.Current != null && app.Battle.Current.Player);
            var hero = app.Battle.Current;
            app.DebugGrantAllConsumables(); yield return null;

            int index = app.Run.Heroes.FindIndex(h => h.Id == hero.Id);
            app.OpenCharacterPanel(index); yield return null;
            Assert.IsTrue(app.CharacterPanelOpen, "战斗队伍头像要能打开角色面板");
            int stock = app.Run.Consumables.Count;
            app.CharacterUsePotion(ConsumableKind.SanityLock);
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;

            Assert.IsTrue(hero.SanityLocked, "战斗中直接使用会立刻锁定本次战斗");
            Assert.AreEqual(stock - 1, app.Run.Consumables.Count);
            Assert.IsFalse(app.CharacterPanelOpen, "战斗中成功使用后会关掉面板并结算行动");
            Assert.IsTrue(app.Battle.Events.Any(e => e.Kind == "sanity" && e.Message.Contains("理智锁定")));
        }

        [UnityTest] public IEnumerator TheCharacterPanelBlocksBattleCommandsAndKeepsTheRandomState()
        {
            app.DebugForceRoom(NodeType.Battle); yield return null;
            app.DebugGrantAllConsumables(); yield return null;
            uint state = app.Run.Random.State;

            app.OpenCharacterPanel(0); yield return null;
            Assert.IsTrue(app.CharacterPanelOpen);
            Assert.AreEqual(state, app.Run.Random.State, "打开面板不消耗随机序列");
            string inspected = app.InspectedId;

            // 面板还开着时，技能、换位和选人都不能开始一个新的行动。
            app.SelectSkill(0);
            app.SelectSwap();
            app.SelectActor(app.Battle.Enemies[0].Id);
            Assert.IsFalse(app.Choosing);
            Assert.AreEqual(inspected, app.InspectedId);

            app.CloseCharacterPanel(); yield return null;
            Assert.IsFalse(app.CharacterPanelOpen);
        }

        [UnityTest] public IEnumerator DebugForceRoomReplacesTheCurrentCandidates()
        {
            app.DebugForceRoom(NodeType.Supply); yield return null;
            Assert.AreEqual(RoomView.Supply, app.Room);
            app.LeaveRoom(); yield return null;

            // 走到下一个位置后再强制一次，房间类型仍然由工具说了算。
            app.DebugForceRoom(NodeType.Elevator); yield return null;
            Assert.AreEqual(RoomView.Elevator, app.Room);
        }
    }
}
