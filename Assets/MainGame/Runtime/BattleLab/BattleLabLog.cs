using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    // One mirrored BattleEvent. The engine only keeps a flat event list with no round stamp, so the
    // session copies new events into this shape right after every action and records the round at that
    // moment. That is accurate for actions and round separators; a round-boundary event may be stamped
    // with the round that just started, which is what a reader wants anyway.
    [Serializable]
    public sealed class BattleLabLogEntry
    {
        public int Index;
        public int Battle;
        public int Round;
        public string Kind;
        public string Message;
        public string ActorId;
        public string TargetId;
        public int Amount;
    }

    public static class BattleLabLog
    {
        public static readonly string[] Kinds =
        {
            "system", "round", "action", "damage", "heal", "sanity", "control",
            "limb", "death", "deathdoor", "bleed", "form", "move", "guard", "miss", "dodge",
            "settlement", "debug"
        };

        public static BattleLabLogEntry FromEvent(BattleEngine battle, int battleNumber, BattleEvent e, int index)
        {
            if (e == null) return null;
            return new BattleLabLogEntry
            {
                Index = index,
                Battle = battleNumber,
                Round = battle == null ? 0 : battle.Round,
                Kind = e.Kind ?? "",
                Message = e.Message ?? "",
                ActorId = e.ActorId,
                TargetId = e.TargetId,
                Amount = e.Amount
            };
        }

        public static IEnumerable<BattleLabLogEntry> Filter(IEnumerable<BattleLabLogEntry> entries, string kind,
            string actorId, string targetId, string search, int currentBattle, bool currentOnly)
        {
            if (entries == null) return Enumerable.Empty<BattleLabLogEntry>();
            return entries.Where(e => e != null
                && (string.IsNullOrEmpty(kind) || e.Kind == kind)
                && (string.IsNullOrEmpty(actorId) || e.ActorId == actorId)
                && (string.IsNullOrEmpty(targetId) || e.TargetId == targetId)
                && (!currentOnly || e.Battle == currentBattle)
                && (string.IsNullOrEmpty(search)
                    || (e.Message != null && e.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        public static string FormatLine(BattleLabLogEntry e)
        {
            if (e == null) return "";
            string ids = "";
            if (!string.IsNullOrEmpty(e.ActorId)) ids += " [" + e.ActorId + "]";
            if (!string.IsNullOrEmpty(e.TargetId)) ids += " → " + e.TargetId;
            return "#" + e.Index.ToString("0000") + "  第" + e.Battle + "场 R" + e.Round
                + "  " + (string.IsNullOrEmpty(e.Kind) ? "log" : e.Kind) + ids + "  " + e.Message;
        }

        public static string ToText(IEnumerable<BattleLabLogEntry> entries)
        {
            if (entries == null) return "";
            return string.Join("\n", entries.Where(e => e != null).Select(FormatLine));
        }
    }
}
