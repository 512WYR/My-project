using System;
using System.Collections;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public sealed class StageOneApp : MonoBehaviour
    {
        public GameCatalog Catalog;
        public RunState Run { get; private set; }
        public BattleEngine Battle { get; private set; }
        public StageOneView View { get; private set; }
        public ActionKind? Pending { get; private set; }
        public string InspectedId { get; private set; }
        public bool Busy { get; private set; }
        public bool HelpOpen { get; private set; }
        public bool LabOpen { get; private set; }
        public string Hint { get; private set; }
        // True while the page is waiting for a node choice, which is also when Battle is null.
        public bool AwaitingNode { get; private set; }
        public string RunEndNote { get; private set; }
        public MapState Map { get { return Run == null ? null : Run.Map; } }
        public bool AutomationMode { get; set; }
        public const uint DefaultSeed = 44972;

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
                else { Pending = null; Refresh(); }
            }
        }

        public void NewRun(double? preset = null)
        {
            StopAllCoroutines(); Busy = false; Pending = null; HelpOpen = false; LabOpen = false;
            RunEndNote = null;
            Run = RunState.Create(Catalog.Heroes.Select(a => a.ToSpec()).ToArray(), DefaultSeed, preset);
            Run.Map.Floor = 1;
            EnterRun();
        }

        // The opening floor offers three rooms instead of dropping straight into an arena.
        private void EnterRun()
        {
            StopAllCoroutines(); Busy = false; Pending = null; LabOpen = false; Battle = null;
            AwaitingNode = true;
            MapGenerator.EnsureFloor(Run.Map, Catalog.MapRules(), Run.Random, Run.PlayerSanity);
            Hint = "选择一个房间进入。标记「待实现」的房间点击即跳过。";
            Refresh();
        }

        public void SelectNode(int index)
        {
            if (Busy || HelpOpen || LabOpen || Run == null || Run.Map.Finished) return;
            var floor = Run.Map.Current;
            if (floor == null || floor.Resolved || index < 0 || index >= floor.Nodes.Count) return;
            var node = floor.Nodes[index];
            floor.ChosenIndex = node.Index;
            if (node.Type == NodeType.Boss)
            {
                FinishRun("第 " + floor.Floor + " 层首领内容待实现，本局到此结束。");
                return;
            }
            if (!node.Placeholder) { StartBattle(node); return; }
            // Placeholder rooms resolve by being skipped, which keeps the full sanity-driven
            // distribution visible from the first run.
            CompleteFloorAndAdvance("「" + node.Name + "」内容待实现，已跳过。");
        }

        public void ContinueBattle()
        {
            if (Battle == null || Battle.Outcome == BattleOutcome.Active) return;
            if (Run.Heroes.All(a => a.Dead)) { FinishRun("全队阵亡，本次冒险结束。"); return; }
            CompleteFloorAndAdvance("进入下一层。");
        }

        private void CompleteFloorAndAdvance(string hint)
        {
            StopAllCoroutines(); Busy = false; Pending = null; LabOpen = false; Battle = null;
            var floor = Run.Map.Current;
            if (floor != null) floor.Resolved = true;
            var rules = Catalog.MapRules();
            Run.Map.Floor = Math.Min(Run.Map.Floor + 1, rules.Floors);
            AwaitingNode = true;
            // Sanity is read here, after the previous node's feedback, and EnsureFloor never re-rolls
            // a floor that has already been shown.
            MapGenerator.EnsureFloor(Run.Map, rules, Run.Random, Run.PlayerSanity);
            Hint = hint + " 选择一个房间进入。";
            Refresh();
        }

        private void FinishRun(string note)
        {
            StopAllCoroutines(); Busy = false; Pending = null; LabOpen = false; Battle = null;
            AwaitingNode = true;
            Run.Map.Finished = true;
            RunEndNote = note;
            Hint = note;
            Refresh();
        }

        private void StartBattle(NodeState node)
        {
            StopAllCoroutines(); Busy = false; Pending = null; LabOpen = false;
            AwaitingNode = false;
            Battle = new BattleEngine(Run, Catalog.Enemies.Select(a => a.ToSpec()).ToArray(), Catalog.Rules());
            InspectedId = Battle.Current != null && Battle.Current.Player ? Battle.Current.Id : Run.Heroes[0].Id;
            Hint = "第 " + node.Floor + " 层 · " + node.Name + "。选择技能，再点击高亮目标；右侧可查看理智与技能概率。";
            Refresh();
            if (Battle.Current != null && !Battle.Current.Player) StartCoroutine(ContinueEnemies(Battle.Events.Count));
        }

        public void SelectAction(ActionKind kind)
        {
            if (Busy || HelpOpen || LabOpen || AwaitingNode || Battle == null) return;
            string reason = Battle.DisabledReason(kind);
            if (reason != null) { Hint = reason; Refresh(); return; }
            if (kind == ActionKind.Defend || kind == ActionKind.Focus || kind == ActionKind.Embrace || kind == ActionKind.Retreat)
            { Submit(new ActionRequest(kind)); return; }
            Pending = kind;
            Hint = kind == ActionKind.Swap ? "点击相邻的高亮队员，交换站位并消耗本次行动。" : "点击高亮目标以执行「" + Battle.SkillFor(Battle.Current, kind).Name + "」。Esc取消选择。";
            Refresh();
        }

        public void SelectActor(string id)
        {
            if (AwaitingNode || Battle == null) return;
            InspectedId = id;
            if (Pending.HasValue && !Busy && Battle.Targets(Pending.Value).Any(a => a.Id == id))
            { Submit(new ActionRequest(Pending.Value, id)); return; }
            Refresh();
        }

        private void Submit(ActionRequest request)
        {
            int from = Battle.Events.Count;
            string error;
            if (!Battle.Execute(request, out error)) { Hint = error; Refresh(); return; }
            Pending = null;
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

        public void ToggleHelp() { HelpOpen = !HelpOpen; LabOpen = false; Pending = null; Refresh(); }
        public void ToggleLab()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Busy) return;
            LabOpen = !LabOpen; HelpOpen = false; Pending = null; Refresh();
