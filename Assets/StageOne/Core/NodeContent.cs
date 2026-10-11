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

    [Serializable]
    public sealed class EventOptionSpec
    {
        public string Label;
        public string Detail;
        public double SanityDelta;        // Applied to the shared player sanity.
        public int SuppliesDelta;
        public int HealPercent;           // Share of each living hero's own max health.
        public bool ClearMental;
        public double AppearanceBaseline; // 0 keeps the option hidden; 1 always shows it.
        public double AppearanceAffinity; // Lucid leans are positive, danger leans negative.
    }

    [Serializable]
    public sealed class EventSpec
    {
        public string Id;
        public string Title;
        public string Body;
        public NodeType Kind;             // Event or Altar: which pool the node draws its content from.
        public int Weight = 1;            // Relative pick weight inside the pool.
        public EventOptionSpec[] Options;
    }

    // Event and altar content. Altars reuse the event shape on purpose: the difference is the kind of
    // content, not the machinery. Option visibility uses the same continuous probability as the map, so
    // a low-sanity party keeps seeing the options a lucid party would not be offered.
    public static class Exploration
    {
        public const double CalmSanity = 10;

        public static readonly EventSpec[] DefaultEvents =
        {
            new EventSpec
            {
                Id = "drowned_archive", Title = "沉没的档案", Kind = NodeType.Event,
                Body = "半塌的档案库里，卷宗还粘在一起。油灯快灭了，你只来得及翻几页。",
                Options = new[]
                {
                    new EventOptionSpec { Label = "抄下能辨认的段落", Detail = "借着最后一口气，你记下了几行字。",
                        SanityDelta = 4, AppearanceBaseline = 1 },
                    new EventOptionSpec { Label = "把整摞卷宗背出去", Detail = "纸比石头还沉，但也许能换点东西。",
                        SanityDelta = -3, SuppliesDelta = 2, AppearanceBaseline = 0.55, AppearanceAffinity = 1.0 },
                    new EventOptionSpec { Label = "翻到最后一页", Detail = "最后一页写着今天的日期，和你的名字。",
                        SanityDelta = -8, SuppliesDelta = 4, AppearanceBaseline = 0.45, AppearanceAffinity = -1.0 }
                }
            },
            new EventSpec
            {
                Id = "dying_campfire", Title = "将熄的营火", Kind = NodeType.Event,
                Body = "一小堆营火在无风的通道里摇。柴是别人劈好的，人已经不在了。",
                Options = new[]
                {
                    new EventOptionSpec { Label = "坐下来烤一会儿火", Detail = "火不旺，但足够让手指重新有知觉。",
                        SanityDelta = 5, AppearanceBaseline = 1 },
                    new EventOptionSpec { Label = "把剩下的柴收进背包", Detail = "别人劈好的柴，别人用不上了。",
                        SanityDelta = -2, SuppliesDelta = 3, AppearanceBaseline = 0.5, AppearanceAffinity = 1.0 },
                    new EventOptionSpec { Label = "循着脚印追过去", Detail = "脚印比你的脚大一圈。",
                        SanityDelta = -4, SuppliesDelta = 5, AppearanceBaseline = 0.4, AppearanceAffinity = -1.0 }
                }
            },
            new EventSpec
            {
                Id = "quiet_altar", Title = "静默的祭坛", Kind = NodeType.Altar, Weight = 1,
                Body = "祭坛干净得不像这个深度该有的东西。石面上刻着一行小字：留下一点什么。",
                Options = new[]
                {
                    new EventOptionSpec { Label = "放下一段回忆", Detail = "你不记得放下了什么，只觉得轻了一点。",
                        SanityDelta = -6, SuppliesDelta = 5, AppearanceBaseline = 1 },
                    new EventOptionSpec { Label = "跪下静默", Detail = "石面很凉，脑子很静。",
                        SanityDelta = 8, ClearMental = true, AppearanceBaseline = 0.5, AppearanceAffinity = 1.0 },
                    new EventOptionSpec { Label = "把手按在石面上", Detail = "石头吸走了一部分你，还回来一点别的。",
                        SanityDelta = -10, SuppliesDelta = 9, HealPercent = 30, AppearanceBaseline = 0.35, AppearanceAffinity = -1.0 }
                }
            },
            new EventSpec
            {
                Id = "bone_altar", Title = "骨砌祭坛", Kind = NodeType.Altar, Weight = 1,
                Body = "骨头砌成的台子还带着体温，台面上有一个刚好放得下一只手的凹槽。",
                Options = new[]
                {
                    new EventOptionSpec { Label = "把手放进去", Detail = "凹槽刚好合手，像是为你留的。",
                        SanityDelta = -12, SuppliesDelta = 7, AppearanceBaseline = 1 },
                    new EventOptionSpec { Label = "只取走台边的供品", Detail = "拿别人的东西，手会抖。",
                        SanityDelta = -3, SuppliesDelta = 3, AppearanceBaseline = 0.5, AppearanceAffinity = 1.0 },
                    new EventOptionSpec { Label = "把祭坛拆了", Detail = "骨头比看上去脆。",
                        SanityDelta = -7, SuppliesDelta = 6, AppearanceBaseline = 0.35, AppearanceAffinity = -1.0 }
                }
            }
        };

        public static EventSpec[] Events(MapRules rules)
        {
            var source = rules == null ? null : rules.Events;
            return source != null && source.Length > 0 ? source : DefaultEvents;
        }

        public static EventSpec Find(MapRules rules, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = Events(rules);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Id == id) return all[i];
            return null;
        }

        public static List<EventSpec> Pool(MapRules rules, NodeType kind)
        {
            var pool = new List<EventSpec>();
            var all = Events(rules);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Kind == kind && all[i].Options != null && all[i].Options.Length > 0) pool.Add(all[i]);
            return pool;
        }

        // Rolls the content and freezes the option mask on the node, so re-rendering the page never
        // re-rolls it. Called once, when the party walks into the room.
        public static void RollEvent(MapRules rules, NodeState node, RunRandom random, double sanity, NodeType kind)
        {
            if (node == null) throw new ArgumentNullException("node");
            if (random == null) throw new ArgumentNullException("random");
            node.OptionChosen = -1;
            node.OptionMask = null;
            node.EventId = null;

            var pool = Pool(rules, kind);
            if (pool.Count == 0) return;
            var spec = pool[Pick(pool, random)];
            node.EventId = spec.Id;

            var mask = new bool[spec.Options.Length];
            mask[0] = true; // The base option is always on offer; the rest depend on the party's state.
            double strength = MapGenerator.Resolve(rules).ProbabilityStrength;
            for (int i = 1; i < spec.Options.Length; i++)
            {
                var option = spec.Options[i];
                mask[i] = SanityProbability.Check(option.AppearanceBaseline, option.AppearanceAffinity, sanity, random, strength);
            }
            node.OptionMask = mask;
        }

        // Returns false without touching anything when the option is hidden, already taken, or out of
        // range. A rejected option consumes nothing, so a misclick cannot move the random sequence.
        public static bool ApplyOption(MapRules rules, NodeState node, int optionIndex, RunState run, out string note)
        {
            note = null;
            if (node == null || run == null) return false;
            var spec = Find(rules, node.EventId);
            if (spec == null || spec.Options == null) return false;
            if (optionIndex < 0 || optionIndex >= spec.Options.Length) return false;
            if (node.OptionMask == null || optionIndex >= node.OptionMask.Length || !node.OptionMask[optionIndex]) return false;
            if (node.OptionChosen >= 0) return false;

            var option = spec.Options[optionIndex];
            var lines = new List<string>();

            if (option.SanityDelta != 0)
            {
                double before = run.PlayerSanity;
                run.PlayerSanity = SanityProbability.Clamp(before + option.SanityDelta, 0, 100);
                lines.Add("玩家理智 " + before.ToString("0.0") + " → " + run.PlayerSanity.ToString("0.0"));
            }
            if (option.SuppliesDelta != 0)
            {
                run.Map.Supplies = Math.Max(0, run.Map.Supplies + option.SuppliesDelta);
                lines.Add("补给 " + (option.SuppliesDelta > 0 ? "+" : "") + option.SuppliesDelta);
            }
            if (option.HealPercent > 0)
            {
                int healed = HealParty(run, option.HealPercent);
                lines.Add("治疗 " + healed + " 点生命");
            }
            if (option.ClearMental)
            {
                int cleared = 0;
                for (int i = 0; i < run.Heroes.Count; i++)
                {
                    if (run.Heroes[i].Mental == MentalState.None) continue;
                    run.Heroes[i].Mental = MentalState.None;
                    cleared++;
                }
                if (cleared > 0) lines.Add("解除 " + cleared + " 名队员的精神状态");
            }

            node.OptionChosen = optionIndex;
            note = lines.Count == 0 ? "什么也没有发生。" : string.Join("，", lines);
            return true;
        }

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

        private static int Pick(IList<EventSpec> pool, RunRandom random)
        {
            int total = 0;
            for (int i = 0; i < pool.Count; i++) total += Math.Max(0, pool[i].Weight);
            if (total <= 0) return 0;
            double pick = random.NextUnit() * total;
            double run = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                run += Math.Max(0, pool[i].Weight);
                if (pick < run) return i;
            }
            return pool.Count - 1;
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
                MaxHp = source.MaxHp, Speed = source.Speed, Attack = source.Attack,
                Accuracy = source.Accuracy, CritChance = source.CritChance, Dodge = source.Dodge,
                Defense = source.Defense, SanityOffset = source.SanityOffset, Pattern = source.Pattern,
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
