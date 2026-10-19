using System.Linq;
using NUnit.Framework;
using Ruin.Core;

namespace Ruin.Tests
{
    // The three items from Docs/一次性道具目录.md. Every test builds its own small party, so it never
    // depends on the catalog's characters or on a particular seed.
    public class ConsumableRulesTests
    {
        private static SkillSpec Strike(string id = "h_hit")
        {
            return new SkillSpec
            {
                Id = id, Name = "击", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Multiplier = 1, ActorRanks = 15, TargetRanks = 15, Accuracy = 1.0
            };
        }

        private static ActorSpec HeroSpec(string id, string silhouette = "warrior", int attack = 10,
            int maxHp = 40, int speed = 10, params SkillSpec[] skills)
        {
            return new ActorSpec
            {
                Id = id, Name = id, Silhouette = silhouette, MaxHp = maxHp, Attack = attack,
                Accuracy = 1.0, Speed = speed, Defense = 0, Dodge = 0, CritChance = 0,
                BasicSkills = skills == null || skills.Length == 0 ? ActorSpec.EmptySkills : skills
            };
        }

        private static ActorSpec Foe(int maxHp = 10000, int attack = 0, int speed = 1)
        {
            return new ActorSpec
            {
                Id = "enemy", Name = "木桩", MaxHp = maxHp, Attack = attack, Accuracy = 1.0,
                Speed = speed, Defense = 0, Dodge = 0, CritChance = 0, Pattern = EnemyPattern.Cleaver
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

        private static ConsumableSpec Find(string id) { return ConsumableSystem.Find(id); }

        private static void Grant(RunState run, ConsumableKind kind)
        {
            run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.ForKind(kind)));
        }

        [Test]
        public void DefaultsHoldExactlyThreeUniqueImplementedItems()
        {
            Assert.AreEqual(3, ConsumableSystem.Defaults.Length);
            Assert.AreEqual(3, ConsumableSystem.Defaults.Select(c => c.Id).Distinct().Count());
            Assert.That(ConsumableSystem.Defaults.All(c => c != null
                && !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.Description)));
            Assert.IsNotNull(Find("con_sanity_lock"));
            Assert.IsNotNull(Find("con_hp_restore"));
            Assert.IsNotNull(Find("con_surgery_kit"));
            Assert.That(ConsumableSystem.Defaults.All(c => c.Implemented), "every catalogued item is usable");
            Assert.AreEqual(ConsumableKind.SurgeryKit, Find("con_surgery_kit").Kind);
        }

