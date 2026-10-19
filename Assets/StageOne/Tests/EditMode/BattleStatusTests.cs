using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;

namespace Ruin.Tests
{
    // BattleStatus is the single aggregation used by both the cut-in feet strip and the persistent card,
    // so these tests pin the concrete names, the numbers and the ordering rather than the UI.
    public class BattleStatusTests
    {
        private static ActorState Hero()
        {
            var spec = new ActorSpec
            {
                Id = "hero",
                Name = "战士",
                MaxHp = 100,
                Attack = 10,
                Accuracy = 0.9
            };
            return new ActorState
            {
                Id = spec.Id,
                Definition = spec,
                Player = true,
                Hp = 100,
                Limbs = LimbSystem.CreateLimbs(spec.MaxHp, new CombatRules().Limbs)
            };
        }

        private static List<BattleStatusEntry> Entries(ActorState actor)
        {
            var entries = new List<BattleStatusEntry>();
            BattleStatus.Build(actor, entries);
            return entries;
        }

        private static BattleStatusEntry Entry(ActorState actor, string name)
        {
            return Entries(actor).First(e => e.Name == name);
        }

        [Test]
        public void BuffsUseConcreteNamesAndPercentages()
        {
            var hero = Hero();
            hero.BlockCharges = 2;
            hero.Counter = true;
            hero.Followup = true;
            hero.DefenseBuff = 0.2;
            hero.AccuracyBuff = 0.1;
            hero.ProtectorId = "ally";

            var entries = Entries(hero);
            CollectionAssert.AreEqual(
                new[] { "格挡", "反击", "追击", "防御力", "命中", "守护" },
                entries.Select(e => e.Name).ToArray());
            Assert.That(entries[0].Value, Is.EqualTo("×2"));
            Assert.That(entries[3].Value, Is.EqualTo("+20%"));
            Assert.That(entries[4].Value, Is.EqualTo("+10%"));
            Assert.That(entries[0].Detail, Does.Contain("免除"));
            Assert.That(entries[3].Detail, Does.Contain("20%"));
        }

        [Test]
        public void NegativeStatusesAndSanityLockAreListed()
        {
            var hero = Hero();
            hero.BleedDamage = 3;
            hero.BleedTurns = 2;
            hero.Imbalance = 2;
            hero.SelfVulnerable = 0.25;
            hero.SanityLocked = true;

            var entries = Entries(hero);
            CollectionAssert.AreEqual(
                new[] { "流血", "失衡", "易伤", "理智锁定" },
                entries.Select(e => e.Name).ToArray());
            Assert.That(entries[0].Value, Is.EqualTo("3×2"));
            Assert.That(entries[1].Value, Is.EqualTo("×2"));
            Assert.That(entries[2].Value, Is.EqualTo("+25%"));
            Assert.That(entries[3].Tone, Is.EqualTo(BattleStatusTone.Meta));
        }

        [Test]
        public void SeveredAndInjuredLimbsShowPartAndAttribute()
        {
            var hero = Hero();
            Assert.IsTrue(LimbSystem.Damage(hero, LimbPart.Arm, 999));
            var leg = LimbSystem.Get(hero, LimbPart.Leg);
            LimbSystem.Damage(hero, LimbPart.Leg, leg.MaxHp / 2 + 1);

            var entries = Entries(hero);
            var arm = entries.First(e => e.Id == "limb:" + LimbPart.Arm);
            var injuredLeg = entries.First(e => e.Id == "limb:" + LimbPart.Leg);
            Assert.That(arm.Name, Is.EqualTo("断肢·手臂"));
            Assert.That(arm.Detail, Does.Contain("攻击"));
            Assert.That(injuredLeg.Name, Is.EqualTo("受伤·腿部"));
            Assert.That(injuredLeg.Detail, Does.Contain("速度"));
            Assert.That(arm.Tone, Is.EqualTo(BattleStatusTone.Injury));
        }

        [Test]
        public void FittedProstheticIsNotReportedAsAnInjury()
        {
            var hero = Hero();
            LimbSystem.Damage(hero, LimbPart.Arm, 999);
            var prosthetic = LimbSystem.Prosthetic("prosthetic_arm", new CombatRules().Limbs);
            Assert.IsTrue(LimbSystem.Install(hero, prosthetic));

            var entries = Entries(hero);
            Assert.IsFalse(entries.Any(e => e.Id == "limb:" + LimbPart.Arm));
        }

        [Test]
        public void AccessoryEffectsAggregateStacks()
        {
            var hero = Hero();
            hero.EquippedAccessories.Add(new AccessorySpec { Id = "addiction", Kind = AccessoryKind.Addiction });
            hero.EquippedAccessories.Add(new AccessorySpec { Id = "heart", Kind = AccessoryKind.Heart });
            hero.EquippedAccessories.Add(new AccessorySpec { Id = "rust", Kind = AccessoryKind.RustMedal });
            hero.AccessoryBattle.AddictionActive = true;
            hero.AccessoryBattle.AddictionStacks = 2;
            hero.AccessoryBattle.HeartResolved = true;
            hero.AccessoryBattle.HeartSuccess = false;
            hero.AccessoryBattle.SleepTurns = 2;

            var entries = Entries(hero);
            Assert.That(entries.Count(e => e.Id == "addiction-attack" && e.Value == "+30%"), Is.EqualTo(1));
            Assert.That(entries.Count(e => e.Id == "addiction-vulnerable" && e.Value == "+30%"), Is.EqualTo(1));
            Assert.That(entries.First(e => e.Id == "heart-attack").Value, Is.EqualTo("-70%"));
            Assert.That(entries.First(e => e.Id == "sleep").Value, Is.EqualTo("2回合"));
            Assert.That(entries.First(e => e.Id == "rust").Value, Is.EqualTo("+5%"));
        }

        [Test]
        public void CompactTruncatesWithARemainder()
        {
            var hero = Hero();
            hero.BlockCharges = 1;
            hero.Counter = true;
            hero.Followup = true;
            hero.DefenseBuff = 0.2;
            hero.AccuracyBuff = 0.1;

            string compact = BattleStatus.Compact(hero, 4);
            Assert.That(compact, Does.StartWith("格挡×1 · 反击 · 追击 · 防御力+20%"));
            Assert.That(compact, Does.EndWith(" +1"));
        }

        [Test]
        public void EmptyAndNullActorsAreSafe()
        {
            Assert.That(BattleStatus.Compact(null), Is.Empty);
            var entries = new List<BattleStatusEntry>();
            BattleStatus.Build(null, entries);
            Assert.That(entries, Is.Empty);
            Assert.That(BattleStatus.Compact(Hero()), Is.Empty);
            Assert.DoesNotThrow(() => BattleStatus.Build(new ActorState { Limbs = null, AccessoryBattle = null }, entries));
        }
    }
}
