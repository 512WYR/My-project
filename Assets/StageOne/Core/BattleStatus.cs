using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ruin.Core
{
    // The strip under a character and its hover tooltip read from one shared aggregation. Keeping this
    // in Core means the battle engine's own state is the only source of truth: a buff that is spent by
    // the engine disappears here on the next refresh, with no separate bookkeeping to drift.
    public enum BattleStatusTone
    {
        Positive,
        Negative,
        Injury,
        Meta
    }

    public struct BattleStatusEntry
    {
        public string Id;
        public string Name;
        public string Value;
        public string Detail;
        public BattleStatusTone Tone;
    }

    public static class BattleStatus
    {
        private static readonly LimbPart[] LimbOrder =
        {
            LimbPart.Head, LimbPart.Torso, LimbPart.Arm, LimbPart.Leg
        };

        // Fills into the caller's list so a card that refreshes several times a second does not allocate
        // a new collection on every frame.
        public static void Build(ActorState actor, List<BattleStatusEntry> into)
        {
            if (into == null) return;
            into.Clear();
            if (actor == null) return;

            if (actor.BlockCharges > 0)
                Add(into, "block", "格挡", "×" + actor.BlockCharges,
                    "每次受到攻击消耗 1 层，完全免除该次伤害与减益。", BattleStatusTone.Positive);
            if (actor.Counter)
                Add(into, "counter", "反击", null,
                    "受到攻击或替队友承伤后，反击一次。", BattleStatusTone.Positive);
            if (actor.Followup)
                Add(into, "followup", "追击", null,
                    "造成伤害后追加一次攻击。", BattleStatusTone.Positive);
            if (actor.DefenseBuff > 0.0001)
                Add(into, "defense", "防御力", "+" + Percent(actor.DefenseBuff),
                    "防御值提高 " + Percent(actor.DefenseBuff) + "，与基础防御相加；本回合同类效果取最高。",
                    BattleStatusTone.Positive);
            if (actor.AccuracyBuff > 0.0001)
                Add(into, "accuracy", "命中", "+" + Percent(actor.AccuracyBuff),
                    "命中率提高 " + Percent(actor.AccuracyBuff) + "；本回合同类效果取最高。",
                    BattleStatusTone.Positive);
            if (!string.IsNullOrEmpty(actor.ProtectorId))
                Add(into, "protect", "守护", null,
                    "被队友守护：针对此角色的攻击会由守护者承受。", BattleStatusTone.Positive);

            AddAccessories(actor, into);

            if (actor.BleedTurns > 0)
            {
                string value = actor.BleedDamage > 0
                    ? actor.BleedDamage + "×" + actor.BleedTurns
                    : actor.BleedTurns + "回合";
                Add(into, "bleed", "流血", value,
                    "每次行动前受到 " + Math.Max(0, actor.BleedDamage) + " 点流血伤害，剩余 "
                    + actor.BleedTurns + " 次行动。", BattleStatusTone.Negative);
            }
            if (actor.Imbalance > 0)
                Add(into, "imbalance", "失衡", actor.Imbalance > 1 ? "×" + actor.Imbalance : null,
                    "下一次行动会被跳过。", BattleStatusTone.Negative);
            if (actor.SelfVulnerable > 0.0001)
                Add(into, "vulnerable", "易伤", "+" + Percent(actor.SelfVulnerable),
                    "本回合受到的伤害提高 " + Percent(actor.SelfVulnerable) + "。",
                    BattleStatusTone.Negative);
            if (actor.SanityLocked)
                Add(into, "sanity-lock", "理智锁定", null,
                    "理智锁定药剂：本场战斗内理智不会变化。", BattleStatusTone.Meta);

            AddLimbs(actor, into);
            AddProsthetics(actor, into);
            AddAccessoryHints(actor, into);
        }

        private static void AddAccessories(ActorState actor, List<BattleStatusEntry> into)
        {
            var state = actor.AccessoryBattle;
            if (state != null && state.AddictionActive && AccessorySystem.Has(actor, AccessoryKind.Addiction))
            {
                Add(into, "addiction-attack", "攻击力", "+" + Percent(AccessorySystem.AddictionAttackBonus),
                    "成瘾物：攻击力提高 " + Percent(AccessorySystem.AddictionAttackBonus) + "。",
                    BattleStatusTone.Positive);
                if (state.AddictionStacks > 0)
                {
                    double extra = state.AddictionStacks * AccessorySystem.AddictionVulnerabilityPerStack;
                    Add(into, "addiction-vulnerable", "易伤", "+" + Percent(extra),
                        "成瘾物：每次攻击叠加 " + Percent(AccessorySystem.AddictionVulnerabilityPerStack)
                        + " 易伤，当前 " + state.AddictionStacks + " 层。", BattleStatusTone.Negative);
                }
            }

            if (AccessorySystem.Has(actor, AccessoryKind.Heart))
            {
                bool failed = state != null && state.HeartResolved && !state.HeartSuccess;
                Add(into, "heart-attack", "攻击力", failed ? "-70%" : "+" + Percent(AccessorySystem.HeartAttackBonus),
                    failed
                        ? "跃动心脏：攻击判定失败，攻击力降至 30%。"
                        : "跃动心脏：攻击力提高 " + Percent(AccessorySystem.HeartAttackBonus) + "；标记目标存活时生效。",
                    failed ? BattleStatusTone.Negative : BattleStatusTone.Positive);
            }

            if (state != null && state.SleepTurns > 0)
                Add(into, "sleep", "假寐", state.SleepTurns + "回合",
                    "假寐药剂：每回合理智 -" + AccessorySystem.SleepSanityDrain.ToString("0.#", CultureInfo.InvariantCulture)
                    + "，致命伤害会被吞掉一次。", BattleStatusTone.Negative);

            if (AccessorySystem.Has(actor, AccessoryKind.RustMedal))
                Add(into, "rust", "易伤", "+" + Percent(AccessorySystem.RustDamageTakenBonus),
                    "锈血勋章：受到的伤害提高 " + Percent(AccessorySystem.RustDamageTakenBonus) + "。",
                    BattleStatusTone.Negative);
        }

        private static void AddLimbs(ActorState actor, List<BattleStatusEntry> into)
        {
            if (actor.Limbs == null) return;
            for (int i = 0; i < LimbOrder.Length; i++)
            {
                var part = LimbOrder[i];
                var limb = LimbSystem.Get(actor, part);
                // A fitted part is reported by AddProsthetics instead, including its remaining HP.
                if (limb == null || limb.ProstheticFitted) continue;
                if (LimbSystem.ConditionOf(limb) == LimbCondition.Intact) continue;

                string partName = LimbSystem.PartName(part);
                bool severed = limb.Hp <= 0;
                string detail = "部位生命 " + Math.Max(0, limb.Hp) + " / " + Math.Max(1, limb.MaxHp) + "。"
                    + AttributeName(part) + (severed
                        ? "大幅下降；安装义肢可以恢复。"
                        : "下降；修复该部位后恢复。");
                Add(into, "limb:" + part, (severed ? "断肢·" : "受伤·") + partName, null, detail,
                    BattleStatusTone.Injury);
            }
        }

        // Fitted prosthetics are permanent until the part is severed again, so each one gets a passive
        // entry of its own. It is a hint only: the detail quotes the authored description, and the
        // remaining part HP is shown so a piece about to be wrecked is still readable.
        private static void AddProsthetics(ActorState actor, List<BattleStatusEntry> into)
        {
            if (actor.Limbs == null) return;
            for (int i = 0; i < LimbOrder.Length; i++)
            {
                var part = LimbOrder[i];
                var limb = LimbSystem.Get(actor, part);
                if (limb == null || !limb.ProstheticFitted) continue;

                var spec = LimbSystem.FindProsthetic(limb.Prosthetic);
                string piece = spec != null && !string.IsNullOrEmpty(spec.Name) ? spec.Name : "义体";
                string effect = spec != null && !string.IsNullOrEmpty(spec.Description)
                    ? spec.Description
                    : "恢复该部位的基础功能。";
                string partName = LimbSystem.PartName(part);
                Add(into, "prosthetic:" + part, "义体·" + partName, null,
                    "「" + piece + "」" + effect
                    + " 部位：" + partName + "，部位生命 " + Math.Max(0, limb.Hp)
                    + " / " + Math.Max(1, limb.MaxHp) + "。义体会随该部位再次断肢而销毁。",
                    BattleStatusTone.Meta);
            }
        }

        // Equipped accessories keep their authored description on hover. Live numbers stay in the
        // entries above; these hints are the only readout for pieces whose effect has no live state,
        // and they also keep a consumed or not-yet-triggered piece visible.
        private static void AddAccessoryHints(ActorState actor, List<BattleStatusEntry> into)
        {
            var items = actor.EquippedAccessories;
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                Add(into, "accessory:" + (item.Id ?? item.Name), "饰品·" + item.Name, null,
                    string.IsNullOrEmpty(item.Description) ? "已装备。" : item.Description,
                    BattleStatusTone.Meta);
            }
        }

        private static string AttributeName(LimbPart part)
        {
            switch (part)
            {
                case LimbPart.Head: return "命中";
                case LimbPart.Torso: return "防御";
                case LimbPart.Arm: return "攻击";
                case LimbPart.Leg: return "速度";
                default: return "属性";
            }
        }

        private static void Add(List<BattleStatusEntry> into, string id, string name, string value,
            string detail, BattleStatusTone tone)
        {
            into.Add(new BattleStatusEntry
            {
                Id = id,
                Name = name,
                Value = value,
                Detail = detail,
                Tone = tone
            });
        }

        public static string Percent(double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction)) return "0%";
            double rounded = Math.Round(fraction * 100, 1, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        public static string Compact(ActorState actor, int maxEntries = 4)
        {
            var entries = new List<BattleStatusEntry>();
            Build(actor, entries);
            var text = new StringBuilder();
            AppendCompact(entries, text, maxEntries);
            return text.ToString();
        }

        public static void AppendCompact(ActorState actor, StringBuilder text, int maxEntries = 4)
        {
            var entries = new List<BattleStatusEntry>();
            Build(actor, entries);
            AppendCompact(entries, text, maxEntries);
        }

        public static void AppendCompact(List<BattleStatusEntry> entries, StringBuilder text, int maxEntries)
        {
            if (text == null || entries == null || entries.Count == 0) return;
            int max = Math.Max(0, maxEntries);
            int shown = Math.Min(max, entries.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text.Append(" · ");
                var entry = entries[i];
                text.Append(entry.Name ?? "");
                if (!string.IsNullOrEmpty(entry.Value)) text.Append(entry.Value);
            }
            if (entries.Count > shown) text.Append(" +").Append(entries.Count - shown);
        }
    }
}
