using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    public sealed class StageOneView : MonoBehaviour
    {
        private StageOneApp app;
        private Canvas canvas;
        private GameObject page;
        private Font font;
        private readonly Dictionary<string, Image> cardImages = new Dictionary<string, Image>();
        private static readonly Color Bg = Hex("10151B"), Panel = Hex("192129"), Raised = Hex("222D35"), Ink = Hex("E6E0D4"), Muted = Hex("99A6AC");
        private static readonly Color Gold = Hex("D2AD70"), Cyan = Hex("81C1C8"), Rust = Hex("CE8875"), LineColor = Hex("39454C");
        public Font InterfaceFont { get { return font; } }

        public void Initialize(StageOneApp owner)
        {
            app = owner;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial" }, 24);
            var canvasObject = new GameObject("StageOneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public void Render()
        {
            if (page) { page.SetActive(false); Destroy(page); }
            cardImages.Clear();
            page = new GameObject("Page", typeof(RectTransform)); page.transform.SetParent(canvas.transform, false);
            var root = page.GetComponent<RectTransform>(); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            var bg = page.AddComponent<Image>(); bg.color = Bg;
            Header(root);
            Battlefield(root);
            Inspector(root);
            Actions(root);
            Journal(root);
            Label(root, "Footer", "第一阶段  /  战斗基础验收     ·     F1 操作说明     ·     F3 验收工具     ·     Esc 取消目标", 34, 868, 1280, 22, 13, Muted);
            Label(root, "Version", "STAGE 01   /   0.3", 1370, 868, 195, 22, 13, Muted, TextAnchor.MiddleRight);
            if (app.Battle.Outcome != BattleOutcome.Active) Settlement(root);
            if (app.HelpOpen) Help(root);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (app.LabOpen) Laboratory(root);
#endif
        }

        private void Header(RectTransform root)
        {
            Label(root, "Title", "界限之间", 34, 22, 420, 43, 31, Ink);
            Label(root, "Subtitle", "战斗演练  /  理智与队伍的第一场试炼", 35, 66, 700, 26, 15, Muted);
            Button(root, "Help", "操作说明", "F1", 1138, 27, 130, 53, app.ToggleHelp, false, true, 15);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Button(root, "Laboratory", "验收工具", "F3", 1280, 27, 130, 53, app.ToggleLab, false, !app.Busy, 15);
#endif
            Button(root, "NewRun", "新开演练", "重置本局", 1422, 27, 143, 53, () => app.NewRun(), false, !app.Busy, 15);
            var strip = Box(root, "StatusStrip", 34, 108, 1532, 63, Panel);
            Label(strip, "PlayerLabel", "玩家理智", 18, 10, 100, 22, 14, Muted);
            Label(strip, "PlayerSanity", app.Run.PlayerSanity.ToString("0.0"), 112, 7, 110, 42, 29, Gold);
            Meter(strip, "PlayerMeter", 220, 25, 200, 7, (float)app.Run.PlayerSanity / 100, Gold);
            Label(strip, "BattleIndex", "第 " + app.Run.Battles + " 场演练   /   第 " + app.Battle.Round + " 回合", 458, 18, 300, 27, 17, Ink);
            string next = app.Battle.Current == null ? "本场已结束" : (app.Battle.Current.Player ? "我方行动 · " : "敌方行动 · ") + app.Battle.Current.Name;
            Label(strip, "CurrentTurn", next, 778, 18, 360, 27, 17, app.Battle.Current != null && app.Battle.Current.Player ? Cyan : Rust);
            Label(strip, "Seed", "固定种子  " + app.Run.Seed, 1210, 18, 300, 27, 14, Muted, TextAnchor.MiddleRight);
        }

        private void Battlefield(RectTransform root)
        {
            var field = Box(root, "Battlefield", 34, 188, 1176, 376, Panel);
            // Subtle architectural shapes establish depth without imported artwork.
            for (int i = 0; i < 8; i++)
            {
                Box(field, "Pillar" + i, 35 + i * 151, 52, 18, 240, Hex("1D282F"));
                Box(field, "Lintel" + i, 35 + i * 151, 52, 110, 7, Hex("202B32"));
            }
            Box(field, "Floor", 18, 294, 1140, 1, LineColor);
            Label(field, "AllySide", "探索者", 22, 16, 350, 27, 16, Cyan);
            Label(field, "EnemySide", "废墟中的回声", 700, 16, 450, 27, 16, Rust, TextAnchor.MiddleRight);
            Label(field, "BattleDivider", "对峙", 535, 157, 106, 50, 18, Muted, TextAnchor.MiddleCenter);
            foreach (var actor in app.Run.Heroes)
            {
                float x = 22 + (3 - actor.Rank) * 122;
                ActorCard(field, actor, x, 56, 112, 252);
            }
            foreach (var actor in app.Battle.Enemies)
                ActorCard(field, actor, 682 + actor.Rank * 122, 56, 112, 252);
            Label(field, "Rear", "后排", 26, 324, 100, 21, 12, Muted);
            Label(field, "Front", "前排  →", 395, 324, 115, 21, 12, Muted, TextAnchor.MiddleRight);
            Label(field, "EnemyFront", "←  前排", 684, 324, 130, 21, 12, Muted);
            var upcoming = app.Battle.TurnPreview.Take(6).Select(a => a.Name);
            Label(field, "TurnOrder", "接下来   " + string.Join("  ·  ", upcoming), 190, 348, 980, 21, 12, Muted, TextAnchor.MiddleRight);
        }

        private void ActorCard(RectTransform parent, ActorState actor, float x, float y, float w, float h)
        {
            bool active = app.Battle.Current == actor;
            bool possible = app.Pending.HasValue && app.Battle.Targets(app.Pending.Value).Any(a => a.Id == actor.Id);
            Color accent = actor.Player ? Cyan : Rust;
            Color background = actor.Dead ? Hex("161C22") : active ? Hex("2D363A") : Hex("202930");
            if (possible) background = actor.Player ? Hex("28434A") : Hex("4C3430");
            var rect = Box(parent, "Actor_" + actor.Id, x, y, w, h, background);
            var img = rect.GetComponent<Image>(); cardImages[actor.Id] = img;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = img;
            var cb = button.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1); cb.pressedColor = Color.gray; button.colors = cb;
            button.onClick.AddListener(() => app.SelectActor(actor.Id));
            if (active || possible || app.InspectedId == actor.Id) Outline(rect, possible ? Gold : active ? Gold : accent, 1.5f);
            Label(rect, "Rank", "位置 " + (actor.Rank + 1), 9, 6, 94, 21, 11, Muted);
            var art = NewRect(rect, "Silhouette", 6, 34, 100, 119).gameObject.AddComponent<SilhouetteGraphic>();
            art.Role = actor.Definition.Role; art.Enemy = actor.Definition.Pattern; art.IsEnemy = !actor.Player;
            art.color = actor.Dead ? Hex("41484B") : Hex("080E13"); art.Accent = actor.Dead ? Hex("5A5E5D") : accent; art.raycastTarget = false;
            Label(rect, "ActorName", actor.Name, 4, 149, 104, 29, 19, actor.Dead ? Muted : Ink, TextAnchor.MiddleCenter);
            Meter(rect, "HealthMeter", 9, 184, 94, 6, (float)actor.Hp / actor.Definition.MaxHp, actor.DeathDoor ? Rust : accent);
            Label(rect, "Health", actor.Dead ? "已死亡" : actor.DeathDoor ? "濒死 · 需要治疗" : actor.Hp + " / " + actor.Definition.MaxHp, 3, 194, 106, 21, 12, actor.DeathDoor ? Rust : Ink, TextAnchor.MiddleCenter);
            string status = actor.Player ? "理智 " + actor.Sanity.ToString("0.#") + " · " + app.Battle.Rules.Mental(actor.Mental).Name : "速度 " + actor.Speed;
            Label(rect, "Status", status, 3, 220, 106, 21, 11, actor.Player ? Cyan : Muted, TextAnchor.MiddleCenter);
            if (actor.Dead) Label(rect, "DeadCross", "×", 31, 63, 52, 65, 54, Rust, TextAnchor.MiddleCenter);
            if (actor.Player)
            {
                string buff = actor.BleedTurns > 0 ? "流血 " + actor.BleedDamage + "×" + actor.BleedTurns : actor.Defending ? "防御" : actor.Form == SkillForm.Technical ? "技术灵感" : actor.Form == SkillForm.Primal ? "原始形态" : "";
                Label(parent, "Buff_" + actor.Id, buff, x, y + h + 2, w, 22, 12, actor.BleedTurns > 0 ? Rust : Gold, TextAnchor.MiddleCenter);
            }
            else Label(parent, "Intent_" + actor.Id, app.Battle.Intent(actor), x - 3, y + h + 4, w + 6, 46, 11, actor.Interrupted > 0 ? Cyan : Rust, TextAnchor.UpperCenter);
        }

        private void Inspector(RectTransform root)
        {
            var box = Box(root, "Inspector", 1228, 188, 338, 328, Panel);
            var actor = app.Battle.Find(app.InspectedId) ?? app.Run.Heroes[0];
            Label(box, "InspectorHeading", "观察 / " + (actor.Player ? "队员" : "敌人"), 20, 15, 300, 24, 13, Muted);
            Label(box, "SelectedName", actor.Name, 20, 47, 200, 34, 25, Ink);
            Label(box, "ActorSubtitle", actor.Definition.Subtitle, 20, 84, 298, 27, 13, Muted);
            if (actor.Player)
            {
                Label(box, "PersonalSanity", "个人理智  " + actor.Sanity.ToString("0.0") + "     速度  " + actor.Speed, 20, 119, 300, 26, 17, Cyan);
                Meter(box, "SanityTrack", 20, 154, 298, 6, (float)actor.Sanity / 100, Cyan);
                Box(box, "LowTick", 20 + 298 * 0.30f, 151, 2, 12, Gold);
                Box(box, "HighTick", 20 + 298 * 0.70f, 151, 2, 12, Gold);
                Label(box, "RangeLabels", "0          30          稳定区          70       100", 20, 169, 300, 19, 11, Muted);
                double[] p = app.Battle.FormProbabilities(actor);
                ProbabilityRow(box, "技术扩展", p[0], 197, Cyan);
                ProbabilityRow(box, "基础形态", p[1], 223, Gold);
                ProbabilityRow(box, "原始形态", p[2], 249, Rust);
                Label(box, "StateInfo", "持续状态：" + app.Battle.Rules.Mental(actor.Mental).Name + "  ·  区间外仍保留", 20, 274, 300, 18, 11, Muted);
                Label(box, "Attributes", AttributeText(actor), 20, 294, 300, 30, 11, Muted);
            }
            else
            {
                Label(box, "EnemyStats", "生命 " + actor.Hp + " / " + actor.Definition.MaxHp + "     速度 " + actor.Speed, 20, 124, 300, 27, 17, Rust);
                Label(box, "EnemyAttr", AttributeText(actor), 20, 152, 300, 30, 11, Muted);
                Label(box, "EnemyIntentHeading", "下一次行动预告", 20, 185, 300, 23, 13, Muted);
                Label(box, "EnemyIntentDetail", app.Battle.Intent(actor), 20, 214, 295, 66, 20, Rust);
                Label(box, "EnemyHint", "换位、掩护或打断可改变受到的威胁。", 20, 287, 300, 25, 12, Muted);
            }
        }

        private string AttributeText(ActorState actor)
        {
            var d = actor.Definition;
            return "命中 " + (d.Accuracy * 100).ToString("0.#") + "%   暴击 " + (d.CritChance * 100).ToString("0.#") + "%   攻击 " + d.Attack
                + "\n闪避 " + (d.Dodge * 100).ToString("0.#") + "%   防御 " + (d.Defense * 100).ToString("0.#") + "%";
        }

        private void ProbabilityRow(RectTransform parent, string name, double value, float y, Color color)
        {
            Label(parent, name, name, 20, y, 82, 21, 13, Ink);
            Meter(parent, name + "Bar", 112, y + 9, 131, 5, (float)value, color);
            Label(parent, name + "Percent", (value * 100).ToString("0.0") + "%", 247, y, 70, 21, 13, color, TextAnchor.MiddleRight);
        }

        private void Actions(RectTransform root)
        {
            var area = Box(root, "ActionPanel", 34, 580, 1176, 273, Panel);
            var actor = app.Battle.Current;
            string title = actor == null ? "战斗已结算" : !actor.Player ? "等待敌方行动" : actor.Name + " · 选择行动";
            Label(area, "ActionTitle", title, 20, 14, 600, 29, 20, Ink);
            Label(area, "Instruction", app.Pending.HasValue ? "点击高亮目标  /  Esc取消" : "每次行动消耗一个回合", 713, 18, 440, 24, 13, Muted, TextAnchor.MiddleRight);
            ActionKind[] order = { ActionKind.Primary, ActionKind.Support, ActionKind.Technical, ActionKind.Defend, ActionKind.Swap, ActionKind.Focus, ActionKind.Embrace, ActionKind.Retreat };
            for (int i = 0; i < order.Length; i++)
            {
                var kind = order[i];
                var skill = actor != null && actor.Player ? app.Battle.SkillFor(actor, kind) : null;
                string name = ActionName(kind, skill);
                string reason = app.Battle.DisabledReason(kind);
                string detail = reason ?? ActionDetail(kind, actor, skill);
                Button(area, "Action_" + kind, name, detail, 20 + (i % 4) * 287, 55 + (i / 4) * 77, 275, 65,
                    () => app.SelectAction(kind), app.Pending == kind, reason == null && !app.Busy, 17);
            }
            Label(area, "ActionHint", app.Hint ?? "", 20, 218, 1136, 42, 14, app.Pending.HasValue ? Gold : Muted);
        }

        private string ActionName(ActionKind kind, SkillSpec skill)
        {
            if (skill != null) return skill.Name;
            switch (kind)
            {
                case ActionKind.Primary: return "主要技能";
                case ActionKind.Support: return "职业技能";
                case ActionKind.Technical: return "技术灵感";
                case ActionKind.Defend: return "防御";
                case ActionKind.Swap: return "换位";
                case ActionKind.Focus: return "稳定心神";
                case ActionKind.Embrace: return "直视深渊";
                default: return "撤退";
            }
        }

        private string ActionDetail(ActionKind kind, ActorState actor, SkillSpec skill)
        {
            if (skill != null)
            {
                string count = kind == ActionKind.Support && skill.Uses > 0 ? " · 剩" + actor.SupportUses + "次" : "";
                string value = skill.Effect == SkillEffect.Attack ? "伤害 " + app.Battle.SkillValue(actor, skill) + " · "
                    : skill.Effect == SkillEffect.Heal ? "恢复 " + app.Battle.SkillValue(actor, skill) + " · " : "";
                return value + skill.Description + count;
            }
            switch (kind)
            {
                case ActionKind.Defend: return "下次行动前 · 攻击伤害减半";
                case ActionKind.Swap: return "与相邻队员交换位置";
                case ActionKind.Focus: return "理智+15 · 剩" + (actor == null ? 0 : actor.FocusUses) + "次";
                case ActionKind.Embrace: return "理智−15 · 剩" + (actor == null ? 0 : actor.EmbraceUses) + "次";
                case ActionKind.Retreat: return "结束本场 · 结算理智反馈";
                default: return "本回合未获得";
            }
        }

        private void Journal(RectTransform root)
        {
            var box = Box(root, "Journal", 1228, 532, 338, 321, Panel);
            Label(box, "JournalHeading", "战斗记录", 20, 15, 298, 25, 17, Ink);
            Box(box, "JournalLine", 20, 51, 298, 1, LineColor);
            var entries = app.Battle.Events.AsEnumerable().Reverse().Take(5).ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                Color c = entries[i].Kind == "damage" || entries[i].Kind == "death" ? Rust : entries[i].Kind == "heal" || entries[i].Kind == "sanity" ? Cyan : Muted;
                Label(box, "Log" + i, entries[i].Message, 20, 65 + i * 48, 298, 44, 13, c);
            }
        }

        private void Settlement(RectTransform root)
        {
            var result = app.Battle.Settlement;
            var p = Modal(root, "Settlement", 900, 612);
            string heading = result.Outcome == BattleOutcome.Victory ? "战斗胜利" : result.Outcome == BattleOutcome.Retreat ? "队伍已撤离" : "本次冒险结束";
            Label(p, "ResultHeading", heading, 36, 24, 828, 48, 31, Ink);
            Label(p, "ResultSubtitle", "战斗中的经历，已经成为下一次出发的起点。", 36, 78, 828, 26, 15, Muted);
            Label(p, "ResultColumns", "队员                       入战理智             结束理智               本场变化", 42, 127, 816, 27, 15, Muted);
            Box(p, "ResultRule", 36, 166, 828, 1, LineColor);
            int row = 0;
            foreach (var e in result.Entries)
            {
                float y = 181 + row * 45;
                Label(p, "ResultName" + row, e.Name + (e.Dead ? "  ·  死亡" : ""), 42, y, 260, 28, 18, e.Dead ? Rust : Ink);
                Label(p, "ResultStart" + row, e.Start.ToString("0.0"), 308, y, 140, 28, 18, Muted);
                Label(p, "ResultEnd" + row, e.End.ToString("0.0"), 487, y, 140, 28, 18, Ink);
                Label(p, "ResultDelta" + row, e.Delta.ToString("+0.0;-0.0;0.0"), 672, y, 145, 28, 18, e.Delta < 0 ? Rust : Cyan);
                row++;
            }
            double average = result.Entries.Count == 0 ? 0 : result.Entries.Average(e => e.Delta);
            Label(p, "Formula", "平均净变化 " + average.ToString("+0.0;-0.0;0.0") + "  ×  " + app.Battle.Rules.FeedbackFactor.ToString("0.0") + "  =  " + result.RawDelta.ToString("+0.0;-0.0;0.0"), 40, 376, 820, 30, 17, Muted);
            Label(p, "PlayerResult", "玩家理智    " + result.Before.ToString("0.0") + "  →  " + result.After.ToString("0.0"), 40, 420, 820, 48, 29, Gold);
            Label(p, "Continuity", "继续演练会整备存活队员的生命；个人理智与精神状态保留，死亡队员不复活。", 40, 478, 820, 33, 13, Muted);
            Button(p, "ContinueBattle", "继续演练", "保留理智与持续状态", 40, 532, 394, 56, app.ContinueBattle, true, app.Run.Heroes.Any(a => !a.Dead), 18);
            Button(p, "ResultNewRun", "新开演练", "重置全部队员", 450, 532, 410, 56, () => app.NewRun(), false, true, 18);
        }

        private void Help(RectTransform root)
        {
            var p = Modal(root, "HelpModal", 980, 656);
            Label(p, "HelpTitle", "行动指南", 34, 25, 900, 45, 29, Ink);
            string text = "01  选择技能，再点击高亮目标。点击任意角色可查看详情。\n\n" +
                "02  我方前排靠近敌人。位置不符时，可以消耗行动与相邻队友换位。\n\n" +
                "03  理智低于30才可能原始化，高于70才可能出现技术灵感。\n      30至70只用基础形态；抽出的技能在本回合内固定。\n\n" +
                "04  稳定心神和直视深渊分别改变15点个人理智，每场各限两次。\n      它们与敌方低语可能产生凝神或躁动；持续状态跨战斗保留。\n\n" +
                "05  敌人卡片下方显示下一次攻击。用掩护、换位或打断应对。\n      医师可治疗濒死队员；濒死时再次受伤会检定死亡。\n\n" +
                "06  胜利和撤退都会反馈玩家理智：队员平均净变化 × 0.5。\n      当前为第一阶段演练版，尚未加入探索、断肢、图鉴和存档。\n\n" +
                "07  命中、暴击与闪避由角色属性直接决定，不随理智变化。\n      技能伤害 = 基础攻击力 × 技能倍率；受伤按目标防御比例减少。";
            Label(p, "HelpBody", text, 38, 92, 904, 470, 18, Ink);
            Button(p, "CloseHelp", "返回战斗", "Esc / F1", 38, 579, 904, 52, app.ToggleHelp, true, true, 18);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Laboratory(RectTransform root)
        {
            var p = Modal(root, "LabModal", 1040, 638);
            Label(p, "LabTitle", "第一阶段验收工具", 34, 23, 960, 44, 28, Ink);
            Label(p, "LabCaption", "仅开发版提供。新建队伍会重置演练；单人调整会计入本场理智净变化。", 36, 76, 968, 28, 15, Muted);
            Label(p, "PresetHeading", "新建固定理智队伍", 36, 128, 920, 26, 17, Gold);
            Button(p, "Preset15", "理智 15", "只可能触发原始形态", 36, 171, 310, 66, () => app.NewRun(15), false, true, 20);
            Button(p, "Preset50", "理智 50", "稳定区 · 仅基础形态", 365, 171, 310, 66, () => app.NewRun(50), false, true, 20);
            Button(p, "Preset85", "理智 85", "只可能触发技术扩展", 694, 171, 310, 66, () => app.NewRun(85), false, true, 20);
            var actor = app.Battle.Find(app.InspectedId);
            string selected = actor != null && actor.Player ? actor.Name : "第一位存活队员";
            Label(p, "ChangeHeading", "调整选中队员 / " + selected + "    （不会重抽本回合技能）", 36, 262, 968, 27, 16, Ink);
            Button(p, "Set15", "设为15", "实际理智变化", 36, 304, 151, 54, () => app.DebugSetSanity(15), false, true, 16);
            Button(p, "Set50", "设为50", "实际理智变化", 200, 304, 151, 54, () => app.DebugSetSanity(50), false, true, 16);
            Button(p, "Set85", "设为85", "实际理智变化", 364, 304, 151, 54, () => app.DebugSetSanity(85), false, true, 16);
            Button(p, "SetComposed", "给予凝神", "持续精神状态", 528, 304, 151, 54, () => app.DebugMental(MentalState.Composed), false, true, 16);
            Button(p, "SetAgitated", "给予躁动", "持续精神状态", 692, 304, 151, 54, () => app.DebugMental(MentalState.Agitated), false, true, 16);
            Button(p, "ClearMental", "治疗状态", "清除持续状态", 856, 304, 148, 54, () => app.DebugMental(MentalState.None), false, true, 16);
            Label(p, "ScenariosHeading", "结算与濒死样例", 36, 389, 920, 26, 17, Gold);
            Button(p, "FeedbackExample", "反馈 −2.5", "变化 −20 / −10 / 0 / +10", 36, 432, 310, 67, app.DebugFeedbackExample, false, true, 18);
            Button(p, "DeathDoorExample", "濒死演练", "护卫0生命 · 尝试救治", 365, 432, 310, 67, app.DebugDeathDoor, false, true, 18);
            Button(p, "EndBattleDebug", "结束当前演练", "查看反馈与状态延续", 694, 432, 310, 67, app.DebugSettle, false, true, 18);
            Button(p, "CloseLab", "返回战斗", "F3 / Esc", 36, 549, 968, 56, app.ToggleLab, true, true, 18);
        }
#endif

        public IEnumerator AnimateEvents(BattleEvent[] events, bool fast)
        {
            var hits = events.Where(e => e.Kind == "damage" || e.Kind == "heal").ToArray();
            if (hits.Length == 0) { yield return new WaitForSeconds(fast ? 0.01f : 0.12f); yield break; }
            foreach (var e in hits)
            {
                Image img;
                if (e.TargetId == null || !cardImages.TryGetValue(e.TargetId, out img) || !img) continue;
                Color saved = img.color;
                img.color = e.Kind == "heal" ? Hex("397274") : Hex("803F39");
                yield return new WaitForSeconds(fast ? 0.01f : 0.18f);
                if (img) img.color = saved;
            }
        }

        private RectTransform Modal(RectTransform root, string name, float width, float height)
        {
            var scrim = Box(root, name + "Scrim", 0, 0, 1600, 900, new Color(0.02f, 0.03f, 0.04f, 0.88f));
            var p = Box(scrim, name, (1600 - width) / 2, (900 - height) / 2, width, height, Panel);
            Outline(p, LineColor, 1); return p;
        }

        private RectTransform NewRect(Transform parent, string name, float x, float y, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); return r;
        }
        private RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        { var r = NewRect(parent, name, x, y, width, height); r.gameObject.AddComponent<Image>().color = color; return r; }
        private Text Label(Transform parent, string name, string content, float x, float y, float w, float h, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var r = NewRect(parent, name, x, y, w, h);
            var t = r.gameObject.AddComponent<Text>(); t.font = font; t.fontSize = size; t.color = color; t.text = content;
            t.alignment = alignment; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.08f; t.raycastTarget = false; t.supportRichText = false;
            return t;
        }
        private Button Button(Transform parent, string id, string title, string detail, float x, float y, float w, float h, Action action, bool selected = false, bool enabled = true, int size = 18)
        {
            var r = Box(parent, id, x, y, w, h, selected ? Hex("3B4140") : Raised);
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = r.GetComponent<Image>(); button.interactable = enabled;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.17f, 1.17f, 1.17f, 1);
            colors.disabledColor = new Color(0.58f, 0.58f, 0.58f, 1); colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1); button.colors = colors;
            button.onClick.AddListener(() => action());
            if (selected) Outline(r, Gold, 1);
            Label(r, id + "Title", title, 13, 8, w - 26, 26, size, enabled ? (selected ? Gold : Ink) : Muted);
            Label(r, id + "Detail", detail, 13, h - 25, w - 26, 21, 11, Muted);
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            return button;
        }
        private void Meter(Transform parent, string id, float x, float y, float w, float h, float amount, Color color)
        {
            var track = Box(parent, id, x, y, w, h, Hex("0B1117"));
            Box(track, id + "Fill", 0, 0, w * Mathf.Clamp01(amount), h, color).GetComponent<Image>().raycastTarget = false;
        }
        private void Outline(RectTransform rect, Color color, float size)
        { var o = rect.gameObject.AddComponent<Outline>(); o.effectColor = color; o.effectDistance = new Vector2(size, -size); o.useGraphicAlpha = true; }
        private static Color Hex(string code) { Color c; ColorUtility.TryParseHtmlString("#" + code, out c); return c; }
    }
}
