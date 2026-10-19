using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // The single entry point for limb state and limb damage. Limb health is a second track next to HP:
    // it only ever goes down, it never regenerates on its own, and a part at zero stays severed until a
    // prosthetic replaces it. The track belongs to heroes only: monsters have no parts at all, and Damage
    // refuses every actor on the enemy side so no caller can sever one even if a debug tool attached limbs.
    public static class LimbSystem
    {
        public const double DefaultHitRatio = 0.5;

        public static readonly LimbPartRule[] DefaultParts =
        {
            new LimbPartRule(LimbPart.Head, 0.30, 0.10, 0.25),
            new LimbPartRule(LimbPart.Torso, 0.45, 0.20, 0.40),
            new LimbPartRule(LimbPart.Arm, 0.35, 0.20, 0.45),
            new LimbPartRule(LimbPart.Leg, 0.35, 0.20, 0.45)
        };

        // A conventional prosthetic restores basic function. Precision prosthetics and their trade-offs
        // stay out of this version, so every entry here is a plain replacement.
        public static readonly ProstheticSpec[] DefaultProsthetics =
        {
            new ProstheticSpec("prosthetic_head", "颅骨支架", LimbPart.Head, "替换受损的头骨，恢复基础感知。"),
            new ProstheticSpec("prosthetic_torso", "胸腔支架", LimbPart.Torso, "支撑躯干，恢复基础防护。"),
            new ProstheticSpec("prosthetic_arm", "义肢手臂", LimbPart.Arm, "替换失去的手臂，恢复基础攻击。"),
            new ProstheticSpec("prosthetic_leg", "义肢腿", LimbPart.Leg, "替换失去的腿，恢复基础移动。")
        };

        // Fills in anything a catalog left blank. Every GameCatalog asset predates these fields, so an
        // absent value arrives as zero or null and has to fall back to the rule default.
        public static LimbRules Resolve(LimbRules rules)
        {
            var source = rules ?? new LimbRules();
            return new LimbRules
            {
                HitRatio = Finite(source.HitRatio) && source.HitRatio > 0 ? source.HitRatio : DefaultHitRatio,
                Parts = UsableParts(source.Parts) ? CloneParts(source.Parts) : CloneParts(DefaultParts),
                Prosthetics = UsableProsthetics(source.Prosthetics)
                    ? CloneProsthetics(source.Prosthetics) : CloneProsthetics(DefaultProsthetics)
            };
        }

        public static List<LimbState> CreateLimbs(int maxHp, LimbRules rules)
        {
            var resolved = Resolve(rules);
            var limbs = new List<LimbState>();
            for (int i = 0; i < resolved.Parts.Length; i++)
            {
                var rule = resolved.Parts[i];
                int max = Math.Max(1, (int)Math.Ceiling(maxHp * rule.HpRatio - 1e-9));
                limbs.Add(new LimbState { Part = rule.Part, Hp = max, MaxHp = max });
            }
            return limbs;
        }

        public static LimbState Get(ActorState actor, LimbPart part)
        {
            if (actor == null || actor.Limbs == null) return null;
            return actor.Limbs.FirstOrDefault(l => l != null && l.Part == part);
        }

        public static LimbCondition ConditionOf(ActorState actor, LimbPart part) { return ConditionOf(Get(actor, part)); }

        public static LimbCondition ConditionOf(LimbState limb)
        {
            if (limb == null) return LimbCondition.Intact;
            if (limb.Hp <= 0) return LimbCondition.Severed;
            return limb.Hp * 2 <= limb.MaxHp ? LimbCondition.Injured : LimbCondition.Intact;
        }

        public static bool HasProsthetic(ActorState actor, LimbPart part)
        {
            var limb = Get(actor, part);
            return limb != null && limb.ProstheticFitted;
        }

        public static bool AnyLimbDamaged(ActorState actor)
        {
            if (actor == null || actor.Limbs == null) return false;
            return actor.Limbs.Any(l => l != null && l.Hp < l.MaxHp);
        }

        // Damage per limb hit for this actor and part. Derived from the part's own maximum so a tougher
        // character takes proportionally tougher hits but still breaks in the same number of them.
        public static int HitAmount(ActorState actor, LimbPart part, LimbRules rules)
        {
            var limb = Get(actor, part);
            if (limb == null) return 0;
            double ratio = Finite(rules == null ? 0 : rules.HitRatio) && rules.HitRatio > 0 ? rules.HitRatio : DefaultHitRatio;
            return Math.Max(1, (int)Math.Ceiling(limb.MaxHp * ratio - 1e-9));
        }

        // Returns true when this hit is the one that severed the part. Limb health never rises here.
        // A prosthetic is wrecked along with the part it replaced, so a second amputation costs another
        // prosthetic rather than leaving a broken fitting that still reports full function.
        public static bool Damage(ActorState actor, LimbPart part, int amount)
        {
            // The limb track is hero-only. Enemies are created without Limbs, but this guard makes the
            // rule independent of that construction detail: no monster can ever be dismembered.
            if (actor == null || !actor.Player) return false;
            var limb = Get(actor, part);
            if (limb == null || amount <= 0 || limb.Hp <= 0) return false;
            limb.Hp = Math.Max(0, limb.Hp - amount);
            if (limb.Hp > 0) return false;
            limb.Prosthetic = null;
            return true;
        }

        // Only an injured part can be repaired. A severed part needs a prosthetic instead.
        public static bool CanRepair(ActorState actor, LimbPart part)
        {
            return ConditionOf(actor, part) == LimbCondition.Injured;
        }

        public static bool Repair(ActorState actor, LimbPart part)
        {
            if (!CanRepair(actor, part)) return false;
            Get(actor, part).Hp = Get(actor, part).MaxHp;
            return true;
        }

        // A prosthetic only goes onto a part that is already gone. A part still carrying a fitting is not
        // severed, and one that is severed lost its fitting with it.
        public static bool CanInstall(ActorState actor, ProstheticSpec item)
        {
            if (actor == null || item == null) return false;
            var limb = Get(actor, item.Part);
            return limb != null && limb.Hp <= 0;
        }

        public static bool Install(ActorState actor, ProstheticSpec item)
        {
            if (!CanInstall(actor, item)) return false;
            var limb = Get(actor, item.Part);
            limb.Prosthetic = item.Id;
            limb.Hp = limb.MaxHp;
            return true;
        }

        public static ProstheticSpec Prosthetic(string id, LimbRules rules)
        {
            return Resolve(rules).Prosthetics.FirstOrDefault(p => p != null && p.Id == id);
        }

        // The four attribute factors. A fitted prosthetic always returns the neutral value, which is what
        // "a conventional prosthetic restores basic function" means in practice. Severed replaces injured
        // rather than stacking with it.
        public static double AttackFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Arm); }
        public static double DefenseFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Torso); }
        public static double SpeedFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Leg); }
        public static double AccuracyFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Head); }

        private static double Factor(ActorState actor, LimbRules rules, LimbPart part)
        {
            if (actor == null || !actor.Player) return 1;
            var limb = Get(actor, part);
            if (limb == null || limb.ProstheticFitted) return 1;
            var rule = RuleOrDefault(rules, part);
            if (limb.Hp <= 0) return 1 - rule.SeveredPenalty;
            if (limb.Hp * 2 <= limb.MaxHp) return 1 - rule.InjuredPenalty;
            return 1;
        }

        // Which enemy actions maul a hero. The whisperer applies sanity pressure instead of physical
        // damage, so it never carries one.
        public static LimbAttackSpec ForPattern(EnemyPattern pattern)
        {
            switch (pattern)
            {
                case EnemyPattern.Cleaver: return new LimbAttackSpec(LimbPart.Arm, 0.5, true);
                case EnemyPattern.Hunter: return new LimbAttackSpec(LimbPart.Leg, 0.35, false);
                default: return null;
            }
        }

        public static string PartName(LimbPart part)
        {
            switch (part)
            {
                case LimbPart.Head: return "头部";
                case LimbPart.Torso: return "躯干";
                case LimbPart.Arm: return "手臂";
                case LimbPart.Leg: return "腿部";
                default: return "部位";
            }
        }

        public static string ConditionName(LimbCondition condition)
        {
            switch (condition)
            {
                case LimbCondition.Injured: return "重伤";
                case LimbCondition.Severed: return "断肢";
                default: return "完好";
            }
        }

        // Short label for the battle card, e.g. "断肢 手臂". Empty while every part is intact.
        public static string Summary(ActorState actor)
        {
            if (actor == null || actor.Limbs == null) return "";
            var severed = actor.Limbs.FirstOrDefault(l => l != null && l.Hp <= 0);
            if (severed != null) return "断肢 " + PartName(severed.Part);
            var injured = actor.Limbs.FirstOrDefault(l => l != null && l.Hp * 2 <= l.MaxHp);
            return injured == null ? "" : "重伤 " + PartName(injured.Part);
        }

        private static LimbPartRule RuleOrDefault(LimbRules rules, LimbPart part)
        {
            if (rules != null && rules.Parts != null)
                for (int i = 0; i < rules.Parts.Length; i++)
                    if (rules.Parts[i] != null && rules.Parts[i].Part == part) return rules.Parts[i];
            for (int i = 0; i < DefaultParts.Length; i++)
                if (DefaultParts[i].Part == part) return DefaultParts[i];
            return new LimbPartRule(part, 0.35, 0.20, 0.45);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool UsableParts(LimbPartRule[] values)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
            {
                var rule = values[i];
                if (rule == null) return false;
                if (!Finite(rule.HpRatio) || rule.HpRatio <= 0) return false;
                if (!Finite(rule.InjuredPenalty) || !Finite(rule.SeveredPenalty)) return false;
                if (rule.InjuredPenalty < 0 || rule.InjuredPenalty >= 1) return false;
                if (rule.SeveredPenalty < 0 || rule.SeveredPenalty >= 1) return false;
            }
            return true;
        }

        private static bool UsableProsthetics(ProstheticSpec[] values)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == null || string.IsNullOrEmpty(values[i].Id) || string.IsNullOrEmpty(values[i].Name)) return false;
            return true;
        }

        private static LimbPartRule[] CloneParts(LimbPartRule[] values)
        {
            var copy = new LimbPartRule[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = new LimbPartRule(values[i].Part, values[i].HpRatio, values[i].InjuredPenalty, values[i].SeveredPenalty);
            return copy;
        }

        private static ProstheticSpec[] CloneProsthetics(ProstheticSpec[] values)
        {
            var copy = new ProstheticSpec[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = new ProstheticSpec(values[i].Id, values[i].Name, values[i].Part, values[i].Description);
            return copy;
        }
    }
}
