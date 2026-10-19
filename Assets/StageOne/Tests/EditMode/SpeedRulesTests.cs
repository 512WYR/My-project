using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;

namespace Ruin.Tests
{
    // Speed is authored as an inclusive range and rolled once per living actor at the start of every
    // round. These tests pin the draw contract: an actor without a real span spends no random number, so
    // battles whose specs never opt into the range reproduce their old fixed-seed sequences exactly.
    public class SpeedRulesTests
    {
        private static ActorSpec Hero(string id, int speed, int min = 0, int max = 0)
        {
            return new ActorSpec
            {
                Id = id, Name = id, MaxHp = 100, Attack = 10, Accuracy = 1, Speed = speed,
                SpeedMin = min, SpeedMax = max, Defense = 0, Dodge = 0, CritChance = 0
            };
        }

        private static ActorSpec Foe(string id, int speed, int min = 0, int max = 0)
        {
            return new ActorSpec
            {
                Id = id, Name = id, MaxHp = 10000, Attack = 0, Accuracy = 1, Speed = speed,
                SpeedMin = min, SpeedMax = max, Defense = 0, Dodge = 0, CritChance = 0,
                Pattern = EnemyPattern.Cleaver
            };
        }

        private static BattleEngine Build(uint seed, ActorSpec[] heroes, ActorSpec[] enemies, out RunState run)
        {
            var rules = new CombatRules();
            run = RunState.Create(heroes, seed, 50, rules);
            return new BattleEngine(run, enemies, rules);
        }

        private static int[] Rolls(BattleEngine battle)
        {
            return battle.All.Select(a => a.SpeedRoll).ToArray();
        }

        [Test]
        public void EveryRoundRollStaysInsideTheAuthoredSpan()
        {
            var battle = Build(44972,
                new[] { Hero("hero_a", 6, 5, 7), Hero("hero_b", 4, 3, 5) },
                new[] { Foe("enemy_a", 5, 4, 6), Foe("enemy_b", 2, 1, 3) }, out _);

            foreach (var actor in battle.All)
            {
                int min = actor.Definition.MinSpeed, max = actor.Definition.MaxSpeed;
                Assert.That(actor.SpeedRoll, Is.InRange(min, max), actor.Name);
            }
        }

        [Test]
        public void FixedSpeedActorsSpendNoDrawAndRangedActorsSpendExactlyOne()
        {
            var specs = new[]
            {
                Hero("fixed", 6),
                Hero("ranged_a", 5, 4, 6),
                // A dead actor is still on the roster but must not consume a draw.
                Hero("dead_ranged", 9, 8, 10),
                Hero("ranged_b", 7, 6, 8)
            };
            var rules = new CombatRules();
            var run = RunState.Create(specs, 9751, 50, rules);
            run.Heroes[2].Hp = 0;
            run.Heroes[2].Dead = true;

            uint before = run.Random.State;
            // A fast enemy takes the first turn. Otherwise selecting a hero as Current draws once for the
            // sanity form, which would land between the speed rolls and muddy the count.
            var battle = new BattleEngine(run,
                new[] { Foe("fixed_foe", 100), Foe("span_of_one", 100, 100, 100) }, rules);

            // fixed, fixed_foe and span_of_one spend nothing; the two living ranged heroes spend one each.
            var reference = new RunRandom(before);
            reference.NextUnit();
            reference.NextUnit();
            Assert.That(run.Random.State, Is.EqualTo(reference.State));
            Assert.That(battle.Round, Is.EqualTo(1));
        }

        [Test]
        public void TurnQueueFollowsRolledSpeedThenSideThenId()
        {
            // Four actors with identical spans: only the tie-breaks can decide the order, which is exactly
            // what the old fixed-speed ordering promised.
            var battle = Build(1234,
                new[] { Hero("hero_b", 6, 5, 7), Hero("hero_a", 6, 5, 7) },
                new[] { Foe("enemy_b", 6, 5, 7), Foe("enemy_a", 6, 5, 7) }, out _);

            var expected = battle.All.Where(a => !a.Dead)
                .OrderByDescending(a => battle.EffectiveSpeed(a))
                .ThenBy(a => a.Player ? 0 : 1)
                .ThenBy(a => a.Id, System.StringComparer.Ordinal)
                .ToArray();

            var queue = new List<ActorState> { battle.Current };
            queue.AddRange(battle.TurnPreview);
            Assert.That(queue.Select(a => a.Id), Is.EqualTo(expected.Select(a => a.Id)));
        }

