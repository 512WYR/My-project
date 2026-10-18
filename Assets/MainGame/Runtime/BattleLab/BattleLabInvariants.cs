using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    // Read-only health checks over a live BattleEngine. Nothing here draws a random number or mutates the
    // run, so it is safe to call from OnGUI.
    public static class BattleLabInvariants
    {
        public static List<string> Check(BattleEngine battle)
        {
            var issues = new List<string>();
            if (battle == null) { issues.Add("没有战斗对象。"); return issues; }
            if (battle.Run == null) { issues.Add("BattleEngine.Run 为空。"); return issues; }
            if (battle.Rules == null) issues.Add("BattleEngine.Rules 为空。");
            if (battle.Run.Random == null) issues.Add("Run.Random 为空。");
            if (battle.Run.Heroes == null) issues.Add("Run.Heroes 为空。");
            if (battle.Enemies == null) issues.Add("Enemies 为空。");
            if (issues.Count > 0) return issues;

            var all = battle.All.ToList();
            CheckUniqueIds(all, issues);
            foreach (var actor in all) CheckActor(actor, battle, issues);

            CheckRanks(battle.Run.Heroes.Where(h => h != null && !h.Dead), "英雄", issues);
            CheckRanks(battle.Enemies.Where(e => e != null && !e.Dead), "敌人", issues);

            if (battle.Current != null && !all.Contains(battle.Current))
                issues.Add("Current 不在本场 Actor 列表中：" + battle.Current.Id);
            if (battle.Current != null && battle.Current.Dead)
                issues.Add("Current 已经死亡：" + battle.Current.Id);

            if (battle.Outcome == BattleOutcome.Active)
            {
                if (battle.Current == null) issues.Add("战斗仍在进行，但 Current 为空。");
                if (battle.Round < 1) issues.Add("战斗仍在进行，但 Round 为 " + battle.Round + "。");
                if (battle.Settlement != null) issues.Add("战斗仍在进行，但已经存在 Settlement。");
            }
            else
            {
                if (battle.Current != null) issues.Add("战斗已结束，但 Current 仍为 " + battle.Current.Id + "。");
                if (battle.Settlement == null) issues.Add("战斗已结束，但 Settlement 为空。");
                else if (battle.Settlement.Outcome != battle.Outcome)
                    issues.Add("Settlement.Outcome=" + battle.Settlement.Outcome + "，Outcome=" + battle.Outcome + "。");
            }

            if (battle.Events == null) issues.Add("Events 为空。");
            else if (battle.Events.Any(e => e == null || string.IsNullOrEmpty(e.Kind)))
                issues.Add("Events 中存在空事件或空 kind。");

            if (battle.TurnPreview.Any(a => a == null || a.Dead))
                issues.Add("TurnPreview 中包含空或已死亡的单位。");

            return issues;
        }

        private static void CheckUniqueIds(List<ActorState> all, List<string> issues)
        {
            var ids = new HashSet<string>();
            foreach (var actor in all)
            {
                if (actor == null) { issues.Add("Actor 列表中存在 null。"); continue; }
                if (string.IsNullOrEmpty(actor.Id)) { issues.Add("存在没有 Id 的 Actor。"); continue; }
                if (!ids.Add(actor.Id)) issues.Add("Actor Id 重复：" + actor.Id);
            }
        }

        private static void CheckActor(ActorState actor, BattleEngine battle, List<string> issues)
        {
            if (actor == null) return;
            string who = string.IsNullOrEmpty(actor.Id) ? "(无 Id)" : actor.Id;
            if (actor.Definition == null) { issues.Add(who + " 没有 Definition。"); return; }
            if (actor.Definition.MaxHp < 1) issues.Add(who + " 的 MaxHp 小于 1。");
            if (actor.Hp < 0) issues.Add(who + " 的 Hp 为负数：" + actor.Hp);
            if (actor.Hp > actor.Definition.MaxHp)
                issues.Add(who + " 的 Hp(" + actor.Hp + ") 超过 MaxHp(" + actor.Definition.MaxHp + ")。");
            if (actor.Sanity < 0 || actor.Sanity > 100)
                issues.Add(who + " 的理智越界：" + actor.Sanity.ToString("0.##"));
            if (double.IsNaN(actor.Sanity)) issues.Add(who + " 的理智是 NaN。");
            if (actor.Rank < 0 || actor.Rank > 3) issues.Add(who + " 的站位越界：" + actor.Rank);
            if (actor.Rank + actor.Width > 4)
                issues.Add(who + " 占用 " + actor.Width + " 格，从 " + actor.Rank + " 开始会越界。");
            if (actor.Definition.Width < 1) issues.Add(who + " 的 Width 小于 1。");
            if (actor.Uses != null && actor.Uses.Any(u => u != null && u.Left < 0))
                issues.Add(who + " 存在负数技能次数。");
            if (actor.Uses != null && actor.Uses.Any(u => u == null))
                issues.Add(who + " 的 Uses 中存在 null。");
            if (actor.EquippedAccessories != null)
            {
                if (actor.EquippedAccessories.Any(a => a == null))
                    issues.Add(who + " 的饰品列表中存在 null。");
                var ids = actor.EquippedAccessories.Where(a => a != null).Select(a => a.Id).ToList();
                if (ids.Count != ids.Distinct().Count())
                    issues.Add(who + " 重复装备了同一件饰品。");
            }
            if (actor.Limbs != null)
            {
                foreach (var limb in actor.Limbs)
                {
                    if (limb == null) { issues.Add(who + " 的部位列表中存在 null。"); continue; }
                    if (limb.Hp < 0) issues.Add(who + " 的" + LimbSystem.PartName(limb.Part) + " Hp 为负数。");
                    if (limb.Hp > limb.MaxHp)
                        issues.Add(who + " 的" + LimbSystem.PartName(limb.Part) + " Hp 超过 MaxHp。");
                    if (limb.MaxHp < 1) issues.Add(who + " 的" + LimbSystem.PartName(limb.Part) + " MaxHp 小于 1。");
                }
            }
            if (!Enum.IsDefined(typeof(SkillForm), actor.Form))
                issues.Add(who + " 的 Form 枚举值无效：" + (int)actor.Form);
            if (actor.ProtectorId != null && battle.Find(actor.ProtectorId) == null)
                issues.Add(who + " 的 ProtectorId 指向不存在的单位：" + actor.ProtectorId);
        }

        private static void CheckRanks(IEnumerable<ActorState> actors, string side, List<string> issues)
        {
            var occupied = new bool[4];
            foreach (var actor in actors)
            {
                if (actor == null) continue;
                for (int rank = actor.Rank; rank < actor.Rank + actor.Width; rank++)
                {
                    if (rank < 0 || rank >= 4) continue;
                    if (occupied[rank])
                        issues.Add(side + "存活单位在 " + (rank + 1) + " 号位重叠（" + actor.Name + "）。");
                    occupied[rank] = true;
                }
            }
        }

        // Calls every pure query the UI uses and verifies the random state, current actor, round and event
        // count are unchanged. Returns null when the contract holds, otherwise the first violation.
        public static string CheckQueryPurity(BattleEngine battle)
        {
            if (battle == null || battle.Run == null || battle.Run.Random == null) return "没有可检查的战斗。";
            uint state = battle.Run.Random.State;
            int events = battle.Events.Count;
            string current = battle.Current == null ? null : battle.Current.Id;
            int round = battle.Round;
            BattleOutcome outcome = battle.Outcome;
            int preview = battle.TurnPreview.Length;

            try
            {
                var all = battle.All.ToArray();
                foreach (var actor in all)
                {
                    if (actor == null) continue;
                    battle.FormProbabilities(actor);
                    battle.EffectiveAttack(actor);
                    battle.EffectiveDefense(actor);
                    battle.EffectiveSpeed(actor);
                    var skills = battle.SkillsFor(actor);
                    foreach (var skill in skills)
                    {
                        if (skill == null) continue;
                        battle.EffectiveAccuracy(actor, skill);
                        battle.SkillValue(actor, skill);
                        battle.CritValue(actor, skill);
                    }
                    if (!actor.Player) battle.Intent(actor);
                }
                for (int i = 0; i < battle.Enemies.Count; i++)
                    if (battle.Enemies[i] != null) battle.EnemyDamage(battle.Enemies[i]);

                var active = battle.Current;
                if (active != null)
                {
                    int count = battle.SkillsFor(active).Length;
                    for (int i = 0; i < count; i++)
                    {
                        battle.SkillDisabledReason(i);
                        battle.Targets(i).ToArray();
                    }
                    battle.SwapDisabledReason();
                    battle.SwapTargets().ToArray();
                }
                battle.Find("__missing__");
                AccessorySystem.Summary(active);
                LimbSystem.Summary(active);
            }
            catch (Exception e)
            {
                return "查询 API 抛异常：" + e.GetType().Name + " " + e.Message;
            }

            if (battle.Run.Random.State != state)
                return "查询改变了 RNG State：" + state.ToString("X8") + " → " + battle.Run.Random.State.ToString("X8");
            if (battle.Events.Count != events)
                return "查询改变了 Events 数量：" + events + " → " + battle.Events.Count;
            if (!string.Equals(current, battle.Current == null ? null : battle.Current.Id, StringComparison.Ordinal))
                return "查询改变了 Current。";
            if (battle.Round != round) return "查询改变了 Round：" + round + " → " + battle.Round;
            if (battle.Outcome != outcome) return "查询改变了 Outcome：" + outcome + " → " + battle.Outcome;
            if (battle.TurnPreview.Length != preview) return "查询改变了 TurnPreview。";
            return null;
        }

        // Compact one-line summary used by the status bar.
        public static string Summary(BattleEngine battle)
        {
            var issues = Check(battle);
            return issues.Count == 0 ? "不变量正常" : "不变量告警 " + issues.Count + " 条";
        }
    }
}
