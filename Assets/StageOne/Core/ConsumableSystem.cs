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
                Id = "con_surgery_kit", Name = "手术包", Kind = ConsumableKind.SurgeryKit, Implemented = false,
                Description = "功能待定。可以持有与查看，暂时不能使用。"
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
            if (!item.Implemented || item.Kind == ConsumableKind.SurgeryKit) return "功能待定。";
            if (hero == null) return "请选择一名队员。";
            if (hero.Dead) return "已经阵亡。";
            switch (item.Kind)
            {
                case ConsumableKind.SanityLock:
                    if (hero.SanityLocked || hero.SanityLockPending) return "理智已经被锁定。";
                    break;
                case ConsumableKind.HpRestore:
                    if (hero.Definition == null || hero.Hp >= hero.Definition.MaxHp) return "生命已满。";
                    break;
            }
            return null;
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