        [Test]
        public void SameSeedProducesTheSameSpeedSequence()
        {
            var first = Build(20261019,
                new[] { Hero("hero_a", 6, 5, 7), Hero("hero_b", 4, 3, 5) },
                new[] { Foe("enemy_a", 5, 4, 6), Foe("enemy_b", 2, 1, 3) }, out _);
            var second = Build(20261019,
                new[] { Hero("hero_a", 6, 5, 7), Hero("hero_b", 4, 3, 5) },
                new[] { Foe("enemy_a", 5, 4, 6), Foe("enemy_b", 2, 1, 3) }, out _);

            Assert.That(Rolls(first), Is.EqualTo(Rolls(second)));
            Assert.That(first.Current.Id, Is.EqualTo(second.Current.Id));
        }

        [Test]
        public void EachNewRoundRollsSpeedsAgain()
        {
            // Wide spans make it effectively impossible for all four actors to repeat a whole round's
            // rolls, so the assertion below is stable while still describing the intended behaviour.
            var battle = Build(777,
                new[] { Hero("hero_a", 50, 1, 100), Hero("hero_b", 50, 1, 100) },
                new[] { Foe("enemy_a", 50, 1, 100), Foe("enemy_b", 50, 1, 100) }, out var run);

            var roundOne = battle.All.ToDictionary(a => a.Id, a => a.SpeedRoll);
            uint stateBeforeRoundTwo = 0;
            while (battle.Outcome == BattleOutcome.Active && battle.Round == 1)
            {
                // The skip that exhausts the round's queue is the one that rolls the next round, so capture
                // the state right before it. Intermediate skips may draw for the next hero's form.
                uint beforeSkip = run.Random.State;
                Assert.That(battle.SkipCurrentTurn(out _), Is.True);
                if (battle.Round == 2) stateBeforeRoundTwo = beforeSkip;
            }

            Assert.That(battle.Round, Is.EqualTo(2));
            var reference = new RunRandom(stateBeforeRoundTwo);
            var expected = new List<int>();
            foreach (var actor in battle.All.Where(a => !a.Dead))
            {
                int min = actor.Definition.MinSpeed, max = actor.Definition.MaxSpeed;
                expected.Add(min + (int)(reference.NextUnit() * (max - min + 1)));
            }

            Assert.That(battle.All.Select(a => a.SpeedRoll), Is.EqualTo(expected));
            Assert.That(battle.All.Any(a => a.SpeedRoll != roundOne[a.Id]), Is.True);
        }

        [Test]
        public void LimbPenaltyAppliesAfterTheRoll()
        {
            var battle = Build(99, new[] { Hero("leg", 10, 9, 11) }, new[] { Foe("enemy", 1) }, out _);
            var hero = battle.Run.Heroes[0];
            int rolled = hero.SpeedRoll;

            Assert.That(LimbSystem.Damage(hero, LimbPart.Leg, 9999), Is.True);
            int expected = System.Math.Max(1,
                (int)System.Math.Round(rolled * 0.55, System.MidpointRounding.AwayFromZero));
            Assert.That(battle.EffectiveSpeed(hero), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidSpanAndRuntimeSpeedOverrideFallBackToFixedSpeed()
        {
            var rules = new CombatRules();
            // Inverted span, and a test/debug override that moved Speed outside its authored span.
            var run = RunState.Create(
                new[] { Hero("invalid", 6, 9, 4), Hero("overridden", 100, 5, 7) }, 5150, 50, rules);
            uint before = run.Random.State;

            // The fast enemy takes the first turn, so the only draws would be speed rolls.
            var battle = new BattleEngine(run, new[] { Foe("enemy", 200) }, rules);

            Assert.That(run.Random.State, Is.EqualTo(before));
            Assert.That(battle.Find("invalid").SpeedRoll, Is.EqualTo(6));
            Assert.That(battle.Find("overridden").SpeedRoll, Is.EqualTo(100));
            Assert.That(battle.EffectiveSpeed(battle.Find("invalid")), Is.EqualTo(6));
            Assert.That(battle.EffectiveSpeed(battle.Find("overridden")), Is.EqualTo(100));
        }
    }
}
