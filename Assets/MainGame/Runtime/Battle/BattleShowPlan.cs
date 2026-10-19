using System;
using System.Collections.Generic;
using Ruin.Core;

namespace Ruin.Runtime
{
    // One "beat" of the full-screen show: who acts, and every target that reacts to it.
    public sealed class BattleShowGroup
    {
        public ActorState Actor;
        public string ActionMessage;
        public bool Critical;
        public readonly List<BattleShowResult> Results = new List<BattleShowResult>();
    }

    // The single result a target shows above its head. Multi-hit attacks accumulate into Damage,
    // multi-target heals accumulate into Heal, and the status flags win over a zero number.
    public sealed class BattleShowResult
    {
        public ActorState Target;
        public int Damage;
        public int Heal;
        public bool Missed;
        public bool Dodged;
        public bool Guarded;
        public bool Died;
        public string Label;
        // Sanity and limb changes are separate from HP so one head slot can show both a hit and its
        // rider. The sanity value is the delta actually applied by the engine (already clamped).
        public double SanityDelta;
        public bool SanityLocked;
        public bool LimbWorn;
        public int LimbDamage;
        public bool LimbSevered;
        public string LimbPart;
        // Concrete buff names, joined with "、". "增益" is only the fallback for old logs.
        public bool Buffed;
        public string BuffText;
    }

    // Turns the events the engine produced for one executed action into presentation beats. This is
    // deliberately read-only: it never touches BattleEngine, Run or the random sequence, and every
    // lookup is guarded so a malformed event can never stop the enemy coroutine that called it.
    public static class BattleShowPlan
    {
        private const int MaxGroups = 4;
        private const int MaxTargets = 4;

