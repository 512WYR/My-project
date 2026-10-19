using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public sealed partial class BattleLabApp
    {
        private static readonly int[] FormValues = { 0, 1, 2 };
        private static readonly string[] FormLabels = { "技术", "基础", "原始" };

        private void DrawBattlePage()
        {
            Ui.BattleScroll = GUILayout.BeginScrollView(Ui.BattleScroll, GUILayout.ExpandHeight(true));
            if (Session == null || Session.Battle == null)
            {
                BattleLabUi.BoxTitle("战斗");
                BattleLabUi.Label("尚未开始战斗。先在配置页设置编队，然后点“应用配置并开战”。");
                if (BattleLabUi.Button("应用配置并开战")) StartBattleNow();
                GUILayout.EndScrollView();
                return;
            }

            DrawBattleStatus(Session.Battle);
            DrawBattleDriver(Session.Battle);
            DrawPlayerActions(Session.Battle);
            DrawActorCards(Session.Battle);
            DrawActorEditor();
            GUILayout.EndScrollView();
        }

        private void DrawBattleStatus(BattleEngine battle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("战斗状态");
            GUILayout.BeginHorizontal();
            GUILayout.Label("场次", GUILayout.Width(34));
            GUILayout.Label(Session.Run == null ? "?" : Session.Run.Battles.ToString(), GUILayout.Width(36));
            GUILayout.Label("回合", GUILayout.Width(34));
            GUILayout.Label(battle.Round.ToString(), GUILayout.Width(36));
            GUILayout.Label("结果", GUILayout.Width(34));
            GUILayout.Label(battle.Outcome.ToString(), GUILayout.Width(66));
            GUILayout.Label("当前", GUILayout.Width(34));
            GUILayout.Label(battle.Current == null ? "无" : battle.Current.Name + "（" + (battle.Current.Player ? "我方" : "敌方") + "）",
                GUILayout.Width(180));
            GUILayout.Label("RNG", GUILayout.Width(30));
            GUILayout.Label(Session.RandomState.ToString("X8"), BattleLabUi.Body, GUILayout.Width(80));
            if (Session.RandomOverrideActive)
                GUILayout.Label("覆盖中，剩余 " + Session.RandomForcedDrawsRemaining + " 次", BattleLabUi.Error);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("行动队列", GUILayout.Width(60));
            var preview = battle.TurnPreview;
            GUILayout.Label(preview.Length == 0 ? "无" : string.Join(" → ", preview.Select(a => a.Name)), BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            if (battle.Settlement != null)
            {
                var s = battle.Settlement;
                GUILayout.Label("结算：" + s.Outcome + "，理智 " + s.Before.ToString("0.#") + " → " + s.After.ToString("0.#")
                    + "（应用变化 " + s.AppliedDelta.ToString("+0.#;-0.#;0") + "）", BattleLabUi.Body);
            }
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawBattleDriver(BattleEngine battle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("驱动");
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("执行一步")) StepOnce();
            if (BattleLabUi.Button("跑到玩家回合")) RunToPlayer();
            if (BattleLabUi.Button("跑完整场")) RunToEnd();
            if (BattleLabUi.Button("跳过当前行动", Session.Active)) SkipTurn();
            GUILayout.Space(10);
            BattleLabUi.Toggle("自动敌方", ref Config.AutoEnemy);
            GUILayout.Label("间隔", GUILayout.Width(32));
            Config.AutoEnemyInterval = BattleLabUi.FloatField("battle-auto-interval", Config.AutoEnemyInterval, GUILayout.Width(52));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("立即胜利", battle.Outcome == BattleOutcome.Active)) FinishBattle(BattleOutcome.Victory);
            if (BattleLabUi.Button("立即失败", battle.Outcome == BattleOutcome.Active)) FinishBattle(BattleOutcome.Defeat);
            if (BattleLabUi.Button("撤退", battle.Outcome == BattleOutcome.Active)) FinishBattle(BattleOutcome.Retreat);
            if (BattleLabUi.Button("重开本场（同 Run）")) StartBattleWithRunNow();
            if (BattleLabUi.Button("新 Run 再开")) StartBattleNow();
            GUILayout.FlexibleSpace();
            GUILayout.Label("快捷键：F5 开战 / F6 单步 / F7 跑到玩家回合 / F8 跑完整场 / F9 跳过 / Esc 清除选择",
                BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void FinishBattle(BattleOutcome outcome)
        {
            var settlement = Session.Finish(outcome);
            if (settlement != null) Note("战斗结算：" + outcome + "。");
        }

        private void DrawPlayerActions(BattleEngine battle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("行动");
            if (battle.Outcome != BattleOutcome.Active)
            {
                BattleLabUi.Label("战斗已结束，不能继续行动。");
                GUILayout.EndVertical();
                GUILayout.Space(6);
                return;
            }
            if (battle.Current == null || !battle.Current.Player)
            {
                BattleLabUi.Label("现在不是我方行动。用“执行一步”或“自动敌方”推进。");
                GUILayout.EndVertical();
                GUILayout.Space(6);
                return;
            }

            var current = battle.Current;
            var skills = battle.SkillsFor(current);
            BattleLabUi.SubTitle("技能（禁用原因会显示在按钮后）");
            BattleLabUi.Small("图例：●可选 ○不可；斜杠前为己方站位，斜杠后为目标位置（阵营见效果说明），1→4号位。");
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                if (skill == null) continue;
                string reason = battle.SkillDisabledReason(i);
                string uses = skill.Uses > 0 ? "（剩 " + current.UsesLeft(skill) + "/" + skill.Uses + "）" : "";
                GUILayout.BeginHorizontal();
                if (BattleLabUi.Button((i + 1) + ". " + skill.Name + uses, reason == null))
                {
                    Ui.SelectedSkillIndex = i;
                    var first = battle.Targets(i).FirstOrDefault();
                    Ui.SelectedTargetId = first == null ? null : first.Id;
                    Ui.ActiveSkillHint = reason ?? "";
                }
                if (reason != null) GUILayout.Label("× " + reason, BattleLabUi.Error);
                else
                {
                    GUILayout.Label("倍率 " + skill.Multiplier.ToString("0.##") + " · 命中 " + skill.Accuracy.ToString("0.##")
                        + " · 范围 " + skill.Area + (skill.Targeting == SkillTargeting.All ? " · 全体" : "")
                        + (skill.OreCost > 0
                            ? " · 耗 " + skill.OreCost + " 矿石（当前 "
                                + (battle.Run.Map == null ? 0 : battle.Run.Map.Supplies) + "）"
                            : ""), BattleLabUi.Hint);
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(skill.Description))
                    GUILayout.Label(skill.Description, BattleLabUi.Hint);
            }

            if (Ui.SelectedSkillIndex >= 0 && Ui.SelectedSkillIndex < skills.Length)
            {
                var skill = skills[Ui.SelectedSkillIndex];
                if (skill != null)
                {
                    GUILayout.Label("已选：" + skill.Name + " · 预估 " + battle.SkillValue(current, skill)
                        + " · 暴击 " + battle.CritValue(current, skill), BattleLabUi.Body);
                    var targets = battle.Targets(Ui.SelectedSkillIndex).ToList();
                    string disabled = battle.SkillDisabledReason(Ui.SelectedSkillIndex);
                    if (targets.Count == 0)
                    {
                        if (disabled != null) BattleLabUi.ErrorLabel(disabled);
                        else BattleLabUi.Small("该技能无需选择目标（或自动选择）。");
                    }
                    else
                    {
                        var labels = targets.Select(a => a.Name + " [" + a.Hp + "/" + a.MaxHp + "] " + (a.Player ? "我" : "敌")).ToArray();
                        int selected = targets.FindIndex(a => a.Id == Ui.SelectedTargetId);
                        int next = BattleLabUi.SelectionGrid(selected, labels, Mathf.Min(4, labels.Length));
                        if (next >= 0 && next < targets.Count) Ui.SelectedTargetId = targets[next].Id;
                    }
                    if (BattleLabUi.Button("执行技能", disabled == null && (targets.Count == 0 || !string.IsNullOrEmpty(Ui.SelectedTargetId))))
                    {
                        if (Session.ExecuteSkill(Ui.SelectedSkillIndex, Ui.SelectedTargetId, out string error))
                        {
                            Note("已执行 " + skill.Name + "。");
                            Ui.SelectedSkillIndex = -1;
                            Ui.SelectedTargetId = null;
                            Ui.SelectedActorId = Session.Current == null ? null : Session.Current.Id;
                        }
                        else Note(error, true);
                    }
                }
            }

            GUILayout.Space(6);
            BattleLabUi.SubTitle("换位");
            var swaps = battle.SwapTargets().ToList();
            string swapDisabled = battle.SwapDisabledReason();
            if (swapDisabled != null) BattleLabUi.Small("× " + swapDisabled);
            if (swaps.Count > 0)
            {
                var labels = swaps.Select(a => a.Name + "（" + (a.Rank + 1) + " 号位）").ToArray();
                int selected = swaps.FindIndex(a => a.Id == Ui.SwapTargetId);
                int next = BattleLabUi.SelectionGrid(selected, labels, Mathf.Min(4, labels.Length));
                if (next >= 0 && next < swaps.Count) Ui.SwapTargetId = swaps[next].Id;
                if (BattleLabUi.Button("执行换位", swapDisabled == null) && !string.IsNullOrEmpty(Ui.SwapTargetId))
                {
                    if (Session.ExecuteSwap(Ui.SwapTargetId, out string error)) Note("换位完成。");
                    else Note(error, true);
                }
            }

            GUILayout.Space(6);
            BattleLabUi.SubTitle("消耗品（消耗当前行动）");
            var consumables = Session.Run == null ? null : Session.Run.Consumables;
            if (consumables == null || consumables.Count == 0)
            {
                BattleLabUi.Small("库存为空。可在配置页给英雄添加消耗品。");
            }
            else
            {
                var livingHeroes = Session.Heroes.Where(a => a != null && !a.Dead).ToList();
                if (Ui.ConsumableTargetId == null && livingHeroes.Count > 0) Ui.ConsumableTargetId = current.Id;
                if (livingHeroes.Count > 0)
                {
                    var heroLabels = livingHeroes.Select(a => a.Name + " [" + a.Hp + "/" + a.MaxHp + " 理智 " + a.Sanity.ToString("0.#") + "]").ToArray();
                    int selected = livingHeroes.FindIndex(a => a.Id == Ui.ConsumableTargetId);
                    int next = BattleLabUi.SelectionGrid(selected, heroLabels, Mathf.Min(4, heroLabels.Length));
                    if (next >= 0 && next < livingHeroes.Count) Ui.ConsumableTargetId = livingHeroes[next].Id;
                }
                for (int i = 0; i < consumables.Count; i++)
                {
                    var item = consumables[i];
                    if (item == null) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("[" + i + "] " + item.Name + (item.Implemented ? "" : "（未实现，使用会被拒绝）"),
                        BattleLabUi.Body, GUILayout.Width(260));
                    if (BattleLabUi.Button("使用", livingHeroes.Count > 0))
                    {
                        if (Session.UseConsumable(i, Ui.ConsumableTargetId, out string note)) Note(note);
                        else Note(note, true);
                    }
                    GUILayout.Label(item.Description ?? "", BattleLabUi.Hint);
                    GUILayout.EndHorizontal();
                }
            }

            if (!string.IsNullOrEmpty(Ui.ActiveSkillHint))
                BattleLabUi.Small(Ui.ActiveSkillHint);
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawActorCards(BattleEngine battle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("场上角色");
            BattleLabUi.Small("点“查看”后在下方修改；我方与敌方分开列出。");
            DrawActorGroup(battle, true);
            DrawActorGroup(battle, false);
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawActorGroup(BattleEngine battle, bool player)
        {
            var actors = battle.All.Where(a => a != null && a.Player == player).ToArray();
            BattleLabUi.SubTitle(player ? "我方" : "敌方");
            if (actors.Length == 0)
            {
                BattleLabUi.Small("无。");
                return;
            }
            foreach (var actor in actors)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                if (BattleLabUi.Button("查看", true, GUILayout.Width(48))) Ui.SelectedActorId = actor.Id;
                GUILayout.Label(actor.Name, BattleLabUi.Body, GUILayout.Width(140));
                // Sanity and form are hero-only; enemies are always constructed with Sanity = 0 and never
                // change form, so showing them here only invites confusion.
                GUILayout.Label("R" + (actor.Rank + 1) + (actor.Width > 1 ? "-" + (actor.Rank + actor.Width) : "")
                    + " · HP " + actor.Hp + "/" + actor.MaxHp
                    + (actor.Dead ? " · 死亡" : actor.DeathDoor ? " · 死门" : "")
                    + (actor.Player ? " · 理智 " + actor.Sanity.ToString("0.#") + " · 形态 " + actor.Form : ""),
                    BattleLabUi.Body);
                if (actor.BleedTurns > 0) GUILayout.Label("流血 " + actor.BleedDamage + "×" + actor.BleedTurns, BattleLabUi.Hint);
                if (actor.Interrupted > 0) GUILayout.Label("打断 " + actor.Interrupted, BattleLabUi.Hint);
                if (actor.Imbalance > 0) GUILayout.Label("失衡 " + actor.Imbalance, BattleLabUi.Hint);
                if (actor.ImbalanceAutoHitUntilRound >= battle.Round)
                    GUILayout.Label("位移必中至 R" + actor.ImbalanceAutoHitUntilRound, BattleLabUi.Hint);
                if (actor.Defending) GUILayout.Label("防御中", BattleLabUi.Hint);
                if (actor.ProtectorId != null) GUILayout.Label("守护 " + actor.ProtectorId, BattleLabUi.Hint);
                if (actor.BlockCharges > 0) GUILayout.Label("格挡 " + actor.BlockCharges, BattleLabUi.Hint);
                if (actor.Counter) GUILayout.Label("反击", BattleLabUi.Hint);
                if (actor.Followup) GUILayout.Label("追击", BattleLabUi.Hint);
                if (actor.EquippedAccessories != null && actor.EquippedAccessories.Count > 0)
                    GUILayout.Label("饰品 " + string.Join("、", actor.EquippedAccessories.Where(x => x != null).Select(x => x.Name)), BattleLabUi.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                var definition = actor.Definition;
                GUILayout.BeginHorizontal();
                GUILayout.Label("有效：攻 " + battle.EffectiveAttack(actor).ToString("0.##")
                    + " · 防 " + battle.EffectiveDefense(actor).ToString("0.##")
                    + " · 命 " + battle.EffectiveAccuracy(actor, definition.SkillsFor(actor.Form).FirstOrDefault()).ToString("0.##")
                    + " · 速 " + battle.EffectiveSpeed(actor), BattleLabUi.Hint);
                if (!actor.Player) GUILayout.Label(" · 意图：" + battle.Intent(actor) + " · 预计伤害 " + battle.EnemyDamage(actor), BattleLabUi.Hint);
                GUILayout.EndHorizontal();
                if (actor.Player)
                {
                    var probs = battle.FormProbabilities(actor);
                    GUILayout.Label("形态概率：技术 " + (probs.Length > 0 ? probs[0].ToString("P0") : "-")
                        + " / 基础 " + (probs.Length > 1 ? probs[1].ToString("P0") : "-")
                        + " / 原始 " + (probs.Length > 2 ? probs[2].ToString("P0") : "-"), BattleLabUi.Hint);
                }
                GUILayout.Space(2);
            }
        }

        private void DrawActorEditor()
        {
            var actor = SelectedActor;
            if (actor == null) return;
            var definition = actor.Definition;

            BattleLabUi.BeginCard(BattleLabUi.NeutralTint);
            BattleLabUi.BoxTitle("修改：" + actor.Name + "（" + actor.Id + "）");
            BattleLabUi.Small("改动立即生效并使录制失效。战斗中直接换饰品不会补触发开战钩子。");

            // ---- 生命与理智 ----
            bool vitals = Ui.GroupOpen("edit-vitals", true);
            bool vitalsNext = BattleLabUi.Foldout(vitals, actor.Player ? "生命与理智" : "生命",
                "HP " + actor.Hp + "/" + definition.MaxHp
                + (actor.Player ? " · 理智 " + actor.Sanity.ToString("0.#") + " · 形态 " + actor.Form : ""));
            Ui.SetGroupOpen("edit-vitals", vitalsNext);
            if (vitalsNext)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                GUILayout.Label("HP", GUILayout.Width(26));
                int hp = BattleLabUi.IntField("edit-hp", actor.Hp, GUILayout.Width(62));
                if (hp != actor.Hp) { Session.SetHp(actor, hp, out string note); Note(note); }
                GUILayout.Label("/ " + definition.MaxHp, GUILayout.Width(60));
                GUILayout.Label("MaxHp", GUILayout.Width(46));
                int maxHp = BattleLabUi.IntField("edit-maxhp", definition.MaxHp, GUILayout.Width(62));
                if (maxHp != definition.MaxHp) { Session.SetMaxHp(actor, maxHp, out string note); Note(note); }
                if (actor.Player)
                {
                    GUILayout.Label("理智", GUILayout.Width(32));
                    double sanity = BattleLabUi.DoubleField("edit-sanity", actor.Sanity, GUILayout.Width(62));
                    if (Math.Abs(sanity - actor.Sanity) > 0.0001) { Session.SetSanity(actor, sanity, out string note); Note(note); }
                    if (BattleLabUi.MiniButton("理智 -5")) { Session.ChangeSanity(actor, -5, out string note); Note(note); }
                    if (BattleLabUi.MiniButton("理智 +5")) { Session.ChangeSanity(actor, 5, out string note); Note(note); }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (actor.Player)
                {
                    GUILayout.Label("形态", GUILayout.Width(32));
                    int form = BattleLabUi.ValueGrid((int)actor.Form, FormValues, FormLabels, 3);
                    if (form != (int)actor.Form) { Session.SetForm(actor, (SkillForm)form, out string note); Note(note); }
                }
                GUILayout.Label("站位", GUILayout.Width(32));
                int rank = BattleLabUi.IntField("edit-rank", actor.Rank + 1, GUILayout.Width(42));
                if (rank - 1 != actor.Rank) { Session.SetRank(actor, rank - 1, out string note); Note(note); }
                GUILayout.EndHorizontal();
                BattleLabUi.EndIndent();
            }

            // ---- 基础属性 ----
            bool stats = Ui.GroupOpen("edit-stats", false);
            bool statsNext = BattleLabUi.Foldout(stats, "基础属性",
                "攻 " + definition.Attack + " · 速 " + definition.MinSpeed + "–" + definition.MaxSpeed
                + " · 命 " + definition.Accuracy.ToString("0.##"));
            Ui.SetGroupOpen("edit-stats", statsNext);
            if (statsNext)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                int attack = BattleLabUi.IntField("edit-atk", definition.Attack, GUILayout.Width(54));
                if (attack != definition.Attack) { definition.Attack = attack; Session.Touch(actor.Name + " Attack → " + attack); }
                GUILayout.Label("Attack", GUILayout.Width(42));
                int speed = BattleLabUi.IntField("edit-speed", definition.Speed, GUILayout.Width(54));
                if (speed != definition.Speed) { definition.Speed = speed; Session.Touch(actor.Name + " Speed → " + speed); }
                GUILayout.Label("Speed", GUILayout.Width(40));
                double accuracy = BattleLabUi.DoubleField("edit-accuracy", definition.Accuracy, GUILayout.Width(54));
                if (Math.Abs(accuracy - definition.Accuracy) > 0.0001) { definition.Accuracy = accuracy; Session.Touch(actor.Name + " Accuracy → " + accuracy.ToString("0.###")); }
                GUILayout.Label("Accuracy", GUILayout.Width(58));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                double crit = BattleLabUi.DoubleField("edit-crit", definition.CritChance, GUILayout.Width(54));
                if (Math.Abs(crit - definition.CritChance) > 0.0001) { definition.CritChance = crit; Session.Touch(actor.Name + " Crit → " + crit.ToString("0.###")); }
                GUILayout.Label("Crit", GUILayout.Width(30));
                double dodge = BattleLabUi.DoubleField("edit-dodge", definition.Dodge, GUILayout.Width(54));
                if (Math.Abs(dodge - definition.Dodge) > 0.0001) { definition.Dodge = dodge; Session.Touch(actor.Name + " Dodge → " + dodge.ToString("0.###")); }
                GUILayout.Label("Dodge", GUILayout.Width(42));
                double defense = BattleLabUi.DoubleField("edit-defense", definition.Defense, GUILayout.Width(54));
                if (Math.Abs(defense - definition.Defense) > 0.0001) { definition.Defense = defense; Session.Touch(actor.Name + " Defense → " + defense.ToString("0.###")); }
                GUILayout.Label("Defense", GUILayout.Width(54));
                GUILayout.EndHorizontal();
                BattleLabUi.EndIndent();
            }

            // ---- 战斗状态 ----
            var statusTags = new List<string>();
            if (actor.BleedTurns > 0) statusTags.Add("流血");
            if (actor.Interrupted > 0) statusTags.Add("打断");
            if (actor.Imbalance > 0) statusTags.Add("失衡");
            if (Session.Battle != null && actor.ImbalanceAutoHitUntilRound >= Session.Battle.Round) statusTags.Add("位移必中");
            if (actor.Defending) statusTags.Add("防御");
            if (actor.BlockCharges > 0) statusTags.Add("格挡");
            if (actor.Counter) statusTags.Add("反击");
            if (actor.Followup) statusTags.Add("追击");
            bool status = Ui.GroupOpen("edit-status", false);
            bool statusNext = BattleLabUi.Foldout(status, "战斗状态与修正",
                statusTags.Count == 0 ? "无" : string.Join(" / ", statusTags));
            Ui.SetGroupOpen("edit-status", statusNext);
            if (statusNext)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                GUILayout.Label("流血伤害", GUILayout.Width(58));
                int bleed = BattleLabUi.IntField("edit-bleed", actor.BleedDamage, GUILayout.Width(44));
                if (bleed != actor.BleedDamage) { actor.BleedDamage = Math.Max(0, bleed); Session.Touch(actor.Name + " 流血伤害 → " + actor.BleedDamage); }
                GUILayout.Label("流血回合", GUILayout.Width(58));
                int bleedTurns = BattleLabUi.IntField("edit-bleedturns", actor.BleedTurns, GUILayout.Width(44));
                if (bleedTurns != actor.BleedTurns) { actor.BleedTurns = Math.Max(0, bleedTurns); Session.Touch(actor.Name + " 流血回合 → " + actor.BleedTurns); }
                GUILayout.Label("打断", GUILayout.Width(32));
                int interrupted = BattleLabUi.IntField("edit-interrupted", actor.Interrupted, GUILayout.Width(44));
                if (interrupted != actor.Interrupted) { actor.Interrupted = Math.Max(0, interrupted); Session.Touch(actor.Name + " 打断 → " + actor.Interrupted); }
                GUILayout.Label("失衡", GUILayout.Width(32));
                int imbalance = BattleLabUi.IntField("edit-imbalance", actor.Imbalance, GUILayout.Width(44));
                if (imbalance != actor.Imbalance) { actor.Imbalance = Math.Max(0, imbalance); Session.Touch(actor.Name + " 失衡 → " + actor.Imbalance); }
                GUILayout.Label("必中至", GUILayout.Width(32));
                int autoHitUntil = BattleLabUi.IntField("edit-auto-hit", actor.ImbalanceAutoHitUntilRound, GUILayout.Width(44));
                if (autoHitUntil != actor.ImbalanceAutoHitUntilRound)
                {
                    actor.ImbalanceAutoHitUntilRound = Math.Max(-1, autoHitUntil);
                    Session.Touch(actor.Name + " 位移必中至 → R" + actor.ImbalanceAutoHitUntilRound);
                }
                GUILayout.Label("格挡层数", GUILayout.Width(58));
                int block = BattleLabUi.IntField("edit-block", actor.BlockCharges, GUILayout.Width(44));
                if (block != actor.BlockCharges) { actor.BlockCharges = Math.Max(0, block); Session.Touch(actor.Name + " 格挡 → " + actor.BlockCharges); }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (BattleLabUi.Toggle("反击", ref actor.Counter)) Session.Touch(actor.Name + " 反击 → " + actor.Counter);
                if (BattleLabUi.Toggle("追击", ref actor.Followup)) Session.Touch(actor.Name + " 追击 → " + actor.Followup);
                if (BattleLabUi.Toggle("防御", ref actor.Defending)) Session.Touch(actor.Name + " 防御 → " + actor.Defending);
                GUILayout.Label("守护者Id", GUILayout.Width(58));
                string protector = BattleLabUi.TextField("edit-protector", actor.ProtectorId, GUILayout.Width(120));
                if ((protector ?? "") != (actor.ProtectorId ?? ""))
                {
                    actor.ProtectorId = string.IsNullOrEmpty(protector) ? null : protector;
                    Session.Touch(actor.Name + " 守护者 → " + (actor.ProtectorId ?? "无"));
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("防御buff", GUILayout.Width(56));
                double defenseBuff = BattleLabUi.DoubleField("edit-defensebuff", actor.DefenseBuff, GUILayout.Width(50));
                if (Math.Abs(defenseBuff - actor.DefenseBuff) > 0.0001) { actor.DefenseBuff = defenseBuff; Session.Touch(actor.Name + " 防御buff → " + defenseBuff.ToString("0.###")); }
                GUILayout.Label("命中buff", GUILayout.Width(56));
                double accuracyBuff = BattleLabUi.DoubleField("edit-accuracybuff", actor.AccuracyBuff, GUILayout.Width(50));
                if (Math.Abs(accuracyBuff - actor.AccuracyBuff) > 0.0001) { actor.AccuracyBuff = accuracyBuff; Session.Touch(actor.Name + " 命中buff → " + accuracyBuff.ToString("0.###")); }
                GUILayout.Label("易伤", GUILayout.Width(28));
                double vulnerable = BattleLabUi.DoubleField("edit-vulnerable", actor.SelfVulnerable, GUILayout.Width(50));
                if (Math.Abs(vulnerable - actor.SelfVulnerable) > 0.0001) { actor.SelfVulnerable = vulnerable; Session.Touch(actor.Name + " 易伤 → " + vulnerable.ToString("0.###")); }
                GUILayout.EndHorizontal();

                // Sanity locks only affect hero form selection; an enemy has neither.
                if (actor.Player)
                {
                    GUILayout.BeginHorizontal();
                    BattleLabUi.Toggle("理智锁定", ref actor.SanityLocked);
                    if (GUILayout.Button("刷新锁定", GUILayout.Width(70))) Session.Touch(actor.Name + " 理智锁定 → " + actor.SanityLocked);
                    BattleLabUi.Toggle("只走理智技能", ref actor.SanitySkillsOnly);
                    if (GUILayout.Button("应用理智技能标记", GUILayout.Width(110))) Session.Touch(actor.Name + " 只走理智技能 → " + actor.SanitySkillsOnly);
                    BattleLabUi.Toggle("理智锁定待生效", ref actor.SanityLockPending);
                    GUILayout.EndHorizontal();
                }
                BattleLabUi.EndIndent();
            }

            // ---- 快捷操作 ----
            bool quick = Ui.GroupOpen("edit-quick", false);
            bool quickNext = BattleLabUi.Foldout(quick, "快捷操作", "击杀 / 复活 / 回满 / 清状态 / 重置");
            Ui.SetGroupOpen("edit-quick", quickNext);
            if (quickNext)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                if (BattleLabUi.Button("击杀")) { Session.Kill(actor, out string note); Note(note); }
                if (BattleLabUi.Button("复活")) { Session.Revive(actor, out string note); Note(note); }
                if (BattleLabUi.Button("回满")) { Session.HealFull(actor, out string note); Note(note); }
                if (BattleLabUi.Button("清空战斗状态")) { Session.ClearStatuses(actor, out string note); Note(note); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (BattleLabUi.Button("重置技能次数")) { Session.ResetSkillUses(actor, out string note); Note(note); }
                if (BattleLabUi.Button("重置饰品战斗状态")) { Session.ResetAccessoryBattle(actor, out string note); Note(note); }
                // Monsters have no limb track, so the rebuild shortcut is a hero-only action.
                if (actor.Player && BattleLabUi.Button("按当前 MaxHp 重建部位")) { Session.RebuildLimbs(actor, out string note); Note(note); }
                GUILayout.EndHorizontal();
                BattleLabUi.EndIndent();
            }

            // ---- 部位 ----
            if (actor.Player)
            {
                bool limbs = Ui.GroupOpen("edit-limbs", false);
                bool limbsNext = BattleLabUi.Foldout(limbs, "部位与义肢", "断肢 / 修复 / 安装义肢");
                Ui.SetGroupOpen("edit-limbs", limbsNext);
                if (limbsNext)
                {
                    BattleLabUi.BeginIndent();
                    for (int part = 0; part < 4; part++)
                    {
                        var limb = LimbSystem.Get(actor, (LimbPart)part);
                        if (limb == null) continue;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(LimbSystem.PartName((LimbPart)part), GUILayout.Width(46));
                        int condition = BattleLabUi.ValueGrid((int)LimbSystem.ConditionOf(limb), LimbValues, LimbLabels, 3);
                        if (condition != (int)LimbSystem.ConditionOf(limb))
                        {
                            Session.SetLimb(actor, (LimbPart)part, (LabLimbState)condition, out string note);
                            Note(note);
                        }
                        GUILayout.Label("Hp " + limb.Hp + "/" + limb.MaxHp, GUILayout.Width(110));
                        GUILayout.Label("义肢 " + (string.IsNullOrEmpty(limb.Prosthetic) ? "无" : limb.Prosthetic), GUILayout.Width(150));
                        var prosthetics = LimbSystem.DefaultProsthetics.Where(p => p.Part == (LimbPart)part).ToArray();
                        if (prosthetics.Length > 0)
                        {
                            var labels = prosthetics.Select(p => p.Name).ToArray();
                            int selected = Ui.ProstheticSelection[part];
                            int chosen = BattleLabUi.SelectionGrid(selected, labels, labels.Length);
                            if (chosen >= 0 && chosen < prosthetics.Length) Ui.ProstheticSelection[part] = chosen;
                            if (BattleLabUi.Button("安装", LimbSystem.ConditionOf(limb) == LimbCondition.Severed))
                            {
                                Session.InstallProsthetic(actor, prosthetics[Ui.ProstheticSelection[part]].Id, out string note);
                                Note(note);
                            }
                        }
                        if (BattleLabUi.Button("修复", LimbSystem.CanRepair(actor, (LimbPart)part)))
                        {
                            Session.RepairLimb(actor, (LimbPart)part, out string note);
                            Note(note);
                        }
                        GUILayout.EndHorizontal();
                    }
                    BattleLabUi.EndIndent();
                }
            }

            // ---- 技能次数 ----
            var allSkills = new List<SkillSpec>();
            AddSkills(allSkills, definition.BasicSkills);
            AddSkills(allSkills, definition.TechnicalSkills);
            AddSkills(allSkills, definition.PrimalSkills);
            bool uses = Ui.GroupOpen("edit-uses", false);
            bool usesNext = BattleLabUi.Foldout(uses, "技能次数", allSkills.Count + " 个技能");
            Ui.SetGroupOpen("edit-uses", usesNext);
            if (usesNext)
            {
                BattleLabUi.BeginIndent();
                if (allSkills.Count == 0) BattleLabUi.Small("没有技能。");
                foreach (var skill in allSkills)
                {
                    if (skill == null) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(skill.Name + "（" + skill.Id + "）", BattleLabUi.Body, GUILayout.Width(220));
                    if (skill.Uses > 0)
                    {
                        int left = BattleLabUi.IntField("edit-use-" + actor.Id + "-" + skill.Id, actor.UsesLeft(skill), GUILayout.Width(50));
                        if (left != actor.UsesLeft(skill)) { Session.SetSkillUse(actor, skill, left, out string note); Note(note); }
                        GUILayout.Label("/ " + skill.Uses, BattleLabUi.Hint);
                    }
                    else GUILayout.Label("无限", BattleLabUi.Hint);
                    GUILayout.EndHorizontal();
                }
                BattleLabUi.EndIndent();
            }

            // ---- 饰品 ----
            int equippedCount = actor.EquippedAccessories == null ? 0 : actor.EquippedAccessories.Count(x => x != null);
            bool accessories = Ui.GroupOpen("edit-accessories", false);
            bool accessoriesNext = BattleLabUi.Foldout(accessories, "饰品", "已装备 " + equippedCount + " 件");
            Ui.SetGroupOpen("edit-accessories", accessoriesNext);
            if (accessoriesNext)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                if (actor.EquippedAccessories != null)
                {
                    foreach (var item in actor.EquippedAccessories.ToArray())
                    {
                        if (item == null) continue;
                        if (BattleLabUi.Button("卸下 " + item.Name, true, GUILayout.Width(150)))
                        {
                            Session.DetachAccessory(actor, item.Id, out string note);
                            Note(note);
                        }
                    }
                }
                GUILayout.EndHorizontal();
                if (Session.Run != null && Session.Run.AccessoryStock != null && Session.Run.AccessoryStock.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    foreach (var item in Session.Run.AccessoryStock.ToArray())
                    {
                        if (item == null) continue;
                        if (BattleLabUi.Button("装备 " + item.Name, true, GUILayout.Width(150)))
                        {
                            Session.AttachAccessory(actor, item.Id, out string note);
                            Note(note);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                BattleLabUi.Small("战斗中装备只立即生效实时读取的被动；跃动心脏、锈血勋章、成瘾物的开战钩子不会补触发。金丝苹果 +10 MaxHp 立即生效但不补 HP。");
                BattleLabUi.EndIndent();
            }

            if (GUILayout.Button("取消选中")) Ui.SelectedActorId = null;
            BattleLabUi.EndCard();
        }

        private static void AddSkills(List<SkillSpec> list, SkillSpec[] skills)
        {
            if (skills == null) return;
            foreach (var skill in skills)
                if (skill != null && !list.Any(x => x.Id == skill.Id)) list.Add(skill);
        }
    }
}
