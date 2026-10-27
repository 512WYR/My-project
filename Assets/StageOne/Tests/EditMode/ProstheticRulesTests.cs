using System;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;

namespace Ruin.Tests
{
    // The twenty-eight steampunk prosthetics: four conventional repair pieces plus four universal
    // series and eight character-exclusive pieces. Every test builds a small party and drives the public
    // battle API, so nothing here depends on the catalog's characters or on a particular seed.
    public class ProstheticRulesTests
    {
        private static readonly CombatRules Rules = new CombatRules();

        private static SkillSpec Hit(string id = "hit", int actorRanks = 15, int targetRanks = 15)
        {
            return new SkillSpec
            {
                Id = id, Name = "击", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = actorRanks,
                TargetRanks = targetRanks, Accuracy = Rules.SkillAccuracyBaseline
            };
        }

        private static ActorSpec HeroSpec(string id, int speed = 100, string silhouette = null,
            int maxHp = 100, params SkillSpec[] skills)
        {
            return new ActorSpec
            {
                Id = id, Name = id, Silhouette = silhouette, MaxHp = maxHp, Attack = 10,
                Accuracy = 1.0, Speed = speed, Defense = 0, Dodge = 0, CritChance = 0,
                BasicSkills = skills == null || skills.Length == 0 ? new[] { Hit() } : skills
            };
        }

        private static ActorSpec Foe(int rank = 0, int maxHp = 100000, int attack = 0, int speed = 1)
        {
            return new ActorSpec
            {
                Id = "enemy", Name = "木桩", MaxHp = maxHp, Attack = attack, Accuracy = 1.0,
                Speed = speed, Defense = 0, Dodge = 0, CritChance = 0, StartRank = rank,
                Pattern = EnemyPattern.Cleaver
            };
        }

        private static RunState Run(params ActorSpec[] heroes)
        {
            var run = RunState.Create(heroes, 9751, 50, Rules);
            run.Map.Supplies = 10;
            return run;
        }

        private static BattleEngine Battle(RunState run, int enemyRank = 0,
            int enemyHp = 100000, int enemyAttack = 0)
        {
            return new BattleEngine(run, new[] { Foe(enemyRank, enemyHp, enemyAttack) }, Rules);
        }

        private static ActorState Find(BattleEngine battle, string id)
        {
            return battle.Run.Heroes.First(h => h.Id == id);
        }

        private static void Sever(ActorState actor, LimbPart part)
        {
            int hit = LimbSystem.HitAmount(actor, part, Rules.Limbs);
            LimbSystem.Damage(actor, part, hit);
            LimbSystem.Damage(actor, part, hit);
        }

        private static void Fit(ActorState actor, LimbPart part, string id)
        {
            Sever(actor, part);
            Assert.IsTrue(LimbSystem.Install(actor, LimbSystem.Prosthetic(id, Rules.Limbs)),
                "安装 " + id + " 失败：" + LimbSystem.InstallBlockedReason(actor,
                    LimbSystem.Prosthetic(id, Rules.Limbs)));
        }

