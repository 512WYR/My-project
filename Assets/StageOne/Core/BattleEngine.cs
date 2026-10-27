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
        // Ranks vacated by enemies that died since the last action resolved. The survivors behind each
        // one step forward when the action is over, so the formation never leaves the dead unit's slot
        // as a hole and a later summon cannot share a rank with a corpse.
        private readonly List<int> deathRanks = new List<int>();

        public BattleEngine(RunState run, ActorSpec[] enemies, CombatRules rules)
        {
            Run = run; Rules = rules; Outcome = BattleOutcome.Active;
            foreach (var hero in run.Heroes.Where(x => !x.Dead))
            {
                roster.Add(hero); startSanity.Add(hero.Id, hero.Sanity);
                ResetBattleEffects(hero);
            }
            for (int i = 0; i < enemies.Length; i++)
            {
                var spec = ResolveSummoner(enemies[i]);
                if (spec == null) continue;
                Enemies.Add(new ActorState
                {
                    Id = "enemy_" + i, Definition = spec, Player = false,
                    Rank = spec.StartRank >= 0 ? spec.StartRank : i,
                    // Enemies have no sanity; the field exists because ActorState is shared with heroes.
                    Hp = spec.MaxHp, Sanity = 0
                });
            }
            Run.Battles++;
            Log("第 " + Run.Battles + " 场演练开始。队员理智沿用。", "system");
            if (roster.Count == 0) { Finish(BattleOutcome.Defeat); return; }
            if (Enemies.Count == 0) { Finish(BattleOutcome.Victory); return; }
            ApplyBattleStartAccessories();
            Advance();
        }

        private void ResetBattleEffects(ActorState actor)
        {
            actor.Form = SkillForm.Basic; actor.Defending = false; actor.ProtectorId = null;
            actor.Interrupted = 0; actor.BleedDamage = 0; actor.BleedTurns = 0;
            actor.BlockCharges = 0; actor.Counter = false; actor.Followup = false; actor.FollowupAttack = 0;
            actor.Imbalance = 0; actor.ImbalanceImmuneRound = -1; actor.ImbalanceAutoHitUntilRound = -1;
            actor.DefenseBuff = 0; actor.AccuracyBuff = 0; actor.SelfVulnerable = 0;
            actor.GuardReduction = 0; actor.EnemyActionCount = 0;
            actor.Operations = 0; actor.BlastRound = 0; actor.NextSpawnRound = 0;
            actor.IsCopy = false; actor.SummonedBy = null;
            actor.Uses.Clear();
            actor.AccessoryBattle = new AccessoryBattleState();
            actor.ProstheticBattle = new ProstheticBattleState();
            // 理智锁定药剂 used outside battle primes this fight. A lock bought during the previous fight
            // was already cleared when it settled, so this is the only place a pending lock matures.
            actor.SanityLocked = actor.SanityLockPending;
            actor.SanityLockPending = false;
        }

        // Runs after every hero's per-battle flags are reset and after the enemy roster exists, so the heart
        // can mark a real target. Randomness is only drawn by accessories that are actually equipped.
        private void ApplyBattleStartAccessories()
        {
            foreach (var hero in Run.Heroes.Where(h => h != null && !h.Dead).ToArray())
            {
                var state = hero.AccessoryBattle;
                if (state == null) { state = new AccessoryBattleState(); hero.AccessoryBattle = state; }

                if (AccessorySystem.Has(hero, AccessoryKind.Addiction)) state.AddictionActive = true;

                if (AccessorySystem.Has(hero, AccessoryKind.Heart))
                {
                    var pool = Enemies.Where(e => !e.Dead).ToArray();
                    if (pool.Length > 0)
                    {
                        var target = pool[Math.Min(pool.Length - 1, (int)(Run.Random.NextUnit() * pool.Length))];
                        state.HeartActive = true;
                        state.HeartTargetId = target.Id;
                        Log(hero.Name + " 的跃动心脏标记了 " + target.Name + "。", "system", hero.Id, target.Id);
                    }
                }

                if (AccessorySystem.Has(hero, AccessoryKind.RustMedal))
                {
                    int loss = Math.Max(1, (int)Math.Ceiling(hero.MaxHp * AccessorySystem.RustEntryLoss - 1e-9));
                    Log(hero.Name + " 的锈血勋章索取 " + loss + " 点生命作为入场代价。", "system", hero.Id);
                    Damage(hero, loss, hero.Id, false, true, true);
                    if (AccessorySystem.AddSkillUse(hero, AccessorySystem.RustSkillId, 1) && !hero.Dead)
                        Log(hero.Name + " 的锈血勋章让「带伤强攻」多出 1 次使用。", "system", hero.Id);
                }
            }
        }

        public IEnumerable<ActorState> All { get { return Run.Heroes.Concat(Enemies); } }
        public ActorState Find(string id) { return All.FirstOrDefault(a => a.Id == id); }
        public ActorState[] TurnPreview { get { return turns.Where(a => !a.Dead).ToArray(); } }

        public double[] FormProbabilities(ActorState actor)
        {
            // A hero the medicine chest rewired (EVT-004) never falls back on the plain kit, so the basic
            // row is pinned to zero instead of being offered. Dropping the row would change the shape the
            // inspector and the tests read, and zeroing the weight while keeping the 30/70 gates would make
            // every row disabled in the stable band and throw inside Distribution. The two remaining rows
            // are therefore drawn without gates, which keeps the middle row exactly 0 and the outer two
            // strictly positive at every sanity.
            if (actor != null && actor.SanitySkillsOnly)
            {
                var split = SanityProbability.Distribution(actor.Sanity, new[] {
                    new WeightedOutcome(Rules.TechnicalWeight, 1),
                    new WeightedOutcome(Rules.PrimalWeight, -1)
                }, Rules.ProbabilityStrength);
                return new[] { split[0], 0.0, split[1] };
            }
            return SanityProbability.Distribution(actor.Sanity, new[] {
                new WeightedOutcome(Rules.TechnicalWeight, 1, SanityGate.High),
                new WeightedOutcome(Rules.BasicWeight, 0),
                new WeightedOutcome(Rules.PrimalWeight, -1, SanityGate.Low)
            }, Rules.ProbabilityStrength);
        }

        private void Advance()
        {
            ResolveEnemyRanksAfterDeaths();
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
                    // Boss summons and refills happen before the turn queue is built, so a unit summoned
                    // "at the end of round one" is already standing when round two starts.
                    RunRoundStartHooks();
                    RollRoundSpeeds();
                    foreach (var actor in All.Where(a => !a.Dead).OrderByDescending(a => EffectiveSpeed(a)).ThenBy(a => a.Player ? 0 : 1).ThenBy(a => a.Id, StringComparer.Ordinal))
                    {
                        turns.Enqueue(actor);
                        // 跃动心脏：同一角色紧接着再行动一次。失败后的 ExtraActions 返回 0，下一回合起自然消失。
                        for (int extra = AccessorySystem.ExtraActions(actor); extra > 0; extra--) turns.Enqueue(actor);
                    }
                    Log("—— 第 " + Round + " 回合 ——", "round");
                }
                var next = turns.Dequeue();
                if (next.Dead) continue;
                next.Defending = false;
                // A guard lasts until the protector's own next turn begins, for either side.
                foreach (var ward in All) if (ward.ProtectorId == next.Id) ward.ProtectorId = null;
                if (next.Player) OnProstheticTurnStart(next);
                // A monster on a two-round action interval simply passes on its off round.
                if (!next.Player && !next.Definition.ActsOn(Round))
                {
                    Log(next.Name + " 尚未准备好下一次行动。", "control", null, next.Id);
                    continue;
                }
                if (next.BleedTurns > 0)
                {
                    next.BleedTurns--;
                    int bleedDealt = Damage(next, next.BleedDamage, null, true);
                    Log(next.Name + " 流血，剩余 " + next.BleedTurns + " 次。", "bleed", null, next.Id,
                        Math.Max(0, bleedDealt), 0, "流血");
                    if (CheckEnd()) return;
                    // Bleed can kill an enemy inside Advance, outside any Execute call. Close that gap
                    // before the turn queue picks the next actor.
                    ResolveEnemyRanksAfterDeaths();
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
                    Log(next.Name + " 因失衡跳过行动。", "control", null, next.Id);
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
            if (actor == null) return ActorSpec.EmptySkills;
            // The restricted hero skips the plain kit entirely. FormProbabilities never hands out Basic for
            // them, but the form also starts as Basic in ResetBattleEffects, so this guards that first turn
            // and keeps the action row from ever being empty.
            if (actor.SanitySkillsOnly && actor.Form == SkillForm.Basic)
            {
                var list = actor.Sanity >= 50 ? actor.Definition.TechnicalSkills : actor.Definition.PrimalSkills;
                if (list != null && list.Length > 0) return ProstheticSystem.DecorateSkills(actor, list);
                var other = actor.Sanity >= 50 ? actor.Definition.PrimalSkills : actor.Definition.TechnicalSkills;
                if (other != null && other.Length > 0) return ProstheticSystem.DecorateSkills(actor, other);
            }
            return ProstheticSystem.DecorateSkills(actor, actor.Definition.SkillsFor(actor.Form));
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
            if (skill.OreCost > 0 && (Run.Map == null || Run.Map.Supplies < skill.OreCost))
                return "矿石不足：需要 " + skill.OreCost + " 矿石";
            if (NeedsTarget(skill) && !Targets(index).Any()) return "没有合法目标";
            return null;
        }

        public string SwapDisabledReason()
        {
            if (Outcome != BattleOutcome.Active) return "本场战斗已结束";
            if (Current == null || !Current.Player) return "等待敌方行动";
            if (ProstheticSystem.BlocksActiveMovement(Current))
                return "桩锚支撑足无法主动移动，只能由队友换位。";
            return SwapTargets().Any() ? null : "没有相邻的存活队员";
        }

        // Free swaps come from Steam Jet / Steel Wire legs and cost no action. A pile-anchor hero can
        // still be the target of a teammate's free swap; only initiating is blocked.
        public IEnumerable<ActorState> FreeSwapTargets()
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            return ProstheticSystem.FreeSwapTargets(this, Current);
        }

        public string FreeSwapDisabledReason()
        {
            if (Outcome != BattleOutcome.Active) return "本场战斗已结束";
            if (Current == null || !Current.Player) return "等待敌方行动";
            return ProstheticSystem.FreeSwapDisabledReason(this, Current);
        }

        // Choose skills expose the actors the player may pick as the main target. All skills take no main
        // target, but every living actor on their side is still a valid confirmation click for the UI, so
        // they are returned too; ExecuteSkill ignores the chosen id for them. TargetRanks is not applied to
        // All skills because their assets deliberately leave it at zero.
        public IEnumerable<ActorState> Targets(int skillIndex)
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            var skill = SkillFor(Current, skillIndex);
            if (skill == null) return Enumerable.Empty<ActorState>();
            var side = skill.Target == TargetSide.Ally ? (IEnumerable<ActorState>)Run.Heroes : Enemies;
            if (skill.Targeting == SkillTargeting.All)
            {
                var sweep = side.Where(a => !a.Dead);
                if (skill.AllWithinTargetRanks) sweep = sweep.Where(a => a.OccupiesAnyRank(skill.TargetRanks));
                return sweep.ToList();
            }
            if (!NeedsTarget(skill)) return Enumerable.Empty<ActorState>();
            return side.Where(a => !a.Dead && a.OccupiesAnyRank(skill.TargetRanks)
                && (skill.Effect != SkillEffect.Heal || a.Hp < a.MaxHp || (skill.Cleanse && a.BleedTurns > 0))
                && (skill.Effect != SkillEffect.Protect || a != Current));
        }

        public IEnumerable<ActorState> SwapTargets()
        {
            if (Current == null) return Enumerable.Empty<ActorState>();
            if (ProstheticSystem.BlocksActiveMovement(Current)) return Enumerable.Empty<ActorState>();
            // The anchor is included here for a teammate who initiates the swap.
            return Run.Heroes.Where(a => !a.Dead && a != Current && Math.Abs(a.Rank - Current.Rank) == 1);
        }

        public bool Execute(ActionRequest request, out string error)
        {
            if (request.Kind == ActionKind.Swap) return ExecuteSwap(request, out error);
            return ExecuteSkill(request, out error);
        }

        // A consumable used in battle spends the current hero's action. Anything refused here leaves the
        // stock, the turn and the random sequence exactly as they were.
        public bool UseConsumable(int stockIndex, string targetId, out string note)
        {
            note = null;
            if (Outcome != BattleOutcome.Active) { note = "战斗已经结束。"; return false; }
            if (Current == null || !Current.Player) { note = "现在不是我方行动。"; return false; }
            if (Run.Consumables == null || stockIndex < 0 || stockIndex >= Run.Consumables.Count)
            { note = "请选择库存中的道具。"; return false; }
            var target = Run.Heroes.FirstOrDefault(a => a != null && a.Id == targetId);
            if (target == null) { note = "请选择一名存活队员。"; return false; }
            var item = Run.Consumables[stockIndex];
            int hpBefore = target.Hp;
            bool lockedBefore = target.SanityLocked;
            if (!ConsumableSystem.Use(Run, target, stockIndex, true, out note)) return false;
            if (item.Kind == ConsumableKind.HpRestore)
            {
                Log(note, "heal", Current.Id, target.Id, Math.Max(0, target.Hp - hpBefore));
            }
            else
            {
                bool lockedNow = !lockedBefore && target.SanityLocked;
                Log(note, "sanity", Current.Id, target.Id, 0, 0, lockedNow ? "locked" : null);
            }
            OnProstheticActionEnd(Current);
            if (!CheckEnd()) Advance();
            return true;
        }

        // A surgery-kit fitting is settled by ConsumableSystem because it needs a second choice (which
        // prosthetic), so it cannot go through UseConsumable. Like any item it still spends the current
        // hero's action and runs the same end-of-action prosthetic hooks.
        public void EndTurnAfterSurgery(string note)
        {
            if (Outcome != BattleOutcome.Active || Current == null || !Current.Player) return;
            if (!string.IsNullOrEmpty(note)) Log(note, "limb", Current.Id, Current.Id);
            OnProstheticActionEnd(Current);
            if (!CheckEnd()) Advance();
        }

        private bool ExecuteSwap(ActionRequest request, out string error)
        {
            error = SwapDisabledReason();
            if (error != null) return false;
            var actor = Current;
            var target = SwapTargets().FirstOrDefault(a => a.Id == request.TargetId);
            if (target == null) { error = "请选择相邻的高亮队员"; return false; }
            SwapRanks(actor, target, actor);
            Log(actor.Name + " 与 " + target.Name + " 交换站位。", "move", actor.Id, target.Id);
            OnProstheticActionEnd(actor);
            if (!CheckEnd()) Advance();
            return true;
        }

        // A prosthetic free swap costs no action: the current hero keeps their turn.
        public bool ExecuteFreeSwap(string targetId, out string error)
        {
            error = FreeSwapDisabledReason();
            if (error != null) return false;
            var actor = Current;
            var target = FreeSwapTargets().FirstOrDefault(a => a.Id == targetId);
            if (target == null) { error = "请选择高亮队友"; return false; }
            SwapRanks(actor, target, actor);
            var state = actor.ProstheticBattle;
            if (state != null)
            {
                state.FreeSwapUsed = true;
                if (ProstheticSystem.Has(actor, ProstheticKind.SteamJetLeg))
                {
                    state.SteamSwapAttackBonus = true;
                    state.SteamSwapVulnerable = true;
                }
                if (ProstheticSystem.Has(actor, ProstheticKind.SteelWireLeg))
                {
                    state.SteelWireVulnerable = true;
                    state.SteelWireSourceId = actor.Id;
                    if (target.ProstheticBattle != null)
                    {
                        target.ProstheticBattle.SteelWireVulnerable = true;
                        target.ProstheticBattle.SteelWireSourceId = actor.Id;
                    }
                }
            }
            Log(actor.Name + " 借助义体与 " + target.Name + " 换位，不消耗行动。", "move", actor.Id, target.Id);
            return true;
        }

        private bool ExecuteSkill(ActionRequest request, out string error)
        {
            error = SkillDisabledReason(request.SkillIndex);
            if (error != null) return false;
            var actor = Current;
            var skill = SkillFor(actor, request.SkillIndex);
            ActorState primary = null;
            List<ActorState> chosen = null;
            if (NeedsTarget(skill))
            {
                if (skill.MaxTargets > 1)
                {
                    chosen = ChosenSupportTargets(request, request.SkillIndex, skill);
                    if (chosen.Count == 0) { error = "请选择至少 1 名合法队友"; return false; }
                    primary = chosen[0];
                }
                else
                {
                    primary = skill.RandomTarget
                        ? DrawRandom(Targets(request.SkillIndex).ToArray())
                        : Targets(request.SkillIndex).FirstOrDefault(a => a.Id == request.TargetId);
                    if (primary == null) { error = "请选择高亮的合法目标"; return false; }
                }
            }
            actor.SpendUse(skill);
            // Ore is spent once the action is committed. A miss does not refund it.
            if (skill.OreCost > 0 && Run.Map != null)
                Run.Map.Supplies = Math.Max(0, Run.Map.Supplies - skill.OreCost);
            ResolveSkill(actor, primary, skill, chosen);
            OnProstheticActionEnd(actor);
            if (!CheckEnd()) Advance();
            return true;
        }

        // Double Guard takes up to two allies. The request carries the picks in TargetIds; duplicates and
        // illegal picks are dropped, and the order the player clicked is kept.
        private List<ActorState> ChosenSupportTargets(ActionRequest request, int skillIndex, SkillSpec skill)
        {
            var legal = Targets(skillIndex).ToList();
            var result = new List<ActorState>();
            if (request.TargetIds != null)
            {
                for (int i = 0; i < request.TargetIds.Length && result.Count < skill.MaxTargets; i++)
                {
                    var target = legal.FirstOrDefault(a => a.Id == request.TargetIds[i]);
                    if (target != null && !result.Contains(target)) result.Add(target);
                }
            }
            if (result.Count == 0)
            {
                var first = legal.FirstOrDefault(a => a.Id == request.TargetId);
                if (first != null) result.Add(first);
            }
            return result;
        }

        private ActorState DrawRandom(ActorState[] pool)
        {
            if (pool.Length == 0) return null;
            int index = Math.Min(pool.Length - 1, (int)(Run.Random.NextUnit() * pool.Length));
            return pool[index];
        }

        private void ResolveSkill(ActorState actor, ActorState primary, SkillSpec skill,
            IList<ActorState> chosen = null)
        {
            if (skill.SelfMove != 0) MoveSelf(actor, skill.SelfMove);
            ApplyBuffs(actor, actor, skill.SelfGrant, skill);
            if (skill.Effect == SkillEffect.Attack)
            {
                int actualDealt;
                bool hit = ResolveAttack(actor, primary, skill, out actualDealt);
                ApplyAttackAccessories(actor, skill, hit);
                AfterProstheticAttack(actor, skill, actualDealt);
                ApplyPostSelfMove(actor, skill);
                return;
            }
            // Healing, guarding and buffs land on the whole sweep without a hit roll. A multi-pick guard
            // uses the exact allies the player chose instead of a sweep.
            if (chosen != null)
            {
                foreach (var target in chosen) ApplySupport(actor, target, skill);
            }
            else
            {
                foreach (var target in BuildArea(actor, primary, skill))
                    ApplySupport(actor, target, skill);
            }
            if (skill.Effect == SkillEffect.Heal) ApplyHealAccessories(actor);
            ApplyRider(actor, primary, skill, 0);
        }

        private bool ResolveAttack(ActorState actor, ActorState primary, SkillSpec skill, out int actualDealt)
        {
            actualDealt = 0;
            var set = BuildArea(actor, primary, skill);
            var main = primary ?? set.FirstOrDefault();
            if (main == null) return false;
            if (skill.PerTargetAccuracy) return ResolvePerTargetAttack(actor, primary, set, main, skill, out actualDealt);

            // A displaced enemy in the imbalance window cannot dodge the displacement. The window is
            // independent of the skipped action, so it survives the enemy's consumed turn.
            bool autoHit = skill.PullToFront && main.ImbalanceAutoHitUntilRound >= Round;
            if (!autoHit)
            {
                if (!Roll(EffectiveAccuracy(actor, skill)))
                { Log(actor.Name + " · " + skill.Name + " 未命中。", "miss", actor.Id, main.Id); ApplyRider(actor, primary, skill, 0); return false; }
                if (main.Definition.Dodge > 0 && Roll(main.Definition.Dodge))
                { Log(actor.Name + " · " + skill.Name + " → " + main.Name + " 被闪避。", "dodge", actor.Id, main.Id); return false; }
            }
            // 穿肠弹把暴击变成必然，并因此省掉一次暴击骰。
            bool critical = AccessorySystem.AlwaysCrit(actor) || Roll(actor.Definition.CritChance);
            Log(actor.Name + " · " + skill.Name + (critical ? " 暴击！" : ""), "action", actor.Id, main.Id);
            int dealt = 0;
            foreach (var target in set)
            {
                if (target.Dead) continue;
                int amount = DamageFor(actor, target, skill, main, critical);
                // A tank standing in front of the target takes the hit instead. The guard's own share is
                // the only reduction it adds; the target's defense no longer applies because it was never
                // actually struck.
                var victim = GuardedTarget(target);
                if (victim != target)
                {
                    amount = ReduceForGuard(amount, victim);
                    Log(victim.Name + " 为 " + target.Name + " 挡下攻击。", "guard", victim.Id, target.Id);
                }
                actualDealt += Damage(victim, amount, actor.Id);
                dealt += amount;
                if (!victim.Dead) ApplyOnHit(actor, victim, skill, target == main);
                // A guard only intercepts damage. Position effects still belong to the target the player
                // selected, otherwise the tank at rank one is "pulled" to rank one and nothing happens.
                if (target == main && skill.PullToFront && !target.Dead) TryPull(actor, target, skill);
            }
            ApplyRider(actor, primary, skill, dealt);
            TryFollowups(actor, main, false);
            return true;
        }

        // Boiler Shock rolls accuracy and dodge separately per victim. Critical is still one shared roll.
        // A victim that misses or dodges is skipped; the action counts as a hit if any victim was struck.
        private bool ResolvePerTargetAttack(ActorState actor, ActorState primary, IList<ActorState> set,
            ActorState main, SkillSpec skill, out int actualDealt)
        {
            actualDealt = 0;
            bool critical = AccessorySystem.AlwaysCrit(actor) || Roll(actor.Definition.CritChance);
            Log(actor.Name + " · " + skill.Name + (critical ? " 暴击！" : ""), "action", actor.Id, main.Id);
            bool anyHit = false;
            int dealt = 0;
            foreach (var target in set)
            {
                if (target.Dead) continue;
                bool autoHit = skill.PullToFront && target.ImbalanceAutoHitUntilRound >= Round;
                if (!autoHit)
                {
                    if (!Roll(EffectiveAccuracy(actor, skill)))
                    {
                        Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 未命中。", "miss", actor.Id, target.Id);
                        continue;
                    }
                    if (target.Definition.Dodge > 0 && Roll(target.Definition.Dodge))
                    {
                        Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 被闪避。", "dodge", actor.Id, target.Id);
                        continue;
                    }
                }
                int amount = DamageFor(actor, target, skill, main, critical);
                var victim = GuardedTarget(target);
                if (victim != target)
                {
                    amount = ReduceForGuard(amount, victim);
                    Log(victim.Name + " 为 " + target.Name + " 挡下攻击。", "guard", victim.Id, target.Id);
                }
                actualDealt += Damage(victim, amount, actor.Id);
                dealt += amount;
                bool isMain = target == main || skill.AllWithinTargetRanks;
                if (!victim.Dead) ApplyOnHit(actor, victim, skill, isMain);
                if (target == main && skill.PullToFront && !target.Dead) TryPull(actor, target, skill);
                anyHit = true;
            }
            ApplyRider(actor, primary, skill, anyHit ? dealt : 0);
            if (anyHit) TryFollowups(actor, main, false);
            return anyHit;
        }

        // 穿肠弹和成瘾物 both ride on the attack action. The gutter's backlash only lands on a hit; the
        // addiction's own top-up and vulnerability stack happen every time the hero attacks, hit or miss.
        private void ApplyAttackAccessories(ActorState actor, SkillSpec skill, bool hit)
        {
            if (actor == null || !actor.Player || actor.Dead) return;
            if (hit && AccessorySystem.Has(actor, AccessoryKind.Gutter))
            {
                if (!AccessorySystem.LimbLock(actor))
                {
                    var part = AccessorySystem.RandomPart(Run.Random, actor);
                    var limb = LimbSystem.Get(actor, part);
                    var conditionBefore = limb == null ? LimbCondition.Intact : LimbSystem.ConditionOf(limb);
                    bool severed = LimbSystem.Damage(actor, part, AccessorySystem.GutterLimbDamage);
                    bool milestone = severed
                        || (limb != null && LimbSystem.ConditionOf(limb) != conditionBefore);
                    Log(actor.Name + " 的穿肠弹反噬" + LimbSystem.PartName(part) + " -" + AccessorySystem.GutterLimbDamage
                        + (severed ? "，部位断裂！" : milestone ? "，陷入受伤。" : "。"), "limb", actor.Id, actor.Id,
                        milestone ? AccessorySystem.GutterLimbDamage : 0,
                        severed ? 1 : 0, LimbSystem.PartName(part), milestone);
                }
                actor.BleedDamage += AccessorySystem.GutterBleed;
                actor.BleedTurns = Math.Max(actor.BleedTurns, 2);
                Log(actor.Name + " 因穿肠弹流血 " + actor.BleedDamage + " × " + actor.BleedTurns + " 次行动。",
                    "bleed", actor.Id, actor.Id, AccessorySystem.GutterBleed, 0,
                    "流血 " + actor.BleedDamage + "×" + actor.BleedTurns);
            }
            if (AccessorySystem.Has(actor, AccessoryKind.Addiction) && actor.AccessoryBattle.AddictionActive)
            {
                ActorState target = actor;
                if (target.Sanity >= 100)
                {
                    var pool = Run.Heroes.Where(h => !h.Dead && h.Sanity < 100).ToArray();
                    target = pool.Length == 0 ? null
                        : pool[Math.Min(pool.Length - 1, (int)(Run.Random.NextUnit() * pool.Length))];
                }
                if (target != null)
                    ChangeSanity(target, AccessorySystem.AddictionSanityRestore, "成瘾物");
                var state = actor.AccessoryBattle;
                state.AddictionStacks = Math.Min(AccessorySystem.AddictionMaxStacks, state.AddictionStacks + 1);
                Log(actor.Name + " 的成瘾加深：受到伤害 +"
                    + ((AccessorySystem.DamageTakenFactor(actor) - 1) * 100).ToString("0.#") + "%。", "sanity", actor.Id);
            }
        }

        // 献血针筒: one self-cost per heal skill cast, not per target, so a party heal still charges once.
        private void ApplyHealAccessories(ActorState actor)
        {
            if (actor == null || !actor.Player || actor.Dead || !AccessorySystem.Has(actor, AccessoryKind.Syringe)) return;
            var victim = AccessorySystem.RandomLivingHero(Run, Run.Random);
            if (victim == null) return;
            int amount = Math.Max(1, (int)Math.Ceiling(victim.MaxHp * AccessorySystem.SyringeSelfCost - 1e-9));
            Log(actor.Name + " 的献血针筒索取 " + victim.Name + " 的最大生命 " + amount + " 点。", "damage", actor.Id, victim.Id, amount);
            Damage(victim, amount, actor.Id, false, true);
        }

        // The sweep around the main target. Only enemy-side skills read Adjacent/Splash/Pierce; ally-side
        // All/Party skills simply take the whole living side.
        private IList<ActorState> BuildArea(ActorState actor, ActorState primary, SkillSpec skill)
        {
            var side = skill.Target == TargetSide.Ally ? (IEnumerable<ActorState>)Run.Heroes : Enemies;
            // A rank-bounded sweep is narrower than a whole-side sweep and takes precedence over it.
            if (skill.AllWithinTargetRanks)
                return side.Where(a => !a.Dead && a.OccupiesAnyRank(skill.TargetRanks)).ToList();
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
                        .OrderBy(a => RankGap(a, primary)).ThenBy(a => a.Rank).Take(skill.AreaMax));
                    break;
                case SkillArea.Pierce:
                    result.AddRange(side.Where(a => !a.Dead && a != primary && a.Rank > primary.Rank + primary.Width - 1)
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
            var actor = side.FirstOrDefault(a => !a.Dead && a.OccupiesRank(rank));
            if (actor != null && !result.Contains(actor)) result.Add(actor);
        }

        // Distance between an actor and a rank, treating every rank a wide actor covers as distance 0.
        private static int RankGap(ActorState actor, ActorState other)
        {
            if (actor.OccupiesAnyRank(other.Rank)) return 0;
            if (other.OccupiesAnyRank(actor.Rank)) return 0;
            int left = Math.Abs(actor.Rank - other.Rank);
            int right = Math.Abs(actor.Rank - (other.Rank + other.Width - 1));
            return Math.Min(left, right);
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
            // A rank-bounded sweep hits every selected target for the full value; only a real splash
            // around a single main target decays.
            if (target != main && !skill.AllWithinTargetRanks)
            {
                int step = Math.Max(1, Math.Abs(target.Rank - main.Rank));
                if (skill.Area == SkillArea.Pierce) multiplier = Math.Max(0.1, multiplier - skill.AreaDecay * step);
                else multiplier *= skill.AreaRatio;
            }
            // The wound gambit drops every limb penalty for this one swing.
            double attack = skill.IgnoreLimbPenalty ? actor.Definition.Attack
                : actor.Definition.Attack * LimbSystem.AttackFactor(actor, Rules.Limbs);
            attack *= AccessorySystem.AttackMultiplier(actor);
            if (skill.HpMissingDamageScale > 0 && actor.MaxHp > 0)
                multiplier += skill.HpMissingDamageScale * Math.Max(0, actor.MaxHp - actor.Hp) / actor.MaxHp;
            multiplier *= 1 + ProstheticSystem.OutgoingDamageBonus(actor, skill,
                target == main || skill.AllWithinTargetRanks);
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
            if (isMain && skill.Imbalance)
            {
                if (target.ImbalanceImmuneRound != Round)
                {
                    target.Imbalance = 1;
                    target.ImbalanceImmuneRound = Round + Rules.ImbalanceImmuneRounds;
                    target.ImbalanceAutoHitUntilRound = Rules.ImbalanceAutoHitRounds > 0
                        ? Round + Rules.ImbalanceAutoHitRounds - 1 : -1;
                    Log(target.Name + " 失衡：下一次行动将被跳过；位移必中 " + Rules.ImbalanceAutoHitRounds + " 回合。",
                        "control", actor.Id, target.Id);
                }
                else
                {
                    // The immunity is intentional, but silently refusing a second bash reads like a bug.
                    Log(target.Name + " 处于失衡免疫，本次失衡未生效。", "control", actor.Id, target.Id);
                }
            }
            if (skill.Bleed > 0 && (skill.BleedChance <= 0 || Roll(skill.BleedChance)))
            {
                target.BleedDamage = skill.Bleed;
                target.BleedTurns = Math.Max(1, skill.BleedTurns);
                Log(target.Name + " 流血 " + skill.Bleed + " × " + target.BleedTurns + " 次行动。", "bleed",
                    actor.Id, target.Id, 0, 0, "流血 " + target.BleedDamage + "×" + target.BleedTurns);
            }
            // Only the main target is interrupted: the piercing round's rear targets just take damage.
            if (isMain && skill.Interrupt && !target.Player)
            {
                target.Interrupted = Math.Max(1, target.Interrupted);
                Log(target.Name + " 下一次行动被打断。", "control", actor.Id, target.Id);
            }
        }

        // Prosthetic bleeds stack without ever weakening what is already there: take the higher damage
        // value and the longer duration.
        private void ApplyLingeringBleed(ActorState target, int damage, int turns, string sourceId)
        {
            if (target == null || target.Dead || damage <= 0) return;
            target.BleedDamage = Math.Max(target.BleedDamage, damage);
            target.BleedTurns = Math.Max(target.BleedTurns, Math.Max(1, turns));
            Log(target.Name + " 流血 " + target.BleedDamage + " × " + target.BleedTurns + " 次行动。", "bleed",
                sourceId, target.Id, 0, 0, "流血 " + target.BleedDamage + "×" + target.BleedTurns);
        }

        // The pull roll lives outside ApplyOnHit so a guarded hit cannot move the guard instead of the
        // selected target, and so a failed roll leaves a readable log line.
        private void TryPull(ActorState actor, ActorState target, SkillSpec skill)
        {
            if (target == null || target.Dead || target.Player) return;
            if (!Roll(skill.PullChance))
            {
                Log(actor.Name + " · " + skill.Name + " 的拖拽未生效（"
                    + Math.Round(skill.PullChance * 100) + "% 概率未触发）。", "move", actor.Id, target.Id);
                return;
            }
            PullToFront(target);
        }

        private void ApplySupport(ActorState actor, ActorState target, SkillSpec skill)
        {
            if (skill.Effect == SkillEffect.Heal)
            {
                int restored = ProstheticSystem.RestoreHp(target, SkillValue(actor, skill));
                if (skill.Cleanse) target.BleedTurns = 0;
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 恢复 " + restored + (skill.Cleanse ? "，清除流血。" : "。"), "heal", actor.Id, target.Id, restored);
                if (skill.Bleed > 0 && (skill.BleedChance <= 0 || Roll(skill.BleedChance)))
                {
                    // Boiling Mist's bleed is deliberately weaker than the medic's usual 3/4, so it must
                    // never replace a stronger bleed already on the target.
                    if (skill.Id == "m_boiling_mist")
                        ApplyLingeringBleed(target, skill.Bleed, skill.BleedTurns, actor.Id);
                    else
                    {
                        target.BleedDamage = skill.Bleed;
                        target.BleedTurns = Math.Max(1, skill.BleedTurns);
                        Log(target.Name + " 流血 " + skill.Bleed + " × " + target.BleedTurns + " 次行动。", "bleed",
                            actor.Id, target.Id, 0, 0, "流血 " + target.BleedDamage + "×" + target.BleedTurns);
                    }
                }
                return;
            }
            if (skill.Effect == SkillEffect.Protect)
            {
                target.ProtectorId = actor.Id;
                // The reduction belongs to the guarding actor, not the ward, and only ever applies to a
                // hit it intercepts. The elite tank keeps 25%; everyone else keeps zero.
                actor.GuardReduction = Math.Max(0, skill.GuardReduction);
                if (skill.MultiGuard && actor.ProstheticBattle != null)
                    actor.ProstheticBattle.DoubleGuardActive = true;
                // Keep the " · " skill prefix here too: the cut-in resolves the skill portrait from the
                // log line, and a protect beat has no action event of its own.
                Log(actor.Name + " · " + skill.Name + " → " + target.Name + "，持续到自身下次行动。",
                    "guard", actor.Id, target.Id);
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
            var labels = new List<string>(5);
            if ((grant & AllyBuff.Block) != 0)
            {
                target.BlockCharges++;
                labels.Add("格挡×1");
                touched = true;
            }
            if ((grant & AllyBuff.Counter) != 0)
            {
                target.Counter = true;
                labels.Add("反击");
                touched = true;
            }
            if ((grant & AllyBuff.Followup) != 0)
            {
                target.Followup = true;
                target.FollowupAttack = actor.Definition.Attack;
                labels.Add("追击");
                touched = true;
            }
            if ((grant & AllyBuff.DefenseUp) != 0)
            {
                target.DefenseBuff = Math.Max(target.DefenseBuff, skill.GrantDefense);
                labels.Add("防御力+" + (skill.GrantDefense * 100).ToString("0.#") + "%");
                touched = true;
            }
            if ((grant & AllyBuff.AccuracyUp) != 0)
            {
                target.AccuracyBuff = Math.Max(target.AccuracyBuff, skill.GrantAccuracy);
                labels.Add("命中+" + (skill.GrantAccuracy * 100).ToString("0.#") + "%");
                touched = true;
            }
            if (!touched) return;
            string detail = string.Join("、", labels);
            Log(actor.Name + " · " + skill.Name + " → " + target.Name + " 获得 " + detail + "。",
                "buff", actor.Id, target.Id, 0, 0, detail);
        }

        private void ApplyRider(ActorState actor, ActorState primary, SkillSpec skill, int damageDealt)
        {
            var rider = skill.Rider;
            if (rider == null || rider.Kind == RiderKind.None) return;
            if (rider.Kind == RiderKind.SanityRestore)
            {
                var chosen = rider.Area == RiderArea.Chosen ? primary : actor;
                if (chosen != null && !chosen.Dead) ChangeSanity(chosen, rider.Amount, skill.Name);
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
                int restored = ProstheticSystem.RestoreHp(target, amount);
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

        // Only enemies guard in this design. One level of redirection is enough: a guarded protector cannot
        // chain the hit onward.
        private ActorState GuardedTarget(ActorState target)
        {
            if (target == null || target.Player || target.Dead || string.IsNullOrEmpty(target.ProtectorId)) return target;
            var protector = Enemies.FirstOrDefault(e => e.Id == target.ProtectorId && !e.Dead);
            return protector == null || protector == target ? target : protector;
        }

        private static int ReduceForGuard(int amount, ActorState guard)
        {
            if (amount <= 0 || guard == null) return amount;
            double reduction = guard.GuardReduction;
            // 殉道锁链 adds another 50% to whatever share the guard already absorbs.
            if (AccessorySystem.Has(guard, AccessoryKind.MartyrChain))
                reduction = 1 - (1 - reduction) * (1 - AccessorySystem.MartyrGuardReduction);
            // Double Guard raises the share the shield actually absorbs by 25%. This is a multiplier on
            // the intercepted damage, separate from the guard's own reduction.
            double intercept = ProstheticSystem.GuardInterceptMultiplier(guard);
            if (intercept == 1.0)
            {
                if (reduction <= 0) return amount;
                return Math.Max(1, (int)Math.Ceiling(amount * (1 - reduction) - 1e-9));
            }
            return Math.Max(1, (int)Math.Ceiling(amount * Math.Max(0, 1 - reduction) * intercept - 1e-9));
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
            int amount = (int)Math.Ceiling(bearer.Definition.Attack * Rules.CounterRatio
                * ProstheticSystem.CounterMultiplier(bearer) - 1e-9);
            Log(bearer.Name + " 反击 " + attacker.Name + "。", "action", bearer.Id, attacker.Id);
            Damage(attacker, amount, bearer.Id);
            // The flywheel only doubles the counter; every actual counter then takes 8% of max life.
            if (ProstheticSystem.Has(bearer, ProstheticKind.VengeanceFlywheelArm))
                PayLife(bearer, Math.Max(1, (int)Math.Ceiling(bearer.MaxHp * 0.08 - 1e-9)), "复仇飞轮臂");
            TryFollowups(bearer, attacker, false);
        }

        // ---------------- prosthetic lifecycle ----------------

        // Round-scoped prosthetic flags expire when the wearer's own turn begins. The steel-wire
        // vulnerability belongs to the caster's turn, so it clears on every actor the caster marked.
        private void OnProstheticTurnStart(ActorState actor)
        {
            if (actor == null) return;
            var state = actor.ProstheticBattle;
            if (state == null) return;
            state.FreeSwapUsed = false;
            state.SteamSwapAttackBonus = false;
            state.RankChangedThisAction = false;
            state.SteamSwapVulnerable = false;
            state.BoilerVulnerable = false;
            state.DoubleGuardActive = false;

            foreach (var hero in Run.Heroes)
            {
                var other = hero.ProstheticBattle;
                if (other == null) continue;
                if (other.SteelWireVulnerable && other.SteelWireSourceId == actor.Id)
                    other.SteelWireVulnerable = false;
                // Defensive cleanup: a wire whose user died must not leave a permanent vulnerability.
                if (other.SteelWireVulnerable && !string.IsNullOrEmpty(other.SteelWireSourceId)
                    && !Run.Heroes.Any(h => h.Id == other.SteelWireSourceId && !h.Dead))
                    other.SteelWireVulnerable = false;
            }
        }

        // Runs once after the hero's action resolves, before the turn advances.
        private void OnProstheticActionEnd(ActorState actor)
        {
            if (actor == null || actor.Dead) return;
            if (ProstheticSystem.Has(actor, ProstheticKind.CondensationHead))
                ChangeSanity(actor, 8, "冷凝镇静颅");
            var state = actor.ProstheticBattle;
            if (ProstheticSystem.Has(actor, ProstheticKind.BedframeLeg)
                && state != null && !state.RankChangedThisAction)
            {
                int restored = ProstheticSystem.RestoreHp(actor,
                    Math.Max(1, (int)Math.Ceiling(actor.MaxHp * 0.05 - 1e-9)));
                if (restored > 0)
                    Log(actor.Name + " 的驻床支撑架恢复 " + restored + " 点生命。", "heal", actor.Id, actor.Id, restored);
            }
        }

        // Self-costs and on-hit prosthetic effects ride on an active attack skill only.
        private void AfterProstheticAttack(ActorState actor, SkillSpec skill, int actualDealt)
        {
            if (actor == null || actor.Dead || skill == null || skill.Effect != SkillEffect.Attack) return;
            // Detonation Stroke pays whether the attack hit or missed, and always leaves at least 1 HP.
            if (ProstheticSystem.Has(actor, ProstheticKind.DetonationStrokeArm))
                PayLife(actor, Math.Max(1, (int)Math.Ceiling(actor.MaxHp * 0.15 - 1e-9)), "爆燃冲程臂");

            var state = actor.ProstheticBattle;
            if (state != null)
            {
                if (state.GyroCharged) state.GyroCharged = false;
                if (skill.Id == "w_boiler_shock" && ProstheticSystem.Has(actor, ProstheticKind.SteamCityHammerArm))
                    state.BoilerVulnerable = true;
            }
            if (actualDealt <= 0) return;
            if (ProstheticSystem.Has(actor, ProstheticKind.RedlineSteamHeart))
            {
                int restored = ProstheticSystem.RestoreHp(actor,
                    Math.Max(1, (int)Math.Ceiling(actor.MaxHp * 0.10 - 1e-9)));
                if (restored > 0)
                    Log(actor.Name + " 的红线蒸汽心恢复 " + restored + " 点生命。", "heal", actor.Id, actor.Id, restored);
                ChangeSanity(actor, -8, "红线蒸汽心");
            }
            if (ProstheticSystem.Has(actor, ProstheticKind.LeechReturnArm))
            {
                int restored = ProstheticSystem.RestoreHp(actor,
                    Math.Max(1, (int)Math.Ceiling(actor.MaxHp * 0.15 - 1e-9)));
                if (restored > 0)
                    Log(actor.Name + " 的刺吸回流臂恢复 " + restored + " 点生命。", "heal", actor.Id, actor.Id, restored);
                ApplyLingeringBleed(actor, ProstheticSystem.SelfBleedDamage, ProstheticSystem.SelfBleedTurns, actor.Id);
            }
        }

        // Direct, self-inflicted health cost. Bypasses defence, guard and vulnerability; leaves 1 HP.
        private void PayLife(ActorState actor, int amount, string reason)
        {
            if (actor == null || actor.Dead || amount <= 0) return;
            int paid = Math.Min(amount, Math.Max(0, actor.Hp - 1));
            if (paid <= 0) return;
            Damage(actor, paid, actor.Id, false, true, true);
            Log(actor.Name + " 的" + reason + "索取 " + paid + " 点生命。", "damage", actor.Id, actor.Id, paid);
        }

        private void NoteRankChanged(ActorState actor, bool ownAction)
        {
            if (actor == null || actor.Dead) return;
            var state = actor.ProstheticBattle;
            if (ProstheticSystem.Has(actor, ProstheticKind.GyroChargeArm) && state != null)
                state.GyroCharged = true;
            if (ProstheticSystem.Has(actor, ProstheticKind.BedframeLeg))
                ChangeSanity(actor, -5, "驻床支撑架");
            if (ownAction && state != null) state.RankChangedThisAction = true;
        }

        // Single place for every rank exchange so gyro charging and the bedframe sanity cost always fire.
        private void SwapRanks(ActorState a, ActorState b, ActorState initiator)
        {
            if (a == null || b == null) return;
            int rank = a.Rank; a.Rank = b.Rank; b.Rank = rank;
            NoteRankChanged(a, a == initiator);
            NoteRankChanged(b, b == initiator);
        }

        private void MoveSelf(ActorState actor, int delta)
        {
            if (actor == null || actor.Dead || delta == 0) return;
            // A pile anchor refuses every active move; attacks that move the caster resolve in place.
            if (ProstheticSystem.BlocksActiveMovement(actor))
            {
                Log(actor.Name + " 的桩锚支撑足钉住原地，移动未执行。", "move", actor.Id);
                return;
            }
            int step = Math.Sign(delta);
            int remaining = Math.Abs(delta);
            bool moved = false;
            while (remaining-- > 0)
            {
                int destination = actor.Rank + step;
                if (destination < 0 || destination > 3) break;
                var occupant = Run.Heroes.FirstOrDefault(h => !h.Dead && h != actor && h.Rank == destination);
                if (occupant != null) SwapRanks(actor, occupant, actor);
                else { actor.Rank = destination; NoteRankChanged(actor, true); }
                moved = true;
            }
            if (moved) Log(actor.Name + " 移动到 " + (actor.Rank + 1) + " 号位。", "move", actor.Id);
        }

        // Whaling Anchor's forward step. It is independent of whether the shot hit or the pull worked.
        private void ApplyPostSelfMove(ActorState actor, SkillSpec skill)
        {
            if (actor == null || actor.Dead || skill == null || skill.PostSelfMove == 0) return;
            if (ProstheticSystem.BlocksActiveMovement(actor)) return;
            int destination = actor.Rank + skill.PostSelfMove;
            if (destination < 0 || destination > 3) return;
            var occupant = Run.Heroes.FirstOrDefault(h => !h.Dead && h != actor && h.Rank == destination);
            if (occupant == null)
            {
                if (skill.PostSelfMoveRequiresAlly) return;
                actor.Rank = destination;
                NoteRankChanged(actor, true);
            }
            else SwapRanks(actor, occupant, actor);
            Log(actor.Name + " 用后移动到 " + (actor.Rank + 1) + " 号位。", "move", actor.Id);
        }

        private void PullToFront(ActorState enemy)
        {
            if (enemy.Definition.Immobile)
            {
                Log(enemy.Name + " 固定在原地，没有被拖拽。", "move", null, enemy.Id);
                return;
            }
            int from = enemy.Rank;
            if (from <= 0)
            {
                Log(enemy.Name + " 已经在 1 号位，无需拖拽。", "move", null, enemy.Id);
                return;
            }
            // Wide and immobile units do not step aside, so a pull either finds room or fails.
            int shift = enemy.Width;
            foreach (var other in Enemies.Where(e => e != enemy && !e.Dead && e.Rank < from && !e.Definition.Immobile).ToArray())
                other.Rank = Math.Min(3, other.Rank + shift);
            enemy.Rank = 0;
            Log(enemy.Name + " 被拖拽到 1 号位。", "move", null, enemy.Id);
        }

        // Character attribute helpers. Judgements use the attribute values directly and are never shifted by
        // sanity; the only things that move them are limb damage and the temporary team boosts.
        public double EffectiveAccuracy(ActorState actor, SkillSpec skill)
        {
            if (actor == null || skill == null) return 0;
            double value = SanityProbability.Clamp(actor.Definition.Accuracy * LimbSystem.AccuracyFactor(actor, Rules.Limbs)
                + (skill.Accuracy - Rules.SkillAccuracyBaseline)
                + actor.AccuracyBuff
                // Prosthetic accuracy is authored in percentage points.
                + ProstheticSystem.AccuracyOffset(actor), 0, 1);
            // 穿肠弹 raises accuracy to a floor, not to a fixed value.
            return Math.Max(value, AccessorySystem.AccuracyFloor(actor));
        }

        public double EffectiveAttack(ActorState actor)
        {
            return actor.Definition.Attack * LimbSystem.AttackFactor(actor, Rules.Limbs)
                * AccessorySystem.AttackMultiplier(actor);
        }

        public double EffectiveDefense(ActorState actor)
        {
            return SanityProbability.Clamp((actor.Definition.Defense + actor.DefenseBuff) * LimbSystem.DefenseFactor(actor, Rules.Limbs), 0, 1);
        }

        // One draw per living actor with a real speed span, in the stable roster order (heroes, then
        // enemies). Called after round-start hooks so units summoned at the start of the round are included;
        // dead actors and fixed-speed actors spend no draw. The queue is built from these values.
        private void RollRoundSpeeds()
        {
            foreach (var actor in All.Where(a => !a.Dead).ToArray())
                actor.SpeedRoll = actor.Definition.RollSpeed(Run.Random);
        }

        // Speed drives turn order, so a broken leg has to be visible here rather than on ActorState.Speed.
        // The limb factor is applied after the round's roll; before the first roll (or for a unit summoned
        // mid-round that has not acted yet) the base speed is the fallback.
        public int EffectiveSpeed(ActorState actor)
        {
            int baseSpeed = actor.SpeedRoll > 0 ? actor.SpeedRoll : actor.Definition.Speed;
            return Math.Max(1, (int)Math.Round(baseSpeed * LimbSystem.SpeedFactor(actor, Rules.Limbs), MidpointRounding.AwayFromZero));
        }

        public int SkillValue(ActorState actor, SkillSpec skill)
        {
            double multiplier = skill.Multiplier;
            if (skill.Effect == SkillEffect.Attack)
                multiplier *= AccessorySystem.AttackMultiplier(actor)
                    * (1 + ProstheticSystem.OutgoingDamageBonus(actor, skill, true));
            else if (skill.Effect == SkillEffect.Heal)
                multiplier *= AccessorySystem.HealMultiplier(actor)
                    * (1 + ProstheticSystem.CasterHealBonus(actor, skill));
            return Scale(actor.Definition.Attack, multiplier * LimbSystem.AttackFactor(actor, Rules.Limbs));
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

        // The heart marks one enemy for as long as its contract is open. Cards read this to keep the
        // target visible; success and the round-three failure both resolve the state, so the marker
        // disappears on its own. This is a pure query: it draws no randomness.
        public bool IsHeartTarget(ActorState enemy)
        {
            if (enemy == null || enemy.Dead) return false;
            return Run.Heroes.Any(h => h != null && h.AccessoryBattle != null
                && h.AccessoryBattle.HeartActive && !h.AccessoryBattle.HeartResolved
                && h.AccessoryBattle.HeartTargetId == enemy.Id);
        }

        public string Intent(ActorState enemy)
        {
            if (enemy.Dead) return "已被击败";
            // Imbalance has to be visible on the enemy card; otherwise a skipped turn looks like the
            // skill did nothing at all. The displacement window can outlive the skipped action, so it is
            // shown separately as well.
            if (enemy.Imbalance > 0)
                return enemy.ImbalanceAutoHitUntilRound >= Round ? "失衡：下次行动跳过 · 位移必中" : "失衡：下次行动跳过";
            if (enemy.ImbalanceAutoHitUntilRound >= Round) return "失衡余波：位移必中";
            if (enemy.Interrupted > 0) return "下一次行动被打断";
            bool noSkills = enemy.Definition.BasicSkills == null || enemy.Definition.BasicSkills.Length == 0;
            return noSkills && enemy.Definition.Behavior == EnemyBehavior.Simple
                ? LegacyIntent(enemy)
                : DesignedIntent(enemy);
        }

        private string LegacyIntent(ActorState enemy)
        {
            var target = EnemyTarget(enemy);
            if (target == null) return "—";
            string text;
            switch (enemy.Definition.Pattern)
            {
                case EnemyPattern.Whisperer: text = "低语 → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 理智−14"; break;
                case EnemyPattern.Hunter: text = "追猎 → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 流血2×2"; break;
                default: text = (Round % 2 == 0 ? "重劈" : "斩击") + " → " + target.Name + "\n伤害" + EnemyDamage(enemy) + " · 前排"; break;
            }
            // Every physical hit wears a random part, so the card can say so without a pattern lookup.
            // This stays pure so re-rendering costs no draws.
            return text + "\n命中磨损随机部位";
        }

        // Pure preview: it reads the round and the action counter but never draws from the random sequence,
        // so a re-render cannot change the fight.
        private string DesignedIntent(ActorState enemy)
        {
            var def = enemy.Definition;
            switch (def.Behavior)
            {
                case EnemyBehavior.SteamMachine:
                    if (enemy.BlastRound > 0 && Round >= enemy.BlastRound)
                    {
                        var blast = FirstUsableSkill(enemy);
                        return blast == null ? "蓄能完毕" : blast.Name + " → 我方全体\n伤害 " + SkillValue(enemy, blast);
                    }
                    return "等待操作（累计 " + enemy.Operations + "/2）";
                case EnemyBehavior.Operator:
                    if (enemy.Rank != 3) return "后撤 → 尝试回到 4 号位";
                    var machine = Enemies.FirstOrDefault(e => !e.Dead && e.Definition.Behavior == EnemyBehavior.SteamMachine);
                    return machine == null ? "没有可操作的机器" : "操作" + machine.Name + "（累计 " + machine.Operations + "/2）";
                case EnemyBehavior.Healer:
                    if (enemy.Rank == 0) return "后撤 → 离开 1 号位，放弃本次治疗";
                    var heal = FirstUsableSkill(enemy, SkillEffect.Heal);
                    return heal == null ? "待机" : heal.Name + " → 最虚弱的同伴\n恢复 " + SkillValue(enemy, heal);
                case EnemyBehavior.Tank:
                    bool guard = enemy.Rank >= 2 || enemy.EnemyActionCount % 2 == 0;
                    var protect = FirstUsableSkill(enemy, SkillEffect.Protect);
                    if (guard && protect != null) return protect.Name + " → 保护优先级最高的同伴\n" + GuardPriorityHint(enemy);
                    var tankAttack = FirstUsableSkill(enemy, SkillEffect.Attack);
                    return tankAttack == null ? "待机" : SkillIntent(enemy, tankAttack);
                case EnemyBehavior.GiantBoss:
                    int step = enemy.EnemyActionCount % 5;
                    int index = step == 0 || step == 2 ? 0 : step == 1 || step == 3 ? 1 : 2;
                    var skills = def.BasicSkills;
                    return skills == null || index >= skills.Length ? "待机" : SkillIntent(enemy, skills[index]);
                default:
                    var skill = def.Behavior == EnemyBehavior.Sanity
                        ? FirstUsableSkill(enemy, SkillEffect.Sanity) ?? FirstUsableSkill(enemy, SkillEffect.Attack)
                        : FirstUsableSkill(enemy);
                    if (skill == null) return enemy.Definition.Behavior == EnemyBehavior.RandomSummoner ? "等待被召唤" : "待机";
                    return SkillIntent(enemy, skill);
            }
        }

        private string SkillIntent(ActorState enemy, SkillSpec skill)
        {
            string target = skill.TargetCount > 1 ? "我方 " + skill.TargetCount + " 人" : TargetRankText(skill.TargetRanks);
            if (skill.Effect == SkillEffect.Sanity)
            {
                string drain = "理智−" + skill.SanityDamage;
                if (skill.SanityAdjacent > 0) drain += " · 相邻各−" + skill.SanityAdjacent;
                return skill.Name + " → " + target + "\n" + drain;
            }
            if (skill.Effect == SkillEffect.Heal) return skill.Name + " → " + target + "\n恢复 " + SkillValue(enemy, skill);
            if (skill.Effect == SkillEffect.Protect) return skill.Name + " → " + target;
            if (skill.Effect != SkillEffect.Attack) return skill.Name + " → " + target;
            string text = skill.Name + " → " + target + "\n伤害 " + SkillValue(enemy, skill);
            if (skill.Bleed > 0) text += " · 流血 " + skill.Bleed + "×" + skill.BleedTurns;
            if (skill.Accuracy < 0.9) text += " · 命中 " + (AccuracyPercent(enemy, skill)).ToString("0") + "%";
            return text + " · 磨损部位";
        }

        private double AccuracyPercent(ActorState enemy, SkillSpec skill) { return EffectiveAccuracy(enemy, skill) * 100; }

        private static string TargetRankText(int mask)
        {
            if (mask == 15) return "我方任意一人";
            if (mask == 3) return "我方1～2号位";
            if (mask == 7) return "我方1～3号位";
            return "我方对应站位";
        }

        private string GuardPriorityHint(ActorState tank)
        {
            var others = Enemies.Where(e => e != tank && !e.Dead).ToArray();
            if (others.Length == 0) return "没有可保护的同伴";
            int best = others.Max(GuardPriority);
            var names = others.Where(e => GuardPriority(e) == best).Select(e => e.Name);
            return "目标：" + string.Join(" / ", names);
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
            // The three stage-one dummies are Simple behaviour with no skills and keep their scripted
            // pattern behaviour. A designed monster either carries skills or names a special behaviour
            // (the operator has no attack of its own).
            bool noSkills = enemy.Definition.BasicSkills == null || enemy.Definition.BasicSkills.Length == 0;
            if (noSkills && enemy.Definition.Behavior == EnemyBehavior.Simple)
                ExecuteLegacyEnemy(enemy);
            else
                ExecuteDesignedEnemy(enemy);
            if (!CheckEnd()) Advance();
        }

        // Debug tooling only: ends the current actor's turn without acting. Gameplay never calls this.
        // Skipping draws no random number, spends no skill use and changes no HP or sanity; the turn
        // queue then advances exactly as it would after a normal action.
        public bool SkipCurrentTurn(out string note)
        {
            note = null;
            if (Outcome != BattleOutcome.Active) { note = "战斗已经结束。"; return false; }
            if (Current == null) { note = "当前没有行动者。"; return false; }
            Log(Current.Name + " 跳过本次行动（调试）。", "debug", Current.Id);
            Advance();
            return true;
        }

        // ---------------- legacy pattern enemies ----------------

        private void ExecuteLegacyEnemy(ActorState enemy)
        {
            var target = EnemyTarget(enemy);
            if (target == null) { Finish(BattleOutcome.Defeat); return; }
            ActorState protector = string.IsNullOrEmpty(target.ProtectorId) ? null : Find(target.ProtectorId);
            bool guarded = false;
            if (protector != null && !protector.Dead)
            {
                Log(protector.Name + " 为 " + target.Name + " 拦下攻击。", "guard", protector.Id, target.Id);
                // The guard holds for the whole round now, so ProtectorId is left in place; it is cleared
                // when the protector takes its next turn.
                target = protector;
                guarded = true;
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
                int amount = EnemyDamage(enemy);
                if (guarded) amount = ReduceForGuard(amount, target);
                int dealt = Damage(target, amount, enemy.Id);
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Whisperer)
                { ChangeSanity(target, -14, "受到低语侵蚀"); }
                if (!target.Dead && enemy.Definition.Pattern == EnemyPattern.Hunter)
                { target.BleedDamage = 2; target.BleedTurns = 2; Log(target.Name + " 开始流血，2×2次行动。", "bleed", enemy.Id, target.Id, 0, 0, "流血 2×2"); }
                if (!target.Dead) WearLimb(target, dealt, enemy.Id);
                TryCounter(target, enemy);
            }
        }

        // ---------------- designed enemy AI ----------------

        private void ExecuteDesignedEnemy(ActorState enemy)
        {
            switch (enemy.Definition.Behavior)
            {
                case EnemyBehavior.SteamMachine: MachineTurn(enemy); return;
                case EnemyBehavior.Operator: OperatorTurn(enemy); return;
                case EnemyBehavior.Healer: HealerTurn(enemy); return;
                case EnemyBehavior.Tank: TankTurn(enemy); return;
                case EnemyBehavior.GiantBoss: GiantTurn(enemy); return;
                case EnemyBehavior.CopyBoss: CopyBossTurn(enemy); return;
                case EnemyBehavior.Sanity: SanityTurn(enemy); return;
                default: StandardTurn(enemy); return;
            }
        }

        private void StandardTurn(ActorState enemy)
        {
            var skill = FirstUsableSkill(enemy);
            if (skill == null) { Wait(enemy); return; }
            UseEnemySkill(enemy, skill);
        }

        private void SanityTurn(ActorState enemy)
        {
            var skill = FirstUsableSkill(enemy, SkillEffect.Sanity) ?? FirstUsableSkill(enemy, SkillEffect.Attack);
            if (skill == null) { Wait(enemy); return; }
            UseEnemySkill(enemy, skill);
        }

        private void CopyBossTurn(ActorState enemy)
        {
            var skill = FirstUsableSkill(enemy);
            if (skill == null) { Wait(enemy); return; }
            UseEnemySkill(enemy, skill);
        }

        private void GiantTurn(ActorState enemy)
        {
            var skills = enemy.Definition.BasicSkills;
            if (skills == null || skills.Length == 0) { Wait(enemy); return; }
            // 1 → 2 → 1 → 2 → 3, then repeat.
            int step = enemy.EnemyActionCount % 5;
            int index = step == 0 || step == 2 ? 0 : step == 1 || step == 3 ? 1 : 2;
            if (index >= skills.Length) index = skills.Length - 1;
            UseEnemySkill(enemy, skills[index]);
        }

        private void MachineTurn(ActorState enemy)
        {
            var blast = FirstUsableSkill(enemy);
            if (enemy.BlastRound > 0 && Round >= enemy.BlastRound && blast != null)
            {
                Log(enemy.Name + " 释放积蓄的蒸汽！", "action", enemy.Id);
                var targets = AliveHeroes();
                StrikeEnemyTargets(enemy, blast, targets);
                enemy.Operations = 0;
                enemy.BlastRound = 0;
                enemy.EnemyActionCount++;
                return;
            }
            Wait(enemy);
        }

        private void OperatorTurn(ActorState enemy)
        {
            if (enemy.Rank != 3)
            {
                // Displacing the operator is meant to be real counterplay, but it walks back on its own
                // turns instead of being permanently disabled.
                if (MoveEnemy(enemy, 1))
                {
                    Log(enemy.Name + " 向后退回 " + (enemy.Rank + 1) + " 号位。", "move", enemy.Id);
                    enemy.EnemyActionCount++;
                }
                else Wait(enemy);
                return;
            }
            var machine = Enemies.FirstOrDefault(e => !e.Dead && e.Definition.Behavior == EnemyBehavior.SteamMachine);
            if (machine == null) { Wait(enemy); return; }
            if (machine.BlastRound > 0)
            {
                Log(enemy.Name + " 检查已经蓄能完毕的" + machine.Name + "。", "action", enemy.Id);
                enemy.EnemyActionCount++;
                return;
            }
            machine.Operations++;
            Log(enemy.Name + " 操作" + machine.Name + "（累计 " + machine.Operations + "/2）。", "action", enemy.Id, machine.Id);
            if (machine.Operations >= 2)
            {
                machine.Operations = 2;
                machine.BlastRound = Round + 1;
                Log(machine.Name + " 开始蓄能，下回合发动全体攻击。", "control", machine.Id);
            }
            enemy.EnemyActionCount++;
        }

        private void HealerTurn(ActorState enemy)
        {
            if (enemy.Rank == 0)
            {
                if (MoveEnemy(enemy, 1))
                {
                    Log(enemy.Name + " 后退到 " + (enemy.Rank + 1) + " 号位，放弃本次治疗。", "move", enemy.Id);
                    enemy.EnemyActionCount++;
                }
                else Wait(enemy);
                return;
            }
            var heal = FirstUsableSkill(enemy, SkillEffect.Heal);
            if (heal == null) { Wait(enemy); return; }
            var candidates = Enemies.Where(e => e != enemy && !e.Dead && e.Hp < e.MaxHp).ToArray();
            if (candidates.Length == 0) { Wait(enemy); return; }
            int most = candidates.Max(e => e.MaxHp - e.Hp);
            var tied = candidates.Where(e => e.MaxHp - e.Hp == most).ToArray();
            var target = DrawRandom(tied);
            Log(enemy.Name + " · " + heal.Name, "action", enemy.Id, target.Id);
            ApplySupport(enemy, target, heal);
            enemy.EnemyActionCount++;
        }

        private void TankTurn(ActorState enemy)
        {
            var others = Enemies.Where(e => e != enemy && !e.Dead).ToArray();
            if (others.Length == 0)
            {
                // No one left to protect: attack from the front, otherwise just stand there.
                var solo = enemy.Rank <= 1 ? FirstUsableSkill(enemy, SkillEffect.Attack) : null;
                if (solo == null) { Wait(enemy); return; }
                UseEnemySkill(enemy, solo);
                return;
            }
            bool guard = enemy.Rank >= 2 || enemy.EnemyActionCount % 2 == 0;
            var protect = FirstUsableSkill(enemy, SkillEffect.Protect);
            if (guard && protect != null)
            {
                var target = GuardPriorityTarget(enemy, others);
                if (target != null)
                {
                    Log(enemy.Name + " · " + protect.Name, "action", enemy.Id);
                    ApplySupport(enemy, target, protect);
                    enemy.EnemyActionCount++;
                    return;
                }
            }
            var attack = FirstUsableSkill(enemy, SkillEffect.Attack);
            if (attack == null) { Wait(enemy); return; }
            UseEnemySkill(enemy, attack);
        }

        private ActorState GuardPriorityTarget(ActorState tank, ActorState[] others)
        {
            int best = int.MinValue;
            foreach (var other in others) best = Math.Max(best, GuardPriority(other));
            var tied = others.Where(e => GuardPriority(e) == best).ToArray();
            return tied.Length == 0 ? null : DrawRandom(tied);
        }

        // 回血怪 > 理智怪 > 高伤怪 > 流血怪 > 小怪. Elite versions count as their own category.
        private static int GuardPriority(ActorState actor)
        {
            switch (actor.Definition.Behavior)
            {
                case EnemyBehavior.Healer: return 5;
                case EnemyBehavior.Sanity: return 4;
                case EnemyBehavior.HighDamage: return 3;
                case EnemyBehavior.Bleed: return 2;
                case EnemyBehavior.Simple: return 1;
                default: return 0;
            }
        }

        private SkillSpec FirstUsableSkill(ActorState enemy, SkillEffect? effect = null)
        {
            var skills = enemy.Definition.BasicSkills;
            if (skills == null) return null;
            foreach (var skill in skills)
            {
                if (skill == null) continue;
                if (effect.HasValue && skill.Effect != effect.Value) continue;
                if (!enemy.OccupiesAnyRank(skill.ActorRanks)) continue;
                if (enemy.UsesLeft(skill) <= 0) continue;
                return skill;
            }
            return null;
        }

        private void UseEnemySkill(ActorState enemy, SkillSpec skill)
        {
            if (skill.Effect == SkillEffect.Sanity) { UseSanitySkill(enemy, skill); return; }
            if (skill.Effect == SkillEffect.Heal) { UseEnemyHeal(enemy, skill); return; }
            if (skill.Effect == SkillEffect.Protect)
            {
                var ally = GuardPriorityTarget(enemy, Enemies.Where(e => e != enemy && !e.Dead).ToArray());
                if (ally == null) { Wait(enemy); return; }
                Log(enemy.Name + " · " + skill.Name, "action", enemy.Id);
                ApplySupport(enemy, ally, skill);
                enemy.EnemyActionCount++;
                return;
            }
            if (skill.Effect != SkillEffect.Attack) { ApplySupport(enemy, enemy, skill); enemy.EnemyActionCount++; return; }
            var targets = SelectEnemyTargets(enemy, skill);
            if (targets.Length == 0) { Wait(enemy); return; }
            Log(enemy.Name + " · " + skill.Name, "action", enemy.Id, targets[0].Id);
            StrikeEnemyTargets(enemy, skill, targets);
            enemy.EnemyActionCount++;
        }

        private void UseEnemyHeal(ActorState enemy, SkillSpec skill)
        {
            var candidates = Enemies.Where(e => e != enemy && !e.Dead && e.Hp < e.MaxHp).ToArray();
            if (candidates.Length == 0) { Wait(enemy); return; }
            int most = candidates.Max(e => e.MaxHp - e.Hp);
            var tied = candidates.Where(e => e.MaxHp - e.Hp == most).ToArray();
            var target = DrawRandom(tied);
            Log(enemy.Name + " · " + skill.Name, "action", enemy.Id, target.Id);
            ApplySupport(enemy, target, skill);
            enemy.EnemyActionCount++;
        }

        private void UseSanitySkill(ActorState enemy, SkillSpec skill)
        {
            var pool = Run.Heroes.Where(a => !a.Dead && a.OccupiesAnyRank(skill.TargetRanks)).ToArray();
            if (pool.Length == 0) { Wait(enemy); return; }
            var target = DrawRandom(pool);
            Log(enemy.Name + " · " + skill.Name + " → " + target.Name, "action", enemy.Id, target.Id);
            if (skill.SanityDamage > 0) ChangeSanity(target, -skill.SanityDamage, skill.Name);
            if (skill.SanityAdjacent > 0)
            {
                foreach (var neighbour in Run.Heroes.Where(a => !a.Dead && a != target && Math.Abs(a.Rank - target.Rank) == 1).OrderBy(a => a.Rank).ToArray())
                {
                    ChangeSanity(neighbour, -skill.SanityAdjacent, skill.Name + "波及");
                }
            }
            enemy.EnemyActionCount++;
        }

        // Each target is rolled separately, so a three-target sweep can hit one hero and miss another.
        private void StrikeEnemyTargets(ActorState enemy, SkillSpec skill, ActorState[] targets)
        {
            foreach (var intended in targets)
            {
                if (intended.Dead) continue;
                var target = HeroGuardTarget(intended);
                bool guarded = target != intended;
                if (guarded)
                    Log(target.Name + " 为 " + intended.Name + " 拦下攻击。", "guard", target.Id, intended.Id);
                if (!Roll(EffectiveAccuracy(enemy, skill)))
                {
                    Log(target.Name + " 避开了 " + enemy.Name + " 的" + skill.Name + "。", "miss", enemy.Id, target.Id);
                    continue;
                }
                if (target.Definition.Dodge > 0 && Roll(target.Definition.Dodge))
                {
                    Log(target.Name + " 闪避了 " + enemy.Name + " 的" + skill.Name + "。", "dodge", enemy.Id, target.Id);
                    continue;
                }
                if (ConsumeBlock(target))
                {
                    Log(target.Name + " 的格挡免除了这次攻击。", "guard", enemy.Id, target.Id);
                    TryCounter(target, enemy);
                    continue;
                }
                int amount = Math.Max(0, SkillValue(enemy, skill));
                if (guarded) amount = ReduceForGuard(amount, target);
                int dealt = Damage(target, amount, enemy.Id);
                if (!target.Dead)
                {
                    ApplyOnHit(enemy, target, skill, true);
                    // Both the legacy dummies and every designed monster wear the same limb track. The
                    // loss is read from the actual post-mitigation damage, so a miss, dodge or block
                    // above never reaches here and spends no draw.
                    WearLimb(target, dealt, enemy.Id);
                }
                TryCounter(target, enemy);
            }
        }

        private ActorState HeroGuardTarget(ActorState target)
        {
            if (string.IsNullOrEmpty(target.ProtectorId)) return target;
            var protector = Run.Heroes.FirstOrDefault(h => h.Id == target.ProtectorId && !h.Dead);
            return protector == null || protector == target ? target : protector;
        }

        private ActorState[] AliveHeroes()
        {
            return Run.Heroes.Where(a => !a.Dead).OrderBy(a => a.Rank).ToArray();
        }

        private ActorState[] SelectEnemyTargets(ActorState enemy, SkillSpec skill)
        {
            var pool = Run.Heroes.Where(a => !a.Dead && a.OccupiesAnyRank(skill.TargetRanks)).ToArray();
            if (pool.Length == 0) return new ActorState[0];
            int count = skill.TargetCount > 0 ? Math.Min(skill.TargetCount, pool.Length) : 1;
            // Single-target rules (bleed preference) only ever apply to a one-target skill.
            if (count == 1 && skill.TargetRule == EnemyTargetRule.MostBleed)
            {
                int most = pool.Max(Stacks);
                var tied = pool.Where(a => Stacks(a) == most).ToArray();
                return new[] { DrawRandom(tied) };
            }
            if (count == 1 && skill.TargetRule == EnemyTargetRule.LowestHp)
            {
                double lowest = pool.Min(a => (double)a.Hp / Math.Max(1, a.MaxHp));
                var tied = pool.Where(a => (double)a.Hp / Math.Max(1, a.MaxHp) == lowest).ToArray();
                return new[] { DrawRandom(tied) };
            }
            if (count >= pool.Length) return pool.OrderBy(a => a.Rank).ToArray();
            // Draw without replacement: each removal keeps the remaining picks distinct.
            var remaining = pool.ToList();
            var picked = new List<ActorState>();
            while (picked.Count < count && remaining.Count > 0)
            {
                var pick = DrawRandom(remaining.ToArray());
                picked.Add(pick);
                remaining.Remove(pick);
            }
            return picked.ToArray();
        }

        private static int Stacks(ActorState actor)
        {
            return actor.BleedTurns > 0 ? actor.BleedDamage : 0;
        }

        private void Wait(ActorState enemy)
        {
            Log(enemy.Name + " 待机。", "control", null, enemy.Id);
        }

        // ---------------- boss round hooks ----------------

        private void RunRoundStartHooks()
        {
            foreach (var boss in Enemies.Where(e => !e.Dead && e.Definition.Behavior == EnemyBehavior.CopyBoss).ToArray())
                SummonCopies(boss);
            RespawnOperator();
            RunAccessoryRoundStartHooks();
        }

        // Round-scoped accessory effects. The heart is judged before the turn queue is built, so a failed
        // heart already has its extra action removed on the round the failure happens.
        private void RunAccessoryRoundStartHooks()
        {
            foreach (var hero in Run.Heroes.Where(h => h != null && !h.Dead).ToArray())
            {
                var state = hero.AccessoryBattle;
                if (state == null) continue;
                if (state.SleepTurns > 0 && !hero.Dead)
                {
                    state.SleepTurns--;
                    Log(hero.Name + " 仍被困在假寐中，理智 -" + AccessorySystem.SleepSanityDrain.ToString("0.#") + "。", "sanity", hero.Id);
                    ChangeSanity(hero, -AccessorySystem.SleepSanityDrain, "假寐药剂的余波");
                }
                if (AccessorySystem.Has(hero, AccessoryKind.Heart) && state.HeartActive
                    && !state.HeartResolved && Round >= 3)
                {
                    state.HeartActive = false;
                    state.HeartResolved = true;
                    state.HeartSuccess = false;
                    Log(hero.Name + " 的跃动心脏未能在两回合内杀死标记目标，攻击大幅衰减。", "system", hero.Id);
                }
            }
        }

        private void SummonCopies(ActorState boss)
        {
            if (Round < 2 || Run.Heroes.Count == 0) return;
            int alive = Enemies.Count(e => e.IsCopy && e.SummonedBy == boss.Id && !e.Dead);
            if (alive >= 3) return;
            foreach (int rank in new[] { 1, 2, 3 })
            {
                if (alive >= 3) break;
                if (Enemies.Any(e => !e.Dead && e.OccupiesRank(rank))) continue;
                SpawnCopy(boss, rank);
                alive++;
            }
        }

        private void SpawnCopy(ActorState boss, int rank)
        {
            var hero = Run.Heroes[Math.Min(Run.Heroes.Count - 1, (int)(Run.Random.NextUnit() * Run.Heroes.Count))];
            var source = hero.Definition;
            var spec = CopySpec(source);
            spec.Id = "copy_" + source.Id;
            spec.Name = source.Name + "复制体";
            spec.Subtitle = "复制自" + source.Name;
            spec.Silhouette = source.Silhouette;
            // The copied maximum is half of the party member's actual maximum, and the copy arrives full.
            spec.MaxHp = Math.Max(1, source.MaxHp / 2);
            spec.Attack = source.Attack;
            spec.Accuracy = source.Accuracy;
            spec.CritChance = 0;
            spec.Speed = source.Speed;
            spec.Behavior = EnemyBehavior.Simple;
            spec.RankWidth = 1;
            spec.Immobile = false;
            spec.ActionInterval = 1;
            spec.BossHealOnHeroDeath = 0;
            spec.BasicSkills = new[] { CopyStrike };
            var actor = new ActorState
            {
                Id = "enemy_copy_" + (++copySerial), Definition = spec, Player = false, Rank = rank,
                Hp = spec.MaxHp, Sanity = 0, IsCopy = true, SummonedBy = boss.Id
            };
            Enemies.Add(actor);
            Log(boss.Name + " 复制了" + source.Name + "，召唤复制体（位置 " + (rank + 1) + "，生命 " + spec.MaxHp + "）。", "summon", boss.Id, actor.Id);
        }

        private void RespawnOperator()
        {
            if (Round < 2) return;
            var machine = Enemies.FirstOrDefault(e => !e.Dead && e.Definition.Behavior == EnemyBehavior.SteamMachine);
            if (machine == null) return;
            if (Enemies.Any(e => !e.Dead && e.Definition.Behavior == EnemyBehavior.Operator)) return;
            var fallen = Enemies.FirstOrDefault(e => e.Dead && e.Definition.Behavior == EnemyBehavior.Operator
                && e.NextSpawnRound > 0 && Round >= e.NextSpawnRound);
            if (fallen == null) return;
            int rank = -1;
            for (int candidate = 3; candidate >= 0; candidate--)
                if (!Enemies.Any(e => !e.Dead && e.OccupiesRank(candidate))) { rank = candidate; break; }
            if (rank < 0) return;
            var spec = CopySpec(fallen.Definition);
            var actor = new ActorState
            {
                Id = "enemy_operator_" + (++operatorSerial), Definition = spec, Player = false,
                Rank = rank, Hp = spec.MaxHp, Sanity = 0
            };
            Enemies.Add(actor);
            // The old corpse must not keep qualifying for a respawn on every later round.
            fallen.NextSpawnRound = int.MaxValue;
            Log(spec.Name + " 被重新召唤到 " + (rank + 1) + " 号位。", "summon", null, actor.Id);
        }

        private int copySerial;
        private int operatorSerial;
        private static readonly SkillSpec CopyStrike = new SkillSpec
        {
            Id = "copy_strike", Name = "复制体攻击", Description = "复制体沿用原角色的基础攻击。",
            Effect = SkillEffect.Attack, Target = TargetSide.Enemy, Multiplier = 1.0,
            ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9
        };

        private ActorSpec ResolveSummoner(ActorSpec spec)
        {
            if (spec == null) return null;
            if (spec.Behavior != EnemyBehavior.RandomSummoner || spec.SummonPool == null || spec.SummonPool.Length == 0)
                return spec;
            int index = Math.Min(spec.SummonPool.Length - 1, (int)(Run.Random.NextUnit() * spec.SummonPool.Length));
            var copy = CopySpec(spec.SummonPool[index]);
            // The transformed summoner keeps the marker's action cadence and starting slot.
            copy.ActionInterval = Math.Max(1, spec.ActionInterval);
            copy.ActionPhase = spec.ActionPhase;
            copy.StartRank = spec.StartRank;
            return copy;
        }

        private ActorSpec CopySpec(ActorSpec source)
        {
            return new ActorSpec
            {
                Id = source.Id, Name = source.Name, Subtitle = source.Subtitle, Silhouette = source.Silhouette,
                MaxHp = source.MaxHp, Speed = source.Speed, SpeedMin = source.SpeedMin, SpeedMax = source.SpeedMax,
                StartRank = source.StartRank,
                Attack = source.Attack, Accuracy = source.Accuracy, CritChance = source.CritChance,
                Dodge = source.Dodge, Defense = source.Defense, SanityOffset = source.SanityOffset,
                Pattern = source.Pattern, Behavior = source.Behavior, RankWidth = source.RankWidth,
                Immobile = source.Immobile, ActionInterval = source.ActionInterval, ActionPhase = source.ActionPhase,
                BossHealOnHeroDeath = source.BossHealOnHeroDeath, GuardReduction = source.GuardReduction,
                SummonPool = source.SummonPool,
                BasicSkills = source.BasicSkills, PrimalSkills = source.PrimalSkills, TechnicalSkills = source.TechnicalSkills
            };
        }

        // Survivors behind a fallen enemy take one step toward the front. Processing front-to-back
        // keeps two survivors from claiming the same destination, and immobile or multi-rank units
        // keep their ground so the giant boss and the steam machine are not dragged around by adds.
        // Dead actors are deliberately not consulted for occupancy: they are corpses, not obstacles.
        private void ResolveEnemyRanksAfterDeaths()
        {
            if (deathRanks.Count == 0) return;
            // Process from the back so a survivor can fill every rank vacated in the same action.
            // Front-to-back would move it into the first hole and then stop counting it as "behind".
            foreach (int deathRank in deathRanks.OrderByDescending(r => r))
            {
                var behind = Enemies
                    .Where(e => !e.Dead && e.Rank > deathRank)
                    .OrderBy(e => e.Rank).ThenBy(e => e.Id, StringComparer.Ordinal)
                    .ToArray();
                foreach (var enemy in behind)
                {
                    if (enemy.Definition.Immobile || enemy.Width != 1) continue;
                    int destination = enemy.Rank - 1;
                    if (destination < 0) continue;
                    bool blocked = Enemies.Any(e => e != enemy && !e.Dead
                        && e.Rank < destination + enemy.Width && destination < e.Rank + e.Width);
                    if (blocked) continue;
                    enemy.Rank = destination;
                    Log(enemy.Name + " 向前移动到 " + (destination + 1) + " 号位。", "move", enemy.Id);
                }
            }
            deathRanks.Clear();
        }

        private bool MoveEnemy(ActorState enemy, int delta)
        {
            int destination = enemy.Rank + delta;
            if (destination < 0 || destination + enemy.Width > 4) return false;
            var occupant = Enemies.FirstOrDefault(e => e != enemy && !e.Dead
                && e.Rank < destination + enemy.Width && destination < e.Rank + e.Width);
            if (occupant != null)
            {
                if (occupant.Definition.Immobile || occupant.Width != 1 || enemy.Width != 1) return false;
                occupant.Rank = enemy.Rank;
            }
            enemy.Rank = destination;
            return true;
        }

        // Limb damage is a second track next to HP. Every attack that actually lands removes a share of
        // the real HP damage from one random intact part: light minions chip away, elites and bosses can
        // sever a part in a couple of hits. All guards below sit before the draw, so a miss, a block, a
        // locked limb track or a fully severed body never consumes a random number.
        private void WearLimb(ActorState target, int dealt, string sourceId)
        {
            if (!target.Player || target.Dead || dealt <= 0) return;
            // 金丝苹果 locks the limb track too. Returning here also keeps the part draw untouched.
            if (AccessorySystem.LimbLock(target)) return;
            if (target.Limbs == null || !target.Limbs.Any(l => l != null && l.Hp > 0)) return;
            var part = AccessorySystem.RandomPart(Run.Random, target, true);
            var limb = LimbSystem.Get(target, part);
            if (limb == null || limb.Hp <= 0) return;
            int loss = Math.Max(1,
                (int)Math.Ceiling(dealt * LimbSystem.EffectiveDamageRatio(Rules.Limbs) - 1e-9));
            var conditionBefore = LimbSystem.ConditionOf(limb);
            bool severed = LimbSystem.Damage(target, part, loss);
            // The cut-in only announces the two thresholds the player can see on the limb track: a part
            // turning injured at half health, and a part reaching zero. Every other hit stays in the log.
            bool milestone = severed || LimbSystem.ConditionOf(limb) != conditionBefore;
            Log(target.Name + " 的" + LimbSystem.PartName(part) + "受到 " + loss + " 点部位伤害"
                + (severed ? "，已经断肢！" : milestone ? "，陷入受伤。" : "。"), "limb", sourceId, target.Id,
                loss, severed ? 1 : 0, LimbSystem.PartName(part), milestone);
        }

        public void ChangeSanity(ActorState actor, double amount, string reason)
        {
            if (actor == null || actor.Dead) return;
            // 理智锁定药剂: the whole battle, in both directions. Returning before the zero check also keeps
            // the golden apple's direct-death roll out of a locked hero.
            if (actor.SanityLocked)
            {
                Log(actor.Name + " 的理智已被锁定，无法变化。", "sanity", actor.Id, actor.Id, 0, 0, "locked");
                return;
            }
            double before = actor.Sanity;
            actor.Sanity = SanityProbability.Clamp(before + amount, 0, 100);
            Log(actor.Name + " " + reason + "：理智 " + before.ToString("0.#") + " → " + actor.Sanity.ToString("0.#") + "。",
                "sanity", actor.Id, actor.Id, 0, actor.Sanity - before);
            // 金丝苹果: crossing to zero triggers one direct-death check, not one per further drain at zero.
            if (before > 0 && actor.Sanity <= 0 && actor.Player && !actor.Dead
                && AccessorySystem.Has(actor, AccessoryKind.GoldenApple))
            {
                if (Roll(Rules.DeathChance))
                {
                    Log(actor.Name + " 的理智归零，金丝苹果停止了心跳。", "death", null, actor.Id);
                    Kill(actor);
                }
                else Log(actor.Name + " 的理智归零，金丝苹果暂时没有夺走生命。", "sanity", actor.Id);
            }
        }

        // A direct loss skips defence and the round-scoped vulnerability, but still goes through the
        // accessories that care about HP reaching zero. selfInflicted only excludes the rust medal's own
        // entry cost from its damage accumulation.
        // Returns the HP damage actually applied after defence, guard and vulnerability. Callers that
        // layer a second effect on the same hit (limb wear) use this number instead of the raw amount.
        public int Damage(ActorState actor, int amount, string sourceId = null, bool bleeding = false,
            bool direct = false, bool selfInflicted = false)
        {
            if (actor == null || actor.Dead || amount <= 0) return 0;
            int actual = amount;
            if (!bleeding && !direct)
            {
                double reduced = amount * (1 - EffectiveDefense(actor));
                if (actor.Defending) reduced *= 0.5;
                if (actor.SelfVulnerable > 0) reduced *= 1 + actor.SelfVulnerable;
                reduced *= AccessorySystem.DamageTakenFactor(actor);
                // Prosthetic and accessory factors are separate multipliers, while prosthetic shares add
                // up among themselves inside DamageTakenFactor.
                reduced *= ProstheticSystem.DamageTakenFactor(actor);
                actual = Math.Max(1, (int)Math.Ceiling(reduced - 1e-9));
            }
            else if (bleeding)
            {
                // Bleed bypasses defence and accessories, but Double Pump deliberately doubles it.
                actual = Math.Max(1, (int)Math.Ceiling(amount * ProstheticSystem.BleedTakenFactor(actor) - 1e-9));
            }
            bool alreadyDying = actor.DeathDoor;

            // 假寐的药剂: one lethal hit per battle is swallowed whole.
            if (!bleeding && !direct && actor.Player && !alreadyDying
                && AccessorySystem.Has(actor, AccessoryKind.SleepPotion)
                && actor.AccessoryBattle != null && !actor.AccessoryBattle.SleepTriggered
                && actor.Hp - actual <= 0)
            {
                actor.AccessoryBattle.SleepTriggered = true;
                actor.AccessoryBattle.SleepTurns = 2;
                Log(actor.Name + " 的假寐药剂吞掉了这次致命伤害。", "system", actor.Id);
                if (!AccessorySystem.LimbLock(actor))
                {
                    var part = AccessorySystem.RandomPart(Run.Random, actor);
                    int hit = LimbSystem.HitAmount(actor, part, Rules.Limbs);
                    var limb = LimbSystem.Get(actor, part);
                    var conditionBefore = limb == null ? LimbCondition.Intact : LimbSystem.ConditionOf(limb);
                    bool severed = LimbSystem.Damage(actor, part, hit);
                    bool milestone = severed
                        || (limb != null && LimbSystem.ConditionOf(limb) != conditionBefore);
                    Log(actor.Name + " 的" + LimbSystem.PartName(part) + "受到 " + hit + " 点部位伤害"
                        + (severed ? "，已经断肢！" : milestone ? "，陷入受伤。" : "。"), "limb", null, actor.Id,
                        hit, severed ? 1 : 0, LimbSystem.PartName(part), milestone);
                }
                ChangeSanity(actor, -AccessorySystem.SleepSanityDrain, "假寐药剂的代价");
                // The potion already spent a limb on its own; returning zero keeps the attack site from
                // wearing a second part for the same hit.
                return 0;
            }

            // 殉道锁链: at zero HP, spend one intact limb instead of entering the death check.
            if (!direct && actor.Player && !alreadyDying && actor.Hp > 0 && actor.Hp - actual <= 0
                && AccessorySystem.Has(actor, AccessoryKind.MartyrChain))
            {
                int intact = actor.Limbs == null ? 0 : actor.Limbs.Count(l => l != null && l.Hp > 0);
                if (intact == 0)
                {
                    Log(actor.Name + " 的四肢已经全部断去，殉道锁链再也付不出代价。", "death", null, actor.Id);
                    Kill(actor, sourceId);
                    return 0;
                }
                var part = AccessorySystem.RandomPart(Run.Random, actor, true);
                var limb = LimbSystem.Get(actor, part);
                if (limb != null) LimbSystem.Damage(actor, part, limb.Hp);
                actor.Hp = Math.Max(1, (int)Math.Ceiling(actor.MaxHp * AccessorySystem.MartyrRestore - 1e-9));
                Log(actor.Name + " 的殉道锁链消耗" + LimbSystem.PartName(part) + "，恢复 " + actor.Hp + " 点生命。",
                    "heal", null, actor.Id, actor.Hp);
                ChangeSanity(actor, -AccessorySystem.MartyrSanityLoss, "殉道锁链");
                // The chain already consumed a limb instead of the HP loss.
                return 0;
            }

            // 金丝苹果: the HP track is locked at one, so a hit can never take the last point.
            if (actor.Player && AccessorySystem.HpLock(actor) && actor.Hp - actual <= 0)
                actual = Math.Min(actual, Math.Max(0, actor.Hp - 1));

            actor.Hp = Math.Max(0, actor.Hp - actual);
            Log(actor.Name + " 受到 " + actual + (bleeding ? " 点流血伤害。" : " 点伤害。"), "damage", sourceId, actor.Id, actual);

            // 金丝苹果 also drains sanity on every attack that lands on the bearer.
            if (!bleeding && !direct && actor.Player && AccessorySystem.Has(actor, AccessoryKind.GoldenApple))
            {
                ChangeSanity(actor, -AccessorySystem.AppleSanityPerHit, "金丝苹果");
                if (actor.Dead) return actual;
            }

            if (!actor.Player && actor.Hp == 0) Kill(actor, sourceId);
            else if (alreadyDying)
            {
                bool dies = SanityProbability.Check(Rules.DeathChance, -0.35, actor.Sanity, Run.Random, Rules.ProbabilityStrength);
                if (dies) Kill(actor, sourceId); else Log(actor.Name + " 挺过了死亡检定，仍处于濒死。", "deathdoor", null, actor.Id);
            }
            else if (actor.DeathDoor) Log(actor.Name + " 陷入濒死！再次受伤将进行死亡检定。", "deathdoor", null, actor.Id);

            if (actor.Player && !actor.Dead && !direct && !selfInflicted)
                AccumulateRustDamage(actor, actual);
            return actual;
        }

        // 锈血勋章 counts real incoming damage. Every full threshold buys one more 「带伤强攻」 use for this
        // battle only; the shared SkillSpec.Uses is never touched.
        private void AccumulateRustDamage(ActorState actor, int actual)
        {
            if (actual <= 0 || !AccessorySystem.Has(actor, AccessoryKind.RustMedal)) return;
            var state = actor.AccessoryBattle;
            if (state == null) return;
            state.RustDamageAccum += actual;
            int threshold = Math.Max(1, (int)Math.Ceiling(actor.MaxHp * AccessorySystem.RustThreshold - 1e-9));
            while (state.RustDamageAccum >= threshold)
            {
                state.RustDamageAccum -= threshold;
                if (AccessorySystem.AddSkillUse(actor, AccessorySystem.RustSkillId, 1))
                    Log(actor.Name + " 的锈血勋章让「带伤强攻」多出 1 次使用。", "system", actor.Id);
            }
        }

        private void Kill(ActorState actor, string sourceId = null)
        {
            if (actor == null || actor.Dead) return;
            actor.Dead = true;
            // The rank is resolved after the current action finishes, not here: an area attack may
            // still be iterating over its captured target list and moving the survivors mid-swing
            // would make rank-dependent riders see a different formation.
            if (!actor.Player) deathRanks.Add(actor.Rank);
            Log(actor.Name + (actor.Player ? " 死亡，本局无法恢复。" : " 被击败。"), "death", null, actor.Id);
            if (!actor.Player)
            {
                // 成瘾物 ends on a kill made by its bearer.
                var killer = Run.Heroes.FirstOrDefault(h => h != null && h.Id == sourceId);
                if (killer != null && killer.AccessoryBattle != null && killer.AccessoryBattle.AddictionActive
                    && AccessorySystem.Has(killer, AccessoryKind.Addiction))
                {
                    killer.AccessoryBattle.AddictionActive = false;
                    killer.AccessoryBattle.AddictionStacks = 0;
                    Log(killer.Name + " 的成瘾物在击杀后失效，叠加的易伤一并清空。", "system", killer.Id);
                }
                // 跃动心脏 succeeds only when the marked target dies within the first two rounds.
                foreach (var hero in Run.Heroes.Where(h => h != null && h.AccessoryBattle != null
                    && h.AccessoryBattle.HeartActive && !h.AccessoryBattle.HeartResolved
                    && h.AccessoryBattle.HeartTargetId == actor.Id).ToArray())
                {
                    if (Round > 2) continue;
                    hero.AccessoryBattle.HeartSuccess = true;
                    hero.AccessoryBattle.HeartResolved = true;
                    Log(hero.Name + " 的跃动心脏在时限内杀死了标记目标，增益持续整场。", "system", hero.Id);
                }
            }
            if (!actor.Player && actor.Definition.Behavior == EnemyBehavior.Operator)
                actor.NextSpawnRound = Round + 1;
            if (actor.Player) HealBossesOnHeroDeath();
        }

        // The giant boss's passive: every party death hands it health back. The heal is capped at its
        // maximum and never revives anything.
        private void HealBossesOnHeroDeath()
        {
            foreach (var boss in Enemies.Where(e => !e.Dead && e.Definition.BossHealOnHeroDeath > 0).ToArray())
            {
                int restored = Math.Min(boss.Definition.BossHealOnHeroDeath, boss.MaxHp - boss.Hp);
                if (restored <= 0) continue;
                boss.Hp += restored;
                Log(boss.Name + " 吸收死亡，恢复 " + restored + " 点生命。", "heal", null, boss.Id, restored);
            }
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
            // The lock is defined as "this battle only". Both layers go away here, including a pending
            // lock that never got to mature and the debug settle path.
            foreach (var hero in Run.Heroes)
                if (hero != null) { hero.SanityLocked = false; hero.SanityLockPending = false; }
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

        private void Log(string message, string kind, string actorId = null, string targetId = null,
            int amount = 0, double value = 0, string detail = null, bool milestone = false)
        {
            Events.Add(new BattleEvent
            {
                Message = message,
                Kind = kind,
                ActorId = actorId,
                TargetId = targetId,
                Amount = amount,
                Value = value,
                Detail = detail,
                Milestone = milestone
            });
        }
    }
}
