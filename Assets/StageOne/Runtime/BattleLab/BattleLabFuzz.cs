using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    [Serializable]
    public sealed class BattleLabFuzzSettings
    {
        public int Battles = 50;
        public int MaxSteps = 400;
        public uint Seed = 12345;
        public bool RandomizeStats = true;
        public bool RandomizeEquipment = true;
        public bool RandomizeRules;
        public bool StopOnFailure = true;
    }

    public sealed class BattleLabFuzzFailure
    {
        public int Index;
        public uint Seed;
        public string Message;
        public string ConfigJson;
        public string LogText;
        public string StateText;

        public override string ToString()
        {
            return "#" + Index + " seed=" + Seed + " " + Message;
        }
    }

    public sealed class BattleLabFuzzProgress
    {
        public bool Running;
        public int Completed;
        public int Failed;
        public int Total;
        public uint CurrentSeed;
        public string LastMessage = "未开始。";
        public readonly List<BattleLabFuzzFailure> Failures = new List<BattleLabFuzzFailure>();

        public void Reset(int total)
        {
            Running = true;
            Completed = 0;
            Failed = 0;
            Total = Math.Max(1, total);
            CurrentSeed = 0;
            LastMessage = "开始 fuzz…";
            Failures.Clear();
        }

        public void Record(BattleLabFuzzFailure failure)
        {
            if (failure == null) return;
            Failed++;
            Failures.Add(failure);
            LastMessage = "失败：" + failure.Message;
        }

        public string ReportText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Fuzz 报告");
            sb.AppendLine("完成 " + Completed + "/" + Total + "，失败 " + Failed + "。");
            sb.AppendLine(LastMessage);
            foreach (var failure in Failures)
            {
                sb.AppendLine();
                sb.AppendLine("---- " + failure + " ----");
                sb.AppendLine(failure.Message);
                if (!string.IsNullOrEmpty(failure.StateText)) sb.AppendLine(failure.StateText);
            }
            return sb.ToString();
        }
    }

    // Generates random lineups, stats, equipment and action choices, then drives real BattleEngines until
    // one settles. Every generated battle is a fresh RunState, so a crash cannot leak into the next case.
    public static class BattleLabFuzz
    {
        public static BattleLabFuzzProgress Run(GameCatalog catalog, BattleLabFuzzSettings settings)
        {
            var progress = new BattleLabFuzzProgress();
            int total = settings == null ? 1 : Math.Max(1, settings.Battles);
            progress.Reset(total);
            for (int i = 0; i < total; i++)
            {
                var failure = RunOne(catalog, settings, i, progress);
                if (failure != null && settings != null && settings.StopOnFailure)
                {
                    progress.Running = false;
                    return progress;
                }
            }
            progress.Running = false;
            progress.LastMessage = progress.Failed == 0
                ? "全部通过：" + progress.Completed + " 场。"
                : "完成，失败 " + progress.Failed + " 场。";
            return progress;
        }

        // Runs one battle. The caller (a coroutine) can invoke this once per frame to keep the editor
        // responsive; the returned failure is already recorded into progress.
        public static BattleLabFuzzFailure RunOne(GameCatalog catalog, BattleLabFuzzSettings settings,
            int index, BattleLabFuzzProgress progress)
        {
            int maxSteps = settings == null ? 400 : Math.Max(10, Math.Min(10000, settings.MaxSteps));
            uint seed = settings == null ? (uint)(index + 1) : settings.Seed + (uint)index;
            if (progress != null) progress.CurrentSeed = seed;

            var rng = new RunRandom(seed == 0 ? 0xA5A5A5A5u : seed);
            BattleLabConfig cfg = null;
            BattleLabSession session = null;
            try
            {
                cfg = BuildConfig(catalog, settings, seed, rng);
                cfg.Normalize();
                var errors = BattleLabFactory.Validate(cfg, catalog);
                if (errors.Count > 0)
                    return Fail(progress, index, seed, "随机配置未通过校验：" + string.Join(" | ", errors), cfg, session);

                session = new BattleLabSession(catalog, cfg);
                if (!session.StartBattle(out string startError))
                    return Fail(progress, index, seed, "开战失败：" + startError, cfg, session);
                session.StopRecording();

                var actionRng = new RunRandom(seed ^ 0x9E3779B9u);
                int step = 0;
                for (; step < maxSteps; step++)
                {
                    if (session.Battle == null || session.Battle.Outcome != BattleOutcome.Active) break;
                    var issues = session.CheckInvariants();
                    if (issues.Count > 0)
                        return Fail(progress, index, seed, "不变量失败：" + string.Join(" | ", issues), cfg, session);
                    if (!TakeAction(session, actionRng, out string actionError))
                    {
                        if (session.Battle.Outcome == BattleOutcome.Active)
                            return Fail(progress, index, seed, "第 " + (step + 1) + " 步行动失败：" + actionError, cfg, session);
                        break;
                    }
                }

                if (session.Battle != null && session.Battle.Outcome == BattleOutcome.Active)
                    return Fail(progress, index, seed, "达到最大步数 " + maxSteps + " 仍未结束。", cfg, session);

                var finalIssues = session.CheckInvariants();
                if (finalIssues.Count > 0)
                    return Fail(progress, index, seed, "结算后不变量失败：" + string.Join(" | ", finalIssues), cfg, session);

                if (progress != null)
                {
                    progress.Completed++;
                    progress.LastMessage = "第 " + progress.Completed + "/" + progress.Total + " 场通过（seed " + seed + "）。";
                }
                return null;
            }
            catch (Exception e)
            {
                return Fail(progress, index, seed, "异常：" + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace, cfg, session);
            }
        }

        private static BattleLabFuzzFailure Fail(BattleLabFuzzProgress progress, int index, uint seed, string message,
            BattleLabConfig cfg, BattleLabSession session)
        {
            var failure = new BattleLabFuzzFailure
            {
                Index = index,
                Seed = seed,
                Message = message,
                ConfigJson = cfg == null ? "{}" : JsonUtility.ToJson(cfg, true),
                LogText = session == null ? "" : BattleLabLog.ToText(session.Log),
                StateText = session == null ? "" : BattleLabJson.DescribeState(session)
            };
            if (progress != null)
            {
                progress.Completed++;
                progress.Record(failure);
            }
            return failure;
        }

        private static bool TakeAction(BattleLabSession session, RunRandom rng, out string error)
        {
            error = null;
            var battle = session.Battle;
            if (battle == null || battle.Outcome != BattleOutcome.Active) { error = "战斗已经结束。"; return false; }
            var current = battle.Current;
            if (current == null) { error = "没有当前行动者。"; return false; }
            if (!current.Player) return session.ExecuteEnemy(out error);

            double pick = rng.NextUnit();
            // A small amount of skipping tests the debug hook and the turn queue on both sides.
            if (pick < 0.04) return session.SkipTurn(out error);

            if (pick < 0.18)
            {
                var swaps = battle.SwapTargets().ToList();
                if (swaps.Count > 0)
                {
                    int swapIndex = Math.Min(swaps.Count - 1, (int)(rng.NextUnit() * swaps.Count));
                    if (session.ExecuteSwap(swaps[swapIndex].Id, out error)) return true;
                }
            }

            if (pick < 0.34 && session.Run != null && session.Run.Consumables != null && session.Run.Consumables.Count > 0)
            {
                int stock = Math.Min(session.Run.Consumables.Count - 1, (int)(rng.NextUnit() * session.Run.Consumables.Count));
                var living = session.Run.Heroes.Where(h => h != null && !h.Dead).ToList();
                if (living.Count > 0)
                {
                    var target = living[Math.Min(living.Count - 1, (int)(rng.NextUnit() * living.Count))];
                    if (session.UseConsumable(stock, target.Id, out error)) return true;
                }
            }

            var skills = battle.SkillsFor(current);
            var usable = new List<int>();
            for (int i = 0; i < skills.Length; i++)
                if (battle.SkillDisabledReason(i) == null) usable.Add(i);
            if (usable.Count > 0)
            {
                int index = usable[Math.Min(usable.Count - 1, (int)(rng.NextUnit() * usable.Count))];
                var skill = skills[index];
                string targetId = null;
                if (skill.Targeting == SkillTargeting.Choose)
                {
                    var targets = battle.Targets(index).ToList();
                    if (targets.Count == 0) return session.SkipTurn(out error);
                    targetId = targets[Math.Min(targets.Count - 1, (int)(rng.NextUnit() * targets.Count))].Id;
                }
                if (session.ExecuteSkill(index, targetId, out error)) return true;
            }
            return session.SkipTurn(out error);
        }

        private static BattleLabConfig BuildConfig(GameCatalog catalog, BattleLabFuzzSettings settings, uint seed, RunRandom rng)
        {
            var cfg = BattleLabFactory.CreateDefault(catalog);
            cfg.SeedText = seed.ToString(CultureInfo.InvariantCulture);
            bool randomizeStats = settings == null || settings.RandomizeStats;
            bool randomizeEquipment = settings == null || settings.RandomizeEquipment;
            bool randomizeRules = settings != null && settings.RandomizeRules;

            var heroes = catalog != null && catalog.Heroes != null
                ? catalog.Heroes.Where(h => h).ToList() : new List<CharacterDefinition>();
            int enabled = 0;
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = cfg.Heroes[i];
                slot.Enabled = rng.NextUnit() < 0.8;
                if (!slot.Enabled) continue;
                if (heroes.Count > 0)
                {
                    var source = heroes[Math.Min(heroes.Count - 1, (int)(rng.NextUnit() * heroes.Count))];
                    BattleLabFactory.ApplyHeroSource(slot, source);
                }
                else
                {
                    slot.Spec = new ActorSpec
                    {
                        Id = "fuzz_hero_" + i, Name = "FuzzHero" + i, MaxHp = 40, Speed = 5,
                        Attack = 10, Accuracy = 0.9, CritChance = 0.1
                    };
                }
                enabled++;
                slot.Sanity = rng.NextUnit() * 100;
                slot.SanityLockPending = rng.NextUnit() < 0.08;
                slot.SanitySkillsOnly = rng.NextUnit() < 0.08;
                if (randomizeStats) RandomizeStats(slot.Spec, rng, true);
                if (randomizeEquipment) RandomizeEquipment(slot, rng);
            }
            if (enabled == 0)
            {
                // Keep the generated case useful: a team of zero is a valid defeat test, but fuzz should
                // mostly exercise combat. Force the first slot back on.
                cfg.Heroes[0].Enabled = true;
                if (heroes.Count > 0) BattleLabFactory.ApplyHeroSource(cfg.Heroes[0], heroes[0]);
                cfg.Heroes[0].Sanity = 50;
            }

            var enemies = BattleLabFactory.EnemySources(catalog);
            int count = 1 + (int)(rng.NextUnit() * 4); // 1..4
            int rank = 0;
            int placed = 0;
            for (int i = 0; i < BattleLabConfig.SlotCount && placed < count; i++)
            {
                var slot = cfg.Enemies[i];
                ActorSpec spec;
                CharacterDefinition source = null;
                if (enemies.Count > 0)
                {
                    source = enemies[Math.Min(enemies.Count - 1, (int)(rng.NextUnit() * enemies.Count))];
                    spec = BattleLabFactory.Clone(source.ToSpec());
                }
                else
                {
                    spec = BattleLabFactory.DummySpec();
                }
                int width = Math.Max(1, Math.Min(4, spec.Width));
                if (rank + width > 4) break;
                slot.Enabled = true;
                slot.SourceId = source == null ? "lab_dummy" : source.Id;
                if (source == null) slot.Spec = spec;
                else BattleLabFactory.ApplyEnemySource(slot, source);
                slot.StartRank = rank;
                if (randomizeStats) RandomizeStats(slot.Spec, rng, false);
                rank += width;
                placed++;
            }
            if (placed == 0)
            {
                cfg.Enemies[0].Enabled = true;
                cfg.Enemies[0].SourceId = "lab_dummy";
                cfg.Enemies[0].Spec = BattleLabFactory.DummySpec();
                cfg.Enemies[0].StartRank = 0;
            }

            if (randomizeRules)
            {
                cfg.UseCatalogRules = false;
                var rules = BattleLabFactory.CloneRules(cfg.Rules);
                rules.DeathChance = rng.NextUnit();
                rules.CritMultiplier = 1 + rng.NextUnit() * 2;
                rules.ProbabilityStrength = rng.NextUnit() * 3;
                rules.FeedbackFactor = rng.NextUnit();
                rules.ImbalanceImmuneRounds = (int)(rng.NextUnit() * 3);
                cfg.Rules = rules;
            }
            return cfg;
        }

        private static void RandomizeStats(ActorSpec spec, RunRandom rng, bool hero)
        {
            if (spec == null) return;
            spec.MaxHp = 15 + (int)(rng.NextUnit() * 140);
            spec.Attack = (int)(rng.NextUnit() * 30);
            spec.Speed = 1 + (int)(rng.NextUnit() * 12);
            spec.Accuracy = rng.NextUnit();
            spec.CritChance = rng.NextUnit() * 0.5;
            spec.Dodge = rng.NextUnit() * 0.4;
            spec.Defense = rng.NextUnit() * 0.5;
            if (!hero)
            {
                // Keep the source's rank width and behavior; only the numbers move. A percentage of enemies
                // gets the healing-on-hero-death passive so the branch is covered.
                spec.BossHealOnHeroDeath = rng.NextUnit() < 0.15 ? 1 + (int)(rng.NextUnit() * 10) : 0;
            }
        }

        private static void RandomizeEquipment(BattleLabHeroConfig slot, RunRandom rng)
        {
            slot.ForceAccessories = true;
            slot.AccessoryIds.Clear();
            var pool = AccessorySystem.Defaults.Where(a => a != null).Select(a => a.Id).ToList();
            int accessoryCount = (int)(rng.NextUnit() * 3); // 0..2
            for (int i = 0; i < accessoryCount && pool.Count > 0; i++)
            {
                int index = Math.Min(pool.Count - 1, (int)(rng.NextUnit() * pool.Count));
                slot.AccessoryIds.Add(pool[index]);
                pool.RemoveAt(index);
            }
            slot.ConsumableCounts = new int[ConsumableSystem.Defaults.Length];
            for (int i = 0; i < slot.ConsumableCounts.Length; i++)
                slot.ConsumableCounts[i] = (int)(rng.NextUnit() * 3); // 0..2

            slot.Limbs = new[] { LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact };
            slot.ProstheticIds = new[] { "", "", "", "" };
            for (int i = 0; i < 4; i++)
            {
                double roll = rng.NextUnit();
                if (roll < 0.10) slot.Limbs[i] = LabLimbState.Severed;
                else if (roll < 0.30) slot.Limbs[i] = LabLimbState.Injured;
            }
        }
    }
}
