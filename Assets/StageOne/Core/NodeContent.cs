using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // The one team buff the party picks before the run starts.
    public enum GrowthBuff { None, Toughness, Sharpness, Clarity }

    // The buff edits ActorSpec fields, never the run's own bookkeeping, so the battle engine picks the
    // change up for free: it reads Definition.MaxHp and Definition.Attack. It never writes into a
    // character asset either, because CharacterDefinition.ToSpec hands out a fresh ActorSpec per call.
    public static class OpeningBuffs
    {
        public const int ToughnessHealth = 8;
        public const int SharpnessAttack = 2;
        public const int ClaritySanity = 10;

        // The spec-level half, kept for tests: it is the whole trick behind the buff.
        public static void ApplyToSpecs(GrowthBuff buff, ActorSpec[] specs)
        {
            if (specs == null) return;
            for (int i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                if (spec == null) continue;
                if (buff == GrowthBuff.Toughness) spec.MaxHp = Math.Max(1, spec.MaxHp + ToughnessHealth);
                else if (buff == GrowthBuff.Sharpness) spec.Attack = Math.Max(0, spec.Attack + SharpnessAttack);
            }
        }

        // The buff room is the very first position of the first floor, so the run already exists by the
        // time the choice is known. That is why this works on the run's own ActorSpecs instead: they are
        // the fresh copies the catalog handed out, never the character assets. Limb caps follow the new
        // health because they are derived from it, and the party is at full health this early in a run.
        public static void Apply(GrowthBuff buff, RunState run, LimbRules limbs)
        {
            if (run == null || buff == GrowthBuff.None) return;
            for (int i = 0; i < run.Heroes.Count; i++)
            {
                var hero = run.Heroes[i];
                if (hero == null || hero.Definition == null) continue;
                if (buff == GrowthBuff.Toughness)
                {
                    hero.Definition.MaxHp = Math.Max(1, hero.Definition.MaxHp + ToughnessHealth);
                    if (!hero.Dead) hero.Hp = Math.Min(hero.Definition.MaxHp, hero.Hp + ToughnessHealth);
                    hero.Limbs = LimbSystem.CreateLimbs(hero.Definition.MaxHp, limbs);
                }
                else if (buff == GrowthBuff.Sharpness) hero.Definition.Attack = Math.Max(0, hero.Definition.Attack + SharpnessAttack);
                else if (buff == GrowthBuff.Clarity) hero.Sanity = SanityProbability.Clamp(hero.Sanity + ClaritySanity, 0, 100);
            }
        }

        public static string Name(GrowthBuff buff)
        {
            switch (buff)
            {
                case GrowthBuff.Toughness: return "坚韧";
                case GrowthBuff.Sharpness: return "锋锐";
                case GrowthBuff.Clarity: return "清明";
                default: return "未选";
            }
        }

        public static string Describe(GrowthBuff buff)
        {
            switch (buff)
            {
                case GrowthBuff.Toughness: return "全队最大生命 +" + ToughnessHealth;
                case GrowthBuff.Sharpness: return "全队基础攻击 +" + SharpnessAttack;
                case GrowthBuff.Clarity: return "全队初始理智 +" + ClaritySanity;
                default: return "";
            }
        }

    }

    // Exploration owns the supply room and the shared healing helpers. Event and altar content used to live
    // here as plain data assets; the documented events need steps, chosen members and fights, so that half
    // moved to EventScripts. What stays here is a room service with no choices in it.
    public static class Exploration
    {
        public const double CalmSanity = 10;

        // Heals every living hero by a share of its own max health. Dead members never come back.
        public static int HealParty(RunState run, int percent)
        {
            if (run == null || percent <= 0) return 0;
            int total = 0;
            for (int i = 0; i < run.Heroes.Count; i++) total += Heal(run.Heroes[i], percent);
            return total;
        }

        public static int Heal(ActorState hero, int percent)
        {
            if (hero == null || hero.Dead || percent <= 0) return 0;
            int amount = Math.Max(1, (int)Math.Ceiling(hero.Definition.MaxHp * percent / 100.0 - 1e-9));
            int before = hero.Hp;
            hero.Hp = Math.Min(hero.Definition.MaxHp, hero.Hp + amount);
            return hero.Hp - before;
        }

        // One supply heal for a single hero. Returns how much health it actually restored.
        public static int SupplyHeal(MapRules rules, ActorState hero)
        {
            if (hero == null || hero.Dead) return 0;
            double ratio = MapGenerator.Resolve(rules).SupplyHealRatio;
            int amount = Math.Max(1, (int)Math.Ceiling(hero.Definition.MaxHp * ratio - 1e-9));
            int before = hero.Hp;
            hero.Hp = Math.Min(hero.Definition.MaxHp, hero.Hp + amount);
            return hero.Hp - before;
        }

        public static int SupplyHealParty(MapRules rules, RunState run)
        {
            if (run == null) return 0;
            int total = 0;
            for (int i = 0; i < run.Heroes.Count; i++) total += SupplyHeal(rules, run.Heroes[i]);
            return total;
        }

        // Steadying the party trades a supply room visit for a little shared clarity.
        public static double SupplyCalm(RunState run)
        {
            if (run == null) return 0;
            double before = run.PlayerSanity;
            run.PlayerSanity = SanityProbability.Clamp(before + CalmSanity, 0, 100);
            return run.PlayerSanity - before;
        }
    }

    // Battle scaling and the elite drop. Nothing here touches the catalog: every spec handed out is a
    // brand new ActorSpec, so a scaled elite never leaks back into the enemy asset.
    public static class Encounter
    {
        public static ActorSpec[] Scale(IList<ActorSpec> source, NodeType type, MapRules rules)
        {
            int count = source == null ? 0 : source.Count;
            var copy = new ActorSpec[count];
            var resolved = MapGenerator.Resolve(rules);
            for (int i = 0; i < count; i++) copy[i] = Copy(source[i], type, resolved);
            return copy;
        }

        private static ActorSpec Copy(ActorSpec source, NodeType type, MapRules rules)
        {
            if (source == null) return null;
            // The skill references stay shared on purpose: they are read only, and copying them would break
            // the identity comparisons the battle engine relies on.
            var spec = new ActorSpec
            {
                Id = source.Id, Name = source.Name, Subtitle = source.Subtitle, Silhouette = source.Silhouette,
                MaxHp = source.MaxHp, Speed = source.Speed, StartRank = source.StartRank, Attack = source.Attack,
                Accuracy = source.Accuracy, CritChance = source.CritChance, Dodge = source.Dodge,
                Defense = source.Defense, SanityOffset = source.SanityOffset, Pattern = source.Pattern,
                Behavior = source.Behavior, RankWidth = source.RankWidth, Immobile = source.Immobile,
                ActionInterval = source.ActionInterval, ActionPhase = source.ActionPhase,
                BossHealOnHeroDeath = source.BossHealOnHeroDeath, GuardReduction = source.GuardReduction,
                SummonPool = source.SummonPool,
                BasicSkills = source.BasicSkills, PrimalSkills = source.PrimalSkills, TechnicalSkills = source.TechnicalSkills
            };
            if (type == NodeType.EliteBattle)
            {
                spec.MaxHp = source.MaxHp + rules.EliteHealthBonus;
                spec.Attack = (int)Math.Round(source.Attack * rules.EliteAttackScale);
                spec.Name = "精英·" + source.Name;
            }
            else if (type == NodeType.Boss)
            {
                spec.MaxHp = source.MaxHp + rules.BossHealthBonus;
                spec.Attack = (int)Math.Round(source.Attack * rules.BossAttackScale);
                spec.Name = "首领·" + source.Name;
            }
            return spec;
        }

        // Supplies only ever come from a won fight. A retreat pays nothing.
        public static int SuppliesFor(MapRules rules, NodeType type)
        {
            var resolved = MapGenerator.Resolve(rules);
            switch (type)
            {
                case NodeType.Battle: return resolved.SuppliesPerBattle;
                case NodeType.EliteBattle: return resolved.SuppliesPerElite;
                case NodeType.Boss: return resolved.SuppliesPerBoss;
                default: return 0;
            }
        }

        // Elites are the only source of prosthetics today. Returns a fresh copy so the run stock never
        // aliases the shared table.
        public static ProstheticSpec DropProsthetic(LimbRules limbs, RunRandom random)
        {
            if (random == null) throw new ArgumentNullException("random");
            var pool = LimbSystem.Resolve(limbs).Prosthetics;
            if (pool == null || pool.Length == 0) return null;
            int index = (int)(random.NextUnit() * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            var pick = pool[index];
            return new ProstheticSpec(pick.Id, pick.Name, pick.Part, pick.Description);
        }
    }
}
