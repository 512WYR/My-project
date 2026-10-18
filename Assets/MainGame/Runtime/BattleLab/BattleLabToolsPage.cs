using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public sealed partial class BattleLabApp
    {
        private void DrawToolsPage()
        {
            Ui.ToolsScroll = GUILayout.BeginScrollView(Ui.ToolsScroll, GUILayout.ExpandHeight(true));
            if (Session == null)
            {
                BattleLabUi.Label("尚未初始化。");
                GUILayout.EndScrollView();
                return;
            }
            DrawInvariantTools();
            DrawRandomTools();
            DrawReplayTools();
            DrawFuzzTools();
            DrawBundleTools();
            GUILayout.EndScrollView();
        }

        private void DrawInvariantTools()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("不变量与查询纯函数");
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("检查不变量", Session.Battle != null))
            {
                var issues = Session.CheckInvariants();
                Ui.InvariantReport = issues.Count == 0 ? "不变量检查通过。" : string.Join("\n", issues);
            }
            if (BattleLabUi.Button("检查查询是否消耗随机", Session.Battle != null))
                Ui.PurityReport = Session.CheckQueryPurity();
            if (BattleLabUi.Button("复制报告"))
                BattleLabUi.CopyToClipboard((Ui.InvariantReport ?? "") + "\n" + (Ui.PurityReport ?? ""));
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Ui.InvariantReport))
                GUILayout.Label(Ui.InvariantReport, BattleLabUi.Body);
            if (!string.IsNullOrEmpty(Ui.PurityReport))
                GUILayout.Label(Ui.PurityReport, BattleLabUi.Body);
            BattleLabUi.Small("DeathDoor 的语义是 我方 && 未死亡 && HP<=0；Hp>0 也可能是死亡（殉道）。不变量不会把 Hp<=0 直接当成死亡。");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawRandomTools()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("随机状态与覆盖");
            GUILayout.BeginHorizontal();
            GUILayout.Label("当前 State", GUILayout.Width(72));
            GUILayout.Label(Session.RandomState.ToString("X8") + " / " + Session.RandomState, BattleLabUi.Body, GUILayout.Width(180));
            GUILayout.Label("设置", GUILayout.Width(30));
            Ui.RngStateInput = BattleLabUi.TextField("rng-state", Ui.RngStateInput, GUILayout.Width(120));
            if (BattleLabUi.Button("应用", Session.Run != null))
            {
                if (Session.SetRandomStateFromText(Ui.RngStateInput, out string note)) Note(note);
                else Note(note, true);
            }
            if (BattleLabUi.Button("复制当前值")) Ui.RngStateInput = Session.RandomState.ToString("X8");
            GUILayout.Label("（十进制或 0x 十六进制）", BattleLabUi.Hint);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("强制值", GUILayout.Width(46));
            Ui.ForceValueInput = BattleLabUi.TextField("rng-force-value", Ui.ForceValueInput, GUILayout.Width(70));
            GUILayout.Label("次数", GUILayout.Width(30));
            Ui.ForceTimesInput = BattleLabUi.TextField("rng-force-times", Ui.ForceTimesInput, GUILayout.Width(48));
            BattleLabUi.Toggle("推进 State", ref Ui.ForceConsumeState);
            if (BattleLabUi.Button("强制下一次", Session.Run != null))
            {
                if (TryForceInputs(out double value, out int times))
                {
                    if (Session.ForceNextDraw(value, times, Ui.ForceConsumeState, out string note)) Note(note);
                    else Note(note, true);
                }
            }
            if (BattleLabUi.Button("锁定", Session.Run != null))
            {
                if (TryParseInvariant(Ui.ForceValueInput, out double value))
                {
                    if (Session.LockDraw(value, Ui.ForceConsumeState, out string note)) Note(note);
                    else Note(note, true);
                }
                else Note("强制值必须是数字。", true);
            }
            if (BattleLabUi.Button("清除覆盖", Session.Run != null))
            {
                if (Session.ClearRandomOverrides(out string note)) Note(note);
                else Note(note, true);
            }
            if (BattleLabUi.Button("下一次 = 0")) QuickForce(0);
            if (BattleLabUi.Button("下一次 ≈ 1")) QuickForce(0.999999);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Session.RandomOverrideActive
                ? "覆盖生效中，剩余 " + Session.RandomForcedDrawsRemaining + " 次"
                : "没有覆盖", Session.RandomOverrideActive ? BattleLabUi.Error : BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            BattleLabUi.Small("Roll(0)/Roll(1) 不抽随机，因此不会被强制值影响。覆盖只作用于当前 Run 的随机实例。");

            GUILayout.BeginHorizontal();
            GUILayout.Label("快照名", GUILayout.Width(52));
            Ui.SnapshotName = BattleLabUi.TextField("rng-snapshot-name", Ui.SnapshotName, GUILayout.Width(140));
            if (BattleLabUi.Button("保存快照", Session.Run != null))
            {
                if (Session.SaveSnapshot(Ui.SnapshotName, out string note)) Note(note);
                else Note(note, true);
            }
            GUILayout.EndHorizontal();
            if (Session.RandomSnapshots.Count == 0)
            {
                BattleLabUi.Small("没有保存的快照。");
            }
            else
            {
                foreach (var name in Session.RandomSnapshots.Keys.ToList())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(name + " = " + Session.RandomSnapshots[name].ToString("X8"), BattleLabUi.Body, GUILayout.Width(220));
                    if (BattleLabUi.Button("恢复")) { if (Session.RestoreSnapshot(name, out string note)) Note(note); else Note(note, true); }
                    if (BattleLabUi.Button("删除")) { Session.DeleteSnapshot(name); Note("已删除快照「" + name + "」。"); }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private bool TryForceInputs(out double value, out int times)
        {
            times = 1;
            if (!TryParseInvariant(Ui.ForceValueInput, out value))
            {
                Note("强制值必须是数字。", true);
                return false;
            }
            if (!int.TryParse(Ui.ForceTimesInput, out times)) times = 1;
            times = Mathf.Clamp(times, 1, 1000000);
            return true;
        }

        private static bool TryParseInvariant(string text, out double value)
        {
            return double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private void QuickForce(double value)
        {
            Ui.ForceValueInput = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            Ui.ForceTimesInput = "1";
            if (Session.ForceNextDraw(value, 1, Ui.ForceConsumeState, out string note)) Note(note);
            else Note(note, true);
        }

        private void DrawReplayTools()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("录制与重放");
            GUILayout.BeginHorizontal();
            BattleLabUi.Label("状态：" + (Session.Recording ? "录制中" : Session.RecordingInvalid ? "失效（手动修改过）" : "未录制")
                + " · 已记录 " + Session.RecordedSteps.Count + " 步");
            if (BattleLabUi.Button("开始/继续录制", !Session.Recording))
            {
                Session.StartRecording();
                Note("已开始录制。");
            }
            if (BattleLabUi.Button("停止录制", Session.Recording))
            {
                Session.StopRecording();
                Note("已停止录制。");
            }
            if (BattleLabUi.Button("清空录制"))
            {
                Session.ClearRecording();
                Note("已清空录制。");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("用录制重放（独立会话）", Session.RecordedSteps.Count > 0))
                RunReplay(Session.RecordedConfigJson, Session.RecordedSteps, Session.RecordingInvalid);
            if (BattleLabUi.Button("复制录制 JSON", Session.RecordedSteps.Count > 0))
            {
                Ui.ReplayJson = BattleLabReplay.ToJson(BuildReplayFile(Session.RecordedConfigJson, Session.RecordedSteps));
                BattleLabUi.CopyToClipboard(Ui.ReplayJson);
                Note("录制 JSON 已复制。");
            }
            if (BattleLabUi.Button("保存录制文件", Session.RecordedSteps.Count > 0))
            {
                try
                {
                    string path = BattleLabJson.SaveReplay(BuildReplayFile(Session.RecordedConfigJson, Session.RecordedSteps));
                    Ui.ExportMessage = "已保存录制：" + path;
                    Ui.ExportMessageIsError = false;
                }
                catch (Exception e)
                {
                    Ui.ExportMessage = "保存录制失败：" + e.Message;
                    Ui.ExportMessageIsError = true;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("从 JSON 重放（配置与步骤都来自粘贴内容）", BattleLabUi.Section);
            Ui.ReplayJson = BattleLabUi.TextArea("replay-json", Ui.ReplayJson, GUILayout.MinHeight(120), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("粘贴"))
            {
                Ui.ReplayJson = BattleLabUi.Clipboard();
                Note("已从剪贴板粘贴。");
            }
            if (BattleLabUi.Button("重放粘贴内容", !string.IsNullOrEmpty(Ui.ReplayJson)))
            {
                var file = BattleLabReplay.FromJson(Ui.ReplayJson);
                if (file == null) Note("无法解析录制 JSON。", true);
                else RunReplay(file.ConfigJson, file.Steps, false);
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(Ui.ReplayReport))
            {
                GUILayout.Label("重放报告", BattleLabUi.Section);
                Ui.ReplayReport = BattleLabUi.TextArea("replay-report", Ui.ReplayReport, GUILayout.MinHeight(110), GUILayout.ExpandWidth(true));
            }
            BattleLabUi.Small("手动改数值会使录制失效；重放在独立会话里执行，不会破坏当前战斗。");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private static BattleLabReplayFile BuildReplayFile(string configJson, IEnumerable<BattleLabReplayStep> steps)
        {
            return new BattleLabReplayFile
            {
                ConfigJson = configJson ?? "",
                Steps = steps == null ? new List<BattleLabReplayStep>() : steps.Where(s => s != null).ToList()
            };
        }

        private void RunReplay(string configJson, IEnumerable<BattleLabReplayStep> steps, bool invalid)
        {
            BattleLabConfig replayConfig = Config;
            if (!string.IsNullOrEmpty(configJson))
            {
                var parsed = BattleLabJson.ImportConfig(configJson, Catalog, out List<string> errors);
                if (parsed == null || errors.Count > 0)
                {
                    Ui.ReplayReport = "录制中的配置无法导入：\n" + BattleLabUi.FirstLines(errors, 8);
                    return;
                }
                parsed.Normalize();
                replayConfig = parsed;
            }
            var report = BattleLabReplay.Run(Catalog, replayConfig, steps, invalid);
            Ui.ReplayReport = report.ToString();
            Note(report.Success ? "重放成功。" : "重放出现分歧。", !report.Success);
        }

        private void DrawFuzzTools()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("Fuzz");
            var settings = Ui.FuzzSettings;
            GUILayout.BeginHorizontal();
            GUILayout.Label("场数", GUILayout.Width(30));
            settings.Battles = BattleLabUi.IntField("fuzz-battles", settings.Battles, GUILayout.Width(60));
            GUILayout.Label("每场最大步数", GUILayout.Width(90));
            settings.MaxSteps = BattleLabUi.IntField("fuzz-steps", settings.MaxSteps, GUILayout.Width(60));
            GUILayout.Label("种子", GUILayout.Width(30));
            long seed = BattleLabUi.LongField("fuzz-seed", settings.Seed, GUILayout.Width(90));
            settings.Seed = (uint)Math.Max(0, Math.Min(uint.MaxValue, seed));
            BattleLabUi.Toggle("随机数值", ref settings.RandomizeStats);
            BattleLabUi.Toggle("随机装备", ref settings.RandomizeEquipment);
            BattleLabUi.Toggle("随机规则", ref settings.RandomizeRules);
            BattleLabUi.Toggle("失败即停", ref settings.StopOnFailure);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool running = Ui.FuzzRoutine != null;
            if (BattleLabUi.Button("开始 Fuzz", !running)) StartFuzz();
            if (BattleLabUi.Button("停止", running)) StopFuzz();
            if (Ui.FuzzProgress != null)
            {
                var p = Ui.FuzzProgress;
                GUILayout.Label("进度 " + p.Completed + "/" + p.Total + " · 失败 " + p.Failed
                    + " · 当前种子 " + p.CurrentSeed, BattleLabUi.Body);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Ui.FuzzReport))
            {
                Ui.FuzzReport = BattleLabUi.TextArea("fuzz-report", Ui.FuzzReport, GUILayout.MinHeight(140), GUILayout.ExpandWidth(true));
                if (BattleLabUi.Button("保存 fuzz 报告"))
                {
                    try
                    {
                        string path = BattleLabJson.SaveText("fuzz-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt", Ui.FuzzReport);
                        Ui.ExportMessage = "已保存 fuzz 报告：" + path;
                        Ui.ExportMessageIsError = false;
                    }
                    catch (Exception e)
                    {
                        Ui.ExportMessage = "保存失败：" + e.Message;
                        Ui.ExportMessageIsError = true;
                    }
                }
            }
            BattleLabUi.Small("Fuzz 在独立 Run 里运行，失败会附带配置、日志与状态（见报告和导出目录）。界面每场让出一帧。");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawBundleTools()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("导出与目录");
            GUILayout.BeginHorizontal();
            GUILayout.Label("bug 备注", GUILayout.Width(56));
            Ui.BugNote = BattleLabUi.TextField("tools-bug-note", Ui.BugNote, GUILayout.Width(320));
            if (BattleLabUi.Button("导出 bug bundle")) ExportBugBundle();
            if (BattleLabUi.Button("保存配置")) ExportConfig();
            if (BattleLabUi.Button("保存日志")) ExportLog();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Ui.ExportMessage))
                GUILayout.Label(Ui.ExportMessage, Ui.ExportMessageIsError ? BattleLabUi.Error : BattleLabUi.Body);
            GUILayout.Label("目录：" + BattleLabJson.ExportDir, BattleLabUi.Hint);
            GUILayout.EndVertical();
        }
    }
}
