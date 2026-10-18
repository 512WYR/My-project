using System.Linq;
using NUnit.Framework;
using Ruin.Core;

namespace Ruin.Tests
{
    // The eight accessories from Docs/饰品设计记录.md. Every test builds its own small party and uses
    // the public battle API, so it never depends on the catalog's character set or on a particular seed.
    public class AccessoryRulesTests
    {
        private static SkillSpec Strike(string id = "h_hit")
        {
            return new SkillSpec
            {
                Id = id, Name = "击", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Multiplier = 1, ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9
            };
        }

        private static SkillSpec Heal(string id = "m_heal", bool party = false)
        {
            return new SkillSpec
            {
                Id = id, Name = "治疗", Effect = SkillEffect.Heal, Target = TargetSide.Ally,
                Multiplier = 1, ActorRanks = 15, TargetRanks = 15, Accuracy = 0.9,
                Targeting = party ? SkillTargeting.All : SkillTargeting.Choose,
                Area = party ? SkillArea.Party : SkillArea.Single
            };
        }

        private static ActorSpec HeroSpec(string id, string silhouette, int attack = 10, int maxHp = 40,
            double accuracy = 1.0, params SkillSpec[] skills)
        {
            return new ActorSpec
            {
                Id = id, Name = id, Silhouette = silhouette, MaxHp = maxHp, Attack = attack,
                Accuracy = accuracy, Speed = 10, Defense = 0, Dodge = 0, CritChance = 0,
                BasicSkills = skills == null || skills.Length == 0 ? ActorSpec.EmptySkills : skills
            };
        }

        private static ActorSpec Foe(int maxHp = 10000, int attack = 0, int speed = 1,
            EnemyPattern pattern = EnemyPattern.Cleaver)
        {
            return new ActorSpec
            {
                Id = "enemy", Name = "木桩", MaxHp = maxHp, Attack = attack, Accuracy = 1.0,
                Speed = speed, Defense = 0, Dodge = 0, CritChance = 0, Pattern = pattern
            };
        }

        private static RunState Run(CombatRules rules, params ActorSpec[] heroes)
        {
            return RunState.Create(heroes, 9751, 50, rules);
        }

        private static BattleEngine Battle(RunState run, CombatRules rules, ActorSpec enemy = null)
        {
            return new BattleEngine(run, new[] { enemy ?? Foe() }, rules);
        }

        private static AccessorySpec Find(string id) { return AccessorySystem.Find(id); }

        // Puts an accessory into the stock and equips it, exactly as the supply room does.
        private static void Equip(RunState run, ActorState hero, string id)
        {
            run.AccessoryStock.Add(AccessorySystem.Clone(Find(id)));
            string note;
            Assert.That(AccessorySystem.Equip(run, hero, run.AccessoryStock.Count - 1, out note), Is.True, note);
        }

        private static void Hit(BattleEngine battle, ActorState hero, ActorState target)
        {
            string error;
            Assert.That(battle.Current, Is.SameAs(hero));
            Assert.That(battle.Execute(new ActionRequest(ActionKind.Skill, target.Id, 0), out error), Is.True, error);
        }

        [Test]
        public void DefaultsHoldExactlyEightUniqueAccessoriesAndFourRestrictions()
        {
            Assert.AreEqual(8, AccessorySystem.Defaults.Length);
            Assert.AreEqual(8, AccessorySystem.Defaults.Select(a => a.Id).Distinct().Count());
            Assert.That(AccessorySystem.Defaults.All(a => a != null && !string.IsNullOrEmpty(a.Name)));
            Assert.AreEqual("gunner", Find("acc_gutter").RestrictSilhouette);
            Assert.AreEqual("medic", Find("acc_syringe").RestrictSilhouette);
            Assert.AreEqual("warrior", Find("acc_rust").RestrictSilhouette);
            Assert.AreEqual("shield", Find("acc_martyr").RestrictSilhouette);
            Assert.IsNull(Find("acc_apple").RestrictSilhouette);
        }

        [Test]
        public void EquipMovesStockAndTheAppleChangesTheMaximum()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", "warrior"));
            var hero = run.Heroes[0];
            run.AccessoryStock.Add(AccessorySystem.Clone(Find("acc_apple")));
            string note;

            Assert.That(AccessorySystem.Equip(run, hero, 0, out note), Is.True, note);
            Assert.AreEqual(0, run.AccessoryStock.Count);
            Assert.AreEqual(50, hero.Definition.MaxHp);
            Assert.AreEqual(1, hero.EquippedAccessories.Count);

