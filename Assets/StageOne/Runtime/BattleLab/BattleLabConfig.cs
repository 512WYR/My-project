using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    // Pre-battle limb choice. The numeric thresholds follow LimbSystem: at or below half health is
    // injured, zero is severed.
    public enum LabLimbState { Intact, Injured, Severed }

    // Everything the Battle Lab can configure before a fight starts. The shape is deliberately flat and
    // JsonUtility-friendly: no dictionaries, no nullable fields, fixed-size slot arrays.
    [Serializable]
    public sealed class BattleLabConfig
    {
        public const int CurrentVersion = 1;
        public const int SlotCount = 4;

        public int Version = CurrentVersion;
        public string SeedText = "44972";
        public double PlayerSanity = 50;
        public bool FullHealOnStart = true;
        // True means the catalog's CombatRules are used and the Rules copy below is only a preview.
        public bool UseCatalogRules = true;
        public CombatRules Rules = new CombatRules();
        public BattleLabHeroConfig[] Heroes = new BattleLabHeroConfig[SlotCount];
        public BattleLabEnemyConfig[] Enemies = new BattleLabEnemyConfig[SlotCount];
        public bool AutoEnemy;
        public float AutoEnemyInterval = 0.35f;

        public bool TryGetSeed(out uint seed, out string error)
        {
            seed = 0;
            error = null;
            string text = (SeedText ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed))
                    return true;
            }
            else if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                return true;
            }
            error = "种子必须是十进制整数或 0x 十六进制。";
            return false;
        }

        // Fills in anything a hand-edited or older JSON left blank. Called after every import and before
        // every build, so the rest of the tool can assume non-null arrays and in-range values.
        public void Normalize()
        {
            if (Version <= 0) Version = CurrentVersion;
            if (string.IsNullOrWhiteSpace(SeedText)) SeedText = "44972";
            PlayerSanity = Clamp(PlayerSanity, 0, 100);
            if (float.IsNaN(AutoEnemyInterval) || float.IsInfinity(AutoEnemyInterval)) AutoEnemyInterval = 0.35f;
            AutoEnemyInterval = (float)Clamp(AutoEnemyInterval, 0.05, 2.0);

            Rules = Rules ?? new CombatRules();
            NormalizeRules(Rules);

            Heroes = Resize(Heroes);
            for (int i = 0; i < SlotCount; i++)
            {
                var hero = Heroes[i] ?? (Heroes[i] = new BattleLabHeroConfig());
                hero.Normalize(i);
            }

            Enemies = Resize(Enemies);
            for (int i = 0; i < SlotCount; i++)
            {
                var enemy = Enemies[i] ?? (Enemies[i] = new BattleLabEnemyConfig());
                enemy.Normalize(i);
            }
        }

        public static void NormalizeRules(CombatRules rules)
        {
            if (rules == null) return;
            var defaults = new CombatRules();
            rules.Limbs = LimbSystem.Resolve(rules.Limbs);
            rules.ProbabilityStrength = Clamp(rules.ProbabilityStrength, 0, 3);
            rules.FeedbackFactor = Clamp(rules.FeedbackFactor, 0, 1);
            rules.DeathChance = Clamp(rules.DeathChance, 0, 1);
            rules.CritMultiplier = Clamp(rules.CritMultiplier, 1, 3);
            if (rules.MindStep <= 0) rules.MindStep = defaults.MindStep;
            if (rules.ImbalanceImmuneRounds < 0) rules.ImbalanceImmuneRounds = 0;
            if (rules.ImbalanceAutoHitRounds < 0) rules.ImbalanceAutoHitRounds = 0;
            rules.TechnicalWeight = Math.Max(0, rules.TechnicalWeight);
            rules.BasicWeight = Math.Max(0, rules.BasicWeight);
            rules.PrimalWeight = Math.Max(0, rules.PrimalWeight);
            if (rules.TechnicalWeight + rules.BasicWeight + rules.PrimalWeight <= 0)
            {
                rules.TechnicalWeight = defaults.TechnicalWeight;
                rules.BasicWeight = defaults.BasicWeight;
                rules.PrimalWeight = defaults.PrimalWeight;
            }
            if (double.IsNaN(rules.FollowupRatio) || rules.FollowupRatio < 0) rules.FollowupRatio = defaults.FollowupRatio;
            if (double.IsNaN(rules.CounterRatio) || rules.CounterRatio < 0) rules.CounterRatio = defaults.CounterRatio;
            if (double.IsNaN(rules.SkillAccuracyBaseline)) rules.SkillAccuracyBaseline = defaults.SkillAccuracyBaseline;
        }

        private static BattleLabHeroConfig[] Resize(BattleLabHeroConfig[] source)
        {
            var result = new BattleLabHeroConfig[SlotCount];
            if (source != null)
                for (int i = 0; i < SlotCount && i < source.Length; i++)
                    result[i] = source[i];
            for (int i = 0; i < SlotCount; i++)
                if (result[i] == null) result[i] = new BattleLabHeroConfig();
            return result;
        }

        private static BattleLabEnemyConfig[] Resize(BattleLabEnemyConfig[] source)
        {
            var result = new BattleLabEnemyConfig[SlotCount];
            if (source != null)
                for (int i = 0; i < SlotCount && i < source.Length; i++)
                    result[i] = source[i];
            for (int i = 0; i < SlotCount; i++)
                if (result[i] == null) result[i] = new BattleLabEnemyConfig();
            return result;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    [Serializable]
    public sealed class BattleLabHeroConfig
    {
        public bool Enabled = true;
        public string SourceId = "";
        // A deep-cloned ActorSpec. The UI edits this copy directly; the catalog asset is never touched.
        public ActorSpec Spec = new ActorSpec();
        public int Rank = -1;                 // -1 = use the compact order of enabled slots.
        public double Sanity = 50;
        public bool SanitySkillsOnly;
        public bool SanityLockPending;
        public LabLimbState[] Limbs = { LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact };
        // Indexed by LimbPart: Head, Torso, Arm, Leg.
        public string[] ProstheticIds = new string[4];
        public List<string> AccessoryIds = new List<string>();
        public bool ForceAccessories;
        // Indexed like ConsumableSystem.Defaults.
        public int[] ConsumableCounts = new int[3];

        public void Normalize(int slot)
        {
            SourceId = SourceId ?? "";
            if (Spec == null) Spec = new ActorSpec();
            Spec.Id = string.IsNullOrEmpty(Spec.Id) ? "hero_" + slot : Spec.Id;
            Spec.Name = string.IsNullOrEmpty(Spec.Name) ? "英雄 " + (slot + 1) : Spec.Name;
            Spec.MaxHp = Math.Max(1, Spec.MaxHp);
            Spec.Speed = Math.Max(1, Spec.Speed);
            Spec.Attack = Math.Max(0, Spec.Attack);
            Spec.Accuracy = Clamp01(Spec.Accuracy, 0.9);
            Spec.CritChance = Clamp01(Spec.CritChance, 0.1);
            Spec.Dodge = Clamp01(Spec.Dodge, 0);
            Spec.Defense = Clamp01(Spec.Defense, 0);
            Spec.SkillsNormalize();
            Sanity = ClampRange(Sanity, 0, 100);
            Limbs = ResizeLimbs(Limbs);
            ProstheticIds = ResizeStrings(ProstheticIds, 4);
            AccessoryIds = AccessoryIds ?? new List<string>();
            AccessoryIds.RemoveAll(string.IsNullOrEmpty);
            ConsumableCounts = ResizeCounts(ConsumableCounts, ConsumableSystem.Defaults.Length);
            if (Rank < -1 || Rank > 3) Rank = -1;
        }

        private static LabLimbState[] ResizeLimbs(LabLimbState[] source)
        {
            var result = new[] { LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Intact };
            if (source != null)
                for (int i = 0; i < 4 && i < source.Length; i++)
                    if (Enum.IsDefined(typeof(LabLimbState), source[i])) result[i] = source[i];
            return result;
        }

        private static string[] ResizeStrings(string[] source, int length)
        {
            var result = new string[length];
            if (source != null)
                for (int i = 0; i < length && i < source.Length; i++)
                    result[i] = source[i] ?? "";
            for (int i = 0; i < length; i++) result[i] = result[i] ?? "";
            return result;
        }

        private static int[] ResizeCounts(int[] source, int length)
        {
            var result = new int[length];
            if (source != null)
                for (int i = 0; i < length && i < source.Length; i++)
                    result[i] = Math.Max(0, source[i]);
            return result;
        }

        private static double ClampRange(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            return value < min ? min : value > max ? max : value;
        }

        private static double Clamp01(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }
    }

    [Serializable]
    public sealed class BattleLabEnemyConfig
    {
        public bool Enabled;
        public string SourceId = "";
        // A deep-cloned ActorSpec. StartRank is applied from StartRank below when it is -1.
        public ActorSpec Spec = new ActorSpec();
        public int StartRank = -1; // -1 = use the slot index.

        public void Normalize(int slot)
        {
            SourceId = SourceId ?? "";
            if (Spec == null) Spec = new ActorSpec();
            Spec.Id = string.IsNullOrEmpty(Spec.Id) ? "enemy_" + slot : Spec.Id;
            Spec.Name = string.IsNullOrEmpty(Spec.Name) ? "敌人 " + (slot + 1) : Spec.Name;
            Spec.MaxHp = Math.Max(1, Spec.MaxHp);
            Spec.Speed = Math.Max(1, Spec.Speed);
            Spec.Attack = Math.Max(0, Spec.Attack);
            Spec.Accuracy = Clamp01(Spec.Accuracy, 0.9);
            Spec.CritChance = Clamp01(Spec.CritChance, 0.1);
            Spec.Dodge = Clamp01(Spec.Dodge, 0);
            Spec.Defense = Clamp01(Spec.Defense, 0);
            Spec.RankWidth = Math.Max(1, Math.Min(4, Spec.RankWidth));
            Spec.ActionInterval = Math.Max(1, Spec.ActionInterval);
            Spec.ActionPhase = Math.Max(0, Spec.ActionPhase);
            Spec.GuardReduction = Clamp01(Spec.GuardReduction, 0);
            Spec.BossHealOnHeroDeath = Math.Max(0, Spec.BossHealOnHeroDeath);
            Spec.SkillsNormalize();
            if (StartRank < -1 || StartRank > 3) StartRank = -1;
        }

        private static double Clamp01(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }
    }

    internal static class ActorSpecLabExtensions
    {
        // JsonUtility turns the static EmptySkills reference into a plain empty array. Re-establish the
        // invariant so callers can concatenate the three lists without null checks.
        public static void SkillsNormalize(this ActorSpec spec)
        {
            if (spec == null) return;
            spec.BasicSkills = spec.BasicSkills ?? ActorSpec.EmptySkills;
            spec.PrimalSkills = spec.PrimalSkills ?? ActorSpec.EmptySkills;
            spec.TechnicalSkills = spec.TechnicalSkills ?? ActorSpec.EmptySkills;
            spec.SummonPool = spec.SummonPool ?? new ActorSpec[0];
            for (int i = 0; i < spec.BasicSkills.Length; i++)
                NormalizeSkill(spec.BasicSkills[i]);
            for (int i = 0; i < spec.PrimalSkills.Length; i++)
                NormalizeSkill(spec.PrimalSkills[i]);
            for (int i = 0; i < spec.TechnicalSkills.Length; i++)
                NormalizeSkill(spec.TechnicalSkills[i]);
            for (int i = 0; i < spec.SummonPool.Length; i++)
                if (spec.SummonPool[i] != null) spec.SummonPool[i].SkillsNormalize();
        }

        private static void NormalizeSkill(SkillSpec skill)
        {
            if (skill == null) return;
            skill.Id = skill.Id ?? "";
            skill.Name = skill.Name ?? "";
            skill.Description = skill.Description ?? "";
            // A null Rider must stay null: the engine treats "no rider" differently from a rider with
            // Kind == None in a few preview paths, and JsonUtility can materialise a default object.
            if (skill.Rider != null && skill.Rider.Kind == RiderKind.None) skill.Rider = null;
            if (skill.ActorRanks == 0) skill.ActorRanks = 15;
            if (skill.TargetRanks == 0) skill.TargetRanks = 15;
            if (skill.BleedTurns <= 0) skill.BleedTurns = 1;
            if (skill.MaxLimbBonusStacks <= 0) skill.MaxLimbBonusStacks = 1;
        }
    }
}
