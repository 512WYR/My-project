using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// Drives the authored node pages — the route and the six rooms — from a live StageOne run, so the
    /// built-in pages never have to be drawn on top of the HUD. It only ever writes into objects that
    /// already exist: the page is redrawn on every StageOneApp refresh and the hierarchy has to survive it.
    ///
    /// Every page shares one skeleton: a general frame, an illustration block, a text block and an option
    /// block, with one action in the fixed bottom-right slot that carries the party from a finished node to
    /// the next one on the route. Pages whose content does not fit that shape say so in the option block
    /// instead of being forced into it.
    ///
    /// This is the node half only. GothicBattleHudController owns the battle page, the debug button and the
    /// debug sheet, and neither writes into the other's objects.
    /// </summary>
    public sealed class GothicNodeHudController : MonoBehaviour
    {
        [Header("Source / 数据来源")]
        public StageOneApp Source;

        [Header("Pages / 页面")]
        public GameObject PageRoot;
        public GameObject RouteRoot;
        public GameObject RunEndRoot;
        public GameObject RoomRoot;

        [Header("Route / 路线页")]
        public Text RouteHeading;
        public Text RouteStock;
        public Text RouteNote;
        public Text RouteOddsHeading;
        public Text RoutePassedHeading;
        public Text RouteCandidatesHeading;
        public Text RouteMapNote;
        public Text RouteHint;
        public Text RouteRule;
        public GothicRouteNode[] Candidates = new GothicRouteNode[4];
        public GameObject[] PassedRoots = new GameObject[8];
        public Text[] PassedNames = new Text[8];
        public Outline[] PassedAccents = new Outline[8];
        public GameObject[] OddsRoots = new GameObject[6];
        public Text[] OddsNames = new Text[6];
        public Image[] OddsFills = new Image[6];
        public Text[] OddsPercents = new Text[6];

        [Header("Run end / 结束页")]
        public Text RunEndTitle;
        public Text RunEndNote;
        public Text RunEndSummary;
        public Text RunEndStock;
        public Text RunEndParty;
        public Button RunEndButton;

        [Header("Room / 房间页")]
        public Text RoomHeading;
        public Text RoomSubtitle;
        public Text RoomTitle;
        public Text RoomBody;
        public Text RoomNote;
        public Outline RoomAccent;
        public Text OptionsHeading;
        public Text OptionsPlaceholder;
        public GothicNodeOption[] Options = new GothicNodeOption[4];
        public Text ContinueHint;
        public Button ContinueButton;
        public Text ContinueTitle;
        public Text ContinueDetail;
        public Button HelpButton;
        public Button LabButton;

        // The option block's inner box, in the 1920x1080 reference space. Slots are centred inside it, or
        // laid out from its left edge when a page needs room beside them for a note.
        const float OptionInnerLeft = 140f, OptionInnerWidth = 1640f, OptionSlotW = 385f, OptionGap = 30f;
        const float OptionRowY = 656f, OptionRowH = 190f;
        // The one place a page puts its closing action, so a finished node always continues from the same
        // spot and a window that needs a close button later has somewhere to put it.
        const float ContinueX = 1600f, ContinueY = 918f;

        private StageOneApp bound;
        private bool hooked;

        private void OnEnable() { Bind(); Sync(); }
        private void Start() { Bind(); Sync(); }
        private void OnDisable()
        {
            if (bound) bound.Changed -= Sync;
            bound = null;
        }

        private void Bind()
        {
            if (!Source) Source = FindObjectOfType<StageOneApp>();
            if (bound != Source)
            {
                if (bound) bound.Changed -= Sync;
                bound = Source;
                if (bound) bound.Changed += Sync;
            }
            if (hooked) return;
            hooked = true;
            // Wired here rather than as persistent listeners: the prefab is built in a scene that has no
            // StageOneApp yet, so only a runtime reference can reach one.
            foreach (var slot in Candidates)
            {
                if (!slot || !slot.Hit) continue;
                var captured = slot;
                slot.Hit.onClick.AddListener(() => SelectCandidate(captured));
            }
            foreach (var slot in Options)
            {
                if (!slot || !slot.Hit) continue;
                var captured = slot;
                slot.Hit.onClick.AddListener(() => SelectOption(captured));
            }
            if (ContinueButton) ContinueButton.onClick.AddListener(OnContinue);
            if (RunEndButton) RunEndButton.onClick.AddListener(() => { if (Source) Source.NewRun(); });
            if (HelpButton) HelpButton.onClick.AddListener(() => { if (Source) Source.ToggleHelp(); });
            if (LabButton) LabButton.onClick.AddListener(() => { if (Source) Source.ToggleLab(); });
        }

        // ---- input, routed straight into the existing app commands ----

        private void SelectCandidate(GothicRouteNode slot)
        {
            if (Source && slot) Source.SelectNode(slot.Index);
        }

        private void SelectOption(GothicNodeOption slot)
        {
            var app = Source;
            if (!app || !slot) return;
            switch (app.Room)
            {
                case RoomView.Event:
                    app.ChooseEventOption(slot.Index);
                    break;
                case RoomView.Buff:
                    var buffs = app.Catalog.MapRules().OpeningBuffs;
                    if (slot.Index >= 0 && slot.Index < buffs.Length) app.ChooseBuff(buffs[slot.Index]);
                    break;
                case RoomView.Supply:
                    if (slot.Index == 0) app.SupplyHeal();
                    else if (slot.Index == 1) app.SupplyCalm();
                    break;
            }
        }

        // The one action that carries the party off a finished node. The elevator is the only room whose
        // way out is not LeaveRoom, because going down is what resolves it.
        private void OnContinue()
        {
            var app = Source;
            if (!app || !app.InRoom) return;
            if (app.Room == RoomView.Elevator) app.AdvanceFloor();
            else app.LeaveRoom();
        }

        // ---- the single sync path, driven by StageOneApp.Changed ----

        public void Sync()
        {
            var app = Source;
            if (!app || app.Run == null || app.Map == null) { SetActive(PageRoot, false); return; }

            bool show = app.AwaitingNode && !app.HelpOpen && !app.LabOpen;
            bool route = show && app.Room == RoomView.Map && !app.Map.Finished && app.Map.Current != null;
            bool runEnd = show && app.Room == RoomView.Map && !route;
            bool room = show && app.Room != RoomView.Map;

            SetActive(PageRoot, route || runEnd || room);
            SetActive(RouteRoot, route);
            SetActive(RunEndRoot, runEnd);
            SetActive(RoomRoot, room);
            if (route) SyncRoute(app);
            else if (runEnd) SyncRunEnd(app);
            else if (room) SyncRoom(app);
        }

        // ---------------- route page ----------------

        private void SyncRoute(StageOneApp app)
        {
            var step = app.Map.Current;
            if (step == null) return;
            var rules = app.Catalog.MapRules();

            SetText(RouteHeading, "第 " + step.Floor + " 层  /  共 " + rules.Floors + " 层");
            SetText(RouteStock, "补给 " + app.Map.Supplies + "   ·   持有义肢 " + app.Run.Prosthetics.Count
                + (app.Map.BuffChosen ? "   ·   增益 " + OpeningBuffs.Name(app.Map.Buff) : ""));
            SetText(RouteNote, "已通过 " + app.Map.ResolvedSteps + " 个位置 · 玩家理智 " + app.Run.PlayerSanity.ToString("0.0")
                + " 同时决定候选数量与房间类型（不受角色 30/70 门槛限制）");
            SetText(RouteCandidatesHeading, "本层路线  ·  " + (step.Fixed ? "固定房间" : step.Candidates.Count + " 个候选"));
            SetText(RouteMapNote, app.MapNote ?? "");
            SetText(RouteHint, app.Hint ?? "");

            SyncOdds(app, rules);
            SyncPassed(app, step);
            SyncCandidates(app, step);
        }

        private void SyncOdds(StageOneApp app, MapRules rules)
        {
            double[] odds = MapGenerator.TypeOdds(rules, app.Run.PlayerSanity);
            var specs = MapGenerator.Resolve(rules).NodeOdds;
            int shown = 0;
            for (int i = 0; i < specs.Length && shown < OddsRoots.Length; i++)
            {
                // A type parked at weight 0 would only ever draw a 0.0% row, so it stays off the panel.
                if (specs[i].Weight <= 0) continue;
                var color = NodeAccent(specs[i].Type);
                if (OddsRoots[shown]) OddsRoots[shown].SetActive(true);
                if (OddsNames[shown]) OddsNames[shown].text = MapGenerator.Name(specs[i].Type);
                if (OddsPercents[shown])
                {
                    OddsPercents[shown].text = (odds[i] * 100).ToString("0.0") + "%";
                    OddsPercents[shown].color = color;
                }
                if (OddsFills[shown])
                {
                    OddsFills[shown].fillAmount = Mathf.Clamp01((float)odds[i]);
                    OddsFills[shown].color = color;
                }
                shown++;
            }
            for (int i = shown; i < OddsRoots.Length; i++) SetActive(OddsRoots[i], false);
        }

        // The positions already walked through on this floor, as a run of small cards. Only the ones this
        // floor actually has are shown; the slots beyond that are switched off.
        private void SyncPassed(StageOneApp app, StepState step)
        {
            var passed = new List<NodeState>();
            foreach (var s in app.Map.OnFloor(step.Floor))
                if (s.Resolved && s.Chosen != null) passed.Add(s.Chosen);

            int count = Mathf.Min(passed.Count, PassedRoots.Length);
            for (int i = 0; i < count; i++)
            {
                SetActive(PassedRoots[i], true);
                if (PassedNames[i]) PassedNames[i].text = passed[i].Name;
                if (PassedAccents[i]) { PassedAccents[i].effectColor = NodeAccent(passed[i].Type); PassedAccents[i].enabled = true; }
            }
            for (int i = count; i < PassedRoots.Length; i++) SetActive(PassedRoots[i], false);
            SetText(RoutePassedHeading, "已经过的位置  ·  " + passed.Count);
        }

        // The candidates, stacked vertically: the choice reads down the page instead of across it.
        private void SyncCandidates(StageOneApp app, StepState step)
        {
            var candidates = step.Candidates;
            int count = Mathf.Min(candidates.Count, Candidates.Length);
            for (int i = 0; i < count; i++)
            {
                Candidates[i].Index = i;
                Candidates[i].Render(candidates[i], NodeDetail(candidates[i].Type), NodeAccent(candidates[i].Type), true);
            }
            for (int i = count; i < Candidates.Length; i++) if (Candidates[i]) Candidates[i].gameObject.SetActive(false);
        }

        private void SyncRunEnd(StageOneApp app)
        {
            SetText(RunEndTitle, app.Map.Cleared ? "本局通关" : "本局结束");
            SetText(RunEndNote, app.RunEndNote ?? "冒险已经结束。");
            SetText(RunEndSummary, "到达第 " + app.Map.Floor + " 层  ·  通过 " + app.Map.ResolvedSteps
                + " 个位置  ·  玩家理智 " + app.Run.PlayerSanity.ToString("0.0"));
            SetText(RunEndStock, "补给 " + app.Map.Supplies + "  ·  " + StockText(app));
            SetText(RunEndParty, string.Join("   ·   ", app.Run.Heroes.Select(h =>
                h.Name + (h.Dead ? " 阵亡" : " " + h.Hp + "/" + h.Definition.MaxHp))));
        }

        // ---------------- room pages ----------------

        // Every room starts from the same defaults, so a page only writes what it actually differs in.
        private void SyncRoom(StageOneApp app)
        {
            HideOptions();
            SetText(OptionsHeading, "选项");
            SetOptionsPlaceholder(null, OptionInnerLeft, OptionInnerWidth);
            SetContinue("继续前进", "离开这个房间", true);
            SetText(ContinueHint, app.Hint ?? "");
            if (RoomAccent) RoomAccent.enabled = true;

            switch (app.Room)
            {
                case RoomView.Event: SyncEventRoom(app); break;
                case RoomView.Buff: SyncBuffRoom(app); break;
                case RoomView.Supply: SyncSupplyRoom(app); break;
                case RoomView.Elevator: SyncElevatorRoom(app); break;
                case RoomView.Treasure: SyncTreasureRoom(app); break;
            }
        }

        private void SyncEventRoom(StageOneApp app)
        {
            var node = app.ActiveNode;
            bool altar = node != null && node.Type == NodeType.Altar;
            SetText(RoomHeading, altar ? "祭坛房间" : "事件房间");
            SetText(RoomSubtitle, altar ? "代价换收获。选择一种处理方式。" : "面对情境并做出选择。");
            SetAccent(RoomAccent, altar ? Rust : Gold);

            var page = EventScripts.Page(node, app.Run, app.Catalog.MapRules());
            if (page == null)
            {
                SetText(RoomTitle, "空房间");
                SetText(RoomBody, "这里什么也没有留下。");
                SetText(RoomNote, app.RoomNote ?? "");
                SetOptionsPlaceholder("这个房间没有可选项。", OptionInnerLeft, OptionInnerWidth);
                SetContinue("离开", "继续前进");
                return;
            }

            SetText(RoomTitle, page.Title);
            SetText(RoomBody, page.Body ?? "");
            SetText(RoomNote, app.RoomNote ?? "");
            SetText(OptionsHeading, page.Step > 0 && page.StepCount > 0
                ? "选项　步骤 " + page.Step + "/" + page.StepCount : "选项");

            // The current step is rebuilt from the node every sync, and that read draws nothing, so a
            // re-render never re-rolls the room and never widens or narrows the choice.
            int count = Mathf.Min(page.Choices.Count, Options.Length);
            LayoutOptions(count, OptionStart(count));
            for (int i = 0; i < count; i++)
            {
                var choice = page.Choices[i];
                Options[i].Index = i;
                Options[i].Render(choice.Shown ? choice.Label : "—— 不可选 ——", choice.Detail ?? "",
                    choice.Shown ? Gold : Muted, choice.Shown && node != null && node.OptionChosen == i,
                    choice.Shown && choice.Enabled);
            }
            SetContinue(app.EventCanLeave ? "离开房间" : "先完成这一事件", "继续前进", app.EventCanLeave);
        }

        private void SyncBuffRoom(StageOneApp app)
        {
            SetText(RoomHeading, "初始增益");
            SetText(RoomSubtitle, "出发之前，队伍只来得及带走一样东西。");
            SetText(RoomTitle, "三选一的全队增益");
            SetText(RoomBody, "增益直接改写队员的基础属性，战斗引擎读的就是这些数值，所以它整局有效。\n"
                + "选定之后本次冒险不再更改。");
            SetText(RoomNote, app.RoomNote ?? "");
            SetAccent(RoomAccent, Gold);

            var buffs = app.Catalog.MapRules().OpeningBuffs;
            bool taken = app.Map.BuffChosen;
            int count = Mathf.Min(buffs.Length, Options.Length);
            LayoutOptions(count, OptionStart(count));
            for (int i = 0; i < count; i++)
            {
                var buff = buffs[i];
                bool mine = taken && app.Map.Buff == buff;
                Options[i].Index = i;
                Options[i].Render(OpeningBuffs.Name(buff), OpeningBuffs.Describe(buff), mine ? Gold : Muted, mine, !taken);
            }
            SetContinue(taken ? "离开增益房间" : "先选择一项增益", "继续前进", taken);
        }

        private void SyncSupplyRoom(StageOneApp app)
        {
            string ratio = (app.Catalog.MapRules().SupplyHealRatio * 100).ToString("0");
            SetText(RoomHeading, "补给站");
            SetText(RoomSubtitle, "治疗全队、修复重伤部位、为断肢安装义肢、静养恢复理智。每项各可用一次。");
            SetText(RoomTitle, "可以选择的事");
            SetText(RoomBody, "治疗为每名队员恢复 " + ratio + "% 生命，阵亡成员不会复活。\n"
                + "静养为玩家理智 +" + Exploration.CalmSanity.ToString("0.#") + "。");
            SetText(RoomNote, "治疗 " + (app.Map.SupplyHealed ? "已使用" : "可用")
                + "   ·   静养 " + (app.Map.SupplyCalmed ? "已使用" : "可用") + "\n" + StockText(app));
            SetAccent(RoomAccent, Cyan);

            // The two party-wide actions fit the block; the per-hero limb work does not, so it is stated
            // rather than forced in. The built-in page still offers it in StageOne.unity.
            if (Options.Length > 0 && Options[0])
            {
                Options[0].Index = 0;
                Options[0].Render(app.Map.SupplyHealed ? "治疗已使用" : "治疗全队",
                    "每名队员恢复 " + ratio + "% 生命", Cyan, app.Map.SupplyHealed, !app.Map.SupplyHealed);
            }
            if (Options.Length > 1 && Options[1])
            {
                Options[1].Index = 1;
                Options[1].Render(app.Map.SupplyCalmed ? "静养已使用" : "静养",
                    "玩家理智 +" + Exploration.CalmSanity.ToString("0.#"), Gold, app.Map.SupplyCalmed, !app.Map.SupplyCalmed);
            }
            for (int i = 2; i < Options.Length; i++) if (Options[i]) Options[i].Hide();
            LayoutOptions(2, OptionInnerLeft);
            SetOptionsPlaceholder("修复部位 / 安装义肢\n按角色与部位逐个处理，本页暂未接入。", 970f, 740f);
            SetContinue("离开补给站", "继续前进");
        }

        private void SyncElevatorRoom(StageOneApp app)
        {
            SetText(RoomHeading, "电梯井");
            SetText(RoomSubtitle, "第 " + app.Map.Floor + " 层到此为止。");
            SetText(RoomTitle, "本层路线   " + FloorSummary(app, app.Map.Floor));
            SetText(RoomBody, "下行之后不会自动整备生命，个人理智与精神状态保留。\n\n"
                + string.Join("\n", app.Run.Heroes.Select(h =>
                {
                    string limbs = LimbSystem.Summary(h);
                    return h.Name + (h.Dead ? "   阵亡" : "   生命 " + h.Hp + " / " + h.Definition.MaxHp)
                        + "   " + (limbs.Length == 0 ? "部位全部完好" : "最严重 " + limbs);
                })));
            SetText(RoomNote, StockText(app) + "\n补给 " + app.Map.Supplies
                + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0")
                + "   ·   增益 " + OpeningBuffs.Name(app.Map.Buff));
            SetAccent(RoomAccent, Muted);

            SetOptionsPlaceholder("电梯井没有选项，下行就是离开本层。", OptionInnerLeft, OptionInnerWidth);
            SetContinue("下到第 " + (app.Map.Floor + 1) + " 层", "进入下一层的补给站");
        }

        private void SyncTreasureRoom(StageOneApp app)
        {
            var node = app.ActiveNode;
            SetText(RoomHeading, "宝箱房");
            SetText(RoomSubtitle, "第 " + app.Map.Floor + " 层   ·   " + (node == null ? "未知" : node.Index + " 号位置"));
            SetText(RoomTitle, "这个房间还没有内容");
            SetText(RoomBody, "宝箱房已经进入路线，理智 50 时占房间概率的 10%，但它发什么奖励还没有定，"
                + "所以进来不发放任何东西。\n奖励确定后在这里补上即可，权重可以在 GameCatalog 资产的 MapNodeOdds 里调。");
            SetText(RoomNote, app.RoomNote ?? "");
            SetAccent(RoomAccent, Gold);

            SetOptionsPlaceholder("内容待定，暂时没有选项。", OptionInnerLeft, OptionInnerWidth);
            SetContinue("离开宝箱房", "继续前进");
        }

        // ---------------- shared page furniture ----------------

        // Centres a run of slots in the option block. Only the position changes; nothing is rebuilt.
        private static float OptionStart(int count)
        {
            if (count <= 0) return OptionInnerLeft;
            float total = count * OptionSlotW + (count - 1) * OptionGap;
            return OptionInnerLeft + (OptionInnerWidth - total) * .5f;
        }

        private void LayoutOptions(int count, float startX)
        {
            int slots = Mathf.Min(count, Options.Length);
            for (int i = 0; i < slots; i++)
            {
                if (!Options[i]) continue;
                var rect = (RectTransform)Options[i].transform;
                rect.anchoredPosition = new Vector2(startX + i * (OptionSlotW + OptionGap), -OptionRowY);
                rect.sizeDelta = new Vector2(OptionSlotW, OptionRowH);
            }
        }

        private void HideOptions()
        {
            for (int i = 0; i < Options.Length; i++) if (Options[i]) Options[i].Hide();
        }

        // The stand-in for a page that has no options, or whose options do not cover the whole block.
        private void SetOptionsPlaceholder(string text, float x, float width)
        {
            if (!OptionsPlaceholder) return;
            bool on = !string.IsNullOrEmpty(text);
            OptionsPlaceholder.gameObject.SetActive(on);
            if (!on) return;
            OptionsPlaceholder.text = text;
            var rect = (RectTransform)OptionsPlaceholder.transform;
            rect.anchoredPosition = new Vector2(x, -OptionRowY);
            rect.sizeDelta = new Vector2(width, OptionRowH);
        }

        private void SetContinue(string title, string detail) { SetContinue(title, detail, true); }

        private void SetContinue(string title, string detail, bool interactable)
        {
            SetText(ContinueTitle, title);
            SetText(ContinueDetail, detail);
            if (ContinueButton) ContinueButton.interactable = interactable;
        }

        private static void SetText(Text label, string text) { if (label) label.text = text ?? ""; }

        private static void SetActive(GameObject target, bool on) { if (target) target.SetActive(on); }

        private static void SetAccent(Outline outline, Color color)
        {
            if (!outline) return;
            outline.effectColor = color;
            outline.enabled = true;
        }

        private static string StockText(StageOneApp app)
        {
            var items = app.Run.Prosthetics;
            if (items.Count == 0) return "持有义肢：无";
            var groups = items.GroupBy(p => p.Id).Select(g => g.First().Name + (g.Count() > 1 ? " ×" + g.Count() : ""));
            return "持有义肢：" + string.Join("   ·   ", groups);
        }

        private static string FloorSummary(StageOneApp app, int floor)
        {
            var names = new List<string>();
            foreach (var step in app.Map.OnFloor(floor))
            {
                if (!step.Resolved || step.Chosen == null) continue;
                names.Add(step.Chosen.Name);
            }
            return names.Count == 0 ? "（未经过任何房间）" : string.Join("  →  ", names);
        }

        // Same wording as the built-in route page, so a candidate reads identically on either UI.
        private static string NodeDetail(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle: return "普通敌人。胜利后结算理智反馈与补给。";
                case NodeType.EliteBattle: return "更强的敌人，掉落义肢。";
                case NodeType.Event: return "面对情境并做出选择。";
                case NodeType.Altar: return "祭坛：代价换收获。";
                case NodeType.Boss: return "本层终点。胜利即通关，全队阵亡则本局结束。";
                case NodeType.Supply: return "治疗、修复部位、安装义肢、静养。";
                case NodeType.Elevator: return "本层到此为止，通往下一层。";
                case NodeType.Opening: return "三选一的全队增益。";
                case NodeType.Treasure: return "宝箱房（内容待定，进去目前不发放任何东西）。";
                default: return "";
            }
        }

        private static Color NodeAccent(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle:
                case NodeType.EliteBattle:
                case NodeType.Boss: return Rust;
                case NodeType.Supply: return Cyan;
                case NodeType.Elevator: return Muted;
                default: return Gold;
            }
        }

        // The authored palette, matching ModularHudBuilder so a page written here reads like the rest.
        private static readonly Color Gold = new Color32(219, 187, 131, 255);
        private static readonly Color Muted = new Color32(162, 144, 111, 255);
        private static readonly Color Cyan = new Color32(129, 193, 200, 255);
        private static readonly Color Rust = new Color32(206, 136, 117, 255);
    }
}
