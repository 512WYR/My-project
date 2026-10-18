using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Commands that only the authored debug console calls. They live in their own file so the debug
    // surface is easy to audit: every method here mutates a live run through the same rule systems the
    // game uses, then raises Refresh so the console and the HUD read the same state.
    public sealed partial class StageOneApp
    {
        // The console's hero pages need to point the shared inspector at a hero outside battle too,
        // which SelectActor refuses while a node page is up.
        public void DebugInspect(string heroId)
        {
            if (Run == null || string.IsNullOrEmpty(heroId)) return;
            if (Run.Heroes.All(h => h.Id != heroId)) return;
            InspectedId = heroId;
            Refresh();
        }

        // Used by the console for its own validation messages, so a rejected input still leaves a
        // readable line in the hint history instead of silently doing nothing.
        public void DebugNote(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Hint = message;
            Refresh();
        }

        public void DebugSetPlayerSanity(double value)
        {
            if (Run == null) return;
            Run.PlayerSanity = SanityProbability.Clamp(value, 0, 100);
            Hint = "玩家理智已设为 " + Run.PlayerSanity.ToString("0.0") + "。候选数量与事件分支会在下一次判定时使用它。";
            Refresh();
        }

        // Battle sanity goes through BattleEngine so the settlement feedback and the sanity-lock rule stay
        // intact; outside battle the hero value is written directly, the same field the next battle reads.
        public void DebugSetHeroSanity(string heroId, double value)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            value = SanityProbability.Clamp(value, 0, 100);
            if (!AwaitingNode && Battle != null && Battle.Outcome == BattleOutcome.Active)
            {
                if (hero.Dead) { Hint = hero.Name + " 已经阵亡，无法调整理智。"; Refresh(); return; }
                if (hero.SanityLocked) { Hint = "理智已被锁定，无法调整。"; Refresh(); return; }
                Battle.ChangeSanity(hero, value - hero.Sanity, "调试台调整");
                Hint = hero.Name + " 的理智已改变；本回合形态固定，下回合才重新抽取。";
                Refresh();
                return;
            }
            hero.Sanity = value;
            Hint = hero.Name + " 的理智已设为 " + value.ToString("0.0") + "。";
            Refresh();
        }

        // Direct form override. The battle engine rolls a hero's form at the start of their turn, so
        // this is mainly for inspecting a form's skill list on the spot; the next turn rolls again.
        public void DebugSetHeroForm(string heroId, SkillForm form)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            hero.Form = form;
            string name = form == SkillForm.Technical ? "技术"
                : form == SkillForm.Primal ? "原始" : "常态";
            Hint = hero.Name + " 的形态已设为「" + name
                + "」。战斗中形态会在该角色下个回合开始时按理智重新抽取。";
            Refresh();
        }

        public void DebugSetHeroHp(string heroId, int hp)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            int max = hero.Definition == null ? hero.Hp : hero.Definition.MaxHp;
            hero.Hp = Mathf.Clamp(hp, 0, Mathf.Max(1, max));
            if (hero.Hp > 0 && hero.Dead)
            {
                // Reviving a party member is deliberately not part of the normal rules, so the console
                // makes it explicit instead of silently clearing the death flag.
                Hint = hero.Name + " 的生命已设为 " + hero.Hp + "，但阵亡记录仍然保留。";
                Refresh();
                return;
            }
            Hint = hero.Name + " 的生命已设为 " + hero.Hp + " / " + max + "。";
            Refresh();
        }

        // The accessory manager in the console works on the live run rather than opening a modal, so it
        // does not need AccessoryEditorOpen. The system still enforces exclusivity and restrictions.
        public void DebugEquipAccessory(string heroId, int stockIndex)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId)
                ?? Run.Heroes.FirstOrDefault(h => !h.Dead) ?? Run.Heroes.FirstOrDefault();
            if (hero == null) return;
            string note;
            if (AccessorySystem.Equip(Run, hero, stockIndex, out note)) InspectedId = hero.Id;
            Hint = note;
            Refresh();
        }

        public void DebugUnequipAccessory(string heroId, string itemId)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            string note;
            if (AccessorySystem.Unequip(Run, hero, itemId, out note)) InspectedId = hero.Id;
            Hint = note;
            Refresh();
        }

        // Outside battle the item is used straight from the stock. In battle it goes through the engine,
        // and the enemy turns that follow are advanced by the same coroutine the item button uses.
        public void DebugUseConsumable(string heroId, int stockIndex)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId)
                ?? Run.Heroes.FirstOrDefault(h => !h.Dead) ?? Run.Heroes.FirstOrDefault();
            if (hero == null) return;
            if (!AwaitingNode && Battle != null && Battle.Outcome == BattleOutcome.Active)
            {
                int from = Battle.Events.Count;
                string battleNote;
                if (!Battle.UseConsumable(stockIndex, hero.Id, out battleNote))
                {
                    Hint = battleNote;
                    Refresh();
                    return;
                }
                InspectedId = hero.Id;
                Hint = battleNote;
                StartCoroutine(ContinueEnemies(from));
                return;
            }
            string note;
            if (ConsumableSystem.Use(Run, hero, stockIndex, false, out note)) InspectedId = hero.Id;
            Hint = note;
            Refresh();
        }

        // The older one-argument limb commands target whatever the shared inspector points at. The console
        // selects a hero by id, so these overloads hit exactly that hero even if the game view changed the
        // inspector in the meantime.
        public void DebugInjureLimb(string heroId, LimbPart part)
        {
            var hero = Run == null ? null : Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "受到一次部位伤害，现在是"
                + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part)) + "。";
            Refresh();
        }

        public void DebugSeverLimb(string heroId, LimbPart part)
        {
            var hero = Run == null ? null : Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经断肢，需要义肢才能恢复。";
            Refresh();
        }

        // The supply-room repair and fit commands are gated on Room == Supply. The console needs them
        // from any page, so these variants call the same LimbSystem rules without the room gate.
        public void DebugRepairLimb(string heroId, LimbPart part)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            if (!LimbSystem.Repair(hero, part))
            {
                Hint = LimbSystem.PartName(part) + "不是重伤，无法用修复处理；断肢需要安装义肢。";
                Refresh();
                return;
            }
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经修复。";
            Refresh();
        }

        public void DebugInstallProsthetic(string heroId, string itemId)
        {
            if (Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (hero == null || item == null)
            {
                Hint = "没有可安装的义肢。";
                Refresh();
                return;
            }
            if (!LimbSystem.Install(hero, item))
            {
                Hint = "只有断肢才能安装义肢。";
                Refresh();
                return;
            }
            Run.Prosthetics.Remove(item);
            Hint = hero.Name + " 装上了" + item.Name + "，" + LimbSystem.PartName(item.Part) + "恢复基础功能。";
            Refresh();
        }
    }
}