        [Test]
        public void HealingPotionRestoresHalfMaxRoundedUpAndClampsAtTheMaximum()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.HpRestore);
            hero.Hp = 10;

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.True, note);
            Assert.AreEqual(30, hero.Hp, "50% of 40 is 20");
            Assert.AreEqual(0, run.Consumables.Count);

            // 41 maximum: half is 20.5, rounded up to 21.
            var odd = Run(rules, HeroSpec("b", maxHp: 41));
            odd.Heroes[0].Hp = 0;
            Grant(odd, ConsumableKind.HpRestore);
            Assert.That(ConsumableSystem.Use(odd, odd.Heroes[0], 0, false, out note), Is.True, note);
            Assert.AreEqual(21, odd.Heroes[0].Hp);

            var nearFull = Run(rules, HeroSpec("c", maxHp: 40));
            nearFull.Heroes[0].Hp = 35;
            Grant(nearFull, ConsumableKind.HpRestore);
            Assert.That(ConsumableSystem.Use(nearFull, nearFull.Heroes[0], 0, false, out note), Is.True, note);
            Assert.AreEqual(40, nearFull.Heroes[0].Hp, "the heal stops at the maximum");
        }

        [Test]
        public void HealingPotionRefusesFullHealthAndDeadHeroesWithoutSpendingTheItem()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.HpRestore);

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.False);
            Assert.AreEqual("生命已满。", note);
            Assert.AreEqual(1, run.Consumables.Count, "a refused use keeps the item");

            hero.Dead = true;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.False);
            Assert.AreEqual("已经阵亡。", note);
            Assert.AreEqual(1, run.Consumables.Count);
        }

        [Test]
        public void HealingPotionRescuesADeathDoorHero()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            hero.Hp = 0;
            Assert.IsTrue(hero.DeathDoor);
            Grant(run, ConsumableKind.HpRestore);

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.True, note);
            Assert.AreEqual(20, hero.Hp);
            Assert.IsFalse(hero.DeathDoor);
        }

        [Test]
        public void SanityLockPrimesTheNextBattleAndReleasesWhenItSettles()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.SanityLock);

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.True, note);
            Assert.IsTrue(hero.SanityLockPending);
            Assert.IsFalse(hero.SanityLocked, "the lock only matures when the battle starts");
            Assert.AreEqual(0, run.Consumables.Count);

            var battle = Battle(run, rules);
            Assert.IsTrue(hero.SanityLocked);
            Assert.IsFalse(hero.SanityLockPending);

            double sanity = hero.Sanity;
            uint state = run.Random.State;
            battle.ChangeSanity(hero, -30, "测试");
            Assert.AreEqual(sanity, hero.Sanity);
            battle.ChangeSanity(hero, 15, "测试");
            Assert.AreEqual(sanity, hero.Sanity);
            Assert.AreEqual(state, run.Random.State, "a locked sanity change never draws");

            battle.Finish(BattleOutcome.Retreat);
            Assert.IsFalse(hero.SanityLocked);
            Assert.IsFalse(hero.SanityLockPending);

            var next = Battle(run, rules);
            Assert.IsFalse(hero.SanityLocked, "the lock lasts exactly one battle");
            next.ChangeSanity(hero, -5, "测试");
            Assert.AreEqual(sanity - 5, hero.Sanity);
        }

        [Test]
        public void ASanityLockCannotBeStackedAndTheSecondItemIsKept()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.SanityLock);
            Grant(run, ConsumableKind.SanityLock);

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.True, note);
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.False);
            Assert.AreEqual("理智已经被锁定。", note);
            Assert.AreEqual(1, run.Consumables.Count);

            var battle = Battle(run, rules);
            Assert.That(ConsumableSystem.Use(run, hero, 0, true, out note), Is.False);
            Assert.AreEqual("理智已经被锁定。", note);
            Assert.AreEqual(1, run.Consumables.Count);
        }

        [Test]
        public void ABattleConsumableSpendsTheCurrentHeroesAction()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40, speed: 10));
            var hero = run.Heroes[0];
            hero.Hp = 10;
            Grant(run, ConsumableKind.HpRestore);
            var battle = Battle(run, rules, Foe(speed: 1));

            Assert.AreSame(hero, battle.Current);
            string note;
            Assert.That(battle.UseConsumable(0, hero.Id, out note), Is.True, note);
            Assert.AreEqual(30, hero.Hp);
            Assert.AreEqual(0, run.Consumables.Count);
            Assert.IsFalse(battle.Current == hero, "using an item consumes the action");
            Assert.AreEqual(1, battle.Events.Count(e => e.Kind == "heal" && e.Message.Contains("血量恢复药")));
        }

        [Test]
        public void ABattleConsumableIsRefusedOutsideOurTurnAndKeepsTheStock()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40, speed: 10));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.HpRestore);
            Grant(run, ConsumableKind.HpRestore);
            hero.Hp = 10;
            var battle = Battle(run, rules, Foe(speed: 1));

            string note;
            Assert.That(battle.UseConsumable(0, hero.Id, out note), Is.True, note);
            Assert.That(battle.UseConsumable(0, hero.Id, out note), Is.False);
            Assert.AreEqual("现在不是我方行动。", note);
            Assert.AreEqual(1, run.Consumables.Count, "a refused item stays in the stock");
        }

        [Test]
        public void TheSurgeryKitFitsAStockedProstheticAndSpendsExactlyOneKit()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            LimbSystem.Get(hero, LimbPart.Arm).Hp = 0;
            run.Prosthetics.Add(LimbSystem.Clone(LimbSystem.Prosthetic("prosthetic_arm", rules.Limbs)));
            Grant(run, ConsumableKind.SurgeryKit);
            Grant(run, ConsumableKind.SurgeryKit);
            Grant(run, ConsumableKind.HpRestore);

            string note;
            Assert.That(ConsumableSystem.UseSurgeryKit(run, hero, run.Prosthetics[0], out note), Is.True, note);
            Assert.IsTrue(LimbSystem.HasProsthetic(hero, LimbPart.Arm));
            Assert.AreEqual(LimbSystem.Get(hero, LimbPart.Arm).MaxHp, LimbSystem.Get(hero, LimbPart.Arm).Hp);
            Assert.AreEqual(1, ConsumableSystem.Count(run, ConsumableKind.SurgeryKit), "one kit per fitting");
            Assert.AreEqual(1, ConsumableSystem.Count(run, ConsumableKind.HpRestore), "other items stay put");
            Assert.AreEqual(0, run.Prosthetics.Count, "the fitted piece leaves the stock");
        }

        [Test]
        public void TheSurgeryKitKeepsBothItemsWhenTheFittingIsRefused()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            run.Prosthetics.Add(LimbSystem.Clone(LimbSystem.Prosthetic("prosthetic_arm", rules.Limbs)));

            string note;
            // No kit yet, limb already severed.
            LimbSystem.Get(hero, LimbPart.Arm).Hp = 0;
            Assert.That(ConsumableSystem.UseSurgeryKit(run, hero, run.Prosthetics[0], out note), Is.False);
            Assert.AreEqual("需要手术包才能在这里安装义体。", note);
            Assert.IsFalse(LimbSystem.HasProsthetic(hero, LimbPart.Arm));
            Assert.AreEqual(1, run.Prosthetics.Count);

            // Kit in hand, but the part is still flesh.
            Grant(run, ConsumableKind.SurgeryKit);
            LimbSystem.Get(hero, LimbPart.Arm).Hp = LimbSystem.Get(hero, LimbPart.Arm).MaxHp;
            Assert.That(ConsumableSystem.UseSurgeryKit(run, hero, run.Prosthetics[0], out note), Is.False);
            Assert.AreEqual("只能安装在断肢上。", note);
            Assert.AreEqual(1, ConsumableSystem.Count(run, ConsumableKind.SurgeryKit));
            Assert.AreEqual(1, run.Prosthetics.Count);
        }

        [Test]
        public void TheGenericUsePathPointsAtTheCharacterPanelWithoutSpendingTheKit()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            Grant(run, ConsumableKind.SurgeryKit);

            string note;
            Assert.That(ConsumableSystem.Use(run, hero, 0, false, out note), Is.False);
            Assert.AreEqual("请在角色面板的义体列表中选择要安装的义体。", note);
            Assert.AreEqual(1, run.Consumables.Count);

            var battle = Battle(run, rules, Foe(speed: 1));
            Assert.AreSame(hero, battle.Current);
            Assert.That(battle.UseConsumable(0, hero.Id, out note), Is.False);
            Assert.AreEqual("请在角色面板的义体列表中选择要安装的义体。", note);
            Assert.AreEqual(1, run.Consumables.Count);
            Assert.AreSame(hero, battle.Current, "a refused use does not spend the turn");
        }

        [Test]
        public void ConsumableQueriesNeverSpendRandomDraws()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("a", maxHp: 40));
            var hero = run.Heroes[0];
            hero.Hp = 10;
            Grant(run, ConsumableKind.HpRestore);
            Grant(run, ConsumableKind.SanityLock);
            Grant(run, ConsumableKind.SurgeryKit);
            uint before = run.Random.State;

            Assert.AreEqual(3, run.Consumables.Count);
            Assert.AreEqual(1, ConsumableSystem.Count(run, ConsumableKind.HpRestore));
            Assert.AreEqual("血量恢复药 · 理智锁定药剂 · 手术包", ConsumableSystem.Summary(run));
            Assert.IsNull(ConsumableSystem.UseBlockedReason(run, hero, Find("con_hp_restore"), false));
            Assert.IsNotNull(ConsumableSystem.UseBlockedReason(run, hero, Find("con_surgery_kit"), false));
            Assert.IsNotNull(ConsumableSystem.Find("con_hp_restore"));
            Assert.IsNotNull(ConsumableSystem.ForKind(ConsumableKind.SanityLock));
            Assert.AreEqual(before, run.Random.State);
        }

        [Test]
        public void TheSyringeDoesNotDoubleAPotionHeal()
        {
            var rules = new CombatRules();
            var run = Run(rules, HeroSpec("m", "medic", maxHp: 40));
            var medic = run.Heroes[0];
            run.AccessoryStock.Add(AccessorySystem.Clone(AccessorySystem.Find("acc_syringe")));
            string equipNote;
            Assert.That(AccessorySystem.Equip(run, medic, 0, out equipNote), Is.True, equipNote);
            medic.Hp = 10;
            Grant(run, ConsumableKind.HpRestore);

            uint state = run.Random.State;
            string note;
            Assert.That(ConsumableSystem.Use(run, medic, 0, false, out note), Is.True, note);
            Assert.AreEqual(30, medic.Hp, "the potion is not a healing skill and ignores the syringe");
            Assert.AreEqual(state, run.Random.State, "the potion never triggers the syringe cost");
        }
    }
}
