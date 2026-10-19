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

        // 速度 100 保证敌人先手。技能命中拉到 1、目标闪避 0，命中骰不抽取；命中后只剩一次选部位。
        private static ActorSpec Foe(EnemyPattern pattern, int maxHp)
        {
            var spec = Fighter("foe", maxHp, 100);
            spec.Pattern = pattern; spec.Accuracy = 1;
            foreach (var skill in spec.BasicSkills) skill.Accuracy = 1;
            return spec;
        }

        // 无技能 + Simple 走 legacy 路径，用来验证旧敌人也遵守同一套磨损规则。
        private static ActorSpec LegacyFoe(EnemyPattern pattern)
        {
            return new ActorSpec { Id = "legacy", Name = "legacy", MaxHp = 200, Speed = 100, Attack = 10,
                Accuracy = 1, Pattern = pattern, Behavior = EnemyBehavior.Simple,
                BasicSkills = ActorSpec.EmptySkills };
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

        [Test] public void AttackIntentWarnsAboutLimbWearWithoutConsumingADraw()
        {
            var run = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 200) }, catalog.Rules());
            uint before = run.Random.State;
            Assert.IsTrue(battle.Intent(battle.Enemies[0]).Contains("磨损"), "攻击意图应预告部位磨损");
            Assert.AreEqual(before, run.Random.State, "行动预告不消耗随机序列");

            var legacyRun = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var legacyBattle = new BattleEngine(legacyRun, new[] { LegacyFoe(EnemyPattern.Cleaver) }, catalog.Rules());
            uint legacyBefore = legacyRun.Random.State;
            Assert.IsTrue(legacyBattle.Intent(legacyBattle.Enemies[0]).Contains("磨损"));
            Assert.AreEqual(legacyBefore, legacyRun.Random.State, "legacy 预告同样不消耗随机序列");
        }

        [Test] public void WhispererPhysicalHitsStillWearLimbs()
        {
            // Legacy 低语者仍造成 HP 伤害，所以会磨损；只带理智技能的 designed 版本不会。
            var run = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { LegacyFoe(EnemyPattern.Whisperer) }, catalog.Rules());
            battle.ExecuteEnemy();
            Assert.IsTrue(battle.Events.Any(e => e.Kind == "limb"), "低语者的物理命中同样磨损部位");
            Assert.IsTrue(LimbSystem.AnyLimbDamaged(run.Heroes[0]));

            var whisper = Fighter("whisperer", 300, 100);
            whisper.Behavior = EnemyBehavior.Sanity;
            whisper.BasicSkills = new[]
            {
                new SkillSpec { Id = "whisper", Name = "低语", Effect = SkillEffect.Sanity, Target = TargetSide.Enemy,
                    TargetRanks = 15, SanityDamage = 10, Accuracy = 1 }
            };
            var sanityRun = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var sanityBattle = new BattleEngine(sanityRun, new[] { whisper }, catalog.Rules());
            sanityBattle.ExecuteEnemy();
            Assert.IsFalse(sanityBattle.Events.Any(e => e.Kind == "limb"), "纯理智技能不造成 HP 伤害，也不磨损部位");
            Assert.IsFalse(LimbSystem.AnyLimbDamaged(sanityRun.Heroes[0]));
        }

        [Test] public void LandedHitsWearRandomUnseveredLimbsProportionally()
        {
            var run = RunState.Create(new[] { Fighter("guard", 500, 1) }, 44972, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            int actions = 0;
            while (battle.Outcome == BattleOutcome.Active && actions++ < 200
                && battle.Events.Count(e => e.Kind == "limb") < 6)
            {
                if (battle.Current.Player) { battle.SkipCurrentTurn(out _); continue; }
                battle.ExecuteEnemy();
            }

            var limbs = battle.Events.Where(e => e.Kind == "limb").ToList();
            Assert.That(limbs, Is.Not.Empty, "命中的攻击必须磨损部位");
            // 英雄防御 0，Foe 技能伤害 10，默认比例 0.5，所以每击正好 5 点。
            Assert.IsTrue(limbs.All(e => e.Amount == 5), "部位损伤应为实际伤害的一半");
            var hero = run.Heroes[0];
            int lost = EveryPart.Sum(p => LimbSystem.Get(hero, p).MaxHp - LimbSystem.Get(hero, p).Hp);
            Assert.AreEqual(5 * limbs.Count, lost, "每次命中只磨损一个部位");
            Assert.IsTrue(LimbSystem.AnyLimbDamaged(hero));
        }

        [Test] public void LimbWearUsesPostMitigationDamage()
        {
            var plain = RunState.Create(new[] { Fighter("plain", 200, 1) }, 1, 50, catalog.Rules());
            var guarded = RunState.Create(new[] { Fighter("guarded", 200, 1) }, 1, 50, catalog.Rules());
            guarded.Heroes[0].Definition.Defense = 0.5;
            var a = new BattleEngine(plain, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            var b = new BattleEngine(guarded, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            a.ExecuteEnemy();
            b.ExecuteEnemy();

            Assert.AreEqual(5, a.Events.Where(e => e.Kind == "limb").Sum(e => e.Amount));
            Assert.AreEqual(3, b.Events.Where(e => e.Kind == "limb").Sum(e => e.Amount),
                "防御把 10 点伤害降到 5 点，部位损伤随实际伤害降到 3");
        }

        [Test] public void LimbWearNeverTouchesASeveredPart()
        {
            var run = RunState.Create(new[] { Fighter("guard", 500, 1) }, 1, 50, catalog.Rules());
            var hero = run.Heroes[0];
            foreach (var part in new[] { LimbPart.Head, LimbPart.Torso, LimbPart.Arm })
                LimbSystem.Damage(hero, part, 9999);
            int legBefore = LimbSystem.Get(hero, LimbPart.Leg).Hp;

            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            int hits = 0;
            while (battle.Outcome == BattleOutcome.Active && hits < 5)
            {
                if (battle.Current.Player) { battle.SkipCurrentTurn(out _); continue; }
                battle.ExecuteEnemy();
                hits++;
            }

            var events = battle.Events.Where(e => e.Kind == "limb").ToList();
            Assert.AreEqual(5, events.Count);
            Assert.AreEqual(legBefore - events.Sum(e => e.Amount), LimbSystem.Get(hero, LimbPart.Leg).Hp,
                "只剩腿部时，所有磨损都应落在腿部");
            foreach (var part in new[] { LimbPart.Head, LimbPart.Torso, LimbPart.Arm })
                Assert.AreEqual(LimbCondition.Severed, LimbSystem.ConditionOf(hero, part), "已断部位不再承受伤害");
        }

        [Test] public void GoldenAppleLocksLimbWearWithoutConsumingADraw()
        {
            var run = RunState.Create(new[] { Fighter("guard", 200, 1) }, 1, 50, catalog.Rules());
            var hero = run.Heroes[0];
            AccessorySystem.Attach(hero, AccessorySystem.Find("acc_apple"));
            var battle = new BattleEngine(run, new[] { Foe(EnemyPattern.Cleaver, 300) }, catalog.Rules());
            uint before = run.Random.State;
            battle.ExecuteEnemy();

            Assert.IsFalse(battle.Events.Any(e => e.Kind == "limb"), "金丝苹果锁定部位轨道");
            Assert.IsFalse(LimbSystem.AnyLimbDamaged(hero));
            // Advance 切到玩家时会抽取一次技能形态；除此之外没有部位抽取。
            Assert.AreEqual(Drained(before, 1), run.Random.State, "锁定在部位抽取之前返回，随机序列只前进到形态抽取");
        }

        [Test] public void MultiTargetAttackWearsEachVictimOnce()
        {
            var skill = new SkillSpec { Id = "sweep", Name = "横扫", Effect = SkillEffect.Attack,
                Target = TargetSide.Enemy, Multiplier = 1, ActorRanks = 15, TargetRanks = 15,
                Accuracy = 1, TargetCount = 2 };
            var foe = Fighter("foe", 300, 100);
            foe.Attack = 10; foe.Accuracy = 1;
            foe.BasicSkills = new[] { skill };
            var run = RunState.Create(new[] { Fighter("a", 100, 1), Fighter("b", 100, 2) }, 1, 50, catalog.Rules());
            var battle = new BattleEngine(run, new[] { foe }, catalog.Rules());
            battle.ExecuteEnemy();

            var events = battle.Events.Where(e => e.Kind == "limb").ToList();
            Assert.AreEqual(2, events.Count, "多目标技能每个命中英雄各磨损一个部位");
            Assert.IsTrue(events.All(e => e.Amount == 5));
            Assert.IsTrue(run.Heroes.All(LimbSystem.AnyLimbDamaged));
        }

        [Test] public void MissingLimbRulesFallBackToTheDefaults()
        {
            var broken = LimbSystem.Resolve(new LimbRules { HitRatio = 0, Parts = new LimbPartRule[0], Prosthetics = new ProstheticSpec[0] });
            Assert.AreEqual(4, broken.Parts.Length, "旧资产缺字段时回落到默认部位表");
            Assert.AreEqual(4, broken.Prosthetics.Length, "义肢表回落到四件默认义肢");
            Assert.That(broken.HitRatio, Is.EqualTo(LimbSystem.DefaultHitRatio).Within(1e-12));
            Assert.That(broken.DamageRatio, Is.EqualTo(LimbSystem.DefaultDamageRatio).Within(1e-12),
                "旧资产缺字段时回落默认磨损比例");
            Assert.AreEqual(4, LimbSystem.Resolve(null).Parts.Length);

            var hero = Run().Heroes[0];
            int hit = LimbSystem.HitAmount(hero, LimbPart.Arm, broken);
            Assert.IsFalse(LimbSystem.Damage(hero, LimbPart.Arm, hit));
            Assert.IsTrue(LimbSystem.Damage(hero, LimbPart.Arm, hit), "回落到默认值后仍然是一次重伤两次断肢");
        }
    }
}
