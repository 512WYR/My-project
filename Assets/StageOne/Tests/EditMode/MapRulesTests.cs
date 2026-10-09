using System;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 探索节点生成规则：玩家理智影响房间类型与分支数量，不使用角色30/70区间门槛。
    public class MapRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private MapRules Rules() { return catalog.MapRules(); }
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity); }

        private static double Share(MapRules rules, double sanity, params NodeType[] types)
        {
            var specs = MapGenerator.Resolve(rules).NodeOdds;
            var odds = MapGenerator.TypeOdds(rules, sanity);
            double sum = 0;
            for (int i = 0; i < specs.Length; i++) if (types.Contains(specs[i].Type)) sum += odds[i];
            return sum;
        }

        private static uint Drained(uint state, int draws) { var r = new RunRandom(state); for (int i = 0; i < draws; i++) r.NextUnit(); return r.State; }

        [Test] public void CatalogFloorsSurviveAnOlderAsset()
        {
            var rules = Rules();
            Assert.AreEqual(10, rules.Floors); Assert.AreEqual(3, rules.OpeningBranchCount);
            Assert.AreEqual(10, MapGenerator.Resolve(null).Floors, "缺失或损坏的规则要回落到默认值");
        }

        [Test] public void TypeOddsSumToOneAndFollowSanity()
        {
            var rules = Rules();
            foreach (double sanity in new[] { 15.0, 50, 85 })
            {
                var odds = MapGenerator.TypeOdds(rules, sanity);
                Assert.That(odds.Sum(), Is.EqualTo(1).Within(1e-12));
                Assert.That(odds.All(p => p >= 0 && p <= 1));
            }
            Assert.That(Share(rules, 15, NodeType.Merchant, NodeType.Camp, NodeType.Workshop),
                Is.LessThan(Share(rules, 50, NodeType.Merchant, NodeType.Camp, NodeType.Workshop)));
            Assert.That(Share(rules, 50, NodeType.Merchant, NodeType.Camp, NodeType.Workshop),
                Is.LessThan(Share(rules, 85, NodeType.Merchant, NodeType.Camp, NodeType.Workshop)));
            Assert.That(Share(rules, 15, NodeType.Battle, NodeType.EliteBattle),
                Is.GreaterThan(Share(rules, 85, NodeType.Battle, NodeType.EliteBattle)));
        }

        [Test] public void ReferenceOddsMatchTheDocumentedTable()
        {
            var rules = Rules();
            Assert.That(Share(rules, 15, NodeType.Battle), Is.EqualTo(0.411213).Within(0.0005));
            Assert.That(Share(rules, 50, NodeType.Battle), Is.EqualTo(0.28).Within(0.0005));
            Assert.That(Share(rules, 85, NodeType.Merchant), Is.EqualTo(0.218991).Within(0.0005));
            // 低理智的危险偏向必须收敛：战斗类从 40% 提高到约 59%，不更高。
            Assert.That(Share(rules, 15, NodeType.Battle, NodeType.EliteBattle), Is.EqualTo(0.587446).Within(0.002));
            Assert.That(Share(rules, 15, NodeType.Merchant, NodeType.Camp, NodeType.Workshop), Is.EqualTo(0.205577).Within(0.002));
        }

        [Test] public void OddsDoNotUseTheCharacterActivationGate()
        {
            var rules = Rules();
            foreach (double sanity in new[] { 0.0, 15, 29.99, 70.01, 85, 100 })
                Assert.That(MapGenerator.TypeOdds(rules, sanity).All(p => p > 0), "探索概率随理智连续变化，不受30/70门槛限制");
        }

        [Test] public void BranchCountGrowsWithSanity()
        {
            var rules = Rules();
            double low = MapGenerator.ExpectedBranches(rules, 15), mid = MapGenerator.ExpectedBranches(rules, 50), high = MapGenerator.ExpectedBranches(rules, 85);
            Assert.That(low, Is.LessThan(mid)); Assert.That(mid, Is.LessThan(high));
            Assert.That(low, Is.EqualTo(1.826639).Within(0.001));
            Assert.That(mid, Is.EqualTo(2.10).Within(0.001));
            Assert.That(high, Is.EqualTo(2.364151).Within(0.001));
            foreach (double sanity in new[] { 0.0, 50, 100 })
                Assert.That(MapGenerator.BranchOdds(rules, sanity).Sum(), Is.EqualTo(1).Within(1e-12));
        }

        [Test] public void OpeningFloorHasThreeRoomsAndBossFloorHasOne()
        {
            foreach (double sanity in new[] { 15.0, 50, 85 })
            {
                var run = Run(sanity);
                var opening = MapGenerator.Generate(1, Rules(), run.Random, sanity);
                Assert.AreEqual(3, opening.Nodes.Count, "开局固定三个房间供选择");
                var boss = MapGenerator.Generate(Rules().Floors, Rules(), run.Random, sanity);
                Assert.AreEqual(1, boss.Nodes.Count);
                Assert.AreEqual(NodeType.Boss, boss.Nodes[0].Type);
            }
        }

        [Test] public void EveryFloorKeepsPlayableBattlePath()
        {
            var run = Run(50);
            for (int floor = 1; floor < Rules().Floors; floor++)
            {
                var generated = MapGenerator.Generate(floor, Rules(), run.Random, run.PlayerSanity);
                Assert.AreEqual(NodeType.Battle, generated.Nodes[0].Type, "第 " + floor + " 层缺少可玩的普通战斗分支");
                Assert.IsFalse(generated.Nodes[0].Placeholder);
            }
        }

        [Test] public void SameSeedProducesTheSameFloors()
        {
            var a = Run(50); var b = Run(50);
            for (int floor = 1; floor <= Rules().Floors; floor++)
            {
                var x = MapGenerator.Generate(floor, Rules(), a.Random, a.PlayerSanity);
                var y = MapGenerator.Generate(floor, Rules(), b.Random, b.PlayerSanity);
                Assert.AreEqual(x.Nodes.Count, y.Nodes.Count);
                Assert.That(y.Nodes.Select(n => n.Type), Is.EqualTo(x.Nodes.Select(n => n.Type)));
            }
        }

        [Test] public void ShownFloorIsNeverRerolled()
        {
            var run = Run(50);
            var first = MapGenerator.EnsureFloor(run.Map, Rules(), run.Random, run.PlayerSanity);
            uint state = run.Random.State;
            // 即使玩家理智已经变化，已经展示的层也必须保持原样。
            var again = MapGenerator.EnsureFloor(run.Map, Rules(), run.Random, 15);
            Assert.AreSame(first, again);
            Assert.AreEqual(state, run.Random.State, "已展示的层不消耗随机序列");
            Assert.That(again.Nodes.Select(n => n.Type), Is.EqualTo(first.Nodes.Select(n => n.Type)));
            Assert.AreEqual(1, run.Map.Floors.Count);
        }

        [Test] public void DrawCountContract()
        {
            var rules = Rules();
            var opening = Run(50);
            uint openingStart = opening.Random.State;
            var openingFloor = MapGenerator.Generate(1, rules, opening.Random, 50);
            Assert.AreEqual(3, openingFloor.Nodes.Count);
            Assert.AreEqual(Drained(openingStart, 2), opening.Random.State, "开局不抽分支数量，只抽两个非骨架房间");

            var middle = Run(50);
            uint middleStart = middle.Random.State;
            var middleFloor = MapGenerator.Generate(4, rules, middle.Random, 50);
            Assert.That(middleFloor.Nodes.Count, Is.InRange(1, 3));
            Assert.AreEqual(Drained(middleStart, middleFloor.Nodes.Count), middle.Random.State, "其余层先抽分支数量，再抽每个非骨架房间");

            var boss = Run(50);
            uint bossStart = boss.Random.State;
            var bossFloor = MapGenerator.Generate(rules.Floors, rules, boss.Random, 50);
            Assert.AreEqual(NodeType.Boss, bossFloor.Nodes[0].Type);
            Assert.AreEqual(Drained(bossStart, 0), boss.Random.State, "首领层不消耗随机序列");
        }

        [Test] public void PlaceholderFlagsMatchImplementation()
        {
            foreach (NodeType type in Enum.GetValues(typeof(NodeType)))
                Assert.AreEqual(type != NodeType.Battle, MapGenerator.IsPlaceholder(type), type.ToString());
            Assert.AreEqual("普通战斗", MapGenerator.Name(NodeType.Battle));
            Assert.AreEqual("首领", MapGenerator.Name(NodeType.Boss));
        }

        [Test] public void ResolveRepairsBrokenWeights()
        {
            var broken = new MapRules { Floors = 0, ProbabilityStrength = 0, OpeningBranchCount = 0, BranchAffinity = 0, BranchWeights = new double[0], NodeOdds = new NodeOddsSpec[0] };
            var fixedRules = MapGenerator.Resolve(broken);
            Assert.AreEqual(10, fixedRules.Floors);
            Assert.AreEqual(1.0, fixedRules.ProbabilityStrength);
            Assert.AreEqual(3, fixedRules.OpeningBranchCount);
            Assert.AreEqual(6, fixedRules.NodeOdds.Length);
            Assert.That(MapGenerator.TypeOdds(fixedRules, 50).Sum(), Is.EqualTo(1).Within(1e-12));
        }
    }
}
