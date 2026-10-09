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
            Require(app.Battle != null && app.Run.Heroes.Count == 4, "Four-hero battle loaded");
            app.View.InterfaceFont.RequestCharactersInTexture("界限之间护卫侦察者弩手医师理智凝神躁动濒死", 24, FontStyle.Normal);
            Require("界限之间护卫理智濒死".All(c => app.View.InterfaceFont.HasCharacter(c)), "Chinese font glyphs available");
            yield return Capture("battle-1080.png", 1920, 1080);
            yield return Capture("battle-720.png", 1280, 720);
            Click("Action_Primary"); yield return null;
            Require(app.Pending == ActionKind.Primary, "Skill selection exposes target selection");
            string target = app.Battle.Targets(ActionKind.Primary).First().Id;
            Click("Actor_" + target);
            yield return WaitReady();
            Require(app.Battle.Events.Any(e => e.Kind == "action" || e.Kind == "miss"), "Target click resolves action and enemy response");

            Click("Laboratory"); yield return null; Click("Preset85"); yield return null;
            Require(app.Run.Heroes.All(h => h.Sanity == 85), "Sanity 85 preset available through UI");
            yield return Capture("high-sanity-1080.png", 1920, 1080);
            string selected = app.InspectedId;
            Click("Laboratory"); yield return null; Click("SetComposed"); yield return null; Click("Set50"); yield return null;
            Require(app.Battle.Find(selected).Mental == MentalState.Composed && app.Battle.Find(selected).Sanity == 50, "Persistent mental state survives leaving high-sanity range");
            Require(app.Battle.FormProbabilities(app.Battle.Find(selected))[0] == 0, "Persistent mental state cannot bypass activation gate");
            Click("EndBattleDebug"); yield return null; Click("ContinueBattle"); yield return null;
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

            int actions = 0;
            while (app.Battle.Outcome == BattleOutcome.Active && actions++ < 150)
            {
                yield return WaitReady();
                if (app.Battle.Outcome != BattleOutcome.Active) break;
                var battle = app.Battle;
                ActionKind chosen = ActionKind.Defend;
                var actor = battle.Current;
                if (actor == null || !actor.Player) throw new Exception("No human turn after ready");
                if (actor.Definition.Role == HeroRole.Medic && battle.DisabledReason(ActionKind.Support) == null && battle.Targets(ActionKind.Support).Any(h => h.Hp <= h.Definition.MaxHp - 9))
                    chosen = ActionKind.Support;
                else
                {
                    foreach (var k in new[] { ActionKind.Technical, ActionKind.Primary, ActionKind.Support })
                        if (battle.DisabledReason(k) == null && battle.SkillFor(actor, k).Effect == SkillEffect.Attack) { chosen = k; break; }
                }
                string targetId = battle.Targets(chosen).OrderBy(a => a.Hp).Select(a => a.Id).FirstOrDefault();
                Click("Action_" + chosen); yield return null;
                if (targetId != null) { Click("Actor_" + targetId); yield return null; }
                yield return WaitReady();
            }
            Require(app.Battle.Outcome == BattleOutcome.Victory, "Full battle completed to victory using UI buttons");
            Require(app.Battle.Settlement != null, "Victory produces sanity settlement");
            yield return Capture("victory-1080.png", 1920, 1080);
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
