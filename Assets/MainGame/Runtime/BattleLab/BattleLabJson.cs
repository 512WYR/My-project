using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // JsonUtility wrappers and the persistent export folder. This file deliberately has no IMGUI calls so it
    // can be exercised from EditMode tests.
    public static class BattleLabJson
    {
        public static string ExportDir
        {
            get { return Path.Combine(Application.persistentDataPath, "BattleLab"); }
        }

        public static string ToJson<T>(T value, bool pretty = true)
        {
            return JsonUtility.ToJson(value, pretty);
        }

        public static T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        // Parses and normalizes, then runs the same validation the battle path uses. The returned config is
        // still usable for repair even when errors are present; the caller decides whether to apply it.
        public static BattleLabConfig ImportConfig(string json, GameCatalog catalog, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("JSON 为空。");
                return null;
            }
            BattleLabConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<BattleLabConfig>(json);
            }
            catch (Exception e)
            {
                errors.Add("JSON 解析失败：" + e.Message);
                return null;
            }
            if (cfg == null)
            {
                errors.Add("JSON 没有解析出配置对象。");
                return null;
            }
            cfg.Normalize();
            errors.AddRange(BattleLabFactory.Validate(cfg, catalog));
            return cfg;
        }

        public static string SaveConfig(BattleLabConfig config, string prefix = "config")
        {
            return SaveText(prefix + "-" + Stamp() + ".json", JsonUtility.ToJson(config, true));
        }

        public static string SaveReplay(BattleLabReplayFile file, string prefix = "replay")
        {
            return SaveText(prefix + "-" + Stamp() + ".json", BattleLabReplay.ToJson(file));
        }

        public static string SaveText(string fileName, string text)
        {
            Directory.CreateDirectory(ExportDir);
            string path = Path.Combine(ExportDir, Sanitize(fileName));
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
            return path;
        }

        public static bool TrySaveText(string fileName, string text, out string path, out string error)
        {
            path = null;
            error = null;
            try
            {
                path = SaveText(fileName, text);
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        // The bundle a bug report should attach: configuration, mirrored log, actor state, invariant output,
        // replay steps and environment metadata.
        public static string BuildBugBundle(GameCatalog catalog, BattleLabSession session, string extraNote, out string error)
        {
            error = null;
            try
            {
                string root = Path.Combine(ExportDir, "bug-" + Stamp());
                Directory.CreateDirectory(root);
                Write(root, "config.json", session == null || session.Config == null ? "{}" : JsonUtility.ToJson(session.Config, true));
                Write(root, "log.txt", session == null ? "" : BattleLabLog.ToText(session.Log));
                Write(root, "state.txt", DescribeState(session));
                Write(root, "invariants.txt", DescribeInvariants(session));
                Write(root, "meta.txt", MetaText(catalog, session, extraNote));
                if (session != null && session.RecordedSteps.Count > 0)
                {
                    var replay = new BattleLabReplayFile
                    {
                        ConfigJson = session.RecordedConfigJson,
                        Steps = new List<BattleLabReplayStep>(session.RecordedSteps)
                    };
                    Write(root, "recording.json", BattleLabReplay.ToJson(replay));
                }
                return root;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + " " + e.Message;
                return null;
            }
        }

        private static void Write(string root, string name, string text)
        {
            File.WriteAllText(Path.Combine(root, name), text ?? "", new UTF8Encoding(false));
        }

        public static string DescribeState(BattleLabSession session)
        {
            var sb = new StringBuilder();
            if (session == null)
            {
                sb.AppendLine("没有会话。");
                return sb.ToString();
            }
            var battle = session.Battle;
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("种子：" + (session.Config == null ? "" : session.Config.SeedText));
            sb.AppendLine("Run.Battles：" + (session.Run == null ? 0 : session.Run.Battles));
            sb.AppendLine("RNG State：" + session.RandomState.ToString("X8")
                + (session.RandomOverrideActive ? "（覆盖中，剩余 " + session.RandomForcedDrawsRemaining + "）" : ""));
            if (battle == null)
            {
                sb.AppendLine("战斗：尚未开始");
                return sb.ToString();
            }
            sb.AppendLine("Round：" + battle.Round + "  Outcome：" + battle.Outcome
                + "  Current：" + (battle.Current == null ? "null" : battle.Current.Id));
            sb.AppendLine("事件数：" + battle.Events.Count + "  日志条目：" + session.Log.Count
                + "  录制步骤：" + session.RecordedSteps.Count
                + (session.RecordingInvalid ? "（录制已失效）" : ""));
            sb.AppendLine();
            foreach (var actor in battle.All)
            {
                if (actor == null) continue;
                sb.AppendLine("[" + (actor.Player ? "英雄" : "敌人") + "] " + actor.Name + " (" + actor.Id + ")");
                sb.AppendLine("  HP " + actor.Hp + "/" + actor.MaxHp
                    + (actor.Dead ? " 已死亡" : actor.DeathDoor ? " 濒死" : "")
                    + "  理智 " + actor.Sanity.ToString("0.#") + "  形态 " + actor.Form
                    + "  站位 " + (actor.Rank + 1) + "×" + actor.Width);
                sb.AppendLine("  流血 " + actor.BleedDamage + "×" + actor.BleedTurns
                    + "  打断 " + actor.Interrupted + "  失衡 " + actor.Imbalance
                    + "  位移必中至R" + actor.ImbalanceAutoHitUntilRound
                    + "  格挡 " + actor.BlockCharges + "  反击 " + actor.Counter + "  追击 " + actor.Followup
                    + "  守护者 " + (actor.ProtectorId ?? "无") + "  易伤 " + actor.SelfVulnerable.ToString("0.##")
                    + "  防御buff " + actor.DefenseBuff.ToString("0.##") + "  命中buff " + actor.AccuracyBuff.ToString("0.##"));
                sb.AppendLine("  部位：" + (actor.Limbs == null ? "无" : string.Join(" / ", actor.Limbs.Where(l => l != null)
                    .Select(l => LimbSystem.PartName(l.Part) + " " + l.Hp + "/" + l.MaxHp
                        + (l.ProstheticFitted ? "(" + l.Prosthetic + ")" : "")))));
                sb.AppendLine("  饰品：" + AccessorySystem.Summary(actor));
                sb.AppendLine("  技能次数：" + (actor.Uses == null || actor.Uses.Count == 0 ? "无记录"
                    : string.Join(", ", actor.Uses.Where(u => u != null).Select(u => u.Id + "=" + u.Left))));
            }
            if (session.Run != null && session.Run.Consumables != null)
                sb.AppendLine("队伍消耗品：" + ConsumableSystem.Summary(session.Run));
            return sb.ToString();
        }

        public static string DescribeInvariants(BattleLabSession session)
        {
            var sb = new StringBuilder();
            if (session == null || session.Battle == null)
            {
                sb.AppendLine("没有战斗，无法检查不变量。");
                return sb.ToString();
            }
            var issues = session.CheckInvariants();
            sb.AppendLine(issues.Count == 0 ? "不变量：正常。" : "不变量：发现 " + issues.Count + " 条问题。");
            foreach (var issue in issues) sb.AppendLine("- " + issue);
            string purity = session.CheckQueryPurity();
            sb.AppendLine("查询纯度：" + (purity == null ? "正常。" : purity));
            return sb.ToString();
        }

        public static string MetaText(GameCatalog catalog, BattleLabSession session, string extra)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Unity：" + Application.unityVersion);
            sb.AppendLine("平台：" + Application.platform + " / " + SystemInfo.operatingSystem);
            sb.AppendLine("Catalog：" + (catalog == null ? "null" : catalog.name));
            sb.AppendLine("场景：" + Application.productName);
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (session != null)
            {
                sb.AppendLine("最近提示：" + session.LastNote);
                sb.AppendLine("最近错误：" + (session.LastErrors.Count == 0 ? "无" : string.Join(" | ", session.LastErrors)));
            }
            if (!string.IsNullOrEmpty(extra)) sb.AppendLine("备注：" + extra);
            return sb.ToString();
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "battlelab.txt";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }
    }
}
