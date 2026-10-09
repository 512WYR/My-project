using System;

namespace Ruin.Core
{
    // The only entry point for node generation. Every draw reads the player sanity (Run.PlayerSanity)
    // and uses SanityGate.None: the document keeps map and event probability continuous, without the
    // character-only 30/70 activation window.
    public static class MapGenerator
    {
        // Danger leans are deliberately smaller than supply leans, so low sanity stays risky but not extreme.
        public static readonly NodeOddsSpec[] DefaultOdds =
        {
            new NodeOddsSpec(NodeType.Battle, 0.28, -0.5),
            new NodeOddsSpec(NodeType.EliteBattle, 0.12, -0.5),
            new NodeOddsSpec(NodeType.Event, 0.20, 0),
            new NodeOddsSpec(NodeType.Merchant, 0.14, 1.0),
            new NodeOddsSpec(NodeType.Camp, 0.14, 1.0),
            new NodeOddsSpec(NodeType.Workshop, 0.12, 1.0)
        };

        public static readonly double[] DefaultBranchWeights = { 0.15, 0.60, 0.25 };

        // Fills in anything a catalog left blank. Every GameCatalog asset predates these fields, so an
        // absent value arrives as zero and has to fall back to the rule default.
        public static MapRules Resolve(MapRules rules)
        {
            var source = rules ?? new MapRules();
            var resolved = new MapRules
            {
                Floors = source.Floors > 0 ? source.Floors : 10,
                ProbabilityStrength = Finite(source.ProbabilityStrength) && source.ProbabilityStrength > 0
                    ? source.ProbabilityStrength : 1.0,
                OpeningBranchCount = source.OpeningBranchCount > 0 ? Math.Min(source.OpeningBranchCount, 3) : 3,
                GuaranteedBattleIndex = source.GuaranteedBattleIndex,
                BranchAffinity = Finite(source.BranchAffinity) && source.BranchAffinity > 0
                    ? source.BranchAffinity : 1.0,
                BranchWeights = Usable(source.BranchWeights) ? (double[])source.BranchWeights.Clone() : (double[])DefaultBranchWeights.Clone(),
                NodeOdds = Usable(source.NodeOdds) ? Clone(source.NodeOdds) : Clone(DefaultOdds)
            };
            return resolved;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool Usable(double[] values)
        {
            if (values == null || values.Length != 3) return false;
            double sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0 || double.IsNaN(values[i]) || double.IsInfinity(values[i])) return false;
                sum += values[i];
            }
            return sum > 0;
        }

        private static bool Usable(NodeOddsSpec[] values)
        {
            if (values == null || values.Length == 0) return false;
            double sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null) return false;
                if (values[i].Weight < 0 || double.IsNaN(values[i].Weight) || double.IsInfinity(values[i].Weight)) return false;
                if (double.IsNaN(values[i].Affinity) || double.IsInfinity(values[i].Affinity)) return false;
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

        // Pure. Index 0 is one branch, index 2 is three branches.
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

        // Idempotent: a floor that has already been shown is returned untouched and consumes no draws,
        // so re-rendering or reloading never re-rolls it.
        public static FloorState EnsureFloor(MapState map, MapRules rules, RunRandom random, double playerSanity)
        {
            if (map == null) throw new ArgumentNullException("map");
            if (random == null) throw new ArgumentNullException("random");
            var existing = map.FloorAt(map.Floor);
            if (existing != null) return existing;
            var created = Generate(map.Floor, rules, random, playerSanity);
            map.Floors.Add(created);
            return created;
        }

        public static FloorState Generate(int floor, MapRules rules, RunRandom random, double playerSanity)
        {
            if (random == null) throw new ArgumentNullException("random");
            var resolved = Resolve(rules);
            var state = new FloorState { Floor = floor };

            if (floor >= resolved.Floors)
            {
                state.Nodes.Add(Create(floor, 0, NodeType.Boss));
                return state;
            }

            int count = floor <= 1
                ? resolved.OpeningBranchCount
                : SanityProbability.Sample(BranchOdds(resolved, playerSanity), random) + 1;

            var typeOdds = TypeOdds(resolved, playerSanity);
            for (int index = 0; index < count; index++)
            {
                // The spine branch is fixed and draws nothing, which also keeps at least one playable
                // node on every floor.
                var type = index == resolved.GuaranteedBattleIndex
                    ? NodeType.Battle
                    : resolved.NodeOdds[SanityProbability.Sample(typeOdds, random)].Type;
                state.Nodes.Add(Create(floor, index, type));
            }
            return state;
        }

        private static NodeState Create(int floor, int index, NodeType type)
        {
            return new NodeState { Floor = floor, Index = index, Type = type, Placeholder = IsPlaceholder(type) };
        }

        // Only plain battles have content behind them today. Everything else is a clearly marked
        // placeholder that resolves by being skipped.
        public static bool IsPlaceholder(NodeType type) { return type != NodeType.Battle; }

        public static bool IsCombat(NodeType type) { return type == NodeType.Battle || type == NodeType.EliteBattle; }

        public static string Name(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle: return "普通战斗";
                case NodeType.EliteBattle: return "精英战斗";
                case NodeType.Event: return "事件";
                case NodeType.Merchant: return "商人";
                case NodeType.Camp: return "营地";
                case NodeType.Workshop: return "工坊";
                case NodeType.Boss: return "首领";
                default: return "未知";
            }
        }
    }
}
