#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ruin.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    public sealed class StageOneSmoke : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> layoutWarnings = new List<string>();
            public List<string> screenshots = new List<string>();
        }
        private StageOneApp app;
        private Report report = new Report();
        private string directory;

        private IEnumerator Start()
        {
            app = GetComponent<StageOneApp>(); app.AutomationMode = true;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--qa-out");
            directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "StageOneQA");
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += OnLog;
            yield return new WaitForSeconds(0.4f);
            // Drive nested enumerators manually so any failure still writes a report and exits.
            var stack = new Stack<IEnumerator>(); stack.Push(RunChecks());
            while (stack.Count > 0)
            {
                object current = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                catch (Exception ex) { report.errors.Add(ex.ToString()); stack.Clear(); break; }
                if (!moved) { stack.Pop(); continue; }
                var child = current as IEnumerator;
                if (child != null) stack.Push(child); else yield return current;
            }
            report.passed = report.errors.Count == 0;
            File.WriteAllText(Path.Combine(directory, "smoke-report.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= OnLog;
            Debug.Log("STAGE_ONE_SMOKE " + (report.passed ? "PASS" : "FAIL"));
            Application.Quit(report.passed ? 0 : 1);
        }

        private IEnumerator RunChecks()
        {
            // 开局：路线的第一个位置是第一层的初始增益房，固定不抽随机。
            Require(app.AwaitingNode && app.Map != null && app.Map.Current != null, "Run opens on the route page");
            Require(app.Map.Floor == 1 && app.Map.Current.Index == 0, "Run opens on the first position of the first floor");
            Require(app.Map.Current.Fixed && app.Map.Current.Candidates.Count == 1 && app.Map.Current.Candidates[0].Type == NodeType.Opening,
                "The opening position is a fixed buff room");
            app.View.InterfaceFont.RequestCharactersInTexture("界限之间战士盾卫枪手奶妈理智凝神躁动濒死祭坛事件补给站电梯井首领增益坚韧锋锐清明部位义肢断修复重创", 24, FontStyle.Normal);
            Require("界限之间战士盾卫枪手奶妈理智濒死祭坛事件补给站电梯井首领增益部位义肢断修复".All(c => app.View.InterfaceFont.HasCharacter(c)), "Chinese font glyphs available");
            yield return Capture("map-1080.png", 1920, 1080);
            yield return Capture("map-720.png", 1280, 720);

            // 已经抽出的位置保持固定：重新渲染既不改动候选，也不消耗随机序列。
            var shownTypes = app.Map.Current.Candidates.Select(n => n.Type).ToArray();
            uint shownState = app.Run.Random.State;
            app.Refresh(); yield return null; app.Refresh(); yield return null;
            Require(app.Map.Current.Candidates.Select(n => n.Type).SequenceEqual(shownTypes) && app.Run.Random.State == shownState,
                "A drawn position never re-rolls and consumes no draws");

            // 初始增益：三选一，全队生效，部位上限跟着生命走。
            var maxBefore = app.Run.Heroes.Select(h => h.Definition.MaxHp).ToArray();
            var torsoBefore = app.Run.Heroes.Select(h => LimbSystem.Get(h, LimbPart.Torso).MaxHp).ToArray();
            Click("Node_0"); yield return null;
            Require(app.Room == RoomView.Buff && app.Battle == null, "The opening room opens the buff page");
            yield return Capture("buff-1080.png", 1920, 1080);
            Require(FindObjectsOfType<Button>().Any(b => b.name == "Buff_Toughness") && FindObjectsOfType<Button>().Any(b => b.name == "Buff_Clarity"),
                "All three opening buffs are offered");
            Click("Buff_Toughness"); yield return null;
            Require(app.Map.BuffChosen && app.Map.Buff == GrowthBuff.Toughness, "Choosing a buff records the pick");
            Require(app.Run.Heroes.Select(h => h.Definition.MaxHp).SequenceEqual(maxBefore.Select(v => v + OpeningBuffs.ToughnessHealth)),
                "Toughness raises every hero's max health");
            Require(app.Run.Heroes.Select((h, i) => LimbSystem.Get(h, LimbPart.Torso).MaxHp > torsoBefore[i]).All(grew => grew),
                "Limb caps follow the buffed health");
            Click("LeaveRoom"); yield return null;
            Require(app.AwaitingNode && app.Room == RoomView.Map, "Leaving the buff room returns to the route");
            Require(!app.Map.Current.Fixed && app.Map.Current.Candidates.Count >= 1 && app.Map.Current.Candidates.Count <= 3,
                "The next position draws one to three candidates");
            Require(app.Map.Current.Candidates.All(n => n.Type == NodeType.Battle || n.Type == NodeType.EliteBattle
                || n.Type == NodeType.Altar || n.Type == NodeType.Event || n.Type == NodeType.Treasure),
                "Drawable positions only offer the sampleable rooms");
            // Candidates inside one position never repeat a room type, except events: two events side by
            // side read as two leads, so they are allowed to pair up.
            var drawn = app.Map.Current.Candidates.Select(n => n.Type).Where(t => t != NodeType.Event).ToList();
            Require(drawn.Distinct().Count() == drawn.Count, "One position never offers the same non-event room twice");
            yield return Capture("route-1080.png", 1920, 1080);

            yield return EnterBattle();
            Require(app.Battle != null && app.Run.Heroes.Count == 4, "Four-hero battle loaded");
            yield return Capture("battle-1080.png", 1920, 1080);
            yield return Capture("battle-720.png", 1280, 720);
            // 行动区只有当前形态的技能加一个换位，所以这里按下标找第一个按钮可用的技能。
            int opening = FirstUsableSkill(SkillEffect.Attack);
            Require(opening >= 0 || app.Battle.SwapDisabledReason() == null, "The action area always offers something to do");
            if (opening >= 0)
            {
                Click("Skill_" + opening); yield return null;
                Require(app.PendingSkill == opening, "Skill selection exposes target selection");
                string target = app.Battle.Targets(opening).First().Id;
                Click("Actor_" + target);
                yield return WaitReady();
            }
            else
            {
                Click("Swap"); yield return null;
                Require(app.PendingSwap, "Swap selection exposes neighbour selection");
                Click("Actor_" + app.Battle.SwapTargets().First().Id);
                yield return WaitReady();
            }
            Require(app.Battle.Events.Any(e => e.Kind == "action" || e.Kind == "miss" || e.Kind == "heal" || e.Kind == "move"),
                "Target click resolves action and enemy response");

            Click("Laboratory"); yield return null; Click("Preset85"); yield return null;
            yield return EnterBattle();
            Require(app.Run.Heroes.All(h => h.Sanity == 85), "Sanity 85 preset available through UI");
            yield return Capture("high-sanity-1080.png", 1920, 1080);
            string selected = app.InspectedId;
            Click("Laboratory"); yield return null; Click("SetComposed"); yield return null; Click("Set50"); yield return null;
            Require(app.Battle.Find(selected).Mental == MentalState.Composed && app.Battle.Find(selected).Sanity == 50, "Persistent mental state survives leaving high-sanity range");
            Require(app.Battle.FormProbabilities(app.Battle.Find(selected))[0] == 0, "Persistent mental state cannot bypass activation gate");
            Click("EndBattleDebug"); yield return null; Click("ContinueBattle"); yield return null;
            Require(app.AwaitingNode, "Settlement continue returns to the node page");
            yield return EnterBattle();
            Require(app.Battle.Find(selected).Mental == MentalState.Composed && app.Battle.Find(selected).Sanity == 50, "Next battle preserves personal sanity and mental state");

            Click("Laboratory"); yield return null; Click("FeedbackExample"); yield return null;
            Require(Math.Abs(app.Run.PlayerSanity - 47.5) < 0.000001, "Feedback example produces player sanity 47.5");
            yield return Capture("settlement-1080.png", 1920, 1080);
            Click("ContinueBattle"); yield return null;
            Require(Math.Abs(app.Run.PlayerSanity - 47.5) < 0.000001, "Continuing does not duplicate feedback");
            Click("Laboratory"); yield return null; Click("DeathDoorExample"); yield return null;
            Require(app.Run.Heroes[0].DeathDoor, "Death-door scenario loads");
            yield return Capture("deathdoor-720.png", 1280, 720);
            Click("Help"); yield return null;
            yield return Capture("help-720.png", 1280, 720);
            Click("CloseHelp"); yield return null;
            Click("NewRun"); yield return null;
            yield return EnterBattle();

            yield return Fight();
            Require(app.Battle.Outcome == BattleOutcome.Victory, "Full battle completed to victory using UI buttons");
            Require(app.Battle.Settlement != null, "Victory produces sanity settlement");
            yield return Capture("victory-1080.png", 1920, 1080);

            // 结算只把队伍往前推一个位置，不换层；走过之后那个位置仍然固定不变。
            Click("ContinueBattle"); yield return null;
            Require(app.AwaitingNode && app.Battle == null && app.Map.Floor == 1, "A won battle walks to the next position on the same floor");
            Require(!string.IsNullOrEmpty(app.MapNote) && app.MapNote.Contains("补给"),
                "The route page reports what the won fight paid out");
            Require(FindObjectsOfType<Text>().Any(t => t.name == "MapNote" && t.text == app.MapNote),
                "That reward line is actually drawn on the route page");
            var nextTypes = app.Map.Current.Candidates.Select(n => n.Type).ToArray();
            uint nextState = app.Run.Random.State;
            app.Refresh(); yield return null; app.Refresh(); yield return null;
            Require(app.Map.Current.Candidates.Select(n => n.Type).SequenceEqual(nextTypes) && app.Run.Random.State == nextState,
                "A drawn position stays fixed after a battle");

            // 撤退不发奖励。验收工具结束战斗走的是撤退，正好用来验这条规则。
            int suppliesBefore = app.Map.Supplies;
            int stockBefore = app.Run.Prosthetics.Count;
            app.DebugForceRoom(NodeType.EliteBattle);
            yield return WaitReady();
            Require(app.Battle != null, "The debug tool can open an elite battle");
            app.DebugSettle();
            yield return WaitReady();
            Click("ContinueBattle"); yield return null;
            Require(app.Map.Supplies == suppliesBefore && app.Run.Prosthetics.Count == stockBefore,
                "A retreat pays no supplies and drops no prosthetic");
            Require(!string.IsNullOrEmpty(app.MapNote) && app.MapNote.Contains("撤退"),
                "The route page reports the retreat");

            // 事件房间：内容与可见选项在进房时冻结，隐藏选项不能选，也改不动任何状态。
            Click("Laboratory"); yield return null; Click("ForceEvent"); yield return null;
            Require(app.Room == RoomView.Event && app.Battle == null, "The event room opens the event page");
            yield return Capture("event-1080.png", 1920, 1080);
            var eventNode = app.ActiveNode;
            Require(eventNode != null && !string.IsNullOrEmpty(eventNode.EventId) && eventNode.OptionMask != null && eventNode.OptionMask[0],
                "The event rolls content and always offers the base option");
            var eventSpec = Exploration.Find(app.Catalog.MapRules(), eventNode.EventId);
            Require(eventSpec != null && eventSpec.Options.Length == eventNode.OptionMask.Length,
                "The frozen option mask matches the rolled content");
            for (int i = 0; i < eventNode.OptionMask.Length; i++)
            {
                var option = FindObjectsOfType<Button>().FirstOrDefault(b => b.name == "EventOption_" + i);
                if (option == null)
                {
                    // Every option is rendered, even the frozen-out ones, so a hidden option is visible as
                    // a disabled card rather than vanishing.
                    throw new Exception("Event option card missing: " + i);
                }
                if (option.interactable != eventNode.OptionMask[i]) throw new Exception("A frozen-out option is still selectable: " + i);
            }
            double sanityBefore = app.Run.PlayerSanity;
            int hidden = Array.FindIndex(eventNode.OptionMask, m => !m);
            if (hidden >= 0)
            {
                app.ChooseEventOption(hidden); yield return null;
                Require(!app.EventChosen && Math.Abs(app.Run.PlayerSanity - sanityBefore) < 0.000001,
                    "A frozen-out option is refused and changes nothing");
            }
            app.ChooseEventOption(eventSpec.Options.Length); yield return null;
            Require(!app.EventChosen, "An out-of-range option is refused");
            int open = Array.FindIndex(eventNode.OptionMask, m => m);
            var picked = eventSpec.Options[open];
            Click("EventOption_" + open); yield return null;
            Require(app.EventChosen && eventNode.OptionChosen == open, "A visible option resolves the room");
            Require(Math.Abs(app.Run.PlayerSanity - SanityProbability.Clamp(sanityBefore + picked.SanityDelta, 0, 100)) < 0.000001,
                "The chosen option applies its sanity cost");
            Click("LeaveRoom"); yield return null;
            Require(app.AwaitingNode && app.Room == RoomView.Map, "Leaving the event returns to the route");

            // 补给站：治疗、修复重伤部位、为断肢安装义肢。工坊的全部职责都并进了这一页。
            app.DebugInjureLimb(LimbPart.Arm); yield return null;
            app.DebugSeverLimb(LimbPart.Head); yield return null;
            var wounded = app.Run.Heroes.FirstOrDefault(h => LimbSystem.ConditionOf(h, LimbPart.Arm) == LimbCondition.Injured);
            var amputee = app.Run.Heroes.FirstOrDefault(h => LimbSystem.ConditionOf(h, LimbPart.Head) == LimbCondition.Severed);
            Require(wounded != null && amputee != null, "The acceptance tool injures one part and severs another");
            Click("Laboratory"); yield return null; Click("ForceSupply"); yield return null;
            Require(app.Room == RoomView.Supply && app.Battle == null, "The supply room opens the supply page");
            yield return Capture("supply-1080.png", 1920, 1080);
            Require(FindObjectsOfType<Button>().Any(b => b.name == "Repair_" + wounded.Id + "_Arm"), "An injured part offers a repair");
            Click("Repair_" + wounded.Id + "_Arm"); yield return null;
            Require(LimbSystem.ConditionOf(wounded, LimbPart.Arm) == LimbCondition.Intact, "The supply room repairs an injured part");
            Require(FindObjectsOfType<Text>().Any(t => t.name == "Part_Head_Need"), "A severed part without a matching prosthetic says so");
            app.DebugGrantProsthetic(LimbPart.Head); yield return null;
            Require(FindObjectsOfType<Button>().Any(b => b.name == "Install_" + amputee.Id + "_Head"), "A held prosthetic offers an install");
            Click("Install_" + amputee.Id + "_Head"); yield return null;
            Require(LimbSystem.HasProsthetic(amputee, LimbPart.Head), "The supply room fits a prosthetic");
            Require(app.Run.Prosthetics.All(p => p.Part != LimbPart.Head), "Fitting consumes the prosthetic from the stock");
            Click("SupplyHeal"); yield return null;
            Require(app.Map.SupplyHealed, "The supply room heals the party once per visit");
            Click("SupplyCalm"); yield return null;
            Require(app.Map.SupplyCalmed, "The supply room steadies the party once per visit");
            Click("LeaveRoom"); yield return null;
            Require(app.AwaitingNode && app.Room == RoomView.Map, "Leaving the supply room returns to the route");

            // 电梯井：纯过渡页，只换层，不整备生命。
            Click("Laboratory"); yield return null; Click("ForceElevator"); yield return null;
            Require(app.Room == RoomView.Elevator && app.Battle == null, "The elevator opens a transition page");
            yield return Capture("elevator-1080.png", 1920, 1080);
            int hpAtElevator = app.Run.Heroes.Where(h => !h.Dead).Sum(h => h.Hp);
            Click("ElevatorDown"); yield return null;
            Require(app.AwaitingNode && app.Room == RoomView.Map && app.Map.Floor == 2, "The elevator goes down exactly one floor");
            Require(app.Map.Current.Fixed && app.Map.Current.Candidates[0].Type == NodeType.Supply,
                "The second floor opens on a fixed supply room");
            Require(app.Run.Heroes.Where(h => !h.Dead).Sum(h => h.Hp) == hpAtElevator, "Going down does not heal the party");

            // 首领：最后一层的末端。胜利即通关，撤退或阵亡则本局结束。
            app.DebugEnterFloor(app.Catalog.MapRules().Floors); yield return null;
            Require(app.Map.Floor == app.Catalog.MapRules().Floors, "The acceptance tool jumps to the final floor");
            app.DebugSkipToFloorEnd(); yield return null;
            Require(app.Map.Current.Fixed && app.Map.Current.Candidates[0].Type == NodeType.Boss,
                "The final floor ends on a single boss room");
            Click("Node_0"); yield return WaitReady();
            Require(app.Battle != null && app.Battle.Enemies.All(e => e.Name.StartsWith("首领")),
                "The boss room scales the enemies into bosses");
            yield return Fight();
            bool bossWon = app.Battle.Outcome == BattleOutcome.Victory;
            Click("ContinueBattle"); yield return null;
            Require(app.Map.Finished, "The boss room always ends the run");
            Require(app.Map.Cleared == bossWon, "Only a won boss fight clears the run");
            yield return Capture("map-end-1080.png", 1920, 1080);
            Click("MapNewRun"); yield return null;
            Require(!app.Map.Finished && app.Map.Floor == 1 && app.Map.Current.Candidates[0].Type == NodeType.Opening,
                "A new run restarts at the opening buff room");

            // 战斗页的部位面板：断肢与重伤都要能在队伍卡片下方看到。
            app.DebugSeverLimb(LimbPart.Arm); yield return null;
            app.DebugInjureLimb(LimbPart.Leg); yield return null;
            yield return EnterBattle();
            Require(FindObjectsOfType<Text>().Any(t => t.name == "LimbHeading"), "The inspector carries the part panel");
            Require(FindObjectsOfType<Text>().Any(t => t.text.Contains("断肢")), "A severed part shows on the battle page");
            yield return Capture("limbs-1080.png", 1920, 1080);
        }

        // The route draws its own rooms, so a battle on demand goes through the acceptance tool instead of
        // whatever the seed happened to put in front of the party.
        private IEnumerator EnterBattle()
        {
            if (app.Room != RoomView.Map) throw new Exception("EnterBattle needs the route page, was " + app.Room);
            app.DebugForceRoom(NodeType.Battle);
            yield return WaitReady();
            if (app.Battle == null) throw new Exception("Battle room did not open a battle");
        }

        // The greedy policy the smoke uses to grind a fight to its end through the real UI buttons.
        private IEnumerator Fight()
        {
            int actions = 0;
            while (app.Battle.Outcome == BattleOutcome.Active && actions++ < 200)
            {
                yield return WaitReady();
                if (app.Battle.Outcome != BattleOutcome.Active) break;
                yield return PlayOneAction();
            }
            if (app.Battle.Outcome == BattleOutcome.Active) throw new Exception("Battle did not finish within 200 actions");
        }

        // 优先打人，其次治疗或增益，都不行才换位。低理智形态只留一个技能，如果那个技能是治疗，
        // 满血时就会没有合法目标，这时唯一还能做的就是换位。
        private IEnumerator PlayOneAction()
        {
            var battle = app.Battle;
            var actor = battle.Current;
            if (actor == null || !actor.Player) throw new Exception("No human turn after ready");
            int chosen = FirstUsableSkill(SkillEffect.Attack);
            if (chosen < 0) chosen = FirstUsableSkill(null);
            if (chosen >= 0)
            {
                string targetId = battle.Targets(chosen).OrderBy(a => a.Hp).Select(a => a.Id).FirstOrDefault();
                Click("Skill_" + chosen); yield return null;
                if (targetId != null) { Click("Actor_" + targetId); yield return null; }
                yield return WaitReady();
                yield break;
            }
            if (battle.SwapDisabledReason() != null) throw new Exception("No usable action for " + actor.Name);
            string neighbour = battle.SwapTargets().Select(a => a.Id).First();
            Click("Swap"); yield return null;
            Click("Actor_" + neighbour); yield return null;
            yield return WaitReady();
        }

        // 第一个按钮可用、且有合法目标的技能下标。effect 为 null 时不筛效果。
        private int FirstUsableSkill(SkillEffect? effect)
        {
            var battle = app.Battle;
            var skills = battle.SkillsFor(battle.Current);
            for (int i = 0; i < skills.Length; i++)
            {
                if (effect.HasValue && skills[i].Effect != effect.Value) continue;
                if (battle.SkillDisabledReason(i) != null) continue;
                if (battle.Targets(i).Any() || skills[i].Targeting == SkillTargeting.All) return i;
            }
            return -1;
        }

        private IEnumerator WaitReady()
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (app.Busy)
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception("UI action timed out");
                yield return null;
            }
            yield return null;
        }

        private void Click(string name)
        {
            var button = FindObjectsOfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.name == name);
            if (!button || !button.interactable) throw new Exception("Button unavailable: " + name);
            Canvas.ForceUpdateCanvases();
            var r = (RectTransform)button.transform;
            var position = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = position };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button) throw new Exception("Button raycast blocked: " + name);
            button.onClick.Invoke();
        }

        private IEnumerator Capture(string filename, int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSeconds(0.3f);
            Canvas.ForceUpdateCanvases();
            foreach (var text in FindObjectsOfType<Text>())
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                if (text.preferredHeight > text.rectTransform.rect.height + 2)
                    report.layoutWarnings.Add(filename + ": " + text.name + " preferred=" + text.preferredHeight + " available=" + text.rectTransform.rect.height);
            }
            string path = Path.Combine(directory, filename);
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path))
            { if (Time.realtimeSinceStartup > until) throw new Exception("Screenshot timed out: " + filename); yield return null; }
            yield return new WaitForSeconds(0.1f);
            report.screenshots.Add(filename + " " + Screen.width + "x" + Screen.height);
        }

        private void Require(bool condition, string name) { if (!condition) throw new Exception(name); report.checks.Add(name); }
        private void OnLog(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
    }
}
#endif
