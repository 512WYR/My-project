using System;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // 肢体与义肢：四个部位各有独立血量，一次重创即重伤、两次断肢；只有敌人特定行动附带部位伤害。
    public class LimbRulesTests
    {
        private GameCatalog catalog;
        [SetUp] public void SetUp() { catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog"); Assert.NotNull(catalog, "Generate catalog before running tests"); }
        private RunState Run(double sanity = 50, uint seed = 44972) { return RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), seed, sanity, catalog.Rules()); }
        private LimbRules Rules() { return catalog.Rules().Limbs; }
        private static uint Drained(uint state, int draws) { var r = new RunRandom(state); for (int i = 0; i < draws; i++) r.NextUnit(); return r.State; }
        private static readonly LimbPart[] EveryPart = { LimbPart.Head, LimbPart.Torso, LimbPart.Arm, LimbPart.Leg };

        // 三套技能列表都填同一个技能，好让一次性队伍在任何形态下都还有事可做。
        private static ActorSpec Fighter(string id, int maxHp, int speed)
        {
            SkillSpec Swing(string skillId, string skillName)
            {
                return new SkillSpec { Id = skillId, Name = skillName, Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                    Multiplier = 1.0, ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9 };
            }
            var hit = Swing("hit", "攻击");
            return new ActorSpec { Id = id, Name = id, MaxHp = maxHp, Speed = speed, Attack = 10, Accuracy = 0.9,
                BasicSkills = new[] { hit }, PrimalSkills = new[] { hit }, TechnicalSkills = new[] { hit } };
        }

        // 速度 100 保证敌人先手，命中 1 且目标闪避 0 保证不掷命中骰，这样抽取次数只剩部位概率。
        private static ActorSpec Foe(EnemyPattern pattern, int maxHp)
        {
            var spec = Fighter("foe", maxHp, 100);
            spec.Pattern = pattern; spec.Accuracy = 1;
            return spec;
        }

        private LimbPartRule Rule(LimbPart part) { return Rules().Parts.First(x => x.Part == part); }

        // 随便走一步合法行动，只为了让回合继续推进：先找能用的技能，实在没有就换位。
        private static void PlaySomething(BattleEngine battle)
        {
            var skills = battle.SkillsFor(battle.Current);
            string error;
            for (int i = 0; i < skills.Length; i++)
            {
                if (battle.SkillDisabledReason(i) != null) continue;
                Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill,
                    battle.Targets(i).Select(a => a.Id).FirstOrDefault(), i), out error), error);
                return;
            }
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Swap,
                battle.SwapTargets().Select(a => a.Id).First()), out error), error);
        }

        [Test] public void EveryHeroStartsWithFourIntactLimbs()
        {
            foreach (var hero in Run().Heroes)
            {
                Assert.AreEqual(4, hero.Limbs.Count);
                foreach (var part in EveryPart)
                {
                    var limb = LimbSystem.Get(hero, part);
                    Assert.NotNull(limb, part.ToString());
                    Assert.AreEqual((int)Math.Ceiling(hero.Definition.MaxHp * Rule(part).HpRatio - 1e-9), limb.MaxHp, part.ToString());
                    Assert.AreEqual(limb.MaxHp, limb.Hp, part.ToString());
                    Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(limb), part.ToString());
                    Assert.IsFalse(limb.ProstheticFitted, part.ToString());
                }
                Assert.IsFalse(LimbSystem.AnyLimbDamaged(hero), hero.Name);
            }
        }

        [Test] public void OneBlowInjuresAndASecondSevers()
        {
            foreach (var hero in Run().Heroes)
                foreach (var part in EveryPart)
                {
                    int hit = LimbSystem.HitAmount(hero, part, Rules());
                    Assert.IsFalse(LimbSystem.Damage(hero, part, hit), "第一次重创只应进入重伤：" + part);
                    Assert.AreEqual(LimbCondition.Injured, LimbSystem.ConditionOf(hero, part), part.ToString());
                    Assert.IsTrue(LimbSystem.Damage(hero, part, hit), "第二次重创应当断肢：" + part);
                    Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, part), part.ToString());
                }
        }

        [Test] public void SeveredPenaltyReplacesInjuredPenaltyWithoutStacking()
        {
            var hero = Run().Heroes[0];
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.That(LimbSystem.AttackFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Arm).InjuredPenalty).Within(1e-12));
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.That(LimbSystem.AttackFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Arm).SeveredPenalty).Within(1e-12));
            Assert.That(LimbSystem.DefenseFactor(hero, Rules()), Is.EqualTo(1).Within(1e-12), "其他部位不受影响");
            Assert.That(LimbSystem.SpeedFactor(hero, Rules()), Is.EqualTo(1).Within(1e-12));
            Assert.That(LimbSystem.AccuracyFactor(hero, Rules()), Is.EqualTo(1).Within(1e-12));
        }

        [Test] public void EachPartWeakensItsOwnAttribute()
        {
            var hero = Run().Heroes[0];
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.That(LimbSystem.AttackFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Arm).InjuredPenalty).Within(1e-12));
            LimbSystem.Damage(hero, LimbPart.Torso, LimbSystem.HitAmount(hero, LimbPart.Torso, Rules()));
            Assert.That(LimbSystem.DefenseFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Torso).InjuredPenalty).Within(1e-12));
            LimbSystem.Damage(hero, LimbPart.Leg, LimbSystem.HitAmount(hero, LimbPart.Leg, Rules()));
            Assert.That(LimbSystem.SpeedFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Leg).InjuredPenalty).Within(1e-12));
            LimbSystem.Damage(hero, LimbPart.Head, LimbSystem.HitAmount(hero, LimbPart.Head, Rules()));
            Assert.That(LimbSystem.AccuracyFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Head).InjuredPenalty).Within(1e-12));
        }

        [Test] public void EffectiveAttributesReadTheLimbState()
        {
            var run = RunState.Create(new[] { Fighter("guard", 42, 9) }, 1, 50, catalog.Rules());
            var hero = run.Heroes[0];
            hero.Definition.Defense = 0.5;
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Whisperer, 200) }, catalog.Rules());
            var skill = hero.Definition.BasicSkills[0];

            Assert.AreEqual(9, battle.EffectiveSpeed(hero));
            Assert.That(battle.EffectiveDefense(hero), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(battle.EffectiveAccuracy(hero, skill), Is.EqualTo(0.9).Within(1e-12));
            Assert.AreEqual(10, battle.SkillValue(hero, skill));

            LimbSystem.Damage(hero, LimbPart.Leg, LimbSystem.HitAmount(hero, LimbPart.Leg, Rules()));
            Assert.AreEqual(7, battle.EffectiveSpeed(hero), "腿部重伤：速度 9 → 7");
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.AreEqual(8, battle.SkillValue(hero, skill), "手臂重伤：技能伤害 10 → 8");
            LimbSystem.Damage(hero, LimbPart.Torso, LimbSystem.HitAmount(hero, LimbPart.Torso, Rules()));
            Assert.That(battle.EffectiveDefense(hero), Is.EqualTo(0.4).Within(1e-12), "躯干重伤：防御 0.5 → 0.4");
            LimbSystem.Damage(hero, LimbPart.Head, LimbSystem.HitAmount(hero, LimbPart.Head, Rules()));
            Assert.That(battle.EffectiveAccuracy(hero, skill), Is.EqualTo(0.81).Within(1e-12), "头部重伤：命中 0.9 → 0.81");
        }

        [Test] public void ProstheticRestoresBaseFunctionAndIsWreckedWithThePart()
        {
            var hero = Run().Heroes[0];
            int hit = LimbSystem.HitAmount(hero, LimbPart.Leg, Rules());
            LimbSystem.Damage(hero, LimbPart.Leg, hit);
            LimbSystem.Damage(hero, LimbPart.Leg, hit);
            Assert.IsTrue(LimbSystem.Install(hero, LimbSystem.Prosthetic("prosthetic_leg", Rules())));
            Assert.IsTrue(LimbSystem.HasProsthetic(hero, LimbPart.Leg));
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Leg));
            Assert.That(LimbSystem.SpeedFactor(hero, Rules()), Is.EqualTo(1).Within(1e-12), "义肢恢复基础功能");

            // 装了义肢的部位血量回满，所以再次打断仍然要两下；义肢随部位一起损毁。
            LimbSystem.Damage(hero, LimbPart.Leg, hit);
            Assert.IsTrue(LimbSystem.HasProsthetic(hero, LimbPart.Leg), "只受一次伤害时义肢还在");
            LimbSystem.Damage(hero, LimbPart.Leg, hit);
            Assert.IsFalse(LimbSystem.HasProsthetic(hero, LimbPart.Leg), "义肢随断肢一并损毁");
            Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, LimbPart.Leg));
            Assert.That(LimbSystem.SpeedFactor(hero, Rules()), Is.EqualTo(1 - Rule(LimbPart.Leg).SeveredPenalty).Within(1e-12));
            Assert.IsTrue(LimbSystem.CanInstall(hero, LimbSystem.Prosthetic("prosthetic_leg", Rules())), "义肢损毁后可以重新安装");
        }

        [Test] public void RepairHealsInjuredButNotSevered()
        {
            var hero = Run().Heroes[0];
            int hit = LimbSystem.HitAmount(hero, LimbPart.Arm, Rules());
            LimbSystem.Damage(hero, LimbPart.Arm, hit);
            Assert.IsTrue(LimbSystem.CanRepair(hero, LimbPart.Arm));
            Assert.IsTrue(LimbSystem.Repair(hero, LimbPart.Arm));
            Assert.AreEqual(LimbSystem.Get(hero, LimbPart.Arm).MaxHp, LimbSystem.Get(hero, LimbPart.Arm).Hp);
            Assert.AreEqual(LimbCondition.Intact, LimbSystem.ConditionOf(hero, LimbPart.Arm));

            LimbSystem.Damage(hero, LimbPart.Arm, hit);
            LimbSystem.Damage(hero, LimbPart.Arm, hit);
            Assert.IsFalse(LimbSystem.CanRepair(hero, LimbPart.Arm), "断肢不可修复");
            Assert.IsFalse(LimbSystem.Repair(hero, LimbPart.Arm));
            Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, LimbPart.Arm));
        }

        [Test] public void InstallRequiresASeveredLimb()
        {
            var hero = Run().Heroes[0];
            var arm = LimbSystem.Prosthetic("prosthetic_arm", Rules());
            Assert.IsFalse(LimbSystem.CanInstall(hero, arm), "完好部位不能安装义肢");
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.IsFalse(LimbSystem.CanInstall(hero, arm), "重伤也不是安装条件，必须先断肢");
            LimbSystem.Damage(hero, LimbPart.Arm, LimbSystem.HitAmount(hero, LimbPart.Arm, Rules()));
            Assert.IsTrue(LimbSystem.CanInstall(hero, arm));
            Assert.IsTrue(LimbSystem.Install(hero, arm));
            Assert.IsFalse(LimbSystem.CanInstall(hero, arm), "装好以后就不再是断肢");
        }

        [Test] public void CrippledHeroKeepsItsSkillsAndSwap()
        {
            var run = RunState.Create(new[] { Fighter("guard", 42, 100) }, 1, 50, catalog.Rules());
            var hero = run.Heroes[0];
            foreach (var part in EveryPart)
            {
                LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, Rules()));
                LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, Rules()));
            }
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Whisperer, 200) }, catalog.Rules());
            Assert.AreSame(hero, battle.Current, "高速队员先行动");
            Assert.IsNull(battle.SkillDisabledReason(0), "断肢不禁止技能");
            Assert.AreEqual("没有相邻的存活队员", battle.SwapDisabledReason(), "孤身一人时无处换位");
        }

        [Test] public void OnlyCleaverAndHunterCarryALimbAttack()
        {
            Assert.IsNull(LimbSystem.ForPattern(EnemyPattern.Whisperer), "低语者用理智施压，不做部位攻击");
            var cleaver = LimbSystem.ForPattern(EnemyPattern.Cleaver);
            Assert.AreEqual(LimbPart.Arm, cleaver.Part);
            Assert.IsTrue(cleaver.HeavyRoundsOnly, "执斧者只在重劈的偶数回合重创手臂");
            var hunter = LimbSystem.ForPattern(EnemyPattern.Hunter);
            Assert.AreEqual(LimbPart.Leg, hunter.Part);
            Assert.IsFalse(hunter.HeavyRoundsOnly, "追猎者每次命中都可能重创腿部");
        }

        [Test] public void HeavySwingRollsOnlyOnEvenRounds()
        {
            var run = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 200) }, catalog.Rules());
            Assert.AreEqual(1, battle.Round);
            Assert.IsFalse(battle.Current.Player);

            // 奇数回合只有斩击：除了 Advance 抽下一次行动的形态，不消耗任何随机数。
            uint before = run.Random.State;
            battle.ExecuteEnemy();
            Assert.IsFalse(battle.Events.Any(e => e.Kind == "limb"), "斩击回合不该造成部位伤害");
            Assert.AreEqual(Drained(before, 1), run.Random.State, "斩击回合不掷部位概率");

            Assert.IsTrue(battle.Current.Player);
            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, battle.Enemies[0].Id, 0), out error), error);
            Assert.AreEqual(2, battle.Round);
            Assert.IsFalse(battle.Current.Player);

            // 偶数回合是重劈：多出来的那一次抽取就是部位概率，命中与否都会先掷。
            uint before2 = run.Random.State;
            battle.ExecuteEnemy();
            Assert.AreEqual(Drained(before2, 2), run.Random.State, "重劈命中后正好多掷一次部位概率");
        }

        [Test] public void IntentWarnsAboutTheThreatWithoutConsumingADraw()
        {
            var run = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 200) }, catalog.Rules());
            var foe = battle.Enemies[0];
            uint before = run.Random.State;
            Assert.IsFalse(battle.Intent(foe).Contains("可能重创"), "斩击回合不该预告部位威胁");
            Assert.AreEqual(before, run.Random.State, "行动预告不消耗随机序列");

            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, battle.Enemies[0].Id, 0), out error), error);
            Assert.IsTrue(battle.Intent(foe).Contains("可能重创手臂"), "重劈回合必须预告部位威胁");

            var hunterRun = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var hunterBattle = new BattleEngine(hunterRun, new[] { Foe(EnemyPattern.Hunter, 200) }, catalog.Rules());
            Assert.IsTrue(hunterBattle.Intent(hunterBattle.Enemies[0]).Contains("可能重创腿部"));

            var whisperRun = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var whisperBattle = new BattleEngine(whisperRun, new[] { Foe(EnemyPattern.Whisperer, 200) }, catalog.Rules());
            Assert.IsFalse(whisperBattle.Intent(whisperBattle.Enemies[0]).Contains("可能重创"));
        }

        [Test] public void WhispererBattleNeverDamagesLimbs()
        {
            var run = Run(50);
            var specs = catalog.Enemies.Select(x => x.ToSpec()).ToArray();
            foreach (var spec in specs) spec.Pattern = EnemyPattern.Whisperer;
            var battle = new BattleEngine(run, specs, catalog.Rules());
            int actions = 0;
            while (battle.Outcome == BattleOutcome.Active && actions++ < 240)
            {
                if (!battle.Current.Player) { battle.ExecuteEnemy(); continue; }
                PlaySomething(battle);
            }
            Assert.IsFalse(battle.Events.Any(e => e.Kind == "limb"));
            Assert.IsFalse(run.Heroes.Any(LimbSystem.AnyLimbDamaged), "低语者不该伤到任何部位");
        }

        [Test] public void CleaverBattleEventuallyDamagesAnArm()
        {
            var run = RunState.Create(new[] { Fighter("guard", 500, 1) }, 44972, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            int actions = 0;
            while (battle.Outcome == BattleOutcome.Active && actions++ < 400)
            {
                if (!battle.Current.Player) { battle.ExecuteEnemy(); continue; }
                string error;
                Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, battle.Enemies[0].Id, 0), out error), error);
            }
            Assert.That(battle.Events.Select(e => e.Kind), Does.Contain("limb"), "执斧者的重劈最终应重创手臂");
            Assert.IsTrue(LimbSystem.AnyLimbDamaged(run.Heroes[0]));
            Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(run.Heroes[0], LimbPart.Arm), "足够多的重劈应当打断手臂");
        }

        [Test] public void MissingLimbRulesFallBackToTheDefaults()
        {
            var broken = LimbSystem.Resolve(new LimbRules { HitRatio = 0, Parts = new LimbPartRule[0], Prosthetics = new ProstheticSpec[0] });
            Assert.AreEqual(4, broken.Parts.Length, "旧资产缺字段时回落到默认部位表");
            Assert.AreEqual(4, broken.Prosthetics.Length, "义肢表回落到四件默认义肢");
            Assert.That(broken.HitRatio, Is.EqualTo(LimbSystem.DefaultHitRatio).Within(1e-12));
            Assert.AreEqual(4, LimbSystem.Resolve(null).Parts.Length);

            var hero = Run().Heroes[0];
            int hit = LimbSystem.HitAmount(hero, LimbPart.Arm, broken);
            Assert.IsFalse(LimbSystem.Damage(hero, LimbPart.Arm, hit));
            Assert.IsTrue(LimbSystem.Damage(hero, LimbPart.Arm, hit), "回落到默认值后仍然是一次重伤两次断肢");
        }
    }
}
