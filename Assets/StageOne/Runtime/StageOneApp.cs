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
            Run = RunState.Create(Catalog.Heroes.Select(a => a.ToSpec()).ToArray(), DefaultSeed, preset);
            StartBattle(false);
        }

        public void ContinueBattle()
        {
            if (Battle.Outcome == BattleOutcome.Active || Run.Heroes.All(a => a.Dead)) return;
            // Stage-one arena preparation is explicit; stage two supplies actual rest nodes.
            foreach (var hero in Run.Heroes.Where(a => !a.Dead)) hero.Hp = hero.Definition.MaxHp;
            StartBattle(true);
        }

        private void StartBattle(bool next)
        {
            StopAllCoroutines(); Busy = false; Pending = null; LabOpen = false;
            Battle = new BattleEngine(Run, Catalog.Enemies.Select(a => a.ToSpec()).ToArray(), Catalog.Rules());
            InspectedId = Battle.Current != null && Battle.Current.Player ? Battle.Current.Id : Run.Heroes[0].Id;
            Hint = next ? "存活队员已整备生命；个人理智、精神状态和死亡记录沿用。" : "选择技能，再点击高亮目标。右侧可查看理智与技能概率。";
            Refresh();
            if (Battle.Current != null && !Battle.Current.Player) StartCoroutine(ContinueEnemies(Battle.Events.Count));
        }

        public void SelectAction(ActionKind kind)
        {
            if (Busy || HelpOpen || LabOpen) return;
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
        public void Refresh() { if (View && Battle != null) View.Render(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugSetSanity(double value)
        {
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null || Battle.Outcome != BattleOutcome.Active) return;
            Battle.ChangeSanity(actor, value - actor.Sanity, "验收工具调整");
            Hint = "已改变实际理智；本回合形态固定，下回合才重新抽取。";
            Refresh();
        }

        public void DebugMental(MentalState mental)
        {
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null) return;
            actor.Mental = mental; Refresh();
        }

        public void DebugSettle() { StopAllCoroutines(); Busy = false; LabOpen = false; Battle.Finish(BattleOutcome.Retreat); Refresh(); }

        public void DebugFeedbackExample()
        {
            NewRun(50);
            int[] delta = { -20, -10, 0, 10 };
            for (int i = 0; i < Run.Heroes.Count; i++) Battle.ChangeSanity(Run.Heroes[i], delta[i], "验收样例");
            Battle.Finish(BattleOutcome.Retreat);
            Refresh();
        }

        public void DebugDeathDoor()
        {
            NewRun(50); Run.Heroes[0].Hp = 0;
            InspectedId = Run.Heroes[0].Id;
            Hint = "护卫处于濒死；等医师行动后使用包扎，可以恢复生命。";
            Refresh();
        }
#endif
    }
}
