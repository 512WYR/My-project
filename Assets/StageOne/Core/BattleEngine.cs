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
            actor.BlockCharges = 0; actor.Counter = false; actor.Followup = false; actor.FollowupAttack = 0;
            actor.Imbalance = 0; actor.ImbalanceImmuneRound = -1;
            actor.DefenseBuff = 0; actor.AccuracyBuff = 0; actor.SelfVulnerable = 0;
            actor.Uses.Clear();
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
                    // Round-scoped effects fall off here: team boosts, blocks, counters, the pursuit buff and
                    // the wound gambit's own vulnerability. Guard redirection is not cleared here, it ends on
                    // the protector's turn.
                    foreach (var actor in All)
                    {
                        actor.DefenseBuff = 0; actor.AccuracyBuff = 0;
                        actor.SelfVulnerable = 0; actor.BlockCharges = 0;
                        actor.Counter = false; actor.Followup = false; actor.FollowupAttack = 0;
                    }
                    foreach (var actor in All.Where(a => !a.Dead).OrderByDescending(a => EffectiveSpeed(a)).ThenBy(a => a.Player ? 0 : 1).ThenBy(a => a.Id, StringComparer.Ordinal))
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
                if (next.Imbalance > 0)
                {
                    next.Imbalance--;
                    Log(next.Name + " 陷入失衡，本回合无法行动。", "control", null, next.Id);
                    continue;
                }
                Current = next;
                if (next.Player)
                {
                    next.Form = (SkillForm)SanityProbability.Sample(FormProbabilities(next), Run.Random);
                    if (next.Form != SkillForm.Basic)
                        Log(next.Name + (next.Form == SkillForm.Technical ? " 高理智：解锁特殊技能。" : " 低理智：技能发生变化。"), "form", next.Id);
                }
                return;
            }
        }

        // ---------------- skills ----------------

        public SkillSpec[] SkillsFor(ActorState actor)
        {
            return actor == null ? ActorSpec.EmptySkills : actor.Definition.SkillsFor(actor.Form);
        }

        public SkillSpec SkillFor(ActorState actor, int index)
        {
            var list = SkillsFor(actor);
            return index >= 0 && index < list.Length ? list[index] : null;
        }

        private static bool NeedsTarget(SkillSpec skill)
        {
            return skill != null && skill.Targeting == SkillTargeting.Choose;
        }

        public string SkillDisabledReason(int index)
        {
            if (Outcome != BattleOutcome.Active) return "本场战斗已结束";
            if (Current == null || !Current.Player) return "等待敌方行动";
            var skill = SkillFor(Current, index);
            if (skill == null) return "本回合没有这个技能";
            if ((skill.ActorRanks & (1 << Current.Rank)) == 0) return "当前站位无法使用，尝试换位";
            if (Current.UsesLeft(skill) <= 0) return "本场技能次数已用尽";
            if (NeedsTarget(skill) && !Targets(index).Any()) return "没有合法目标";
            return null;
        }

        public string SwapDisabledReason()
        {
            if (Outcome != BattleOutcome.Active) return "本场战斗已结束";
            if (Current == null || !Current.Player) return "等待敌方行动";
            return SwapTargets().Any() ? null : "没有相邻的存活队员";
        }

        public IEnumerable<ActorState> Targets(int skillIndex)
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            var skill = SkillFor(Current, skillIndex);
            if (!NeedsTarget(skill)) return Enumerable.Empty<ActorState>();
            var side = skill.Target == TargetSide.Ally ? (IEnumerable<ActorState>)Run.Heroes : Enemies;
            return side.Where(a => !a.Dead && (skill.TargetRanks & (1 << a.Rank)) != 0
                && (skill.Effect != SkillEffect.Heal || a.Hp < a.Definition.MaxHp || (skill.Cleanse && a.BleedTurns > 0))
                && (skill.Effect != SkillEffect.Protect || a != Current));
        }

        public IEnumerable<ActorState> SwapTargets()
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            return Run.Heroes.Where(a => !a.Dead && a != Current && Math.Abs(a.Rank - Current.Rank) == 1);
        }

        public bool Execute(ActionRequest request, out string error)
        {
            if (request.Kind == ActionKind.Swap) return ExecuteSwap(request, out error);
            return ExecuteSkill(request, out error);
        }

        private bool ExecuteSwap(ActionRequest request, out string error)
        {
            error = SwapDisabledReason();
            if (error != null) return false;
            var actor = Current;
            var target = SwapTargets().FirstOrDefault(a => a.Id == request.TargetId);
            if (target == null) { error = "请选择相邻的高亮队员"; return false; }
            int rank = actor.Rank; actor.Rank = target.Rank; target.Rank = rank;
            Log(actor.Name + " 与 " + target.Name + " 交换站位。", "move", actor.Id, target.Id);
            if (!CheckEnd()) Advance();
            return true;
        }

        private bool ExecuteSkill(ActionRequest request, out string error)
        {
            error = SkillDisabledReason(request.SkillIndex);
            if (error != null) return false;
            var actor = Current;
            var skill = SkillFor(actor, request.SkillIndex);
            ActorState primary = null;
            if (NeedsTarget(skill))
            {
                primary = skill.RandomTarget
                    ? DrawRandom(Targets(request.SkillIndex).ToArray())
                    : Targets(request.SkillIndex).FirstOrDefault(a => a.Id == request.TargetId);
                if (primary == null) { error = "请选择高亮的合法目标"; return false; }
            }
            actor.SpendUse(skill);
            ResolveSkill(actor, primary, skill);
            if (!CheckEnd()) Advance();
            return true;
        }

        private ActorState DrawRandom(ActorState[] pool)
        {
            if (pool.Length == 0) return null;
            int index = Math.Min(pool.Length - 1, (int)(Run.Random.NextUnit() * pool.Length));
            return pool[index];
        }

        private void ResolveSkill(ActorState actor, ActorState primary, SkillSpec skill)
        {
            if (skill.SelfMove != 0) MoveSelf(actor, skill.SelfMove);
            ApplyBuffs(actor, actor, skill.SelfGrant, skill);
            if (skill.Effect == SkillEffect.Attack)
            {
                ResolveAttack(actor, primary, skill);
                return;
            }
            // Healing, guarding and buffs land on the whole sweep without a hit roll.
            foreach (var target in BuildArea(actor, primary, skill))
                ApplySupport(actor, target, skill);
            ApplyRider(actor, primary, skill, 0);
        }

        private void ResolveAttack(ActorState actor, ActorState primary, SkillSpec skill)
        {
            var set = BuildArea(actor, primary, skill);
            var main = primary ?? set.FirstOrDefault();
            if (main == null) return;

            // A displaced enemy that is imbalanced this round cannot dodge the displacement.
            bool autoHit = skill.PullToFront && main.Imbalance > 0 && main.ImbalanceImmuneRound != Round;
            if (!autoHit)
            {
                if (!Roll(EffectiveAccuracy(actor, skill)))
                { Log(actor.Name + " · " + skill.Name + " 未命中。", "miss", actor.Id, main.Id); ApplyRider(actor, primary, skill, 0); return; }
                if (main.Definition.Dodge > 0 && Roll(main.Definition.Dodge))
                { Log(actor.Name + " · " + skill.Name + " → " + main.Name + " 被闪避。", "dodge", actor.Id, main.Id); return; }
            }
            bool critical = Roll(actor.Definition.CritChance);
            Log(actor.Name + " · " + skill.Name + (critical ? " 暴击！" : ""), "action", actor.Id, main.Id);
            int dealt = 0;
            foreach (var target in set)
            {
                if (target.Dead) continue;
                int amount = DamageFor(actor, target, skill, main, critical);
                Damage(target, amount, actor.Id);
                dealt += amount;
                if (!target.Dead) ApplyOnHit(actor, target, skill, target == main);
            }
            ApplyRider(actor, primary, skill, dealt);
            TryFollowups(actor, main, false);
        }

        // The sweep around the main target. Only enemy-side skills read Adjacent/Splash/Pierce; ally-side
        // All/Party skills simply take the whole living side.
        private IList<ActorState> BuildArea(ActorState actor, ActorState primary, SkillSpec skill)
        {
            var side = skill.Target == TargetSide.Ally ? (IEnumerable<ActorState>)Run.Heroes : Enemies;
            if (skill.Targeting == SkillTargeting.All || skill.Area == SkillArea.Party)
                return side.Where(a => !a.Dead).ToList();
            var result = new List<ActorState>();
            if (primary == null || primary.Dead) return result;
            result.Add(primary);
            switch (skill.Area)
            {
                case SkillArea.Adjacent:
                {
                    int before = result.Count;
                    AddByRank(result, side, primary.Rank - 1);
                    AddByRank(result, side, primary.Rank + 1);
                    // Adjacent honours AreaMax as well: the low-sanity sweep takes at most one neighbour,
                    // and it takes the front one (rank - 1 is added first). A non-positive AreaMax means no cap.
                    if (skill.AreaMax > 0)
                        while (result.Count > before + skill.AreaMax) result.RemoveAt(result.Count - 1);
                    break;
                }
                case SkillArea.Splash:
                    result.AddRange(side.Where(a => !a.Dead && a != primary)
                        .OrderBy(a => Math.Abs(a.Rank - primary.Rank)).ThenBy(a => a.Rank).Take(skill.AreaMax));
                    break;
                case SkillArea.Pierce:
                    result.AddRange(side.Where(a => !a.Dead && a != primary && a.Rank > primary.Rank)
                        .OrderBy(a => a.Rank).Take(skill.AreaMax));
                    break;
            }
            // Warrior's low-sanity sweep: with no adjacent enemy to spread onto, it may maul a neighbour.
            if (skill.FriendlyFireChance > 0 && result.Count == 1)
                result.AddRange(FriendlyFire(actor, skill.FriendlyFireChance));
            return result;
        }

        private static void AddByRank(List<ActorState> result, IEnumerable<ActorState> side, int rank)
        {
            var actor = side.FirstOrDefault(a => !a.Dead && a.Rank == rank);
            if (actor != null) result.Add(actor);
        }

        private IEnumerable<ActorState> FriendlyFire(ActorState actor, double chance)
        {
            var neighbours = Run.Heroes.Where(a => !a.Dead && a != actor && Math.Abs(a.Rank - actor.Rank) == 1).ToArray();
            if (neighbours.Length == 0 || !Roll(chance)) return Enumerable.Empty<ActorState>();
            return new[] { DrawRandom(neighbours) };
        }

        private int DamageFor(ActorState actor, ActorState target, SkillSpec skill, ActorState main, bool critical)
        {
            double multiplier = skill.Multiplier;
            if (target != main)
            {
                int step = Math.Max(1, Math.Abs(target.Rank - main.Rank));
                if (skill.Area == SkillArea.Pierce) multiplier = Math.Max(0.1, multiplier - skill.AreaDecay * step);
                else multiplier *= skill.AreaRatio;
            }
            // The wound gambit drops every limb penalty for this one swing.
            double attack = skill.IgnoreLimbPenalty ? actor.Definition.Attack
                : actor.Definition.Attack * LimbSystem.AttackFactor(actor, Rules.Limbs);
            if (skill.HpMissingDamageScale > 0 && actor.Definition.MaxHp > 0)
                multiplier += skill.HpMissingDamageScale * Math.Max(0, actor.Definition.MaxHp - actor.Hp) / actor.Definition.MaxHp;
            int value = attack <= 0 || multiplier <= 0 ? 0 : (int)Math.Ceiling(attack * multiplier - 1e-9);
            if (skill.LimbBonusPerStack > 0)
            {
                int injured = actor.Limbs.Count(l => LimbSystem.ConditionOf(l) != LimbCondition.Intact);
                value += Math.Min(injured, skill.MaxLimbBonusStacks) * skill.LimbBonusPerStack;
            }
            if (critical) value = (int)Math.Ceiling(value * Rules.CritMultiplier - 1e-9);
            return value;
        }

        private void ApplyOnHit(ActorState actor, ActorState target, SkillSpec skill, bool isMain)
        {
            if (isMain && skill.Imbalance && target.ImbalanceImmuneRound != Round)
            {
                target.Imbalance = 1;
                target.ImbalanceImmuneRound = Round + Rules.ImbalanceImmuneRounds;
                Log(target.Name + " 失衡，本回合无法行动。", "control", actor.Id, target.Id);
            }
            if (skill.Bleed > 0 && (skill.BleedChance <= 0 || Roll(skill.BleedChance)))
            {
                target.BleedDamage = skill.Bleed;
                target.BleedTurns = Math.Max(1, skill.BleedTurns);
                Log(target.Name + " 流血 " + skill.Bleed + " × " + target.BleedTurns + " 次行动。", "bleed", actor.Id, target.Id);
            }
            // Only the main target is interrupted: the piercing round's rear targets just take damage.
            if (isMain && skill.Interrupt && !target.Player)
            {
                target.Interrupted = Math.Max(1, target.Interrupted);
                Log(target.Name + " 下一次行动被打断。", "control", actor.Id, target.Id);
            }
            if (skill.PullToFront && !target.Player && Roll(skill.PullChance)) PullToFront(target);
        }

        private void ApplySupport(ActorState actor, ActorState target, SkillSpec skill)
        {
            if (skill.Effect == SkillEffect.Heal)
            {
                int restored = Math.Max(0, Math.Min(SkillValue(actor, skill), target.Definition.MaxHp - target.Hp));
                target.Hp += restored;
                if (skill.Cleanse) target.BleedTurns = 0;
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 恢复 " + restored + (skill.Cleanse ? "，清除流血。" : "。"), "heal", actor.Id, target.Id, restored);
                if (skill.Bleed > 0 && (skill.BleedChance <= 0 || Roll(skill.BleedChance)))
                {
                    target.BleedDamage = skill.Bleed;
                    target.BleedTurns = Math.Max(1, skill.BleedTurns);
                    Log(target.Name + " 流血 " + skill.Bleed + " × " + target.BleedTurns + " 次行动。", "bleed", actor.Id, target.Id);
                }
                return;
            }
            if (skill.Effect == SkillEffect.Protect)
            {
                target.ProtectorId = actor.Id;
                Log(actor.Name + " 守护 " + target.Name + "，本回合代为承受攻击。", "guard", actor.Id, target.Id);
            }
            else if (skill.Cleanse)
            {
                if (target.BleedTurns > 0) target.BleedTurns = 0;
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 移除一处减益。", "heal", actor.Id, target.Id);
            }
            ApplyBuffs(actor, target, skill.Grant, skill);
        }

        // One place applies every buff flag, so Protect and Buff skills share the same mechanics and the
        // caster's own grants (SelfGrant) ride through the same path.
        private void ApplyBuffs(ActorState actor, ActorState target, AllyBuff grant, SkillSpec skill)
        {
            if (target == null || target.Dead) return;
            bool touched = false;
            if ((grant & AllyBuff.Block) != 0) { target.BlockCharges++; touched = true; }
            if ((grant & AllyBuff.Counter) != 0) { target.Counter = true; touched = true; }
            if ((grant & AllyBuff.Followup) != 0) { target.Followup = true; target.FollowupAttack = actor.Definition.Attack; touched = true; }
            if ((grant & AllyBuff.DefenseUp) != 0) { target.DefenseBuff = Math.Max(target.DefenseBuff, skill.GrantDefense); touched = true; }
            if ((grant & AllyBuff.AccuracyUp) != 0) { target.AccuracyBuff = Math.Max(target.AccuracyBuff, skill.GrantAccuracy); touched = true; }
            if (touched) Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 获得增益。", "guard", actor.Id, target.Id);
        }

        private void ApplyRider(ActorState actor, ActorState primary, SkillSpec skill, int damageDealt)
        {
            var rider = skill.Rider;
            if (rider == null || rider.Kind == RiderKind.None) return;
            if (rider.Kind == RiderKind.SanityRestore)
            {
                var chosen = rider.Area == RiderArea.Chosen ? primary : actor;
                if (chosen != null && !chosen.Dead) { ChangeSanity(chosen, rider.Amount, skill.Name); TryMentalState(chosen); }
                return;
            }
            var targets = new List<ActorState> { actor };
            if (rider.Area == RiderArea.SelfAdjacent)
                targets.AddRange(Run.Heroes.Where(a => !a.Dead && a != actor && Math.Abs(a.Rank - actor.Rank) == 1)
                    .OrderBy(a => a.Rank));
            int main = Math.Max(0, (int)Math.Ceiling(damageDealt * rider.Ratio - 1e-9));
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.Dead) continue;
                int amount = i == 0 ? main : (int)Math.Ceiling(main * rider.SpreadRatio - 1e-9);
                int restored = Math.Max(0, Math.Min(amount, target.Definition.MaxHp - target.Hp));
                target.Hp += restored;
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 恢复 " + restored + "。", "heal", actor.Id, target.Id, restored);
            }
        }

        // A follow-up fires whenever a buffed hero lands damage on an enemy. The follow-up hit itself
        // passes fromFollowup, so it can never chain into another follow-up.
        private void TryFollowups(ActorState source, ActorState enemy, bool fromFollowup)
        {
            if (fromFollowup || enemy == null || enemy.Player || enemy.Dead) return;
            foreach (var hero in Run.Heroes.Where(h => !h.Dead && h.Followup).ToArray())
            {
                if (hero.Dead || enemy.Dead) continue;
                int amount = (int)Math.Ceiling(hero.FollowupAttack * Rules.FollowupRatio - 1e-9);
                if (amount <= 0) continue;
                Log(hero.Name + " 追击 " + enemy.Name + "。", "action", hero.Id, enemy.Id);
                Damage(enemy, amount, hero.Id);
            }
        }

        private bool ConsumeBlock(ActorState target)
        {
            if (target.BlockCharges <= 0) return false;
            target.BlockCharges--;
            return true;
        }

        private void TryCounter(ActorState bearer, ActorState attacker)
        {
            if (bearer == null || attacker == null || bearer.Dead || attacker.Dead || !bearer.Counter) return;
            bearer.Counter = false;
            int amount = (int)Math.Ceiling(bearer.Definition.Attack * Rules.CounterRatio - 1e-9);
            Log(bearer.Name + " 反击 " + attacker.Name + "。", "action", bearer.Id, attacker.Id);
            Damage(attacker, amount, bearer.Id);
            TryFollowups(bearer, attacker, false);
        }

        private void MoveSelf(ActorState actor, int delta)
        {
            int destination = actor.Rank + delta;
            if (destination < 0 || destination > 3) return;
            var occupant = Run.Heroes.FirstOrDefault(h => !h.Dead && h != actor && h.Rank == destination);
            if (occupant != null) occupant.Rank = actor.Rank;
            actor.Rank = destination;
            Log(actor.Name + " 移动到 " + (destination + 1) + " 号位。", "move", actor.Id);
        }

        private void PullToFront(ActorState enemy)
        {
            int from = enemy.Rank;
            if (from <= 0) return;
            foreach (var other in Enemies.Where(e => e != enemy && !e.Dead && e.Rank < from)) other.Rank++;
            enemy.Rank = 0;
            Log(enemy.Name + " 被拖拽到 1 号位。", "move", null, enemy.Id);
        }

        // Character attribute helpers. Judgements use the attribute values directly and are never shifted by
        // sanity; the only things that move them are limb damage and the temporary team boosts.
        public double EffectiveAccuracy(ActorState actor, SkillSpec skill)
        {
            if (actor == null || skill == null) return 0;
            return SanityProbability.Clamp(actor.Definition.Accuracy * LimbSystem.AccuracyFactor(actor, Rules.Limbs)
                + (skill.Accuracy - Rules.SkillAccuracyBaseline)
                + Rules.Mental(actor.Mental).AccuracyModifier
                + actor.AccuracyBuff, 0, 1);
        }

        public double EffectiveAttack(ActorState actor)
        {
            return actor.Definition.Attack * LimbSystem.AttackFactor(actor, Rules.Limbs);
        }

        public double EffectiveDefense(ActorState actor)
        {
            return SanityProbability.Clamp((actor.Definition.Defense + actor.DefenseBuff) * LimbSystem.DefenseFactor(actor, Rules.Limbs), 0, 1);
        }

        // Speed drives turn order, so a broken leg has to be visible here rather than on ActorState.Speed.
        public int EffectiveSpeed(ActorState actor)
        {
            return Math.Max(1, (int)Math.Round(actor.Definition.Speed * LimbSystem.SpeedFactor(actor, Rules.Limbs), MidpointRounding.AwayFromZero));
        }

        public int SkillValue(ActorState actor, SkillSpec skill)
        {
            return Scale(actor.Definition.Attack, skill.Multiplier * LimbSystem.AttackFactor(actor, Rules.Limbs));
        }

        public int CritValue(ActorState actor, SkillSpec skill)
        {
            return (int)Math.Ceiling(SkillValue(actor, skill) * Rules.CritMultiplier - 1e-9);
        }

        public int EnemyDamage(ActorState enemy)
        {
            double multiplier = enemy.Definition.Pattern == EnemyPattern.Whisperer ? 0.3
                : enemy.Definition.Pattern == EnemyPattern.Hunter ? 0.5
                : Round % 2 == 0 ? 1.1 : 0.7;
            return Scale(enemy.Definition.Attack, multiplier);
        }

        private static int Scale(int attack, double multiplier)
        {
            if (attack <= 0 || multiplier <= 0) return 0;
            return (int)Math.Ceiling(attack * multiplier - 1e-9); // Epsilon guards against float representation error.
        }

        private bool Roll(double chance)
        {
            if (chance <= 0) return false;
            if (chance >= 1) return true;
            return Run.Random.NextUnit() < chance;
        }

        public string Intent(ActorState enemy)
        {
            if (enemy.Dead) return "已被击败";
            if (enemy.Interrupted > 0) return "下一次行动被打断";
            var target = EnemyTarget(enemy);
            if (target == null) return "—";
            string text;
            switch (enemy.Definition.Pattern)
            {
                case EnemyPattern.Whisperer: text = "低语 → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 理智−14"; break;
                case EnemyPattern.Hunter: text = "追猎 → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 流血2×2"; break;
                default: text = (Round % 2 == 0 ? "重劈" : "斩击") + " → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 前排"; break;
            }
            // A crippling attack has to be announced. This stays pure so re-rendering costs no draws.
            var spec = LimbSystem.ForPattern(enemy.Definition.Pattern);
            if (spec != null && (!spec.HeavyRoundsOnly || Round % 2 == 0))
                text += "\n可能重创" + LimbSystem.PartName(spec.Part);
            return text;
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
                // The guard holds for the whole round now, so ProtectorId is left in place; it is cleared
                // when the protector takes its next turn.
                target = protector;
            }
            if (!Roll(SanityProbability.Clamp(enemy.Definition.Accuracy, 0, 1)))
                Log(target.Name + " 避开了 " + enemy.Name + " 的攻击。", "miss", enemy.Id, target.Id);
            else if (target.Definition.Dodge > 0 && Roll(target.Definition.Dodge))
                Log(target.Name + " 闪避了 " + enemy.Name + " 的攻击。", "dodge", enemy.Id, target.Id);
            else if (ConsumeBlock(target))
            {
                Log(target.Name + " 的格挡免除了这次攻击。", "guard", enemy.Id, target.Id);
                TryCounter(target, enemy);
            }
            else
            {
                Log(enemy.Name + " 发动" + (enemy.Definition.Pattern == EnemyPattern.Whisperer ? "低语。" : "攻击。"), "action", enemy.Id, target.Id);
                Damage(target, EnemyDamage(enemy), enemy.Id);
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Whisperer)
                { ChangeSanity(target, -14, "受到低语侵蚀"); TryMentalState(target); }
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Hunter)
                { target.BleedDamage = 2; target.BleedTurns = 2; Log(target.Name + " 开始流血，2×2次行动。", "bleed", enemy.Id, target.Id); }
                if (!target.Dead) TryLimbDamage(enemy, target);
                TryCounter(target, enemy);
            }
            if (!CheckEnd()) Advance();
        }

        // Limb damage is a second track next to HP, so it only rolls once the attack has actually landed.
        // A miss, a non-crippling pattern, an off-turn heavy swing and an already severed part all spend
        // no draw at all.
        private void TryLimbDamage(ActorState enemy, ActorState target)
        {
            if (!target.Player) return;
            var spec = LimbSystem.ForPattern(enemy.Definition.Pattern);
            if (spec == null) return;
            if (spec.HeavyRoundsOnly && Round % 2 != 0) return;
            if (LimbSystem.ConditionOf(target, spec.Part) == LimbCondition.Severed) return;
            if (!Roll(spec.Chance)) return;
            int amount = LimbSystem.HitAmount(target, spec.Part, Rules.Limbs);
            bool severed = LimbSystem.Damage(target, spec.Part, amount);
            Log(target.Name + " 的" + LimbSystem.PartName(spec.Part) + "受到 " + amount + " 点部位伤害，"
                + (severed ? "已经断肢！" : "陷入重伤。"), "limb", enemy.Id, target.Id, amount);
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
            int actual = amount;
            if (!bleeding)
            {
                double reduced = amount * (1 - EffectiveDefense(actor));
                if (actor.Defending) reduced *= 0.5;
                if (actor.SelfVulnerable > 0) reduced *= 1 + actor.SelfVulnerable;
                actual = Math.Max(1, (int)Math.Ceiling(reduced - 1e-9));
            }
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
