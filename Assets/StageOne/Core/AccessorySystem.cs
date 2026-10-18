using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // The single entry point for accessories, mirroring LimbSystem. The content table lives here rather
    // than in a ScriptableObject for the same reason LimbSystem.DefaultProsthetics and
    // MapGenerator.DefaultOdds do: a GameCatalog asset made before a new array field existed reads that
    // field back as empty, so content placed there would silently disappear.
    //
    // Every number the design document left open is a provisional constant in this one block. Change a
    // value here and both the battle engine and the UI previews follow.
    public static class AccessorySystem
    {
        // ---- 暂定数值（文档标“待明确/不自动补设”的部分集中在这里） ----
        public const int GutterBleed = 2;
        public const int GutterLimbDamage = 1;
        public const double GutterAccuracyFloor = 0.90;
        public const double SleepSanityDrain = 10;
        public const double AddictionAttackBonus = 0.30;
        public const double AddictionSanityRestore = 5;
        public const double AddictionVulnerabilityPerStack = 0.15;
        public const int AddictionMaxStacks = 10;
        public const int AppleMaxHpBonus = 10;
        public const double AppleSanityPerHit = 5;
        public const double HeartAttackBonus = 0.50;
        public const double HeartFailureMultiplier = 0.30;
        public const double SyringeHealMultiplier = 2.0;
        public const double SyringeSelfCost = 0.20;
        public const double RustEntryLoss = 0.50;
        public const double RustDamageTakenBonus = 0.05;
        public const double RustThreshold = 0.10;
        public const double MartyrGuardReduction = 0.50;
        public const double MartyrRestore = 0.20;
        public const double MartyrSanityLoss = 10;
        public const string RustSkillId = "w_gambit";

        public static readonly AccessorySpec[] Defaults =
        {
            new AccessorySpec
            {
                Id = "acc_gutter", Name = "穿肠弹", Kind = AccessoryKind.Gutter, RestrictSilhouette = "gunner",
                Description = "攻击必定暴击，命中率不低于90%。每次命中自身随机部位-1，并叠加2点流血。"
            },
            new AccessorySpec
            {
                Id = "acc_sleep", Name = "假寐的药剂", Kind = AccessoryKind.SleepPotion,
                Description = "每场一次：致命攻击的伤害归零，随机部位重创、理智-10，之后2回合每回合理智-10。"
            },
            new AccessorySpec
            {
                Id = "acc_addiction", Name = "成瘾物", Kind = AccessoryKind.Addiction,
                Description = "入场攻击+30%。每次攻击自身理智+5、受到伤害+15%（可叠加）。击杀敌人后失效。"
            },
            new AccessorySpec
            {
                Id = "acc_apple", Name = "金丝苹果", Kind = AccessoryKind.GoldenApple,
                Description = "装备时最大生命+10。生命不低于1，部位不受伤害。每次受攻击理智-5；理智归零时可能直接死亡。"
            },
            new AccessorySpec
            {
                Id = "acc_heart", Name = "跃动心脏", Kind = AccessoryKind.Heart,
                Description = "入场标记一名敌人：每回合额外行动1次、攻击+50%。前2回合击杀标记目标则持续整场；否则攻击-70%。"
            },
            new AccessorySpec
            {
                Id = "acc_syringe", Name = "献血针筒", Kind = AccessoryKind.Syringe, RestrictSilhouette = "medic",
                Description = "治疗量+100%。每次使用治疗技能后，随机一名队员损失20%最大生命，群体治疗只扣一次。"
            },
            new AccessorySpec
            {
                Id = "acc_rust", Name = "锈血勋章", Kind = AccessoryKind.RustMedal, RestrictSilhouette = "warrior",
                Description = "入场损失50%最大生命。受到伤害+5%。带伤强攻+1次；每累计受到10%最大生命伤害再+1次。"
            },
            new AccessorySpec
            {
                Id = "acc_martyr", Name = "殉道锁链", Kind = AccessoryKind.MartyrChain, RestrictSilhouette = "shield",
                Description = "代守时拦截伤害-50%。生命归零时消耗1个未断部位，恢复20%最大生命并理智-10；部位全断则死亡。"
            }
        };

        public static AccessorySpec Clone(AccessorySpec item)
        {
            if (item == null) return null;
            return new AccessorySpec
            {
                Id = item.Id, Name = item.Name, Description = item.Description,
                Kind = item.Kind, RestrictSilhouette = item.RestrictSilhouette
            };
        }

        public static AccessorySpec Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Defaults.FirstOrDefault(a => a != null && a.Id == id);
        }

        public static bool Has(ActorState actor, AccessoryKind kind)
        {
            return actor != null && actor.EquippedAccessories != null
                && actor.EquippedAccessories.Any(a => a != null && a.Kind == kind);
        }

        public static AccessorySpec Equipped(ActorState actor, AccessoryKind kind)
        {
            return actor == null || actor.EquippedAccessories == null ? null
                : actor.EquippedAccessories.FirstOrDefault(a => a != null && a.Kind == kind);
        }

        public static bool CanEquip(ActorState actor, AccessorySpec item)
        {
            if (actor == null || actor.Definition == null || item == null || actor.Dead) return false;
            if (actor.EquippedAccessories == null) actor.EquippedAccessories = new List<AccessorySpec>();
            if (!string.IsNullOrEmpty(item.RestrictSilhouette)
                && item.RestrictSilhouette != actor.Definition.Silhouette) return false;
            // Unlimited accessories per hero, but the same Id may only appear once on that hero.
            return !actor.EquippedAccessories.Any(a => a != null && a.Id == item.Id);
        }

        public static string RestrictionText(AccessorySpec item)
        {
            if (item == null || string.IsNullOrEmpty(item.RestrictSilhouette)) return "不限角色";
            switch (item.RestrictSilhouette)
            {
                case "warrior": return "战士专属";
                case "shield": return "盾卫专属";
                case "gunner": return "枪手专属";
                case "medic": return "奶妈专属";
                default: return item.RestrictSilhouette + "专属";
            }
        }

        public static string EquipBlockedReason(ActorState actor, AccessorySpec item)
        {
            if (actor == null || item == null) return "没有可装备的对象。";
            if (actor.Dead) return actor.Name + " 已经阵亡，不能装备饰品。";
            if (!string.IsNullOrEmpty(item.RestrictSilhouette)
                && actor.Definition != null && item.RestrictSilhouette != actor.Definition.Silhouette)
                return actor.Name + " 不能装备「" + item.Name + "」（" + RestrictionText(item) + "）。";
            if (actor.EquippedAccessories != null && actor.EquippedAccessories.Any(a => a != null && a.Id == item.Id))
                return "同一角色不能重复装备「" + item.Name + "」。";
            return null;
        }

        public static void Attach(ActorState actor, AccessorySpec item)
        {
            if (actor == null || item == null) return;
            if (actor.EquippedAccessories == null) actor.EquippedAccessories = new List<AccessorySpec>();
            var copy = Clone(item);
            actor.EquippedAccessories.Add(copy);
            if (copy.Kind == AccessoryKind.GoldenApple && actor.Definition != null)
                actor.Definition.MaxHp = Math.Max(1, actor.Definition.MaxHp + AppleMaxHpBonus);
        }

        public static bool Detach(ActorState actor, string itemId)
        {
            if (actor == null || actor.EquippedAccessories == null || string.IsNullOrEmpty(itemId)) return false;
            var item = actor.EquippedAccessories.FirstOrDefault(a => a != null && a.Id == itemId);
            if (item == null) return false;
            actor.EquippedAccessories.Remove(item);
            if (item.Kind == AccessoryKind.GoldenApple && actor.Definition != null)
            {
                actor.Definition.MaxHp = Math.Max(1, actor.Definition.MaxHp - AppleMaxHpBonus);
                if (actor.Hp > actor.Definition.MaxHp) actor.Hp = actor.Definition.MaxHp;
            }
            return true;
        }

        public static bool Equip(RunState run, ActorState hero, int stockIndex, out string note)
        {
            note = null;
            if (run == null || hero == null || run.AccessoryStock == null
                || stockIndex < 0 || stockIndex >= run.AccessoryStock.Count)
            {
                note = "请选择库存中的饰品。";
                return false;
            }
            var item = run.AccessoryStock[stockIndex];
            var blocked = EquipBlockedReason(hero, item);
            if (blocked != null) { note = blocked; return false; }
            run.AccessoryStock.RemoveAt(stockIndex);
            Attach(hero, item);
            note = hero.Name + " 装备了「" + item.Name + "」。";
            return true;
        }

        public static bool Unequip(RunState run, ActorState hero, string itemId, out string note)
        {
            note = null;
            if (run == null || hero == null) { note = "没有可卸下的对象。"; return false; }
            var item = hero.EquippedAccessories == null ? null
                : hero.EquippedAccessories.FirstOrDefault(a => a != null && a.Id == itemId);
            if (item == null) { note = "这件饰品不在该角色身上。"; return false; }
            Detach(hero, itemId);
            if (run.AccessoryStock == null) run.AccessoryStock = new List<AccessorySpec>();
            run.AccessoryStock.Add(Clone(item));
            note = hero.Name + " 卸下了「" + item.Name + "」，已放回队伍仓库。";
            return true;
        }

        public static AccessorySpec Drop(RunRandom random)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (Defaults.Length == 0) return null;
            int index = Math.Min(Defaults.Length - 1, (int)(random.NextUnit() * Defaults.Length));
            return Clone(Defaults[index]);
        }

        // ---- battle queries ----

        public static double AttackMultiplier(ActorState actor)
        {
            if (actor == null) return 1;
            var state = actor.AccessoryBattle;
            double multiplier = 1;
            if (state != null && state.AddictionActive && Has(actor, AccessoryKind.Addiction))
                multiplier *= 1 + AddictionAttackBonus;
            if (Has(actor, AccessoryKind.Heart))
            {
                bool failed = state != null && state.HeartResolved && !state.HeartSuccess;
                multiplier *= failed ? HeartFailureMultiplier : 1 + HeartAttackBonus;
            }
            return multiplier;
        }

        public static double DamageTakenFactor(ActorState actor)
        {
            if (actor == null) return 1;
            var state = actor.AccessoryBattle;
            double factor = 1;
            if (state != null && state.AddictionActive && Has(actor, AccessoryKind.Addiction))
            {
                int stacks = Math.Max(0, Math.Min(AddictionMaxStacks, state.AddictionStacks));
                factor *= 1 + stacks * AddictionVulnerabilityPerStack;
            }
            if (Has(actor, AccessoryKind.RustMedal)) factor *= 1 + RustDamageTakenBonus;
            return factor;
        }

        public static double HealMultiplier(ActorState actor)
        {
            return Has(actor, AccessoryKind.Syringe) ? SyringeHealMultiplier : 1;
        }

        public static double AccuracyFloor(ActorState actor)
        {
            return AlwaysCrit(actor) ? GutterAccuracyFloor : 0;
        }

        public static bool AlwaysCrit(ActorState actor) { return Has(actor, AccessoryKind.Gutter); }
        public static bool HpLock(ActorState actor) { return Has(actor, AccessoryKind.GoldenApple); }
        public static bool LimbLock(ActorState actor) { return Has(actor, AccessoryKind.GoldenApple); }

        public static int ExtraActions(ActorState actor)
        {
            if (!Has(actor, AccessoryKind.Heart)) return 0;
            var state = actor.AccessoryBattle;
            if (state == null) return 1;
            return state.HeartResolved && !state.HeartSuccess ? 0 : 1;
        }

        public static string Summary(ActorState actor)
        {
            if (actor == null || actor.EquippedAccessories == null || actor.EquippedAccessories.Count == 0) return "无";
            return string.Join(" · ", actor.EquippedAccessories.Where(a => a != null).Select(a => a.Name));
        }

        public static string ShortSummary(ActorState actor)
        {
            if (actor == null || actor.EquippedAccessories == null) return "";
            var first = actor.EquippedAccessories.FirstOrDefault(a => a != null);
            return first == null ? "" : first.Name;
        }

        public static LimbPart RandomPart(RunRandom random, ActorState actor = null, bool unseveredOnly = false)
        {
            var parts = new List<LimbPart> { LimbPart.Head, LimbPart.Torso, LimbPart.Arm, LimbPart.Leg };
            if (actor != null && unseveredOnly)
                parts = parts.Where(p => LimbSystem.ConditionOf(actor, p) != LimbCondition.Severed).ToList();
            if (parts.Count == 0) return LimbPart.Head;
            int index = random == null ? 0 : Math.Min(parts.Count - 1, (int)(random.NextUnit() * parts.Count));
            return parts[index];
        }

        public static ActorState RandomLivingHero(RunState run, RunRandom random)
        {
            if (run == null || run.Heroes == null) return null;
            var pool = run.Heroes.Where(h => h != null && !h.Dead).ToArray();
            if (pool.Length == 0) return null;
            int index = random == null ? 0 : Math.Min(pool.Length - 1, (int)(random.NextUnit() * pool.Length));
            return pool[index];
        }

        public static SkillSpec FindSkill(ActorState actor, string skillId)
        {
            if (actor == null || actor.Definition == null || string.IsNullOrEmpty(skillId)) return null;
            return AllSkills(actor).FirstOrDefault(s => s != null && s.Id == skillId);
        }

        public static bool AddSkillUse(ActorState actor, string skillId, int amount)
        {
            var skill = FindSkill(actor, skillId);
            if (skill == null || skill.Uses <= 0 || amount <= 0) return false;
            if (actor.Uses == null) actor.Uses = new List<SkillUse>();
            var use = actor.Uses.FirstOrDefault(u => u != null && u.Id == skillId);
            if (use == null) actor.Uses.Add(new SkillUse { Id = skillId, Left = skill.Uses + amount });
            else use.Left = Math.Max(0, use.Left + amount);
            return true;
        }

        private static IEnumerable<SkillSpec> AllSkills(ActorState actor)
        {
            var d = actor.Definition;
            return (d.BasicSkills ?? ActorSpec.EmptySkills)
                .Concat(d.PrimalSkills ?? ActorSpec.EmptySkills)
                .Concat(d.TechnicalSkills ?? ActorSpec.EmptySkills);
        }
    }
}
