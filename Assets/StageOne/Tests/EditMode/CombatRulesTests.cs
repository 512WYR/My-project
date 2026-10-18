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
        // 队伍要在与战斗相同的规则下建部位血量，否则部位上限会和战斗读取的规则脱节。
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity, catalog.Rules()); }
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
            string error; Assert.IsFalse(battle.Execute(new ActionRequest(ActionKind.Skill, actor.Id, 0), out error));
            Assert.AreSame(actor, battle.Current); Assert.AreEqual(state, run.Random.State); Assert.IsNotEmpty(error);
        }

        [Test] public void SwapOnlyAllowsAdjacentLivingAllyAndChangesRanks()
        {
            var run = Run(); var battle = Battle(run); var gunner = battle.Current;
            Assert.AreEqual("gunner", gunner.Id); Assert.AreEqual(3, gunner.Rank);
            Assert.IsFalse(battle.SwapTargets().Contains(run.Heroes[1]), "只能与相邻的队员换位");
            string error; Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Swap, "medic"), out error));
            Assert.AreEqual(2, gunner.Rank); Assert.AreEqual(3, run.Heroes[2].Rank);
        }

        [Test] public void FirstZeroHpHitEntersDeathDoorAndSecondCanKill()
        {
            var run = Run(); var rules = catalog.Rules(); rules.DeathChance = 1; var battle = Battle(run, rules);
            var hero = run.Heroes[0]; battle.Damage(hero, 999);
            Assert.IsTrue(hero.DeathDoor); Assert.IsFalse(hero.Dead);
            battle.Damage(hero, 1); Assert.IsTrue(hero.Dead);
        }

        [Test] public void HealingRemovesDeathDoor()
        {
            var run = Run(); var medic = run.Heroes[2]; medic.Definition.Speed = 100; run.Heroes[0].Hp = 0;
            var battle = Battle(run); string error; Assert.AreSame(medic, battle.Current);
            // 「流血奶」是奶妈常态列表里的第二项：治疗攻击力 × 1.4。
            var heal = battle.SkillFor(medic, 1);
            Assert.AreEqual(SkillEffect.Heal, heal.Effect);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "shield", 1), out error), error);
            Assert.AreEqual(14, run.Heroes[0].Hp); Assert.IsFalse(run.Heroes[0].DeathDoor);
        }

        [Test] public void DefenseHalvesAttackButDoesNotHalveBleeding()
        {
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0]; hero.Defending = true;
            battle.Damage(hero, 10); Assert.AreEqual(39, hero.Hp);
            battle.Damage(hero, 2, bleeding: true); Assert.AreEqual(37, hero.Hp);
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
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0]; // 盾卫，44 生命
            hero.Definition.Defense = 0.25;
            battle.Damage(hero, 8); Assert.AreEqual(38, hero.Hp);
            hero.Definition.Defense = 0.5; hero.Defending = true;
            battle.Damage(hero, 10); Assert.AreEqual(35, hero.Hp); // 10 → 5 → 2.5 → 3
            hero.Definition.Defense = 0.9;
            battle.Damage(hero, 5); Assert.AreEqual(34, hero.Hp); // reduction never drops below 1
        }

        [Test] public void BleedingIgnoresDefenseAndBrace()
        {
            var run = Run(); var battle = Battle(run); var hero = run.Heroes[0];
            hero.Definition.Defense = 0.5; hero.Defending = true;
            battle.Damage(hero, 6, bleeding: true); Assert.AreEqual(38, hero.Hp);
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

        [Test] public void DeadEnemyReleasesItsRankAndSurvivorsStepForward()
        {
            var run = Run();
            foreach (var hero in run.Heroes) hero.Definition.Speed = 100;
            var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(3).ToArray();
            for (int i = 0; i < specs.Length; i++)
            {
                specs[i].StartRank = i;
                specs[i].Speed = 1;
                specs[i].MaxHp = 100;
                specs[i].Dodge = 0;
                specs[i].Defense = 0;
                specs[i].Accuracy = 0;
                specs[i].RankWidth = 1;
                specs[i].Immobile = false;
                specs[i].Behavior = EnemyBehavior.Simple;
                specs[i].BasicSkills = ActorSpec.EmptySkills;
            }
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var front = battle.Enemies.Single(e => e.Rank == 0);
            var middle = battle.Enemies.Single(e => e.Rank == 1);
            var back = battle.Enemies.Single(e => e.Rank == 2);

            battle.Damage(front, 1000);
            Assert.IsTrue(front.Dead);

            string note;
            Assert.IsTrue(battle.SkipCurrentTurn(out note), note);
            Assert.AreEqual(0, middle.Rank, "中间的敌人补上头排空位");
            Assert.AreEqual(1, back.Rank, "后排依次前移一格");
            Assert.AreEqual(2, battle.Enemies.Count(e => !e.Dead), "尸体保留在名单里，但不参与站位");
            Assert.AreEqual(2, battle.Enemies.Where(e => !e.Dead).Select(e => e.Rank).Distinct().Count(),
                "存活敌人不能共享同一个站位");
        }

        [Test] public void TwoDeathsInOneActionReleaseBothRanks()
        {
            var run = Run();
            foreach (var hero in run.Heroes) hero.Definition.Speed = 100;
            var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(4).ToArray();
            for (int i = 0; i < specs.Length; i++)
            {
                specs[i].StartRank = i;
                specs[i].Speed = 1;
                specs[i].MaxHp = 100;
                specs[i].Dodge = 0;
                specs[i].Defense = 0;
                specs[i].Accuracy = 0;
                specs[i].RankWidth = 1;
                specs[i].Immobile = false;
                specs[i].Behavior = EnemyBehavior.Simple;
                specs[i].BasicSkills = ActorSpec.EmptySkills;
            }
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var front = battle.Enemies.Single(e => e.Rank == 0);
            var second = battle.Enemies.Single(e => e.Rank == 1);
            var third = battle.Enemies.Single(e => e.Rank == 2);
            var last = battle.Enemies.Single(e => e.Rank == 3);

            // 同一次范围攻击击杀两具尸体，存活单位应连续补上两个空位。
            battle.Damage(front, 1000);
            battle.Damage(second, 1000);

            string note;
            Assert.IsTrue(battle.SkipCurrentTurn(out note), note);
            Assert.AreEqual(0, third.Rank, "3 号位的敌人补到 1 号位");
            Assert.AreEqual(1, last.Rank, "4 号位的敌人补到 2 号位");
        }

        [Test] public void SummonedCopiesDoNotShareARankWithEachOther()
        {
            var run = Run();
            foreach (var hero in run.Heroes) hero.Definition.Speed = 100;
            var strike = new SkillSpec
            {
                Id = "test_copy_strike", Name = "复制打击", Effect = SkillEffect.Attack,
                Target = TargetSide.Enemy, Multiplier = 1.0, ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9
            };
            var specs = new[]
            {
                new ActorSpec
                {
                    Id = "copy_boss", Name = "复制者", MaxHp = 500, Speed = 1, StartRank = 0,
                    Attack = 1, Accuracy = 0, Behavior = EnemyBehavior.CopyBoss,
                    BasicSkills = new[] { strike }
                }
            };
            var battle = new BattleEngine(run, specs, catalog.Rules());

            // Walk to the start of round two. CopyBoss refills its three copies before the turn queue
            // is built, which is exactly the moment the old corpse/slot overlap used to appear.
            int guard = 0;
            while (battle.Round < 2 && battle.Outcome == BattleOutcome.Active && guard++ < 32)
            {
                if (battle.Current != null && battle.Current.Player) battle.SkipCurrentTurn(out _);
                else battle.ExecuteEnemy();
            }

            var livingCopies = battle.Enemies.Where(e => !e.Dead && e.IsCopy).ToArray();
            Assert.AreEqual(3, livingCopies.Length);
            Assert.AreEqual(3, livingCopies.Select(e => e.Rank).Distinct().Count(), "复制体各占一个站位");
            Assert.IsFalse(livingCopies.Any(e => e.Rank == 0), "复制体不会和首领挤在同一格");
        }

        [Test] public void CertainHitAndZeroDodgeConsumeNoRandomDraw()
        {
            var run = Run(); run.Heroes[1].Definition.Speed = 100; // 战士先动：「斩劈」打得到 1 号位的敌人
            var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].MaxHp = 1; specs[0].Speed = 1; specs[0].Dodge = 0; specs[0].Defense = 0;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var actor = battle.Current; actor.Definition.Accuracy = 1; actor.Definition.CritChance = 0;
            var target = battle.Targets(0).First();
            uint state = run.Random.State; string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, target.Id, 0), out error), error);
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome); // killing blow ends the battle, so no further draws
            Assert.AreEqual(state, run.Random.State);
        }

        [Test] public void DodgeOneNegatesHitWithoutDamage()
        {
            var run = Run(); run.Heroes[1].Definition.Speed = 100;
            var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].Speed = 1; specs[0].Dodge = 1;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var actor = battle.Current; actor.Definition.Accuracy = 1; actor.Definition.CritChance = 0;
            var target = battle.Targets(0).First();
            int hp = target.Hp; string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, target.Id, 0), out error), error);
            Assert.AreEqual(hp, target.Hp); Assert.IsTrue(battle.Events.Any(e => e.Kind == "dodge"));
        }

        [Test] public void HitAndCritUseFlatProbabilityIgnoringSanity()
        {
            var low = Run(15); var high = Run(85);
            var a = Battle(low); var b = Battle(high);
            var actorA = a.Current; var actorB = b.Current;
            Assert.IsTrue(actorA.Player); Assert.AreEqual(actorA.Id, actorB.Id);
            actorA.Form = SkillForm.Basic; actorB.Form = SkillForm.Basic;
            var skillA = a.SkillFor(actorA, 0); var skillB = b.SkillFor(actorB, 0);
            Assert.AreEqual(skillA.Id, skillB.Id);
            actorA.Sanity = 15; double atLow = a.EffectiveAccuracy(actorA, skillA);
            actorA.Sanity = 85; double atHigh = a.EffectiveAccuracy(actorA, skillA);
            Assert.AreEqual(atLow, atHigh); Assert.AreEqual(actorA.Definition.Accuracy, atHigh);
            var targetA = a.Targets(0).First(); var targetB = b.Targets(0).First();
            Assert.AreEqual(targetA.Id, targetB.Id);
            string error;
            Assert.IsTrue(a.Execute(new ActionRequest(ActionKind.Skill, targetA.Id, 0), out error), error);
            Assert.IsTrue(b.Execute(new ActionRequest(ActionKind.Skill, targetB.Id, 0), out error), error);
            Assert.AreEqual(targetA.Hp, targetB.Hp);
        }

        [Test] public void HealScalesWithBaseAttack()
        {
            var run = Run(); var medic = run.Heroes[2]; medic.Definition.Speed = 100; medic.Definition.Attack = 20;
            run.Heroes[0].Hp = 0;
            var battle = Battle(run); string error; Assert.AreSame(medic, battle.Current);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "shield", 1), out error), error);
            Assert.AreEqual(28, run.Heroes[0].Hp); // 20 × 1.4
        }

        [Test] public void GuardRedirectsTheWholeRoundToTheProtector()
        {
            var run = Run(50, 1); var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].Speed = 100; specs[0].Accuracy = 1;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            run.Heroes[0].ProtectorId = "medic"; // 盾卫被奶妈守护
            int shieldHp = run.Heroes[0].Hp; int medicHp = run.Heroes[2].Hp;
            int expected = battle.EnemyDamage(battle.Enemies[0]);
            battle.ExecuteEnemy();
            Assert.AreEqual(shieldHp, run.Heroes[0].Hp, "被守护者不承受这次攻击");
            Assert.AreEqual(medicHp - expected, run.Heroes[2].Hp, "守护者代为承受");
            Assert.AreEqual("medic", run.Heroes[0].ProtectorId, "守护持续整个回合");
        }

        [Test] public void LimitedSkillCannotBeFarmedWithinBattle()
        {
            var run = Run(); var battle = Battle(run); var actor = battle.Current;
            var skill = battle.SkillFor(actor, 1);
            Assert.IsTrue(skill.Uses > 0, "第二项技能应当是有次数限制的");
            actor.Uses.Add(new SkillUse { Id = skill.Id, Left = 0 });
            Assert.IsNotNull(battle.SkillDisabledReason(1));
            // 次数按技能 Id 记，下一场战斗会重新计数。
            var second = Battle(run);
            Assert.AreEqual(skill.Uses, second.Find(actor.Id).UsesLeft(second.SkillFor(second.Find(actor.Id), 1)));
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
                var skills = battle.SkillsFor(battle.Current);
                // 先找能打的技能，再找其余能用的技能，都没有才换位：低理智形态只留一个技能。
                int chosen = -1;
                for (int i = 0; i < skills.Length && chosen < 0; i++)
                    if (battle.SkillDisabledReason(i) == null && skills[i].Effect == SkillEffect.Attack) chosen = i;
                for (int i = 0; i < skills.Length && chosen < 0; i++)
                    if (battle.SkillDisabledReason(i) == null) chosen = i;
                string error;
                if (chosen >= 0)
                {
                    var target = battle.Targets(chosen).Select(a => a.Id).FirstOrDefault();
                    Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, target, chosen), out error), error);
                }
                else
                {
                    Assert.IsNull(battle.SwapDisabledReason(), "每个回合都必须至少有一个可用行动");
                    var neighbour = battle.SwapTargets().Select(a => a.Id).First();
                    Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Swap, neighbour), out error), error);
                }
            }
            Assert.That(actions, Is.LessThan(240)); Assert.NotNull(battle.Settlement);
        }

        [Test] public void ImbalanceIsShownOnTheIntentAndImmuneReapplicationIsLogged()
        {
            var run = Run();
            var skill = new SkillSpec
            {
                Id = "test_bash", Name = "测试猛击", Effect = SkillEffect.Attack,
                Target = TargetSide.Enemy, Multiplier = 0.1, ActorRanks = 15, TargetRanks = 15,
                Accuracy = 0.9, Imbalance = true
            };
            for (int i = 0; i < run.Heroes.Count; i++)
            {
                run.Heroes[i].Definition.Speed = 100 - i;
                run.Heroes[i].Definition.BasicSkills = new[] { skill };
                run.Heroes[i].Definition.CritChance = 0;
            }
            var specs = catalog.Enemies.Select(x => x.ToSpec()).Take(1).ToArray();
            specs[0].Speed = 1; specs[0].MaxHp = 1000; specs[0].Defense = 0; specs[0].Dodge = 0;
            specs[0].Accuracy = 0; specs[0].Behavior = EnemyBehavior.Simple;
            specs[0].BasicSkills = ActorSpec.EmptySkills;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            var target = battle.Enemies.Single();

            string error;
            Assert.AreSame(run.Heroes[0], battle.Current);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, target.Id, 0), out error), error);
            Assert.AreEqual(1, target.Imbalance, "失衡应让敌人跳过下一次行动");
            Assert.That(battle.Intent(target), Does.Contain("失衡"), "敌人卡片要能看到行动被跳过");
            Assert.IsTrue(battle.Events.Any(e => e.Message.Contains("下一次行动将被跳过")));

            // 同回合内再次施加失衡：免疫应当有明确反馈，而不是静默失败。
            target.ImbalanceImmuneRound = battle.Round;
            Assert.AreSame(run.Heroes[1], battle.Current);
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, target.Id, 0), out error), error);
            Assert.IsTrue(battle.Events.Any(e => e.Message.Contains("失衡免疫")), "免疫回合要写明本次失衡未生效");
        }

        [Test] public void PullMovesTheChosenEnemyEvenWhenAGuardTakesTheDamage()
        {
            var rules = catalog.Rules();
            var hero = new ActorSpec
            {
                Id = "test_gunner", Name = "测试枪手", MaxHp = 40, Speed = 100, Attack = 10,
                Accuracy = 1, CritChance = 0, Dodge = 0, Defense = 0
            };
            var pull = new SkillSpec
            {
                Id = "test_pull", Name = "测试拖拽射击", Effect = SkillEffect.Attack,
                Target = TargetSide.Enemy, Targeting = SkillTargeting.Choose,
                Multiplier = 1.0, ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9,
                PullToFront = true, PullChance = 1.0
            };
            hero.BasicSkills = new[] { pull };
            var run = RunState.Create(new[] { hero }, 1, 50, rules);
            var enemies = new[]
            {
                new ActorSpec
                {
                    Id = "test_tank", Name = "测试守护", MaxHp = 200, Speed = 1, StartRank = 0,
                    Attack = 0, Accuracy = 0, Behavior = EnemyBehavior.Simple,
                    BasicSkills = ActorSpec.EmptySkills
                },
                new ActorSpec
                {
                    Id = "test_ward", Name = "测试被守护者", MaxHp = 50, Speed = 1, StartRank = 2,
                    Attack = 0, Accuracy = 0, Behavior = EnemyBehavior.Simple,
                    BasicSkills = ActorSpec.EmptySkills
                }
            };
            var battle = new BattleEngine(run, enemies, rules);
            var tank = battle.Enemies.Single(e => e.Definition.Id == "test_tank");
            var ward = battle.Enemies.Single(e => e.Definition.Id == "test_ward");
            ward.ProtectorId = tank.Id;
            Assert.IsTrue(battle.Targets(0).Any(a => a == ward), "被守护者应当在可选目标里");

            string error;
            int tankHp = tank.Hp;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, ward.Id, 0), out error), error);

            Assert.Less(tank.Hp, tankHp, "守护者应当承担这次伤害");
            Assert.AreEqual(ward.Definition.MaxHp, ward.Hp, "被守护者不应掉血");
            Assert.AreEqual(0, ward.Rank, "拖拽仍然作用于玩家选中的目标，而不是挡刀者");
            Assert.AreEqual(1, tank.Rank, "挡刀者被让到 2 号位");
        }
    }
}
