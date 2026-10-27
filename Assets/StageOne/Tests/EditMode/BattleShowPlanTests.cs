using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace Ruin.Tests
{
    // The cut-in consumes engine events, not combat state. These pin the grouping rules so a change to
    // event wording or ordering shows up here instead of as a missing number on screen.
    public class BattleShowPlanTests
    {
        private GameCatalog catalog;
        private BattleEngine battle;

        [SetUp]
        public void SetUp()
        {
            catalog = Resources.Load<GameCatalog>("StageOne/GameCatalog");
            Assert.NotNull(catalog, "Generate catalog before running tests");
            var run = RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), 44972, 50, catalog.Rules());
            battle = new BattleEngine(run, catalog.Enemies.Select(x => x.ToSpec()).ToArray(), catalog.Rules());
        }

        private BattleShowGroup Single(params BattleEvent[] events)
        {
            var groups = BattleShowPlan.Build(events, battle.Find);
            Assert.That(groups.Count, Is.EqualTo(1));
            return groups[0];
        }

        [Test]
        public void CriticalAttackSumsDamageAndRaisesFlag()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = hero.Id, Message = "锈刃命中（暴击）" },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = enemy.Id, Amount = 12 },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = enemy.Id, Amount = 8 });

            Assert.That(group.Actor, Is.SameAs(hero));
            Assert.IsTrue(group.Critical);
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(enemy));
            Assert.That(group.Results[0].Damage, Is.EqualTo(20));
        }

        [Test]
        public void MultiTargetAttackKeepsOneResultPerTargetInRankOrder()
        {
            var hero = battle.Run.Heroes[0];
            Assert.That(battle.Enemies.Count, Is.GreaterThanOrEqualTo(3));
            var first = battle.Enemies[0];
            var third = battle.Enemies[2];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = hero.Id, Message = "横扫" },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = third.Id, Amount = 5 },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = first.Id, Amount = 9 });

            var expected = new[] { first, third }.OrderBy(e => e.Rank).ThenBy(e => e.Id).ToArray();
            Assert.That(group.Results.Select(r => r.Target).ToArray(), Is.EqualTo(expected));
            Assert.That(group.Results.Count, Is.EqualTo(2));
        }

        // A miss returns before the engine logs its action event. The plan must still build a beat.
        [Test]
        public void MissWithoutActionStillProducesAResult()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(new BattleEvent { Kind = "miss", ActorId = hero.Id, TargetId = enemy.Id });

            Assert.That(group.Actor, Is.SameAs(hero));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.IsTrue(group.Results[0].Missed);
            Assert.That(group.Results[0].Damage, Is.EqualTo(0));
        }

        // Heal, protect and buff skills likewise never log an action event.
        [Test]
        public void HealWithoutActionProducesASelfResult()
        {
            var hero = battle.Run.Heroes[0];
            var group = Single(new BattleEvent { Kind = "heal", ActorId = hero.Id, TargetId = hero.Id, Amount = 7, Message = "恢复 7" });

            Assert.That(group.Actor, Is.SameAs(hero));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(hero));
            Assert.That(group.Results[0].Heal, Is.EqualTo(7));
        }

        [Test]
        public void SameSideGuardBecomesAProtectLabel()
        {
            var caster = battle.Run.Heroes[0];
            var ward = battle.Run.Heroes[1];
            var group = Single(new BattleEvent { Kind = "guard", ActorId = caster.Id, TargetId = ward.Id, Message = "守护" });

            Assert.That(group.Actor, Is.SameAs(caster));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(ward));
            Assert.IsTrue(group.Results[0].Guarded);
            Assert.That(group.Results[0].Label, Is.EqualTo("守护"));
        }

        // A guardian steps in before the engine logs the action. Its guard event opens the beat, then
        // the action and damage move the beat onto the real attacker and onto the guardian's body.
        [Test]
        public void InterceptionMovesTheGuardResultOntoTheGuardian()
        {
            var hero = battle.Run.Heroes[0];
            var attacker = battle.Enemies[0];
            var guardian = battle.Enemies[1];
            var group = Single(
                new BattleEvent
                {
                    Kind = "guard", ActorId = guardian.Id, TargetId = hero.Id,
                    Message = guardian.Name + " 为 " + hero.Name + " 挡下攻击。"
                },
                new BattleEvent { Kind = "action", ActorId = attacker.Id, TargetId = guardian.Id, Message = "攻击" },
                new BattleEvent { Kind = "damage", ActorId = attacker.Id, TargetId = guardian.Id, Amount = 6 });

            Assert.That(group.Actor, Is.SameAs(attacker));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(guardian));
            Assert.That(group.Results[0].Damage, Is.EqualTo(6));
            Assert.IsTrue(group.Results[0].Guarded);
            Assert.That(group.Results[0].Label, Is.EqualTo("格挡"));
        }

        // A block that negates the hit logs the attacker as ActorId and the blocker as TargetId.
        [Test]
        public void BlockLabelsTheBlockerNotTheAttacker()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = hero.Id, Message = "重击" },
                new BattleEvent
                {
                    Kind = "guard", ActorId = hero.Id, TargetId = enemy.Id,
                    Message = enemy.Name + " 的格挡免除了这次攻击。"
                });

            Assert.That(group.Actor, Is.SameAs(hero));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(enemy));
            Assert.IsTrue(group.Results[0].Guarded);
            Assert.That(group.Results[0].Label, Is.EqualTo("格挡"));
        }

        // Interception followed by a miss has no action event, so the beat must still end up showing
        // the attacking enemy rather than the guardian who opened it.
        [Test]
        public void MissAfterInterceptionKeepsTheAttackerAsActor()
        {
            var hero = battle.Run.Heroes[0];
            var attacker = battle.Enemies[0];
            var guardian = battle.Enemies[1];
            var group = Single(
                new BattleEvent
                {
                    Kind = "guard", ActorId = guardian.Id, TargetId = hero.Id,
                    Message = guardian.Name + " 为 " + hero.Name + " 拦下攻击。"
                },
                new BattleEvent { Kind = "miss", ActorId = attacker.Id, TargetId = guardian.Id, Message = "未命中" });

            Assert.That(group.Actor, Is.SameAs(attacker));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(guardian));
            Assert.IsTrue(group.Results[0].Missed);
            Assert.That(group.Results[0].Label, Is.EqualTo("格挡"));
        }

        [Test]
        public void DeathMarksTheTargetResult()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = hero.Id, Message = "终结" },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = enemy.Id, Amount = 40 },
                new BattleEvent { Kind = "death", TargetId = enemy.Id });

            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.IsTrue(group.Results[0].Died);
        }

        // Environment damage has no actor to stand on the left. Flavor sanity lines carry no applied
        // delta either, so neither produces a cut-in. A real sanity change is covered below, and a
        // round-start bleed tick gets its own beat through the dedicated "bleed" event.
        [Test]
        public void EnvironmentOnlyBatchProducesNoGroups()
        {
            var groups = BattleShowPlan.Build(new[]
            {
                new BattleEvent { Kind = "damage", ActorId = null, TargetId = battle.Run.Heroes[0].Id, Amount = 3 },
                new BattleEvent { Kind = "sanity", ActorId = battle.Run.Heroes[0].Id, Message = "理智 -2" },
                new BattleEvent { Kind = "system", Message = "毒雾" }
            }, battle.Find);

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void SanityChangeBecomesItsOwnBeat()
        {
            var hero = battle.Run.Heroes[0];
            var group = Single(new BattleEvent
            {
                Kind = "sanity", ActorId = hero.Id, TargetId = hero.Id, Value = -12, Message = "低语：理智 50 → 38"
            });

            Assert.That(group.Actor, Is.SameAs(hero));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Target, Is.SameAs(hero));
            Assert.That(group.Results[0].SanityDelta, Is.EqualTo(-12).Within(0.001));
            Assert.IsFalse(group.Results[0].SanityLocked);
        }

        [Test]
        public void SanityLockIsFlagged()
        {
            var hero = battle.Run.Heroes[0];
            var group = Single(new BattleEvent
            {
                Kind = "sanity", ActorId = hero.Id, TargetId = hero.Id, Detail = "locked", Message = "理智已被锁定"
            });

            Assert.IsTrue(group.Results[0].SanityLocked);
            Assert.That(group.Results[0].SanityDelta, Is.EqualTo(0).Within(0.001));
        }

        // "成瘾加深" and the sleep flavor line share the sanity kind but report no applied change. They
        // must stay in the log instead of opening a cut-in with an empty purple line.
        [Test]
        public void SanityFlavorWithoutDeltaIsIgnored()
        {
            var hero = battle.Run.Heroes[0];
            var groups = BattleShowPlan.Build(new[]
            {
                new BattleEvent { Kind = "sanity", ActorId = hero.Id, Message = hero.Name + " 仍被困在假寐中，理智 -10。" }
            }, battle.Find);

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void SanityRiderMergesIntoTheDamageResult()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = enemy.Id, Message = "低语" },
                new BattleEvent { Kind = "damage", ActorId = enemy.Id, TargetId = hero.Id, Amount = 4 },
                new BattleEvent { Kind = "sanity", ActorId = hero.Id, TargetId = hero.Id, Value = -6 });

            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.That(group.Results[0].Damage, Is.EqualTo(4));
            Assert.That(group.Results[0].SanityDelta, Is.EqualTo(-6).Within(0.001));
        }

        [Test]
        public void LimbDamageKeepsTheAttackerAndRecordsThePart()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = enemy.Id, Message = "撕咬" },
                new BattleEvent { Kind = "damage", ActorId = enemy.Id, TargetId = hero.Id, Amount = 7 },
                new BattleEvent
                {
                    Kind = "limb", ActorId = enemy.Id, TargetId = hero.Id, Amount = 5,
                    Detail = "手臂", Value = 1, Message = "手臂受到 5 点部位伤害，已经断肢！"
                });

            Assert.That(group.Actor, Is.SameAs(enemy), "the victim must not replace the attacker on the left");
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.IsTrue(group.Results[0].LimbWorn);
            Assert.IsTrue(group.Results[0].LimbSevered);
            Assert.That(group.Results[0].LimbDamage, Is.EqualTo(5));
            Assert.That(group.Results[0].LimbPart, Is.EqualTo("手臂"));
        }

        [Test]
        public void OrdinaryLimbWearDoesNotGetItsOwnLine()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = enemy.Id, Message = "撕咬" },
                new BattleEvent { Kind = "damage", ActorId = enemy.Id, TargetId = hero.Id, Amount = 7 },
                new BattleEvent
                {
                    Kind = "limb", ActorId = enemy.Id, TargetId = hero.Id, Amount = 5,
                    Detail = "手臂", Value = 0, Message = "手臂受到 5 点部位伤害。"
                });

            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.IsFalse(group.Results[0].LimbWorn, "部位还在一半以上时不提示");
        }

        [Test]
        public void LimbInjuryMilestoneIsAnnounced()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = enemy.Id, Message = "撕咬" },
                new BattleEvent { Kind = "damage", ActorId = enemy.Id, TargetId = hero.Id, Amount = 7 },
                new BattleEvent
                {
                    Kind = "limb", ActorId = enemy.Id, TargetId = hero.Id, Amount = 5, Milestone = true,
                    Detail = "手臂", Value = 0, Message = "手臂受到 5 点部位伤害，陷入受伤。"
                });

            Assert.IsTrue(group.Results[0].LimbWorn);
            Assert.IsFalse(group.Results[0].LimbSevered);
            Assert.That(group.Results[0].LimbDamage, Is.EqualTo(5));
        }

        [Test]
        public void BleedApplicationUsesItsConcreteName()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "action", ActorId = enemy.Id, Message = "追猎" },
                new BattleEvent { Kind = "damage", ActorId = enemy.Id, TargetId = hero.Id, Amount = 6 },
                new BattleEvent
                {
                    Kind = "bleed", ActorId = enemy.Id, TargetId = hero.Id, Detail = "流血 2×2",
                    Message = "开始流血，2×2次行动。"
                });

            Assert.IsTrue(group.Results[0].Buffed);
            Assert.That(group.Results[0].BuffText, Is.EqualTo("流血 2×2"));
        }

        [Test]
        public void BleedTickBecomesItsOwnDamageBeat()
        {
            var hero = battle.Run.Heroes[0];
            var enemy = battle.Enemies[0];
            var groups = BattleShowPlan.Build(new[]
            {
                new BattleEvent { Kind = "action", ActorId = hero.Id, Message = "锈刃" },
                new BattleEvent { Kind = "damage", ActorId = hero.Id, TargetId = enemy.Id, Amount = 3 },
                new BattleEvent
                {
                    Kind = "bleed", TargetId = hero.Id, Amount = 2, Detail = "流血",
                    Message = "流血，剩余 1 次。"
                }
            }, battle.Find);

            Assert.That(groups.Count, Is.EqualTo(2), "流血结算单独成组，不并入上一次行动");
            var tick = groups[1];
            Assert.That(tick.Actor, Is.SameAs(hero));
            Assert.That(tick.Results.Count, Is.EqualTo(1));
            Assert.That(tick.Results[0].Target, Is.SameAs(hero));
            Assert.That(tick.Results[0].Damage, Is.EqualTo(2));
            Assert.IsTrue(tick.Results[0].Buffed);
        }

        [Test]
        public void BuffUsesConcreteNamesAndDeduplicates()
        {
            var caster = battle.Run.Heroes[0];
            var ward = battle.Run.Heroes[1];
            var group = Single(
                new BattleEvent
                {
                    Kind = "buff", ActorId = caster.Id, TargetId = ward.Id, Detail = "格挡×1、防御力+20%",
                    Message = "坚壁 → 格挡×1、防御力+20%"
                },
                new BattleEvent
                {
                    Kind = "buff", ActorId = caster.Id, TargetId = ward.Id, Detail = "格挡×1",
                    Message = "坚壁 → 格挡×1"
                });

            Assert.That(group.Actor, Is.SameAs(caster));
            Assert.That(group.Results.Count, Is.EqualTo(1));
            Assert.IsTrue(group.Results[0].Buffed);
            Assert.That(group.Results[0].BuffText, Is.EqualTo("格挡×1、防御力+20%"));
        }

        [Test]
        public void BuffBeforeActionFoldsIntoOneBeat()
        {
            var caster = battle.Run.Heroes[0];
            var ward = battle.Run.Heroes[1];
            var enemy = battle.Enemies[0];
            var group = Single(
                new BattleEvent { Kind = "buff", ActorId = caster.Id, TargetId = ward.Id, Detail = "反击" },
                new BattleEvent { Kind = "action", ActorId = caster.Id, Message = "突刺" },
                new BattleEvent { Kind = "damage", ActorId = caster.Id, TargetId = enemy.Id, Amount = 9 });

            Assert.That(group.Actor, Is.SameAs(caster));
            Assert.That(group.Results.Count, Is.EqualTo(2));
            var buffed = group.Results.First(r => r.Target == ward);
            Assert.IsTrue(buffed.Buffed);
            Assert.That(buffed.BuffText, Is.EqualTo("反击"));
            var hit = group.Results.First(r => r.Target == enemy);
            Assert.That(hit.Damage, Is.EqualTo(9));
        }

        // ---- engine event payload ----
        // The presentation must not parse Chinese messages to find the numbers, so these pin the fields
        // the engine writes for sanity, limbs and buffs.

        [Test]
        public void ChangeSanityCarriesTheAppliedDelta()
        {
            var hero = battle.Run.Heroes[0];
            int before = battle.Events.Count;
            battle.ChangeSanity(hero, -12, "低语");

            var e = battle.Events[before];
            Assert.That(e.Kind, Is.EqualTo("sanity"));
            Assert.That(e.TargetId, Is.EqualTo(hero.Id));
            Assert.That(e.Value, Is.EqualTo(-12).Within(0.001));
            Assert.IsNull(e.Detail);
        }

        [Test]
        public void LockedSanityCarriesTheLockDetail()
        {
            var hero = battle.Run.Heroes[0];
            hero.SanityLocked = true;
            int before = battle.Events.Count;
            battle.ChangeSanity(hero, -12, "低语");

            var e = battle.Events[before];
            Assert.That(e.Kind, Is.EqualTo("sanity"));
            Assert.That(e.Detail, Is.EqualTo("locked"));
            Assert.That(e.Value, Is.EqualTo(0).Within(0.001));
        }

        [Test]
        public void BuffSkillEmitsConcreteNamesAsBuffKind()
        {
            var run = RunState.Create(catalog.Heroes.Select(x => x.ToSpec()).ToArray(), 44972, 50, catalog.Rules());
            var shield = run.Heroes.First(h => h.Id == "shield");
            shield.Definition.Speed = 100; // fixed, outside the authored span, so it moves first
            var fresh = new BattleEngine(run, catalog.Enemies.Select(x => x.ToSpec()).ToArray(), catalog.Rules());
            Assert.That(fresh.Current, Is.SameAs(shield));

            var skills = fresh.SkillsFor(shield);
            int index = System.Array.FindIndex(skills, s => s != null && s.Name == "壁垒");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));

            int before = fresh.Events.Count;
            string error;
            Assert.IsTrue(fresh.Execute(new ActionRequest(ActionKind.Skill, "warrior", index), out error), error);

            var buff = fresh.Events.Skip(before).FirstOrDefault(e => e.Kind == "buff" && e.TargetId == "warrior");
            Assert.NotNull(buff, "壁垒 should emit a buff event for every ally");
            Assert.That(buff.Detail, Does.Contain("防御力+20%"));
            Assert.That(buff.ActorId, Is.EqualTo(shield.Id));
        }

        // ---- skill id resolution ----
        // Skill portraits hang off SkillId. Only action events used to be parsed, so a miss, heal,
        // protect or buff beat fell back to the base portrait even when the skill had its own art.

        [Test]
        public void MissMessageResolvesTheSkillId()
        {
            var hero = battle.Run.Heroes.First(h => battle.SkillsFor(h).Any(s => s.Effect == SkillEffect.Attack));
            var skill = battle.SkillsFor(hero).First(s => s.Effect == SkillEffect.Attack);
            var enemy = battle.Enemies[0];
            var group = Single(new BattleEvent
            {
                Kind = "miss", ActorId = hero.Id, TargetId = enemy.Id,
                Message = hero.Name + " · " + skill.Name + " 未命中。"
            });

            Assert.That(group.SkillId, Is.EqualTo(skill.Id));
        }

        [Test]
        public void HealMessageResolvesTheSkillId()
        {
            var healer = battle.Run.Heroes.First(h => battle.SkillsFor(h).Any(s => s.Effect == SkillEffect.Heal));
            var skill = battle.SkillsFor(healer).First(s => s.Effect == SkillEffect.Heal);
            var group = Single(new BattleEvent
            {
                Kind = "heal", ActorId = healer.Id, TargetId = healer.Id, Amount = 7,
                Message = healer.Name + " · " + skill.Name + " → " + healer.Name + " 恢复 7。"
            });

            Assert.That(group.SkillId, Is.EqualTo(skill.Id));
        }

        [Test]
        public void ProtectMessageResolvesTheSkillId()
        {
            var caster = battle.Run.Heroes.First(h => battle.SkillsFor(h).Any(s => s.Effect == SkillEffect.Protect));
            var skill = battle.SkillsFor(caster).First(s => s.Effect == SkillEffect.Protect);
            var ward = battle.Run.Heroes.First(h => h.Id != caster.Id);
            var group = Single(new BattleEvent
            {
                Kind = "guard", ActorId = caster.Id, TargetId = ward.Id,
                Message = caster.Name + " · " + skill.Name + " → " + ward.Name + "，持续到自身下次行动。"
            });

            Assert.That(group.SkillId, Is.EqualTo(skill.Id));
        }

        [Test]
        public void BuffMessageResolvesTheSkillId()
        {
            var caster = battle.Run.Heroes.First(h => battle.SkillsFor(h).Any(s => s.Effect == SkillEffect.Buff));
            var skill = battle.SkillsFor(caster).First(s => s.Effect == SkillEffect.Buff);
            var ward = battle.Run.Heroes.First(h => h.Id != caster.Id);
            var group = Single(new BattleEvent
            {
                Kind = "buff", ActorId = caster.Id, TargetId = ward.Id, Detail = "防御力+20%",
                Message = caster.Name + " · " + skill.Name + " → " + ward.Name + " 获得 防御力+20%。"
            });

            Assert.That(group.SkillId, Is.EqualTo(skill.Id));
        }

        // A prosthetic replaces a base skill with a brand new Id that is not in the character asset's
        // three skill arrays. The plan decorates the actor's forms before matching so the replacement
        // still finds its cut-in instead of dropping back to the attack frame.
        [Test]
        public void ProstheticReplacementNameResolvesToItsNewId()
        {
            var gunner = battle.Run.Heroes.First(h => h.Id == "gunner");
            var limb = gunner.Limbs[0];
            limb.Prosthetic = "test_whaling";
            limb.ProstheticKind = ProstheticKind.WhalingHarpoonArm;

            var anchor = battle.SkillsFor(gunner).First(s => s.Id == "g_whaling_anchor");
            var enemy = battle.Enemies[0];
            var group = Single(new BattleEvent
            {
                Kind = "miss", ActorId = gunner.Id, TargetId = enemy.Id,
                Message = gunner.Name + " · " + anchor.Name + " 未命中。"
            });

            Assert.That(group.SkillId, Is.EqualTo("g_whaling_anchor"));
        }

        [Test]
        public void NullInputsAreSafe()
        {
            Assert.That(BattleShowPlan.Build(null, battle.Find), Is.Empty);
            Assert.That(BattleShowPlan.Build(new BattleEvent[0], battle.Find), Is.Empty);
            Assert.That(BattleShowPlan.Build(new[] { new BattleEvent { Kind = "action", ActorId = "nobody" } }, battle.Find), Is.Empty);
            Assert.That(BattleShowPlan.Build(new[] { new BattleEvent { Kind = "damage", ActorId = "nobody", TargetId = "nobody", Amount = 1 } }, battle.Find), Is.Empty);
            Assert.That(BattleShowPlan.Build(new[] { new BattleEvent { Kind = "action", ActorId = battle.Run.Heroes[0].Id } }, null), Is.Empty);
        }
    }
}
