using System;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    public class CombatRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity); }
        private BattleEngine Battle(RunState run, CombatRules rules = null) { return new BattleEngine(run, catalog.Enemies.Select(x => x.ToSpec()).ToArray(), rules ?? catalog.Rules()); }
        private static WeightedOutcome[] Forms() { return new[] { new WeightedOutcome(.2, 1, SanityGate.High), new WeightedOutcome(.6, 0), new WeightedOutcome(.2, -1, SanityGate.Low) }; }

        [TestCase(30)] [TestCase(50)] [TestCase(70)]
        public void StableRangeHasOnlyBasicForm(double sanity)
        {
            var p = SanityProbability.Distribution(sanity, Forms());
            Assert.That(p, Is.EqualTo(new[] { 0.0, 1.0, 0.0 }));
        }

        [TestCase(15, 2)] [TestCase(85, 0)]
        public void ReferenceProbabilityMatchesExpectedValue(double sanity, int index)
        { var p = SanityProbability.Distribution(sanity, Forms()); Assert.That(p[index], Is.EqualTo(0.32262).Within(0.0001)); }

        [TestCase(0, 2)] [TestCase(100, 0)]
        public void ExtremeIsLikelyButNotGuaranteed(double sanity, int index)
        { var p = SanityProbability.Distribution(sanity, Forms()); Assert.That(p[index], Is.EqualTo(0.59902).Within(0.0002)); }

        [Test] public void AllValuesRespectGatesAndNormalization()
        {
            for (int i = 0; i <= 10000; i++)
            {
                double s = i / 100.0; var p = SanityProbability.Distribution(s, Forms());
                Assert.That(p.Sum(), Is.EqualTo(1).Within(1e-12)); Assert.That(p.All(x => x >= 0 && x <= 1));
                if (s <= 70) Assert.AreEqual(0, p[0]); if (s >= 30) Assert.AreEqual(0, p[2]);
            }
        }

        [Test] public void ProbabilitiesIncreaseTowardCorrespondingExtreme()
        {
            double last = 0;
            for (int i = 7000; i <= 10000; i++) { double p = SanityProbability.Distribution(i / 100.0, Forms())[0]; Assert.That(p, Is.GreaterThanOrEqualTo(last)); last = p; }
            Assert.That(SanityProbability.Distribution(70.00001, Forms())[0], Is.LessThan(0.000001));
            Assert.That(SanityProbability.Distribution(29.99999, Forms())[2], Is.LessThan(0.000001));
        }

        [Test] public void DisabledOutcomeNeverSelectedEvenWhenRandomIsZero()
        {
            var r = new RunRandom(1);
            for (int i = 0; i < 10000; i++) Assert.AreEqual(1, SanityProbability.Sample(new[] { 0.0, 1.0, 0.0 }, r));
        }
        [Test] public void RandomStateCanBeRestoredExactly()
        {
            var a = new RunRandom(9751); for (int i = 0; i < 8; i++) a.NextUnit();
            var b = new RunRandom(a.State); for (int i = 0; i < 30; i++) Assert.AreEqual(a.NextUnit(), b.NextUnit());
        }
        [Test] public void InvalidProbabilityConfigurationFailsClearly()
        {
            Assert.Throws<ArgumentException>(() => SanityProbability.Distribution(50, new[] { new WeightedOutcome(0, 0) }));
            Assert.Throws<ArgumentException>(() => SanityProbability.Distribution(double.NaN, Forms()));
            Assert.Throws<ArgumentException>(() => SanityProbability.Distribution(50, new[] { new WeightedOutcome(-1, 0) }));
        }

        [Test] public void FeedbackMatchesExampleAndOnlySettlesOnce()
        {
            var run = Run(); var battle = Battle(run); var deltas = new[] { -20, -10, 0, 10 };
            for (int i = 0; i < 4; i++) battle.ChangeSanity(run.Heroes[i], deltas[i], "test");
            var s = battle.Finish(BattleOutcome.Retreat);
            Assert.AreEqual(-2.5, s.RawDelta); Assert.AreEqual(47.5, run.PlayerSanity);
            Assert.AreSame(s, battle.Finish(BattleOutcome.Victory)); Assert.AreEqual(47.5, run.PlayerSanity);
        }

        [Test] public void UnchangedDifferentInitialValuesDoNotCreateFeedback()
        {
            var run = RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), 42);
            var s = Battle(run).Finish(BattleOutcome.Retreat); Assert.AreEqual(0, s.RawDelta); Assert.AreEqual(50, s.After);
        }

        [Test] public void DeadMemberRetainsSnapshotAndFixedDenominator()
        {
            var run = Run(); var rules = catalog.Rules(); rules.DeathChance = 1;
            var battle = Battle(run, rules);
            battle.ChangeSanity(run.Heroes[0], -20, "test");
            run.Heroes[0].Hp = 0; battle.Damage(run.Heroes[0], 1);
            var s = battle.Finish(BattleOutcome.Retreat);
            Assert.IsTrue(run.Heroes[0].Dead); Assert.AreEqual(4, s.Entries.Count); Assert.AreEqual(-2.5, s.RawDelta);
            Assert.AreEqual(30, s.Entries[0].End);
        }

        [Test] public void PersonalSanityAndMentalStateSurviveNewBattle()
        {
            var run = Run(15); var battle = Battle(run);
            run.Heroes[0].Mental = MentalState.Agitated;
            battle.ChangeSanity(run.Heroes[0], 35, "recover"); battle.Finish(BattleOutcome.Retreat);
            var second = Battle(run);
            Assert.AreEqual(50, run.Heroes[0].Sanity); Assert.AreEqual(MentalState.Agitated, run.Heroes[0].Mental);
            Assert.AreEqual(0, second.FormProbabilities(run.Heroes[0])[2]);
            Assert.AreEqual(0, second.Finish(BattleOutcome.Retreat).RawDelta);
        }

        [Test] public void MentalModifierCannotBypassGates()
        {
            var run = Run(50); var battle = Battle(run); run.Heroes[0].Mental = MentalState.Composed;
            Assert.AreEqual(0, battle.FormProbabilities(run.Heroes[0])[0]);
            run.Heroes[0].Mental = MentalState.Agitated; Assert.AreEqual(0, battle.FormProbabilities(run.Heroes[0])[2]);
        }

        [Test] public void FormDoesNotRerollWhenSanityChangesDuringTurn()
        {
            var run = Run(85); var battle = Battle(run); var current = battle.Current; var old = current.Form; uint state = run.Random.State;
            battle.ChangeSanity(current, -70, "test");
            Assert.AreEqual(old, current.Form); Assert.AreEqual(state, run.Random.State);
        }

        [Test] public void InvalidTargetDoesNotSpendActionOrRandomDraw()
        {
            var run = Run(); var battle = Battle(run); var actor = battle.Current; uint state = run.Random.State;
            string error; Assert.IsFalse(battle.Execute(new ActionRequest(ActionKind.Primary, actor.Id), out error));
            Assert.AreSame(actor, battle.Current); Assert.AreEqual(state, run.Random.State); Assert.IsNotEmpty(error);
        }

        [Test] public void SwapOnlyAllowsAdjacentLivingAllyAndChangesRanks()
        {
            var run = Run(); var battle = Battle(run); var scout = battle.Current;
            Assert.AreEqual("scout", scout.Id); Assert.IsFalse(battle.Targets(ActionKind.Swap).Contains(run.Heroes[3]));
            string error; Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Swap, "guard"), out error));
            Assert.AreEqual(0, scout.Rank); Assert.AreEqual(1, run.Heroes[0].Rank);
        }

        [Test] public void FirstZeroHpHitEntersDeathDoorAndSecondCanKill()
        {
            var run = Run(); var rules = catalog.Rules(); rules.DeathChance = 1; var battle = Battle(run, rules);
            var hero = run.Heroes[0]; battle.Damage(hero, 999);
            Assert.IsTrue(hero.DeathDoor); Assert.IsFalse(hero.Dead);
            battle.Damage(hero, 1); Assert.IsTrue(hero.Dead);
        }

        [Test] public void HealingRemovesDeathDoorAndConsumesLimitedUse()
        {
            var run = Run(); var medic = run.Heroes[3]; medic.Definition.Speed = 100; run.Heroes[0].Hp = 0;
            var battle = Battle(run); string error; Assert.AreSame(medic, battle.Current);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Support, "guard"), out error));
            Assert.AreEqual(9, run.Heroes[0].Hp); Assert.IsFalse(run.Heroes[0].DeathDoor); Assert.AreEqual(2, medic.SupportUses);
        }

        [Test] public void DefenseHalvesAttackButDoesNotHalveBleeding()
        {
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0]; hero.Defending = true;
            battle.Damage(hero, 10); Assert.AreEqual(37, hero.Hp);
            battle.Damage(hero, 2, bleeding: true); Assert.AreEqual(35, hero.Hp);
        }

        [Test] public void BaseAttackAndSkillMultiplierProduceDamage()
        {
            var run = Run(); var battle = Battle(run); var actor = run.Heroes[0];
            actor.Definition.Attack = 10;
            var skill = new SkillSpec { Multiplier = 0.4 };
            Assert.AreEqual(4, battle.SkillValue(actor, skill));
            skill.Multiplier = 1.3; Assert.AreEqual(13, battle.SkillValue(actor, skill));
            actor.Definition.Attack = 15; Assert.AreEqual(20, battle.SkillValue(actor, skill)); // 19.5 rounds up, never truncates
            Assert.AreEqual(30, battle.CritValue(actor, skill)); // 20 × 1.5
        }

        [Test] public void DefenseReducesDamagePercentuallyWithFloorOne()
        {
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0]; // guard, 42 HP
            hero.Definition.Defense = 0.25;
            battle.Damage(hero, 8); Assert.AreEqual(36, hero.Hp);
            hero.Definition.Defense = 0.5; hero.Defending = true;
            battle.Damage(hero, 10); Assert.AreEqual(33, hero.Hp); // 10 → 5 → 2.5 → 3
            hero.Definition.Defense = 0.9;
            battle.Damage(hero, 5); Assert.AreEqual(32, hero.Hp); // reduction never drops below 1
        }

        [Test] public void BleedingIgnoresDefenseAndBrace()
        {
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0];
            hero.Definition.Defense = 0.5; hero.Defending = true;
            battle.Damage(hero, 6, bleeding: true); Assert.AreEqual(36, hero.Hp);
        }

        [Test] public void EnemyDamageFollowsCharacterAttackAndPattern()
        {
            var run = Run(); var battle = Battle(run); var cleaver = battle.Enemies[0];
            cleaver.Definition.Attack = 10; cleaver.Definition.Pattern = EnemyPattern.Cleaver;
            Assert.AreEqual(battle.Round % 2 == 0 ? 11 : 7, battle.EnemyDamage(cleaver));
            cleaver.Definition.Pattern = EnemyPattern.Whisperer; Assert.AreEqual(3, battle.EnemyDamage(cleaver));
            cleaver.Definition.Pattern = EnemyPattern.Hunter; Assert.AreEqual(5, battle.EnemyDamage(cleaver));
            cleaver.Definition.Attack = 20; Assert.AreEqual(10, battle.EnemyDamage(cleaver));
        }

        [Test] public void CertainHitAndZeroDodgeConsumeNoRandomDraw()
        {
            var run = Run(); var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].MaxHp = 1; specs[0].Speed = 1; specs[0].Dodge = 0; specs[0].Defense = 0;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var actor = battle.Current; actor.Definition.Accuracy = 1; actor.Definition.CritChance = 0;
            var target = battle.Targets(ActionKind.Primary).First();
            uint state = run.Random.State; string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Primary, target.Id), out error), error);
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome); // killing blow ends the battle, so no further draws
            Assert.AreEqual(state, run.Random.State);
        }

        [Test] public void DodgeOneNegatesHitWithoutDamage()
        {
            var run = Run(); var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].Speed = 1; specs[0].Dodge = 1;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var actor = battle.Current; actor.Definition.Accuracy = 1; actor.Definition.CritChance = 0;
            var target = battle.Targets(ActionKind.Primary).First();
            int hp = target.Hp; string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Primary, target.Id), out error), error);
            Assert.AreEqual(hp, target.Hp); Assert.IsTrue(battle.Events.Any(e => e.Kind == "dodge"));
        }

        [Test] public void HitAndCritUseFlatProbabilityIgnoringSanity()
        {
            var low = Run(15); var high = Run(85);
            var a = Battle(low); var b = Battle(high);
            var actorA = a.Current; var actorB = b.Current;
            Assert.IsTrue(actorA.Player); Assert.AreEqual(actorA.Id, actorB.Id);
            actorA.Form = SkillForm.Basic; actorB.Form = SkillForm.Basic;
            var skillA = a.SkillFor(actorA, ActionKind.Primary); var skillB = b.SkillFor(actorB, ActionKind.Primary);
            Assert.AreEqual(skillA.Id, skillB.Id);
            actorA.Sanity = 15; double atLow = a.EffectiveAccuracy(actorA, skillA);
            actorA.Sanity = 85; double atHigh = a.EffectiveAccuracy(actorA, skillA);
            Assert.AreEqual(atLow, atHigh); Assert.AreEqual(actorA.Definition.Accuracy, atHigh);
            var targetA = a.Targets(ActionKind.Primary).First(); var targetB = b.Targets(ActionKind.Primary).First();
            Assert.AreEqual(targetA.Id, targetB.Id);
            string error;
            Assert.IsTrue(a.Execute(new ActionRequest(ActionKind.Primary, targetA.Id), out error), error);
            Assert.IsTrue(b.Execute(new ActionRequest(ActionKind.Primary, targetB.Id), out error), error);
            Assert.AreEqual(targetA.Hp, targetB.Hp);
        }

        [Test] public void HealScalesWithBaseAttack()
        {
            var run = Run(); var medic = run.Heroes[3]; medic.Definition.Speed = 100; medic.Definition.Attack = 20;
            run.Heroes[0].Hp = 0;
            var battle = Battle(run); string error; Assert.AreSame(medic, battle.Current);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Support, "guard"), out error), error);
            Assert.AreEqual(18, run.Heroes[0].Hp); // 20 × 0.9
        }

        [Test] public void InterceptProtectsTargetAndUsesGuardReduction()
        {
            var run = Run(50, 1); var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray(); specs[0].Speed = 100;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            run.Heroes[0].ProtectorId = "medic"; run.Heroes[3].Defending = true;
            battle.ExecuteEnemy();
            Assert.AreEqual(42, run.Heroes[0].Hp); Assert.AreEqual(24, run.Heroes[3].Hp); Assert.IsNull(run.Heroes[0].ProtectorId);
        }

        [Test] public void LimitedSanityActionsCannotBeFarmedWithinBattle()
        {
            var run = Run(); var battle = Battle(run); battle.Current.FocusUses = 0;
            Assert.IsNotNull(battle.DisabledReason(ActionKind.Focus));
        }

        [Test] public void DefeatSettlesWithoutAliveRosterDivisionError()
        {
            var run = Run(); foreach (var hero in run.Heroes) hero.Dead = true;
            var battle = Battle(run); Assert.AreEqual(BattleOutcome.Defeat, battle.Outcome); Assert.AreEqual(0, battle.Settlement.RawDelta);
        }

        [Test] public void AutomatedBattleTerminatesAndAllActionsHaveValidTargets()
        {
            var run = Run(85); var battle = Battle(run); int actions = 0;
            while (battle.Outcome == BattleOutcome.Active && actions++ < 240)
            {
                if (!battle.Current.Player) { battle.ExecuteEnemy(); continue; }
                var kind = new[] { ActionKind.Technical, ActionKind.Primary, ActionKind.Support }.FirstOrDefault(k => battle.DisabledReason(k) == null && battle.SkillFor(battle.Current, k).Effect == SkillEffect.Attack);
                if (battle.DisabledReason(kind) != null || !battle.Targets(kind).Any()) kind = ActionKind.Defend;
                string error;
                Assert.IsTrue(battle.Execute(new ActionRequest(kind, battle.Targets(kind).Select(a => a.Id).FirstOrDefault()), out error), error);
            }
            Assert.That(actions, Is.LessThan(240)); Assert.NotNull(battle.Settlement);
        }
    }
}
