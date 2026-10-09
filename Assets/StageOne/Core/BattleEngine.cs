using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    public sealed class BattleEngine
    {
        public readonly RunState Run;
        public readonly CombatRules Rules;
        public readonly List<ActorState> Enemies = new List<ActorState>();
        public readonly List<BattleEvent> Events = new List<BattleEvent>();
        public BattleOutcome Outcome { get; private set; }
        public BattleSettlement Settlement { get; private set; }
        public ActorState Current { get; private set; }
        public int Round { get; private set; }
        private readonly Dictionary<string, double> startSanity = new Dictionary<string, double>();
        private readonly List<ActorState> roster = new List<ActorState>();
        private readonly Queue<ActorState> turns = new Queue<ActorState>();

        public BattleEngine(RunState run, ActorSpec[] enemies, CombatRules rules)
        {
            Run = run; Rules = rules; Outcome = BattleOutcome.Active;
            foreach (var hero in run.Heroes.Where(x => !x.Dead))
            {
                roster.Add(hero); startSanity.Add(hero.Id, hero.Sanity);
                ResetBattleEffects(hero);
            }
            for (int i = 0; i < enemies.Length; i++)
                Enemies.Add(new ActorState { Id = "enemy_" + i, Definition = enemies[i], Player = false, Rank = i, Hp = enemies[i].MaxHp, Sanity = 50 });
            Run.Battles++;
            Log("第 " + Run.Battles + " 场演练开始。队员理智与持续精神状态沿用。", "system");
            if (roster.Count == 0) { Finish(BattleOutcome.Defeat); return; }
            if (Enemies.Count == 0) { Finish(BattleOutcome.Victory); return; }
            Advance();
        }

        private void ResetBattleEffects(ActorState actor)
        {
            actor.Form = SkillForm.Basic; actor.Defending = false; actor.ProtectorId = null;
            actor.Interrupted = 0; actor.BleedDamage = 0; actor.BleedTurns = 0;
            actor.FocusUses = 2; actor.EmbraceUses = 2;
            actor.SupportUses = actor.Definition.Support == null ? 0 : actor.Definition.Support.Uses;
        }

        public IEnumerable<ActorState> All { get { return Run.Heroes.Concat(Enemies); } }
        public ActorState Find(string id) { return All.FirstOrDefault(a => a.Id == id); }
        public ActorState[] TurnPreview { get { return turns.Where(a => !a.Dead).ToArray(); } }

        public double[] FormProbabilities(ActorState actor)
        {
            var mental = Rules.Mental(actor.Mental);
            return SanityProbability.Distribution(actor.Sanity, new[] {
                new WeightedOutcome(Rules.TechnicalWeight * mental.TechnicalMultiplier, 1, SanityGate.High),
                new WeightedOutcome(Rules.BasicWeight, 0),
                new WeightedOutcome(Rules.PrimalWeight * mental.PrimalMultiplier, -1, SanityGate.Low)
            }, Rules.ProbabilityStrength);
        }

        private void Advance()
        {
            Current = null;
            while (Outcome == BattleOutcome.Active)
            {
                if (CheckEnd()) return;
                if (turns.Count == 0)
                {
                    Round++;
                    foreach (var actor in All.Where(a => !a.Dead).OrderByDescending(a => a.Speed).ThenBy(a => a.Player ? 0 : 1).ThenBy(a => a.Id, StringComparer.Ordinal))
                        turns.Enqueue(actor);
                    Log("—— 第 " + Round + " 回合 ——", "round");
                }
                var next = turns.Dequeue();
                if (next.Dead) continue;
                next.Defending = false;
                foreach (var ally in Run.Heroes) if (ally.ProtectorId == next.Id) ally.ProtectorId = null;
                if (next.BleedTurns > 0)
                {
                    next.BleedTurns--;
                    Damage(next, next.BleedDamage, null, true);
                    Log(next.Name + " 流血，剩余 " + next.BleedTurns + " 次。", "bleed", null, next.Id);
                    if (CheckEnd()) return;
                    if (next.Dead) continue;
                }
                if (next.Interrupted > 0)
                {
                    next.Interrupted--;
                    Log(next.Name + " 的行动被打断。", "control", null, next.Id);
                    continue;
                }
                Current = next;
                if (next.Player)
                {
                    next.Form = (SkillForm)SanityProbability.Sample(FormProbabilities(next), Run.Random);
                    if (next.Form != SkillForm.Basic)
                        Log(next.Name + (next.Form == SkillForm.Technical ? " 获得技术灵感：" + next.Definition.Technical.Name : " 技能原始化：" + next.Definition.Primal.Name), "form", next.Id);
                }
                return;
            }
        }

        public SkillSpec SkillFor(ActorState actor, ActionKind kind)
        {
            if (actor == null) return null;
            if (kind == ActionKind.Primary) return actor.Form == SkillForm.Primal ? actor.Definition.Primal : actor.Definition.Primary;
            if (kind == ActionKind.Support) return actor.Definition.Support;
            if (kind == ActionKind.Technical && actor.Form == SkillForm.Technical) return actor.Definition.Technical;
            return null;
        }

        public string DisabledReason(ActionKind kind)
        {
            if (Outcome != BattleOutcome.Active) return "本场战斗已结束";
            if (Current == null || !Current.Player) return "等待敌方行动";
            if (kind == ActionKind.Focus && Current.FocusUses <= 0) return "本场稳定心神次数已用尽";
            if (kind == ActionKind.Embrace && Current.EmbraceUses <= 0) return "本场直视深渊次数已用尽";
            if (kind == ActionKind.Focus && Current.Sanity >= 100) return "理智已达到100";
            if (kind == ActionKind.Embrace && Current.Sanity <= 0) return "理智已达到0";
            if (kind == ActionKind.Swap && !Targets(kind).Any()) return "没有相邻的存活队员";
            if (kind == ActionKind.Primary || kind == ActionKind.Support || kind == ActionKind.Technical)
            {
                var skill = SkillFor(Current, kind);
                if (skill == null) return "本回合未获得该技能";
                if ((skill.ActorRanks & (1 << Current.Rank)) == 0) return "当前站位无法使用，尝试换位";
                if (kind == ActionKind.Support && skill.Uses > 0 && Current.SupportUses <= 0) return "本场技能次数已用尽";
                if (!Targets(kind).Any()) return "没有合法目标";
            }
            return null;
        }

        public IEnumerable<ActorState> Targets(ActionKind kind)
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            if (kind == ActionKind.Swap) return Run.Heroes.Where(a => !a.Dead && a != Current && Math.Abs(a.Rank - Current.Rank) == 1);
            var skill = SkillFor(Current, kind);
            if (skill == null) return Enumerable.Empty<ActorState>();
            var side = skill.Target == TargetSide.Ally ? Run.Heroes : Enemies;
            return side.Where(a => !a.Dead && (skill.TargetRanks & (1 << a.Rank)) != 0
                && (skill.Effect != SkillEffect.Heal || a.Hp < a.Definition.MaxHp || (skill.Cleanse && a.BleedTurns > 0))
                && (skill.Effect != SkillEffect.Protect || a != Current));
        }

        public bool Execute(ActionRequest request, out string error)
        {
            error = DisabledReason(request.Kind);
            if (error != null) return false;
            var actor = Current;
            if (request.Kind == ActionKind.Retreat) { Log("队伍撤离。保留本场理智变化。", "system"); Finish(BattleOutcome.Retreat); return true; }
            if (request.Kind == ActionKind.Defend)
            { actor.Defending = true; Log(actor.Name + " 防御，至下次行动前受到的攻击伤害减半。", "guard", actor.Id); }
            else if (request.Kind == ActionKind.Focus || request.Kind == ActionKind.Embrace)
            {
                bool focus = request.Kind == ActionKind.Focus;
                if (focus) actor.FocusUses--; else actor.EmbraceUses--;
                ChangeSanity(actor, focus ? Rules.MindStep : -Rules.MindStep, focus ? "稳定心神" : "直视深渊");
                TryMentalState(actor);
            }
            else
            {
                var target = Targets(request.Kind).FirstOrDefault(a => a.Id == request.TargetId);
                if (target == null) { error = "请选择高亮的合法目标"; return false; }
                if (request.Kind == ActionKind.Swap)
                {
                    int rank = actor.Rank; actor.Rank = target.Rank; target.Rank = rank;
                    Log(actor.Name + " 与 " + target.Name + " 交换站位。", "move", actor.Id, target.Id);
                }
                else
                {
                    var skill = SkillFor(actor, request.Kind);
                    if (request.Kind == ActionKind.Support && skill.Uses > 0) actor.SupportUses--;
                    ResolveSkill(actor, target, skill);
                }
            }
            if (!CheckEnd()) Advance();
            return true;
        }

        private void ResolveSkill(ActorState actor, ActorState target, SkillSpec skill)
        {
            if (skill.Effect == SkillEffect.Heal)
            {
                int restored = Math.Min(skill.Power, target.Definition.MaxHp - target.Hp);
                target.Hp += restored;
                if (skill.Cleanse) target.BleedTurns = 0;
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 恢复 " + restored + (skill.Cleanse ? "，清除流血。" : "。"), "heal", actor.Id, target.Id, restored);
                return;
            }
            if (skill.Effect == SkillEffect.Protect)
            {
                target.ProtectorId = actor.Id; actor.Defending = true;
                Log(actor.Name + " 掩护 " + target.Name + "，拦截下一次攻击并减半伤害。", "guard", actor.Id, target.Id);
                return;
            }
            double accuracy = skill.Accuracy + Rules.Mental(actor.Mental).AccuracyModifier;
            if (!SanityProbability.Check(accuracy, 0.3, actor.Sanity, Run.Random, Rules.ProbabilityStrength))
            { Log(actor.Name + " · " + skill.Name + " 未命中。", "miss", actor.Id, target.Id); return; }
            bool critical = SanityProbability.Check(0.10, 0.4, actor.Sanity, Run.Random, Rules.ProbabilityStrength);
            int power = critical ? (int)Math.Ceiling(skill.Power * 1.5) : skill.Power;
            Log(actor.Name + " · " + skill.Name + (critical ? " 暴击！" : ""), "action", actor.Id, target.Id);
            Damage(target, power, actor.Id);
            if (!target.Dead)
            {
                if (skill.Bleed > 0) { target.BleedDamage = skill.Bleed; target.BleedTurns = 2; Log(target.Name + " 流血 " + skill.Bleed + " × 2次行动。", "bleed", actor.Id, target.Id); }
                if (skill.Interrupt) { target.Interrupted = Math.Max(1, target.Interrupted); Log(target.Name + " 下一次行动被打断。", "control", actor.Id, target.Id); }
            }
        }

        public string Intent(ActorState enemy)
        {
            if (enemy.Dead) return "已被击败";
            if (enemy.Interrupted > 0) return "下一次行动被打断";
            var target = EnemyTarget(enemy);
            if (target == null) return "—";
            switch (enemy.Definition.Pattern)
            {
                case EnemyPattern.Whisperer: return "低语 → " + target.Name + "\n伤害3 · 理智−14";
                case EnemyPattern.Hunter: return "追猎 → " + target.Name + "\n伤害5 · 流血2×2";
                default: return (Round % 2 == 0 ? "重劈" : "斩击") + " → " + target.Name + "\n伤害" + (Round % 2 == 0 ? "11" : "7") + " · 前排";
            }
        }

        private ActorState EnemyTarget(ActorState enemy)
        {
            var alive = Run.Heroes.Where(a => !a.Dead);
            if (enemy.Definition.Pattern == EnemyPattern.Whisperer) return alive.OrderByDescending(a => a.Sanity).ThenBy(a => a.Rank).FirstOrDefault();
            if (enemy.Definition.Pattern == EnemyPattern.Hunter) return alive.OrderBy(a => a.Hp).ThenBy(a => a.Rank).FirstOrDefault();
            return alive.OrderBy(a => a.Rank).FirstOrDefault();
        }

        public void ExecuteEnemy()
        {
            if (Outcome != BattleOutcome.Active || Current == null || Current.Player) return;
            var enemy = Current;
            var target = EnemyTarget(enemy);
            if (target == null) { Finish(BattleOutcome.Defeat); return; }
            ActorState protector = string.IsNullOrEmpty(target.ProtectorId) ? null : Find(target.ProtectorId);
            if (protector != null && !protector.Dead)
            {
                Log(protector.Name + " 为 " + target.Name + " 拦下攻击。", "guard", protector.Id, target.Id);
                target.ProtectorId = null; target = protector;
            }
            if (SanityProbability.Check(0.86, -0.25, target.Sanity, Run.Random, Rules.ProbabilityStrength))
            {
                int damage = enemy.Definition.Pattern == EnemyPattern.Whisperer ? 3 : enemy.Definition.Pattern == EnemyPattern.Hunter ? 5 : Round % 2 == 0 ? 11 : 7;
                Log(enemy.Name + " 发动" + (enemy.Definition.Pattern == EnemyPattern.Whisperer ? "低语。" : "攻击。"), "action", enemy.Id, target.Id);
                Damage(target, damage, enemy.Id);
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Whisperer)
                { ChangeSanity(target, -14, "受到低语侵蚀"); TryMentalState(target); }
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Hunter)
                { target.BleedDamage = 2; target.BleedTurns = 2; Log(target.Name + " 开始流血，2×2次行动。", "bleed", enemy.Id, target.Id); }
            }
            else Log(target.Name + " 避开了 " + enemy.Name + " 的攻击。", "miss", enemy.Id, target.Id);
            if (!CheckEnd()) Advance();
        }

        public void ChangeSanity(ActorState actor, double amount, string reason)
        {
            if (actor == null || actor.Dead) return;
            double before = actor.Sanity;
            actor.Sanity = SanityProbability.Clamp(before + amount, 0, 100);
            Log(actor.Name + " " + reason + "：理智 " + before.ToString("0.#") + " → " + actor.Sanity.ToString("0.#") + "。", "sanity", actor.Id);
        }

        private void TryMentalState(ActorState actor)
        {
            int state = SanityProbability.Sample(SanityProbability.Distribution(actor.Sanity, new[] {
                new WeightedOutcome(0.15, 1, SanityGate.High), new WeightedOutcome(0.7, 0), new WeightedOutcome(0.15, -1, SanityGate.Low)
            }, Rules.ProbabilityStrength), Run.Random);
            var mental = state == 0 ? MentalState.Composed : state == 2 ? MentalState.Agitated : actor.Mental;
            if (mental == actor.Mental) return;
            actor.Mental = mental;
            Log(actor.Name + " 获得持续状态「" + Rules.Mental(mental).Name + "」。离开理智区间后仍保留。", "mental", actor.Id);
        }

        public void Damage(ActorState actor, int amount, string sourceId = null, bool bleeding = false)
        {
            if (actor.Dead || amount <= 0) return;
            int actual = actor.Defending && !bleeding ? Math.Max(1, (int)Math.Ceiling(amount * 0.5)) : amount;
            bool alreadyDying = actor.DeathDoor;
            actor.Hp = Math.Max(0, actor.Hp - actual);
            Log(actor.Name + " 受到 " + actual + (bleeding ? " 点流血伤害。" : " 点伤害。"), "damage", sourceId, actor.Id, actual);
            if (!actor.Player && actor.Hp == 0) Kill(actor);
            else if (alreadyDying)
            {
                bool dies = SanityProbability.Check(Rules.DeathChance, -0.35, actor.Sanity, Run.Random, Rules.ProbabilityStrength);
                if (dies) Kill(actor); else Log(actor.Name + " 挺过了死亡检定，仍处于濒死。", "deathdoor", null, actor.Id);
            }
            else if (actor.DeathDoor) Log(actor.Name + " 陷入濒死！再次受伤将进行死亡检定。", "deathdoor", null, actor.Id);
        }

        private void Kill(ActorState actor)
        {
            actor.Dead = true;
            Log(actor.Name + (actor.Player ? " 死亡，本局无法恢复。" : " 被击败。"), "death", null, actor.Id);
        }

        private bool CheckEnd()
        {
            if (Outcome != BattleOutcome.Active) return true;
            if (Run.Heroes.All(a => a.Dead)) { Finish(BattleOutcome.Defeat); return true; }
            if (Enemies.All(a => a.Dead)) { Finish(BattleOutcome.Victory); return true; }
            return false;
        }

        public BattleSettlement Finish(BattleOutcome outcome)
        {
            if (Settlement != null) return Settlement;
            if (outcome == BattleOutcome.Active) throw new ArgumentException("Cannot settle active outcome");
            Outcome = outcome; Current = null;
            var result = new BattleSettlement { Outcome = outcome, Before = Run.PlayerSanity };
            foreach (var actor in roster)
                result.Entries.Add(new FeedbackEntry { Id = actor.Id, Name = actor.Name, Start = startSanity[actor.Id], End = actor.Sanity, Dead = actor.Dead });
            result.RawDelta = result.Entries.Count == 0 ? 0 : result.Entries.Average(e => e.Delta) * Rules.FeedbackFactor;
            result.After = SanityProbability.Clamp(result.Before + result.RawDelta, 0, 100);
            Run.PlayerSanity = result.After;
            Settlement = result;
            Log("战后反馈：玩家理智 " + result.Before.ToString("0.#") + " → " + result.After.ToString("0.#") + "（队员平均净变化 × " + Rules.FeedbackFactor.ToString("0.##") + "）。", "settlement");
            return result;
        }

        private void Log(string message, string kind, string actorId = null, string targetId = null, int amount = 0)
        { Events.Add(new BattleEvent { Message = message, Kind = kind, ActorId = actorId, TargetId = targetId, Amount = amount }); }
    }
}
