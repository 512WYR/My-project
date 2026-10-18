using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Scene entry point. The console is IMGUI on purpose: no prefab, no serialized references, and the
    // catalog is loaded from Resources when the scene field is empty.
    public sealed partial class BattleLabApp : MonoBehaviour
    {
        public GameCatalog Catalog;
        public BattleLabConfig Config { get; private set; }
        public BattleLabSession Session { get; private set; }
        public BattleLabUiState Ui = new BattleLabUiState();
        public string FatalError { get; private set; }

        private float nextAutoTime;
        private static readonly string[] Tabs = { "配置", "战斗", "日志", "工具" };

        private void Awake()
        {
            Catalog = Catalog ? Catalog : Resources.Load<GameCatalog>("StageOne/GameCatalog");
            if (!Catalog)
            {
                FatalError = "找不到 GameCatalog：请确认 Assets/StageOne/Resources/StageOne/GameCatalog.asset 存在。";
                return;
            }
            Config = BattleLabFactory.CreateDefault(Catalog);
            Session = new BattleLabSession(Catalog, Config);
            if (!Session.NewRun(out string error)) Note(error, true);
            Ui.SelectedActorId = Session.Current == null ? null : Session.Current.Id;
            SyncConfigJson();
        }

        private void Update()
        {
            if (Session == null) return;
            HandleShortcuts();
            AutoDrive();
        }

        private void HandleShortcuts()
        {
            // Escape drops the current selection; the other keys mirror the battle page buttons.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Ui.SelectedSkillIndex = -1;
                Ui.SelectedTargetId = null;
                Ui.SelectedConsumableIndex = -1;
            }
            if (Input.GetKeyDown(KeyCode.F5)) StartBattleNow();
            if (Input.GetKeyDown(KeyCode.F6)) StepOnce();
            if (Input.GetKeyDown(KeyCode.F7)) RunToPlayer();
            if (Input.GetKeyDown(KeyCode.F8)) RunToEnd();
            if (Input.GetKeyDown(KeyCode.F9)) SkipTurn();
        }

        private void AutoDrive()
        {
            if (Config == null || !Config.AutoEnemy || Session == null) return;
            if (!Session.Active || Session.Current == null || Session.Current.Player) return;
            if (Time.unscaledTime < nextAutoTime) return;
            nextAutoTime = Time.unscaledTime + Mathf.Max(0.05f, Config.AutoEnemyInterval);
            if (!Session.ExecuteEnemy(out string note)) Note(note, true);
        }

        private void OnGUI()
        {
            BattleLabUi.EnsureSkin();
            if (!string.IsNullOrEmpty(FatalError))
            {
                GUILayout.BeginArea(new Rect(12, 12, Mathf.Max(320, Screen.width - 24), Mathf.Max(200, Screen.height - 24)));
                GUILayout.Label("Battle Lab 无法启动", BattleLabUi.Title);
                BattleLabUi.ErrorLabel(FatalError);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginArea(new Rect(8, 8, Mathf.Max(320, Screen.width - 16), Mathf.Max(240, Screen.height - 16)));
            DrawTopBar();
            Ui.Tab = GUILayout.Toolbar(Ui.Tab, Tabs);
            GUILayout.Space(4);
            switch (Ui.Tab)
            {
                case 0: DrawConfigPage(); break;
                case 1: DrawBattlePage(); break;
                case 2: DrawLogPage(); break;
                default: DrawToolsPage(); break;
            }
            DrawBottomBar();
            GUILayout.EndArea();
        }

        private void DrawTopBar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Battle Lab", BattleLabUi.Title, GUILayout.Width(92));
            GUILayout.Label("Catalog：" + (Catalog ? Catalog.name : "缺失"), BattleLabUi.Body);
            if (Session != null && Session.Battle != null)
            {
                var battle = Session.Battle;
                GUILayout.Label("第 " + (Session.Run == null ? 0 : Session.Run.Battles) + " 场 · R" + battle.Round
                    + " · " + battle.Outcome
                    + " · Current：" + (battle.Current == null ? "无" : battle.Current.Name)
                    + " · RNG " + Session.RandomState.ToString("X8"), BattleLabUi.Body);
            }
            else GUILayout.Label("尚未开始战斗", BattleLabUi.Body);
            GUILayout.FlexibleSpace();
            if (BattleLabUi.Button("新建 Run", Session != null)) NewRunNow();
            if (BattleLabUi.Button("应用配置并开战", Session != null)) StartBattleNow();
            if (BattleLabUi.Button("当前 Run 再开一场", Session != null && Session.Run != null)) StartBattleWithRunNow();
            GUILayout.EndHorizontal();
        }

        private void DrawBottomBar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (Session != null)
            {
                GUILayout.Label(Session.LastNote ?? "", Session.LastNoteIsError ? BattleLabUi.Error : BattleLabUi.Body);
                GUILayout.FlexibleSpace();
                string recording = Session.Recording ? "录制中" : Session.RecordingInvalid ? "录制失效" : "未录制";
                GUILayout.Label(recording + " · " + Session.RecordedSteps.Count + " 步", BattleLabUi.Hint, GUILayout.Width(150));
                if (Session.Battle != null)
                {
                    int issues = BattleLabInvariants.Check(Session.Battle).Count;
                    GUILayout.Label(issues == 0 ? "不变量正常" : "不变量告警 " + issues,
                        issues == 0 ? BattleLabUi.Hint : BattleLabUi.Error, GUILayout.Width(110));
                }
            }
            GUILayout.EndHorizontal();
        }

        // ---- lifecycle helpers shared by pages ----

        public void NewRunNow()
        {
            if (Session == null) return;
            if (Session.NewRun(out string error))
            {
                ClearSelection();
                Note("已重建 Run（种子 " + Config.SeedText + "）。");
            }
            else Note(error, true);
        }

        public void StartBattleNow()
        {
            if (Session == null) return;
            if (Session.StartBattle(out string error))
            {
                ClearSelection();
                Ui.SelectedActorId = Session.Current == null ? null : Session.Current.Id;
                Note("战斗开始。");
            }
            else Note(error, true);
        }

        public void StartBattleWithRunNow()
        {
            if (Session == null) return;
            if (Session.StartBattleWithCurrentRun(out string error))
            {
                ClearSelection();
                Ui.SelectedActorId = Session.Current == null ? null : Session.Current.Id;
                Note("已用当前 Run 再开一场。");
            }
            else Note(error, true);
        }

        public void ApplyPresetNow(BattleLabPreset preset)
        {
            if (Session == null) return;
            BattleLabFactory.ApplyPreset(Config, Catalog, preset);
            Session.NewRun(out string error);
            ClearSelection();
            SyncConfigJson();
            Note(error ?? ("已应用预设：" + preset + "。"), error != null);
        }

        public void ReplaceConfig(BattleLabConfig config)
        {
            if (config == null) return;
            Config = config;
            Config.Normalize();
            Session = new BattleLabSession(Catalog, Config);
            Session.NewRun(out string error);
            ClearSelection();
            SyncConfigJson();
            if (error != null) Note(error, true);
        }

        public void SyncConfigJson()
        {
            Ui.ConfigJson = BattleLabJson.ToJson(Config, true);
        }

        public void Note(string text, bool error = false)
        {
            if (Session != null) Session.SetNote(text, error);
        }

        public void StepOnce()
        {
            if (Session == null) return;
            if (!Session.Step(out string note)) Note(note, true);
            else if (!string.IsNullOrEmpty(note)) Note(note);
        }

        public void RunToPlayer()
        {
            if (Session == null) return;
            Session.RunUntilPlayerTurn(out string note);
            Note(note, Session.Battle != null && Session.Battle.Outcome == BattleOutcome.Active && Session.Current != null && !Session.Current.Player);
        }

        public void RunToEnd()
        {
            if (Session == null) return;
            Session.RunToEnd(out string note);
            Note(note);
        }

        public void SkipTurn()
        {
            if (Session == null) return;
            if (!Session.SkipTurn(out string note)) Note(note, true);
            else Note(note);
        }

        public void ClearSelection()
        {
            Ui.SelectedSkillIndex = -1;
            Ui.SelectedTargetId = null;
            Ui.SelectedConsumableIndex = -1;
            Ui.ConsumableTargetId = null;
            Ui.ActiveSkillHint = "";
        }

        public ActorState SelectedActor
        {
            get
            {
                if (Session == null || string.IsNullOrEmpty(Ui.SelectedActorId)) return null;
                return Session.AllActors.FirstOrDefault(a => a != null && a.Id == Ui.SelectedActorId);
            }
        }

        // ---- fuzz coroutine ----

        public void StartFuzz()
        {
            if (Session == null) return;
            if (Ui.FuzzRoutine != null) StopCoroutine(Ui.FuzzRoutine);
            Ui.FuzzRoutine = StartCoroutine(FuzzRoutine());
        }

        public void StopFuzz()
        {
            if (Ui.FuzzRoutine != null)
            {
                StopCoroutine(Ui.FuzzRoutine);
                Ui.FuzzRoutine = null;
            }
            if (Ui.FuzzProgress != null) Ui.FuzzProgress.Running = false;
            Note("Fuzz 已停止。");
        }

        private IEnumerator FuzzRoutine()
        {
            var settings = Ui.FuzzSettings;
            int total = Mathf.Clamp(settings.Battles, 1, 100000);
            settings.Battles = total;
            var progress = new BattleLabFuzzProgress();
            progress.Reset(total);
            Ui.FuzzProgress = progress;
            Ui.FuzzReport = "运行中…";
            for (int i = 0; i < total; i++)
            {
                BattleLabFuzz.RunOne(Catalog, settings, i, progress);
                Ui.FuzzReport = progress.ReportText();
                if (settings.StopOnFailure && progress.Failed > 0) break;
                yield return null;
            }
            progress.Running = false;
            Ui.FuzzRoutine = null;
            Ui.FuzzReport = progress.ReportText();
            Note(progress.Failed == 0 ? "Fuzz 完成，全部通过。" : "Fuzz 完成，失败 " + progress.Failed + " 场。", progress.Failed > 0);
        }

        // ---- export helpers ----

        public void ExportConfig()
        {
            try
            {
                string path = BattleLabJson.SaveConfig(Config);
                Ui.ExportMessage = "已保存配置：" + path;
                Ui.ExportMessageIsError = false;
            }
            catch (Exception e)
            {
                Ui.ExportMessage = "保存失败：" + e.Message;
                Ui.ExportMessageIsError = true;
            }
        }

        public void ExportLog()
        {
            try
            {
                string path = BattleLabJson.SaveText("log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                    BattleLabLog.ToText(Session == null ? null : Session.Log));
                Ui.ExportMessage = "已保存日志：" + path;
                Ui.ExportMessageIsError = false;
            }
            catch (Exception e)
            {
                Ui.ExportMessage = "保存失败：" + e.Message;
                Ui.ExportMessageIsError = true;
            }
        }

        public void ExportBugBundle()
        {
            string error;
            string path = BattleLabJson.BuildBugBundle(Catalog, Session, Ui.BugNote, out error);
            if (path == null)
            {
                Ui.ExportMessage = "导出 bug 包失败：" + error;
                Ui.ExportMessageIsError = true;
            }
            else
            {
                Ui.ExportMessage = "已导出 bug 包：" + path;
                Ui.ExportMessageIsError = false;
            }
        }
    }
}