        private static void Act(BattleEngine battle, string targetId = "enemy_0", int index = 0)
        {
            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, targetId, index), out error), error);
        }

        [Test]
        public void DefaultsHoldTwentyEightPiecesWithEightExclusive()
        {
            var table = LimbSystem.DefaultProsthetics;
            Assert.AreEqual(28, table.Length);
            Assert.AreEqual(28, table.Select(p => p.Id).Distinct().Count());
            Assert.AreEqual(4, table.Count(p => p.Kind == ProstheticKind.Conventional));
            Assert.AreEqual(8, table.Count(p => !string.IsNullOrEmpty(p.RestrictSilhouette)));
            Assert.AreEqual(7, table.Count(p => p.Part == LimbPart.Head));
            Assert.AreEqual(6, table.Count(p => p.Part == LimbPart.Torso));
            Assert.AreEqual(9, table.Count(p => p.Part == LimbPart.Arm));
            Assert.AreEqual(6, table.Count(p => p.Part == LimbPart.Leg));
            Assert.IsTrue(table.All(p => p != null && !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.Description)));
            foreach (var item in table)
            {
                var resolved = LimbSystem.Prosthetic(item.Id, Rules.Limbs);
                Assert.IsNotNull(resolved);
                Assert.AreEqual(item.Kind, resolved.Kind);
                Assert.AreEqual(item.RestrictSilhouette, resolved.RestrictSilhouette);
            }
        }

        [Test]
        public void InstallAcceptsAnyLimbAndStillChecksSilhouette()
        {
            var warrior = Find(Battle(Run(HeroSpec("a", silhouette: "warrior"))), "a");
            var gunner = Find(Battle(Run(HeroSpec("g", silhouette: "gunner"))), "g");
            var arm = LimbSystem.Prosthetic("prosthetic_arm", Rules.Limbs);
            var monocle = LimbSystem.Prosthetic("gunner_monocle", Rules.Limbs);

            Assert.IsNull(LimbSystem.InstallBlockedReason(warrior, arm), "完好部位也能自由装载");
            Assert.IsTrue(LimbSystem.Install(warrior, arm));
            Assert.AreEqual("该部位已经装有义体，无法替换。",
                LimbSystem.InstallBlockedReason(warrior, arm), "已装义体的部位不能替换");

            Assert.IsNotNull(LimbSystem.InstallBlockedReason(warrior, monocle), "专属义体拒绝错误角色");
            Assert.IsFalse(LimbSystem.Install(warrior, monocle));
            Assert.IsNull(LimbSystem.InstallBlockedReason(gunner, monocle), "完好部位也能装专属义体");
            Assert.IsTrue(LimbSystem.Install(gunner, LimbSystem.Clone(monocle)));
            Assert.AreEqual(ProstheticKind.GunnerMonocle, LimbSystem.Get(gunner, LimbPart.Head).ProstheticKind);
            Assert.AreEqual(LimbSystem.Get(gunner, LimbPart.Head).MaxHp, LimbSystem.Get(gunner, LimbPart.Head).Hp);
        }

        [Test]
        public void MaxHpSharesAddAndDestructionClamps()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Sever(hero, LimbPart.Torso);
            Sever(hero, LimbPart.Head);
            LimbSystem.Install(hero, LimbSystem.Prosthetic("armored_pressure_torso", Rules.Limbs));
            LimbSystem.Install(hero, LimbSystem.Prosthetic("clockwork_compound_eye", Rules.Limbs));
            Assert.AreEqual(140, hero.MaxHp, "+70 与 -30 视为同类百分比相加");
            hero.Hp = hero.MaxHp;
            Sever(hero, LimbPart.Torso);
            Assert.IsFalse(LimbSystem.HasProsthetic(hero, LimbPart.Torso));
            Assert.AreEqual(70, hero.MaxHp);
            Assert.AreEqual(70, hero.Hp);
        }

        [Test]
        public void AccuracyHealingAndBleedQueries()
        {
            var run = Run(HeroSpec("a"), HeroSpec("b", speed: 5));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Head, "cast_iron_bell_head");
            Fit(hero, LimbPart.Torso, "double_pump_torso");
            Near(ProstheticSystem.AccuracyOffset(hero), -0.20);
            Near(ProstheticSystem.HealReceivedMultiplier(hero), 1.75);
            Near(ProstheticSystem.BleedTakenFactor(hero), 2.0);

            var eyeRun = Run(HeroSpec("a"));
            var eye = Find(Battle(eyeRun), "a");
            Fit(eye, LimbPart.Head, "clockwork_compound_eye");
            Near(ProstheticSystem.AccuracyOffset(eye), 0.30);
            Near(ProstheticSystem.HealReceivedMultiplier(eye), 1.0);
        }

        [Test]
        public void DamageFactorsAddWithinProsthetics()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Head, "cast_iron_bell_head");
            Fit(hero, LimbPart.Torso, "hollow_wing_frame");
            hero.Rank = 0;
            Near(ProstheticSystem.DamageTakenFactor(hero), 1.20);
            hero.Rank = 3;
            Near(ProstheticSystem.DamageTakenFactor(hero), 0.30);

            var anchorRun = Run(HeroSpec("a"));
            var anchor = Find(Battle(anchorRun), "a");
            Fit(anchor, LimbPart.Leg, "pile_anchor_leg");
            anchor.Rank = 0;
            Near(ProstheticSystem.DamageTakenFactor(anchor), 0.65);
            anchor.Rank = 2;
            Near(ProstheticSystem.DamageTakenFactor(anchor), 1.0);
            Assert.IsTrue(ProstheticSystem.BlocksActiveMovement(anchor));
        }

        [Test]
        public void OutgoingBonusesAreAdditiveAndSkillScoped()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Arm, "siege_hydraulic_arm");
            Fit(hero, LimbPart.Head, "pain_amplifier_crown");
            hero.Hp = hero.MaxHp / 2;
            Near(ProstheticSystem.OutgoingDamageBonus(hero, Hit(), true), 1.20);
            hero.Hp = hero.MaxHp;
            Near(ProstheticSystem.OutgoingDamageBonus(hero, Hit(), true), 0.60);

            var gunRun = Run(HeroSpec("g", silhouette: "gunner"));
            var gunner = Find(Battle(gunRun), "g");
            Fit(gunner, LimbPart.Head, "gunner_monocle");
            Near(ProstheticSystem.OutgoingDamageBonus(gunner, new SkillSpec { Id = "g_shot" }, true), 0.80);
            Near(ProstheticSystem.OutgoingDamageBonus(gunner, new SkillSpec { Id = "g_shot" }, false), 0);
        }

        [Test]
        public void RestoreHpAppliesModifierAndClamps()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Torso, "armored_pressure_torso");
            hero.Hp = hero.MaxHp - 3;
            Assert.AreEqual(3, ProstheticSystem.RestoreHp(hero, 10), "治疗 -40% 后仍被上限夹住");
            Assert.AreEqual(hero.MaxHp, hero.Hp);
        }

        [Test]
        public void ReplacementSkillsOnlyAppearWithThePiece()
        {
            var guard = new SkillSpec
            {
                Id = "s_guard", Name = "守护", Effect = SkillEffect.Protect, Target = TargetSide.Ally,
                Targeting = SkillTargeting.Choose, ActorRanks = 15, TargetRanks = 15,
                GuardReduction = 0.25
            };
            var lowGuard = new SkillSpec
            {
                Id = "s_primal_guard", Name = "守护", Effect = SkillEffect.Protect, Target = TargetSide.Ally,
                Targeting = SkillTargeting.Choose, ActorRanks = 3, TargetRanks = 15,
                GuardReduction = 0.25, SelfGrant = AllyBuff.Counter
            };
            var run = Run(HeroSpec("a", silhouette: "shield", skills: new[] { guard }),
                HeroSpec("b", speed: 5, skills: new[] { guard }),
                HeroSpec("c", speed: 4, skills: new[] { lowGuard }));
            var shield = Find(Battle(run), "a");
            var plain = Find(Battle(Run(HeroSpec("p", skills: new[] { guard }))), "p");
            Fit(shield, LimbPart.Torso, "double_valve_boiler_torso");

            // The decorated list is a fresh array of clones; the shared skill objects stay untouched.
            var decorated = ProstheticSystem.DecorateSkills(shield, new[] { guard, lowGuard });
            Assert.AreEqual("s_double_guard", decorated[0].Id);
            Assert.AreEqual("s_double_guard_low", decorated[1].Id);
            Assert.AreEqual(2, decorated[0].MaxTargets);
            Assert.AreEqual(1, decorated[0].OreCost);
            Assert.AreEqual(AllyBuff.Counter, decorated[1].SelfGrant);
            Assert.AreEqual("s_guard", guard.Id);
            Assert.AreEqual(SkillEffect.Protect, guard.Effect);

            var untouched = ProstheticSystem.DecorateSkills(plain, new[] { guard });
            Assert.AreSame(guard, untouched[0], "未装义体的角色不应克隆技能");
        }

        [Test]
        public void OreBlocksTheSkillAndIsSpentEvenOnAMiss()
        {
            var run = Run(HeroSpec("g", silhouette: "gunner"));
            var gunner = Find(Battle(run), "g");
            Fit(gunner, LimbPart.Arm, "whaling_harpoon_arm");
            var battle = new BattleEngine(run, new[] { Foe(2) }, Rules);
            // Whaling Anchor is cast from the back ranks at a back-rank target.
            gunner.Rank = 2;
            Assert.AreEqual("g_whaling_anchor", battle.SkillFor(gunner, 0).Id);
            run.Map.Supplies = 0;
            StringAssert.StartsWith("矿石不足", battle.SkillDisabledReason(0));
            string error;
            Assert.IsFalse(battle.Execute(new ActionRequest(ActionKind.Skill, "enemy_0", 0), out error));

            run.Map.Supplies = 1;
            gunner.Definition.Accuracy = 0; // Force a miss; the ore is still spent.
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "enemy_0", 0), out error), error);
            Assert.AreEqual(0, run.Map.Supplies);
        }

        [Test]
        public void DoubleGuardCoversTwoAllies()
        {
            var guard = new SkillSpec
            {
                Id = "s_guard", Name = "守护", Effect = SkillEffect.Protect, Target = TargetSide.Ally,
                Targeting = SkillTargeting.Choose, ActorRanks = 15, TargetRanks = 15,
                GuardReduction = 0.25
            };
            var run = Run(HeroSpec("a", silhouette: "shield", skills: new[] { guard }),
                HeroSpec("b", speed: 5), HeroSpec("c", speed: 4));
            var shield = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            var c = Find(Battle(run), "c");
            Fit(shield, LimbPart.Torso, "double_valve_boiler_torso");
            var battle = new BattleEngine(run, new[] { Foe(0) }, Rules);
            var skill = battle.SkillFor(shield, 0);
            Assert.AreEqual(2, skill.MaxTargets);
            string error;
            var request = new ActionRequest(ActionKind.Skill, b.Id, 0) { TargetIds = new[] { b.Id, c.Id, b.Id } };
            Assert.IsTrue(battle.Execute(request, out error), error);
            Assert.AreEqual(shield.Id, b.ProtectorId);
            Assert.AreEqual(shield.Id, c.ProtectorId);
            Assert.IsTrue(shield.ProstheticBattle.DoubleGuardActive);
            Near(ProstheticSystem.GuardInterceptMultiplier(shield), 1.25);
            Assert.AreEqual(9, run.Map.Supplies);
        }

        [Test]
        public void BoilerShockHitsBothFrontRanksAndUsesTwice()
        {
            var bash = new SkillSpec
            {
                Id = "w_bash", Name = "猛击", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = 3, TargetRanks = 3,
                Accuracy = Rules.SkillAccuracyBaseline, Uses = 0
            };
            // The second hero keeps the battle from advancing all the way back to the warrior, so the
            // post-shock vulnerability can still be observed when Execute returns.
            var run = Run(HeroSpec("a", silhouette: "warrior", skills: new[] { bash }),
                HeroSpec("b", speed: 50));
            var warrior = Find(Battle(run), "a");
            Fit(warrior, LimbPart.Arm, "steam_city_hammer_arm");
            var battle = new BattleEngine(run, new[] { Foe(0), Foe(1) }, Rules);
            var shock = battle.SkillFor(warrior, 0);
            Assert.AreEqual("w_boiler_shock", shock.Id);
            Assert.AreEqual(2, shock.Uses);
            Assert.AreEqual(1, shock.OreCost);
            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "enemy_0", 0), out error), error);
            Assert.AreEqual(1, warrior.UsesLeft(shock));
            // Both victims are marked on the hit; Advance then consumes the skipped turns, so inspect the log.
            Assert.AreEqual(1, battle.Events.Count(e => e.TargetId == "enemy_0"
                && e.Message.Contains("失衡：下一次行动将被跳过")));
            Assert.AreEqual(1, battle.Events.Count(e => e.TargetId == "enemy_1"
                && e.Message.Contains("失衡：下一次行动将被跳过")));
            Assert.IsTrue(warrior.ProstheticBattle.BoilerVulnerable);
            Assert.AreEqual(9, run.Map.Supplies);
        }

        [Test]
        public void WhalingAnchorPullsAndMovesTheGunnerForward()
        {
            var grapple = new SkillSpec
            {
                Id = "g_grapple", Name = "拖拽射击", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = 12, TargetRanks = 12,
                Accuracy = Rules.SkillAccuracyBaseline, PullToFront = true, PullChance = 0.5, Uses = 1
            };
            var run = Run(HeroSpec("b", speed: 5), HeroSpec("g", speed: 100, silhouette: "gunner",
                skills: new[] { grapple }));
            var gunner = Find(Battle(run), "g");
            var ally = Find(Battle(run), "b");
            gunner.Rank = 2; ally.Rank = 1;
            Fit(gunner, LimbPart.Arm, "whaling_harpoon_arm");
            var battle = new BattleEngine(run, new[] { Foe(2) }, Rules);
            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "enemy_0", 0), out error), error);
            Assert.AreEqual(0, battle.Enemies[0].Rank, "无概率拖拽到 1 号位");
            Assert.AreEqual(1, gunner.Rank);
            Assert.AreEqual(2, ally.Rank);
            Assert.AreEqual(9, run.Map.Supplies);
        }

        [Test]
        public void BoilingMistHealsPartyAndBleedsEvenAtFullHp()
        {
            var mist = new SkillSpec
            {
                Id = "m_mist", Name = "药雾", Effect = SkillEffect.Heal, Target = TargetSide.Ally,
                Targeting = SkillTargeting.All, Area = SkillArea.Party, Multiplier = 0.5,
                ActorRanks = 12, TargetRanks = 15, Accuracy = Rules.SkillAccuracyBaseline
            };
            var run = Run(HeroSpec("a", silhouette: "medic", skills: new[] { mist }),
                HeroSpec("b", speed: 5));
            var medic = Find(Battle(run), "a");
            var ally = Find(Battle(run), "b");
            Fit(medic, LimbPart.Arm, "pressure_drug_arm");
            medic.Rank = 2; ally.Rank = 3;
            var battle = new BattleEngine(run, new[] { Foe(0) }, Rules);
            var skill = battle.SkillFor(medic, 0);
            Assert.AreEqual("m_boiling_mist", skill.Id);
            ally.Hp = ally.MaxHp;
            string error;
            Assert.IsTrue(battle.Execute(new ActionRequest(ActionKind.Skill, "enemy_0", 0), out error), error);
            Assert.Greater(medic.Hp, 0);
            Assert.Greater(ally.BleedTurns, 0, "满血队员也应获得药雾流血");
            Assert.AreEqual(2, ally.BleedDamage);
            Assert.AreEqual(9, run.Map.Supplies);
        }

        [Test]
        public void SteamJetFreeSwapCostsNoAction()
        {
            var run = Run(HeroSpec("b", speed: 5), HeroSpec("a", speed: 100));
            var a = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            Fit(a, LimbPart.Leg, "steam_jet_leg");
            var battle = new BattleEngine(run, new[] { Foe(3) }, Rules);
            Assert.AreSame(a, battle.Current);
            CollectionAssert.AreEquivalent(new[] { "b" }, battle.FreeSwapTargets().Select(x => x.Id));
            string error;
            Assert.IsTrue(battle.ExecuteFreeSwap("b", out error), error);
            Assert.AreSame(a, battle.Current, "免费换位不消耗行动");
            Assert.AreEqual(0, a.Rank);
            Assert.AreEqual(1, b.Rank);
            Assert.IsTrue(a.ProstheticBattle.SteamSwapAttackBonus);
            Assert.IsTrue(a.ProstheticBattle.SteamSwapVulnerable);
            Assert.AreEqual(1.5, ProstheticSystem.OutgoingDamageBonus(a, Hit(), true), 1e-9);
            Assert.AreEqual(1.4, ProstheticSystem.DamageTakenFactor(a), 1e-9);
            Assert.IsFalse(battle.ExecuteFreeSwap("b", out error), "每次行动只能免费换位一次");
        }

        [Test]
        public void SteelWireVulnerabilityEndsOnTheUsersNextTurn()
        {
            var run = Run(HeroSpec("b", speed: 90), HeroSpec("a", speed: 100));
            var a = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            Fit(b, LimbPart.Leg, "steel_wire_leg");
            var battle = new BattleEngine(run, new[] { Foe(3) }, Rules);
            Act(battle);
            Assert.AreSame(b, battle.Current);
            string error;
            Assert.IsTrue(battle.ExecuteFreeSwap(a.Id, out error), error);
            Assert.IsTrue(a.ProstheticBattle.SteelWireVulnerable);
            Assert.IsTrue(b.ProstheticBattle.SteelWireVulnerable);
            Act(battle);
            battle.ExecuteEnemy();
            Assert.AreSame(a, battle.Current);
            Assert.IsTrue(a.ProstheticBattle.SteelWireVulnerable, "易伤持续到使用者下次行动开始");
            Act(battle);
            Assert.AreSame(b, battle.Current);
            Assert.IsFalse(a.ProstheticBattle.SteelWireVulnerable);
            Assert.IsFalse(b.ProstheticBattle.SteelWireVulnerable);
        }

        [Test]
        public void PileAnchorBlocksEveryActiveMove()
        {
            var step = new SkillSpec
            {
                Id = "step", Name = "突进", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = 15, TargetRanks = 15,
                Accuracy = Rules.SkillAccuracyBaseline, SelfMove = -1
            };
            var run = Run(HeroSpec("b", speed: 5), HeroSpec("a", speed: 100, skills: new[] { step }));
            var a = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            a.Rank = 2; b.Rank = 1;
            Fit(a, LimbPart.Leg, "pile_anchor_leg");
            var battle = new BattleEngine(run, new[] { Foe(3) }, Rules);
            Assert.IsNotNull(battle.SwapDisabledReason());
            Assert.IsNotNull(battle.FreeSwapDisabledReason());
            Act(battle);
            Assert.AreEqual(2, a.Rank, "带移动的技能原地执行");
            Assert.AreEqual(1, b.Rank);
            Assert.IsNotNull(battle.FreeSwapDisabledReason(), "桩锚角色不能主动免费换位");
        }

        [Test]
        public void GyroChargesOnRankChangeAndIsSpentByTheNextAttack()
        {
            var run = Run(HeroSpec("b", speed: 5), HeroSpec("a", speed: 100));
            var a = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            Fit(a, LimbPart.Leg, "steam_jet_leg");
            Fit(a, LimbPart.Arm, "gyro_charge_arm");
            var battle = new BattleEngine(run, new[] { Foe(3) }, Rules);
            string error;
            Assert.IsTrue(battle.ExecuteFreeSwap("b", out error), error);
            Assert.IsTrue(a.ProstheticBattle.GyroCharged);
            Assert.AreEqual(1.2, ProstheticSystem.OutgoingDamageBonus(a, Hit(), true), 1e-9);
            Act(battle);
            Assert.IsFalse(a.ProstheticBattle.GyroCharged, "攻击后消耗蓄力");
        }

        [Test]
        public void DetonationPaysFifteenPercentAndLeavesOneHp()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Arm, "detonation_stroke_arm");
            var battle = new BattleEngine(run, new[] { Foe(0) }, Rules);
            hero.Hp = hero.MaxHp;
            Act(battle);
            Assert.AreEqual(hero.MaxHp - (int)Math.Ceiling(hero.MaxHp * 0.15), hero.Hp);

            var lowRun = Run(HeroSpec("low"));
            var low = lowRun.Heroes.First();
            Fit(low, LimbPart.Arm, "detonation_stroke_arm");
            var lowBattle = new BattleEngine(lowRun, new[] { Foe(0) }, Rules);
            low.Hp = 2;
            Act(lowBattle);
            Assert.AreEqual(1, low.Hp, "生命代价最低留 1");
        }

        [Test]
        public void RedlineAndLeechNeedRealDamage()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Torso, "redline_steam_heart");
            var battle = new BattleEngine(run, new[] { Foe(0) }, Rules);
            hero.Hp = 50; hero.Sanity = 50;
            Act(battle);
            Assert.AreEqual(60, hero.Hp);
            Assert.AreEqual(42, hero.Sanity, 1e-9);

            var missRun = Run(HeroSpec("a"));
            var miss = Find(Battle(missRun), "a");
            Fit(miss, LimbPart.Torso, "redline_steam_heart");
            miss.Definition.Accuracy = 0;
            var missBattle = new BattleEngine(missRun, new[] { Foe(0) }, Rules);
            miss.Hp = 50; miss.Sanity = 50;
            Act(missBattle);
            Assert.AreEqual(50, miss.Hp);
            Assert.AreEqual(50, miss.Sanity, 1e-9);

            var leechRun = Run(HeroSpec("a"));
            var leech = Find(Battle(leechRun), "a");
            Fit(leech, LimbPart.Arm, "leech_return_arm");
            var leechBattle = new BattleEngine(leechRun, new[] { Foe(0) }, Rules);
            leech.Hp = 50;
            Act(leechBattle);
            Assert.AreEqual(65, leech.Hp);
            Assert.AreEqual(2, leech.BleedDamage);
            Assert.AreEqual(2, leech.BleedTurns);
        }

        [Test]
        public void CondensationAndBedframeFireAtActionEnd()
        {
            var run = Run(HeroSpec("a"));
            var hero = Find(Battle(run), "a");
            Fit(hero, LimbPart.Head, "condensation_head");
            var battle = new BattleEngine(run, new[] { Foe(0) }, Rules);
            hero.Sanity = 50;
            Act(battle);
            Assert.AreEqual(58, hero.Sanity, 1e-9);

            var bedRun = Run(HeroSpec("a"));
            var bed = Find(Battle(bedRun), "a");
            Fit(bed, LimbPart.Leg, "bedframe_leg");
            var bedBattle = new BattleEngine(bedRun, new[] { Foe(0) }, Rules);
            bed.Hp = 50;
            Act(bedBattle);
            Assert.AreEqual(55, bed.Hp);

            var move = new SkillSpec
            {
                Id = "step", Name = "突进", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = 15, TargetRanks = 15,
                Accuracy = Rules.SkillAccuracyBaseline, SelfMove = -1
            };
            var moveRun = Run(HeroSpec("a", skills: new[] { move }));
            var mover = Find(Battle(moveRun), "a");
            Fit(mover, LimbPart.Leg, "bedframe_leg");
            mover.Rank = 1;
            var moveBattle = new BattleEngine(moveRun, new[] { Foe(0) }, Rules);
            mover.Hp = 50; mover.Sanity = 50;
            Act(moveBattle);
            Assert.AreEqual(0, mover.Rank);
            Assert.AreEqual(50, mover.Hp, "换位后不触发驻床治疗");
            Assert.AreEqual(45, mover.Sanity, 1e-9);
        }

        [Test]
        public void RatchetCleaveReachesFromTheBackAndAdvancesTwo()
        {
            var cleave = new SkillSpec
            {
                Id = "w_cleave", Name = "斩劈", Effect = SkillEffect.Attack, Target = TargetSide.Enemy,
                Targeting = SkillTargeting.Choose, Multiplier = 1, ActorRanks = 3, TargetRanks = 15,
                Accuracy = Rules.SkillAccuracyBaseline, SelfMove = -1
            };
            var run = Run(HeroSpec("b", speed: 5), HeroSpec("a", speed: 100, silhouette: "warrior",
                skills: new[] { cleave }));
            var a = Find(Battle(run), "a");
            var b = Find(Battle(run), "b");
            a.Rank = 3; b.Rank = 2;
            Fit(a, LimbPart.Leg, "ratchet_leg");
            var battle = new BattleEngine(run, new[] { Foe(1) }, Rules);
            var skill = battle.SkillFor(a, 0);
            Assert.AreEqual(15, skill.ActorRanks);
            Assert.AreEqual(-2, skill.SelfMove);
            Assert.AreEqual(0.7, ProstheticSystem.OutgoingDamageBonus(a, skill, true), 1e-9);
            Act(battle);
            Assert.AreEqual(1, a.Rank);
            Assert.AreEqual(3, b.Rank);
        }

        [Test]
        public void VengeanceFlywheelDoublesTheCounterAndPaysLife()
        {
            var skill = new SkillSpec
            {
                Id = "brace", Name = "架势", Effect = SkillEffect.Buff, Target = TargetSide.Ally,
                Targeting = SkillTargeting.All, ActorRanks = 15, TargetRanks = 15,
                Accuracy = Rules.SkillAccuracyBaseline, SelfGrant = AllyBuff.Counter
            };
            var withRun = Run(HeroSpec("a", silhouette: "shield", skills: new[] { skill }));
            var plainRun = Run(HeroSpec("a", silhouette: "shield", skills: new[] { skill }));
            var withHero = Find(Battle(withRun), "a");
            var plainHero = Find(Battle(plainRun), "a");
            Fit(withHero, LimbPart.Arm, "vengeance_flywheel_arm");
            var withBattle = new BattleEngine(withRun, new[] { Foe(0, 100000, 10) }, Rules);
            var plainBattle = new BattleEngine(plainRun, new[] { Foe(0, 100000, 10) }, Rules);
            Near(ProstheticSystem.CounterMultiplier(withHero), 2.0);
            Act(withBattle, withHero.Id);
            Act(plainBattle, plainHero.Id);
            withBattle.ExecuteEnemy();
            plainBattle.ExecuteEnemy();
            Assert.AreEqual(plainHero.Hp - 8, withHero.Hp, "每次实际反击后支付 8% 最大生命");
        }

        private static void Near(double actual, double expected, double tolerance = 1e-9)
        {
            Assert.AreEqual(expected, actual, tolerance);
        }
    }
}
