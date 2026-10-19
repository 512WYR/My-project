using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // Rules for precision and heterogeneous prosthetics. Everything here is a pure read: the engine calls
    // these from damage, accuracy, healing and preview paths, so a query must never draw a random number
    // or mutate actor state. Installation and destruction stay in LimbSystem.
    public static class ProstheticSystem
    {
        // Shared bleed numbers for the prosthetic self-inflicted bleeds. Provisional balance values.
        public const int SelfBleedDamage = 2;
        public const int SelfBleedTurns = 2;
        public const int OrePrice = 1;

        // ---- replacement skills ----
        // Data-only SkillSpecs so the four old character assets never need a new array. DecorateSkills
        // hands out clones; nothing outside a battle mutates these originals.
        private static readonly SkillSpec WhalingAnchor = new SkillSpec
        {
            Id = "g_whaling_anchor", Name = "贯体锚枪", Effect = SkillEffect.Attack,
            Description = "造成攻击力125%的伤害；命中后无概率把目标拖到敌方1号位。用后向前移动1位并与前方队友换位。",
            Multiplier = 1.25, ActorRanks = 12, TargetRanks = 12, Accuracy = 0.9,
            PullToFront = true, PullChance = 1.0, OreCost = OrePrice,
            PostSelfMove = -1, PostSelfMoveRequiresAlly = true
        };

        private static readonly SkillSpec DoubleGuard = new SkillSpec
        {
            Id = "s_double_guard", Name = "双线守护", Effect = SkillEffect.Protect,
            Description = "守护其他最多2名队友（不能选自己），持续到自身下次行动；代承的伤害提高25%。",
            Target = TargetSide.Ally, Targeting = SkillTargeting.Choose,
            ActorRanks = 15, TargetRanks = 15, MaxTargets = 2, MultiGuard = true,
            GuardReduction = 0, OreCost = OrePrice
        };

        private static readonly SkillSpec DoubleGuardLow = new SkillSpec
        {
            Id = "s_double_guard_low", Name = "双线守护", Effect = SkillEffect.Protect,
            Description = "守护其他最多2名队友（不能选自己），持续到自身下次行动；自身受击或代伤后反击，不按人数叠加；代承伤害提高25%。",
            Target = TargetSide.Ally, Targeting = SkillTargeting.Choose,
            ActorRanks = 3, TargetRanks = 15, MaxTargets = 2, MultiGuard = true,
            GuardReduction = 0, SelfGrant = AllyBuff.Counter, OreCost = OrePrice
        };

        private static readonly SkillSpec BoilerShock = new SkillSpec
        {
            Id = "w_boiler_shock", Name = "锅炉震荡", Effect = SkillEffect.Attack,
            Description = "对敌方1、2号位的每名敌人分别判定命中，造成攻击力75%的伤害并使其失衡；每场2次。用后自身受伤+50%，至下次行动。",
            Targeting = SkillTargeting.All, AllWithinTargetRanks = true, Area = SkillArea.Single,
            Multiplier = 0.75, ActorRanks = 3, TargetRanks = 3, Accuracy = 0.9,
            Uses = 2, OreCost = OrePrice, PerTargetAccuracy = true, Imbalance = true
        };

        private static readonly SkillSpec BoilingMist = new SkillSpec
        {
            Id = "m_boiling_mist", Name = "沸血药雾", Effect = SkillEffect.Heal,
            Description = "全队各恢复攻击力125%的生命；治疗后每名队员获得2点流血，持续2次行动。",
            Target = TargetSide.Ally, Targeting = SkillTargeting.All, Area = SkillArea.Party,
            Multiplier = 1.25, ActorRanks = 12, TargetRanks = 15,
            Bleed = 2, BleedTurns = 2, OreCost = OrePrice
        };

        // Applies every prosthetic-driven skill change to the current form's list. Replacement skills are
        // clones, and a skill is only cloned when this actor actually modifies it, so the shared arrays and
        // the old behaviour stay untouched.
        public static SkillSpec[] DecorateSkills(ActorState actor, SkillSpec[] skills)
        {
            if (actor == null || skills == null) return skills;
            var result = new SkillSpec[skills.Length];
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                if (skill == null) { result[i] = null; continue; }
                if (Has(actor, ProstheticKind.WhalingHarpoonArm) && skill.Id == "g_grapple")
                { result[i] = WhalingAnchor.Clone(); continue; }
                if (Has(actor, ProstheticKind.DoubleValveBoilerTorso) && skill.Id == "s_guard")
                { result[i] = DoubleGuard.Clone(); continue; }
                if (Has(actor, ProstheticKind.DoubleValveBoilerTorso) && skill.Id == "s_primal_guard")
                { result[i] = DoubleGuardLow.Clone(); continue; }
                if (Has(actor, ProstheticKind.SteamCityHammerArm) && skill.Id == "w_bash")
                { result[i] = BoilerShock.Clone(); continue; }
                if (Has(actor, ProstheticKind.PressureDrugArm) && skill.Id == "m_mist")
                { result[i] = BoilingMist.Clone(); continue; }

                SkillSpec copy = null;
                if (Has(actor, ProstheticKind.RatchetLeg) && skill.Id == "w_cleave")
                {
                    copy = skill.Clone();
                    // Cleave becomes usable from the back rank and advances up to two.
                    copy.ActorRanks = 15;
                    copy.SelfMove = -2;
                }
                if (Has(actor, ProstheticKind.MadSurgeonLens)
                    && (skill.Id == "m_blood_heal" || skill.Id == "m_primal_heal"))
                {
                    copy = copy ?? skill.Clone();
                    copy.BleedChance = 1;
                }
                if (Has(actor, ProstheticKind.GunnerMonocle)
                    && (skill.Id == "g_shot" || skill.Id == "g_primal_shot"))
                {
                    copy = copy ?? skill.Clone();
                    // Only the main target is restricted; splash still follows the main target's neighbours.
                    copy.TargetRanks = 12;
                }
                result[i] = copy ?? skill;
            }
            return result;
        }

        public static bool Has(ActorState actor, ProstheticKind kind)
        {
            if (actor == null || actor.Limbs == null) return false;
            for (int i = 0; i < actor.Limbs.Count; i++)
            {
                var limb = actor.Limbs[i];
                if (limb != null && limb.ProstheticFitted && limb.ProstheticKind == kind) return true;
            }
            return false;
        }

        // Additive share applied to Definition.MaxHp. The four conventional pieces have no entry.
        public static double MaxHpPercent(ProstheticKind kind)
        {
            switch (kind)
            {
                case ProstheticKind.ArmoredPressureTorso: return 0.70;
                case ProstheticKind.ClockworkCompoundEye: return -0.30;
                default: return 0;
            }
        }

        // Percentage points added to the 0-1 accuracy value, not a multiplicative share.
        public static double AccuracyOffset(ActorState actor)
        {
            double value = 0;
            if (Has(actor, ProstheticKind.CastIronBellHead)) value -= 0.20;
            if (Has(actor, ProstheticKind.ClockworkCompoundEye)) value += 0.30;
            return value;
        }

        // Healing strength of the caster. Only the two blood-heal skills care.
        public static double CasterHealBonus(ActorState actor, SkillSpec skill)
        {
            if (skill == null || !Has(actor, ProstheticKind.MadSurgeonLens)) return 0;
            if (skill.Id == "m_blood_heal" || skill.Id == "m_primal_heal") return 0.80;
            return 0;
        }

        // Additive share on the damage the caster deals with an active attack skill. Follow-ups, counters
        // and bleed ticks never pass through here. `mainTarget` is true for the selected target and for
        // every victim of an AllWithinTargetRanks sweep.
        public static double OutgoingDamageBonus(ActorState actor, SkillSpec skill, bool mainTarget)
        {
            if (actor == null || skill == null) return 0;
            double value = 0;
            if (Has(actor, ProstheticKind.SiegeHydraulicArm)) value += 0.60;
            if (Has(actor, ProstheticKind.PainAmplifierCrown) && actor.Hp * 2 <= actor.MaxHp) value += 0.60;
            if (Has(actor, ProstheticKind.CondensationHead)) value -= 0.25;
            if (Has(actor, ProstheticKind.DetonationStrokeArm)) value += 1.00;
            var state = actor.ProstheticBattle;
            bool charged = state != null && state.GyroCharged;
            if (Has(actor, ProstheticKind.SteamJetLeg) && state != null && state.SteamSwapAttackBonus) value += 0.50;
            if (Has(actor, ProstheticKind.GyroChargeArm)) value += charged ? 0.70 : -0.25;
            if (Has(actor, ProstheticKind.RatchetLeg) && skill.Id == "w_cleave") value += 0.70;
            if (mainTarget && Has(actor, ProstheticKind.GunnerMonocle)
                && (skill.Id == "g_shot" || skill.Id == "g_primal_shot")) value += 0.80;
            return value;
        }

        // Additive share on incoming attack damage. Bleed and direct losses skip this.
        public static double DamageTakenFactor(ActorState actor)
        {
            if (actor == null) return 1;
            double value = 1;
            if (Has(actor, ProstheticKind.CastIronBellHead)) value -= 0.30;
            if (Has(actor, ProstheticKind.SiegeHydraulicArm)) value += 0.30;
            if (Has(actor, ProstheticKind.PileAnchorLeg) && actor.Rank == 0) value -= 0.35;
            if (Has(actor, ProstheticKind.HollowWingFrame)) value += actor.Rank >= 2 ? -0.40 : 0.50;
            var state = actor.ProstheticBattle;
            if (state != null)
            {
                if (state.SteamSwapVulnerable && Has(actor, ProstheticKind.SteamJetLeg)) value += 0.40;
                if (state.SteelWireVulnerable && Has(actor, ProstheticKind.SteelWireLeg)) value += 0.25;
                if (state.BoilerVulnerable && Has(actor, ProstheticKind.SteamCityHammerArm)) value += 0.50;
            }
            return Math.Max(0, value);
        }

        // Bleed is the one damage type that bypasses defence and every other incoming-damage modifier.
        // Double Pump is the sole exception and doubles it on purpose.
        public static double BleedTakenFactor(ActorState actor)
        {
            return Has(actor, ProstheticKind.DoublePumpTorso) ? 2.0 : 1.0;
        }

        public static double HealReceivedMultiplier(ActorState actor)
        {
            if (actor == null) return 1;
            double value = 1;
            if (Has(actor, ProstheticKind.ArmoredPressureTorso)) value -= 0.40;
            if (Has(actor, ProstheticKind.PainAmplifierCrown)) value -= 0.30;
            if (Has(actor, ProstheticKind.DoublePumpTorso)) value += 0.75;
            return Math.Max(0, value);
        }

        public static double CounterMultiplier(ActorState actor)
        {
            return Has(actor, ProstheticKind.VengeanceFlywheelArm) ? 2.0 : 1.0;
        }

        // Damage the shield actually absorbs when intercepting with Double Guard. The skill raises its
        // share by 25%; the flag is only set while the guard it cast is active.
        public static double GuardInterceptMultiplier(ActorState guard)
        {
            if (guard == null || !Has(guard, ProstheticKind.DoubleValveBoilerTorso)) return 1.0;
            var state = guard.ProstheticBattle;
            return state != null && state.DoubleGuardActive ? 1.25 : 1.0;
        }

        public static bool BlocksActiveMovement(ActorState actor)
        {
            return Has(actor, ProstheticKind.PileAnchorLeg);
        }

        // Reserved for enemy pulls on heroes. Today TryPull refuses the hero side outright.
        public static bool BlocksForcedMovement(ActorState actor)
        {
            return Has(actor, ProstheticKind.PileAnchorLeg);
        }

        public static bool HasFreeSwap(ActorState actor)
        {
            if (actor == null || actor.Dead || BlocksActiveMovement(actor)) return false;
            var state = actor.ProstheticBattle;
            if (state == null || state.FreeSwapUsed) return false;
            return Has(actor, ProstheticKind.SteamJetLeg) || Has(actor, ProstheticKind.SteelWireLeg);
        }

        // Steam Jet only reaches the adjacent teammate in front (one rank towards the enemy). Steel Wire
        // reaches any other living teammate. A pile-anchor ally is a legal target for either.
        public static List<ActorState> FreeSwapTargets(BattleEngine battle, ActorState actor)
        {
            var result = new List<ActorState>();
            if (battle == null || battle.Run == null || !HasFreeSwap(actor)) return result;
            bool jet = Has(actor, ProstheticKind.SteamJetLeg);
            bool wire = Has(actor, ProstheticKind.SteelWireLeg);
            if (!jet && !wire) return result;
            var heroes = battle.Run.Heroes;
            for (int i = 0; i < heroes.Count; i++)
            {
                var other = heroes[i];
                if (other == null || other.Dead || other == actor) continue;
                // Only one leg piece can be fitted, but if both were ever present the wire's wider reach wins.
                if (jet && !wire && other.Rank != actor.Rank - 1) continue;
                result.Add(other);
            }
            return result;
        }

        public static string FreeSwapDisabledReason(BattleEngine battle, ActorState actor)
        {
            if (actor == null) return "没有可换位的角色。";
            if (BlocksActiveMovement(actor)) return "桩锚支撑足无法主动移动，只能由队友换位。";
            bool jet = Has(actor, ProstheticKind.SteamJetLeg);
            bool wire = Has(actor, ProstheticKind.SteelWireLeg);
            if (!jet && !wire) return "没有可用的免费换位义体。";
            var state = actor.ProstheticBattle;
            if (state != null && state.FreeSwapUsed) return "本次行动已经使用过免费换位。";
            if (FreeSwapTargets(battle, actor).Count == 0)
                return jet ? "前方没有相邻队友可供换位。" : "没有其他存活队友可供换位。";
            return null;
        }

        // Single entry point for "restore HP". Applies the receiver's healing modifiers unless the effect is
        // a direct set (revival, Martyr Chain), and clamps to the derived MaxHp. Returns the actual gain.
        public static int RestoreHp(ActorState target, int raw, bool applyHealingModifiers = true)
        {
            if (target == null || target.Dead || raw <= 0) return 0;
            int amount = applyHealingModifiers
                ? (int)Math.Ceiling(raw * HealReceivedMultiplier(target) - 1e-9)
                : raw;
            int restored = Math.Max(0, Math.Min(amount, target.MaxHp - target.Hp));
            if (restored > 0) target.Hp += restored;
            return restored;
        }

        public static string RestrictionText(string silhouette)
        {
            switch (silhouette)
            {
                case "gunner": return "仅枪手可装备。";
                case "medic": return "仅医师可装备。";
                case "warrior": return "仅战士可装备。";
                case "shield": return "仅盾卫可装备。";
                default: return "";
            }
        }

        public static string RestrictionText(ProstheticSpec item)
        {
            return item == null ? "" : RestrictionText(item.RestrictSilhouette);
        }

    }
}
