using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // The single entry point for single-use items, mirroring AccessorySystem. The content table lives in
    // code for the same reason the other two stock tables do: a GameCatalog asset made before a new array
    // field existed reads that field back as empty, so content placed there would silently disappear.
    public static class ConsumableSystem
    {
        // ---- 暂定数值 ----
        public const int HealPercent = 50;

        public static readonly ConsumableSpec[] Defaults =
        {
            new ConsumableSpec
            {
                Id = "con_sanity_lock", Name = "理智锁定药剂", Kind = ConsumableKind.SanityLock,
                Description = "选中一名存活角色：本场战斗内理智不增不减。战斗外使用则锁定下一场战斗。"
            },
            new ConsumableSpec
            {
                Id = "con_hp_restore", Name = "血量恢复药", Kind = ConsumableKind.HpRestore,
                Description = "选中一名存活角色：恢复其最大生命的50%，不能复活阵亡角色。"
            },
            new ConsumableSpec
            {
                Id = "con_surgery_kit", Name = "手术包", Kind = ConsumableKind.SurgeryKit,
                Description = "为一名队员的断肢装载仓库中的一件义体，消耗一个手术包。"
                    + "补给站等特殊地点的安装选项不需要手术包。"
            }
        };

        public static ConsumableSpec Clone(ConsumableSpec item)
        {
            if (item == null) return null;
            return new ConsumableSpec
            {
                Id = item.Id, Name = item.Name, Description = item.Description,
                Kind = item.Kind, Implemented = item.Implemented
            };
        }

        public static ConsumableSpec Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Defaults.FirstOrDefault(c => c != null && c.Id == id);
        }

        public static ConsumableSpec ForKind(ConsumableKind kind)
        {
            return Defaults.FirstOrDefault(c => c != null && c.Kind == kind);
        }

        public static int Count(RunState run, ConsumableKind kind)
        {
            if (run == null || run.Consumables == null) return 0;
            return run.Consumables.Count(c => c != null && c.Kind == kind);
        }

        // "理智锁定药剂 ×2 · 血量恢复药 ×1", or "无" for an empty stock.
        public static string Summary(RunState run)
        {
            if (run == null || run.Consumables == null || run.Consumables.Count == 0) return "无";
            return string.Join(" · ", run.Consumables.Where(c => c != null)
                .GroupBy(c => c.Id)
                .Select(g =>
                {
                    string name = g.First().Name;
                    int count = g.Count();
                    return count > 1 ? name + " ×" + count : name;
                }));
        }

        // Pure query: the UI calls this for every row and for the disabled state, so it must never draw a
        // random number or change the run.
        public static string UseBlockedReason(RunState run, ActorState hero, ConsumableSpec item, bool inBattle)
        {
            if (item == null) return "请选择一件道具。";
            if (!item.Implemented) return "功能待定。";
            // A kit needs a second choice (which prosthetic), so the generic Use path cannot settle it.
            // The character panel installs through UseSurgeryKit with that choice already made.
            if (item.Kind == ConsumableKind.SurgeryKit) return "请在角色面板的义体列表中选择要安装的义体。";
            if (hero == null) return "请选择一名队员。";
            if (hero.Dead) return "已经阵亡。";
            switch (item.Kind)
            {
                case ConsumableKind.SanityLock:
                    if (hero.SanityLocked || hero.SanityLockPending) return "理智已经被锁定。";
                    break;
                case ConsumableKind.HpRestore:
                    if (hero.Definition == null || hero.Hp >= hero.MaxHp) return "生命已满。";
                    break;
            }
            return null;
        }

        // ---- 手术包 ----
        // The kit is the field alternative to a supply station's fitting service: one kit plus one
        // stocked prosthetic fits it onto a severed part. Everything else still follows LimbSystem's
        // install rules (severed part, matching silhouette). A refused fitting keeps both items.

        public static string SurgeryKitBlockedReason(RunState run, ActorState hero, ProstheticSpec item)
        {
            if (item == null) return "请选择一件义体。";
            if (hero == null) return "请选择一名队员。";
            if (hero.Dead) return "已经阵亡。";
            var blocked = LimbSystem.InstallBlockedReason(hero, item);
            if (blocked != null) return blocked;
            if (Count(run, ConsumableKind.SurgeryKit) <= 0) return "需要手术包才能在这里安装义体。";
            return null;
        }

        // Installs and only then spends the kit and the stocked piece, so a refused fitting never
        // changes either list.
        public static bool UseSurgeryKit(RunState run, ActorState hero, ProstheticSpec item, out string note)
        {
            note = null;
            string blocked = SurgeryKitBlockedReason(run, hero, item);
            if (blocked != null) { note = blocked; return false; }
            if (!LimbSystem.Install(hero, item)) { note = "只有断肢才能安装义体。"; return false; }
            int kit = run.Consumables == null ? -1
                : run.Consumables.FindIndex(c => c != null && c.Kind == ConsumableKind.SurgeryKit);
            if (kit >= 0) run.Consumables.RemoveAt(kit);
            if (run.Prosthetics != null) run.Prosthetics.Remove(item);
            note = hero.Name + " 消耗手术包，装上了" + item.Name + "，"
                + LimbSystem.PartName(item.Part) + "恢复基础功能。";
            return true;
        }

        // Consumes one item only after the effect has been accepted, so a refused use leaves the stock and
        // the battle turn untouched.
        public static bool Use(RunState run, ActorState hero, int stockIndex, bool inBattle, out string note)
        {
            note = null;
            if (run == null || run.Consumables == null || stockIndex < 0 || stockIndex >= run.Consumables.Count)
            {
                note = "请选择库存中的道具。";
                return false;
            }
            var item = run.Consumables[stockIndex];
            var blocked = UseBlockedReason(run, hero, item, inBattle);
            if (blocked != null) { note = blocked; return false; }

            run.Consumables.RemoveAt(stockIndex);
            switch (item.Kind)
            {
                case ConsumableKind.HpRestore:
                    int healed = Exploration.Heal(hero, HealPercent);
                    note = hero.Name + " 使用「" + item.Name + "」，恢复 " + healed + " 点生命。";
                    break;
                case ConsumableKind.SanityLock:
                    if (inBattle) hero.SanityLocked = true;
                    else hero.SanityLockPending = true;
                    note = inBattle
                        ? hero.Name + " 使用「" + item.Name + "」，本场战斗内理智锁定。"
                        : hero.Name + " 使用「" + item.Name + "」，下一场战斗的理智将被锁定。";
                    break;
                default:
                    // Unreachable while UseBlockedReason refuses every unimplemented kind; kept so a new
                    // kind added without a branch cannot silently consume the item.
                    run.Consumables.Insert(stockIndex, item);
                    note = "功能待定。";
                    return false;
            }
            return true;
        }
    }
}