        public static List<BattleShowGroup> Build(BattleEvent[] events, Func<string, ActorState> find)
        {
            var groups = new List<BattleShowGroup>();
            if (events == null || events.Length == 0 || find == null) return groups;

            BattleShowGroup current = null;
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                if (e == null || string.IsNullOrEmpty(e.Kind)) continue;

                switch (e.Kind)
                {
                    case "action":
                    {
                        var actor = Find(find, e.ActorId);
                        if (actor == null) { Commit(groups, current); current = null; break; }

                        // Support skills and misses have no action event. If an earlier effect opened a
                        // synthetic group, fold the action into it instead of starting a second beat.
                        bool synthetic = current != null && string.IsNullOrEmpty(current.ActionMessage) && !HasNumber(current);
                        if (synthetic)
                        {
                            current.Actor = actor;
                            current.ActionMessage = e.Message;
                            current.Critical |= HasCritical(e.Message);
                        }
                        else
                        {
                            Commit(groups, current);
                            current = new BattleShowGroup
                            {
                                Actor = actor,
                                ActionMessage = e.Message,
                                Critical = HasCritical(e.Message)
                            };
                        }
                        break;
                    }
                    case "damage":
                    {
                        // A null source is round-start bleeding or an environment effect; it has no
                        // actor to stand on the left and is left to the combat log.
                        if (string.IsNullOrEmpty(e.ActorId)) break;
                        if (current == null) current = Synthetic(find, e.ActorId);
                        if (current == null) break;
                        RetargetSynthetic(current, find, e.ActorId);
                        if (e.Amount <= 0) break;
                        var target = Find(find, e.TargetId);
                        if (target == null) break;
                        ResultFor(current, target).Damage += e.Amount;
                        break;
                    }
                    case "heal":
                    {
                        if (current == null) current = Synthetic(find, e.ActorId);
                        if (current == null) break;
                        RetargetSynthetic(current, find, e.ActorId);
                        var target = Find(find, e.TargetId);
                        if (target == null) break;
                        bool cleanse = e.Amount <= 0 && (Contains(e.Message, "清除") || Contains(e.Message, "移除"));
                        if (e.Amount <= 0 && !cleanse) break;
                        var result = ResultFor(current, target);
                        if (e.Amount > 0) result.Heal += e.Amount;
                        else result.Label = "净化";
                        break;
                    }
                    case "miss":
                    case "dodge":
                    {
                        if (current == null) current = Synthetic(find, e.ActorId);
                        if (current == null) break;
                        RetargetSynthetic(current, find, e.ActorId);
                        var target = Find(find, e.TargetId);
                        if (target == null) break;
                        var result = ResultFor(current, target);
                        if (e.Kind == "miss") result.Missed = true;
                        else result.Dodged = true;
                        break;
                    }
                    case "guard":
                    {
                        var actor = Find(find, e.ActorId);
                        var target = Find(find, e.TargetId);
                        if (actor == null && target == null) break;

                        // The engine uses one event kind for three different things:
                        //   "X 的格挡免除了这次攻击。"  ActorId = attacker, TargetId = blocker
                        //   "X 为 Y 挡下攻击。"        ActorId = guardian, TargetId = protected ally
                        //   "X 守护 Y。" / "获得增益"  ActorId = caster,  TargetId = warded ally
                        // Only the middle case puts the result over the ActorId.
                        bool blocked = Contains(e.Message, "免除了") || Contains(e.Message, "格挡免");
                        bool intercepted = Contains(e.Message, "挡下") || Contains(e.Message, "拦下");

                        if (blocked) RetargetSynthetic(current, find, e.ActorId);
                        if (current == null)
                            current = blocked
                                ? Synthetic(find, e.ActorId) ?? new BattleShowGroup { Actor = target }
                                : new BattleShowGroup { Actor = actor ?? target };
                        if (current == null) break;

                        var subject = (intercepted ? actor : target) ?? actor ?? target;
                        if (subject == null) break;
                        var result = ResultFor(current, subject);
                        // Older logs folded buffs into the guard kind. They now become concrete buff text;
                        // new buff events arrive as "buff" with their own Detail list.
                        if (Contains(e.Message, "获得增益"))
                        {
                            result.Buffed = true;
                            AppendBuff(result, string.IsNullOrEmpty(e.Detail) ? "增益" : e.Detail);
                        }
                        else
                        {
                            result.Guarded = true;
                            result.Label = blocked || intercepted ? "格挡" : "守护";
                        }
                        break;
                    }
                    case "sanity":
                    {
                        bool locked = e.Detail == "locked" || Contains(e.Message, "锁定");
                        // Flavor lines ("成瘾加深", "仍被困在假寐中") share the kind but carry no applied
                        // delta; they belong in the combat log, not in a cut-in of their own.
                        if (!locked && Math.Abs(e.Value) < 0.0001) break;

                        var target = Find(find, e.TargetId) ?? Find(find, e.ActorId);
                        if (target == null) break;
                        if (current == null)
                            current = new BattleShowGroup { Actor = Find(find, e.ActorId) ?? target };
                        if (current == null) break;
                        var result = ResultFor(current, target);
                        if (locked) result.SanityLocked = true;
                        else result.SanityDelta += e.Value;
                        break;
                    }
                    case "limb":
                    {
                        // Every guaranteed hit wears the limb track, but only two thresholds are worth a
                        // cut-in line: a part turning injured at half health and a part being severed.
                        bool milestone = e.Milestone || e.Value >= 0.5
                            || Contains(e.Message, "断肢") || Contains(e.Message, "断裂");
                        if (!milestone) break;

                        var target = Find(find, e.TargetId) ?? Find(find, e.ActorId);
                        if (target == null) break;
                        if (current == null)
                            current = new BattleShowGroup { Actor = Find(find, e.ActorId) ?? target };
                        if (current == null) break;
                        var result = ResultFor(current, target);
                        result.LimbWorn = true;
                        if (e.Amount > 0) result.LimbDamage += e.Amount;
                        if (!string.IsNullOrEmpty(e.Detail)) result.LimbPart = e.Detail;
                        if (e.Value >= 0.5 || Contains(e.Message, "断肢") || Contains(e.Message, "断裂"))
                            result.LimbSevered = true;
                        break;
                    }
                    case "bleed":
                    {
                        var target = Find(find, e.TargetId) ?? Find(find, e.ActorId);
                        if (target == null) { Commit(groups, current); current = null; break; }

                        // A round-start tick has no source: it is the bleed itself hurting the carrier,
                        // so it gets a self-contained beat above their own portrait.
                        if (string.IsNullOrEmpty(e.ActorId))
                        {
                            Commit(groups, current);
                            current = null;
                            if (e.Amount <= 0) break;
                            current = new BattleShowGroup { Actor = target };
                            var tick = ResultFor(current, target);
                            tick.Damage += e.Amount;
                            // The damage log and the death log both precede this tick event, so the
                            // result has to pick the death flag up from the actor's final state.
                            tick.Died = target.Dead;
                            tick.Buffed = true;
                            AppendBuff(tick, string.IsNullOrEmpty(e.Detail) ? "流血" : e.Detail);
                            break;
                        }

                        // Applying bleed is a buff-like rider on the current action. Keep the group open
                        // so the damage that caused it and the bleed share one beat.
                        if (current == null) current = Synthetic(find, e.ActorId) ?? new BattleShowGroup { Actor = target };
                        if (current == null) break;
                        var result = ResultFor(current, target);
                        result.Buffed = true;
                        AppendBuff(result, string.IsNullOrEmpty(e.Detail) ? "流血" : e.Detail);
                        break;
                    }
                    case "buff":
                    {
                        var target = Find(find, e.TargetId);
                        if (target == null) break;
                        if (current == null) current = Synthetic(find, e.ActorId);
                        if (current == null) current = new BattleShowGroup { Actor = target };
                        if (current == null) break;
                        var result = ResultFor(current, target);
                        result.Buffed = true;
                        AppendBuff(result, string.IsNullOrEmpty(e.Detail) ? "增益" : e.Detail);
                        break;
                    }
                    case "death":
                    {
                        if (current == null) break;
                        var target = Find(find, e.TargetId);
                        if (target == null) break;
                        // A death outside this action (a bleed tick that runs after the action, for
                        // example) must not be pinned on the previous beat. Normal deaths already have
                        // a damage or sanity result for the same target.
                        var result = ExistingResult(current, target);
                        if (result == null) break;
                        result.Died = true;
                        break;
                    }
                    case "round":
                    case "control":
                    case "move":
                    case "summon":
                        // Status beats: they end the current action but never get a show of their own.
                        Commit(groups, current);
                        current = null;
                        break;
                    default:
                        // system/form/debug/settlement/deathdoor: no portrait result. Sanity, limb and
                        // buff effects are handled above so they can share a target's head slot.
                        break;
                }
            }

