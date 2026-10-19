using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    // The non-MonoBehaviour state machine behind the Battle Lab. It owns one RunState and at most one live
    // BattleEngine and exposes only the operations the console needs. UI code never touches the engine
    // directly, so a future headless smoke test can drive the same object.
    public sealed class BattleLabSession
    {
        public GameCatalog Catalog { get; }
        public BattleLabConfig Config { get; }
        public RunState Run { get; private set; }
        public BattleEngine Battle { get; private set; }

        public readonly List<BattleLabLogEntry> Log = new List<BattleLabLogEntry>();
        public readonly List<string> LastErrors = new List<string>();
        public readonly List<BattleLabReplayStep> RecordedSteps = new List<BattleLabReplayStep>();
        public readonly Dictionary<string, uint> RandomSnapshots = new Dictionary<string, uint>();

        public string LastNote { get; private set; }
        public bool LastNoteIsError { get; private set; }
        public bool Recording { get; private set; } = true;
        public bool RecordingInvalid { get; private set; }
        public string RecordedConfigJson { get; private set; }
        public int MaxSteps = 2000;

        private int logCursor;

        public BattleLabSession(GameCatalog catalog, BattleLabConfig config)
        {
            Catalog = catalog;
            Config = config;
            if (Config != null) Config.Normalize();
            LastNote = "尚未开始。";
        }

        public bool HasRun { get { return Run != null; } }
        public bool HasBattle { get { return Battle != null; } }
        public bool Active { get { return Battle != null && Battle.Outcome == BattleOutcome.Active; } }
        public ActorState Current { get { return Battle == null ? null : Battle.Current; } }
        public IEnumerable<ActorState> Heroes { get { return Run == null || Run.Heroes == null ? Enumerable.Empty<ActorState>() : Run.Heroes; } }
        public IEnumerable<ActorState> Enemies { get { return Battle == null ? Enumerable.Empty<ActorState>() : Battle.Enemies; } }
        public IEnumerable<ActorState> AllActors { get { return Battle == null ? Heroes : Battle.All; } }
        public uint RandomState { get { return Run == null || Run.Random == null ? 0u : Run.Random.State; } }
        public bool RandomOverrideActive { get { return Run != null && Run.Random != null && Run.Random.DebugOverrideActive; } }
        public int RandomForcedDrawsRemaining { get { return Run == null || Run.Random == null ? 0 : Run.Random.DebugForcedDrawsRemaining; } }

        // ---- run and battle lifecycle ----

        public bool NewRun(out string error)
        {
            error = null;
            LastErrors.Clear();
            if (!BattleLabFactory.TryBuildRun(Config, Catalog, out RunState run, out List<string> errors))
            {
                LastErrors.AddRange(errors);
                error = string.Join("\n", errors);
                Run = null;
                Battle = null;
                SetNote(error, true);
                return false;
            }
            Run = run;
            Battle = null;
            Log.Clear();
            logCursor = 0;
            ResetRecording();
            SetNote("已创建 Run，种子 " + Config.SeedText + "。");
            return true;
        }

        public bool StartBattle(out string error)
        {
            if (!NewRun(out error)) return false;
            return StartBattleWithCurrentRun(out error);
        }

        // Builds a new BattleEngine over the existing Run. Reusing a run keeps HP, spent skill uses and the
        // random sequence, which is how the "再开一场" button tests multi-battle behaviour.
        public bool StartBattleWithCurrentRun(out string error)
        {
            error = null;
            if (Run == null)
            {
                error = "还没有 Run，请先新建 Run。";
                SetNote(error, true);
                return false;
            }
            var enemies = BattleLabFactory.BuildEnemySpecs(Config, out List<string> errors);
            if (errors.Count > 0)
            {
                LastErrors.Clear();
                LastErrors.AddRange(errors);
                error = string.Join("\n", errors);
                SetNote(error, true);
                return false;
            }
            try
            {
                var rules = BattleLabFactory.ResolveRules(Config, Catalog);
                Battle = new BattleEngine(Run, enemies.ToArray(), rules);
            }
            catch (Exception e)
            {
                Battle = null;
                error = "开战失败：" + e.GetType().Name + " " + e.Message;
                LastErrors.Clear();
                LastErrors.Add(error);
                SetNote(error, true);
                return false;
            }
            Log.Clear();
            logCursor = 0;
            ResetRecording();
            SyncLog();
            SetNote("战斗开始：第 " + Run.Battles + " 场，种子 " + Config.SeedText + "。");
            return true;
        }

        public BattleSettlement Finish(BattleOutcome outcome)
        {
            if (Battle == null) { SetNote("没有战斗。", true); return null; }
            if (outcome == BattleOutcome.Active) { SetNote("不能用 Active 结算。", true); return null; }
            try
            {
                var settlement = Battle.Finish(outcome);
                SyncLog();
                SetNote("战斗结算：" + outcome + "。");
                return settlement;
            }
            catch (Exception e)
            {
                SetNote("结算失败：" + e.Message, true);
                return null;
            }
        }

        public void Restart()
        {
            if (!StartBattle(out string error)) SetNote(error, true);
        }

        public void RestartWithCurrentRun()
        {
            if (!StartBattleWithCurrentRun(out string error)) SetNote(error, true);
        }

        // ---- actions ----

        public bool CanAct(out string error)
        {
            error = null;
            if (Battle == null) { error = "还没有开始战斗。"; return false; }
            if (Battle.Outcome != BattleOutcome.Active) { error = "战斗已经结束。"; return false; }
            if (Battle.Current == null) { error = "当前没有行动者。"; return false; }
            return true;
        }

        public bool ExecuteSkill(int skillIndex, string targetId, out string error)
        {
            if (!CanAct(out error)) { SetNote(error, true); return false; }
            uint before = RandomState;
            bool ok = Battle.Execute(new ActionRequest(ActionKind.Skill, targetId, skillIndex), out error);
            if (ok)
            {
                Record("skill", skillIndex, targetId, -1, before);
                SetNote("技能执行完成。");
            }
            else SetNote(error ?? "技能执行失败。", true);
            SyncLog();
            return ok;
        }

        public bool ExecuteSwap(string targetId, out string error)
        {
            if (!CanAct(out error)) { SetNote(error, true); return false; }
            uint before = RandomState;
            bool ok = Battle.Execute(new ActionRequest(ActionKind.Swap, targetId, 0), out error);
            if (ok)
            {
                Record("swap", 0, targetId, -1, before);
                SetNote("换位完成。");
            }
            else SetNote(error ?? "换位失败。", true);
            SyncLog();
            return ok;
        }

        public bool UseConsumable(int stockIndex, string targetId, out string note)
        {
            if (!CanAct(out string error)) { note = error; SetNote(note, true); return false; }
            uint before = RandomState;
            bool ok = Battle.UseConsumable(stockIndex, targetId, out note);
            if (ok)
            {
                Record("consumable", -1, targetId, stockIndex, before);
                SetNote(note);
            }
            else SetNote(note ?? "道具使用失败。", true);
            SyncLog();
            return ok;
        }

        public bool ExecuteEnemy(out string note)
        {
            note = null;
            if (!CanAct(out string error)) { note = error; SetNote(note, true); return false; }
            if (Battle.Current.Player) { note = "现在是我方行动。"; SetNote(note, true); return false; }
            uint before = RandomState;
            try
            {
                Battle.ExecuteEnemy();
            }
            catch (Exception e)
            {
                note = "敌方行动异常：" + e.GetType().Name + " " + e.Message;
                SetNote(note, true);
                SyncLog();
                return false;
            }
            Record("enemy", -1, null, -1, before);
            note = "敌方行动完成。";
            SetNote(note);
            SyncLog();
            return true;
        }

        public bool SkipTurn(out string note)
        {
            if (!CanAct(out string error)) { note = error; SetNote(note, true); return false; }
            uint before = RandomState;
            bool ok = Battle.SkipCurrentTurn(out note);
            if (ok)
            {
                Record("skip", -1, null, -1, before);
                note = note ?? "跳过当前行动。";
                SetNote(note);
            }
            else SetNote(note ?? "跳过失败。", true);
            SyncLog();
            return ok;
        }

        // One actor's action. The player side picks its first legal skill and first legal target, then falls
        // back to a swap and finally to a skip. This is the deterministic driver used by fuzz and replay
        // diagnostics; the battle page still lets a human choose exactly what to do.
        public bool Step(out string note)
        {
            note = null;
            if (!CanAct(out string error)) { note = error; SetNote(note, true); return false; }
            var current = Battle.Current;
            if (!current.Player)
                return ExecuteEnemy(out note);

            var skills = Battle.SkillsFor(current);
            for (int i = 0; i < skills.Length; i++)
            {
                if (Battle.SkillDisabledReason(i) != null) continue;
                var skill = skills[i];
                string target = null;
                if (skill.Targeting == SkillTargeting.Choose)
                {
                    var first = Battle.Targets(i).FirstOrDefault();
                    if (first == null) continue;
                    target = first.Id;
                }
                if (ExecuteSkill(i, target, out note)) return true;
            }

            var swapTarget = Battle.SwapTargets().FirstOrDefault();
            if (swapTarget != null && ExecuteSwap(swapTarget.Id, out note)) return true;

            return SkipTurn(out note);
        }

        public int RunUntilPlayerTurn(out string note, int maxSteps = 0)
        {
            if (maxSteps <= 0) maxSteps = MaxSteps;
            note = null;
            int steps = 0;
            while (Battle != null && Battle.Outcome == BattleOutcome.Active
                && Battle.Current != null && !Battle.Current.Player && steps < maxSteps)
            {
                if (!Step(out note)) break;
                steps++;
            }
            if (steps >= maxSteps) note = "达到最大步数 " + maxSteps + "，可能仍在敌方行动。";
            else if (Battle == null) note = "没有战斗。";
            else if (Battle.Outcome != BattleOutcome.Active) note = "战斗已经结束。";
            else note = "已到达玩家回合。";
            SetNote(note);
            return steps;
        }

        public int RunToEnd(out string note, int maxSteps = 0)
        {
            if (maxSteps <= 0) maxSteps = MaxSteps;
            note = null;
            int steps = 0;
            while (Battle != null && Battle.Outcome == BattleOutcome.Active && steps < maxSteps)
            {
                if (!Step(out note)) break;
                steps++;
            }
            if (steps >= maxSteps) note = "达到最大步数 " + maxSteps + "，战斗仍未结束。";
            else if (Battle == null) note = "没有战斗。";
            else note = "战斗结束：" + Battle.Outcome + "，共 " + steps + " 步。";
            SetNote(note);
            return steps;
        }

        // ---- in-battle editing ----

        public bool SetHp(ActorState actor, int hp, out string note)
        {
            if (actor == null || actor.Definition == null) { note = "没有选中角色。"; return false; }
            actor.Hp = Math.Max(0, Math.Min(actor.Definition.MaxHp, hp));
            if (!actor.Player && actor.Hp == 0) actor.Dead = true;
            MarkMutation();
            CheckEndAfterMutation();
            SyncLog();
            note = actor.Name + " 的 HP → " + actor.Hp;
            SetNote(note);
            return true;
        }

        public bool SetMaxHp(ActorState actor, int maxHp, out string note)
        {
            if (actor == null || actor.Definition == null) { note = "没有选中角色。"; return false; }
            actor.Definition.MaxHp = Math.Max(1, maxHp);
            if (actor.Hp > actor.Definition.MaxHp) actor.Hp = actor.Definition.MaxHp;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的 MaxHp → " + actor.Definition.MaxHp + "（部位上限未自动重建）";
            SetNote(note);
            return true;
        }

        public bool SetSanity(ActorState actor, double sanity, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.Sanity = SanityProbability.Clamp(sanity, 0, 100);
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的理智 → " + actor.Sanity.ToString("0.#") + "（直接赋值，不触发金丝苹果死亡检定）";
            SetNote(note);
            return true;
        }

        public bool ChangeSanity(ActorState actor, double amount, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            if (Battle == null) { note = "没有战斗。"; return false; }
            Battle.ChangeSanity(actor, amount, "调试");
            MarkMutation();
            CheckEndAfterMutation();
            SyncLog();
            note = actor.Name + " 的理智变化 " + amount.ToString("+0.#;-0.#;0") + " → " + actor.Sanity.ToString("0.#");
            SetNote(note);
            return true;
        }

        public bool SetForm(ActorState actor, SkillForm form, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.Form = form;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的形态 → " + form + "（下个玩家回合会被重新抽取）";
            SetNote(note);
            return true;
        }

        public bool SetRank(ActorState actor, int rank, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            int clamped = Math.Max(0, Math.Min(3, rank));
            if (clamped + actor.Width > 4) clamped = Math.Max(0, 4 - actor.Width);
            actor.Rank = clamped;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的站位 → " + (actor.Rank + 1) + " 号位（允许重叠，用不变量检查确认）";
            SetNote(note);
            return true;
        }

        public bool Kill(ActorState actor, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.Hp = 0;
            actor.Dead = true;
            MarkMutation();
            CheckEndAfterMutation();
            SyncLog();
            note = actor.Name + " 已被直接击杀（不触发开战/击杀类饰品钩子）。";
            SetNote(note);
            return true;
        }

        public bool Revive(ActorState actor, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.Dead = false;
            actor.Hp = 1;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 已复活，HP=1。若开战时已死亡，它可能不在结算名单里。";
            SetNote(note);
            return true;
        }

        public bool HealFull(ActorState actor, out string note)
        {
            if (actor == null || actor.Definition == null) { note = "没有选中角色。"; return false; }
            actor.Hp = actor.Definition.MaxHp;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 已回满生命。";
            SetNote(note);
            return true;
        }

        public bool ClearStatuses(ActorState actor, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.BleedDamage = 0;
            actor.BleedTurns = 0;
            actor.Interrupted = 0;
            actor.Imbalance = 0;
            actor.ImbalanceImmuneRound = -1;
            actor.ImbalanceAutoHitUntilRound = -1;
            actor.Defending = false;
            actor.ProtectorId = null;
            actor.BlockCharges = 0;
            actor.Counter = false;
            actor.Followup = false;
            actor.FollowupAttack = 0;
            actor.DefenseBuff = 0;
            actor.AccuracyBuff = 0;
            actor.SelfVulnerable = 0;
            actor.GuardReduction = 0;
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的战斗状态已清空（不会重置技能次数与饰品状态）。";
            SetNote(note);
            return true;
        }

        public bool ResetSkillUses(ActorState actor, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.Uses.Clear();
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的技能次数已重置。";
            SetNote(note);
            return true;
        }

        public bool ResetAccessoryBattle(ActorState actor, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            actor.AccessoryBattle = new AccessoryBattleState();
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的饰品战斗状态已重置（开战钩子不会补触发）。";
            SetNote(note);
            return true;
        }

        // Direct field edits made by the battle page have no replayable representation, so they invalidate
        // the current recording and refresh the log. They deliberately do not force an end check: the user
        // may be halfway through editing several fields.
        public void Touch(string note)
        {
            MarkMutation();
            SyncLog();
            if (!string.IsNullOrEmpty(note)) SetNote(note);
        }

        public bool SetSkillUse(ActorState actor, SkillSpec skill, int left, out string note)
        {
            if (actor == null || skill == null) { note = "没有选中角色或技能。"; return false; }
            left = Math.Max(0, left);
            var use = actor.Uses.FirstOrDefault(x => x != null && x.Id == skill.Id);
            if (use == null) actor.Uses.Add(new SkillUse { Id = skill.Id, Left = left });
            else use.Left = left;
            note = actor.Name + " 的「" + skill.Name + "」次数 → " + left;
            Touch(note);
            return true;
        }

        public bool SetLimb(ActorState actor, LimbPart part, LabLimbState state, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            if (!actor.Player) { note = actor.Name + " 是怪物，没有部位。"; return false; }
            var limb = LimbSystem.Get(actor, part);
            if (limb == null) { note = actor.Name + " 没有" + LimbSystem.PartName(part) + "。"; return false; }
            switch (state)
            {
                case LabLimbState.Severed:
                    limb.Hp = 0;
                    limb.Prosthetic = null;
                    break;
                case LabLimbState.Injured:
                    limb.Hp = Math.Max(1, limb.MaxHp / 2);
                    limb.Prosthetic = null;
                    break;
                default:
                    limb.Hp = limb.MaxHp;
                    limb.Prosthetic = null;
                    break;
            }
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的" + LimbSystem.PartName(part) + " → " + state;
            SetNote(note);
            return true;
        }

        public bool InstallProsthetic(ActorState actor, string prostheticId, out string note)
        {
            if (actor == null || Battle == null) { note = "没有战斗或角色。"; return false; }
            var item = LimbSystem.Prosthetic(prostheticId, Battle.Rules == null ? null : Battle.Rules.Limbs);
            if (item == null) { note = "义肢 " + prostheticId + " 不存在。"; return false; }
            if (!LimbSystem.Install(actor, item)) { note = "该部位未断肢，无法安装「" + item.Name + "」。"; return false; }
            MarkMutation();
            SyncLog();
            note = actor.Name + " 安装了「" + item.Name + "」。";
            SetNote(note);
            return true;
        }

        public bool RepairLimb(ActorState actor, LimbPart part, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            if (!LimbSystem.Repair(actor, part)) { note = "该部位不是重伤状态，无法修复。"; return false; }
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的" + LimbSystem.PartName(part) + "已修复。";
            SetNote(note);
            return true;
        }

        public bool RebuildLimbs(ActorState actor, out string note)
        {
            if (actor == null || actor.Definition == null) { note = "没有选中角色。"; return false; }
            // The limb track is hero-only. Without this guard the lab's rebuild shortcut would be the one
            // place able to give a monster parts, contradicting the battle rule.
            if (!actor.Player) { note = actor.Name + " 是怪物，没有部位，也不会断肢。"; return false; }
            actor.Limbs = LimbSystem.CreateLimbs(actor.Definition.MaxHp, Battle == null || Battle.Rules == null ? null : Battle.Rules.Limbs);
            MarkMutation();
            SyncLog();
            note = actor.Name + " 的部位已按当前 MaxHp 重建（部位伤害被清零）。";
            SetNote(note);
            return true;
        }

        public bool AttachAccessory(ActorState actor, string accessoryId, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            var item = AccessorySystem.Find(accessoryId);
            if (item == null) { note = "饰品 " + accessoryId + " 不存在。"; return false; }
            AccessorySystem.Attach(actor, item);
            MarkMutation();
            SyncLog();
            note = actor.Name + " 装备了「" + item.Name + "」。金丝苹果会立即 +MaxHp；心脏/锈血/成瘾的开战钩子不会补触发。";
            SetNote(note);
            return true;
        }

        public bool DetachAccessory(ActorState actor, string accessoryId, out string note)
        {
            if (actor == null) { note = "没有选中角色。"; return false; }
            if (!AccessorySystem.Detach(actor, accessoryId)) { note = "该角色没有装备这件饰品。"; return false; }
            MarkMutation();
            SyncLog();
            note = actor.Name + " 卸下了饰品 " + accessoryId + "。";
            SetNote(note);
            return true;
        }

        // ---- random controls ----

        public bool SetRandomState(uint state, out string note)
        {
            if (Run == null || Run.Random == null) { note = "没有 Run。"; return false; }
            Run.Random.State = state;
            MarkMutation();
            note = "RNG State → " + state.ToString("X8");
            SetNote(note);
            return true;
        }

        public bool SetRandomStateFromText(string text, out string note)
        {
            text = (text ?? "").Trim();
            uint state;
            bool parsed;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                parsed = uint.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out state);
            else
                parsed = uint.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out state);
            if (!parsed)
            {
                note = "RNG State 必须是十进制整数或 0x 十六进制。";
                SetNote(note, true);
                return false;
            }
            return SetRandomState(state, out note);
        }

        public bool ForceNextDraw(double value, int times, bool consumeState, out string note)
        {
            if (Run == null || Run.Random == null) { note = "没有 Run。"; return false; }
            int count = Math.Max(1, times);
            Run.Random.ForceNextDraw(value, count, consumeState);
            Record("rng-force", -1, null, -1, RandomState, value, count, consumeState);
            note = "接下来 " + count + " 次抽取将返回 " + value.ToString("0.######")
                + (consumeState ? "（仍推进 State）" : "（不推进 State）");
            SetNote(note);
            return true;
        }

        public bool LockDraw(double value, bool consumeState, out string note)
        {
            if (Run == null || Run.Random == null) { note = "没有 Run。"; return false; }
            Run.Random.LockDraw(value, consumeState);
            Record("rng-lock", -1, null, -1, RandomState, value, -1, consumeState);
            note = "锁定所有抽取为 " + value.ToString("0.######") + "，直到清除覆盖。";
            SetNote(note);
            return true;
        }

        public bool ClearRandomOverrides(out string note)
        {
            if (Run == null || Run.Random == null) { note = "没有 Run。"; return false; }
            Run.Random.ClearDebugOverrides();
            Record("rng-clear", -1, null, -1, RandomState);
            note = "随机抽取覆盖已清除。";
            SetNote(note);
            return true;
        }

        public bool SaveSnapshot(string name, out string note)
        {
            if (Run == null || Run.Random == null) { note = "没有 Run。"; return false; }
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "快照 " + (RandomSnapshots.Count + 1);
            RandomSnapshots[name] = Run.Random.State;
            note = "已保存随机快照「" + name + "」= " + Run.Random.State.ToString("X8");
            SetNote(note);
            return true;
        }

        public bool RestoreSnapshot(string name, out string note)
        {
            if (RandomSnapshots.TryGetValue(name ?? "", out uint state))
                return SetRandomState(state, out note);
            note = "找不到随机快照「" + name + "」。";
            SetNote(note, true);
            return false;
        }

        public bool DeleteSnapshot(string name)
        {
            return RandomSnapshots.Remove(name ?? "");
        }

        // ---- recording ----

        public void ResetRecording()
        {
            RecordedSteps.Clear();
            RecordingInvalid = false;
            Recording = true;
            RecordedConfigJson = BattleLabJson.ToJson(Config, false);
        }

        public void StartRecording()
        {
            RecordedSteps.Clear();
            RecordingInvalid = false;
            Recording = true;
            RecordedConfigJson = BattleLabJson.ToJson(Config, false);
            SetNote("开始录制。开战和手动修改数值都会影响回放，请保持配置不变。");
        }

        public void StopRecording()
        {
            Recording = false;
            SetNote("录制已停止，共 " + RecordedSteps.Count + " 步。");
        }

        public void ClearRecording()
        {
            RecordedSteps.Clear();
            RecordingInvalid = false;
            Recording = false;
            SetNote("录制已清空。");
        }

        public void RestoreRecording(List<BattleLabReplayStep> steps, bool invalid, bool recording)
        {
            RecordedSteps.Clear();
            if (steps != null) RecordedSteps.AddRange(steps);
            RecordingInvalid = invalid;
            Recording = recording;
        }

        private void Record(string kind, int skillIndex = -1, string targetId = null, int stockIndex = -1,
            uint randomBefore = 0, double rngValue = 0, int rngTimes = 0, bool rngConsumeState = true)
        {
            if (!Recording || Run == null || Run.Random == null || Battle == null) return;
            RecordedSteps.Add(new BattleLabReplayStep
            {
                Kind = kind,
                SkillIndex = skillIndex,
                TargetId = targetId,
                StockIndex = stockIndex,
                RngValue = rngValue,
                RngTimes = rngTimes,
                RngConsumeState = rngConsumeState,
                RandomBefore = randomBefore,
                RandomAfter = Run.Random.State,
                EventsAfter = Battle.Events.Count,
                CurrentAfter = Battle.Current == null ? null : Battle.Current.Id,
                RoundAfter = Battle.Round,
                BattleAfter = Run.Battles
            });
        }

        private void MarkMutation()
        {
            if (Recording)
            {
                Recording = false;
                RecordingInvalid = true;
            }
        }

        private void CheckEndAfterMutation()
        {
            if (Battle == null || Battle.Outcome != BattleOutcome.Active) return;
            if (Battle.Enemies.All(e => e == null || e.Dead))
            {
                Battle.Finish(BattleOutcome.Victory);
                return;
            }
            if (Run.Heroes.All(h => h == null || h.Dead))
                Battle.Finish(BattleOutcome.Defeat);
        }

        private void SyncLog()
        {
            if (Battle == null) return;
            int battleNumber = Run == null ? 0 : Run.Battles;
            while (logCursor < Battle.Events.Count)
            {
                var entry = BattleLabLog.FromEvent(Battle, battleNumber, Battle.Events[logCursor], Log.Count);
                logCursor++;
                if (entry != null) Log.Add(entry);
            }
            // Keep the in-memory list bounded; the engine's own Events list is the authoritative one.
            if (Log.Count > 5000) Log.RemoveRange(0, Log.Count - 5000);
        }

        public List<string> CheckInvariants()
        {
            return BattleLabInvariants.Check(Battle);
        }

        public string CheckQueryPurity()
        {
            return BattleLabInvariants.CheckQueryPurity(Battle);
        }

        public void SetNote(string text, bool error = false)
        {
            LastNote = text ?? "";
            LastNoteIsError = error;
        }
    }
}
