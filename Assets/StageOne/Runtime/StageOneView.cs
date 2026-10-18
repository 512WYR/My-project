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
        // The event page's option band: the full content width, split evenly between however many options
        // the current step carries.
        private const float EventArea = 1532f, EventGap = 30f, EventTop = 344f;
        public Font InterfaceFont { get { return font; } }

        public void Initialize(StageOneApp owner)
        {
            app = owner;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial" }, 24);
            var canvasObject = new GameObject("StageOneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the optional external battle HUD (sortingOrder 10), so the settlement, help and lab
            // modals it still paints cover it. With a single canvas in the scene this changes nothing.
            canvas.sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>Lets an external battle UI hand its actor portraits over, so the hit flash still finds them.</summary>
        public void RegisterCardImage(string id, Image image) { if (!string.IsNullOrEmpty(id) && image) cardImages[id] = image; }

        public void Render()
        {
            // A scene that draws its own pages keeps this canvas for the modals only. The background has to
            // stay transparent then, or it would cover the external UI sitting underneath.
            bool external = app.ExternalBattleUi;
            if (page) { page.SetActive(false); Destroy(page); }
            if (!external) cardImages.Clear();
            page = new GameObject("Page", typeof(RectTransform)); page.transform.SetParent(canvas.transform, false);
            var root = page.GetComponent<RectTransform>(); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            if (external)
            {
                if (app.Battle != null && app.Battle.Outcome != BattleOutcome.Active) Settlement(root);
                if (app.HelpOpen) Help(root);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (app.LabOpen) Laboratory(root);
#endif
                if (app.AccessoryEditorOpen) AccessoryEditor(root);
                if (app.ConsumableEditorOpen) ConsumableEditor(root);
                return;
            }
            var bg = page.AddComponent<Image>(); bg.color = Bg;
            Header(root);
            // The room pages replace the battle widgets in place. They stay inline rather than using a
            // modal so the header buttons keep taking clicks, which the smoke run relies on.
            bool nodePage = app.AwaitingNode;
            if (nodePage)
            {
                switch (app.Room)
                {
                    case RoomView.Buff: BuffRoom(root); break;
                    case RoomView.Supply: SupplyRoom(root); break;
                    case RoomView.Event: EventRoom(root); break;
                    case RoomView.Elevator: ElevatorRoom(root); break;
                    case RoomView.Treasure: TreasureRoom(root); break;
                    default: NodeSelect(root); break;
                }
            }
            else
            {
                Battlefield(root);
                Inspector(root);
                Actions(root);
                Journal(root);
            }
            Label(root, "Footer", "第一阶段  /  战斗与探索     ·     操作说明、道具、验收工具见上方按钮     ·     Esc 取消目标", 34, 868, 1320, 22, 13, Muted);
            Label(root, "Version", "STAGE 01   /   0.3", 1370, 868, 195, 22, 13, Muted, TextAnchor.MiddleRight);
            if (!nodePage && app.Battle != null && app.Battle.Outcome != BattleOutcome.Active) Settlement(root);
            if (app.HelpOpen) Help(root);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (app.LabOpen) Laboratory(root);
#endif
            if (app.AccessoryEditorOpen) AccessoryEditor(root);
            if (app.ConsumableEditorOpen) ConsumableEditor(root);
        }

        private void Header(RectTransform root)
        {
            Label(root, "Title", "界限之间", 34, 22, 420, 43, 31, Ink);
            Label(root, "Subtitle", app.AwaitingNode ? "探索路线  /  玩家理智同时决定候选数量与房间类型" : "战斗演练  /  理智与队伍的第一场试炼", 35, 66, 700, 26, 15, Muted);
            Button(root, "Consumables", "道具", "点击", 996, 27, 130, 53, app.ToggleConsumableEditor, app.ConsumableEditorOpen, !app.Busy, 15);
            Button(root, "Help", "操作说明", "点击", 1138, 27, 130, 53, app.ToggleHelp, false, true, 15);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Button(root, "Laboratory", "验收工具", "点击", 1280, 27, 130, 53, app.ToggleLab, false, !app.Busy, 15);
#endif
            Button(root, "NewRun", "新开演练", "重置本局", 1422, 27, 143, 53, () => app.NewRun(), false, !app.Busy, 15);
            var strip = Box(root, "StatusStrip", 34, 108, 1532, 63, Panel);
            Label(strip, "PlayerLabel", "玩家理智", 18, 10, 100, 22, 14, Muted);
            Label(strip, "PlayerSanity", app.Run.PlayerSanity.ToString("0.0"), 112, 7, 110, 42, 29, Gold);
            Meter(strip, "PlayerMeter", 220, 25, 200, 7, (float)app.Run.PlayerSanity / 100, Gold);
            Label(strip, "BattleIndex", app.Battle == null
                ? "第 " + app.Map.Floor + " 层   /   共 " + app.Catalog.MapRules().Floors + " 层   ·   补给 " + app.Map.Supplies
                : "第 " + app.Run.Battles + " 场演练   /   第 " + app.Battle.Round + " 回合", 458, 18, 340, 27, 17, Ink);
            string next = app.Battle == null
                ? (app.Map.Finished ? "本局已结束" : "等待选择房间")
                : app.Battle.Current == null ? "本场已结束" : (app.Battle.Current.Player ? "我方行动 · " : "敌方行动 · ") + app.Battle.Current.Name;
            bool waiting = app.Battle == null || (app.Battle.Current != null && app.Battle.Current.Player);
            Label(strip, "CurrentTurn", next, 778, 18, 360, 27, 17, waiting ? Cyan : Rust);
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
            {
                // Corpses keep their old rank so the log can still name it, but drawing them would put
                // a second card on top of the living unit that has since stepped into that slot.
                if (actor.Dead) continue;
                ActorCard(field, actor, 682 + actor.Rank * 122, 56, 112 + (actor.Width - 1) * 122, 252);
            }
            Label(field, "Rear", "后排", 26, 324, 100, 21, 12, Muted);
            Label(field, "Front", "前排  →", 395, 324, 115, 21, 12, Muted, TextAnchor.MiddleRight);
            Label(field, "EnemyFront", "←  前排", 684, 324, 130, 21, 12, Muted);
            var upcoming = app.Battle.TurnPreview.Take(6).Select(a => a.Name);
            Label(field, "TurnOrder", "接下来   " + string.Join("  ·  ", upcoming), 190, 348, 980, 21, 12, Muted, TextAnchor.MiddleRight);
        }

        private void ActorCard(RectTransform parent, ActorState actor, float x, float y, float w, float h)
        {
            bool active = app.Battle.Current == actor;
            bool possible = app.PendingSwap ? app.Battle.SwapTargets().Any(a => a.Id == actor.Id)
                : app.PendingSkill.HasValue && app.Battle.Targets(app.PendingSkill.Value).Any(a => a.Id == actor.Id);
            Color accent = actor.Player ? Cyan : Rust;
            Color background = actor.Dead ? Hex("161C22") : active ? Hex("2D363A") : Hex("202930");
            if (possible) background = actor.Player ? Hex("28434A") : Hex("4C3430");
            var rect = Box(parent, "Actor_" + actor.Id, x, y, w, h, background);
            var img = rect.GetComponent<Image>(); cardImages[actor.Id] = img;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = img;
            var cb = button.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1); cb.pressedColor = Color.gray; button.colors = cb;
            button.onClick.AddListener(() => app.SelectActor(actor.Id));
            if (active || possible || app.InspectedId == actor.Id) Outline(rect, possible ? Gold : active ? Gold : accent, 1.5f);
            string rankText = actor.Width > 1 ? "位置 " + (actor.Rank + 1) + "～" + (actor.Rank + actor.Width) : "位置 " + (actor.Rank + 1);
            Label(rect, "Rank", rankText, 9, 6, Math.Max(94, 94 + (actor.Width - 1) * 122), 21, 11, Muted);
            var art = NewRect(rect, "Silhouette", 6, 34, 100, 119).gameObject.AddComponent<SilhouetteGraphic>();
            art.Silhouette = actor.Definition.Silhouette; art.Enemy = actor.Definition.Pattern; art.IsEnemy = !actor.Player;
            art.color = actor.Dead ? Hex("41484B") : Hex("080E13"); art.Accent = actor.Dead ? Hex("5A5E5D") : accent; art.raycastTarget = false;
            Label(rect, "ActorName", actor.Name, 4, 149, 104, 29, 19, actor.Dead ? Muted : Ink, TextAnchor.MiddleCenter);
            Meter(rect, "HealthMeter", 9, 184, 94, 6, (float)actor.Hp / actor.Definition.MaxHp, actor.DeathDoor ? Rust : accent);
            Label(rect, "Health", actor.Dead ? "已死亡" : actor.DeathDoor ? "濒死 · 需要治疗" : actor.Hp + " / " + actor.Definition.MaxHp, 3, 194, 106, 21, 12, actor.DeathDoor ? Rust : Ink, TextAnchor.MiddleCenter);
            string status = actor.Player ? "理智 " + actor.Sanity.ToString("0.#") + " · " + app.Battle.Rules.Mental(actor.Mental).Name : "速度 " + actor.Speed;
            Label(rect, "Status", status, 3, 220, 106, 21, 11, actor.Player ? Cyan : Muted, TextAnchor.MiddleCenter);
            if (actor.Dead) Label(rect, "DeadCross", "×", 31, 63, 52, 65, 54, Rust, TextAnchor.MiddleCenter);
            if (actor.Player)
            {
                string buff = actor.BleedTurns > 0 ? "流血 " + actor.BleedDamage + "×" + actor.BleedTurns : actor.Form == SkillForm.Technical ? "高理智技能" : actor.Form == SkillForm.Primal ? "低理智变化" : "";
                // The most serious part injury rides along on the card, so a debuff is visible without
                // opening the inspector.
                string limb = LimbSystem.Summary(actor);
                if (limb.Length > 0) buff = buff.Length > 0 ? limb + " · " + buff : limb;
                Label(parent, "Buff_" + actor.Id, buff, x, y + h + 2, w, 22, 12, actor.BleedTurns > 0 || limb.Length > 0 ? Rust : Gold, TextAnchor.MiddleCenter);
            }
            else Label(parent, "Intent_" + actor.Id, app.Battle.Intent(actor), x - 3, y + h + 4, w + 6, 46, 11, actor.Interrupted > 0 ? Cyan : Rust, TextAnchor.UpperCenter);
        }

        private void Inspector(RectTransform root)
        {
            var box = Box(root, "Inspector", 1228, 188, 338, 424, Panel);
            var actor = app.Battle.Find(app.InspectedId) ?? app.Run.Heroes[0];
            Label(box, "InspectorHeading", "观察 / " + (actor.Player ? "队员" : "敌人"), 20, 14, 300, 22, 13, Muted);
            Label(box, "SelectedName", actor.Name, 20, 42, 200, 32, 25, Ink);
            Label(box, "ActorSubtitle", actor.Definition.Subtitle, 20, 76, 298, 22, 13, Muted);
            if (actor.Player)
            {
                Label(box, "PersonalSanity", "个人理智  " + actor.Sanity.ToString("0.0") + "     速度  " + actor.Speed, 20, 104, 300, 26, 17, Cyan);
                Meter(box, "SanityTrack", 20, 138, 298, 6, (float)actor.Sanity / 100, Cyan);
                Box(box, "LowTick", 20 + 298 * 0.30f, 135, 2, 12, Gold);
                Box(box, "HighTick", 20 + 298 * 0.70f, 135, 2, 12, Gold);
                Label(box, "RangeLabels", "0          30          稳定区          70       100", 20, 150, 300, 18, 11, Muted);
                // The four part rows need the room, so the three form odds share one compact line.
                double[] p = app.Battle.FormProbabilities(actor);
                Label(box, "FormRow", "高理智 " + (p[0] * 100).ToString("0.0") + "%   常态 " + (p[1] * 100).ToString("0.0")
                    + "%   低理智 " + (p[2] * 100).ToString("0.0") + "%", 20, 174, 300, 20, 12, Muted);
                Label(box, "StateInfo", "持续状态：" + app.Battle.Rules.Mental(actor.Mental).Name + "  ·  区间外仍保留", 20, 198, 300, 18, 11, Muted);
                Label(box, "Attributes", AttributeText(actor), 20, 218, 300, 30, 11, Muted);
                Label(box, "Accessories", "饰品  " + AccessorySystem.Summary(actor), 20, 252, 300, 20, 11, Gold);
                Label(box, "LimbHeading", "部位损伤  ·  重伤→断肢→义肢", 20, 276, 300, 20, 13, Gold);
                LimbRow(box, actor, LimbPart.Head, 300);
                LimbRow(box, actor, LimbPart.Torso, 324);
                LimbRow(box, actor, LimbPart.Arm, 348);
                LimbRow(box, actor, LimbPart.Leg, 372);
            }
            else
            {
                Label(box, "EnemyStats", "生命 " + actor.Hp + " / " + actor.Definition.MaxHp + "     速度 " + actor.Speed, 20, 124, 300, 27, 17, Rust);
                Label(box, "EnemyAttr", AttributeText(actor), 20, 152, 300, 30, 11, Muted);
                Label(box, "EnemyIntentHeading", "下一次行动预告", 20, 190, 300, 23, 13, Muted);
                // Three lines once the threat warning is appended, so the box grew with the panel.
                Label(box, "EnemyIntentDetail", app.Battle.Intent(actor), 20, 220, 295, 84, 20, Rust);
                Label(box, "EnemyHint", "换位、掩护或打断可改变受到的威胁。", 20, 310, 300, 25, 12, Muted);
                Label(box, "EnemyLimbNote", "敌人没有部位。旧演练中的执斧者与追猎者仍会重创我方部位。", 20, 344, 300, 62, 12, Muted);
            }
        }

        // One part: name, health bar, health, state. A fitted prosthetic is shown in place of the
        // condition, because that is what the player actually cares about here.
        private void LimbRow(RectTransform parent, ActorState actor, LimbPart part, float y)
        {
            var limb = LimbSystem.Get(actor, part);
            var condition = LimbSystem.ConditionOf(limb);
            bool fitted = limb != null && limb.ProstheticFitted;
            Color color = fitted ? Cyan : condition == LimbCondition.Severed ? Rust : condition == LimbCondition.Injured ? Gold : Cyan;
            Label(parent, "Limb_" + part + "_Name", LimbSystem.PartName(part), 20, y, 46, 21, 13, Ink);
            Meter(parent, "Limb_" + part + "_Bar", 70, y + 8, 104, 5,
                limb == null || limb.MaxHp <= 0 ? 0 : (float)limb.Hp / limb.MaxHp, color);
            Label(parent, "Limb_" + part + "_Value", limb == null ? "—" : limb.Hp + "/" + limb.MaxHp, 180, y, 56, 21, 12, Muted, TextAnchor.MiddleRight);
            Label(parent, "Limb_" + part + "_State", fitted ? "义肢" : LimbSystem.ConditionName(condition), 242, y, 76, 21, 12, color, TextAnchor.MiddleRight);
        }

        private string AttributeText(ActorState actor)
        {
            var d = actor.Definition;
            return "命中 " + (d.Accuracy * 100).ToString("0.#") + "%   暴击 " + (d.CritChance * 100).ToString("0.#") + "%   攻击 " + d.Attack
                + "\n闪避 " + (d.Dodge * 100).ToString("0.#") + "%   防御 " + (d.Defense * 100).ToString("0.#") + "%";
        }

        private void ProbabilityRow(RectTransform parent, string name, double value, float y, Color color, float x = 20)
        {
            Label(parent, name, name, x, y, 82, 21, 13, Ink);
            Meter(parent, name + "Bar", x + 92, y + 9, 131, 5, (float)value, color);
            Label(parent, name + "Percent", (value * 100).ToString("0.0") + "%", x + 227, y, 70, 21, 13, color, TextAnchor.MiddleRight);
        }

        // Inline page: the header stays clickable, so Help, the acceptance tool and 新开演练 keep working.
        //
        // The route is drawn as a line: every position already walked through is a settled card joined to
        // the next, and the position the party is standing on opens out into this step's candidates.
        private void NodeSelect(RectTransform root)
        {
            if (app.Map.Finished) { RunEnd(root); return; }
            var step = app.Map.Current;
            var rules = app.Catalog.MapRules();
            if (step == null) { RunEnd(root); return; }

            Label(root, "MapHeading", "第 " + step.Floor + " 层  /  共 " + rules.Floors + " 层", 34, 186, 700, 38, 29, Ink);
            Label(root, "MapSupplies", "补给 " + app.Map.Supplies + "   ·   持有义肢 " + app.Run.Prosthetics.Count
                + "   ·   持有饰品 " + app.Run.AccessoryStock.Count
                + "   ·   持有道具 " + app.Run.Consumables.Count
                + (app.Map.BuffChosen ? "   ·   增益 " + OpeningBuffs.Name(app.Map.Buff) : ""),
                760, 192, 806, 30, 16, Gold, TextAnchor.MiddleRight);
            Label(root, "MapSanityNote", "已通过 " + app.Map.ResolvedSteps + " 个位置 · 玩家理智 " + app.Run.PlayerSanity.ToString("0.0")
                + " 同时决定候选数量与房间类型（不受角色30/70门槛限制）", 36, 226, 1240, 24, 15, Muted);

            // Floors already behind the party collapse into one summary line each.
            float y = 254;
            for (int f = 1; f < step.Floor; f++)
            {
                Label(root, "MapHistory" + f, "第 " + f + " 层   " + FloorSummary(f), 36, y, 1500, 24, 14, Muted);
                y += 24;
            }

            Label(root, "MapOddsHeading", "本位置可能出现房间的概率", 34, y + 6, 600, 24, 17, Gold);
            // Four rows fit across the panel; the fifth type (the chest room) wraps onto a second row and
            // the route panel gives up exactly as much height as it takes. Zero-weight rows stay off the
            // panel, so parking a type costs nothing here.
            double[] odds = MapGenerator.TypeOdds(rules, app.Run.PlayerSanity);
            var specs = MapGenerator.Resolve(rules).NodeOdds;
            int shown = 0;
            for (int i = 0; i < specs.Length; i++) if (specs[i].Weight > 0) shown++;
            int rows = Mathf.Max(1, (shown + 3) / 4);
            float oddsHeight = Mathf.Max(54f, 16f + rows * 30f);
            var panel = Box(root, "MapOddsPanel", 34, y + 36, 1500, oddsHeight, Panel);
            int column = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                // A type parked at weight 0 would only ever draw a 0.0% row, so it stays off the panel.
                if (specs[i].Weight <= 0) continue;
                ProbabilityRow(panel, MapGenerator.Name(specs[i].Type), odds[i],
                    17 + (column / 4) * 30, NodeColor(specs[i].Type), 20 + (column % 4) * 366);
                column++;
            }

            // The route panel shrinks by whatever the odds strip grew, so the page never runs past the
            // footer. RouteLine moves its lower half up to match.
            float routeHeight = Mathf.Max(300f, 330f - (oddsHeight - 54f));
            float routeTop = y + 36 + oddsHeight + 14;
            Label(root, "MapCardsHeading", "本层路线  ·  " + (step.Fixed ? "固定房间" : step.Candidates.Count + " 个候选"), 34, routeTop, 900, 24, 17, Gold);
            // The last fight's reward line, riding along the same row on the right so it cannot push the
            // route panel off the page.
            if (!string.IsNullOrEmpty(app.MapNote))
                Label(root, "MapNote", app.MapNote, 940, routeTop, 626, 24, 14, Gold, TextAnchor.MiddleRight);
            RouteLine(root, step, routeTop + 30, routeHeight);
            Label(root, "MapHint", app.Hint ?? "", 34, 782, 1532, 42, 15, Ink);
            Label(root, "MapRule", "点击一个候选前进。走过的房间固定不变；前方候选只有在走到跟前才会抽出来。", 34, 828, 1532, 22, 12, Muted);
        }

        // "第 1 层   增益 → 战斗 → 事件 → 电梯井" for the floors already behind the party.
        private string FloorSummary(int floor)
        {
            var names = new List<string>();
            foreach (var s in app.Map.OnFloor(floor))
            {
                if (!s.Resolved || s.Chosen == null) continue;
                names.Add(s.Chosen.Name);
            }
            return names.Count == 0 ? "（未经过任何房间）" : string.Join("  →  ", names);
        }

        // height is 330 whenever the odds strip is one row tall, which is the normal case. The candidate
        // half moves up by exactly the shortfall so the cards never spill out of the panel.
        private void RouteLine(RectTransform root, StepState step, float y, float height)
        {
            var panel = Box(root, "RoutePanel", 34, y, 1532, height, Panel);
            Label(panel, "RouteCaption", "第 " + step.Floor + " 层", 18, 10, 200, 22, 13, Muted);

            // 1) the settled cards, left to right.
            var passed = new List<NodeState>();
            foreach (var s in app.Map.OnFloor(step.Floor))
                if (s.Resolved && s.Chosen != null) passed.Add(s.Chosen);
            float cardW = 104, cardH = 62, gap = 22;
            const float inner = 1496;
            if (!step.Fixed && step.Candidates.Count > 0)
            {
                float needed = passed.Count * cardW + (passed.Count + 1) * gap;
                if (needed > inner) { cardW = Mathf.Max(56, cardW * inner / needed); gap = Mathf.Max(12, gap * inner / needed); }
            }
            float x = 18;
            float lastCenter = 18 + cardW / 2;
            for (int i = 0; i < passed.Count; i++)
            {
                var card = Box(panel, "Passed_" + i, x, 42, cardW, cardH, Raised);
                Outline(card, NodeColor(passed[i].Type), 1);
                Label(card, "Passed_" + i + "_Name", passed[i].Name, 4, 8, cardW - 8, 24, 14, Ink, TextAnchor.MiddleCenter);
                Label(card, "Passed_" + i + "_Floor", "已通过", 4, 34, cardW - 8, 20, 11, Muted, TextAnchor.MiddleCenter);
                lastCenter = x + cardW / 2;
                x += cardW;
                if (i < passed.Count - 1)
                {
                    Box(panel, "Link_" + i, x + 2, 42 + cardH / 2 - 1, gap - 4, 2, LineColor);
                    x += gap;
                }
            }

            if (step.Fixed)
            {
                // A fixed position has exactly one way forward, so it is its own card.
                var only = step.Candidates[0];
                float fx = passed.Count == 0 ? 18 : x + gap;
                var card = Button(panel, "Node_0", only.Name, "唯一的固定房间",
                    fx, 42, 220, cardH + 88, () => app.SelectNode(0), false, true, 20);
                var rect = (RectTransform)card.transform;
                Outline(rect, NodeColor(only.Type), 1);
                Label(rect, "NodeDesc0", NodeDescription(only.Type), 13, 48, 194, 72, 12, Muted);
                Label(panel, "RouteNote", passed.Count == 0 ? "这一层的起点。继续前进会走到这里。" : "本层的末端。继续前进会走到这里。",
                    18, 42 + cardH + 96, 900, 22, 12, Muted);
                return;
            }

            // 2) the trunk: down from the last settled card, then across to every candidate.
            int count = step.Candidates.Count;
            float start = (inner - (count * 220 + (count - 1) * 40)) / 2 + 18;
            float firstCenter = start + 110, lastCandCenter = start + (count - 1) * 260 + 110;
            float barY = Mathf.Max(106f, 128f - (330f - height));
            float cardY = barY + 32f;
            Box(panel, "Trunk", lastCenter - 1, 42 + cardH, 2, barY - 42 - cardH, LineColor);
            Box(panel, "TrunkBar", Mathf.Min(lastCenter, firstCenter), barY, Mathf.Abs(lastCandCenter - Mathf.Min(lastCenter, firstCenter)) + 2, 2, LineColor);
            for (int i = 0; i < count; i++)
            {
                // Captured per iteration: the loop variable itself is shared, so every card would
                // otherwise call SelectNode with the value the loop ended on.
                int index = i;
                float cx = start + i * 260;
                Box(panel, "Stub_" + i, cx + 109, barY, 2, cardY - barY, LineColor);
                var node = step.Candidates[i];
                var card = Button(panel, "Node_" + i, node.Name, "点击前进",
                    cx, cardY, 220, 150, () => app.SelectNode(index), false, true, 22);
                var rect = (RectTransform)card.transform;
                Outline(rect, Gold, 1);
                Label(rect, "NodeDesc" + i, NodeDescription(node.Type), 13, 48, 194, 68, 12, Muted);
            }
        }

        private void RunEnd(RectTransform root)
        {
            var panel = Box(root, "MapRunEnd", 34, 240, 1532, 430, Panel);
            Label(panel, "MapRunEndTitle", app.Map.Cleared ? "本局通关" : "本局结束", 40, 34, 1452, 60, 40, app.Map.Cleared ? Gold : Ink);
            Label(panel, "MapRunEndNote", app.RunEndNote ?? "冒险已经结束。", 40, 112, 1452, 56, 18, Muted);
            Label(panel, "MapRunEndSummary", "到达第 " + app.Map.Floor + " 层  ·  通过 " + app.Map.ResolvedSteps + " 个位置  ·  玩家理智 " + app.Run.PlayerSanity.ToString("0.0"),
                40, 180, 1452, 40, 20, Gold);
            Label(panel, "MapRunEndStock", "补给 " + app.Map.Supplies + "  ·  " + StockText(), 40, 224, 1452, 30, 15, Muted);
            string state = string.Join("   ·   ", app.Run.Heroes.Select(h => h.Name + (h.Dead ? " 阵亡" : " " + h.Hp + "/" + h.Definition.MaxHp)));
            Label(panel, "MapRunEndParty", state, 40, 262, 1452, 30, 15, Ink);
            Button(panel, "MapNewRun", "新开一局", "重置全部队员与路线", 40, 322, 520, 66, () => app.NewRun(), true, true, 20);
        }

        private static readonly LimbPart[] PartOrder = { LimbPart.Head, LimbPart.Torso, LimbPart.Arm, LimbPart.Leg };

        // ---------------- the room pages ----------------
        // All four are inline, for the same reason NodeSelect is: the header buttons have to keep working here.

        private void BuffRoom(RectTransform root)
        {
            Label(root, "BuffHeading", "初始增益", 34, 190, 900, 38, 29, Ink);
            Label(root, "BuffNote", "出发之前，队伍只来得及带走一样东西。选定之后本次冒险不再更改。", 36, 230, 1240, 24, 15, Muted);
            var options = app.Catalog.MapRules().OpeningBuffs;
            float start = (1600 - (options.Length * 400 + (options.Length - 1) * 40)) / 2;
            for (int i = 0; i < options.Length; i++)
            {
                var buff = options[i];
                bool taken = app.Map.BuffChosen;
                bool mine = taken && app.Map.Buff == buff;
                var card = Button(root, "Buff_" + buff, OpeningBuffs.Name(buff), OpeningBuffs.Describe(buff),
                    start + i * 440, 300, 400, 200, () => app.ChooseBuff(buff), mine, !taken, 24);
                var rect = (RectTransform)card.transform;
                Outline(rect, mine ? Gold : LineColor, 1);
            }
            Label(root, "BuffResult", app.RoomNote ?? "", 34, 540, 1532, 60, 19, Gold);
            Button(root, "LeaveRoom", app.Map.BuffChosen ? "离开增益房间" : "先选择一项增益", "继续前进",
                34, 806, 1532, 54, app.LeaveRoom, false, app.Map.BuffChosen, 19);
        }

        // The supply room took over everything the workshop and the camp used to do. The limb half below is
        // the old workshop page unchanged, so a part still only ever goes up by being repaired here.
        private void SupplyRoom(RectTransform root)
        {
            Label(root, "SupplyHeading", "补给站", 34, 186, 900, 38, 29, Ink);
            Label(root, "SupplyNote", "治疗全队、修复重伤部位、为断肢安装义肢、静养恢复理智。每项各可用一次，离开后继续前进。",
                36, 226, 1240, 24, 15, Muted);

            var heal = Button(root, "SupplyHeal", app.Map.SupplyHealed ? "治疗已使用" : "治疗全队",
                "每名队员恢复 " + (app.Catalog.MapRules().SupplyHealRatio * 100).ToString("0") + "% 生命",
                34, 258, 500, 52, app.SupplyHeal, app.Map.SupplyHealed, !app.Map.SupplyHealed, 17);
            Outline((RectTransform)heal.transform, Cyan, 1);
            var calm = Button(root, "SupplyCalm", app.Map.SupplyCalmed ? "静养已使用" : "静养",
                "玩家理智 +" + Exploration.CalmSanity,
                548, 258, 500, 52, app.SupplyCalm, app.Map.SupplyCalmed, !app.Map.SupplyCalmed, 17);
            Outline((RectTransform)calm.transform, Gold, 1);
            var accessories = Button(root, "SupplyAccessories", "管理饰品",
                "持有 " + app.Run.AccessoryStock.Count + " 件 · 已装备 "
                    + app.Run.Heroes.Sum(h => h.EquippedAccessories == null ? 0 : h.EquippedAccessories.Count),
                1062, 258, 504, 52, () => app.OpenAccessoryEditor(), false, true, 17);
            Outline((RectTransform)accessories.transform, Cyan, 1);

            for (int i = 0; i < app.Run.Heroes.Count; i++) SupplyHero(root, app.Run.Heroes[i], 322 + i * 118);
            Label(root, "SupplyStock", StockText(), 34, 800, 1532, 22, 14, Gold);
            Label(root, "SupplyHint", app.Hint ?? "", 34, 824, 1100, 22, 13, Muted);
            Button(root, "LeaveRoom", "离开补给站", "继续前进", 1160, 812, 406, 44, app.LeaveRoom, false, true, 17);
        }

        private void SupplyHero(RectTransform root, ActorState hero, float y)
        {
            var box = Box(root, "SupplyHero_" + hero.Id, 34, y, 1532, 112, Panel);
            Label(box, "HeroName", hero.Name, 18, 8, 280, 28, 19, hero.Dead ? Muted : Ink);
            Label(box, "HeroState", hero.Dead ? "已死亡" : "生命 " + hero.Hp + " / " + hero.Definition.MaxHp,
                18, 38, 280, 22, 13, hero.Dead ? Rust : Muted);
            string worst = LimbSystem.Summary(hero);
            Label(box, "HeroLimb", worst.Length == 0 ? "部位全部完好。" : "最严重  " + worst,
                18, 62, 280, 42, 12, worst.Length == 0 ? Muted : Rust);
            for (int i = 0; i < PartOrder.Length; i++)
                SupplyPart(box, hero, PartOrder[i], 316 + i * 290);
        }

        // Repair is offered only on an injured part; a severed part offers whatever prosthetic the party
        // already holds for it, or says so when there is none.
        private void SupplyPart(RectTransform parent, ActorState hero, LimbPart part, float x)
        {
            var limb = LimbSystem.Get(hero, part);
            var condition = LimbSystem.ConditionOf(limb);
            bool fitted = limb != null && limb.ProstheticFitted;
            Color color = fitted ? Cyan : condition == LimbCondition.Severed ? Rust : condition == LimbCondition.Injured ? Gold : Cyan;
            Label(parent, "Part_" + part + "_Name", LimbSystem.PartName(part), x, 6, 272, 21, 14, Ink);
            string state = fitted ? "义肢" : LimbSystem.ConditionName(condition);
            if (limb != null && !fitted) state += "  " + limb.Hp + "/" + limb.MaxHp;
            Label(parent, "Part_" + part + "_State", state, x, 28, 272, 21, 12, color);
            if (hero.Dead || limb == null) return;
            if (condition == LimbCondition.Injured)
            {
                Button(parent, "Repair_" + hero.Id + "_" + part, "修复", "恢复到 " + limb.MaxHp + "/" + limb.MaxHp,
                    x, 52, 272, 48, () => app.RepairLimb(hero.Id, part), false, true, 15);
                return;
            }
            if (condition != LimbCondition.Severed) return;
            var item = app.Run.Prosthetics.FirstOrDefault(p => p != null && p.Part == part);
            if (item == null) { Label(parent, "Part_" + part + "_Need", "缺少该部位的义肢", x, 58, 272, 38, 12, Rust); return; }
            Button(parent, "Install_" + hero.Id + "_" + part, "安装 " + item.Name, item.Description,
                x, 52, 272, 48, () => app.InstallProsthetic(hero.Id, item.Id), false, true, 15);
        }

        // Events and altars share one page. The whole page is rebuilt from EventScripts.Page on every
        // render, and that call is a pure read of the node's step: re-rendering never re-rolls a step and
        // never widens or narrows the visible options. A step can carry one to four options; four is the
        // widest the scripts go (the workshop), so four options get a 2x2 grid and the rest a single row.
        private void EventRoom(RectTransform root)
        {
            var node = app.ActiveNode;
            bool altar = node != null && node.Type == NodeType.Altar;
            Label(root, "EventHeading", altar ? "祭坛房间" : "事件房间", 34, 190, 900, 38, 29, Ink);
            var page = EventScripts.Page(node, app.Run, app.Catalog.MapRules());
            if (page == null)
            {
                Label(root, "EventNote", "这里什么也没有留下。", 36, 232, 1240, 24, 15, Muted);
                Button(root, "LeaveRoom", "离开", "继续前进", 34, 806, 1532, 54, app.LeaveRoom, false, true, 19);
                return;
            }
            string title = page.Step > 0 && page.StepCount > 0
                ? page.Title + "　步骤 " + page.Step + "/" + page.StepCount : page.Title;
            Label(root, "EventTitle", title, 34, 232, 1240, 34, 23, Gold);
            Label(root, "EventBody", page.Body ?? "", 36, 272, 1240, 44, 16, Ink);

            int n = page.Choices.Count;
            int cols = n <= 3 ? n : 2;
            int rows = n <= 3 ? 1 : 2;
            float width = cols > 0 ? (EventArea - (cols - 1) * EventGap) / cols : EventArea;
            float height = rows == 1 ? 140f : 110f;
            for (int i = 0; i < n; i++)
            {
                var choice = page.Choices[i];
                int index = i;
                float x = 34 + (i % cols) * (width + EventGap);
                float y = EventTop + (i / cols) * (height + 20);
                Button(root, "EventOption_" + i, choice.Shown ? choice.Label : "—— 不可选 ——",
                    choice.Detail ?? "", x, y, width, height,
                    choice.Enabled && choice.Shown ? () => app.ChooseEventOption(index) : (Action)(() => { }),
                    choice.Shown && node != null && node.OptionChosen == i,
                    choice.Shown && choice.Enabled, 18);
            }

            float below = EventTop + rows * (height + 20) + 12;
            Label(root, "EventResult", app.RoomNote ?? "", 34, below, 1532, 44, 19, app.EventCanLeave ? Gold : Muted);
            Label(root, "EventSanity", "玩家理智 " + app.Run.PlayerSanity.ToString("0.0") + "   ·   补给 " + app.Map.Supplies,
                34, below + 50, 1532, 24, 15, Muted);
            Button(root, "LeaveRoom", app.EventCanLeave ? "离开房间" : "先完成这一事件", "继续前进",
                34, 806, 1532, 54, app.LeaveRoom, false, app.EventCanLeave, 19);
            if (!string.IsNullOrEmpty(page.Hint)) Label(root, "EventHint", page.Hint, 34, 770, 1532, 24, 14, Muted);
        }

        // A pure transition page: no fight, no draw, just the floor summary and the way down.
        private void ElevatorRoom(RectTransform root)
        {
            Label(root, "ElevatorHeading", "电梯井", 34, 190, 900, 38, 29, Ink);
            Label(root, "ElevatorNote", "第 " + app.Map.Floor + " 层到此为止。下行之后不会自动整备生命，个人理智与精神状态保留。",
                36, 230, 1240, 24, 15, Muted);
            var panel = Box(root, "ElevatorPanel", 34, 268, 1532, 330, Panel);
            Label(panel, "ElevatorRoute", "本层路线   " + FloorSummary(app.Map.Floor), 22, 20, 1488, 30, 17, Gold);
            for (int i = 0; i < app.Run.Heroes.Count; i++)
            {
                var hero = app.Run.Heroes[i];
                string line = hero.Name + (hero.Dead ? "   阵亡" : "   生命 " + hero.Hp + " / " + hero.Definition.MaxHp);
                Label(panel, "ElevatorHero" + i, line, 22, 66 + i * 34, 700, 28, 16, hero.Dead ? Rust : Ink);
                string limbs = LimbSystem.Summary(hero);
                Label(panel, "ElevatorLimb" + i, limbs.Length == 0 ? "部位全部完好" : "最严重  " + limbs,
                    760, 66 + i * 34, 750, 28, 13, limbs.Length == 0 ? Muted : Gold);
            }
            Label(panel, "ElevatorStock", StockText(), 22, 246, 1488, 24, 14, Muted);
            Label(panel, "ElevatorSupply", "补给 " + app.Map.Supplies + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0") + "   ·   增益 " + OpeningBuffs.Name(app.Map.Buff),
                22, 278, 1488, 24, 14, Muted);
            Button(root, "ElevatorDown", "下到第 " + (app.Map.Floor + 1) + " 层", "进入下一层的补给站", 34, 806, 1532, 54, app.AdvanceFloor, false, true, 19);
        }

        // A page for a room type that is registered but has no content yet. It resolves the position like
        // any other room so a forced visit cannot strand the party on the route.
        private void TreasureRoom(RectTransform root)
        {
            Label(root, "TreasureHeading", "宝箱房", 34, 190, 900, 38, 29, Ink);
            Label(root, "TreasureNote", "第 " + app.Map.Floor + " 层   ·   " + app.Map.Current.Index + " 号位置",
                36, 230, 1240, 24, 15, Muted);
            var panel = Box(root, "TreasurePanel", 34, 268, 1532, 330, Panel);
            Label(panel, "TreasureBody", "宝箱里装着的是饰品，不是义肢。", 22, 24, 1488, 32, 19, Gold);
            Label(panel, "TreasureDetail",
                "每个宝箱房只发一次，登记在节点的已领取标记上，重新打开页面不会重复抽取。\n"
                + "饰品在补给站或验收工具里装备；专属饰品只能由对应角色使用。",
                22, 66, 1488, 90, 15, Ink);
            Label(panel, "TreasureStock", "补给 " + app.Map.Supplies + "   ·   持有义肢 " + app.Run.Prosthetics.Count
                + "   ·   持有饰品 " + app.Run.AccessoryStock.Count
                + "   ·   持有道具 " + app.Run.Consumables.Count
                + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0"), 22, 176, 1488, 26, 15, Muted);
            Label(panel, "TreasureResult", app.RoomNote ?? "", 22, 218, 1488, 60, 17, Gold);
            Button(root, "LeaveRoom", "离开宝箱房", "继续前进", 34, 806, 1532, 54, app.LeaveRoom, false, true, 19);
        }

        private string StockText()
        {
            string accessories = "持有饰品 " + app.Run.AccessoryStock.Count + " 件";
            string consumables = "持有道具 " + app.Run.Consumables.Count + " 件";
            if (app.Run.Prosthetics.Count == 0)
                return "持有义肢：无（精英战斗掉落，也可用验收工具发放）   ·   " + accessories + "   ·   " + consumables;
            var groups = app.Run.Prosthetics.GroupBy(p => p.Id).Select(g => g.First().Name + (g.Count() > 1 ? " ×" + g.Count() : ""));
            return "持有义肢：" + string.Join("   ·   ", groups) + "   ·   " + accessories + "   ·   " + consumables;
        }

        private static string NodeDescription(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle: return "普通敌人。胜利后结算理智反馈与补给。";
                case NodeType.EliteBattle: return "更强的敌人，掉落义肢。";
                case NodeType.Event: return "面对情境并做出选择。";
                case NodeType.Altar: return "祭坛：代价换收获。";
                case NodeType.Boss: return "本层终点。胜利即通关，全队阵亡则本局结束。";
                case NodeType.Supply: return "治疗、修复部位、安装义肢、静养、管理饰品。";
                case NodeType.Elevator: return "本层到此为止，通往下一层。";
                case NodeType.Opening: return "三选一的全队增益。";
                case NodeType.Treasure: return "宝箱房：获得一件饰品（每个宝箱只发一次）。";
                default: return "";
            }
        }

        private static Color NodeColor(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle:
                case NodeType.EliteBattle:
                case NodeType.Boss: return Rust;
                case NodeType.Supply: return Cyan;
                case NodeType.Elevator: return LineColor;
                default: return Gold;
            }
        }

        private void Actions(RectTransform root)
        {
            var area = Box(root, "ActionPanel", 34, 580, 1176, 273, Panel);
            var actor = app.Battle.Current;
            string title = actor == null ? "战斗已结算" : !actor.Player ? "等待敌方行动" : actor.Name + " · 选择行动";
            Label(area, "ActionTitle", title, 20, 14, 600, 29, 20, Ink);
            Label(area, "Instruction", app.Choosing ? "点击高亮目标  /  Esc取消" : "每次行动消耗一个回合", 713, 18, 440, 24, 13, Muted, TextAnchor.MiddleRight);
            // 每一回合只有这个角色当前形态的技能，外加一个换位。高理智形态有四个常态技能
            // 加一个特殊技能，所以按钮最多六个，两行四列的网格仍然装得下。
            var skills = actor != null && actor.Player ? app.Battle.SkillsFor(actor) : ActorSpec.EmptySkills;
            for (int i = 0; i <= skills.Length; i++)
            {
                int index = i;
                bool swap = index == skills.Length;
                var skill = swap ? null : skills[index];
                string reason = swap ? app.Battle.SwapDisabledReason() : app.Battle.SkillDisabledReason(index);
                string detail = reason ?? (swap ? "与相邻队员交换位置" : SkillDetail(actor, skill));
                Button(area, swap ? "Swap" : "Skill_" + index, swap ? "换位" : skill.Name, detail,
                    20 + (index % 4) * 287, 55 + (index / 4) * 77, 275, 65,
                    swap ? (Action)(() => app.SelectSwap()) : () => app.SelectSkill(index),
                    swap ? app.PendingSwap : app.PendingSkill == index, reason == null && !app.Busy, 17);
            }
            // The item slot follows the skill grid. Using one spends this turn, so it carries the same
            // "only on our own action" gate as the skills.
            int itemSlot = skills.Length + 1;
            bool canUseItems = actor != null && actor.Player && !app.Busy;
            Button(area, "BattleConsumables", "道具", "使用一件道具并消耗本次行动",
                20 + (itemSlot % 4) * 287, 55 + (itemSlot / 4) * 77, 275, 65,
                app.ToggleConsumableEditor, false, canUseItems, 17);
            Label(area, "ActionHint", app.Hint ?? "", 20, 218, 1136, 42, 14, app.Choosing ? Gold : Muted);
        }

        private string SkillDetail(ActorState actor, SkillSpec skill)
        {
            string count = skill.Uses > 0 ? " · 剩" + actor.UsesLeft(skill) + "次" : "";
            string value = skill.Effect == SkillEffect.Attack ? "伤害 " + app.Battle.SkillValue(actor, skill) + " · "
                : skill.Effect == SkillEffect.Heal ? "恢复 " + app.Battle.SkillValue(actor, skill) + " · " : "";
            // This detail line is a single row; strip the newline that separates the position symbols
            // from the effect text so the button still shows the effect instead of only the dots.
            return value + skill.Description.Replace("\n", "，") + count;
        }

        private void Journal(RectTransform root)
        {
            // Three entries instead of five: the part rows took the vertical room the log used to have.
            var box = Box(root, "Journal", 1228, 628, 338, 225, Panel);
            Label(box, "JournalHeading", "战斗记录", 20, 15, 298, 25, 17, Ink);
            Box(box, "JournalLine", 20, 51, 298, 1, LineColor);
            var entries = app.Battle.Events.AsEnumerable().Reverse().Take(3).ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                Color c = entries[i].Kind == "damage" || entries[i].Kind == "death" ? Rust
                    : entries[i].Kind == "limb" ? Gold
                    : entries[i].Kind == "heal" || entries[i].Kind == "sanity" ? Cyan : Muted;
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
            bool wiped = app.Run.Heroes.All(a => a.Dead);
            bool boss = app.ActiveNode != null && app.ActiveNode.Type == NodeType.Boss;
            bool won = result.Outcome == BattleOutcome.Victory;
            string continuity = boss
                ? "首领是这条路的尽头：胜利即通关，全队阵亡则本局结束。"
                : won
                    ? "胜利的补给已经入账。治疗与义肢只在补给站提供，路线上不会自动整备。"
                    : "战斗失利，没有收获。治疗与义肢只在补给站提供，路线上不会自动整备。";
            Label(p, "Continuity", continuity, 40, 478, 820, 33, 13, Muted);
            Button(p, "ContinueBattle", wiped || (boss && !won) ? "结束本局" : boss ? "通关结算" : "继续前进",
                wiped ? "查看本局结果" : boss ? "查看本局结果" : "选择下一个位置",
                40, 532, 394, 56, app.ContinueBattle, true, true, 18);
            Button(p, "ResultNewRun", "新开演练", "重置全部队员", 450, 532, 410, 56, () => app.NewRun(), false, true, 18);
        }

        private void Help(RectTransform root)
        {
            var p = Modal(root, "HelpModal", 980, 838);
            Label(p, "HelpTitle", "行动指南", 34, 25, 900, 45, 29, Ink);
            string text = "01  选择技能，再点击高亮目标。点击任意角色可查看详情。\n\n" +
                "02  我方前排靠近敌人。位置不符时，可以消耗行动与相邻队友换位。\n\n" +
                "03  每回合只能使用该角色当前的技能，或与相邻队员换位。\n      部分技能每场限次；用尽或站位不符时按钮上会写明原因。\n" +
                "      技能说明第一行以 ●/○ 表示可选/不可选位置：斜杠前为己方站位，斜杠后为目标位置，\n" +
                "      均按1→4号位排列；目标阵营与限制见下一行效果说明。\n\n" +
                "04  理智低于30技能会发生变化，高于70会解锁特殊技能。\n      30至70保持常态；形态在本回合内固定，切换的是整套技能。\n      敌方低语可能带来凝神或躁动；持续状态跨战斗保留。\n\n" +
                "05  敌人卡片下方显示下一次攻击。用格挡、反击、守护、打断或换位应对。\n      奶妈可以治疗濒死队员；濒死时再次受伤会检定死亡。\n\n" +
                "06  战斗结束（胜利或全队阵亡）都会反馈玩家理智：队员平均净变化 × 0.5。\n      全队阵亡本局立即结束；结算后继续前进，理智决定候选数量与房间类型。\n\n" +
                "07  命中、暴击与闪避由角色属性直接决定，不随理智变化。\n      技能伤害 = 基础攻击力 × 技能倍率；受伤按目标防御比例减少。\n\n" +
                "08  每个队员有头部、躯干、手臂、腿部四个部位，各有独立血量。\n" +
                "      只有执斧者的重劈与追猎者的追猎会重创部位：一次重伤，两次断肢。\n" +
                "      重伤与断肢削弱对应属性（头→命中，躯干→防御，手臂→攻击，腿→速度）。\n" +
                "      补给站能修复重伤；断肢只能安装已经持有的义肢，义肢会被再次打断。\n\n" +
                "09  路线共三层：第1层从初始增益房间出发，第2、3层从补给站出发。\n" +
                "      每层最后是电梯井（第3层为首领）；层内的房间只在你走到跟前时才抽出。\n\n" +
                "10  每次给出1至3个候选，你选一个走过去；同一个位置不会重复同一种房间。\n" +
                "      低理智更容易遇到精英与祭坛，高理智更容易遇到事件房间。\n" +
                "      补给站治疗、修复并管理饰品；精英战斗掉落义肢；宝箱房发放饰品。";
            Label(p, "HelpBody", text, 38, 92, 904, 640, 18, Ink);
            Button(p, "CloseHelp", "返回", "Esc 关闭", 38, 752, 904, 52, app.ToggleHelp, true, true, 18);
        }

        // The accessory manager. It is a modal rather than an inline page because both UI systems need it:
        // the external node HUD only opens it, and this canvas draws it above everything else.
        private void AccessoryEditor(RectTransform root)
        {
            var p = Modal(root, "AccessoryModal", 1200, 800);
            Label(p, "AccessoryTitle", "管理饰品", 30, 18, 500, 40, 26, Ink);
            Label(p, "AccessoryCaption", "每名角色可装备任意数量；同一件饰品不能重复装备。专属饰品只能由对应角色使用。",
                30, 58, 1140, 24, 13, Muted);

            var hero = app.Run.Heroes.FirstOrDefault(h => h.Id == app.AccessoryHeroId)
                ?? app.Run.Heroes.FirstOrDefault(h => !h.Dead) ?? app.Run.Heroes.FirstOrDefault();
            for (int i = 0; i < app.Run.Heroes.Count && i < 4; i++)
            {
                var member = app.Run.Heroes[i];
                int worn = member.EquippedAccessories == null ? 0 : member.EquippedAccessories.Count;
                Button(p, "AccessoryHero_" + member.Id,
                    member.Name + (member.Dead ? "（阵亡）" : ""),
                    "饰品 " + worn + " 件 · 生命 " + member.Hp + "/" + member.Definition.MaxHp,
                    30 + i * 286, 94, 276, 56,
                    () => app.SelectAccessoryHero(member.Id), hero != null && hero.Id == member.Id, true, 16);
            }

            Label(p, "EquippedHeading", "已装备" + (hero == null ? "" : " · " + hero.Name), 30, 166, 555, 24, 15, Gold);
            Label(p, "StockHeading", "队伍仓库 · " + app.Run.AccessoryStock.Count + " 件", 615, 166, 555, 24, 15, Gold);

            if (hero != null)
            {
                var worn = hero.EquippedAccessories ?? new List<AccessorySpec>();
                int shown = Math.Min(6, worn.Count);
                if (worn.Count == 0) Label(p, "EquippedEmpty", "这个角色还没有装备饰品。", 30, 202, 555, 24, 13, Muted);
                for (int i = 0; i < shown; i++)
                {
                    var item = worn[i];
                    if (item == null) continue;
                    var row = Box(p, "Equipped_" + i, 30, 198 + i * 82, 555, 74, Raised);
                    Label(row, "Name", item.Name, 12, 6, 300, 24, 16, Ink);
                    Label(row, "Detail", AccessorySystem.RestrictionText(item) + " · " + item.Description,
                        12, 30, 420, 40, 11, Muted);
                    Button(row, "Unequip_" + hero.Id + "_" + item.Id, "卸下", "",
                        452, 11, 88, 52, () => app.UnequipAccessory(hero.Id, item.Id), false, true, 14);
                }
                if (worn.Count > shown)
                    Label(p, "EquippedMore", "另有 " + (worn.Count - shown) + " 件未显示。", 30, 694, 555, 20, 11, Muted);
            }

            int stockShown = Math.Min(6, app.Run.AccessoryStock.Count);
            if (app.Run.AccessoryStock.Count == 0)
                Label(p, "StockEmpty", "仓库是空的。宝箱房与部分事件会发放饰品。", 615, 202, 555, 24, 13, Muted);
            for (int i = 0; i < stockShown; i++)
            {
                var item = app.Run.AccessoryStock[i];
                if (item == null) continue;
                var row = Box(p, "Stock_" + i, 615, 198 + i * 82, 555, 74, Raised);
                Label(row, "Name", item.Name, 12, 6, 300, 24, 16, Ink);
                string blocked = hero == null ? "没有可装备的角色。" : AccessorySystem.EquipBlockedReason(hero, item);
                Label(row, "Detail", blocked ?? (AccessorySystem.RestrictionText(item) + " · " + item.Description),
                    12, 30, 420, 40, 11, blocked == null ? Muted : Rust);
                int index = i;
                Button(row, "Equip_" + i, blocked == null ? "装备" : "不可装备", "",
                    452, 11, 88, 52, () => app.EquipAccessory(hero == null ? null : hero.Id, index),
                    false, blocked == null, 14);
            }
            if (app.Run.AccessoryStock.Count > stockShown)
                Label(p, "StockMore", "另有 " + (app.Run.AccessoryStock.Count - stockShown) + " 件未显示。",
                    615, 694, 555, 20, 11, Muted);

            Button(p, "CloseAccessories", "返回", "Esc", 30, 726, 1140, 52, app.CloseAccessoryEditor, true, true, 18);
        }

        // The consumable panel. It is drawn by this canvas in both UI modes for the same reason the
        // accessory manager is: the external HUD only opens it, and the modal has to sit above both.
        private void ConsumableEditor(RectTransform root)
        {
            bool inBattle = !app.AwaitingNode && app.Battle != null;
            var p = Modal(root, "ConsumableModal", 1200, 800);
            Label(p, "ConsumableTitle", "使用道具", 30, 18, 500, 40, 26, Ink);
            Label(p, "ConsumableCaption", inBattle
                    ? "战斗中只能在我方行动时使用；每件道具消耗当前角色的本次行动。"
                    : "使用立即生效；理智锁定药剂会预置到下一场战斗。阵亡角色不能使用道具。",
                30, 58, 1140, 26, 13, Muted);

            var hero = app.Run.Heroes.FirstOrDefault(h => h.Id == app.ConsumableHeroId)
                ?? app.Run.Heroes.FirstOrDefault(h => !h.Dead) ?? app.Run.Heroes.FirstOrDefault();
            for (int i = 0; i < app.Run.Heroes.Count && i < 4; i++)
            {
                var member = app.Run.Heroes[i];
                string state = member.Dead ? "阵亡"
                    : member.SanityLocked ? "理智锁定"
                    : member.SanityLockPending ? "下场锁定"
                    : "";
                Button(p, "ConsumableHero_" + member.Id,
                    member.Name + (member.Dead ? "（阵亡）" : ""),
                    "生命 " + member.Hp + "/" + member.Definition.MaxHp + (state.Length == 0 ? "" : " · " + state),
                    30 + i * 286, 94, 276, 56,
                    () => app.SelectConsumableHero(member.Id), hero != null && hero.Id == member.Id, true, 16);
            }

            Label(p, "ConsumableStockHeading", "道具仓库 · " + app.Run.Consumables.Count + " 件",
                30, 166, 1140, 24, 15, Gold);
            if (app.Run.Consumables.Count == 0)
                Label(p, "ConsumableEmpty", "仓库是空的。可在验收工具的「发放全部道具」中领取。",
                    30, 202, 1140, 24, 13, Muted);

            int shown = Math.Min(6, app.Run.Consumables.Count);
            string heroId = hero == null ? null : hero.Id;
            for (int i = 0; i < shown; i++)
            {
                var item = app.Run.Consumables[i];
                if (item == null) continue;
                var row = Box(p, "Consumable_" + i, 30, 198 + i * 86, 1140, 74, Raised);
                Label(row, "Name", item.Name, 12, 6, 500, 24, 16, Ink);
                string blocked = ConsumableSystem.UseBlockedReason(app.Run, hero, item, inBattle);
                Label(row, "Detail", item.Description + (blocked == null ? "" : " · " + blocked),
                    12, 30, 990, 40, 11, blocked == null ? Muted : Rust);
                string label = !item.Implemented || item.Kind == ConsumableKind.SurgeryKit ? "功能待定"
                    : blocked == null ? "使用" : "不可用";
                int index = i;
                Button(row, "UseConsumable_" + i, label, "",
                    1030, 11, 94, 52, () => app.UseConsumable(heroId, index),
                    false, blocked == null, 14);
            }
            if (app.Run.Consumables.Count > shown)
                Label(p, "ConsumableMore", "另有 " + (app.Run.Consumables.Count - shown) + " 件未显示。",
                    30, 704, 1140, 20, 11, Muted);

            Button(p, "CloseConsumables", "返回", "Esc", 30, 726, 1140, 52, app.CloseConsumableEditor, true, true, 18);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Laboratory(RectTransform root)
        {
            if (app.LabEvents) { EventLab(root); return; }
            var p = Modal(root, "LabModal", 1040, 890);
            Label(p, "LabTitle", "第一阶段验收工具", 34, 23, 960, 44, 28, Ink);
            Label(p, "LabCaption", "仅开发版提供。新建队伍会重置演练；单人调整会计入本场理智净变化。", 36, 76, 968, 28, 15, Muted);
            Label(p, "PresetHeading", "新建固定理智队伍（队员与玩家理智同时设定）", 36, 128, 920, 26, 17, Gold);
            Button(p, "Preset15", "理智 15", "低理智事件分支与技能变化", 36, 171, 310, 66, () => app.NewRun(15), false, true, 20);
            Button(p, "Preset50", "理智 50", "稳定区 · 仅常态技能", 365, 171, 310, 66, () => app.NewRun(50), false, true, 20);
            Button(p, "Preset85", "理智 85", "高理智事件分支与技能解锁", 694, 171, 310, 66, () => app.NewRun(85), false, true, 20);
            var actor = app.Battle == null ? null : app.Battle.Find(app.InspectedId);
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
            Button(p, "DeathDoorExample", "濒死演练", "盾卫0生命 · 尝试救治", 365, 432, 310, 67, app.DebugDeathDoor, false, true, 18);
            Button(p, "EndBattleDebug", "结束当前演练", "查看反馈与状态延续", 694, 432, 310, 67, app.DebugSettle, false, true, 18);
            Label(p, "MapLabHeading", "地图与房间", 36, 504, 920, 26, 17, Gold);
            Button(p, "RestParty", "整备全队生命", "验收便利 · 不改理智", 36, 542, 310, 54, app.DebugRestParty, false, true, 18);
            Button(p, "SkipFloor", "跳到本层末端", "末位是电梯井或首领", 365, 542, 310, 54, app.DebugSkipToFloorEnd, false, true, 18);
            Button(p, "JumpBoss", "跳到首领层", "查看结束流程", 694, 542, 310, 54, () => app.DebugEnterFloor(app.Catalog.MapRules().Floors), false, true, 18);
            // Six buttons share this row, so each is 151 wide with a 12 px gap. The chest room has no
            // weight on the route yet, which makes this the only way to look at its page.
            Button(p, "ForceSupply", "进入补给站", "替换当前候选", 36, 602, 151, 50, () => app.DebugForceRoom(NodeType.Supply), false, true, 15);
            Button(p, "ForceEvent", "进入事件房间", "随机一条", 199, 602, 151, 50, () => app.DebugForceRoom(NodeType.Event), false, true, 15);
            Button(p, "ForceAltar", "进入祭坛房间", "替换当前候选", 362, 602, 151, 50, () => app.DebugForceRoom(NodeType.Altar), false, true, 15);
            Button(p, "ForceTreasure", "进入宝箱房", "内容待定", 525, 602, 151, 50, () => app.DebugForceRoom(NodeType.Treasure), false, true, 15);
            Button(p, "ForceElevator", "进入电梯井", "替换当前候选", 688, 602, 151, 50, () => app.DebugForceRoom(NodeType.Elevator), false, true, 15);
            Button(p, "LabEvents", "事件验收", "十条事件各钉一条", 851, 602, 151, 50, app.ToggleLabEvents, false, true, 15);
            Label(p, "LimbLabHeading", "身体与义肢（作用于选中队员，可在房间页使用）", 36, 664, 600, 26, 17, Gold);
            Button(p, "LabAccessories", "管理饰品", "", 650, 658, 170, 38, () => app.OpenAccessoryEditor(), false, true, 15);
            Button(p, "GrantAllConsumables", "发放全部道具", "", 834, 658, 170, 38, app.DebugGrantAllConsumables, false, true, 15);
            Button(p, "InjureHead", "击伤·头部", "命中 −10%", 36, 700, 227, 54, () => app.DebugInjureLimb(LimbPart.Head), false, true, 16);
            Button(p, "InjureTorso", "击伤·躯干", "防御 −20%", 283, 700, 227, 54, () => app.DebugInjureLimb(LimbPart.Torso), false, true, 16);
            Button(p, "InjureArm", "击伤·手臂", "攻击 −20%", 530, 700, 227, 54, () => app.DebugInjureLimb(LimbPart.Arm), false, true, 16);
            Button(p, "InjureLeg", "击伤·腿部", "速度 −20%", 777, 700, 227, 54, () => app.DebugInjureLimb(LimbPart.Leg), false, true, 16);
            Button(p, "SeverArm", "断肢·手臂", "两次击伤的效果", 36, 764, 227, 54, () => app.DebugSeverLimb(LimbPart.Arm), false, true, 16);
            Button(p, "SeverLeg", "断肢·腿部", "两次击伤的效果", 283, 764, 227, 54, () => app.DebugSeverLimb(LimbPart.Leg), false, true, 16);
            Button(p, "GrantAllProsthetics", "发放全部义肢", "进入补给站前先领好", 530, 764, 227, 54, app.DebugGrantAllProsthetics, false, true, 16);
            Button(p, "GrantAllAccessories", "发放全部饰品", "八件各一份", 777, 764, 227, 54, app.DebugGrantAllAccessories, false, true, 16);
            Button(p, "CloseLab", "返回", "Esc 关闭", 36, 830, 968, 52, app.ToggleLab, true, true, 18);
        }

        // The per-event page of the acceptance tool. Every script gets a button, because four of the ten
        // only show a branch inside a sanity range and two of them fight before they pay out.
        private void EventLab(RectTransform root)
        {
            var p = Modal(root, "EventLabModal", 1040, 890);
            Label(p, "EventLabTitle", "事件验收工具", 34, 23, 960, 44, 28, Ink);
            Label(p, "EventLabCaption",
                "钉住一条事件并把当前候选换成它。理智 15 / 85 两个预设同时设定队员与玩家理智，"
                + "高／低理智分支、EVT-004 的「全部喝下」和 EVT-L001 都靠它切换。", 36, 76, 968, 46, 15, Muted);

            Label(p, "EventLabBasic", "全部理智", 36, 134, 920, 26, 17, Gold);
            EventButton(p, "Force001", "废弃矿道", "EVT-001 · 挖矿循环→战斗", 36, 177, "EVT-001");
            EventButton(p, "Force002", "失控的机器", "EVT-002 · 理智判定·义体", 281, 177, "EVT-002");
            EventButton(p, "Force003", "铁门后的敲击声", "EVT-003 · 两层选择·开门", 526, 177, "EVT-003");
            EventButton(p, "Force004", "破损的药剂箱", "EVT-004 · 低理智可全喝", 771, 177, "EVT-004");
            EventButton(p, "Force005", "应急医疗室", "EVT-005 · 选人治疗·判定", 36, 271, "EVT-005");
            EventButton(p, "Force006", "安全角落休整", "EVT-006 · 守夜·打乱站位", 281, 271, "EVT-006");
            EventButton(p, "Force007", "剧情事件", "EVT-007 · 纯文案·无效果", 526, 271, "EVT-007");
            EventButton(p, "Force008", "废弃义肢工坊", "EVT-008 · 祭坛房·四选项", 771, 271, "EVT-008");

            Label(p, "EventLabSpecial", "理智限定（高 ≥ 50 / 低 < 50）", 36, 369, 920, 26, 17, Gold);
            EventButton(p, "ForceH001", "隐藏的岔道", "EVT-H001 · 高理智 · 电梯通道", 36, 412, "EVT-H001");
            EventButton(p, "ForceL001", "第五个人", "EVT-L001 · 低理智 · 两条占位", 526, 412, "EVT-L001");

            Label(p, "EventLabFight", "事件里的战斗", 36, 510, 920, 26, 17, Gold);
            Button(p, "LabWinBattle", "直接打赢", "胜利后奖励回到事件页发放", 36, 553, 478, 62, app.DebugWinBattle, false, true, 18);
            Button(p, "LabRetreatBattle", "结束当前战斗", "撤退不发事件奖励", 526, 553, 478, 62, app.DebugSettle, false, true, 18);
            Label(p, "EventLabNote",
                "钉住的事件只替换当前位置的候选，之后仍然按正常流程离开并前进。"
                + "撤退只记录结果，不发奖励：EVT-001／003／006／H001／L001 的奖励都在胜利之后发放。",
                36, 630, 968, 46, 14, Muted);
            Button(p, "CloseEventLab", "返回验收工具", "Esc 关闭", 36, 800, 968, 52, app.ToggleLabEvents, true, true, 18);
        }

        // DebugForceEvent closes the tool on its way in, so the room page is what the owner lands on.
        private void EventButton(RectTransform p, string id, string title, string detail, float x, float y, string eventId)
        {
            Button(p, id, title, detail, x, y, 233, 82, () => app.DebugForceEvent(eventId), false, true, 19);
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