            Commit(groups, current);
            return groups;
        }

        private static BattleShowGroup Synthetic(Func<string, ActorState> find, string actorId)
        {
            var actor = Find(find, actorId);
            return actor == null ? null : new BattleShowGroup { Actor = actor };
        }

        // Interception and block events arrive before the action (or replace it on a miss), so the beat
        // may start on the guardian or blocker. Once a later event names the real acting side, move the
        // beat onto it without losing the results already collected.
        private static void RetargetSynthetic(BattleShowGroup group, Func<string, ActorState> find, string actorId)
        {
            if (group == null || !string.IsNullOrEmpty(group.ActionMessage) || HasNumber(group)) return;
            var actor = Find(find, actorId);
            if (actor != null) group.Actor = actor;
        }

        private static ActorState Find(Func<string, ActorState> find, string id)
        {
            if (find == null || string.IsNullOrEmpty(id)) return null;
            try { return find(id); }
            catch { return null; }
        }

        private static BattleShowResult ResultFor(BattleShowGroup group, ActorState target)
        {
            return ExistingResult(group, target) ?? AddResult(group, target);
        }

        private static BattleShowResult ExistingResult(BattleShowGroup group, ActorState target)
        {
            if (group == null || target == null) return null;
            for (int i = 0; i < group.Results.Count; i++)
            {
                var existing = group.Results[i];
                if (existing != null && existing.Target != null && existing.Target.Id == target.Id)
                    return existing;
            }
            return null;
        }

        private static BattleShowResult AddResult(BattleShowGroup group, ActorState target)
        {
            var result = new BattleShowResult { Target = target };
            group.Results.Add(result);
            return result;
        }

        private static bool HasNumber(BattleShowGroup group)
        {
            for (int i = 0; i < group.Results.Count; i++)
                if (group.Results[i] != null && (group.Results[i].Damage > 0 || group.Results[i].Heal > 0))
                    return true;
            return false;
        }

        // Buff details can repeat when several skills hand out the same effect in one beat. Keep the
        // first occurrence of each name so the head line stays readable.
        private static void AppendBuff(BattleShowResult result, string text)
        {
            if (result == null || string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split(new[] { '、', '，', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                if (ContainsEntry(result.BuffText, part)) continue;
                result.BuffText = string.IsNullOrEmpty(result.BuffText) ? part : result.BuffText + "、" + part;
            }
        }

        private static bool ContainsEntry(string joined, string entry)
        {
            if (string.IsNullOrEmpty(joined)) return false;
            string[] parts = joined.Split('、');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i], entry, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasCritical(string message)
        {
            return Contains(message, "暴击");
        }

        private static bool Contains(string message, string value)
        {
            return !string.IsNullOrEmpty(message) && message.IndexOf(value, StringComparison.Ordinal) >= 0;
        }

        private static void Commit(List<BattleShowGroup> groups, BattleShowGroup group)
        {
            if (group == null || group.Actor == null || group.Results.Count == 0) return;
            group.Results.Sort(CompareResults);
            if (group.Results.Count > MaxTargets)
                group.Results.RemoveRange(MaxTargets, group.Results.Count - MaxTargets);
            if (groups.Count >= MaxGroups) return;
            groups.Add(group);
        }

        private static int CompareResults(BattleShowResult a, BattleShowResult b)
        {
            int rankA = a != null && a.Target != null ? a.Target.Rank : int.MaxValue;
            int rankB = b != null && b.Target != null ? b.Target.Rank : int.MaxValue;
            if (rankA != rankB) return rankA.CompareTo(rankB);
            string idA = a != null && a.Target != null ? a.Target.Id : "";
            string idB = b != null && b.Target != null ? b.Target.Id : "";
            return string.CompareOrdinal(idA, idB);
        }
    }
}
