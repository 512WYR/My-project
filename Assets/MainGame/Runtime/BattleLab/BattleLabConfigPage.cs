using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public sealed partial class BattleLabApp
    {
        private static readonly int[] LimbValues = { 0, 1, 2 };
        private static readonly string[] LimbLabels = { "完好", "重伤", "断肢" };
        private static readonly int[] EffectValues = { 0, 1, 2, 3, 4 };
        private static readonly string[] EffectLabels = { "攻击", "治疗", "保护", "增益", "理智" };
        private static readonly int[] SideValues = { 0, 1 };
        private static readonly string[] SideLabels = { "敌方", "我方" };
        private static readonly int[] TargetingValues = { 0, 1 };
        private static readonly string[] TargetingLabels = { "选择目标", "全体" };
        private static readonly int[] AreaValues = { 0, 1, 2, 3, 4 };
        private static readonly string[] AreaLabels = { "单体", "相邻", "溅射", "穿透", "全队" };
        private static readonly int[] PatternValues = { 0, 1, 2 };
        private static readonly string[] PatternLabels = { "斩击", "低语", "追猎" };
        private static readonly int[] BehaviorValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        private static readonly string[] BehaviorLabels =
        {
            "简单", "理智", "流血", "高伤", "肉盾", "回血", "复制Boss", "巨像Boss", "随机召唤", "蒸汽机", "操作者"
        };
        private static readonly int[] RiderValues = { 0, 1, 2 };
        private static readonly string[] RiderLabels = { "无", "伤害转治疗", "恢复理智" };
        private static readonly int[] RiderAreaValues = { 0, 1, 2 };
        private static readonly string[] RiderAreaLabels = { "自身", "自身相邻", "被选中" };

        private static readonly string[] ConfigSubTabs = { "全局", "队伍", "敌人", "预设与数据" };

        private void DrawConfigPage()
        {
            Ui.ConfigSubTab = GUILayout.Toolbar(Ui.ConfigSubTab, ConfigSubTabs);
            int heroCount = Config.Heroes == null ? 0 : Config.Heroes.Count(h => h != null && h.Enabled);
            int enemyCount = Config.Enemies == null ? 0 : Config.Enemies.Count(e => e != null && e.Enabled);
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("当前配置：" + heroCount + "/" + BattleLabConfig.SlotCount + " 英雄 · "
                + enemyCount + "/" + BattleLabConfig.SlotCount + " 敌人 · 种子 " + Config.SeedText
                + " · " + (Config.UseCatalogRules ? "Catalog 规则" : "手动规则"), BattleLabUi.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.Label("分类：全局 / 队伍 / 敌人 / 预设与数据", BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            int sub = Mathf.Clamp(Ui.ConfigSubTab, 0, ConfigSubTabs.Length - 1);
            if (Ui.ConfigSubScroll == null || Ui.ConfigSubScroll.Length != ConfigSubTabs.Length)
                Ui.ConfigSubScroll = new Vector2[ConfigSubTabs.Length];
            Ui.ConfigSubScroll[sub] = GUILayout.BeginScrollView(Ui.ConfigSubScroll[sub], GUILayout.ExpandHeight(true));
            switch (sub)
            {
                case 0: DrawGlobalConfig(); break;
                case 1: DrawHeroSlots(); break;
                case 2: DrawEnemySlots(); break;
                default:
                    DrawPresets();
                    DrawValidationAndJson();
                    break;
            }
            GUILayout.EndScrollView();
        }

        private void DrawGlobalConfig()
        {
            DrawGlobalBasics();
            DrawCombatRules();
            DrawLimbRules();
        }

        private void DrawGlobalBasics()
        {
            BattleLabUi.BeginCard(BattleLabUi.NeutralTint);
            BattleLabUi.BoxTitle("基础设置");
            BattleLabUi.Small("决定这一局如何开始。种子相同、操作相同，结果可复现。");

            BattleLabUi.SubTitle("随机与理智");
            GUILayout.BeginHorizontal();
            GUILayout.Label("种子", GUILayout.Width(48));
            Config.SeedText = BattleLabUi.TextField("cfg-seed", Config.SeedText, GUILayout.Width(120));
            GUILayout.Label("（十进制或 0x 十六进制）", BattleLabUi.Hint);
            GUILayout.FlexibleSpace();
            GUILayout.Label("玩家共享理智", GUILayout.Width(74));
            Config.PlayerSanity = BattleLabUi.DoubleField("cfg-sanity", Config.PlayerSanity, GUILayout.Width(64));
            GUILayout.EndHorizontal();

            BattleLabUi.SubTitle("开局与推进");
            GUILayout.BeginHorizontal();
            BattleLabUi.Toggle("满血开局", ref Config.FullHealOnStart);
            GUILayout.Space(12);
            BattleLabUi.Toggle("自动敌方", ref Config.AutoEnemy);
            GUILayout.Label("间隔（秒）", GUILayout.Width(64));
            Config.AutoEnemyInterval = BattleLabUi.FloatField("cfg-auto-interval", Config.AutoEnemyInterval, GUILayout.Width(56));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            BattleLabUi.EndCard();
            GUILayout.Space(6);
        }

        private void DrawCombatRules()
        {
            BattleLabUi.BeginCard(BattleLabUi.NeutralTint);
            BattleLabUi.BoxTitle("战斗规则");
            GUILayout.BeginHorizontal();
            BattleLabUi.Toggle("使用 Catalog 的 CombatRules", ref Config.UseCatalogRules);
            if (BattleLabUi.Button("从 Catalog 复制到手动规则", Catalog != null))
            {
                Config.Rules = BattleLabFactory.CloneRules(Catalog.Rules());
                Note("已把 Catalog 规则复制到手动规则。");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            BattleLabUi.Small(Config.UseCatalogRules
                ? "当前使用 Catalog 规则；下面的手动数值只作参考，不会写入 Run。"
                : "当前使用手动规则；下面的数值会写入 Run。");

            bool open = Ui.GroupOpen("global-rules", false);
            bool next = BattleLabUi.Foldout(open, "展开规则细节", Config.UseCatalogRules ? "只读参考" : "手动规则生效");
            Ui.SetGroupOpen("global-rules", next);
            if (next)
            {
                var rules = Config.Rules;
                if (rules == null) { Config.Rules = rules = new CombatRules(); }
                BattleLabUi.BeginIndent();
                BattleLabUi.SubTitle("概率与伤害");
                GUILayout.BeginHorizontal();
                GUILayout.Label("概率强度", GUILayout.Width(70)); rules.ProbabilityStrength = BattleLabUi.DoubleField("cfg-rule-prob", rules.ProbabilityStrength, GUILayout.Width(56));
                GUILayout.Label("反馈系数", GUILayout.Width(70)); rules.FeedbackFactor = BattleLabUi.DoubleField("cfg-rule-feedback", rules.FeedbackFactor, GUILayout.Width(56));
                GUILayout.Label("死亡概率", GUILayout.Width(70)); rules.DeathChance = BattleLabUi.DoubleField("cfg-rule-death", rules.DeathChance, GUILayout.Width(56));
                GUILayout.Label("暴击倍率", GUILayout.Width(70)); rules.CritMultiplier = BattleLabUi.DoubleField("cfg-rule-crit", rules.CritMultiplier, GUILayout.Width(56));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("理智与命中");
                GUILayout.BeginHorizontal();
                GUILayout.Label("理智步长", GUILayout.Width(70)); rules.MindStep = BattleLabUi.IntField("cfg-rule-mindstep", rules.MindStep, GUILayout.Width(56));
                GUILayout.Label("技能命中基准", GUILayout.Width(90)); rules.SkillAccuracyBaseline = BattleLabUi.DoubleField("cfg-rule-accbase", rules.SkillAccuracyBaseline, GUILayout.Width(56));
                GUILayout.Label("失衡免疫回合", GUILayout.Width(90)); rules.ImbalanceImmuneRounds = BattleLabUi.IntField("cfg-rule-immune", rules.ImbalanceImmuneRounds, GUILayout.Width(56));
                GUILayout.Label("失衡必中回合", GUILayout.Width(90)); rules.ImbalanceAutoHitRounds = BattleLabUi.IntField("cfg-rule-auto-hit", rules.ImbalanceAutoHitRounds, GUILayout.Width(56));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("追击与反击");
                GUILayout.BeginHorizontal();
                GUILayout.Label("追击比例", GUILayout.Width(70)); rules.FollowupRatio = BattleLabUi.DoubleField("cfg-rule-followup", rules.FollowupRatio, GUILayout.Width(56));
                GUILayout.Label("反击比例", GUILayout.Width(70)); rules.CounterRatio = BattleLabUi.DoubleField("cfg-rule-counter", rules.CounterRatio, GUILayout.Width(56));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("形态权重");
                GUILayout.BeginHorizontal();
                GUILayout.Label("技术", GUILayout.Width(70)); rules.TechnicalWeight = BattleLabUi.DoubleField("cfg-rule-tw", rules.TechnicalWeight, GUILayout.Width(56));
                GUILayout.Label("基础", GUILayout.Width(70)); rules.BasicWeight = BattleLabUi.DoubleField("cfg-rule-bw", rules.BasicWeight, GUILayout.Width(56));
                GUILayout.Label("原始", GUILayout.Width(70)); rules.PrimalWeight = BattleLabUi.DoubleField("cfg-rule-pw", rules.PrimalWeight, GUILayout.Width(56));
                GUILayout.EndHorizontal();
                BattleLabUi.EndIndent();
            }
            BattleLabUi.EndCard();
            GUILayout.Space(6);
        }

        private void DrawLimbRules()
        {
            var rules = Config.Rules;
            if (rules == null) { Config.Rules = rules = new CombatRules(); }
            var limbs = rules.Limbs;
            if (limbs == null) { rules.Limbs = limbs = LimbSystem.Resolve(null); }
            BattleLabUi.BeginCard(BattleLabUi.NeutralTint);
            BattleLabUi.BoxTitle("部位规则");
            GUILayout.BeginHorizontal();
            GUILayout.Label("部位命中比例", GUILayout.Width(90));
            limbs.HitRatio = BattleLabUi.DoubleField("cfg-rule-limbratio", limbs.HitRatio, GUILayout.Width(56));
            GUILayout.Label("（1 击 = 比例；默认 0.5 两击断肢）", BattleLabUi.Hint);
            GUILayout.EndHorizontal();
            bool open = Ui.GroupOpen("global-limbs", false);
            bool next = BattleLabUi.Foldout(open, "展开各部位数值", limbs.Parts == null ? "无" : limbs.Parts.Length + " 个部位");
            Ui.SetGroupOpen("global-limbs", next);
            if (next && limbs.Parts != null)
            {
                BattleLabUi.BeginIndent();
                GUILayout.BeginHorizontal();
                GUILayout.Label("部位", BattleLabUi.SubHeader, GUILayout.Width(50));
                GUILayout.Label("Hp比例", BattleLabUi.SubHeader, GUILayout.Width(56));
                GUILayout.Label("重伤惩罚", BattleLabUi.SubHeader, GUILayout.Width(70));
                GUILayout.Label("断肢惩罚", BattleLabUi.SubHeader, GUILayout.Width(70));
                GUILayout.EndHorizontal();
                for (int i = 0; i < limbs.Parts.Length; i++)
                {
                    var part = limbs.Parts[i];
                    if (part == null) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(LimbSystem.PartName(part.Part), GUILayout.Width(50));
                    part.HpRatio = BattleLabUi.DoubleField("cfg-limb-hp-" + i, part.HpRatio, GUILayout.Width(56));
                    part.InjuredPenalty = BattleLabUi.DoubleField("cfg-limb-inj-" + i, part.InjuredPenalty, GUILayout.Width(70));
                    part.SeveredPenalty = BattleLabUi.DoubleField("cfg-limb-sev-" + i, part.SeveredPenalty, GUILayout.Width(70));
                    GUILayout.EndHorizontal();
                }
                BattleLabUi.EndIndent();
            }
            BattleLabUi.EndCard();
            GUILayout.Space(6);
        }

        private void DrawHeroSlots()
        {
            BattleLabUi.BoxTitle("队伍配置");
            BattleLabUi.Small("启用顺序决定默认 1～4 号位；站位 -1 = 自动。点槽标题展开或收起，组标题可继续折叠。");
            GUILayout.Space(2);
            var sources = Catalog != null && Catalog.Heroes != null
                ? Catalog.Heroes.Where(h => h).ToList() : new List<CharacterDefinition>();
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = Config.Heroes[i];
                string sourceName = string.IsNullOrEmpty(slot.Spec.Name) ? "（空槽）" : slot.Spec.Name;
                string summary = slot.Enabled
                    ? "HP " + slot.Spec.MaxHp + " · 攻 " + slot.Spec.Attack + " · 速 " + slot.Spec.MinSpeed + "–" + slot.Spec.MaxSpeed
                      + " · 站位 " + (slot.Rank < 0 ? "自动" : (slot.Rank + 1).ToString())
                      + " · 理智 " + slot.Sanity.ToString("0.#")
                    : "未启用";
                string cardKey = "hero-card-" + i;
                bool open = Ui.GroupOpen(cardKey, i == 0);

                BattleLabUi.BeginCard(BattleLabUi.HeroTint);
                GUILayout.BeginHorizontal();
                slot.Enabled = GUILayout.Toggle(slot.Enabled, "启用", GUILayout.Width(52));
                bool next = BattleLabUi.Foldout(open, "槽 " + (i + 1) + " · " + sourceName, summary);
                Ui.SetGroupOpen(cardKey, next);
                GUILayout.FlexibleSpace();
                if (BattleLabUi.MiniButton("清空槽")) slot.Enabled = false;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("来源", GUILayout.Width(34));
                if (sources.Count == 0)
                {
                    GUILayout.Label("Catalog 没有英雄", BattleLabUi.Hint);
                }
                else
                {
                    var names = sources.Select(h => string.IsNullOrEmpty(h.DisplayName) ? h.Id : h.DisplayName).ToArray();
                    int selected = sources.FindIndex(h => h.Id == slot.SourceId);
                    int chosen = GUILayout.SelectionGrid(selected, names, Mathf.Min(4, Mathf.Max(1, names.Length)));
                    if (chosen >= 0 && chosen < sources.Count && chosen != selected)
                    {
                        BattleLabFactory.ApplyHeroSource(slot, sources[chosen]);
                        Note("槽 " + (i + 1) + " 已切换为 " + sources[chosen].DisplayName + "。");
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (!slot.Enabled || !next)
                {
                    BattleLabUi.EndCard();
                    GUILayout.Space(4);
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Id", GUILayout.Width(24));
                GUILayout.Label(slot.Spec.Id ?? "", BattleLabUi.Body, GUILayout.Width(110));
                GUILayout.Label("站位", GUILayout.Width(34));
                slot.Rank = BattleLabUi.IntField("cfg-h-rank-" + i, slot.Rank, GUILayout.Width(46));
                GUILayout.Label("（-1 = 自动）", BattleLabUi.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                DrawHeroStats(slot, i);
                DrawHeroSanity(slot, i);
                DrawHeroLimbs(slot, i);
                DrawHeroEquipment(slot, i);
                DrawHeroSkills(slot, i);

                BattleLabUi.EndCard();
                GUILayout.Space(4);
            }
        }

        private void DrawHeroStats(BattleLabHeroConfig slot, int i)
        {
            bool open = Ui.GroupOpen("hero-stats-" + i, true);
            bool next = BattleLabUi.Foldout(open, "基础属性");
            Ui.SetGroupOpen("hero-stats-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            GUILayout.BeginHorizontal();
            GUILayout.Label("MaxHp", GUILayout.Width(48)); slot.Spec.MaxHp = BattleLabUi.IntField("cfg-h-hp-" + i, slot.Spec.MaxHp, GUILayout.Width(60));
            GUILayout.Label("Attack", GUILayout.Width(48)); slot.Spec.Attack = BattleLabUi.IntField("cfg-h-atk-" + i, slot.Spec.Attack, GUILayout.Width(56));
            GUILayout.Label("Speed", GUILayout.Width(44)); slot.Spec.Speed = BattleLabUi.IntField("cfg-h-spd-" + i, slot.Spec.Speed, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Accuracy", GUILayout.Width(60)); slot.Spec.Accuracy = BattleLabUi.DoubleField("cfg-h-acc-" + i, slot.Spec.Accuracy, GUILayout.Width(56));
            GUILayout.Label("Crit", GUILayout.Width(34)); slot.Spec.CritChance = BattleLabUi.DoubleField("cfg-h-crit-" + i, slot.Spec.CritChance, GUILayout.Width(56));
            GUILayout.Label("Dodge", GUILayout.Width(44)); slot.Spec.Dodge = BattleLabUi.DoubleField("cfg-h-dodge-" + i, slot.Spec.Dodge, GUILayout.Width(56));
            GUILayout.Label("Defense", GUILayout.Width(56)); slot.Spec.Defense = BattleLabUi.DoubleField("cfg-h-def-" + i, slot.Spec.Defense, GUILayout.Width(56));
            GUILayout.EndHorizontal();
            BattleLabUi.EndIndent();
        }

        private void DrawHeroSanity(BattleLabHeroConfig slot, int i)
        {
            bool open = Ui.GroupOpen("hero-sanity-" + i, true);
            bool next = BattleLabUi.Foldout(open, "理智");
            Ui.SetGroupOpen("hero-sanity-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            GUILayout.BeginHorizontal();
            GUILayout.Label("理智", GUILayout.Width(34)); slot.Sanity = BattleLabUi.DoubleField("cfg-h-san-" + i, slot.Sanity, GUILayout.Width(56));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            BattleLabUi.Toggle("只走理智技能列表", ref slot.SanitySkillsOnly);
            GUILayout.Space(8);
            BattleLabUi.Toggle("理智锁定待生效", ref slot.SanityLockPending);
            GUILayout.EndHorizontal();
            BattleLabUi.EndIndent();
        }

        private void DrawHeroLimbs(BattleLabHeroConfig slot, int i)
        {
            bool open = Ui.GroupOpen("hero-limbs-" + i, false);
            bool next = BattleLabUi.Foldout(open, "部位与义肢", "义肢可装在任意未装义体的部位");
            Ui.SetGroupOpen("hero-limbs-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            for (int part = 0; part < 4; part++)
            {
                // Only pieces for this limb are offered, so a slot can never point at a wrong-part item.
                var options = LimbSystem.DefaultProsthetics
                    .Where(p => p != null && p.Part == (LimbPart)part).ToArray();
                var prostheticLabels = new[] { "无" }.Concat(options.Select(p => p.Name)).ToArray();
                var prostheticIds = new[] { "" }.Concat(options.Select(p => p.Id)).ToArray();
                GUILayout.BeginHorizontal();
                GUILayout.Label(LimbSystem.PartName((LimbPart)part), GUILayout.Width(46));
                slot.Limbs[part] = (LabLimbState)BattleLabUi.ValueGrid((int)slot.Limbs[part], LimbValues, LimbLabels, 3);
                GUILayout.Label("义肢", GUILayout.Width(32));
                int selectedProsthetic = Math.Max(0, Array.IndexOf(prostheticIds, slot.ProstheticIds[part] ?? ""));
                int chosen = GUILayout.SelectionGrid(selectedProsthetic, prostheticLabels,
                    Math.Min(4, prostheticLabels.Length));
                if (chosen >= 0 && chosen < prostheticIds.Length)
                    slot.ProstheticIds[part] = prostheticIds[chosen];
                GUILayout.EndHorizontal();
            }
            BattleLabUi.Small("已装义体的部位不能再装；专属义体只能由对应角色装备。");
            BattleLabUi.EndIndent();
        }

        private void DrawHeroEquipment(BattleLabHeroConfig slot, int i)
        {
            if (slot.AccessoryIds == null) slot.AccessoryIds = new List<string>();
            bool open = Ui.GroupOpen("hero-equip-" + i, false);
            bool next = BattleLabUi.Foldout(open, "饰品与消耗品",
                "饰品 " + slot.AccessoryIds.Count + " 件 · 消耗品 " + (slot.ConsumableCounts == null ? 0 : slot.ConsumableCounts.Sum()));
            Ui.SetGroupOpen("hero-equip-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            BattleLabUi.SubTitle("饰品");
            for (int a = 0; a < AccessorySystem.Defaults.Length; a++)
            {
                var item = AccessorySystem.Defaults[a];
                if (item == null) continue;
                bool has = slot.AccessoryIds.Contains(item.Id);
                GUILayout.BeginHorizontal();
                bool chosen = GUILayout.Toggle(has, new GUIContent(item.Name, item.Description ?? ""), GUILayout.Width(150));
                if (chosen != has)
                {
                    if (chosen) slot.AccessoryIds.Add(item.Id);
                    else slot.AccessoryIds.Remove(item.Id);
                }
                GUILayout.Label(AccessorySystem.RestrictionText(item), BattleLabUi.Hint);
                GUILayout.EndHorizontal();
            }
            BattleLabUi.Toggle("强制装备（忽略角色限制与装备校验）", ref slot.ForceAccessories);
            BattleLabUi.Small("饰品加成在战斗开始时应用；金丝苹果 +10 MaxHp，不补 HP。悬停饰品名可看说明。");

            BattleLabUi.SubTitle("消耗品数量");
            if (slot.ConsumableCounts == null || slot.ConsumableCounts.Length != ConsumableSystem.Defaults.Length)
                slot.ConsumableCounts = new int[ConsumableSystem.Defaults.Length];
            GUILayout.BeginHorizontal();
            for (int c = 0; c < ConsumableSystem.Defaults.Length; c++)
            {
                var item = ConsumableSystem.Defaults[c];
                GUILayout.Label(item.Name, GUILayout.Width(90));
                slot.ConsumableCounts[c] = Mathf.Clamp(
                    BattleLabUi.IntField("cfg-h-con-" + i + "-" + c, slot.ConsumableCounts[c], GUILayout.Width(42)), 0, 99);
                GUILayout.Space(6);
            }
            GUILayout.EndHorizontal();
            BattleLabUi.EndIndent();
        }

        private void DrawHeroSkills(BattleLabHeroConfig slot, int i)
        {
            bool open = Ui.GroupOpen("hero-skills-" + i, false);
            int count = (slot.Spec.BasicSkills == null ? 0 : slot.Spec.BasicSkills.Length)
                + (slot.Spec.PrimalSkills == null ? 0 : slot.Spec.PrimalSkills.Length)
                + (slot.Spec.TechnicalSkills == null ? 0 : slot.Spec.TechnicalSkills.Length);
            bool next = BattleLabUi.Foldout(open, "技能列表", count + " 个技能 · 含基础 / 低理智 / 高理智");
            Ui.SetGroupOpen("hero-skills-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            DrawSkillEditor(slot.Spec, "cfg-h-" + i);
            BattleLabUi.EndIndent();
        }

        private void DrawEnemySlots()
        {
            BattleLabUi.BoxTitle("敌人配置");
            BattleLabUi.Small("StartRank -1 = 用槽位；多站位单位需要连续空位。点槽标题展开或收起。");
            GUILayout.Space(2);
            var sources = Catalog == null ? new List<CharacterDefinition>() : BattleLabFactory.EnemySources(Catalog);
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = Config.Enemies[i];
                string sourceName = string.IsNullOrEmpty(slot.Spec.Name) ? "（空槽）" : slot.Spec.Name;
                string summary = slot.Enabled
                    ? "HP " + slot.Spec.MaxHp + " · 攻 " + slot.Spec.Attack + " · 速 " + slot.Spec.MinSpeed + "–" + slot.Spec.MaxSpeed
                      + " · 站位 " + (slot.StartRank < 0 ? "槽位" : (slot.StartRank + 1).ToString())
                      + (slot.Spec.RankWidth > 1 ? " · 宽 " + slot.Spec.RankWidth : "")
                      + " · " + slot.Spec.Behavior
                    : "未启用";
                string cardKey = "enemy-card-" + i;
                bool open = Ui.GroupOpen(cardKey, i == 0);

                BattleLabUi.BeginCard(BattleLabUi.EnemyTint);
                GUILayout.BeginHorizontal();
                slot.Enabled = GUILayout.Toggle(slot.Enabled, "启用", GUILayout.Width(52));
                bool next = BattleLabUi.Foldout(open, "槽 " + (i + 1) + " · " + sourceName, summary);
                Ui.SetGroupOpen(cardKey, next);
                GUILayout.FlexibleSpace();
                if (BattleLabUi.MiniButton("木桩"))
                {
                    slot.Enabled = true;
                    slot.SourceId = "lab_dummy";
                    slot.Spec = BattleLabFactory.DummySpec();
                    slot.StartRank = i;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("来源", GUILayout.Width(34));
                if (sources.Count == 0)
                {
                    GUILayout.Label("Catalog 没有敌人", BattleLabUi.Hint);
                }
                else
                {
                    var names = sources.Select(h => string.IsNullOrEmpty(h.DisplayName) ? h.Id : h.DisplayName).ToArray();
                    int selected = sources.FindIndex(h => h.Id == slot.SourceId);
                    int chosen = GUILayout.SelectionGrid(selected, names, 4);
                    if (chosen >= 0 && chosen < sources.Count && chosen != selected)
                    {
                        BattleLabFactory.ApplyEnemySource(slot, sources[chosen]);
                        slot.StartRank = i;
                        Note("敌人槽 " + (i + 1) + " 已切换为 " + sources[chosen].DisplayName + "。");
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (!slot.Enabled || !next)
                {
                    BattleLabUi.EndCard();
                    GUILayout.Space(4);
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Id", GUILayout.Width(24));
                GUILayout.Label(slot.Spec.Id ?? "", BattleLabUi.Body, GUILayout.Width(110));
                GUILayout.Label("StartRank", GUILayout.Width(66));
                slot.StartRank = BattleLabUi.IntField("cfg-e-rank-" + i, slot.StartRank, GUILayout.Width(46));
                GUILayout.Label("（-1 = 槽位）", BattleLabUi.Hint);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (slot.Spec.Behavior == EnemyBehavior.RandomSummoner)
                    GUILayout.Label("随机召唤者会在开战时被 SummonPool 中的随机普通怪物替换；直接选择它看到的是替换后的单位。", BattleLabUi.Hint);

                DrawEnemyStats(slot, i);
                DrawEnemyBehavior(slot, i);
                DrawEnemySkills(slot, i);

                BattleLabUi.EndCard();
                GUILayout.Space(4);
            }
        }

        private void DrawEnemyStats(BattleLabEnemyConfig slot, int i)
        {
            bool open = Ui.GroupOpen("enemy-stats-" + i, true);
            bool next = BattleLabUi.Foldout(open, "基础属性");
            Ui.SetGroupOpen("enemy-stats-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            GUILayout.BeginHorizontal();
            GUILayout.Label("MaxHp", GUILayout.Width(48)); slot.Spec.MaxHp = BattleLabUi.IntField("cfg-e-hp-" + i, slot.Spec.MaxHp, GUILayout.Width(62));
            GUILayout.Label("Attack", GUILayout.Width(48)); slot.Spec.Attack = BattleLabUi.IntField("cfg-e-atk-" + i, slot.Spec.Attack, GUILayout.Width(56));
            GUILayout.Label("Speed", GUILayout.Width(44)); slot.Spec.Speed = BattleLabUi.IntField("cfg-e-spd-" + i, slot.Spec.Speed, GUILayout.Width(50));
            GUILayout.Label("RankWidth", GUILayout.Width(64)); slot.Spec.RankWidth = BattleLabUi.IntField("cfg-e-width-" + i, slot.Spec.RankWidth, GUILayout.Width(46));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Accuracy", GUILayout.Width(60)); slot.Spec.Accuracy = BattleLabUi.DoubleField("cfg-e-acc-" + i, slot.Spec.Accuracy, GUILayout.Width(56));
            GUILayout.Label("Crit", GUILayout.Width(34)); slot.Spec.CritChance = BattleLabUi.DoubleField("cfg-e-crit-" + i, slot.Spec.CritChance, GUILayout.Width(56));
            GUILayout.Label("Dodge", GUILayout.Width(44)); slot.Spec.Dodge = BattleLabUi.DoubleField("cfg-e-dodge-" + i, slot.Spec.Dodge, GUILayout.Width(56));
            GUILayout.Label("Defense", GUILayout.Width(56)); slot.Spec.Defense = BattleLabUi.DoubleField("cfg-e-def-" + i, slot.Spec.Defense, GUILayout.Width(56));
            GUILayout.EndHorizontal();
            BattleLabUi.EndIndent();
        }

        private void DrawEnemyBehavior(BattleLabEnemyConfig slot, int i)
        {
            bool open = Ui.GroupOpen("enemy-behavior-" + i, false);
            bool next = BattleLabUi.Foldout(open, "行为与阶段", slot.Spec.Behavior + " / " + slot.Spec.Pattern);
            Ui.SetGroupOpen("enemy-behavior-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            BattleLabUi.SubTitle("行为模式");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Behavior", GUILayout.Width(64));
            slot.Spec.Behavior = (EnemyBehavior)BattleLabUi.ValueGrid((int)slot.Spec.Behavior, BehaviorValues, BehaviorLabels, 4);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pattern", GUILayout.Width(64));
            slot.Spec.Pattern = (EnemyPattern)BattleLabUi.ValueGrid((int)slot.Spec.Pattern, PatternValues, PatternLabels, 3);
            GUILayout.EndHorizontal();

            BattleLabUi.SubTitle("行动节奏");
            GUILayout.BeginHorizontal();
            GUILayout.Label("ActionInterval", GUILayout.Width(90)); slot.Spec.ActionInterval = BattleLabUi.IntField("cfg-e-interval-" + i, slot.Spec.ActionInterval, GUILayout.Width(48));
            GUILayout.Label("ActionPhase", GUILayout.Width(80)); slot.Spec.ActionPhase = BattleLabUi.IntField("cfg-e-phase-" + i, slot.Spec.ActionPhase, GUILayout.Width(48));
            BattleLabUi.Toggle("Immobile", ref slot.Spec.Immobile);
            GUILayout.EndHorizontal();

            BattleLabUi.SubTitle("防御与特殊");
            GUILayout.BeginHorizontal();
            GUILayout.Label("GuardReduction", GUILayout.Width(96)); slot.Spec.GuardReduction = BattleLabUi.DoubleField("cfg-e-guard-" + i, slot.Spec.GuardReduction, GUILayout.Width(56));
            GUILayout.Label("BossHealOnHeroDeath", GUILayout.Width(136)); slot.Spec.BossHealOnHeroDeath = BattleLabUi.IntField("cfg-e-bossheal-" + i, slot.Spec.BossHealOnHeroDeath, GUILayout.Width(48));
            GUILayout.EndHorizontal();
            BattleLabUi.EndIndent();
        }

        private void DrawEnemySkills(BattleLabEnemyConfig slot, int i)
        {
            bool open = Ui.GroupOpen("enemy-skills-" + i, false);
            int count = (slot.Spec.BasicSkills == null ? 0 : slot.Spec.BasicSkills.Length)
                + (slot.Spec.PrimalSkills == null ? 0 : slot.Spec.PrimalSkills.Length)
                + (slot.Spec.TechnicalSkills == null ? 0 : slot.Spec.TechnicalSkills.Length);
            bool next = BattleLabUi.Foldout(open, "技能列表", count + " 个技能 · 含基础 / 低理智 / 高理智");
            Ui.SetGroupOpen("enemy-skills-" + i, next);
            if (!next) return;
            BattleLabUi.BeginIndent();
            DrawSkillEditor(slot.Spec, "cfg-e-" + i);
            BattleLabUi.EndIndent();
        }

        private void DrawSkillEditor(ActorSpec spec, string key)
        {
            if (spec == null) return;
            DrawSkillList(spec, "BasicSkills", "基础/当前形态", spec.BasicSkills, key + "b");
            DrawSkillList(spec, "PrimalSkills", "低理智", spec.PrimalSkills, key + "p");
            DrawSkillList(spec, "TechnicalSkills", "高理智", spec.TechnicalSkills, key + "t");
        }

        private void DrawSkillList(ActorSpec spec, string field, string title, SkillSpec[] list, string key)
        {
            if (spec == null) return;
            BattleLabUi.SubTitle(title + "（" + (list == null ? 0 : list.Length) + "）");
            if (list == null || list.Length == 0)
            {
                BattleLabUi.Small("无技能。");
                return;
            }
            for (int i = 0; i < list.Length; i++)
            {
                var skill = list[i];
                if (skill == null) continue;
                string sk = key + "-" + i;
                string groupKey = "skill-" + sk;
                bool open = Ui.GroupOpen(groupKey, false);
                string summary = EffectLabels[Mathf.Clamp((int)skill.Effect, 0, EffectLabels.Length - 1)]
                    + " → " + (skill.Target == TargetSide.Enemy ? "敌方" : "我方")
                    + " · ×" + skill.Multiplier.ToString("0.##")
                    + " · 命中 " + skill.Accuracy.ToString("0.##")
                    + (skill.Uses > 0 ? " · 次数 " + skill.Uses : " · 无限");

                GUILayout.BeginVertical(GUI.skin.box);
                bool next = BattleLabUi.Foldout(open, "#" + i + " " + (skill.Name ?? "") + "（" + (skill.Id ?? "") + "）", summary);
                Ui.SetGroupOpen(groupKey, next);
                // The description stays visible even when the card is collapsed: it is the fastest way
                // to tell two similarly named skills apart.
                if (!string.IsNullOrEmpty(skill.Description))
                    GUILayout.Label(skill.Description, BattleLabUi.Hint);
                if (!next)
                {
                    GUILayout.EndVertical();
                    continue;
                }

                BattleLabUi.BeginIndent();
                BattleLabUi.SubTitle("基础");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Effect", GUILayout.Width(44));
                skill.Effect = (SkillEffect)BattleLabUi.ValueGrid((int)skill.Effect, EffectValues, EffectLabels, 5);
                GUILayout.Label("Target", GUILayout.Width(44));
                skill.Target = (TargetSide)BattleLabUi.ValueGrid((int)skill.Target, SideValues, SideLabels, 2);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("描述", GUILayout.Width(44));
                skill.Description = BattleLabUi.TextField(sk + "-desc", skill.Description ?? "", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("伤害与消耗");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Multiplier", GUILayout.Width(64)); skill.Multiplier = BattleLabUi.DoubleField(sk + "-mult", skill.Multiplier, GUILayout.Width(56));
                GUILayout.Label("Accuracy", GUILayout.Width(58)); skill.Accuracy = BattleLabUi.DoubleField(sk + "-acc", skill.Accuracy, GUILayout.Width(56));
                GUILayout.Label("Uses", GUILayout.Width(34)); skill.Uses = BattleLabUi.IntField(sk + "-uses", skill.Uses, GUILayout.Width(44));
                GUILayout.Label("Bleed", GUILayout.Width(40)); skill.Bleed = BattleLabUi.IntField(sk + "-bleed", skill.Bleed, GUILayout.Width(44));
                GUILayout.Label("BleedTurns", GUILayout.Width(68)); skill.BleedTurns = BattleLabUi.IntField(sk + "-bleedturns", skill.BleedTurns, GUILayout.Width(44));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                BattleLabUi.Toggle("Interrupt", ref skill.Interrupt);
                BattleLabUi.Toggle("Cleanse", ref skill.Cleanse);
                BattleLabUi.Toggle("RandomTarget", ref skill.RandomTarget);
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("范围与目标");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Targeting", GUILayout.Width(62)); skill.Targeting = (SkillTargeting)BattleLabUi.ValueGrid((int)skill.Targeting, TargetingValues, TargetingLabels, 2);
                GUILayout.Label("Area", GUILayout.Width(36)); skill.Area = (SkillArea)BattleLabUi.ValueGrid((int)skill.Area, AreaValues, AreaLabels, 5);
                GUILayout.Label("AreaMax", GUILayout.Width(56)); skill.AreaMax = BattleLabUi.IntField(sk + "-areamax", skill.AreaMax, GUILayout.Width(44));
                GUILayout.Label("AreaRatio", GUILayout.Width(62)); skill.AreaRatio = BattleLabUi.DoubleField(sk + "-arearatio", skill.AreaRatio, GUILayout.Width(52));
                GUILayout.Label("AreaDecay", GUILayout.Width(62)); skill.AreaDecay = BattleLabUi.DoubleField(sk + "-areadecay", skill.AreaDecay, GUILayout.Width(52));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("ActorRanks", GUILayout.Width(74)); skill.ActorRanks = BattleLabUi.IntField(sk + "-actorranks", skill.ActorRanks, GUILayout.Width(44));
                GUILayout.Label("TargetRanks", GUILayout.Width(78)); skill.TargetRanks = BattleLabUi.IntField(sk + "-targetranks", skill.TargetRanks, GUILayout.Width(44));
                GUILayout.Label("TargetCount", GUILayout.Width(76)); skill.TargetCount = BattleLabUi.IntField(sk + "-targetcount", skill.TargetCount, GUILayout.Width(44));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("理智、位移与控制");
                GUILayout.BeginHorizontal();
                GUILayout.Label("SanityDamage", GUILayout.Width(86)); skill.SanityDamage = BattleLabUi.IntField(sk + "-sanitydmg", skill.SanityDamage, GUILayout.Width(44));
                GUILayout.Label("SanityAdjacent", GUILayout.Width(94)); skill.SanityAdjacent = BattleLabUi.IntField(sk + "-sanityadj", skill.SanityAdjacent, GUILayout.Width(44));
                GUILayout.Label("SelfMove", GUILayout.Width(58)); skill.SelfMove = BattleLabUi.IntField(sk + "-selfmove", skill.SelfMove, GUILayout.Width(44));
                GUILayout.Label("PullChance", GUILayout.Width(70)); skill.PullChance = BattleLabUi.DoubleField(sk + "-pullchance", skill.PullChance, GUILayout.Width(52));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                BattleLabUi.Toggle("PullToFront", ref skill.PullToFront);
                BattleLabUi.Toggle("Imbalance", ref skill.Imbalance);
                BattleLabUi.Toggle("IgnoreLimbPenalty", ref skill.IgnoreLimbPenalty);
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("条件加成");
                GUILayout.BeginHorizontal();
                GUILayout.Label("HpMissingScale", GUILayout.Width(96)); skill.HpMissingDamageScale = BattleLabUi.DoubleField(sk + "-hpmissing", skill.HpMissingDamageScale, GUILayout.Width(52));
                GUILayout.Label("LimbBonusPerStack", GUILayout.Width(108)); skill.LimbBonusPerStack = BattleLabUi.IntField(sk + "-limbbonus", skill.LimbBonusPerStack, GUILayout.Width(44));
                GUILayout.Label("MaxLimbBonusStacks", GUILayout.Width(116)); skill.MaxLimbBonusStacks = BattleLabUi.IntField(sk + "-maxlimb", skill.MaxLimbBonusStacks, GUILayout.Width(44));
                GUILayout.Label("SelfVulnerable", GUILayout.Width(88)); skill.SelfVulnerable = BattleLabUi.DoubleField(sk + "-selfvuln", skill.SelfVulnerable, GUILayout.Width(52));
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("授予状态");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Grant", GUILayout.Width(40));
                ToggleFlag(ref skill.Grant, AllyBuff.Block, "Block");
                ToggleFlag(ref skill.Grant, AllyBuff.Counter, "Counter");
                ToggleFlag(ref skill.Grant, AllyBuff.Followup, "Followup");
                ToggleFlag(ref skill.Grant, AllyBuff.DefenseUp, "DefenseUp");
                ToggleFlag(ref skill.Grant, AllyBuff.AccuracyUp, "AccuracyUp");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("SelfGrant", GUILayout.Width(60));
                ToggleFlag(ref skill.SelfGrant, AllyBuff.Block, "Block");
                ToggleFlag(ref skill.SelfGrant, AllyBuff.Counter, "Counter");
                ToggleFlag(ref skill.SelfGrant, AllyBuff.Followup, "Followup");
                GUILayout.EndHorizontal();

                BattleLabUi.SubTitle("Rider");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Rider", GUILayout.Width(40));
                int riderValue = skill.Rider == null ? 0 : (int)skill.Rider.Kind;
                int nextRider = BattleLabUi.ValueGrid(riderValue, RiderValues, RiderLabels, 3);
                if (nextRider != riderValue)
                {
                    if (nextRider == 0) skill.Rider = null;
                    else
                    {
                        if (skill.Rider == null) skill.Rider = new SkillRider();
                        skill.Rider.Kind = (RiderKind)nextRider;
                    }
                }
                if (skill.Rider != null)
                {
                    GUILayout.Label("Area", GUILayout.Width(34));
                    skill.Rider.Area = (RiderArea)BattleLabUi.ValueGrid((int)skill.Rider.Area, RiderAreaValues, RiderAreaLabels, 3);
                    GUILayout.Label("Ratio", GUILayout.Width(34));
                    skill.Rider.Ratio = BattleLabUi.DoubleField(sk + "-riderratio", skill.Rider.Ratio, GUILayout.Width(52));
                    GUILayout.Label("Spread", GUILayout.Width(44));
                    skill.Rider.SpreadRatio = BattleLabUi.DoubleField(sk + "-riderspread", skill.Rider.SpreadRatio, GUILayout.Width(52));
                    GUILayout.Label("Amount", GUILayout.Width(46));
                    skill.Rider.Amount = BattleLabUi.IntField(sk + "-rideramount", skill.Rider.Amount, GUILayout.Width(44));
                }
                GUILayout.EndHorizontal();
                BattleLabUi.EndIndent();

                GUILayout.EndVertical();
            }
        }

        private static void ToggleFlag(ref AllyBuff value, AllyBuff flag, string label)
        {
            bool has = (value & flag) != 0;
            if (GUILayout.Toggle(has, label) != has)
            {
                if (has) value &= ~flag;
                else value |= flag;
            }
        }

        private void DrawPresets()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("一键预设");
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("木桩")) ApplyPresetNow(BattleLabPreset.Dummy);
            if (BattleLabUi.Button("空敌（立即胜利）")) ApplyPresetNow(BattleLabPreset.EmptyEnemies);
            if (BattleLabUi.Button("空队（立即失败）")) ApplyPresetNow(BattleLabPreset.EmptyHeroes);
            if (BattleLabUi.Button("普通编队")) ApplyPresetNow(BattleLabPreset.Normal);
            if (BattleLabUi.Button("精英编队")) ApplyPresetNow(BattleLabPreset.Elite);
            if (BattleLabUi.Button("Boss 编队")) ApplyPresetNow(BattleLabPreset.Boss);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("全部怪物（按站位能塞多少塞多少）")) ApplyPresetNow(BattleLabPreset.AllMonsters);
            if (BattleLabUi.Button("全部饰品 + 消耗品 + 装义肢")) ApplyPresetNow(BattleLabPreset.FullEquipment);
            if (BattleLabUi.Button("恢复默认配置"))
            {
                ReplaceConfig(BattleLabFactory.CreateDefault(Catalog));
                Note("已恢复默认配置。");
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawValidationAndJson()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            BattleLabUi.BoxTitle("校验、应用与 JSON");
            var errors = BattleLabFactory.Validate(Config, Catalog);
            if (errors.Count == 0) GUILayout.Label("配置通过校验。", BattleLabUi.Body);
            else BattleLabUi.ErrorLabel(BattleLabUi.FirstLines(errors, 8));

            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("校验配置")) Note(errors.Count == 0 ? "配置通过校验。" : "配置有 " + errors.Count + " 条问题。", errors.Count > 0);
            if (BattleLabUi.Button("应用配置并开战")) StartBattleNow();
            if (BattleLabUi.Button("只重建 Run（不开战）")) NewRunNow();
            if (BattleLabUi.Button("用当前 Run 再开一场")) StartBattleWithRunNow();
            if (BattleLabUi.Button("刷新下面的 JSON")) { SyncConfigJson(); Note("JSON 已按当前配置刷新。"); }
            if (BattleLabUi.Button("保存配置文件")) ExportConfig();
            GUILayout.EndHorizontal();

            GUILayout.Label("导出目录：" + BattleLabJson.ExportDir, BattleLabUi.Hint);
            GUILayout.BeginHorizontal();
            if (BattleLabUi.Button("复制到剪贴板")) { BattleLabUi.CopyToClipboard(Ui.ConfigJson); Note("配置 JSON 已复制。"); }
            if (BattleLabUi.Button("从剪贴板粘贴")) { Ui.ConfigJson = BattleLabUi.Clipboard(); Note("已从剪贴板粘贴到 JSON 文本区。"); }
            if (BattleLabUi.Button("从 JSON 应用"))
            {
                var cfg = BattleLabJson.ImportConfig(Ui.ConfigJson, Catalog, out List<string> importErrors);
                if (cfg == null)
                {
                    Ui.ImportMessage = string.Join("\n", importErrors);
                    Ui.ImportMessageIsError = true;
                }
                else if (importErrors.Count > 0)
                {
                    Ui.ImportMessage = "导入被拒绝：\n" + BattleLabUi.FirstLines(importErrors, 8);
                    Ui.ImportMessageIsError = true;
                }
                else
                {
                    ReplaceConfig(cfg);
                    Ui.ImportMessage = "配置已从 JSON 应用并重建 Run。";
                    Ui.ImportMessageIsError = false;
                }
            }
            GUILayout.EndHorizontal();
            Ui.ConfigJson = BattleLabUi.TextArea("cfg-json", Ui.ConfigJson, GUILayout.MinHeight(160), GUILayout.ExpandWidth(true));
            if (!string.IsNullOrEmpty(Ui.ImportMessage))
                GUILayout.Label(Ui.ImportMessage, Ui.ImportMessageIsError ? BattleLabUi.Error : BattleLabUi.Body);
            GUILayout.Label("JsonUtility 限制：不支持 Dictionary/接口/多态；枚举按整数；缺失字段回退默认。导入后会 Normalize + Validate。", BattleLabUi.Hint);
            GUILayout.EndVertical();
        }
    }
}
