using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 路线生成规则：3 层固定套路，层内房间走到跟前才抽；玩家理智同时影响候选数量与房间类型，
    // 不使用角色 30/70 区间门槛。
    public class MapRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private MapRules Rules() { return catalog.MapRules(); }
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity, catalog.Rules()); }

        private static double Share(MapRules rules, double sanity, params NodeType[] types)
        {
            var specs = MapGenerator.Resolve(rules).NodeOdds;
            var odds = MapGenerator.TypeOdds(rules, sanity);
            double sum = 0;
            for (int i = 0; i < specs.Length; i++) if (types.Contains(specs[i].Type)) sum += odds[i];
            return sum;
        }

        private static uint Drained(uint state, int draws) { var r = new RunRandom(state); for (int i = 0; i < draws; i++) r.NextUnit(); return r.State; }

        // 某个房间类型在给定理智下的概率；表里没有这一项时返回 0。
        private static double OddsOf(MapRules rules, double sanity, NodeType type)
        {
            var specs = MapGenerator.Resolve(rules).NodeOdds;
            var odds = MapGenerator.TypeOdds(rules, sanity);
            for (int i = 0; i < specs.Length; i++) if (specs[i].Type == type) return odds[i];
            return 0;
        }

        // 整条路线走一遍：每一步都是按当前位置抽出来的，返回 (层, 层内位置, 步) 的序列。
        private static List<StepState> Walk(RunState run, MapRules rules)
        {
            var path = new List<StepState>();
            for (int i = 0; i < MapGenerator.TotalSteps(rules); i++)
            {
                run.Map.StepIndex = i;
                path.Add(MapGenerator.Advance(run.Map, rules, run.Random, run.PlayerSanity));
            }
            return path;
        }

        [Test] public void CatalogSurvivesAnOlderAsset()
        {
            var rules = Rules();
            Assert.AreEqual(3, rules.Floors, "路线共三层");
            Assert.That(rules.StepsPerFloor, Is.EqualTo(new[] { 5, 6, 7 }), "每层的可抽位置数是 5 / 6 / 7");
            var fallback = MapGenerator.Resolve(null);
            Assert.AreEqual(3, fallback.Floors, "缺失或损坏的规则要回落到默认值");
            Assert.That(fallback.StepsPerFloor, Is.EqualTo(new[] { 5, 6, 7 }));
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
            // 危险偏向收敛：精英与祭坛随理智下降而减少，事件随理智上升而增加。
            Assert.That(Share(rules, 15, NodeType.EliteBattle, NodeType.Altar),
                Is.GreaterThan(Share(rules, 50, NodeType.EliteBattle, NodeType.Altar)));
            Assert.That(Share(rules, 50, NodeType.EliteBattle, NodeType.Altar),
                Is.GreaterThan(Share(rules, 85, NodeType.EliteBattle, NodeType.Altar)));
            Assert.That(Share(rules, 15, NodeType.Event), Is.LessThan(Share(rules, 85, NodeType.Event)));
        }

        [Test] public void ReferenceOddsMatchTheDocumentedTable()
        {
            // 五项权重表的精确数值由复现工程 _ruincheck 生成，文档里的百分比是同一组数。
            var rules = Rules();
            Assert.That(Share(rules, 15, NodeType.Battle), Is.EqualTo(0.266180).Within(0.0005));
            Assert.That(Share(rules, 15, NodeType.EliteBattle), Is.EqualTo(0.229723).Within(0.0005));
            Assert.That(Share(rules, 15, NodeType.Altar), Is.EqualTo(0.306297).Within(0.0005));
            Assert.That(Share(rules, 15, NodeType.Event), Is.EqualTo(0.113298).Within(0.0005));
            Assert.That(Share(rules, 15, NodeType.Treasure), Is.EqualTo(0.084502).Within(0.0005));
            Assert.That(Share(rules, 50, NodeType.Battle), Is.EqualTo(0.315).Within(0.0005), "理智50时回到基础权重");
            Assert.That(Share(rules, 50, NodeType.EliteBattle), Is.EqualTo(0.135).Within(0.0005));
            Assert.That(Share(rules, 50, NodeType.Altar), Is.EqualTo(0.180).Within(0.0005));
            Assert.That(Share(rules, 50, NodeType.Event), Is.EqualTo(0.270).Within(0.0005));
            Assert.That(Share(rules, 50, NodeType.Treasure), Is.EqualTo(0.100).Within(0.0005));
            Assert.That(Share(rules, 85, NodeType.Event), Is.EqualTo(0.487575).Within(0.0005));
            Assert.That(Share(rules, 85, NodeType.EliteBattle), Is.EqualTo(0.060117).Within(0.0005));
            // 宝箱房不带倾向，只随归一化浮动：理智15 是 8.45%，理智85 是 8.97%。
            Assert.That(Share(rules, 85, NodeType.Treasure), Is.EqualTo(0.089675).Within(0.0005));
            // 低理智的危险偏向必须收敛：精英加祭坛约 53.6%，不更高。
            Assert.That(Share(rules, 15, NodeType.EliteBattle, NodeType.Altar), Is.EqualTo(0.536021).Within(0.002));
            Assert.That(Share(rules, 50, NodeType.EliteBattle, NodeType.Altar), Is.EqualTo(0.315).Within(0.002));
            Assert.That(Share(rules, 85, NodeType.EliteBattle, NodeType.Altar), Is.EqualTo(0.140274).Within(0.002));
        }

        [Test] public void OddsDoNotUseTheCharacterActivationGate()
        {
            var rules = Rules();
            // 角色形态在 30 与 70 处跳变，探索概率不能：越过这两个点只允许连续的小幅变化。
            foreach (double gate in new[] { 30.0, 70.0 })
            {
                var below = MapGenerator.TypeOdds(rules, gate - 0.01);
                var above = MapGenerator.TypeOdds(rules, gate + 0.01);
                var specs = MapGenerator.Resolve(rules).NodeOdds;
                for (int i = 0; i < below.Length; i++)
                {
                    Assert.That(Math.Abs(below[i] - above[i]), Is.LessThan(0.002), gate + " 附近没有门槛跳变");
                    // 万一有人在 Inspector 里把某项权重归零，恒为 0 的那一项不算在连续性检查里。
                    if (specs[i].Weight <= 0) { Assert.AreEqual(0, below[i]); continue; }
                    Assert.That(below[i], Is.GreaterThan(0), "探索概率随理智连续变化，不受 30/70 门槛限制");
                }
            }
            Assert.That(MapGenerator.TypeOdds(rules, 0).All(p => p >= 0));
            var specs100 = MapGenerator.Resolve(rules).NodeOdds;
            var at100 = MapGenerator.TypeOdds(rules, 100);
            for (int i = 0; i < at100.Length; i++)
                Assert.That(at100[i], specs100[i].Weight > 0 ? Is.GreaterThan(0) : Is.EqualTo(0),
                    "理智拉满时有权重的类型都还有机会，没权重的仍然是 0");
        }

        [Test] public void BranchCountGrowsWithSanity()
        {
            // 期望候选数的验收点是 20/50/80 → 1.50 / 2.00 / 2.50。50 处等于 2 是结构性的：
            // 「1 个」与「3 个」的倾向对称，任何权重下这一点的期望都恰好是 2。
            var rules = Rules();
            Assert.That(MapGenerator.ExpectedBranches(rules, 20), Is.EqualTo(1.50).Within(0.01));
            Assert.That(MapGenerator.ExpectedBranches(rules, 50), Is.EqualTo(2.00).Within(0.01));
            Assert.That(MapGenerator.ExpectedBranches(rules, 80), Is.EqualTo(2.50).Within(0.01));
            // 倾向强度从 1.0 提到 1.5 换来的：理智50 时「2 个」是最常见的一档，不是最少的。
            var mid = MapGenerator.BranchOdds(rules, 50);
            Assert.That(mid[1], Is.GreaterThan(mid[0]));
            Assert.That(mid[1], Is.GreaterThan(mid[2]));
            foreach (double sanity in new[] { 0.0, 50, 100 })
                Assert.That(MapGenerator.BranchOdds(rules, sanity).Sum(), Is.EqualTo(1).Within(1e-12));
            double previous = double.MinValue;
            for (double sanity = 0; sanity <= 50; sanity += 10)
            {
                double now = MapGenerator.ExpectedBranches(rules, sanity);
                Assert.That(now, Is.GreaterThan(previous), "候选数量的期望随理智单调上升");
                previous = now;
            }
        }

        [Test] public void RouteIsAThreeFloorSkeleton()
        {
            var rules = Rules();
            Assert.AreEqual(7, MapGenerator.StepsOnFloor(rules, 1), "第1层：初始增益 + 5 个可抽位置 + 电梯井");
            Assert.AreEqual(8, MapGenerator.StepsOnFloor(rules, 2), "第2层：补给站 + 6 个可抽位置 + 电梯井");
            Assert.AreEqual(9, MapGenerator.StepsOnFloor(rules, 3), "第3层：补给站 + 7 个可抽位置 + 首领");
            Assert.AreEqual(24, MapGenerator.TotalSteps(rules));

            var path = Walk(Run(50), rules);
            Assert.AreEqual(24, path.Count);
            for (int floor = 1; floor <= 3; floor++)
            {
                var on = path.Where(s => s.Floor == floor).ToList();
                Assert.AreEqual(MapGenerator.StepsOnFloor(rules, floor), on.Count);
                Assert.AreEqual(0, on[0].Index);
                Assert.AreEqual(on.Count - 1, on[on.Count - 1].Index);
                Assert.AreEqual(floor == 1 ? NodeType.Opening : NodeType.Supply, on[0].Candidates[0].Type, "每层的首节点是固定房间");
                Assert.AreEqual(floor == 3 ? NodeType.Boss : NodeType.Elevator, on[on.Count - 1].Candidates[0].Type, "每层的末节点是固定房间");
                Assert.AreEqual(2, on.Count(s => s.Fixed), "每层恰好两个固定位置");
                // 从权重表现读允许的类型，改表就不会悄悄让这条断言失效。
                Assert.That(on.Where(s => !s.Fixed).All(s => s.Candidates.All(c => Weighted.Contains(c.Type))),
                    "可抽位置只会出现有权重的房间");
                Assert.That(on.Where(s => !s.Fixed).All(s => s.Candidates.Count >= 1 && s.Candidates.Count <= 3), "候选数量恒为 1 到 3 个");
            }
        }

        // 有正权重的房间类型。分布本身由 ReferenceOddsMatchTheDocumentedTable 钉住，这里只用来回答
        // 「哪些类型可以出现在可抽位置上」。
        private static readonly NodeType[] Weighted =
        {
            NodeType.Battle, NodeType.EliteBattle, NodeType.Altar, NodeType.Event, NodeType.Treasure
        };

        // 同一个位置上的候选不会重复同一个房间类型 —— 事件房间除外：两个事件并排就是两条线索，
        // 允许成对出现。抽取用掩码而不是重抽，所以抽取次数契约仍然成立。
        [Test] public void ACandidateSetNeverRepeatsARoomTypeExceptEvents()
        {
            var rules = Rules();
            int paired = 0;
            for (uint seed = 1; seed <= 200; seed++)
            {
                foreach (var step in Walk(Run(50, seed), rules).Where(s => !s.Fixed))
                {
                    var types = step.Candidates.Select(c => c.Type).Where(t => t != NodeType.Event).ToList();
                    Assert.That(types.Distinct().Count(), Is.EqualTo(types.Count), "非事件房间在同一个位置不能重复");
                    if (step.Candidates.Count(c => c.Type == NodeType.Event) > 1) paired++;
                }
            }
            Assert.That(paired, Is.GreaterThan(0), "事件房间应当允许成对出现");
        }

        // 可抽类型不够填满候选数时，掩码会退回整张表让类型重复，而不是把手上一张空表交给抽取。
        [Test] public void AShortTableFallsBackToRepeatsInsteadOfStalling()
        {
            var rules = MapGenerator.Resolve(new MapRules
            {
                NodeOdds = new[]
                {
                    new NodeOddsSpec(NodeType.Battle, 0.5, 0),
                    new NodeOddsSpec(NodeType.Treasure, 0.5, 0)
                }
            });
            var path = Walk(Run(50), rules).Where(s => !s.Fixed).ToList();
            Assert.That(path.All(s => s.Candidates.All(c => c.Type == NodeType.Battle || c.Type == NodeType.Treasure)));
            Assert.That(path.Any(s => s.Candidates.Count > 2), "只有两种房间时仍然能抽出三个候选");
        }

        [Test] public void FixedPositionsDrawNothingOnDemand()
        {
            var rules = Rules();
            var run = Run(50);
            for (int i = 0; i < MapGenerator.TotalSteps(rules); i++)
            {
                uint before = run.Random.State;
                run.Map.StepIndex = i;
                var step = MapGenerator.Advance(run.Map, rules, run.Random, run.PlayerSanity);
                int draws = step.Fixed ? 0 : step.Candidates.Count + 1;
                Assert.AreEqual(Drained(before, draws), run.Random.State,
                    "第 " + step.Floor + " 层第 " + step.Index + " 个位置的抽取次数应当是 " + draws);
            }
        }

        [Test] public void ShownPositionIsNeverRerolled()
        {
            var run = Run(50);
            var rules = Rules();
            var first = MapGenerator.Advance(run.Map, rules, run.Random, run.PlayerSanity);
            uint state = run.Random.State;
            var types = first.Candidates.Select(n => n.Type).ToArray();
            // 即使玩家理智已经变化，已经抽出来的位置也必须保持原样，而且不消耗随机序列。
            var again = MapGenerator.Advance(run.Map, rules, run.Random, 15);
            Assert.AreSame(first, again);
            Assert.AreEqual(state, run.Random.State, "已抽出的位置不消耗随机序列");
            Assert.That(again.Candidates.Select(n => n.Type), Is.EqualTo(types));
        }

        [Test] public void SameSeedProducesTheSameRoute()
        {
            var rules = Rules();
            var a = Walk(Run(50), rules);
            var b = Walk(Run(50), rules);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Floor, b[i].Floor);
                Assert.AreEqual(a[i].Index, b[i].Index);
                Assert.That(b[i].Candidates.Select(n => n.Type), Is.EqualTo(a[i].Candidates.Select(n => n.Type)));
            }
        }

        [Test] public void ADebugJumpReproducesTheWalkedRoute()
        {
            var rules = Rules();
            var walked = Walk(Run(50), rules);

            // 直接跳到第 3 层：中间被跳过的位置仍按顺序补齐，随机序列与走完全程一致。
            var jumped = Run(50);
            int start = MapGenerator.StepsOnFloor(rules, 1) + MapGenerator.StepsOnFloor(rules, 2);
            jumped.Map.StepIndex = start;
            var step = MapGenerator.Advance(jumped.Map, rules, jumped.Random, jumped.PlayerSanity);
            Assert.AreEqual(start + 1, jumped.Map.Steps.Count, "被跳过的位置按顺序补齐");
            Assert.AreEqual(walked[start].Candidates[0].Type, step.Candidates[0].Type);
            Assert.AreEqual(walked[walked.Count - 1].Candidates[0].Type, NodeType.Boss);
        }

        [Test] public void TheFiveSampleableRoomsHoldWeight()
        {
            var rules = MapGenerator.Resolve(Rules());
            Assert.AreEqual(5, rules.NodeOdds.Length, "表里登记了五种可抽房间");
            Assert.That(rules.NodeOdds.Select(s => s.Type), Is.Unique);
            Assert.That(rules.NodeOdds.Where(s => s.Weight > 0).Select(s => s.Type),
                Is.EquivalentTo(Weighted), "这五种都有正权重");
            Assert.That(rules.NodeOdds.Sum(s => s.Weight), Is.EqualTo(1).Within(1e-9), "基础权重合计为 1");
            Assert.AreEqual(0.10, OddsOf(rules, 50, NodeType.Treasure), "宝箱房占 10%");
            Assert.AreEqual("初始增益", MapGenerator.Name(NodeType.Opening));
            Assert.AreEqual("祭坛房间", MapGenerator.Name(NodeType.Altar));
            Assert.AreEqual("电梯井", MapGenerator.Name(NodeType.Elevator));
            Assert.AreEqual("补给站", MapGenerator.Name(NodeType.Supply));
            Assert.AreEqual("首领", MapGenerator.Name(NodeType.Boss));
            Assert.AreEqual("宝箱房", MapGenerator.Name(NodeType.Treasure));
        }

        // 商人、营地、工坊已经从枚举里删掉，后面的值整体前移。眼下没有任何东西序列化 NodeType，
        // 所以这次重排是免费的；从第三阶段的存档开始，这些值就只能往后追加。
        [Test] public void NodeTypeValuesArePinned()
        {
            Assert.AreEqual(0, (int)NodeType.Battle);
            Assert.AreEqual(1, (int)NodeType.EliteBattle);
            Assert.AreEqual(2, (int)NodeType.Event);
            Assert.AreEqual(3, (int)NodeType.Boss);
            Assert.AreEqual(4, (int)NodeType.Supply);
            Assert.AreEqual(5, (int)NodeType.Altar);
            Assert.AreEqual(6, (int)NodeType.Elevator);
            Assert.AreEqual(7, (int)NodeType.Opening);
            Assert.AreEqual(8, (int)NodeType.Treasure, "宝箱房在末尾");
            Assert.AreEqual(9, Enum.GetValues(typeof(NodeType)).Length, "只有这九种节点类型");
        }

        // 权重为 0 的类型会出现在类型表里，但一次也不能被抽到；掩码也不能因此把它捡起来。
        [Test] public void AZeroWeightTypeIsRegisteredButNeverDrawn()
        {
            var rules = MapGenerator.Resolve(new MapRules
            {
                NodeOdds = new[]
                {
                    new NodeOddsSpec(NodeType.Battle, 0.5, 0),
                    new NodeOddsSpec(NodeType.Event, 0.5, 0),
                    new NodeOddsSpec(NodeType.Treasure, 0, 0)
                }
            });
            Assert.AreEqual(3, rules.NodeOdds.Length, "权重为 0 的类型仍然留在表里");
            Assert.AreEqual(0, OddsOf(rules, 50, NodeType.Treasure));
            Assert.AreEqual(0, OddsOf(rules, 0, NodeType.Treasure));
            Assert.AreEqual(0, OddsOf(rules, 100, NodeType.Treasure));
            for (uint seed = 1; seed <= 200; seed++)
                foreach (var step in Walk(Run(50, seed), rules))
                    Assert.That(step.Candidates.All(c => c.Type != NodeType.Treasure),
                        "权重为 0 的类型一次也不该出现");
        }

        // 反过来：把权重给出去，路线就必须开始抽到它。
        [Test] public void ACustomWeightTableDrivesTheRoute()
        {
            var mine = new MapRules
            {
                NodeOdds = new[]
                {
                    new NodeOddsSpec(NodeType.Battle, 0, 0),
                    new NodeOddsSpec(NodeType.Treasure, 1.0, 0)
                }
            };
            var rules = MapGenerator.Resolve(mine);
            Assert.AreEqual(0, OddsOf(rules, 50, NodeType.Battle), "基础权重为 0 的类型让位");
            Assert.AreEqual(1, OddsOf(rules, 50, NodeType.Treasure), "权重全压在宝箱房上");
            var path = Walk(Run(50), rules);
            Assert.That(path.Where(s => !s.Fixed).SelectMany(s => s.Candidates).All(c => c.Type == NodeType.Treasure),
                "可抽位置只会抽到有权重的类型");
        }

        [Test] public void ResolveRepairsBrokenRules()
        {
            var broken = new MapRules
            {
                Floors = 0, StepsPerFloor = new int[0], ProbabilityStrength = 0, BranchAffinity = 0,
                BranchWeights = new double[0], NodeOdds = new NodeOddsSpec[0], OpeningBuffs = new GrowthBuff[0],
                SupplyHealRatio = 0, SuppliesPerBattle = 0, EliteHealthBonus = 0, BossHealthBonus = 0
            };
            var fixedRules = MapGenerator.Resolve(broken);
            Assert.AreEqual(3, fixedRules.Floors);
            Assert.That(fixedRules.StepsPerFloor, Is.EqualTo(new[] { 5, 6, 7 }));
            Assert.AreEqual(1.0, fixedRules.ProbabilityStrength);
            Assert.AreEqual(1.5, fixedRules.BranchAffinity);
            Assert.AreEqual(5, fixedRules.NodeOdds.Length);
            Assert.AreEqual(3, fixedRules.OpeningBuffs.Length);
            Assert.AreEqual(0.35, fixedRules.SupplyHealRatio);
            Assert.AreEqual(2, fixedRules.SuppliesPerBattle);
            Assert.AreEqual(20, fixedRules.EliteHealthBonus);
            Assert.AreEqual(45, fixedRules.BossHealthBonus);
            Assert.That(MapGenerator.TypeOdds(fixedRules, 50).Sum(), Is.EqualTo(1).Within(1e-12));
        }

        [Test] public void AShortStepTableStillProducesEveryFloor()
        {
            // 两层目录不会把第三层生成成空的：越界的项回落到默认的 5 / 6 / 7。
            var rules = MapGenerator.Resolve(new MapRules { Floors = 3, StepsPerFloor = new[] { 4 } });
            Assert.That(rules.StepsPerFloor, Is.EqualTo(new[] { 4, 6, 7 }));
            Assert.AreEqual(6, MapGenerator.StepsOnFloor(rules, 1));
            Assert.AreEqual(8, MapGenerator.StepsOnFloor(rules, 2));
            Assert.AreEqual(9, MapGenerator.StepsOnFloor(rules, 3));
        }
    }
}
