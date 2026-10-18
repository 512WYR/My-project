using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public sealed partial class BattleLabApp
    {
        private const int LogRenderLimit = 400;

        private void DrawLogPage()
        {
            Ui.LogScroll = GUILayout.BeginScrollView(Ui.LogScroll, GUILayout.ExpandHeight(true));
            DrawLogFilters();
            DrawLogList();
            DrawLogExport();
            GUILayout.EndScrollView();
        }

        private void DrawLogFilters()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("日志过滤");
            var allActors = Session == null ? new List<ActorState>() : Session.AllActors.Where(a => a != null).ToList();

            GUILayout.BeginHorizontal();
            GUILayout.Label("kind", GUILayout.Width(30));
            var kindLabels = new[] { "全部" }.Concat(BattleLabLog.Kinds).ToArray();
            int selectedKind = string.IsNullOrEmpty(Ui.LogKind) ? 0 : Array.IndexOf(BattleLabLog.Kinds, Ui.LogKind) + 1;
            int nextKind = BattleLabUi.SelectionGrid(selectedKind, kindLabels, 10);
            if (nextKind != selectedKind)
                Ui.LogKind = nextKind <= 0 ? "" : BattleLabLog.Kinds[nextKind - 1];
            GUILayout.FlexibleSpace();
            BattleLabUi.Toggle("只看当前场", ref Ui.LogCurrentOnly);
            BattleLabUi.Toggle("自动滚底", ref Ui.LogAutoScroll);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Actor", GUILayout.Width(38));
            DrawActorFilter(ref Ui.LogActor, allActors, "log-actor");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Target", GUILayout.Width(40));
            DrawActorFilter(ref Ui.LogTarget, allActors, "log-target");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索", GUILayout.Width(34));
            Ui.LogSearch = BattleLabUi.TextField("log-search", Ui.LogSearch, GUILayout.Width(240));
            if (BattleLabUi.Button("清空过滤"))
            {
                Ui.LogKind = "";
                Ui.LogActor = "";
                Ui.LogTarget = "";
                Ui.LogSearch = "";
                Ui.LogCurrentOnly = false;
            }
            if (BattleLabUi.Button("只看当前选中角色"))
            {
                Ui.LogActor = Ui.SelectedActorId ?? "";
                Ui.LogTarget = "";
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("显示最新 " + LogRenderLimit + " 条；导出使用完整列表。", BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private static void DrawActorFilter(ref string selectedId, List<ActorState> actors, string key)
        {
            if (actors.Count == 0)
            {
                GUILayout.Label("（没有角色）", BattleLabUi.Hint);
                return;
            }
            var labels = new[] { "全部" }.Concat(actors.Select(a => a.Name + " (" + a.Id + ")")).ToArray();
            string currentId = selectedId;
            int selected = string.IsNullOrEmpty(currentId) ? 0 : actors.FindIndex(a => a.Id == currentId) + 1;
            int next = BattleLabUi.SelectionGrid(selected, labels, 4);
            if (next != selected)
                selectedId = next <= 0 ? "" : actors[next - 1].Id;
        }

        private List<BattleLabLogEntry> FilteredLog()
        {
            if (Session == null) return new List<BattleLabLogEntry>();
            int currentBattle = Session.Run == null ? 0 : Session.Run.Battles;
            return BattleLabLog.Filter(Session.Log, Ui.LogKind, Ui.LogActor, Ui.LogTarget,
                Ui.LogSearch, currentBattle, Ui.LogCurrentOnly).ToList();
        }

        private void DrawLogList()
        {
            var filtered = FilteredLog();
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("日志（" + filtered.Count + " 条）");
            if (filtered.Count == 0)
            {
                BattleLabUi.Small("没有匹配的日志。");
            }
            else
            {
                if (Ui.LogAutoScroll && Ui.LogSeenCount != Session.Log.Count)
                {
                    Ui.LogSeenCount = Session.Log.Count;
                    Ui.LogListScroll.y = float.MaxValue;
                }
                var visible = filtered.Skip(Math.Max(0, filtered.Count - LogRenderLimit));
                Ui.LogListScroll = GUILayout.BeginScrollView(Ui.LogListScroll, GUILayout.Height(360));
                foreach (var entry in visible)
                    GUILayout.Label(BattleLabLog.FormatLine(entry), BattleLabUi.Body);
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawLogExport()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("导出");
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("复制筛选结果"))
            {
                BattleLabUi.CopyToClipboard(BattleLabLog.ToText(FilteredLog()));
                Note("筛选后的日志已复制到剪贴板。");
            }
            if (BattleLabUi.Button("导出完整日志")) ExportLog();
            GUILayout.Label("bug 备注", GUILayout.Width(54));
            Ui.BugNote = BattleLabUi.TextField("bug-note", Ui.BugNote, GUILayout.Width(260));
            if (BattleLabUi.Button("导出 bug bundle")) ExportBugBundle();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(Ui.ExportMessage))
                GUILayout.Label(Ui.ExportMessage, Ui.ExportMessageIsError ? BattleLabUi.Error : BattleLabUi.Body);
            GUILayout.Label("bundle 包含：config.json、log.txt、state.txt、invariants.txt、meta.txt、recording.json（如有）。目录：" + BattleLabJson.ExportDir, BattleLabUi.Hint);
            GUILayout.EndVertical();
        }
    }
}