#endif
        }
        public void Refresh() { if (View) View.Render(); }

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
            SelectNode(0);
            if (Battle == null) return;
            int[] delta = { -20, -10, 0, 10 };
            for (int i = 0; i < Run.Heroes.Count; i++) Battle.ChangeSanity(Run.Heroes[i], delta[i], "验收样例");
            Battle.Finish(BattleOutcome.Retreat);
            Refresh();
        }

        public void DebugDeathDoor()
        {
            NewRun(50);
            SelectNode(0);
            Run.Heroes[0].Hp = 0;
            InspectedId = Run.Heroes[0].Id;
            Hint = "护卫处于濒死；等医师行动后使用包扎，可以恢复生命。";
            Refresh();
        }

        // Battle nodes no longer heal the party on their own; camping owns that, and camping has no
        // content yet, so the acceptance tool keeps the recovery reachable.
        public void DebugRestParty()
        {
            if (Battle == null) return;
            foreach (var hero in Run.Heroes.Where(a => !a.Dead)) hero.Hp = hero.Definition.MaxHp;
            Hint = "验收工具已整备全队生命；个人理智、精神状态和死亡记录沿用。";
            Refresh();
        }

        public void DebugEnterFloor(int floor)
        {
            if (Run == null) return;
            var rules = Catalog.MapRules();
            StopAllCoroutines(); Busy = false; Pending = null; HelpOpen = false; LabOpen = false; Battle = null;
            Run.Map.Floors.RemoveAll(f => f.Floor >= floor);
            Run.Map.Floor = Math.Min(Math.Max(floor, 1), rules.Floors);
            Run.Map.Finished = false; RunEndNote = null; AwaitingNode = true;
            MapGenerator.EnsureFloor(Run.Map, rules, Run.Random, Run.PlayerSanity);
            Hint = "验收工具已跳到第 " + Run.Map.Floor + " 层。";
            Refresh();
        }

        // Skips the current floor the same way a placeholder room does, for driving the smoke run to
        // the end without playing ten battles.
        public void DebugResolveFloor()
        {
            if (Run == null || Run.Map.Finished) return;
            var floor = Run.Map.Current;
            if (floor == null) return;
            if (floor.Floor >= Catalog.MapRules().Floors) { FinishRun("第 " + floor.Floor + " 层首领内容待实现，本局到此结束。"); return; }
            CompleteFloorAndAdvance("验收工具跳过本层。");
        }
#endif
    }
}
