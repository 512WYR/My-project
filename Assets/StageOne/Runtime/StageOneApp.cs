using System;
using System.Collections;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Which page the node half of the game is showing. A battle is not a room: the battle page is driven
    // by AwaitingNode being false, exactly as it was before the route rework.
    public enum RoomView { Map, Buff, Supply, Event, Elevator, Treasure }

    public sealed class StageOneApp : MonoBehaviour
    {
        public GameCatalog Catalog;
        public RunState Run { get; private set; }
        public BattleEngine Battle { get; private set; }
        public StageOneView View { get; private set; }
        // 本回合已经点开、正等着点目标的技能下标。换位走一个单独的开关，因为它没有下标。
        public int? PendingSkill { get; private set; }
        public bool PendingSwap { get; private set; }
        // 任意一种「已选好行动、等待点目标」的状态。
        public bool Choosing { get { return PendingSkill.HasValue || PendingSwap; } }
        public string InspectedId { get; private set; }
        public bool Busy { get; private set; }
        public bool HelpOpen { get; private set; }
        public bool LabOpen { get; private set; }
        public string Hint { get; private set; }
        // True while the battle page is not in charge, which is also when Battle is null.
        public bool AwaitingNode { get; private set; }
        // The room page standing in for the battle page right now.
        public RoomView Room { get; private set; }
        // The room the party walked into, kept so a battle settlement and a room page both know which
        // node on the route they belong to.
        public NodeState ActiveNode { get; private set; }
        public bool EventChosen { get; private set; }
        public string RoomNote { get; private set; }
        // A one-line report of the fight that just settled, shown on the route page. It outlives the walk
        // forward, which is why it is not RoomNote: RevealStep clears that one on every step.
        public string MapNote { get; private set; }
        public string RunEndNote { get; private set; }
        public MapState Map { get { return Run == null ? null : Run.Map; } }
        public bool AutomationMode { get; set; }
        // Off by default, so StageOne.unity, the smoke run and the play mode tests keep the built-in battle
        // page. A scene that supplies its own battle UI turns this on and StageOneView stops drawing the
        // battle half of the page (rooms, route, settlement and the modals are never suppressed).
        public bool ExternalBattleUi;
        public const uint DefaultSeed = 44972;

        // A room page is open, so the debug tool stays shut the way it did around the workshop.
        public bool InRoom { get { return AwaitingNode && Room != RoomView.Map; } }

        private MapRules Rules() { return Catalog.MapRules(); }

        private void Start()
        {
            Application.targetFrameRate = 60;
            Catalog = Catalog ? Catalog : Resources.Load<GameCatalog>("StageOne/GameCatalog");
            if (!Catalog) { Debug.LogError("StageOne catalog missing. Run GameMake/Prepare Stage One from the editor."); return; }
            View = gameObject.AddComponent<StageOneView>();
            View.Initialize(this);
            NewRun();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Environment.GetCommandLineArgs().Contains("--stage1-smoke")) gameObject.AddComponent<StageOneSmoke>();
#endif
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) ToggleHelp();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F3)) ToggleLab();
#endif
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (HelpOpen) ToggleHelp();
                else if (LabOpen) ToggleLab();
                else { ClearPending(); Refresh(); }
            }
        }

        public void NewRun(double? preset = null)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); HelpOpen = false; LabOpen = false;
            Room = RoomView.Map; ActiveNode = null; EventChosen = false; RoomNote = null; MapNote = null;
            RunEndNote = null;
            // The limb rules travel with the rest of the combat rules, so the catalog's part ratios and
            // penalties seed every hero's limb table.
            Run = RunState.Create(Catalog.Heroes.Select(a => a.ToSpec()).ToArray(), DefaultSeed, preset, Catalog.Rules());
            Run.Map.Floor = 1;
            RevealStep();
            Hint = "路线开局是一间初始增益房，先选一项全队增益。";
            Refresh();
        }

        // Stands on the current position of the route. Already-shown positions are returned untouched, so
        // re-rendering or reloading never re-rolls them and never consumes a draw.
        private void RevealStep()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Map; ActiveNode = null; EventChosen = false; RoomNote = null;
            AwaitingNode = true;
            MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            Hint = "选择一条路线前进。已通过的房间固定不变，前方候选只有在走到跟前才会出现。";
            Refresh();
        }

        // Walks one position further along the route.
        private void WalkForward()
        {
            if (Run.Map.Finished) return;
            var rules = Rules();
            if (Run.Map.StepIndex + 1 >= MapGenerator.TotalSteps(rules)) return;
            Run.Map.StepIndex++;
            RevealStep();
        }

        public void SelectNode(int index)
        {
            if (Busy || HelpOpen || LabOpen || Run == null || Run.Map.Finished) return;
            if (Room != RoomView.Map) return;
            var step = Run.Map.Current;
            if (step == null || step.Resolved || index < 0 || index >= step.Candidates.Count) return;
            var node = step.Candidates[index];
            step.ChosenIndex = index;
            ActiveNode = node;
            MapNote = null;

            switch (node.Type)
            {
                case NodeType.Battle:
                case NodeType.EliteBattle:
                case NodeType.Boss:
                    StartBattle(node); return;
                case NodeType.Opening:
                    EnterBuffRoom(); return;
                case NodeType.Supply:
                    EnterSupplyRoom(); return;
                case NodeType.Event:
                case NodeType.Altar:
                    EnterEventRoom(node); return;
                case NodeType.Elevator:
                    EnterElevator(); return;
                case NodeType.Treasure:
                    EnterTreasureRoom(); return;
            }
            // Nothing in the table falls through today; the branch is left so a node type added in a later
            // stage degrades into walking on instead of dead-ending the run.
            RoomNote = "「" + node.Name + "」还没有内容，先继续前进。";
            ResolveCurrent();
        }

        // ---------------- openings, elevators, and the room pages ----------------

        // The opening room hands out one team buff. It is applied to the heroes' own definitions: each of
        // those is the fresh ActorSpec the catalog handed out, so nothing here writes into the asset.
        private void EnterBuffRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Buff; AwaitingNode = true; EventChosen = false; RoomNote = null;
            Hint = "选择一项全队增益。选定之后本次冒险不再更改。";
            Refresh();
        }

        public void ChooseBuff(GrowthBuff buff)
        {
            if (Run == null || Room != RoomView.Buff || Run.Map.BuffChosen) return;
            OpeningBuffs.Apply(buff, Run, Catalog.Rules().Limbs);
            Run.Map.Buff = buff; Run.Map.BuffChosen = true;
            RoomNote = "全队获得「" + OpeningBuffs.Name(buff) + "」：" + OpeningBuffs.Describe(buff) + "。";
            Hint = RoomNote;
            Refresh();
        }

        // The supply room took over everything the workshop and the camp used to do: it heals the party,
        // repairs injured parts, fits prosthetics onto severed ones, and steadies the team. The limb half
        // is the old workshop page unchanged, so a part still only ever goes up by being repaired here.
        private void EnterSupplyRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Supply; AwaitingNode = true; EventChosen = false; RoomNote = null;
            Run.Map.SupplyHealed = false; Run.Map.SupplyCalmed = false;
            Hint = "补给站：治疗全队、修复重伤部位、为断肢安装义肢、静养恢复理智。离开后继续前进。";
            Refresh();
        }

        public void SupplyHeal()
        {
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyHealed) return;
            int healed = Exploration.SupplyHealParty(Rules(), Run);
            Run.Map.SupplyHealed = true;
            Hint = "补给站为全队恢复 " + healed + " 点生命。阵亡成员不会复活。";
            Refresh();
        }

        public void SupplyCalm()
        {
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyCalmed) return;
            double gained = Exploration.SupplyCalm(Run);
            Run.Map.SupplyCalmed = true;
            Hint = "全队静养，玩家理智 +" + gained.ToString("0.0") + "。";
            Refresh();
        }

        public void RepairLimb(string heroId, LimbPart part)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            if (hero == null) return;
            if (!LimbSystem.Repair(hero, part)) { Hint = "只有重伤部位能在这里修复；断肢需要安装义肢。"; Refresh(); return; }
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经修复。";
            Refresh();
        }

        public void InstallProsthetic(string heroId, string itemId)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (hero == null || item == null) return;
            if (!LimbSystem.Install(hero, item)) { Hint = "只有断肢才能安装义肢。"; Refresh(); return; }
            Run.Prosthetics.Remove(item);
            Hint = hero.Name + " 装上了" + item.Name + "，" + LimbSystem.PartName(item.Part) + "恢复基础功能。";
            Refresh();
        }

        // Events and altars share one page. The content and the visible options are frozen when the party
        // walks in, so re-rendering never re-rolls the room.
        private void EnterEventRoom(NodeState node)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Event; AwaitingNode = true; EventChosen = false; RoomNote = null;
            Exploration.RollEvent(Rules(), node, Run.Random, Run.PlayerSanity, node.Type);
            Hint = node.Type == NodeType.Altar ? "祭坛房间：选择一种处理方式。" : "事件房间：选择一种处理方式。";
            Refresh();
        }

        public void ChooseEventOption(int index)
        {
            if (Run == null || Room != RoomView.Event || ActiveNode == null || EventChosen) return;
            string note;
            if (!Exploration.ApplyOption(Rules(), ActiveNode, index, Run, out note))
            { Hint = "这个选项现在不可用。"; Refresh(); return; }
            EventChosen = true; RoomNote = note; Hint = note;
            Refresh();
        }

        // A pure transition page: no fight, no draw, just the floor summary and the way down.
        private void EnterElevator()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Elevator; AwaitingNode = true; EventChosen = false; RoomNote = null;
            Hint = "电梯井：本层到此为止。下行之后不会自动整备生命。";
            Refresh();
        }

        // The chest room is on the route ahead of its content: it holds a real weight in the odds table,
        // so it turns up like any other sampled room, but it hands out nothing until someone decides what
        // it holds. The page exists so walking into it resolves the position instead of dead-ending.
        private void EnterTreasureRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Treasure; AwaitingNode = true; EventChosen = false;
            RoomNote = "宝箱房的内容还没有确定，这里暂时不发放任何东西。";
            Hint = "宝箱房：内容待定。离开会继续前进。";
            Refresh();
        }

        public void AdvanceFloor()
        {
            if (Run == null || Room != RoomView.Elevator) return;
            if (Run.Map.Floor >= Rules().Floors) return;
            ResolveCurrent();
        }

        // The one button every room page ends on. A room that still owes a decision refuses to close.
        public void LeaveRoom()
        {
            if (Run == null || !InRoom) return;
            if (Room == RoomView.Buff && !Run.Map.BuffChosen) { Hint = "先选择一项增益再离开。"; Refresh(); return; }
            if (Room == RoomView.Event && !EventChosen) { Hint = "先选择一种处理方式再离开。"; Refresh(); return; }
            ResolveCurrent();
        }

        // Walks through the current position. Nothing heals here: the supply room owns recovery, and the
        // next position is only drawn once the party actually arrives.
        private void ResolveCurrent()
        {
            var step = Run.Map.Current;
            if (step == null) return;
            step.Resolved = true;
            WalkForward();
        }

        // ---------------- battles ----------------

        public void ContinueBattle()
        {
            if (Battle == null || Battle.Outcome == BattleOutcome.Active) return;
            if (Run.Heroes.All(a => a.Dead)) { FinishRun("全队阵亡，本次冒险结束。", false); return; }

            var node = ActiveNode;
            bool won = Battle.Outcome == BattleOutcome.Victory;
            if (node != null)
            {
                // Written to MapNote rather than RoomNote: the walk forward right below clears the room
                // notes, and a reward nobody can read is the same as no reward.
                if (won)
                {
                    int supplies = Encounter.SuppliesFor(Rules(), node.Type);
                    Run.Map.Supplies += supplies;
                    MapNote = "战斗胜利，获得 " + supplies + " 点补给。";
                    if (node.Type == NodeType.EliteBattle)
                    {
                        var drop = Encounter.DropProsthetic(Catalog.Rules().Limbs, Run.Random);
                        if (drop != null) { Run.Prosthetics.Add(drop); MapNote += "  掉落一件「" + drop.Name + "」，已放进队伍仓库。"; }
                    }
                }
                else MapNote = "撤退：没有获得这个房间的补给。";
            }
            if (node != null && node.Type == NodeType.Boss)
            {
                if (won) FinishRun("击败第 " + node.Floor + " 层首领，本局通关。", true);
                else FinishRun("从首领面前撤退，本局到此结束。", false);
                return;
            }
            // A retreat pays nothing, which is why the reward above is gated on the victory.
            ResolveCurrent();
        }

        private void FinishRun(string note, bool cleared)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            AwaitingNode = true; Room = RoomView.Map; ActiveNode = null;
            Run.Map.Finished = true; Run.Map.Cleared = cleared;
            RunEndNote = note;
            Hint = note;
            Refresh();
        }

        private void StartBattle(NodeState node)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false;
            AwaitingNode = false;
            // Scaling builds brand new specs, so an elite or a boss never leaks back into the enemy asset.
            var enemies = Encounter.Scale(Catalog.Enemies.Select(a => a.ToSpec()).ToArray(), node.Type, Rules());
            Battle = new BattleEngine(Run, enemies, Catalog.Rules());
            InspectedId = Battle.Current != null && Battle.Current.Player ? Battle.Current.Id : Run.Heroes[0].Id;
            Hint = "第 " + node.Floor + " 层 · " + node.Name + "。选择技能，再点击高亮目标；右侧可查看理智与技能概率。";
            Refresh();
            if (Battle.Current != null && !Battle.Current.Player) StartCoroutine(ContinueEnemies(Battle.Events.Count));
        }

        public void SelectSkill(int index)
        {
            if (Busy || HelpOpen || LabOpen || AwaitingNode || Battle == null) return;
            string reason = Battle.SkillDisabledReason(index);
            if (reason != null) { Hint = reason; Refresh(); return; }
            PendingSwap = false; PendingSkill = index;
            Hint = "点击高亮目标以执行「" + Battle.SkillFor(Battle.Current, index).Name + "」。Esc取消选择。";
            Refresh();
        }

        public void SelectSwap()
        {
            if (Busy || HelpOpen || LabOpen || AwaitingNode || Battle == null) return;
            string reason = Battle.SwapDisabledReason();
            if (reason != null) { Hint = reason; Refresh(); return; }
            PendingSkill = null; PendingSwap = true;
            Hint = "点击相邻的高亮队员，交换站位并消耗本次行动。Esc取消选择。";
            Refresh();
        }

        private void ClearPending() { PendingSkill = null; PendingSwap = false; }

        public void SelectActor(string id)
        {
            if (AwaitingNode || Battle == null) return;
            InspectedId = id;
            if (!Busy && PendingSkill.HasValue && Battle.Targets(PendingSkill.Value).Any(a => a.Id == id))
            { Submit(new ActionRequest(ActionKind.Skill, id, PendingSkill.Value)); return; }
            if (!Busy && PendingSwap && Battle.SwapTargets().Any(a => a.Id == id))
            { Submit(new ActionRequest(ActionKind.Swap, id)); return; }
            Refresh();
        }

        private void Submit(ActionRequest request)
        {
            int from = Battle.Events.Count;
            string error;
            if (!Battle.Execute(request, out error)) { Hint = error; Refresh(); return; }
            ClearPending();
            StartCoroutine(ContinueEnemies(from));
        }

        private IEnumerator ContinueEnemies(int from)
        {
            Busy = true; Refresh();
            yield return View.AnimateEvents(Battle.Events.Skip(from).ToArray(), AutomationMode);
            while (Battle.Outcome == BattleOutcome.Active && Battle.Current != null && !Battle.Current.Player)
            {
                Hint = Battle.Current.Name + " 即将行动。";
                Refresh();
                yield return new WaitForSeconds(AutomationMode ? 0.01f : 0.65f);
                int marker = Battle.Events.Count;
                Battle.ExecuteEnemy();
                Refresh();
                yield return View.AnimateEvents(Battle.Events.Skip(marker).ToArray(), AutomationMode);
            }
            Busy = false;
            if (Battle.Current != null && Battle.Current.Player) InspectedId = Battle.Current.Id;
            Hint = "选择技能，再点击高亮目标。F1查看说明，F3打开验收工具。";
            Refresh();
        }

        public void ToggleHelp() { HelpOpen = !HelpOpen; LabOpen = false; ClearPending(); Refresh(); }
        public void ToggleLab()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Closing always wins: a forced room can open a page while the tool is up, and without this the
            // tool would be stuck open because the room page blocks the reopen path.
            if (LabOpen) { LabOpen = false; ClearPending(); Refresh(); return; }
            if (Busy || InRoom) return;
            LabOpen = true; HelpOpen = false; ClearPending(); Refresh();
