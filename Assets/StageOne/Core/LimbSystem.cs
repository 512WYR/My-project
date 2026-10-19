using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // The single entry point for limb state and limb damage. Limb health is a second track next to HP:
    // it only ever goes down, it never regenerates on its own, and a part at zero stays severed until a
    // prosthetic replaces it. The track belongs to heroes only: monsters have no parts at all, and Damage
    // refuses every actor on the enemy side so no caller can sever one even if a debug tool attached limbs.
    public static class LimbSystem
    {
        public const double DefaultHitRatio = 0.5;
        public const double DefaultDamageRatio = 0.5;

        public static readonly LimbPartRule[] DefaultParts =
        {
            new LimbPartRule(LimbPart.Head, 0.30, 0.10, 0.25),
            new LimbPartRule(LimbPart.Torso, 0.45, 0.20, 0.40),
            new LimbPartRule(LimbPart.Arm, 0.35, 0.20, 0.45),
            new LimbPartRule(LimbPart.Leg, 0.35, 0.20, 0.45)
        };

        // The first four are conventional repair pieces with no trade-off. The rest are the steampunk
        // prosthetics: four universal series (head/arm/leg/torso) plus two character-exclusive pieces per
        // hero. Effects live in ProstheticSystem; this table only carries identity and the install rules.
        public static readonly ProstheticSpec[] DefaultProsthetics =
        {
            new ProstheticSpec("prosthetic_head", "颅骨支架", LimbPart.Head, "替换受损的头骨，恢复基础感知。", ProstheticKind.Conventional),
            new ProstheticSpec("prosthetic_torso", "胸腔支架", LimbPart.Torso, "支撑躯干，恢复基础防护。", ProstheticKind.Conventional),
            new ProstheticSpec("prosthetic_arm", "义肢手臂", LimbPart.Arm, "替换失去的手臂，恢复基础攻击。", ProstheticKind.Conventional),
            new ProstheticSpec("prosthetic_leg", "义肢腿", LimbPart.Leg, "替换失去的腿，恢复基础移动。", ProstheticKind.Conventional),

            // 铸铁堡垒：重甲与攻城。
            new ProstheticSpec("cast_iron_bell_head", "铸铁潜钟颅", LimbPart.Head,
                "受到攻击伤害 -30%；攻击命中率 -20 个百分点。", ProstheticKind.CastIronBellHead),
            new ProstheticSpec("siege_hydraulic_arm", "攻城液压臂", LimbPart.Arm,
                "造成攻击伤害 +60%；自身受到攻击伤害 +30%。", ProstheticKind.SiegeHydraulicArm),
            new ProstheticSpec("pile_anchor_leg", "桩锚支撑足", LimbPart.Leg,
                "免疫敌方强制位移；己方 1 号位时受伤 -35%；无法主动移动，只能由队友换位；带自身移动的攻击原地执行。",
                ProstheticKind.PileAnchorLeg),
            new ProstheticSpec("armored_pressure_torso", "装甲承压舱", LimbPart.Torso,
                "最大生命 +70%；受到生命治疗 -40%。", ProstheticKind.ArmoredPressureTorso),

            // 赤炉狂徒：以血换伤。
            new ProstheticSpec("pain_amplifier_crown", "痛觉增幅冠", LimbPart.Head,
                "当前生命 ≤50% 时造成攻击伤害 +60%；受到生命治疗 -30%。", ProstheticKind.PainAmplifierCrown),
            new ProstheticSpec("detonation_stroke_arm", "爆燃冲程臂", LimbPart.Arm,
                "主动攻击技能伤害 +100%；每次施放后损失 15% 最大生命，命中与否均扣，最低留 1。",
                ProstheticKind.DetonationStrokeArm),
            new ProstheticSpec("steam_jet_leg", "喷汽突进腿", LimbPart.Leg,
                "每次行动开始可免费与前方相邻队友换位；换位后本次行动主动攻击 +50%，自身受伤 +40% 至下次行动开始。",
                ProstheticKind.SteamJetLeg),
            new ProstheticSpec("redline_steam_heart", "红线蒸汽心", LimbPart.Torso,
                "主动攻击对敌造成伤害后恢复 10% 最大生命，每次施法一次；每次触发失去 8 点理智。",
                ProstheticKind.RedlineSteamHeart),

            // 黄铜游隼：位移与命中。
            new ProstheticSpec("clockwork_compound_eye", "钟表复眼", LimbPart.Head,
                "攻击命中率 +30 个百分点；最大生命 -30%。", ProstheticKind.ClockworkCompoundEye),
            new ProstheticSpec("gyro_charge_arm", "陀螺蓄力臂", LimbPart.Arm,
                "自身站位改变后，下一次主动攻击 +70%，不可叠加，用后消耗；没有位移时主动攻击 -25%。",
                ProstheticKind.GyroChargeArm),
            new ProstheticSpec("steel_wire_leg", "钢索巡游腿", LimbPart.Leg,
                "每次行动开始可与任意队友换位一次，不消耗行动；使用后自身与队友受伤 +25% 至使用者下次行动开始。",
                ProstheticKind.SteelWireLeg),
            new ProstheticSpec("hollow_wing_frame", "镂空翼架胸腔", LimbPart.Torso,
                "己方 3、4 号位时受到攻击伤害 -40%；己方 1、2 号位时受到攻击伤害 +50%。",
                ProstheticKind.HollowWingFrame),

            // 白雾续生：治疗与流血代价。
            new ProstheticSpec("condensation_head", "冷凝镇静颅", LimbPart.Head,
                "每次自身行动结束恢复 8 点理智；造成攻击伤害 -25%。", ProstheticKind.CondensationHead),
            new ProstheticSpec("leech_return_arm", "刺吸回流臂", LimbPart.Arm,
                "主动攻击对敌造成伤害后恢复 15% 生命，每次施法一次；每次触发对自身施加 2 层流血。",
                ProstheticKind.LeechReturnArm),
            new ProstheticSpec("bedframe_leg", "驻床支撑架", LimbPart.Leg,
                "行动结束若本次行动未改变站位，恢复 5% 生命；每次站位改变失去 5 点理智。",
                ProstheticKind.BedframeLeg),
            new ProstheticSpec("double_pump_torso", "双泵再生胸腔", LimbPart.Torso,
                "受到生命治疗 +75%；受到流血伤害 +100%。", ProstheticKind.DoublePumpTorso),

            // 枪手专属。
            new ProstheticSpec("gunner_monocle", "独眼炮术镜", LimbPart.Head,
                "「指定射击」主目标伤害 +80%（含低理智）；「指定射击」只能选敌方 3、4 号位主目标，溅射仍按相邻位置。",
                ProstheticKind.GunnerMonocle, "gunner"),
            new ProstheticSpec("whaling_harpoon_arm", "捕鲸炮锚臂", LimbPart.Arm,
                "「拖拽射击」替换为「贯体锚枪」；用后枪手向前一位并与前方相邻队友换位，无队友则不移动。",
                ProstheticKind.WhalingHarpoonArm, "gunner"),

            // 盾卫专属。
            new ProstheticSpec("vengeance_flywheel_arm", "复仇飞轮臂", LimbPart.Arm,
                "反击伤害 +100%，不增加次数；每次实际反击后损失 8% 最大生命，最低留 1。",
                ProstheticKind.VengeanceFlywheelArm, "shield"),
            new ProstheticSpec("double_valve_boiler_torso", "双阀承伤炉", LimbPart.Torso,
                "「守护」替换为「双线守护」；通过该技能代承伤害 +25%。",
                ProstheticKind.DoubleValveBoilerTorso, "shield"),

            // 战士专属。
            new ProstheticSpec("ratchet_leg", "棘轮追杀腿", LimbPart.Leg,
                "「斩劈」伤害 +70%，可从己方 4 号位施放；使用斩劈时尽可能前移两位，沿途依次换位。",
                ProstheticKind.RatchetLeg, "warrior"),
            new ProstheticSpec("steam_city_hammer_arm", "蒸汽碎城锤", LimbPart.Arm,
                "「猛击」替换为「锅炉震荡」；使用后自身受伤 +50%，至下次行动开始。",
                ProstheticKind.SteamCityHammerArm, "warrior"),

            // 奶妈专属。
            new ProstheticSpec("mad_surgeon_lens", "狂医手术镜", LimbPart.Head,
                "「流血奶」治疗 +80%（含低理智）；流血概率 50%→100%，层数保持 3/4。",
                ProstheticKind.MadSurgeonLens, "medic"),
            new ProstheticSpec("pressure_drug_arm", "高压灌药臂", LimbPart.Arm,
                "「群体药雾」替换为「沸血药雾」；使用后对所有接受药雾者各施加 2 层流血，包括满血角色。",
                ProstheticKind.PressureDrugArm, "medic")
        };

        // Fills in anything a catalog left blank. Every GameCatalog asset predates these fields, so an
        // absent value arrives as zero or null and has to fall back to the rule default.
        public static LimbRules Resolve(LimbRules rules)
        {
            var source = rules ?? new LimbRules();
            return new LimbRules
            {
                HitRatio = Finite(source.HitRatio) && source.HitRatio > 0 ? source.HitRatio : DefaultHitRatio,
                DamageRatio = EffectiveDamageRatio(source),
                Parts = UsableParts(source.Parts) ? CloneParts(source.Parts) : CloneParts(DefaultParts),
                Prosthetics = UsableProsthetics(source.Prosthetics)
                    ? CloneProsthetics(source.Prosthetics) : CloneProsthetics(DefaultProsthetics)
            };
        }

        // Read on every landed hit. Keeping this separate from Resolve avoids cloning the part and
        // prosthetic tables just to fetch one scalar.
        public static double EffectiveDamageRatio(LimbRules rules)
        {
            double ratio = rules == null ? 0 : rules.DamageRatio;
            return Finite(ratio) && ratio > 0 ? ratio : DefaultDamageRatio;
        }

        public static List<LimbState> CreateLimbs(int maxHp, LimbRules rules)
        {
            var resolved = Resolve(rules);
            var limbs = new List<LimbState>();
            for (int i = 0; i < resolved.Parts.Length; i++)
            {
                var rule = resolved.Parts[i];
                int max = Math.Max(1, (int)Math.Ceiling(maxHp * rule.HpRatio - 1e-9));
                limbs.Add(new LimbState { Part = rule.Part, Hp = max, MaxHp = max });
            }
            return limbs;
        }

        // Rebuilds the part ceilings in place without touching what is fitted. GrowthBuff used to call
        // CreateLimbs, which silently threw away every prosthetic when a run gained MaxHp.
        public static void ResizeLimbs(ActorState actor, int baseMaxHp, LimbRules rules)
        {
            if (actor == null || actor.Limbs == null) return;
            var resolved = Resolve(rules);
            for (int i = 0; i < resolved.Parts.Length; i++)
            {
                var rule = resolved.Parts[i];
                var limb = Get(actor, rule.Part);
                if (limb == null) continue;
                limb.MaxHp = Math.Max(1, (int)Math.Ceiling(baseMaxHp * rule.HpRatio - 1e-9));
                limb.Hp = Math.Min(limb.Hp, limb.MaxHp);
            }
            actor.Hp = Math.Min(actor.Hp, actor.MaxHp);
        }

        public static LimbState Get(ActorState actor, LimbPart part)
        {
            if (actor == null || actor.Limbs == null) return null;
            return actor.Limbs.FirstOrDefault(l => l != null && l.Part == part);
        }

        public static LimbCondition ConditionOf(ActorState actor, LimbPart part) { return ConditionOf(Get(actor, part)); }

        public static LimbCondition ConditionOf(LimbState limb)
        {
            if (limb == null) return LimbCondition.Intact;
            if (limb.Hp <= 0) return LimbCondition.Severed;
            return limb.Hp * 2 <= limb.MaxHp ? LimbCondition.Injured : LimbCondition.Intact;
        }

        public static bool HasProsthetic(ActorState actor, LimbPart part)
        {
            var limb = Get(actor, part);
            return limb != null && limb.ProstheticFitted;
        }

        public static bool AnyLimbDamaged(ActorState actor)
        {
            if (actor == null || actor.Limbs == null) return false;
            return actor.Limbs.Any(l => l != null && l.Hp < l.MaxHp);
        }

        // Damage per limb hit for this actor and part. Derived from the part's own maximum so a tougher
        // character takes proportionally tougher hits but still breaks in the same number of them.
        public static int HitAmount(ActorState actor, LimbPart part, LimbRules rules)
        {
            var limb = Get(actor, part);
            if (limb == null) return 0;
            double ratio = Finite(rules == null ? 0 : rules.HitRatio) && rules.HitRatio > 0 ? rules.HitRatio : DefaultHitRatio;
            return Math.Max(1, (int)Math.Ceiling(limb.MaxHp * ratio - 1e-9));
        }

        // Returns true when this hit is the one that severed the part. Limb health never rises here.
        // A prosthetic is wrecked along with the part it replaced, so a second amputation costs another
        // prosthetic rather than leaving a broken fitting that still reports full function.
        public static bool Damage(ActorState actor, LimbPart part, int amount)
        {
            // The limb track is hero-only. Enemies are created without Limbs, but this guard makes the
            // rule independent of that construction detail: no monster can ever be dismembered.
            if (actor == null || !actor.Player) return false;
            var limb = Get(actor, part);
            if (limb == null || amount <= 0 || limb.Hp <= 0) return false;
            limb.Hp = Math.Max(0, limb.Hp - amount);
            if (limb.Hp > 0) return false;
            // A destroyed fitting takes its effects with it. MaxHp is derived from the fitted kinds, so
            // losing a +MaxHp piece can drop the ceiling below the current value; clamp after clearing.
            bool hadProsthetic = limb.ProstheticFitted;
            limb.Prosthetic = null;
            limb.ProstheticKind = ProstheticKind.Conventional;
            if (hadProsthetic) actor.Hp = Math.Min(actor.Hp, actor.MaxHp);
            return true;
        }

        // Only an injured part can be repaired. A severed part needs a prosthetic instead.
        public static bool CanRepair(ActorState actor, LimbPart part)
        {
            return ConditionOf(actor, part) == LimbCondition.Injured;
        }

        public static bool Repair(ActorState actor, LimbPart part)
        {
            if (!CanRepair(actor, part)) return false;
            Get(actor, part).Hp = Get(actor, part).MaxHp;
            return true;
        }

        // A prosthetic only goes onto a part that is already gone: a part still carrying a fitting is not
        // severed, and one that is severed lost its fitting with it. Character-exclusive pieces also have
        // to match the actor's silhouette. Null means installable; every other value is player-facing.
        public static string InstallBlockedReason(ActorState actor, ProstheticSpec item)
        {
            if (actor == null || item == null) return "义体不存在。";
            var limb = Get(actor, item.Part);
            if (limb == null) return "该角色没有" + PartName(item.Part) + "部位。";
            if (limb.Hp > 0) return "只能安装在断肢上。";
            if (!string.IsNullOrEmpty(item.RestrictSilhouette)
                && (actor.Definition == null || item.RestrictSilhouette != actor.Definition.Silhouette))
                return ProstheticSystem.RestrictionText(item);
            return null;
        }

        public static bool CanInstall(ActorState actor, ProstheticSpec item)
        {
            return InstallBlockedReason(actor, item) == null;
        }

        public static bool Install(ActorState actor, ProstheticSpec item)
        {
            if (!CanInstall(actor, item)) return false;
            var limb = Get(actor, item.Part);
            limb.Prosthetic = item.Id;
            limb.ProstheticKind = item.Kind;
            limb.Hp = limb.MaxHp;
            // Installing a -MaxHp piece can lower the ceiling below the current value.
            actor.Hp = Math.Min(actor.Hp, actor.MaxHp);
            return true;
        }

        public static ProstheticSpec Prosthetic(string id, LimbRules rules)
        {
            return Resolve(rules).Prosthetics.FirstOrDefault(p => p != null && p.Id == id);
        }

        // Copies an item before it enters a run inventory. Kind and RestrictSilhouette have to survive the
        // copy: a clone built through the four-argument constructor would read as a conventional piece.
        public static ProstheticSpec Clone(ProstheticSpec item)
        {
            if (item == null) return null;
            return new ProstheticSpec(item.Id, item.Name, item.Part, item.Description,
                item.Kind, item.RestrictSilhouette);
        }

        // The four attribute factors. A fitted prosthetic always returns the neutral value, which is what
        // "a conventional prosthetic restores basic function" means in practice. Severed replaces injured
        // rather than stacking with it.
        public static double AttackFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Arm); }
        public static double DefenseFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Torso); }
        public static double SpeedFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Leg); }
        public static double AccuracyFactor(ActorState actor, LimbRules rules) { return Factor(actor, rules, LimbPart.Head); }

        private static double Factor(ActorState actor, LimbRules rules, LimbPart part)
        {
            if (actor == null || !actor.Player) return 1;
            var limb = Get(actor, part);
            if (limb == null || limb.ProstheticFitted) return 1;
            var rule = RuleOrDefault(rules, part);
            if (limb.Hp <= 0) return 1 - rule.SeveredPenalty;
            if (limb.Hp * 2 <= limb.MaxHp) return 1 - rule.InjuredPenalty;
            return 1;
        }

        public static string PartName(LimbPart part)
        {
            switch (part)
            {
                case LimbPart.Head: return "头部";
                case LimbPart.Torso: return "躯干";
                case LimbPart.Arm: return "手臂";
                case LimbPart.Leg: return "腿部";
                default: return "部位";
            }
        }

        public static string ConditionName(LimbCondition condition)
        {
            switch (condition)
            {
                case LimbCondition.Injured: return "重伤";
                case LimbCondition.Severed: return "断肢";
                default: return "完好";
            }
        }

        // Short label for the battle card, e.g. "断肢 手臂". Empty while every part is intact.
        public static string Summary(ActorState actor)
        {
            if (actor == null || actor.Limbs == null) return "";
            var severed = actor.Limbs.FirstOrDefault(l => l != null && l.Hp <= 0);
            if (severed != null) return "断肢 " + PartName(severed.Part);
            var injured = actor.Limbs.FirstOrDefault(l => l != null && l.Hp * 2 <= l.MaxHp);
            return injured == null ? "" : "重伤 " + PartName(injured.Part);
        }

        private static LimbPartRule RuleOrDefault(LimbRules rules, LimbPart part)
        {
            if (rules != null && rules.Parts != null)
                for (int i = 0; i < rules.Parts.Length; i++)
                    if (rules.Parts[i] != null && rules.Parts[i].Part == part) return rules.Parts[i];
            for (int i = 0; i < DefaultParts.Length; i++)
                if (DefaultParts[i].Part == part) return DefaultParts[i];
            return new LimbPartRule(part, 0.35, 0.20, 0.45);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool UsableParts(LimbPartRule[] values)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
            {
                var rule = values[i];
                if (rule == null) return false;
                if (!Finite(rule.HpRatio) || rule.HpRatio <= 0) return false;
                if (!Finite(rule.InjuredPenalty) || !Finite(rule.SeveredPenalty)) return false;
                if (rule.InjuredPenalty < 0 || rule.InjuredPenalty >= 1) return false;
                if (rule.SeveredPenalty < 0 || rule.SeveredPenalty >= 1) return false;
            }
            return true;
        }

        private static bool UsableProsthetics(ProstheticSpec[] values)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == null || string.IsNullOrEmpty(values[i].Id) || string.IsNullOrEmpty(values[i].Name)) return false;
            return true;
        }

        private static LimbPartRule[] CloneParts(LimbPartRule[] values)
        {
            var copy = new LimbPartRule[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = new LimbPartRule(values[i].Part, values[i].HpRatio, values[i].InjuredPenalty, values[i].SeveredPenalty);
            return copy;
        }

        private static ProstheticSpec[] CloneProsthetics(ProstheticSpec[] values)
        {
            var copy = new ProstheticSpec[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = Clone(values[i]);
            return copy;
        }
    }
}
