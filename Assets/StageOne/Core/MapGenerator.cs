using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // The only entry point for route generation. Every draw reads the player sanity (Run.PlayerSanity)
    // and uses SanityGate.None: the document keeps map and event probability continuous, without the
    // character-only 30/70 activation window.
    //
    // The route is a fixed skeleton -- every floor opens on a fixed room and ends on a fixed room -- with
    // a run of sampled positions in between. A position is only drawn when the party walks up to it, so
    // looking ahead is impossible and a position already on screen is never re-rolled.
    public static class MapGenerator
    {
        // Danger leans are deliberately smaller than the lucid leans, so low sanity stays risky but not
        // extreme: elite rooms plus altars reach about 54% of the table at sanity 15 and about 14% at 85.
        // The four older entries are scaled by 0.90 so the chest room takes the remaining 10% at sanity 50
        // without changing how the other four relate to each other. The table still sums to exactly 1.
        public static readonly NodeOddsSpec[] DefaultOdds =
        {
            new NodeOddsSpec(NodeType.Battle, 0.315, 0),
            new NodeOddsSpec(NodeType.EliteBattle, 0.135, -1.0),
            new NodeOddsSpec(NodeType.Altar, 0.180, -1.0),
            new NodeOddsSpec(NodeType.Event, 0.270, 1.0),
            new NodeOddsSpec(NodeType.Treasure, 0.100, 0)
        };

        public static readonly double[] DefaultBranchWeights = { 0.31, 0.38, 0.31 };
        public static readonly int[] DefaultSteps = { 5, 6, 7 };

        private const int DefaultFloors = 3;

        // Fills in anything a catalog left blank. Every GameCatalog asset predates these fields, so an
        // absent value arrives as zero and has to fall back to the rule default. Every field has to be
        // copied explicitly: this constructs a brand new MapRules, so a field left out is silently reset.
        public static MapRules Resolve(MapRules rules)
        {
            var source = rules ?? new MapRules();
            int floors = source.Floors > 0 ? source.Floors : DefaultFloors;
            return new MapRules
            {
                Floors = floors,
                StepsPerFloor = ResolveSteps(source.StepsPerFloor, floors),
                ProbabilityStrength = Positive(source.ProbabilityStrength) ? source.ProbabilityStrength : 1.0,
                BranchAffinity = Positive(source.BranchAffinity) ? source.BranchAffinity : 1.5,
                BranchWeights = Usable(source.BranchWeights) ? (double[])source.BranchWeights.Clone() : (double[])DefaultBranchWeights.Clone(),
                NodeOdds = Usable(source.NodeOdds) ? Clone(source.NodeOdds) : Clone(DefaultOdds),
                OpeningBuffs = Usable(source.OpeningBuffs) ? (GrowthBuff[])source.OpeningBuffs.Clone() : DefaultBuffs(),
                SupplyHealRatio = Positive(source.SupplyHealRatio) ? source.SupplyHealRatio : 0.35,
                SuppliesPerBattle = source.SuppliesPerBattle > 0 ? source.SuppliesPerBattle : 2,
                SuppliesPerElite = source.SuppliesPerElite > 0 ? source.SuppliesPerElite : 4,
                SuppliesPerBoss = source.SuppliesPerBoss > 0 ? source.SuppliesPerBoss : 8,
                EliteHealthBonus = source.EliteHealthBonus > 0 ? source.EliteHealthBonus : 20,
                EliteAttackScale = Positive(source.EliteAttackScale) ? source.EliteAttackScale : 1.25,
                BossHealthBonus = source.BossHealthBonus > 0 ? source.BossHealthBonus : 45,
                BossAttackScale = Positive(source.BossAttackScale) ? source.BossAttackScale : 1.5
            };
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool Positive(double value) { return Finite(value) && value > 0; }

        // One entry per floor. A short or damaged array keeps its usable entries and falls back on the
        // documented pattern for the rest, so a two-floor catalog still produces a sane third floor.
        private static int[] ResolveSteps(int[] source, int floors)
        {
            var copy = new int[floors];
            for (int i = 0; i < floors; i++)
            {
                bool usable = source != null && i < source.Length && source[i] > 0;
                copy[i] = usable ? source[i] : DefaultSteps[Math.Min(i, DefaultSteps.Length - 1)];
            }
            return copy;
        }

        private static GrowthBuff[] DefaultBuffs()
        {
            return new[] { GrowthBuff.Toughness, GrowthBuff.Sharpness, GrowthBuff.Clarity };
        }

        private static bool Usable(double[] values)
        {
            if (values == null || values.Length != 3) return false;
            double sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0 || !Finite(values[i])) return false;
                sum += values[i];
            }
            return sum > 0;
        }

        private static bool Usable(GrowthBuff[] values)
        {
            return values != null && values.Length > 0;
        }

        private static bool Usable(NodeOddsSpec[] values)
        {
            if (values == null || values.Length == 0) return false;
            double sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null) return false;
                if (values[i].Weight < 0 || !Finite(values[i].Weight)) return false;
                if (!Finite(values[i].Affinity)) return false;
                sum += values[i].Weight;
            }
            return sum > 0;
        }

        private static NodeOddsSpec[] Clone(NodeOddsSpec[] values)
        {
            var copy = new NodeOddsSpec[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = new NodeOddsSpec(values[i].Type, values[i].Weight, values[i].Affinity);
            return copy;
        }

        // Pure: consumes no draws, so the page can display odds as often as it re-renders.
        public static double[] TypeOdds(MapRules rules, double playerSanity)
        {
            var resolved = Resolve(rules);
            var outcomes = new WeightedOutcome[resolved.NodeOdds.Length];
            for (int i = 0; i < outcomes.Length; i++)
                outcomes[i] = new WeightedOutcome(resolved.NodeOdds[i].Weight, resolved.NodeOdds[i].Affinity);
            return SanityProbability.Distribution(playerSanity, outcomes, resolved.ProbabilityStrength);
        }

        // Pure. Index 0 is one candidate, index 2 is three candidates.
        public static double[] BranchOdds(MapRules rules, double playerSanity)
        {
            var resolved = Resolve(rules);
            double affinity = resolved.BranchAffinity;
            var outcomes = new[]
            {
                new WeightedOutcome(resolved.BranchWeights[0], -affinity),
                new WeightedOutcome(resolved.BranchWeights[1], 0),
                new WeightedOutcome(resolved.BranchWeights[2], affinity)
            };
            return SanityProbability.Distribution(playerSanity, outcomes, resolved.ProbabilityStrength);
        }

        public static double ExpectedBranches(MapRules rules, double playerSanity)
        {
            var odds = BranchOdds(rules, playerSanity);
            return odds[0] + 2 * odds[1] + 3 * odds[2];
        }

        // How many positions a floor holds: the fixed opening room, the sampled run, and the fixed last room.
        public static int StepsOnFloor(MapRules rules, int floor)
        {
            var resolved = Resolve(rules);
            int f = Math.Min(Math.Max(floor, 1), resolved.Floors);
            return resolved.StepsPerFloor[f - 1] + 2;
        }

        public static int TotalSteps(MapRules rules)
        {
            var resolved = Resolve(rules);
            int total = 0;
            for (int i = 0; i < resolved.Floors; i++) total += resolved.StepsPerFloor[i] + 2;
            return total;
        }

        // Walks up to the next position. A position that has already been shown is returned untouched and
        // consumes no draws, so re-rendering or reloading never re-rolls it.
        public static StepState Advance(MapState map, MapRules rules, RunRandom random, double playerSanity)
        {
            if (map == null) throw new ArgumentNullException("map");
            if (random == null) throw new ArgumentNullException("random");
            var resolved = Resolve(rules);
            if (map.Finished) return map.Current;
            if (map.StepIndex < 0) map.StepIndex = 0;

            var existing = map.Current;
            if (existing != null && existing.Shown) return existing;

            // A debug tool can jump straight to a later position. The skipped positions are still built in
            // order so the random sequence evolves exactly as it would have if the party had walked it.
            while (map.Steps.Count < map.StepIndex) map.Steps.Add(Build(map.Steps.Count, resolved, random, playerSanity));

            var step = Build(map.StepIndex, resolved, random, playerSanity);
            if (map.Steps.Count == map.StepIndex) map.Steps.Add(step);
            else map.Steps[map.StepIndex] = step;
            map.Floor = step.Floor;
            return step;
        }

        // Fixed positions draw nothing at all; a sampled position draws its candidate count and then one
        // type per candidate. That count is the documented draw contract and the tests pin it down.
        private static StepState Build(int stepIndex, MapRules rules, RunRandom random, double sanity)
        {
            int floor, index;
            Position(rules, stepIndex, out floor, out index);
            int span = rules.StepsPerFloor[floor - 1] + 2;
            var step = new StepState { Floor = floor, Index = index, Fixed = index == 0 || index == span - 1 };

            if (index == 0)
            {
                step.Candidates.Add(Create(floor, index, floor == 1 ? NodeType.Opening : NodeType.Supply, 0));
            }
            else if (index == span - 1)
            {
                step.Candidates.Add(Create(floor, index, floor >= rules.Floors ? NodeType.Boss : NodeType.Elevator, 0));
            }
            else
            {
                int count = SanityProbability.Sample(BranchOdds(rules, sanity), random) + 1;
                var odds = TypeOdds(rules, sanity);
                var taken = new bool[odds.Length];
                for (int i = 0; i < count; i++)
                {
                    int drawn = SanityProbability.Sample(SpokenFor(odds, rules.NodeOdds, taken), random);
                    step.Candidates.Add(Create(floor, index, rules.NodeOdds[drawn].Type, i));
                    taken[drawn] = true;
                }
            }

            step.Shown = true;
            return step;
        }

        // One candidate per room type, except event rooms: two events side by side read as two leads, so
        // they are allowed to repeat. Masking instead of rejection sampling keeps the draw contract at
        // exactly one draw per candidate, which the tests and the document both pin down.
        private static double[] SpokenFor(double[] odds, NodeOddsSpec[] specs, bool[] taken)
        {
            var masked = new double[odds.Length];
            double sum = 0;
            for (int i = 0; i < odds.Length; i++)
            {
                bool blocked = taken[i] && specs[i].Type != NodeType.Event;
                if (!blocked) { masked[i] = odds[i]; sum += odds[i]; }
            }
            // Fewer sampleable types than candidates: let the table repeat rather than hand Sample a row
            // with nothing left to pick.
            if (sum <= 0) return odds;
            for (int i = 0; i < masked.Length; i++) masked[i] /= sum;
            return masked;
        }

        // Turns a linear step index into the floor it belongs to and the position inside that floor.
        private static void Position(MapRules rules, int stepIndex, out int floor, out int index)
        {
            int remaining = Math.Max(stepIndex, 0);
            for (int f = 1; f <= rules.Floors; f++)
            {
                int span = rules.StepsPerFloor[f - 1] + 2;
                if (remaining < span) { floor = f; index = remaining; return; }
                remaining -= span;
            }
            floor = rules.Floors;
            index = rules.StepsPerFloor[floor - 1] + 1; // Clamp to the last position of the last floor.
        }

        private static NodeState Create(int floor, int index, NodeType type, int optionIndex)
        {
            return new NodeState { Floor = floor, Index = index, Type = type, OptionIndex = optionIndex };
        }

        // Battles, the boss and every room page are implemented now; nothing is skipped any more.
        public static bool IsCombat(NodeType type)
        {
            return type == NodeType.Battle || type == NodeType.EliteBattle || type == NodeType.Boss;
        }

        public static string Name(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle: return "普通战斗";
                case NodeType.EliteBattle: return "精英战斗";
                case NodeType.Event: return "事件房间";
                case NodeType.Boss: return "首领";
                case NodeType.Supply: return "补给站";
                case NodeType.Altar: return "祭坛房间";
                case NodeType.Elevator: return "电梯井";
                case NodeType.Opening: return "初始增益";
                case NodeType.Treasure: return "宝箱房";
                default: return "未知";
            }
        }
    }
}
