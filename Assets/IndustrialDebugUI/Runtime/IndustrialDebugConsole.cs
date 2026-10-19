using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.IndustrialDebugUI
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// The authored acceptance console. Every widget exists in IndustrialDebugConsole.prefab; this
    /// component only writes text, enables buttons and forwards clicks into StageOneApp commands.
    /// Nothing here builds UI at runtime, and nothing opens from a function key: the on-screen
    /// "调试台" launcher is the only entrance, so a build without a keyboard still has the tool.
    /// </summary>
    public sealed class IndustrialDebugConsole : MonoBehaviour
    {
        public const int TabOverview = 0;
        public const int TabParty = 1;
        public const int TabBattle = 2;
        public const int TabMap = 3;
        public const int TabEvents = 4;
        public const int TabLimbs = 5;
        public const int TabItems = 6;
        public const int TabRules = 7;
        public const int TabLog = 8;

        private static readonly Color TabIdle = new Color(0.115f, 0.135f, 0.15f, 1f);
        private static readonly Color TabActive = new Color(0.45f, 0.36f, 0.20f, 1f);
        private static readonly Color Ink = new Color(0.90f, 0.88f, 0.83f, 1f);
        private static readonly Color Muted = new Color(0.60f, 0.65f, 0.67f, 1f);
        private static readonly Color Gold = new Color(0.82f, 0.68f, 0.44f, 1f);
        private static readonly Color Cyan = new Color(0.51f, 0.76f, 0.78f, 1f);
        private static readonly Color Rust = new Color(0.81f, 0.53f, 0.46f, 1f);

        [Header("Source / 数据源")]
        public StageOneApp Source;

        [Header("Shell / 外壳")]
        public GameObject ConsoleRoot;
        public GameObject LauncherRoot;
        public Button LauncherButton;
        public Button CloseButton;
        public Text HeaderStatus;
        public Text FooterText;

        [Header("Tabs / 页签")]
        public Button[] TabButtons = new Button[9];
        public Image[] TabBackgrounds = new Image[9];
        public GameObject[] Pages = new GameObject[9];

        [Header("Overview / 概览")]
        public Text OverviewBody;
        public Text OverviewHint;
        public Text HelpBody;

        [Header("Party / 队伍")]
        public Button[] PartySelect = new Button[4];
        public Text[] PartySelectLabels = new Text[4];
        public Text PartyBody;
        // Sanity is a slider plus a field: dragging applies on release, the field applies on "应用".
        public Slider PartySanitySlider;
        public InputField PartySanityInput;
        public Button PartySanityApply;
        public Text PartySanityNow;
        public Button[] PartyForm = new Button[3];
        public Button[] PartyHp = new Button[2];

        [Header("Run / 运行")]
        public Slider PlayerSanitySlider;
        public InputField PlayerSanityInput;
        public Button PlayerSanityApply;
        public Text PlayerSanityNow;
        public Button[] NewRunButtons = new Button[3];

        [Header("Battle / 战斗")]
        public Text BattleStage;
        public Text BattleBody;
        public Text BattleSettlement;
        public Text BattleLog;
        public Button BattleWin;
        public Button BattleRetreat;
        public Button BattleRest;
        public Button BattleDeathDoor;
        public Button BattleFeedback;
        public Slider BattleSanitySlider;
        public InputField BattleSanityInput;
        public Button BattleSanityApply;
        public Text BattleSanityNow;
        public Button[] BattleForm = new Button[3];

        [Header("Map / 地图与房间")]
        public Text MapBody;
        public Button[] JumpFloors = new Button[3];
        public Button JumpFloorEnd;
        public Button JumpBossFloor;
        public Button[] ForceRooms = new Button[9];

        [Header("Events / 事件")]
        public Text EventBody;
        public Button[] EventButtons = new Button[11];
        public Button EventWin;
        public Button EventRetreat;

        [Header("Limbs / 部位与义肢")]
        public Text LimbBody;
        public Button[] InjureButtons = new Button[4];
        public Button[] SeverButtons = new Button[2];
        public Button[] RepairButtons = new Button[4];
        public Button[] InstallButtons = new Button[4];

        [Header("Items / 物品与饰品")]
        public Text ItemBody;
        public Text EquippedText;
        public Button[] EquipButtons = new Button[8];
        public Text[] EquipLabels = new Text[8];
        public Button[] UnequipButtons = new Button[6];
        public Text[] UnequipLabels = new Text[6];
        public Button[] UseButtons = new Button[6];
        public Text[] UseLabels = new Text[6];
        public Button GrantProstheticItems;
        public Button GrantAccessoryItems;
        public Button GrantConsumableItems;

        [Header("Rules / 规则与目录")]
        public Text RulesBody;

        [Header("Log / 日志")]
        public Text LogBody;
        public Button ClearLog;

        public static IndustrialDebugConsole Instance { get; private set; }
        public static void ToggleFromHud() { if (Instance) Instance.Toggle(); }

        private StageOneApp bound;
        private bool opened;
        private bool wired;
        private int tab;
        private string heroId;
        private string lastHint;
        private float nextPoll;
        // Sliders only raise these when the user moves them; Sync writes with SetValueWithoutNotify,
        // so a value shown for information never counts as an edit of its own.
        private bool partySanityDirty;
        private bool battleSanityDirty;
        private bool playerSanityDirty;
        private readonly List<string> hintHistory = new List<string>();
        private static readonly NodeType[] ForcedRooms =
        {
            NodeType.Supply, NodeType.Event, NodeType.Altar, NodeType.Treasure, NodeType.Elevator,
            NodeType.Battle, NodeType.EliteBattle, NodeType.Boss, NodeType.Opening
        };
        private static readonly string[] EventIds =
        {
            "EVT-001", "EVT-002", "EVT-003", "EVT-004", "EVT-005", "EVT-006",
            "EVT-007", "EVT-008", "EVT-H001", "EVT-L001", ""
        };
        // Button order on both pages. SkillForm's own numeric order is Technical, Basic, Primal,
        // which reads badly in the UI, so the buttons use this explicit order instead.
        private static readonly SkillForm[] FormOrder =
        {
            SkillForm.Basic, SkillForm.Technical, SkillForm.Primal
        };

        private void Awake() { Instance = this; }
        private void OnDestroy()
        {
            if (opened) StageOneApp.DebugOverlayOpen = false;
            if (Instance == this) Instance = null;
        }
        private void OnEnable() { Bind(); Sync(); }
        private void OnDisable()
        {
            if (bound) bound.Changed -= OnChanged;
            bound = null;
        }

        private void Start()
        {
            Bind();
            Close();
        }

        private void Update()
        {
            if (!opened) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Tell StageOneApp this Escape was ours, whichever Update happens to run first.
                StageOneApp.DebugOverlayEscapeFrame = Time.frameCount;
                Close();
                return;
            }
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.15f;
            ApplySliderEdits();
            Sync();
        }

        public void Toggle() { if (opened) Close(); else Open(); }

        public void Open()
        {
            opened = true;
            StageOneApp.DebugOverlayOpen = true;
            if (ConsoleRoot) ConsoleRoot.SetActive(true);
            if (LauncherRoot) LauncherRoot.SetActive(false);
            Sync();
        }

        public void Close()
        {
            // Closing within one poll interval must not drop a slider value the user just released.
            if (opened) ApplySliderEdits(true);
            opened = false;
            StageOneApp.DebugOverlayOpen = false;
            if (ConsoleRoot) ConsoleRoot.SetActive(false);
            if (LauncherRoot) LauncherRoot.SetActive(true);
        }

        // ---- binding ----
        private void Bind()
        {
            if (!Source) Source = FindObjectOfType<StageOneApp>();
            if (bound != Source)
            {
                if (bound) bound.Changed -= OnChanged;
                bound = Source;
                if (bound) { bound.Changed += OnChanged; lastHint = bound.Hint; hintHistory.Clear(); }
            }
            if (wired) return;
            wired = true;

            Wire(LauncherButton, Toggle);
            Wire(CloseButton, Close);
            for (int i = 0; i < TabButtons.Length; i++)
            {
                int index = i;
                Wire(TabButtons[i], () => ShowTab(index));
            }

            for (int i = 0; i < PartySelect.Length; i++)
            {
                int index = i;
                Wire(PartySelect[i], () => SelectHero(index));
            }
            Wire(PartySanityApply, ApplyPartySanity);
            Wire(BattleSanityApply, ApplyBattleSanity);
            SubmitOnEnter(PartySanityInput, ApplyPartySanity);
            SubmitOnEnter(BattleSanityInput, ApplyBattleSanity);
            MarkOnChange(PartySanitySlider, () => partySanityDirty = true);
            MarkOnChange(BattleSanitySlider, () => battleSanityDirty = true);
            MarkOnChange(PlayerSanitySlider, () => playerSanityDirty = true);
            for (int i = 0; i < FormOrder.Length; i++)
            {
                var form = FormOrder[i];
                Wire(PartyForm[i], () => WithHero(id => Source.DebugSetHeroForm(id, form)));
                Wire(BattleForm[i], () => WithHero(id => Source.DebugSetHeroForm(id, form)));
            }
            Wire(PartyHp[0], () => WithHero(id => Source.DebugSetHeroHp(id, HeroMaxHp(id))));
            Wire(PartyHp[1], () => WithHero(id => Source.DebugSetHeroHp(id, 0)));

            Wire(PlayerSanityApply, ApplyPlayerSanity);
            SubmitOnEnter(PlayerSanityInput, ApplyPlayerSanity);
            // New runs take their sanity from the slider instead of fixed presets: the first button
            // uses the catalog default, the second keeps the value currently on the player slider, and
            // the third also keeps the seed, which reproduces the same route for A/B checks.
            Wire(NewRunButtons[0], () =>
            {
                if (!Source) return;
                Source.NewRun();
                heroId = null;
                Sync();
            });
            Wire(NewRunButtons[1], () =>
            {
                if (!Source || Source.Run == null) return;
                double sanity = Source.Run.PlayerSanity;
                Source.NewRun(sanity);
                heroId = null;
                Sync();
            });
            Wire(NewRunButtons[2], () =>
            {
                if (!Source || Source.Run == null) return;
                double sanity = Source.Run.PlayerSanity;
                uint seed = Source.Run.Seed;
                Source.NewRun(sanity, seed);
                heroId = null;
                Sync();
            });

            Wire(BattleWin, () => { if (Source) Source.DebugWinBattle(); });
            Wire(BattleRetreat, () => { if (Source) Source.DebugSettle(); });
            Wire(BattleRest, () => { if (Source) Source.DebugRestParty(); });
            Wire(BattleDeathDoor, () => { if (Source) Source.DebugDeathDoor(); });
            Wire(BattleFeedback, () => { if (Source) Source.DebugFeedbackExample(); });
            Wire(EventWin, () => { if (Source) Source.DebugWinBattle(); });
            Wire(EventRetreat, () => { if (Source) Source.DebugSettle(); });

            for (int i = 0; i < JumpFloors.Length; i++)
            {
                int floor = i + 1;
                Wire(JumpFloors[i], () => { if (Source) Source.DebugEnterFloor(floor); });
            }
            Wire(JumpFloorEnd, () => { if (Source) Source.DebugSkipToFloorEnd(); });
            Wire(JumpBossFloor, () =>
            {
                if (Source && Source.Catalog) Source.DebugEnterFloor(Source.Catalog.MapRules().Floors);
            });
            for (int i = 0; i < ForceRooms.Length; i++)
            {
                var type = ForcedRooms[i];
                Wire(ForceRooms[i], () => { if (Source) Source.DebugForceRoom(type); });
            }
            for (int i = 0; i < EventButtons.Length; i++)
            {
                string id = EventIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                Wire(EventButtons[i], () => { if (Source) Source.DebugForceEvent(id); });
            }

            for (int i = 0; i < InjureButtons.Length; i++)
            {
                var part = (LimbPart)i;
                Wire(InjureButtons[i], () => WithHero(id => Source.DebugInjureLimb(id, part)));
            }
            Wire(SeverButtons[0], () => WithHero(id => Source.DebugSeverLimb(id, LimbPart.Arm)));
            Wire(SeverButtons[1], () => WithHero(id => Source.DebugSeverLimb(id, LimbPart.Leg)));
            for (int i = 0; i < RepairButtons.Length; i++)
            {
                var part = (LimbPart)i;
                Wire(RepairButtons[i], () => WithHero(id => Source.DebugRepairLimb(id, part)));
            }
            for (int i = 0; i < InstallButtons.Length; i++)
            {
                var part = (LimbPart)i;
                Wire(InstallButtons[i], () =>
                {
                    if (!Source) return;
                    var item = Source.Run == null ? null
                        : Source.Run.Prosthetics.FirstOrDefault(p => p != null && p.Part == part);
                    if (item != null) WithHero(id => Source.DebugInstallProsthetic(id, item.Id));
                });
            }

            for (int i = 0; i < EquipButtons.Length; i++)
            {
                int index = i;
                Wire(EquipButtons[i], () => WithHero(id => Source.DebugEquipAccessory(id, index)));
            }
            for (int i = 0; i < UnequipButtons.Length; i++)
            {
                int index = i;
                Wire(UnequipButtons[i], () =>
                {
                    var hero = SelectedHero();
                    if (hero == null || hero.EquippedAccessories == null
                        || index >= hero.EquippedAccessories.Count) return;
                    var item = hero.EquippedAccessories[index];
                    if (item != null) Source.DebugUnequipAccessory(hero.Id, item.Id);
                });
            }
            for (int i = 0; i < UseButtons.Length; i++)
            {
                int index = i;
                Wire(UseButtons[i], () => WithHero(id => Source.DebugUseConsumable(id, index)));
            }
            Wire(GrantProstheticItems, () => { if (Source) Source.DebugGrantAllProsthetics(); });
            Wire(GrantAccessoryItems, () => { if (Source) Source.DebugGrantAllAccessories(); });
            Wire(GrantConsumableItems, () => { if (Source) Source.DebugGrantAllConsumables(); });
            Wire(ClearLog, () => { hintHistory.Clear(); SyncLog(); });

            ShowTab(TabOverview);
        }

        private static void Wire(Button button, Action action)
        {
            if (!button || action == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }

        private static void MarkOnChange(Slider slider, Action action)
        {
            if (!slider || action == null) return;
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(_ => action());
        }

        private static void SubmitOnEnter(InputField field, Action action)
        {
            if (!field || action == null) return;
            field.onEndEdit.RemoveAllListeners();
            field.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) action();
            });
        }

        // The slider writes its value while the user drags, but applying on every intermediate value
        // would flood the hint history and re-roll battle state dozens of times. Instead the drag is
        // applied once, after the mouse is released (or via Enter / 应用 in the field).
        private void ApplySliderEdits(bool force = false)
        {
            if (Source == null || Source.Run == null) return;
            if (!force && Input.GetMouseButton(0)) return;
            var hero = SelectedHero();
            if (partySanityDirty)
            {
                partySanityDirty = false;
                if (hero != null && PartySanitySlider)
                    Source.DebugSetHeroSanity(hero.Id, PartySanitySlider.value);
            }
            if (battleSanityDirty)
            {
                battleSanityDirty = false;
                if (hero != null && BattleSanitySlider && Source.Battle != null && !Source.AwaitingNode
                    && Source.Battle.Outcome == BattleOutcome.Active)
                    Source.DebugSetHeroSanity(hero.Id, BattleSanitySlider.value);
            }
            if (playerSanityDirty)
            {
                playerSanityDirty = false;
                if (PlayerSanitySlider) Source.DebugSetPlayerSanity(PlayerSanitySlider.value);
            }
        }

        private void ApplyPartySanity()
        {
            partySanityDirty = false;
            ApplySanityField(PartySanityInput,
                value => WithHero(id => Source.DebugSetHeroSanity(id, value)));
        }

        private void ApplyBattleSanity()
        {
            battleSanityDirty = false;
            ApplySanityField(BattleSanityInput,
                value => WithHero(id => Source.DebugSetHeroSanity(id, value)));
        }

        private void ApplyPlayerSanity()
        {
            playerSanityDirty = false;
            ApplySanityField(PlayerSanityInput,
                value => { if (Source) Source.DebugSetPlayerSanity(value); });
        }

        private void ApplySanityField(InputField field, Action<double> apply)
        {
            if (!field || apply == null) return;
            string raw = (field.text ?? "").Trim();
            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.TryParse(raw, out value))
            {
                if (Source) Source.DebugNote("「" + raw + "」不是有效数值；请输入 0 到 100。");
                Sync();
                return;
            }
            apply(value);
            Sync();
        }

        private void OnChanged()
        {
            CaptureHint();
            if (opened) Sync();
        }

        private void CaptureHint()
        {
            if (bound == null) return;
            string hint = bound.Hint;
            if (string.IsNullOrEmpty(hint) || hint == lastHint) return;
            lastHint = hint;
            hintHistory.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + hint);
            if (hintHistory.Count > 80) hintHistory.RemoveAt(0);
        }

        private void SelectHero(int index)
        {
            if (Source == null || Source.Run == null || index >= Source.Run.Heroes.Count) return;
            heroId = Source.Run.Heroes[index].Id;
            Source.DebugInspect(heroId);
            Sync();
        }

        private void WithHero(Action<string> action)
        {
            var hero = SelectedHero();
            if (hero == null || Source == null) return;
            // Keep the game's shared inspector on the same hero, so detail panels and the legacy
            // inspector-targeted commands all describe the person the console is acting on.
            if (Source.InspectedId != hero.Id) Source.DebugInspect(hero.Id);
            action(hero.Id);
            Sync();
        }

        private int HeroMaxHp(string id)
        {
            if (Source == null || Source.Run == null) return 1;
            var hero = Source.Run.Heroes.FirstOrDefault(h => h.Id == id);
            return hero == null || hero.Definition == null ? 1 : hero.Definition.MaxHp;
        }

        private ActorState SelectedHero()
        {
            if (Source == null || Source.Run == null || Source.Run.Heroes.Count == 0) return null;
            var hero = Source.Run.Heroes.FirstOrDefault(h => h.Id == heroId)
                ?? Source.Run.Heroes.FirstOrDefault(h => h.Id == Source.InspectedId)
                ?? Source.Run.Heroes[0];
            if (hero != null) heroId = hero.Id;
            return hero;
        }

        private void ShowTab(int index)
        {
            tab = Mathf.Clamp(index, 0, Pages.Length - 1);
            for (int i = 0; i < Pages.Length; i++)
            {
                if (Pages[i]) Pages[i].SetActive(i == tab);
                if (TabBackgrounds[i]) TabBackgrounds[i].color = i == tab ? TabActive : TabIdle;
            }
            Sync();
        }

        // ---- sync ----
        private void Sync()
        {
            var app = Source;
            if (app == null || app.Run == null)
            {
                if (HeaderStatus) HeaderStatus.text = "等待 StageOneApp…";
                if (FooterText) FooterText.text = "场景里找不到 StageOneApp。请确认调试台与 StageOne 在同一场景。";
                return;
            }
            if (HeaderStatus)
                HeaderStatus.text = PageName(app) + "   ·   第 " + app.Run.Map.Floor + " 层"
                    + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0")
                    + "   ·   种子 " + app.Run.Seed;
            if (FooterText) FooterText.text = "提示：" + (app.Hint ?? "—");

            SyncOverview(app);
            SyncParty(app);
            SyncBattle(app);
            SyncMap(app);
            SyncEvents(app);
            SyncLimbs(app);
            SyncItems(app);
            SyncRules(app);
            SyncLog();
        }

        private static string PageName(StageOneApp app)
        {
            if (app.Run == null) return "未开局";
            if (app.Battle != null && !app.AwaitingNode) return "战斗";
            if (app.Map != null && app.Map.Finished)
                return app.Map.Cleared ? "本局结束 · 通关" : "本局结束 · 失败";
            switch (app.Room)
            {
                case RoomView.Buff: return "增益房";
                case RoomView.Supply: return "补给站";
                case RoomView.Event:
                    return app.ActiveNode != null && app.ActiveNode.Type == NodeType.Altar ? "祭坛房" : "事件房";
                case RoomView.Elevator: return "电梯井";
                case RoomView.Treasure: return "宝箱房";
                default: return "路线";
            }
        }

        private void SyncOverview(StageOneApp app)
        {
            var run = app.Run;
            var map = app.Map;
            var text = new StringBuilder();
            text.AppendLine("【运行状态】");
            text.AppendLine("页面          " + PageName(app));
            text.AppendLine("种子          " + run.Seed + "      玩家理智  " + run.PlayerSanity.ToString("0.0") + " / 100");
            text.AppendLine("层 / 位置     第 " + map.Floor + " 层 · 第 " + (map.StepIndex + 1) + " / "
                + MapGenerator.TotalSteps(app.Catalog.MapRules()) + " 步 · 已通过 " + map.ResolvedSteps + " 步");
            text.AppendLine("补给          " + map.Supplies + "      战斗场次  " + run.Battles
                + "      本局状态  " + (map.Finished ? (map.Cleared ? "已通关" : "已结束") : "进行中"));
            text.AppendLine("初始增益      " + (map.BuffChosen
                ? OpeningBuffs.Name(map.Buff) + "（" + OpeningBuffs.Describe(map.Buff) + "）"
                : "尚未选择"));
            text.AppendLine("本场房间      " + RoomDetail(app));
            text.AppendLine();
            text.AppendLine("【队伍】");
            for (int i = 0; i < run.Heroes.Count; i++)
            {
                var hero = run.Heroes[i];
                text.AppendLine("  #" + hero.Rank + " " + hero.Name + "   生命 " + hero.Hp + " / " + MaxHp(hero)
                    + "   理智 " + hero.Sanity.ToString("0.0")
                    + "   部位 " + LimbSummary(hero) + (hero.Dead ? "   [阵亡]" : ""));
            }
            text.AppendLine();
            text.AppendLine("【库存】");
            text.AppendLine("  义肢 " + run.Prosthetics.Count + "   饰品 " + run.AccessoryStock.Count
                + "   道具 " + run.Consumables.Count);
            text.AppendLine("  义肢：" + StockNames(run.Prosthetics));
            text.AppendLine("  饰品：" + StockNames(run.AccessoryStock));
            text.AppendLine("  道具：" + (run.Consumables.Count == 0 ? "无" : ConsumableSystem.Summary(run)));
            if (OverviewBody) OverviewBody.text = text.ToString();

            if (OverviewHint)
                OverviewHint.text = "当前" + PageName(app) + "：\n" + (app.Hint ?? "—")
                    + (string.IsNullOrEmpty(app.RoomNote) ? "" : "\n房间记录：" + app.RoomNote)
                    + (string.IsNullOrEmpty(app.MapNote) ? "" : "\n路线记录：" + app.MapNote)
                    + (string.IsNullOrEmpty(app.RunEndNote) ? "" : "\n本局记录：" + app.RunEndNote);

            if (HelpBody) HelpBody.text = HelpText;
        }

        private static string RoomDetail(StageOneApp app)
        {
            if (app.AwaitingNode && app.Room == RoomView.Map) return "路线选择";
            switch (app.Room)
            {
                case RoomView.Buff:
                    return "增益房 · " + (app.Run.Map.BuffChosen ? "已选择" : "等待选择");
                case RoomView.Supply:
                    return "补给站 · 治疗 " + Yes(app.Run.Map.SupplyHealed)
                        + " · 静养 " + Yes(app.Run.Map.SupplyCalmed);
                case RoomView.Event:
                    if (app.ActiveNode == null) return "事件";
                    return (app.ActiveNode.Type == NodeType.Altar ? "祭坛" : "事件")
                        + " " + (app.ActiveNode.EventId ?? "未抽取")
                        + " · 步骤 " + app.ActiveNode.EventStep + " · 可离开 " + Yes(app.EventCanLeave);
                case RoomView.Elevator: return "电梯井 · 按「下到下一层」继续";
                case RoomView.Treasure: return "宝箱房 · 饰品已放入仓库";
                default: return "战斗";
            }
        }

        private void SyncParty(StageOneApp app)
        {
            var run = app.Run;
            var hero = SelectedHero();
            for (int i = 0; i < PartySelect.Length; i++)
            {
                if (i >= run.Heroes.Count)
                {
                    if (PartySelect[i]) PartySelect[i].gameObject.SetActive(false);
                    continue;
                }
                if (PartySelect[i]) PartySelect[i].gameObject.SetActive(true);
                var member = run.Heroes[i];
                if (PartySelectLabels[i])
                {
                    PartySelectLabels[i].text = member.Name + (member.Dead ? "（阵亡）" : "")
                        + "  " + member.Hp + "/" + MaxHp(member);
                    PartySelectLabels[i].color = hero != null && hero.Id == member.Id ? Gold : Ink;
                }
            }
            SyncPlayerSanity(app);
            SyncHeroSanity(hero, PartySanitySlider, PartySanityInput, PartySanityApply,
                PartySanityNow, PartyForm, true, partySanityDirty);
            SetInteractable(NewRunButtons, true);
            if (PartyBody == null) return;
            if (hero == null) { PartyBody.text = "没有队员。"; return; }
            var definition = hero.Definition;
            var text = new StringBuilder();
            text.AppendLine("#" + hero.Rank + "  " + hero.Name + "   " + (hero.Dead ? "[阵亡]" : "[存活]")
                + "   角色 id：" + hero.Id);
            if (definition != null)
            {
                text.AppendLine("副标题        " + definition.Subtitle + "   剪影 " + definition.Silhouette);
                text.AppendLine("生命          " + hero.Hp + " / " + definition.MaxHp
                    + "      理智  " + hero.Sanity.ToString("0.0"));
                text.AppendLine("速度 " + definition.MinSpeed + "–" + definition.MaxSpeed
                    + "（本回合 " + (hero.SpeedRoll > 0 ? hero.SpeedRoll : definition.Speed) + "）"
                    + "   攻击 " + definition.Attack
                    + "   命中 " + definition.Accuracy.ToString("0.00")
                    + "   暴击 " + definition.CritChance.ToString("0.00")
                    + "   闪避 " + definition.Dodge.ToString("0.00")
                    + "   防御 " + definition.Defense.ToString("0.00"));
            }
            text.AppendLine("形态          " + FormName(hero.Form) + "      范围 等级 " + hero.Rank + " · 宽度 " + hero.Width);
            text.AppendLine("部位          " + LimbDetail(hero));
            text.AppendLine("义肢          " + ProstheticDetail(hero));
            text.AppendLine("饰品          " + AccessoryDetail(hero));
            text.AppendLine("战斗状态      " + BattleFlags(hero));
            text.AppendLine();
            text.AppendLine("技能（当前形态 " + FormName(hero.Form) + "）");
            if (definition != null)
            {
                var skills = definition.SkillsFor(hero.Form) ?? ActorSpec.EmptySkills;
                if (skills.Length == 0) text.AppendLine("  （无）");
                for (int i = 0; i < skills.Length; i++)
                {
                    var skill = skills[i];
                    text.AppendLine("  [" + i + "] " + skill.Name + "  ·  " + skill.Effect
                        + (skill.Uses > 0 ? " · 剩余 " + hero.UsesLeft(skill) + "/" + skill.Uses : ""));
                    text.AppendLine("      " + skill.Description);
                }
            }
            PartyBody.text = text.ToString();
        }

        private void SyncBattle(StageOneApp app)
        {
            var battle = app.Battle;
            bool live = battle != null && !app.AwaitingNode;
            bool active = live && battle.Outcome == BattleOutcome.Active;
            if (BattleStage)
                BattleStage.text = live
                    ? "进行中 · 第 " + battle.Round + " 回合 · 行动者 "
                        + (battle.Current == null ? "—" : battle.Current.Name)
                    : "当前不在战斗中";
            if (BattleBody)
            {
                var text = new StringBuilder();
                if (!live)
                {
                    text.AppendLine("当前不在战斗中。");
                    text.AppendLine("用「地图与房间」页强制进入一场战斗，或直接跳到首领层。");
                }
                else
                {
                    text.AppendLine("结果          " + OutcomeName(battle.Outcome)
                        + "      回合 " + battle.Round + "      行动者 "
                        + (battle.Current == null ? "—" : battle.Current.Name + (battle.Current.Player ? "（我方）" : "（敌方）")));
                    text.AppendLine("行动顺序      " + string.Join(" → ", battle.TurnPreview.Select(a => a.Name)));
                    text.AppendLine();
                    text.AppendLine("【我方】");
                    foreach (var hero in app.Run.Heroes)
                        text.AppendLine("  " + HeroBattleLine(hero));
                    text.AppendLine("【敌方】");
                    if (battle.Enemies.Count == 0) text.AppendLine("  （无）");
                    foreach (var enemy in battle.Enemies)
                        text.AppendLine("  " + EnemyBattleLine(app, battle, enemy));
                    text.AppendLine();
                    text.AppendLine("【我方属性修正】");
                    foreach (var hero in app.Run.Heroes)
                        if (hero.Definition != null)
                            text.AppendLine("  " + hero.Name + "  攻 " + attack(hero) + "  防 " + defense(hero)
                                + "  速 " + speed(hero) + "  命 " + accuracy(hero));
                    text.AppendLine();
                    text.AppendLine("【最近事件】");
                    foreach (var entry in battle.Events.Skip(Math.Max(0, battle.Events.Count - 12)))
                        text.AppendLine("  [" + entry.Kind + "] " + entry.Message);
                }
                BattleBody.text = text.ToString();
            }
            if (BattleSettlement)
            {
                var settlement = battle == null ? null : battle.Settlement;
                if (settlement == null)
                    BattleSettlement.text = live ? "本场尚未结算。" : "没有可显示的结算。";
                else
                {
                    var text = new StringBuilder();
                    text.AppendLine("结果 " + OutcomeName(settlement.Outcome)
                        + "   开始 " + settlement.Before.ToString("0.0")
                        + "   原始变化 " + settlement.RawDelta.ToString("0.0")
                        + "   实际变化 " + settlement.AppliedDelta.ToString("0.0")
                        + "   结束 " + settlement.After.ToString("0.0"));
                    foreach (var entry in settlement.Entries)
                        text.AppendLine("  " + entry.Name + "  " + entry.Start.ToString("0.0") + " → "
                            + entry.End.ToString("0.0") + "  (" + entry.Delta.ToString("0.0") + ")"
                            + (entry.Dead ? "  [阵亡]" : ""));
                    BattleSettlement.text = text.ToString();
                }
            }
            if (BattleLog)
                BattleLog.text = live
                    ? string.Join("\n", battle.Events.Skip(Math.Max(0, battle.Events.Count - 8))
                        .Select(e => "[" + e.Kind + "] " + e.Message))
                    : "（战斗外）";
            SetInteractable(BattleWin, active);
            SetInteractable(BattleRetreat, active);
            SetInteractable(BattleRest, active);
            SetInteractable(BattleDeathDoor, active);
            SetInteractable(BattleFeedback, app.Run != null);
            SyncHeroSanity(SelectedHero(), BattleSanitySlider, BattleSanityInput, BattleSanityApply,
                BattleSanityNow, BattleForm, active, battleSanityDirty);
        }

        // Keeps a sanity slider/field pair showing the live value without fighting the user: the
        // slider is left alone while the mouse is held, and the field is left alone while focused.
        private void SyncHeroSanity(ActorState hero, Slider slider, InputField input, Button apply,
            Text now, Button[] formButtons, bool enabled, bool sliderDirty)
        {
            bool has = hero != null;
            if (slider)
            {
                slider.interactable = has && enabled;
                if (has && !sliderDirty && !Input.GetMouseButton(0))
                    slider.SetValueWithoutNotify((float)hero.Sanity);
            }
            if (input)
            {
                input.interactable = has && enabled;
                if (!input.isFocused) input.text = has ? hero.Sanity.ToString("0.0") : "";
            }
            SetInteractable(apply, has && enabled);
            SetInteractable(formButtons, has && enabled);
            if (now)
            {
                if (!has) now.text = "无队员";
                else if (slider && (sliderDirty || Input.GetMouseButton(0)))
                    now.text = "拖动 " + slider.value.ToString("0") + " / 100";
                else now.text = "当前 " + hero.Sanity.ToString("0.0") + " / 100";
            }
            if (formButtons == null) return;
            for (int i = 0; i < formButtons.Length && i < FormOrder.Length; i++)
            {
                var label = formButtons[i] ? formButtons[i].GetComponentInChildren<Text>(true) : null;
                if (label)
                    label.text = (has && hero.Form == FormOrder[i] ? "▶ " : "") + FormLabel(FormOrder[i]);
            }
        }

        private void SyncPlayerSanity(StageOneApp app)
        {
            if (PlayerSanitySlider)
            {
                PlayerSanitySlider.interactable = true;
                if (!playerSanityDirty && !Input.GetMouseButton(0))
                    PlayerSanitySlider.SetValueWithoutNotify((float)app.Run.PlayerSanity);
            }
            if (PlayerSanityInput && !PlayerSanityInput.isFocused)
                PlayerSanityInput.text = app.Run.PlayerSanity.ToString("0.0");
            SetInteractable(PlayerSanityApply, true);
            if (PlayerSanityNow)
                PlayerSanityNow.text = PlayerSanitySlider && (playerSanityDirty || Input.GetMouseButton(0))
                    ? "拖动 " + PlayerSanitySlider.value.ToString("0") + " / 100"
                    : "当前 " + app.Run.PlayerSanity.ToString("0.0") + " / 100";
        }

        private string HeroBattleLine(ActorState hero)
        {
            return "#" + hero.Rank + " " + hero.Name + "  HP " + hero.Hp + "/" + MaxHp(hero)
                + "  理智 " + hero.Sanity.ToString("0.0")
                + "  形态 " + FormName(hero.Form) + "  " + BattleFlags(hero);
        }

        private static string EnemyBattleLine(StageOneApp app, BattleEngine battle, ActorState enemy)
        {
            string intent;
            try { intent = battle.Intent(enemy); }
            catch { intent = "—"; }
            return "#" + enemy.Rank + " " + enemy.Name + "  HP " + enemy.Hp + "/" + MaxHp(enemy)
                + (enemy.Dead ? "  [阵亡]" : "") + "\n      意图：" + intent;
        }

        private void SyncMap(StageOneApp app)
        {
            var map = app.Map;
            var rules = app.Catalog.MapRules();
            if (MapBody)
            {
                var text = new StringBuilder();
                text.AppendLine("第 " + map.Floor + " 层 · 步 " + (map.StepIndex + 1) + " / "
                    + MapGenerator.TotalSteps(rules) + " · 已通过 " + map.ResolvedSteps + " 步");
                text.AppendLine("本层采样位置：" + (MapGenerator.StepsOnFloor(rules, map.Floor)));
                text.AppendLine("补给 " + map.Supplies + " · 初始增益 " + (map.BuffChosen
                    ? OpeningBuffs.Name(map.Buff) : "未选择"));
                text.AppendLine("当前候选：");
                var step = map.Current;
                if (step == null || step.Candidates.Count == 0) text.AppendLine("  （无）");
                else
                {
                    for (int i = 0; i < step.Candidates.Count; i++)
                    {
                        var node = step.Candidates[i];
                        text.AppendLine("  [" + i + "] " + MapGenerator.Name(node.Type)
                            + (step.ChosenIndex == i ? "   ← 已选择" : ""));
                    }
                    text.AppendLine("  固定位置：" + (step.Fixed ? "是" : "否")
                        + "   已解决：" + (step.Resolved ? "是" : "否"));
                }
                text.AppendLine();
                text.AppendLine("【路线】");
                foreach (var stepState in map.Steps)
                {
                    string chosen = stepState.Chosen == null ? "—" : MapGenerator.Name(stepState.Chosen.Type);
                    text.AppendLine("  第 " + stepState.Floor + " 层 · 位置 " + (stepState.Index + 1)
                        + "  候选 " + stepState.Candidates.Count
                        + "  选择 " + chosen
                        + (stepState.Resolved ? "  [已通过]" : "")
                        + (map.StepIndex == map.Steps.IndexOf(stepState) ? "  ← 当前" : ""));
                }
                MapBody.text = text.ToString();
            }
            bool canJump = app.Run != null && !map.Finished;
            for (int i = 0; i < JumpFloors.Length; i++)
            {
                bool usable = canJump && i < rules.Floors;
                SetInteractable(JumpFloors[i], usable);
                var label = JumpFloors[i] ? JumpFloors[i].GetComponentInChildren<Text>(true) : null;
                if (label) label.text = "跳到第 " + (i + 1) + " 层" + (usable ? "" : "（不存在）");
            }
            SetInteractable(JumpFloorEnd, canJump);
            SetInteractable(JumpBossFloor, canJump);
            SetInteractable(ForceRooms, canJump);
        }

        private void SyncEvents(StageOneApp app)
        {
            var node = app.ActiveNode;
            if (EventBody)
            {
                var text = new StringBuilder();
                if (app.Room != RoomView.Event || node == null)
                {
                    text.AppendLine("当前不在事件/祭坛房间。");
                    text.AppendLine("点击下方任意事件按钮，会把当前位置的候选替换成该事件并立即进入；");
                    text.AppendLine("其中 EVT-008 以祭坛房进入。EVT-H001 / EVT-L001 仍然可以强制进入，方便检查理智分支。");
                }
                else
                {
                    var page = EventScripts.Page(node, app.Run, app.Catalog.MapRules());
                    text.AppendLine("事件          " + (node.EventId ?? "—") + "  " + EventScripts.Title(node.EventId));
                    text.AppendLine("类型          " + (node.Type == NodeType.Altar ? "祭坛房" : "事件房"));
                    text.AppendLine("步骤          " + node.EventStep + " / " + page.StepCount
                        + "    次数 " + node.EventCount + " / " + node.EventLimit
                        + "    目标 " + (string.IsNullOrEmpty(node.EventTarget) ? "—" : node.EventTarget));
                    text.AppendLine("标志          " + node.EventFlag + "    跳跃 " + Yes(node.EventJump)
                        + "    可离开 " + Yes(page.CanLeave));
                    text.AppendLine();
                    text.AppendLine(page.Title);
                    text.AppendLine(page.Body);
                    if (!string.IsNullOrEmpty(page.Hint)) text.AppendLine("提示：" + page.Hint);
                    text.AppendLine();
                    text.AppendLine("选项：");
                    if (page.Choices.Count == 0) text.AppendLine("  （无）");
                    for (int i = 0; i < page.Choices.Count; i++)
                    {
                        var choice = page.Choices[i];
                        if (!choice.Shown) continue;
                        text.AppendLine("  [" + i + "] " + choice.Label
                            + (choice.Enabled ? "" : "（不可用）"));
                        if (!string.IsNullOrEmpty(choice.Detail)) text.AppendLine("      " + choice.Detail);
                    }
                }
                EventBody.text = text.ToString();
            }
            for (int i = 0; i < EventButtons.Length; i++)
            {
                if (!EventButtons[i]) continue;
                bool has = !string.IsNullOrEmpty(EventIds[i]);
                EventButtons[i].gameObject.SetActive(has);
                SetInteractable(EventButtons[i], has && app.Run != null && !app.Map.Finished);
            }
            bool active = app.Battle != null && !app.AwaitingNode && app.Battle.Outcome == BattleOutcome.Active;
            SetInteractable(EventWin, active);
            SetInteractable(EventRetreat, active);
        }

        private void SyncLimbs(StageOneApp app)
        {
            var hero = SelectedHero();
            if (LimbBody)
            {
                var text = new StringBuilder();
                if (hero == null) text.AppendLine("没有队员。");
                else
                {
                    text.AppendLine("选中队员：" + hero.Name + "（在「队伍」页切换）");
                    foreach (LimbPart part in Enum.GetValues(typeof(LimbPart)))
                    {
                        var limb = LimbSystem.Get(hero, part);
                        text.AppendLine("  " + LimbSystem.PartName(part) + "  "
                            + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part))
                            + (limb == null ? "" : "  HP " + limb.Hp + " / " + limb.MaxHp)
                            + (limb == null || string.IsNullOrEmpty(limb.Prosthetic)
                                ? "" : "  义肢 " + limb.Prosthetic));
                    }
                    text.AppendLine();
                    text.AppendLine("义肢库存：" + StockNames(app.Run.Prosthetics));
                    text.AppendLine("命中系数 " + LimbSystem.AccuracyFactor(hero, app.Catalog.Rules().Limbs).ToString("0.00")
                        + "  防御系数 " + LimbSystem.DefenseFactor(hero, app.Catalog.Rules().Limbs).ToString("0.00")
                        + "  攻击系数 " + LimbSystem.AttackFactor(hero, app.Catalog.Rules().Limbs).ToString("0.00")
                        + "  速度系数 " + LimbSystem.SpeedFactor(hero, app.Catalog.Rules().Limbs).ToString("0.00"));
                }
                LimbBody.text = text.ToString();
            }
            bool hasHero = hero != null;
            for (int i = 0; i < RepairButtons.Length; i++)
            {
                var part = (LimbPart)i;
                bool usable = hasHero && LimbSystem.CanRepair(hero, part);
                SetInteractable(RepairButtons[i], usable);
                var label = RepairButtons[i] ? RepairButtons[i].GetComponentInChildren<Text>(true) : null;
                if (label) label.text = "修复·" + LimbSystem.PartName(part) + (usable ? "" : "（非重伤）");
            }
            SetInteractable(InjureButtons, hasHero);
            SetInteractable(SeverButtons, hasHero);
            for (int i = 0; i < InstallButtons.Length; i++)
            {
                var part = (LimbPart)i;
                var item = hasHero ? app.Run.Prosthetics.FirstOrDefault(p => p != null && p.Part == part) : null;
                SetInteractable(InstallButtons[i], hasHero && item != null && LimbSystem.CanInstall(hero, item));
                var label = InstallButtons[i] ? InstallButtons[i].GetComponentInChildren<Text>(true) : null;
                if (label) label.text = item == null
                    ? "安装·" + LimbSystem.PartName(part) + "（无义肢）"
                    : "安装·" + LimbSystem.PartName(part) + " · " + item.Name;
            }
        }

        private void SyncItems(StageOneApp app)
        {
            var run = app.Run;
            var hero = SelectedHero();
            if (ItemBody)
            {
                var text = new StringBuilder();
                text.AppendLine("义肢库存 " + run.Prosthetics.Count);
                if (run.Prosthetics.Count == 0) text.AppendLine("  （无）");
                foreach (var item in run.Prosthetics)
                    if (item != null)
                        text.AppendLine("  " + item.Name + "  ·  " + LimbSystem.PartName(item.Part)
                            + "  ·  " + item.Description);
                text.AppendLine();
                text.AppendLine("饰品库存 " + run.AccessoryStock.Count);
                if (run.AccessoryStock.Count == 0) text.AppendLine("  （无）");
                foreach (var item in run.AccessoryStock)
                    if (item != null)
                        text.AppendLine("  " + item.Name + "  ·  " + AccessorySystem.RestrictionText(item)
                            + "  ·  " + item.Description);
                text.AppendLine();
                text.AppendLine("道具库存 " + run.Consumables.Count);
                if (run.Consumables.Count == 0) text.AppendLine("  （无）");
                foreach (var item in run.Consumables)
                    if (item != null)
                        text.AppendLine("  " + item.Name + (item.Implemented ? "" : "（功能待定）")
                            + "  ·  " + item.Description);
                ItemBody.text = text.ToString();
            }
            if (EquippedText)
            {
                var text = new StringBuilder();
                if (hero == null) text.AppendLine("没有队员。");
                else
                {
                    text.AppendLine("选中队员：" + hero.Name);
                    var worn = hero.EquippedAccessories ?? new List<AccessorySpec>();
                    if (worn.Count == 0) text.AppendLine("  （未装备）");
                    for (int i = 0; i < worn.Count; i++)
                        if (worn[i] != null)
                        text.AppendLine("  [" + i + "] " + worn[i].Name + "  ·  "
                            + AccessorySystem.RestrictionText(worn[i]));
                }
                EquippedText.text = text.ToString();
            }
            for (int i = 0; i < EquipButtons.Length; i++)
            {
                var item = i < run.AccessoryStock.Count ? run.AccessoryStock[i] : null;
                SetInteractable(EquipButtons[i], hero != null && item != null
                    && AccessorySystem.EquipBlockedReason(hero, item) == null);
                if (EquipLabels[i])
                    EquipLabels[i].text = item == null ? "—" : "装备 " + item.Name;
            }
            var equipped = hero == null ? null : hero.EquippedAccessories;
            for (int i = 0; i < UnequipButtons.Length; i++)
            {
                bool has = equipped != null && i < equipped.Count && equipped[i] != null;
                SetInteractable(UnequipButtons[i], has);
                if (UnequipLabels[i])
                    UnequipLabels[i].text = has ? "卸下 " + equipped[i].Name : "—";
            }
            // Outside battle the stock is used directly. In battle the engine only accepts an item on the
            // player's turn, so mirror that here instead of offering a button that will always refuse.
            bool inBattle = app.Battle != null && !app.AwaitingNode;
            bool canAct = !inBattle || (app.Battle.Outcome == BattleOutcome.Active
                && app.Battle.Current != null && app.Battle.Current.Player && !app.Busy);
            for (int i = 0; i < UseButtons.Length; i++)
            {
                var item = i < run.Consumables.Count ? run.Consumables[i] : null;
                bool usable = hero != null && item != null && canAct
                    && ConsumableSystem.UseBlockedReason(run, hero, item, inBattle) == null;
                SetInteractable(UseButtons[i], usable);
                if (UseLabels[i])
                    UseLabels[i].text = item == null ? "—" : "对 " + (hero == null ? "队员" : hero.Name) + " 使用 " + item.Name;
            }
            SetInteractable(GrantProstheticItems, run != null);
            SetInteractable(GrantAccessoryItems, run != null);
            SetInteractable(GrantConsumableItems, run != null);
        }

        private void SyncRules(StageOneApp app)
        {
            if (RulesBody == null || app.Catalog == null) return;
            var rules = app.Catalog.MapRules();
            var combat = app.Catalog.Rules();
            var text = new StringBuilder();
            text.AppendLine("【地图规则】");
            text.AppendLine("层数          " + rules.Floors + "    每层采样位置  " + string.Join(" / ", rules.StepsPerFloor));
            text.AppendLine("分支权重      " + string.Join(" / ", rules.BranchWeights.Select(v => (v * 100).ToString("0"))));
            text.AppendLine("概率强度      " + rules.ProbabilityStrength.ToString("0.00")
                + "    分支敏感度  " + rules.BranchAffinity.ToString("0.00"));
            var resolvedRules = MapGenerator.Resolve(rules);
            var odds = MapGenerator.TypeOdds(rules, app.Run.PlayerSanity);
            var parts = new List<string>();
            for (int i = 0; i < odds.Length && i < resolvedRules.NodeOdds.Length; i++)
                parts.Add(MapGenerator.Name(resolvedRules.NodeOdds[i].Type) + " "
                    + (odds[i] * 100).ToString("0.0") + "%");
            text.AppendLine("当前理智房间概率  " + string.Join(" · ", parts));
            text.AppendLine("补给站治疗    " + (rules.SupplyHealRatio * 100).ToString("0") + "%");
            text.AppendLine("补给/精英/首领  " + rules.SuppliesPerBattle + " / " + rules.SuppliesPerElite + " / " + rules.SuppliesPerBoss);
            text.AppendLine("精英          +" + rules.EliteHealthBonus + " HP · ×" + rules.EliteAttackScale.ToString("0.00") + " 攻");
            text.AppendLine("首领          +" + rules.BossHealthBonus + " HP · ×" + rules.BossAttackScale.ToString("0.00") + " 攻");
            text.AppendLine("初始增益      " + string.Join(" / ", rules.OpeningBuffs.Select(OpeningBuffs.Name)));
            text.AppendLine();
            text.AppendLine("【战斗规则】");
            text.AppendLine("概率强度 " + combat.ProbabilityStrength.ToString("0.00")
                + "   反馈系数 " + combat.FeedbackFactor.ToString("0.00")
                + "   死亡检定 " + combat.DeathChance.ToString("0.00")
                + "   理智步长 " + combat.MindStep);
            text.AppendLine("技能权重 技术 " + combat.TechnicalWeight.ToString("0.00")
                + " / 常态 " + combat.BasicWeight.ToString("0.00")
                + " / 原始 " + combat.PrimalWeight.ToString("0.00")
                + "   暴击倍率 ×" + combat.CritMultiplier.ToString("0.00"));
            text.AppendLine("追击 " + combat.FollowupRatio.ToString("0.00")
                + "   反击 " + combat.CounterRatio.ToString("0.00")
                + "   失衡免疫 " + combat.ImbalanceImmuneRounds + " 回合"
                + "   位移必中 " + combat.ImbalanceAutoHitRounds + " 回合");
            var limbs = LimbSystem.Resolve(combat.Limbs);
            text.AppendLine();
            text.AppendLine("【部位规则】  单次命中移除部位生命的 " + (limbs.HitRatio * 100).ToString("0") + "%");
            if (limbs.Parts != null)
                foreach (var part in limbs.Parts)
                    text.AppendLine("  " + LimbSystem.PartName(part.Part)
                        + "  体量 " + (part.HpRatio * 100).ToString("0") + "%"
                        + "  重伤 -" + (part.InjuredPenalty * 100).ToString("0") + "%"
                        + "  断肢 -" + (part.SeveredPenalty * 100).ToString("0") + "%");
            text.AppendLine();
            text.AppendLine("【角色目录】");
            if (app.Catalog.Heroes != null)
                foreach (var definition in app.Catalog.Heroes)
                {
                    if (!definition) continue;
                    var spec = definition.ToSpec();
                    text.AppendLine("  " + spec.Name + "  " + spec.Id + "  HP " + spec.MaxHp
                        + "  攻 " + spec.Attack + "  速 " + spec.MinSpeed + "–" + spec.MaxSpeed
                        + "  命 " + spec.Accuracy.ToString("0.00")
                        + "  暴 " + spec.CritChance.ToString("0.00")
                        + "  闪 " + spec.Dodge.ToString("0.00")
                        + "  防 " + spec.Defense.ToString("0.00"));
                }
            text.AppendLine();
            text.AppendLine("【事件目录】");
            foreach (string id in EventIds)
                if (!string.IsNullOrEmpty(id))
                    text.AppendLine("  " + id + "  " + EventScripts.Title(id));
            text.AppendLine();
            text.AppendLine("【饰品目录】");
            foreach (var item in AccessorySystem.Defaults)
                if (item != null)
                    text.AppendLine("  " + item.Name + "  ·  " + AccessorySystem.RestrictionText(item)
                        + "  ·  " + item.Description);
            text.AppendLine();
            text.AppendLine("【道具目录】");
            foreach (var item in ConsumableSystem.Defaults)
                if (item != null)
                    text.AppendLine("  " + item.Name + (item.Implemented ? "" : "（功能待定）")
                        + "  ·  " + item.Description);
            text.AppendLine();
            text.AppendLine("【义肢目录】");
            foreach (var item in limbs.Prosthetics ?? new ProstheticSpec[0])
                if (item != null)
                    text.AppendLine("  " + item.Name + "  ·  " + LimbSystem.PartName(item.Part)
                        + "  ·  " + item.Description);
            RulesBody.text = text.ToString();
        }

        private void SyncLog()
        {
            if (LogBody == null) return;
            var text = new StringBuilder();
            text.AppendLine("【提示历史】（新 → 旧）");
            if (hintHistory.Count == 0) text.AppendLine("  （无）");
            for (int i = hintHistory.Count - 1; i >= 0; i--) text.AppendLine("  " + hintHistory[i]);
            var events = Source == null || Source.Battle == null ? null : Source.Battle.Events;
            if (events != null && events.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("【战斗事件】（新 → 旧）");
                for (int i = events.Count - 1; i >= Math.Max(0, events.Count - 60); i--)
                    text.AppendLine("  [" + events[i].Kind + "] " + events[i].Message);
            }
            LogBody.text = text.ToString();
        }

        // ---- formatting helpers ----
        private static readonly string HelpText =
            "行动指南\n\n" +
            "01  选择技能，再点击高亮目标。点击任意角色可查看详情。\n\n" +
            "02  我方前排靠近敌人。位置不符时，可以消耗行动与相邻队友换位。\n\n" +
            "03  每回合只能使用该角色当前的技能，或与相邻队员换位。\n" +
            "      部分技能每场限次；用尽或站位不符时按钮上会写明原因。\n\n" +
            "04  理智低于30技能会发生变化，高于70会解锁特殊技能。\n" +
            "      30至70保持常态；形态在本回合内固定，切换的是整套技能。\n\n" +
            "05  敌人卡片下方显示下一次攻击。用格挡、反击、守护、打断或换位应对。\n" +
            "      奶妈可以治疗濒死队员；濒死时再次受伤会检定死亡。\n\n" +
            "06  战斗结束（胜利或全队阵亡）都会反馈玩家理智：队员平均净变化 × 0.5。\n" +
            "      全队阵亡本局立即结束；结算后继续前进，理智决定候选数量与房间类型。\n\n" +
            "07  命中、暴击与闪避由角色属性直接决定，不随理智变化。\n" +
            "      技能伤害 = 基础攻击力 × 技能倍率；受伤按目标防御比例减少。\n\n" +
            "08  每个队员有头部、躯干、手臂、腿部四个部位，各有独立血量。\n" +
            "      只有执斧者的重劈与追猎者的追猎会重创部位：一次重伤，两次断肢。\n" +
            "      重伤与断肢削弱对应属性（头→命中，躯干→防御，手臂→攻击，腿→速度）。\n" +
            "      补给站能修复重伤；断肢只能安装已经持有的义肢。\n\n" +
            "09  路线共三层：第1层从初始增益房间出发，第2、3层从补给站出发。\n" +
            "      每层最后是电梯井（第3层为首领）；层内的房间只在你走到跟前时才抽出。\n\n" +
            "10  每次给出1至3个候选，你选一个走过去；同一个位置不会重复同一种房间。\n" +
            "      低理智更容易遇到精英与祭坛，高理智更容易遇到事件房间。\n\n" +
            "调试台：右上角按钮打开；Esc 关闭。页签从左到右为概览、队伍、战斗、地图与房间、\n" +
            "事件、部位与义肢、物品与饰品、规则与目录、日志。所有命令都直接修改当前运行状态。";

        private static string Yes(bool value) { return value ? "是" : "否"; }

        private static int MaxHp(ActorState actor)
        {
            return actor == null || actor.Definition == null ? 0 : actor.Definition.MaxHp;
        }

        private static string FormName(SkillForm form)
        {
            switch (form)
            {
                case SkillForm.Primal: return "原始";
                case SkillForm.Technical: return "技术";
                default: return "常态";
            }
        }

        private static string FormLabel(SkillForm form)
        {
            switch (form)
            {
                case SkillForm.Primal: return "原始（低理智）";
                case SkillForm.Technical: return "技术（高理智）";
                default: return "常态";
            }
        }

        private static string OutcomeName(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.Victory: return "胜利";
                case BattleOutcome.Retreat: return "撤退";
                case BattleOutcome.Defeat: return "失败";
                default: return "进行中";
            }
        }

        private static string LimbSummary(ActorState hero)
        {
            return string.Join("/", Enum.GetValues(typeof(LimbPart)).Cast<LimbPart>()
                .Select(part => LimbSystem.PartName(part).Substring(0, 1)
                    + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part))));
        }

        private static string LimbDetail(ActorState hero)
        {
            var parts = new List<string>();
            foreach (LimbPart part in Enum.GetValues(typeof(LimbPart)))
            {
                var limb = LimbSystem.Get(hero, part);
                parts.Add(LimbSystem.PartName(part) + " "
                    + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part))
                    + (limb == null ? "" : " " + limb.Hp + "/" + limb.MaxHp));
            }
            return string.Join(" · ", parts);
        }

        private static string ProstheticDetail(ActorState hero)
        {
            var fitted = new List<string>();
            foreach (LimbPart part in Enum.GetValues(typeof(LimbPart)))
            {
                var limb = LimbSystem.Get(hero, part);
                if (limb != null && !string.IsNullOrEmpty(limb.Prosthetic))
                    fitted.Add(LimbSystem.PartName(part) + " → " + limb.Prosthetic);
            }
            return fitted.Count == 0 ? "无" : string.Join(" · ", fitted);
        }

        private static string AccessoryDetail(ActorState hero)
        {
            if (hero.EquippedAccessories == null || hero.EquippedAccessories.Count == 0) return "无";
            return string.Join(" · ", hero.EquippedAccessories.Where(a => a != null).Select(a => a.Name));
        }

        private static string BattleFlags(ActorState actor)
        {
            var flags = new List<string>();
            if (actor.Defending) flags.Add("防御");
            if (!string.IsNullOrEmpty(actor.ProtectorId)) flags.Add("守护 " + actor.ProtectorId);
            if (actor.BleedTurns > 0) flags.Add("流血 " + actor.BleedDamage + "×" + actor.BleedTurns);
            if (actor.Interrupted > 0) flags.Add("打断 " + actor.Interrupted);
            if (actor.BlockCharges > 0) flags.Add("格挡 " + actor.BlockCharges);
            if (actor.Counter) flags.Add("反击");
            if (actor.Followup) flags.Add("追击 " + actor.FollowupAttack);
            if (actor.Imbalance > 0) flags.Add("失衡");
            if (actor.SanityLocked) flags.Add("理智锁定");
            if (actor.SanityLockPending) flags.Add("理智锁定待生效");
            if (actor.SelfVulnerable > 0) flags.Add("易伤 " + (actor.SelfVulnerable * 100).ToString("0") + "%");
            return flags.Count == 0 ? "无" : string.Join(" · ", flags);
        }

        private static string StockNames<T>(List<T> stock)
        {
            if (stock == null || stock.Count == 0) return "无";
            var names = new List<string>();
            foreach (var item in stock)
            {
                if (item == null) continue;
                var spec = item as ProstheticSpec;
                if (spec != null) { names.Add(spec.Name); continue; }
                var accessory = item as AccessorySpec;
                if (accessory != null) { names.Add(accessory.Name); continue; }
                names.Add(item.ToString());
            }
            return names.Count == 0 ? "无" : string.Join(" · ", names);
        }

        private double attack(ActorState hero)
        {
            return hero.Definition == null ? 0
                : hero.Definition.Attack * LimbSystem.AttackFactor(hero, Source.Catalog.Rules().Limbs);
        }
        private double defense(ActorState hero)
        {
            return hero.Definition == null ? 0
                : hero.Definition.Defense * LimbSystem.DefenseFactor(hero, Source.Catalog.Rules().Limbs);
        }
        private double speed(ActorState hero)
        {
            if (hero.Definition == null) return 0;
            int baseSpeed = hero.SpeedRoll > 0 ? hero.SpeedRoll : hero.Definition.Speed;
            return baseSpeed * LimbSystem.SpeedFactor(hero, Source.Catalog.Rules().Limbs);
        }
        private double accuracy(ActorState hero)
        {
            return hero.Definition == null ? 0
                : hero.Definition.Accuracy * LimbSystem.AccuracyFactor(hero, Source.Catalog.Rules().Limbs);
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button) button.interactable = value;
        }

        private static void SetInteractable(Button[] buttons, bool value)
        {
            if (buttons == null) return;
            foreach (var button in buttons) if (button) button.interactable = value;
        }
    }
#else
    /// <summary>
    /// Release-build shell. The prefab still carries this component so the scene keeps its shape, but
    /// the console is switched off and the launcher is never shown.
    /// </summary>
    public sealed class IndustrialDebugConsole : MonoBehaviour
    {
        public static void ToggleFromHud() { }
        private void Awake() { gameObject.SetActive(false); }
    }
#endif
}