            run.AccessoryStock.Add(AccessorySystem.Clone(Find("acc_apple")));
            Assert.That(AccessorySystem.Equip(run, hero, 0, out note), Is.False);
            Assert.IsNotNull(note);

            Assert.That(AccessorySystem.Unequip(run, hero, "acc_apple", out note), Is.True, note);
            Assert.AreEqual(40, hero.Definition.MaxHp);
            Assert.AreEqual(1, run.AccessoryStock.Count);
        }

        [Test]
        public void RestrictedAccessoriesOnlyFitTheirOwnSilhouette()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("w", "warrior"), HeroSpec("g", "gunner"));
            var warrior = run.Heroes[0];
            var gunner = run.Heroes[1];

            run.AccessoryStock.Add(AccessorySystem.Clone(Find("acc_gutter")));
            string note;
            Assert.That(AccessorySystem.Equip(run, warrior, 0, out note), Is.False);
            Assert.IsNotNull(note);
            Assert.That(AccessorySystem.Equip(run, gunner, 0, out note), Is.True, note);
            Assert.AreEqual(1, gunner.EquippedAccessories.Count);
        }

        [Test]
        public void GutterForcesCritRaisesAccuracyAndMaulsItsBearerOnEveryHit()
        {
            var rules = new CombatRules();
            var skill = Strike();
            var run = Run(rules, HeroSpec("g", "gunner", accuracy: 0.5, skills: skill));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_gutter");
            var battle = Battle(run, rules, Foe(maxHp: 100));

            Assert.That(battle.EffectiveAccuracy(hero, skill), Is.EqualTo(0.9).Within(1e-9));

            hero.AccuracyBuff = 0.5; // push the roll to a certainty so the hit can be asserted exactly
            Assert.That(battle.EffectiveAccuracy(hero, skill), Is.EqualTo(1.0).Within(1e-9));
            Hit(battle, hero, battle.Enemies[0]);

            Assert.AreEqual(85, battle.Enemies[0].Hp, "10 attack × 1.5 crit");
            Assert.AreEqual(2, hero.BleedDamage);
            Assert.AreEqual(2, hero.BleedTurns);
            Assert.AreEqual(1, hero.Limbs.Count(l => l.Hp < l.MaxHp), "one random part loses one point");
            Assert.AreEqual(hero.Limbs.Sum(l => l.MaxHp) - 1, hero.Limbs.Sum(l => l.Hp));
        }

        [Test]
        public void SleepPotionSwallowsOnlyTheFirstLethalHit()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", "warrior"));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_sleep");
            var battle = Battle(run, rules);
            hero.Hp = 5;

            battle.Damage(hero, 50, "enemy");
            Assert.AreEqual(5, hero.Hp);
            Assert.IsTrue(hero.AccessoryBattle.SleepTriggered);
            Assert.AreEqual(2, hero.AccessoryBattle.SleepTurns);
            Assert.AreEqual(40, hero.Sanity);
            Assert.AreEqual(1, hero.Limbs.Count(l => l.Hp < l.MaxHp));

            battle.Damage(hero, 50, "enemy");
            Assert.AreEqual(0, hero.Hp);
            Assert.IsFalse(hero.Dead);
        }

        [Test]
        public void GoldenAppleLocksHealthAndLimbsAndDrainsSanity()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", "warrior"));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_apple");
            var battle = Battle(run, rules, Foe(attack: 1, speed: 100, pattern: EnemyPattern.Hunter));

            Assert.AreEqual(50, hero.Definition.MaxHp);
            Assert.IsTrue(AccessorySystem.HpLock(hero));
            Assert.IsTrue(AccessorySystem.LimbLock(hero));

            hero.Hp = 10;
            battle.Damage(hero, 100, "enemy");
            Assert.AreEqual(1, hero.Hp);
            Assert.AreEqual(45, hero.Sanity);

            battle.ExecuteEnemy();
            Assert.AreEqual(1, hero.Hp, "the HP track is locked at one");
            Assert.That(hero.Limbs.All(l => l.Hp == l.MaxHp), "limb damage never happens while the apple is worn");
        }

        [Test]
        public void GoldenAppleCanKillWhenSanityReachesZero()
        {
            bool died = false;
            for (uint seed = 1; seed <= 200 && !died; seed++)
            {
                var rules = new CombatRules();
                var run = RunState.Create(new[] { HeroSpec("a", "warrior") }, seed, 50, rules);
                var hero = run.Heroes[0];
                Equip(run, hero, "acc_apple");
                var battle = new BattleEngine(run, new[] { Foe() }, rules);
                hero.Sanity = 5;
                battle.Damage(hero, 1, "enemy");
                died = hero.Dead;
            }
            Assert.IsTrue(died, "the death check is a roll, so at least one of 200 seeds should fail it");
        }

        [Test]
        public void AddictionRaisesAttackAndVulnerabilityAndEndsOnAKill()
        {
            var rules = new CombatRules();
            var skill = Strike();
            var run = Run(rules, HeroSpec("a", "warrior", skills: skill));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_addiction");
            var battle = Battle(run, rules, Foe());

            Assert.IsTrue(hero.AccessoryBattle.AddictionActive);
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(AccessorySystem.DamageTakenFactor(hero), Is.EqualTo(1.0).Within(1e-9));

            Hit(battle, hero, battle.Enemies[0]);
            Assert.AreEqual(55, hero.Sanity);
            Assert.AreEqual(1, hero.AccessoryBattle.AddictionStacks);
            Assert.That(AccessorySystem.DamageTakenFactor(hero), Is.EqualTo(1.15).Within(1e-9));

            battle.Damage(battle.Enemies[0], 100000, hero.Id);
            Assert.IsTrue(battle.Enemies[0].Dead);
            Assert.IsFalse(hero.AccessoryBattle.AddictionActive);
            Assert.AreEqual(0, hero.AccessoryBattle.AddictionStacks);
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void HeartMarksATargetAndStaysStrongWhenItDiesWithinTwoRounds()
        {
            var rules = new CombatRules();
            var skill = Strike();
            var run = Run(rules, HeroSpec("a", "warrior", skills: skill));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_heart");
            var battle = Battle(run, rules, Foe(maxHp: 100));

            Assert.IsTrue(hero.AccessoryBattle.HeartActive);
            Assert.AreEqual(battle.Enemies[0].Id, hero.AccessoryBattle.HeartTargetId);
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(1.5).Within(1e-9));
            Assert.AreEqual(1, AccessorySystem.ExtraActions(hero));
            Assert.AreSame(hero, battle.Current);
            Hit(battle, hero, battle.Enemies[0]);
            Assert.AreSame(hero, battle.Current, "the heart grants an immediate second action");

            battle.Damage(battle.Enemies[0], 100000, hero.Id);
            Assert.IsTrue(hero.AccessoryBattle.HeartResolved);
            Assert.IsTrue(hero.AccessoryBattle.HeartSuccess);
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(1.5).Within(1e-9));
            Assert.AreEqual(1, AccessorySystem.ExtraActions(hero));
        }

        [Test]
        public void HeartWeakensOnRoundThreeWhenTheTargetLives()
        {
            var rules = new CombatRules();
            var skill = Strike();
            var run = Run(rules, HeroSpec("a", "warrior", skills: skill));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_heart");
            var battle = Battle(run, rules, Foe(maxHp: 10000));

            for (int step = 0; step < 30 && battle.Outcome == BattleOutcome.Active && battle.Round < 3; step++)
            {
                if (battle.Current != null && battle.Current.Player)
                {
                    string error;
                    battle.Execute(new ActionRequest(ActionKind.Skill, battle.Enemies[0].Id, 0), out error);
                }
                else battle.ExecuteEnemy();
            }

            Assert.GreaterOrEqual(battle.Round, 3);
            Assert.IsTrue(hero.AccessoryBattle.HeartResolved);
            Assert.IsFalse(hero.AccessoryBattle.HeartSuccess);
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(0.3).Within(1e-9));
            Assert.AreEqual(0, AccessorySystem.ExtraActions(hero));
        }

        [Test]
        public void SyringeDoublesHealingAndChargesOncePerSkill()
        {
            var rules = new CombatRules();
            var heal = Heal(party: true);
            var run = Run(rules,
                HeroSpec("m", "medic", skills: heal),
                HeroSpec("a", "warrior"));
            var medic = run.Heroes[0];
            var ally = run.Heroes[1];
            Equip(run, medic, "acc_syringe");
            medic.Definition.Speed = 20; // the medic has to act first for the heal-skill assertion
            var battle = Battle(run, rules);
            medic.Hp = 10;
            ally.Hp = 10;

            Assert.That(AccessorySystem.HealMultiplier(medic), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(battle.SkillValue(medic, heal), Is.EqualTo(20));

            string error;
            Assert.That(battle.Execute(new ActionRequest(ActionKind.Skill, null, 0), out error), Is.True, error);

            // Both allies heal 20, then the syringe takes 20% of one random member's maximum once.
            Assert.AreEqual(1, battle.Events.Count(e => e.Kind == "damage" && e.Message.Contains("献血针筒")));
            Assert.AreEqual(30 + 30 - 8, medic.Hp + ally.Hp);
        }

        [Test]
        public void RustMedalChargesItsEntryCostAndBuysMoreGambitUses()
        {
            var rules = new CombatRules();
            var gambit = Strike("w_gambit");
            gambit.Uses = 1;
            var run = Run(rules, HeroSpec("w", "warrior", skills: gambit));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_rust");
            var battle = Battle(run, rules, Foe(attack: 0));

            Assert.AreEqual(20, hero.Hp, "half of 40 is paid on entry");
            Assert.AreEqual(2, hero.UsesLeft(gambit));
            Assert.That(AccessorySystem.DamageTakenFactor(hero), Is.EqualTo(1.05).Within(1e-9));

            battle.Damage(hero, 4, "enemy");
            Assert.AreEqual(15, hero.Hp);
            Assert.AreEqual(3, hero.UsesLeft(gambit));
            Assert.AreEqual(1, hero.AccessoryBattle.RustDamageAccum);

            battle.Damage(hero, 8, "enemy");
            Assert.AreEqual(5, hero.UsesLeft(gambit));
        }

        [Test]
        public void MartyrChainSpendsALimbAtZeroAndDiesOnceAllFourAreGone()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("s", "shield"));
            var hero = run.Heroes[0];
            Equip(run, hero, "acc_martyr");
            var battle = Battle(run, rules);
            hero.Hp = 5;

            battle.Damage(hero, 50, "enemy");
            Assert.AreEqual(8, hero.Hp, "20% of 40, rounded up");
            Assert.AreEqual(40, hero.Sanity);
            Assert.AreEqual(1, hero.Limbs.Count(l => l.Hp <= 0));

            foreach (var limb in hero.Limbs) LimbSystem.Damage(hero, limb.Part, limb.Hp);
            hero.Hp = 5;
            battle.Damage(hero, 50, "enemy");
            Assert.IsTrue(hero.Dead, "with no limb left to spend, zero HP is death");
        }

        [Test]
        public void MartyrChainHalvesInterceptedDamage()
        {
            var protectedHero = HeroSpec("a", "warrior");
            var shield = HeroSpec("s", "shield");
            var enemy = Foe(attack: 10, speed: 100, pattern: EnemyPattern.Whisperer);

            var plainRules = new CombatRules();
            var plainRun = Run(plainRules, protectedHero, shield);
            var plain = new BattleEngine(plainRun, new[] { enemy }, plainRules);
            plainRun.Heroes[0].ProtectorId = plainRun.Heroes[1].Id;
            plain.ExecuteEnemy();
            int without = plainRun.Heroes[1].Hp;

            var martyrRules = new CombatRules();
            var martyrRun = Run(martyrRules, protectedHero, shield);
            Equip(martyrRun, martyrRun.Heroes[1], "acc_martyr");
            var martyr = new BattleEngine(martyrRun, new[] { enemy }, martyrRules);
            martyrRun.Heroes[0].ProtectorId = martyrRun.Heroes[1].Id;
            martyr.ExecuteEnemy();
            int with = martyrRun.Heroes[1].Hp;

            // The whisperer deals ceil(10 × 0.3) = 3. The martyr chain halves the intercepted hit to 2.
            Assert.AreEqual(37, without);
            Assert.AreEqual(38, with);
        }

        [Test]
        public void AccessoryQueriesNeverSpendRandomDraws()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", "warrior"));
            var hero = run.Heroes[0];
            var battle = new BattleEngine(run, new[] { Foe(speed: 1) }, rules);
            uint before = run.Random.State;

            // Everything the UI and the battle previews read has to stay a pure function of the state.
            Assert.That(AccessorySystem.AttackMultiplier(hero), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(AccessorySystem.DamageTakenFactor(hero), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(AccessorySystem.HealMultiplier(hero), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(AccessorySystem.AccuracyFloor(hero), Is.EqualTo(0.0).Within(1e-9));
            Assert.IsFalse(AccessorySystem.AlwaysCrit(hero));
            Assert.AreEqual(0, AccessorySystem.ExtraActions(hero));
            Assert.AreEqual("无", AccessorySystem.Summary(hero));
            Assert.AreEqual(before, run.Random.State);
            Assert.AreEqual(BattleOutcome.Active, battle.Outcome);
        }
    }
}
