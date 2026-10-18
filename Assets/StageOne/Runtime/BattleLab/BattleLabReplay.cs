using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // One recorded action plus the engine state it must reproduce. Storing the before/after state turns a
    // replay from "did it look similar" into an exact first-divergence report.
    [Serializable]
    public sealed class BattleLabReplayStep
    {
        public string Kind;
        public int SkillIndex = -1;
        public string TargetId;
        public int StockIndex = -1;
        public double RngValue;
        public int RngTimes;
        public bool RngConsumeState = true;
        public uint RandomBefore;
        public uint RandomAfter;
        public int EventsAfter;
        public string CurrentAfter;
        public int RoundAfter;
        public int BattleAfter;
    }

    [Serializable]
    public sealed class BattleLabReplayFile
    {
        public int Version = 1;
        public string ConfigJson;
        public List<BattleLabReplayStep> Steps = new List<BattleLabReplayStep>();
    }

    public sealed class BattleLabReplayReport
    {
        public bool Success;
        public bool RecordingInvalid;
        public int StepsApplied;
        public string Divergence;
        public readonly List<string> Lines = new List<string>();

        public override string ToString()
        {
            return string.Join("\n", Lines);
        }
    }

    public static class BattleLabReplay
    {
        // Runs the recorded steps against a fresh session built from the same config. The live session is
        // never touched, so a failed replay does not destroy the battle the user is inspecting.
        public static BattleLabReplayReport Run(GameCatalog catalog, BattleLabConfig config,
            IEnumerable<BattleLabReplayStep> steps, bool recordingInvalid)
        {
            var report = new BattleLabReplayReport { RecordingInvalid = recordingInvalid };
            var list = steps == null ? new List<BattleLabReplayStep>() : steps.Where(s => s != null).ToList();
            if (list.Count == 0)
            {
                report.Lines.Add("没有可重放的步骤。");
                return report;
            }
            if (recordingInvalid)
                report.Lines.Add("注意：录制期间手动修改过数值，回放可能从该步骤开始分歧。");

            var session = new BattleLabSession(catalog, config);
            if (!session.StartBattle(out string startError))
            {
                report.Lines.Add("开战失败：" + startError);
                return report;
            }
            session.StopRecording();

            for (int i = 0; i < list.Count; i++)
            {
                var step = list[i];
                if (session.Run == null || session.Run.Random == null || session.Run.Random.State != step.RandomBefore)
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的起始 RNG 不一致：期望 "
                        + step.RandomBefore.ToString("X8") + "，实际 "
                        + (session.Run == null || session.Run.Random == null ? "无" : session.Run.Random.State.ToString("X8"));
                    report.Lines.Add(report.Divergence);
                    return report;
                }

                if (!Apply(session, step, out string error))
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 执行失败：" + error;
                    report.Lines.Add(report.Divergence);
                    return report;
                }
                report.StepsApplied++;

                uint actualState = session.Run.Random.State;
                if (actualState != step.RandomAfter)
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的 RNG 不一致：期望 "
                        + step.RandomAfter.ToString("X8") + "，实际 " + actualState.ToString("X8");
                    report.Lines.Add(report.Divergence);
                    return report;
                }
                int actualEvents = session.Battle == null ? -1 : session.Battle.Events.Count;
                if (actualEvents != step.EventsAfter)
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的事件数不一致：期望 "
                        + step.EventsAfter + "，实际 " + actualEvents;
                    report.Lines.Add(report.Divergence);
                    return report;
                }
                string actualCurrent = session.Battle == null || session.Battle.Current == null ? null : session.Battle.Current.Id;
                if (!string.Equals(actualCurrent, step.CurrentAfter, StringComparison.Ordinal))
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的 Current 不一致：期望 "
                        + (step.CurrentAfter ?? "null") + "，实际 " + (actualCurrent ?? "null");
                    report.Lines.Add(report.Divergence);
                    return report;
                }
                if (session.Battle != null && session.Battle.Round != step.RoundAfter)
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的 Round 不一致：期望 "
                        + step.RoundAfter + "，实际 " + session.Battle.Round;
                    report.Lines.Add(report.Divergence);
                    return report;
                }
                if (session.Run.Battles != step.BattleAfter)
                {
                    report.Divergence = "步骤 " + (i + 1) + " (" + step.Kind + ") 的场次不一致：期望 "
                        + step.BattleAfter + "，实际 " + session.Run.Battles;
                    report.Lines.Add(report.Divergence);
                    return report;
                }
            }

            report.Success = true;
            report.Lines.Add("重放成功，共 " + report.StepsApplied + " 步，无分歧。");
            return report;
        }

        private static bool Apply(BattleLabSession session, BattleLabReplayStep step, out string error)
        {
            error = null;
            var battle = session.Battle;
            if (battle == null) { error = "没有战斗。"; return false; }
            switch (step.Kind)
            {
                case "skill":
                    return battle.Execute(new ActionRequest(ActionKind.Skill, step.TargetId, step.SkillIndex), out error);
                case "swap":
                    return battle.Execute(new ActionRequest(ActionKind.Swap, step.TargetId, 0), out error);
                case "consumable":
                    return battle.UseConsumable(step.StockIndex, step.TargetId, out error);
                case "enemy":
                    if (battle.Outcome != BattleOutcome.Active) { error = "战斗已经结束。"; return false; }
                    if (battle.Current == null || battle.Current.Player) { error = "当前不是敌方行动。"; return false; }
                    battle.ExecuteEnemy();
                    return true;
                case "skip":
                    return battle.SkipCurrentTurn(out error);
                case "rng-force":
                    session.Run.Random.ForceNextDraw(step.RngValue, Math.Max(1, step.RngTimes), step.RngConsumeState);
                    return true;
                case "rng-lock":
                    session.Run.Random.LockDraw(step.RngValue, step.RngConsumeState);
                    return true;
                case "rng-clear":
                    session.Run.Random.ClearDebugOverrides();
                    return true;
                default:
                    error = "未知步骤类型：" + step.Kind;
                    return false;
            }
        }

        public static string ToJson(BattleLabReplayFile file)
        {
            return file == null ? "null" : JsonUtility.ToJson(file, true);
        }

        public static BattleLabReplayFile FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<BattleLabReplayFile>(json); }
            catch { return null; }
        }
    }
}