#endif
        }
        // Everything that changes state funnels through Refresh(), so this is the one place a second UI can
        // listen for changes instead of polling. Subscribers must only read: calling back into Refresh()
        // would loop.
        public event Action Changed;
        public void Refresh() { if (View) View.Render(); Changed?.Invoke(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugSetSanity(double value)
        {
            if (Battle == null) return;
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null || Battle.Outcome != BattleOutcome.Active) return;
            Battle.ChangeSanity(actor, value - actor.Sanity, "验收工具调整");
            Hint = "已改变实际理智；本回合形态固定，下回合才重新抽取。";
            Refresh();
        }

        public void DebugMental(MentalState mental)
        {
            if (Battle == null) return;
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null) return;
            actor.Mental = mental; Refresh();
        }

        public void DebugSettle()
        {
            if (Battle == null) return;
            StopAllCoroutines(); Busy = false; LabOpen = false; Battle.Finish(BattleOutcome.Retreat); Refresh();
        }

        public void DebugFeedbackExample()
        {
            NewRun(50);
            DebugForceRoom(NodeType.Battle);
            if (Battle == null) return;
            int[] delta = { -20, -10, 0, 10 };
            for (int i = 0; i < Run.Heroes.Count; i++) Battle.ChangeSanity(Run.Heroes[i], delta[i], "验收样例");
            Battle.Finish(BattleOutcome.Retreat);
            Refresh();
        }

        public void DebugDeathDoor()
        {
            NewRun(50);
            DebugForceRoom(NodeType.Battle);
            if (Battle == null) return;
            Run.Heroes[0].Hp = 0;
            InspectedId = Run.Heroes[0].Id;
            Hint = "盾卫处于濒死；等奶妈行动后使用「流血奶」，可以恢复生命。";
            Refresh();
        }

        // Battles no longer heal the party on their own; the supply room owns that, so the acceptance tool
        // keeps the recovery reachable without walking to one.
        public void DebugRestParty()
        {
            if (Battle == null) return;
            foreach (var hero in Run.Heroes.Where(a => !a.Dead)) hero.Hp = hero.Definition.MaxHp;
            Hint = "验收工具已整备全队生命；部位、个人理智、精神状态和死亡记录沿用。";
            Refresh();
        }

        // Prosthetics are a run-level stock rather than a battle effect, so the limb tools work on the map
        // page too. That is what makes "walk into a supply room with a severed arm" reachable without
        // waiting for a cleaver to land its swing.
        public void DebugGrantProsthetic(LimbPart part)
        {
            if (Run == null) return;
            var item = LimbSystem.Resolve(Catalog.Rules().Limbs).Prosthetics.FirstOrDefault(p => p.Part == part);
            if (item == null) return;
            Run.Prosthetics.Add(item);
            Hint = "验收工具已发放" + item.Name + "。";
            Refresh();
        }

        public void DebugInjureLimb(LimbPart part)
        {
            if (Run == null) return;
            var hero = InspectedHero();
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "受到一次部位伤害，现在是"
                + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part)) + "。";
            Refresh();
        }

        public void DebugSeverLimb(LimbPart part)
        {
            if (Run == null) return;
            var hero = InspectedHero();
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经断肢，需要义肢才能恢复。";
            Refresh();
        }

        public void DebugGrantAllProsthetics()
        {
            if (Run == null) return;
            foreach (var item in LimbSystem.Resolve(Catalog.Rules().Limbs).Prosthetics)
                if (!Run.Prosthetics.Any(p => p != null && p.Id == item.Id)) Run.Prosthetics.Add(item);
            Hint = "验收工具已发放全部义肢。";
            Refresh();
        }

        // Prefers whoever the inspector is showing, the same way every other debug tool does.
        private ActorState InspectedHero()
        {
            var actor = Run.Heroes.FirstOrDefault(a => a.Id == InspectedId && !a.Dead);
            return actor ?? Run.Heroes.FirstOrDefault(a => !a.Dead);
        }

        // Jumps to the first position of a floor. The positions in between are still built in order inside
        // Advance, so the random sequence stays the one a walked route would have produced.
        public void DebugEnterFloor(int floor)
        {
            if (Run == null) return;
            var rules = Rules();
            StopAllCoroutines(); Busy = false; ClearPending(); HelpOpen = false; LabOpen = false; Battle = null;
            int target = Math.Min(Math.Max(floor, 1), rules.Floors);
            int start = 0;
            for (int f = 1; f < target; f++) start += MapGenerator.StepsOnFloor(rules, f);
            Run.Map.StepIndex = start;
            Run.Map.Finished = false; Run.Map.Cleared = false; RunEndNote = null;
            RevealStep();
            Hint = "验收工具已跳到第 " + target + " 层。";
            Refresh();
        }

        // Jumps to the last position of the current floor, which is the elevator on floors 1 and 2 and the
        // boss on the last one. That is what lets the smoke run reach the end without playing every fight.
        public void DebugSkipToFloorEnd()
        {
            if (Run == null || Run.Map.Finished) return;
            var rules = Rules();
            int floor = Math.Min(Math.Max(Run.Map.Floor, 1), rules.Floors);
            int start = 0;
            for (int f = 1; f < floor; f++) start += MapGenerator.StepsOnFloor(rules, f);
            Run.Map.StepIndex = start + MapGenerator.StepsOnFloor(rules, floor) - 1;
            RevealStep();
            Hint = "验收工具已跳到第 " + floor + " 层的末端。";
            Refresh();
        }

        // Replaces the current position with one forced room type and walks into it, so the supply room,
        // an event, an altar and the elevator are all reachable on demand.
        public void DebugForceRoom(NodeType type)
        {
            if (Run == null || Run.Map.Finished) return;
            var step = MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            if (step == null) return;
            step.Candidates.Clear();
            step.Candidates.Add(new NodeState { Floor = step.Floor, Index = step.Index, Type = type, OptionIndex = 0 });
            SelectNode(0);
        }
#endif
    }
}
