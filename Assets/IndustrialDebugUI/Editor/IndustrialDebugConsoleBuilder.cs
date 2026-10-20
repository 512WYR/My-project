using System.IO;
using GameMake.IndustrialDebugUI;
using Ruin.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.IndustrialDebugUI.EditorTools
{
    /// <summary>
    /// Author-time builder for the acceptance console. It writes a prefab asset once; the game never
    /// constructs any of these objects. Rebuilding replaces the prefab, so hand edits inside the
    /// generated console will be lost — move any lasting tweak into this file instead.
    /// </summary>
    public static class IndustrialDebugConsoleBuilder
    {
        public const string Folder = "Assets/IndustrialDebugUI";
        public const string PrefabFolder = Folder + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/IndustrialDebugConsole.prefab";
        // Bump this whenever the layout or the serialized fields change. The installer rebuilds the
        // prefab after a domain reload when the stored version is older, so the scene instance picks
        // the new fields up without the user having to find the menu item.
        public const int Version = 7;
        public const string BuildVersionKey = "GameMake.IndustrialDebugUI.BuildVersion";
        private const string FontPath = "Assets/IndustrialBattleUI/Art/HudChinese.ttf";

        private const float Width = 1920f;
        private const float Height = 1080f;
        private const float RailWidth = 276f;
        private const float HeaderHeight = 58f;
        private const float FooterHeight = 40f;
        private const float ContentW = Width - RailWidth - 60f;
        private const float ContentH = Height - HeaderHeight - FooterHeight - 40f;

        private static readonly Color WindowBg = new Color(0.065f, 0.075f, 0.085f, 1f);
        private static readonly Color BodyBg = new Color(0.055f, 0.065f, 0.075f, 1f);
        private static readonly Color BarBg = new Color(0.10f, 0.12f, 0.13f, 1f);
        private static readonly Color RailBg = new Color(0.075f, 0.085f, 0.095f, 1f);
        private static readonly Color ButtonBg = new Color(0.145f, 0.165f, 0.18f, 1f);
        private static readonly Color Ink = new Color(0.90f, 0.88f, 0.83f, 1f);
        private static readonly Color Muted = new Color(0.60f, 0.65f, 0.67f, 1f);
        private static readonly Color Gold = new Color(0.82f, 0.68f, 0.44f, 1f);
        private static readonly Color Cyan = new Color(0.51f, 0.76f, 0.78f, 1f);
        private static readonly Color Rust = new Color(0.81f, 0.53f, 0.46f, 1f);
        private static readonly Color Scrim = new Color(0.012f, 0.018f, 0.022f, 0.96f);

        private static Font font;

        [MenuItem("GameMake/Debug Console/重建调试台 Prefab")]
        public static void Build()
        {
            font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!font) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            Directory.CreateDirectory(PrefabFolder);
            var root = new GameObject("IndustrialDebugConsole", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            scaler.matchWidthOrHeight = 0.5f;

            var console = root.AddComponent<IndustrialDebugConsole>();

            BuildLauncher(root.transform, console);
            BuildConsole(root.transform, console);

            root.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // A Canvas prefab saved outside a scene is serialized with a zero root scale, which makes
            // the whole overlay invisible when the scene instance inherits it. Repair the saved asset.
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (contents)
            {
                if (contents.transform.localScale == Vector3.zero)
                {
                    contents.transform.localScale = Vector3.one;
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    Debug.Log("DEBUG_CONSOLE_ROOT_SCALE_FIXED " + PrefabPath);
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorPrefs.SetInt(BuildVersionKey, Version);
            Debug.Log("DEBUG_CONSOLE_BUILD_OK " + PrefabPath + " v" + Version);
        }

        private static void BuildLauncher(Transform root, IndustrialDebugConsole console)
        {
            var launcher = Group(root, "Launcher - 调试台入口", 0, 0, Width, Height);
            // Top centre: the Gothic HUD already owns a "调试台" button in the top-right corner, and the
            // console canvas draws above it. Keeping the fallback launcher centred means the two
            // entrances never cover each other in a scene that uses both.
            var button = Btn(launcher, "Launcher Button", "调试台 · DEBUG", 836, 22, 248, 58, out _, 19);
            console.LauncherRoot = launcher.gameObject;
            console.LauncherButton = button;
        }

        private static void BuildConsole(Transform root, IndustrialDebugConsole console)
        {
            var overlay = Group(root, "Console - 调试验收台", 0, 0, Width, Height);
            console.ConsoleRoot = overlay.gameObject;
            var scrim = Solid(overlay, "Scrim", 0, 0, Width, Height, Scrim, true);
            scrim.color = Scrim;

            var window = Group(overlay, "Window", 24, 20, Width - 48, Height - 40);
            Solid(window, "Body", 0, 0, Width - 48, Height - 40, WindowBg, true);

            var header = Group(window, "Header", 0, 0, Width - 48, HeaderHeight);
            Solid(header, "Bar", 0, 0, Width - 48, HeaderHeight, BarBg, true);
            Label(header, "Title", "调试验收台  /  DEBUG CONSOLE", 20, 0, 620, HeaderHeight, 24, Gold);
            console.HeaderStatus = Label(header, "Status", "", 660, 0, 1040, HeaderHeight, 16, Muted);
            var close = Btn(header, "Close", "关闭", Width - 48 - 176, 9, 156, 40, out _, 17);
            console.CloseButton = close;

            var rail = Group(window, "Tab Rail", 0, HeaderHeight, RailWidth, Height - 40 - HeaderHeight - FooterHeight);
            Solid(rail, "Rail", 0, 0, RailWidth, Height - 40 - HeaderHeight - FooterHeight, RailBg, true);
            string[] tabNames =
            {
                "概览  Overview", "队伍  Party", "战斗  Battle", "地图与房间  Map",
                "事件  Events", "部位与义肢  Limbs", "物品与饰品  Items", "规则与目录  Rules", "日志  Log"
            };
            console.TabButtons = new Button[tabNames.Length];
            console.TabBackgrounds = new Image[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                var button = Btn(rail, "Tab " + i, tabNames[i], 8, 10 + i * 60, RailWidth - 16, 52, out _, 18);
                console.TabButtons[i] = button;
                console.TabBackgrounds[i] = button.targetGraphic as Image;
            }

            var content = Group(window, "Content", RailWidth + 12, HeaderHeight, ContentW, ContentH);
            console.Pages = new GameObject[9];
            BuildOverview(content, console);
            BuildParty(content, console);
            BuildBattle(content, console);
            BuildMap(content, console);
            BuildEvents(content, console);
            BuildLimbs(content, console);
            BuildItems(content, console);
            BuildRules(content, console);
            BuildLog(content, console);
            for (int i = 0; i < console.Pages.Length; i++)
                if (console.Pages[i]) console.Pages[i].SetActive(i == 0);

            var footer = Group(window, "Footer", 0, Height - 40 - FooterHeight, Width - 48, FooterHeight);
            Solid(footer, "Bar", 0, 0, Width - 48, FooterHeight, BarBg, true);
            console.FooterText = Label(footer, "Hint", "", 20, 0, Width - 48 - 40, FooterHeight, 15, Muted);
            console.FooterText.horizontalOverflow = HorizontalWrapMode.Wrap;
            console.FooterText.verticalOverflow = VerticalWrapMode.Truncate;

            overlay.gameObject.SetActive(false);
        }

        private static void BuildOverview(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 0 - 概览", 980, out console.Pages[0]);
            Label(content, "Title", "运行概览", 20, 12, 860, 36, 24, Gold);
            console.OverviewBody = Body(content, "Overview Body", 20, 58, 860, 900);
            Label(content, "Hint Title", "当前提示", 896, 12, 300, 36, 20, Cyan);
            console.OverviewHint = Body(content, "Overview Hint", 896, 58, 300, 900, 15, Cyan);
            Label(content, "Help Title", "操作说明", 1212, 12, 350, 36, 20, Gold);
            console.HelpBody = Body(content, "Help Body", 1212, 58, 350, 900, 14, Muted);
        }

        private static void BuildParty(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 1 - 队伍", 1140, out console.Pages[1]);
            Label(content, "Title", "队伍", 20, 12, 1540, 36, 24, Gold);
            console.PartySelect = new Button[4];
            console.PartySelectLabels = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                console.PartySelect[i] = Btn(content, "Hero " + i, "队员 " + (i + 1),
                    20 + i * 386, 56, 370, 54, out console.PartySelectLabels[i], 18);
            }
            console.PartyBody = Body(content, "Party Body", 20, 126, 1540, 450);

            Label(content, "Adjust Title", "调整选中队员", 20, 588, 1540, 30, 20, Gold);
            Label(content, "Sanity Label", "理智", 20, 630, 80, 44, 18, Ink, TextAnchor.MiddleLeft);
            console.PartySanitySlider = SliderBar(content, "Party Sanity Slider", 104, 632, 900, 40);
            console.PartySanityInput = InputBox(content, "Party Sanity Input", "50", 1020, 630, 180, 44);
            console.PartySanityApply = Btn(content, "Party Sanity Apply", "应用", 1216, 630, 120, 44, out _, 17);
            console.PartySanityNow = Label(content, "Party Sanity Now", "",
                1350, 630, 190, 44, 16, Muted, TextAnchor.MiddleLeft);

            Label(content, "Form Label", "技能形态", 20, 688, 80, 44, 18, Ink, TextAnchor.MiddleLeft);
            console.PartyForm = new Button[3];
            string[] forms = { "常态", "技术（高理智）", "原始（低理智）" };
            for (int i = 0; i < 3; i++)
                console.PartyForm[i] = Btn(content, "Party Form " + i, forms[i],
                    104 + i * 280, 688, 260, 44, out _, 17);

            console.PartyHp = new Button[2];
            console.PartyHp[0] = Btn(content, "Hp Full", "恢复满血", 104, 746, 400, 48, out _, 17);
            console.PartyHp[1] = Btn(content, "Hp Death Door", "设为濒死（0 生命）", 524, 746, 400, 48, out _, 17);
            var note = Label(content, "Note",
                "拖动滑块或输入数值后按「应用」（输入框内按回车也可）修改理智；拖动在松开鼠标时写入。\n"
                + "形态按钮直接切换该队员当前的技能组；对战中会在该角色下个回合开始时按理智重新抽取。\n"
                + "「设为濒死」用于检查奶妈救治与死亡检定。",
                20, 800, 1540, 56, 15, Muted);
            note.verticalOverflow = VerticalWrapMode.Overflow;

            // Player sanity is the shared value that drives candidate count and room-type odds, so it
            // belongs next to the per-hero controls. The four new-run buttons mirror the old lab presets.
            Label(content, "Run Title", "运行控制", 20, 872, 1540, 30, 20, Gold);
            Label(content, "Player Sanity Label", "玩家理智", 20, 914, 126, 44, 18, Ink, TextAnchor.MiddleLeft);
            console.PlayerSanitySlider = SliderBar(content, "Player Sanity Slider", 150, 916, 800, 40);
            console.PlayerSanityInput = InputBox(content, "Player Sanity Input", "50", 970, 914, 170, 44);
            console.PlayerSanityApply = Btn(content, "Player Sanity Apply", "应用", 1156, 914, 120, 44, out _, 17);
            console.PlayerSanityNow = Label(content, "Player Sanity Now", "",
                1290, 914, 250, 44, 16, Muted, TextAnchor.MiddleLeft);

            console.NewRunButtons = new Button[3];
            string[] runCaptions =
            {
                "新开演练（默认理智 50）", "新开演练 · 沿用当前玩家理智", "新开演练 · 沿用当前理智与种子"
            };
            for (int i = 0; i < runCaptions.Length; i++)
                console.NewRunButtons[i] = Btn(content, "New Run " + i, runCaptions[i],
                    20 + i * 520, 970, 480, 52, out _, 17);

            var runNote = Label(content, "Run Note",
                "新开会重置整局并回到初始增益房；「沿用当前玩家理智」读取上面滑块的值，队员属性与规则不变。\n"
                + "「沿用当前理智与种子」会重放同一条路线，适合对照修改前后的战斗结果。\n"
                + "玩家理智影响候选数量与房间类型，事件的高/低理智分支读取的也是它。",
                20, 1032, 1540, 80, 15, Muted);
            runNote.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void BuildBattle(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 2 - 战斗", 1060, out console.Pages[2]);
            Label(content, "Title", "战斗", 20, 12, 1540, 36, 24, Gold);
            console.BattleStage = Label(content, "Stage", "", 20, 56, 1540, 28, 17, Cyan);
            console.BattleBody = Body(content, "Battle Body", 20, 92, 980, 570, 16);
            Label(content, "Settlement Title", "结算", 1016, 56, 548, 30, 20, Gold);
            console.BattleSettlement = Body(content, "Settlement Body", 1016, 92, 548, 220, 15, Ink);
            Label(content, "Log Title", "战斗事件", 1016, 330, 548, 30, 20, Gold);
            console.BattleLog = Body(content, "Battle Log", 1016, 366, 548, 296, 14, Muted);

            Label(content, "Control Title", "战斗命令（作用于选中队员）", 20, 676, 1540, 30, 20, Gold);
            Label(content, "Sanity Label", "理智", 20, 714, 62, 44, 18, Ink, TextAnchor.MiddleLeft);
            console.BattleSanitySlider = SliderBar(content, "Battle Sanity Slider", 104, 716, 740, 40);
            console.BattleSanityInput = InputBox(content, "Battle Sanity Input", "50", 860, 714, 150, 44);
            console.BattleSanityApply = Btn(content, "Battle Sanity Apply", "应用", 1020, 714, 110, 44, out _, 17);
            console.BattleSanityNow = Label(content, "Battle Sanity Now", "",
                1146, 714, 394, 44, 16, Muted, TextAnchor.MiddleLeft);

            Label(content, "Form Label", "技能形态", 20, 772, 80, 44, 18, Ink, TextAnchor.MiddleLeft);
            console.BattleForm = new Button[3];
            string[] forms = { "常态", "技术（高理智）", "原始（低理智）" };
            for (int i = 0; i < 3; i++)
                console.BattleForm[i] = Btn(content, "Battle Form " + i, forms[i],
                    84 + i * 280, 772, 260, 44, out _, 17);

            console.BattleWin = Btn(content, "Win", "直接胜利", 20, 830, 300, 54, out _, 18);
            console.BattleRetreat = Btn(content, "Retreat", "撤退结算", 336, 830, 300, 54, out _, 18);
            console.BattleRest = Btn(content, "Rest", "整备全队生命", 652, 830, 300, 54, out _, 18);
            console.BattleDeathDoor = Btn(content, "Death Door", "濒死演练", 20, 894, 300, 54, out _, 18);
            console.BattleFeedback = Btn(content, "Feedback", "反馈样例（−2.5）", 336, 894, 300, 54, out _, 18);
            var note = Label(content, "Note",
                "直接胜利会正常走结算与奖励流程；撤退只记录结果、不发奖励。\n"
                + "理智用滑块或输入框修改，松开鼠标 / 点「应用」时写入；形态按钮直接切换当前技能组。\n"
                + "「反馈样例」会新开一局并直接进入战斗，把四名队员的理智净变化设为 −20 / −10 / 0 / +10。",
                20, 960, 1540, 70, 15, Muted);
            note.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void BuildMap(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 3 - 地图与房间", 1000, out console.Pages[3]);
            Label(content, "Title", "地图与房间", 20, 12, 1540, 36, 24, Gold);
            console.MapBody = Body(content, "Map Body", 20, 56, 980, 900, 16);

            Label(content, "Jump Title", "跳转", 1016, 56, 548, 30, 20, Gold);
            console.JumpFloors = new Button[3];
            for (int i = 0; i < 3; i++)
                console.JumpFloors[i] = Btn(content, "Jump Floor " + i, "跳到第 " + (i + 1) + " 层",
                    1016 + i * 184, 94, 172, 48, out _, 16);
            console.JumpFloorEnd = Btn(content, "Jump Floor End", "跳到本层末端", 1016, 154, 548, 48, out _, 17);
            console.JumpBossFloor = Btn(content, "Jump Boss", "跳到首领层", 1016, 214, 548, 48, out _, 17);

            Label(content, "Force Title", "强制房间（替换当前位置候选并立即进入）", 1016, 286, 548, 30, 20, Gold);
            console.ForceRooms = new Button[9];
            string[] captions =
            {
                "补给站", "事件房", "祭坛房", "宝箱房", "电梯井", "普通战斗", "精英战斗", "首领战", "初始增益"
            };
            for (int i = 0; i < captions.Length; i++)
            {
                float x = 1016 + (i % 2) * 284;
                float y = 326 + (i / 2) * 58;
                console.ForceRooms[i] = Btn(content, "Force Room " + i, captions[i], x, y, 264, 48, out _, 16);
            }
            var note = Label(content, "Note",
                "强制房间会在当前位置前进一步后再替换候选，因此不会跳过本层固定位置。\n"
                + "首领战停留在本层；要从首领层开始请用「跳到首领层」。",
                1016, 620, 548, 70, 15, Muted);
            note.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void BuildEvents(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 4 - 事件", 1100, out console.Pages[4]);
            Label(content, "Title", "事件与祭坛", 20, 12, 1540, 36, 24, Gold);
            console.EventBody = Body(content, "Event Body", 20, 56, 1540, 480, 16);
            Label(content, "Force Title", "强制事件（替换当前位置候选并立即进入）", 20, 552, 1540, 30, 20, Gold);
            string[] ids = { "EVT-001", "EVT-002", "EVT-003", "EVT-004", "EVT-005", "EVT-006",
                "EVT-007", "EVT-008", "EVT-H001", "EVT-L001", "EVT-009" };
            console.EventButtons = new Button[11];
            for (int i = 0; i < console.EventButtons.Length; i++)
            {
                if (i >= ids.Length) { console.EventButtons[i] = null; continue; }
                string id = ids[i];
                string caption = id + "  " + EventScripts.Title(id)
                    + (id == "EVT-H001" ? "（高理智）" : id == "EVT-L001" ? "（低理智）" : "");
                float x = 20 + (i % 3) * 510;
                float y = 592 + (i / 3) * 68;
                console.EventButtons[i] = Btn(content, "Event " + i, caption, x, y, 490, 58, out _, 17);
            }
            Label(content, "Battle Title", "事件中的战斗", 20, 884, 1540, 30, 20, Gold);
            console.EventWin = Btn(content, "Event Win", "直接打赢", 20, 922, 490, 54, out _, 18);
            console.EventRetreat = Btn(content, "Event Retreat", "撤退结算", 530, 922, 490, 54, out _, 18);
        }

        private static void BuildLimbs(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 5 - 部位与义肢", 1000, out console.Pages[5]);
            Label(content, "Title", "部位与义肢", 20, 12, 1540, 36, 24, Gold);
            console.LimbBody = Body(content, "Limb Body", 20, 56, 1540, 360, 16);

            Label(content, "Injure Title", "部位伤害（作用于选中队员）", 20, 430, 1540, 30, 20, Gold);
            console.InjureButtons = new Button[4];
            string[] parts = { "头部", "躯干", "手臂", "腿部" };
            for (int i = 0; i < 4; i++)
                console.InjureButtons[i] = Btn(content, "Injure " + parts[i], "击伤·" + parts[i],
                    20 + i * 386, 468, 370, 52, out _, 18);
            console.SeverButtons = new Button[2];
            console.SeverButtons[0] = Btn(content, "Sever Arm", "断肢·手臂", 20, 530, 370, 52, out _, 18);
            console.SeverButtons[1] = Btn(content, "Sever Leg", "断肢·腿部", 406, 530, 370, 52, out _, 18);

            Label(content, "Repair Title", "修复（只处理重伤）", 20, 600, 1540, 30, 20, Gold);
            console.RepairButtons = new Button[4];
            for (int i = 0; i < 4; i++)
                console.RepairButtons[i] = Btn(content, "Repair " + parts[i], "修复·" + parts[i],
                    20 + i * 386, 638, 370, 52, out _, 18);

            Label(content, "Install Title", "安装义肢（任意未装义体的部位，使用队伍仓库）", 20, 708, 1540, 30, 20, Gold);
            console.InstallButtons = new Button[4];
            for (int i = 0; i < 4; i++)
                console.InstallButtons[i] = Btn(content, "Install " + parts[i], "安装·" + parts[i],
                    20 + i * 386, 746, 370, 52, out _, 18);

            var note = Label(content, "Note",
                "「击伤」造成一次部位伤害，「断肢」连续两次，结果与敌人重劈一致。\n"
                + "义肢在「物品与饰品」页发放；安装按钮在仓库里存在对应部位的义肢且该部位未装义体时可用。",
                20, 820, 1540, 70, 15, Muted);
            note.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void BuildItems(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 6 - 物品与饰品", 1300, out console.Pages[6]);
            Label(content, "Title", "物品与饰品", 20, 12, 1540, 36, 24, Gold);
            console.ItemBody = Body(content, "Item Body", 20, 56, 980, 720, 15);

            Label(content, "Equipped Title", "选中队员的饰品", 1016, 56, 548, 30, 20, Gold);
            console.EquippedText = Body(content, "Equipped Body", 1016, 92, 548, 210, 15, Ink);

            Label(content, "Equip Title", "装备饰品", 1016, 316, 548, 30, 18, Gold);
            console.EquipButtons = new Button[8];
            console.EquipLabels = new Text[8];
            for (int i = 0; i < 8; i++)
            {
                float x = 1016 + (i % 2) * 284;
                float y = 354 + (i / 2) * 50;
                console.EquipButtons[i] = Btn(content, "Equip " + i, "—", x, y, 264, 42,
                    out console.EquipLabels[i], 14);
            }

            Label(content, "Unequip Title", "卸下饰品", 1016, 566, 548, 30, 18, Gold);
            console.UnequipButtons = new Button[6];
            console.UnequipLabels = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                float x = 1016 + (i % 2) * 284;
                float y = 604 + (i / 2) * 50;
                console.UnequipButtons[i] = Btn(content, "Unequip " + i, "—", x, y, 264, 42,
                    out console.UnequipLabels[i], 14);
            }

            Label(content, "Use Title", "使用道具（作用于选中队员）", 1016, 774, 548, 30, 18, Gold);
            console.UseButtons = new Button[6];
            console.UseLabels = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                float x = 1016 + (i % 2) * 284;
                float y = 812 + (i / 2) * 50;
                console.UseButtons[i] = Btn(content, "Use " + i, "—", x, y, 264, 42,
                    out console.UseLabels[i], 14);
            }

            Label(content, "Grant Title", "发放库存", 20, 800, 1540, 30, 20, Gold);
            console.GrantProstheticItems = Btn(content, "Grant Prosthetics", "发放全部义肢", 20, 838, 500, 52, out _, 18);
            console.GrantAccessoryItems = Btn(content, "Grant Accessories", "发放全部饰品", 530, 838, 500, 52, out _, 18);
            console.GrantConsumableItems = Btn(content, "Grant Consumables", "发放全部道具", 1040, 838, 500, 52, out _, 18);
        }

        private static void BuildRules(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 7 - 规则与目录", 2600, out console.Pages[7]);
            Label(content, "Title", "规则与目录", 20, 12, 1540, 36, 24, Gold);
            console.RulesBody = Body(content, "Rules Body", 20, 56, 1540, 2500, 15);
        }

        private static void BuildLog(Transform parent, IndustrialDebugConsole console)
        {
            var content = Page(parent, "Page 8 - 日志", 2600, out console.Pages[8]);
            Label(content, "Title", "日志", 20, 12, 1540, 36, 24, Gold);
            console.ClearLog = Btn(content, "Clear Log", "清空日志", 20, 56, 240, 44, out _, 17);
            var hint = Label(content, "Hint", "提示历史与战斗事件都按「新 → 旧」排列。",
                280, 56, 1280, 44, 15, Muted);
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            console.LogBody = Body(content, "Log Body", 20, 112, 1540, 2450, 14);
        }

        // ---- primitives ----
        private static RectTransform Page(Transform parent, string name, float height, out GameObject page)
        {
            var view = Group(parent, name, 0, 0, ContentW, ContentH);
            page = view.gameObject;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            var viewport = Group(view, "Viewport", 0, 0, ContentW, ContentH);
            var background = viewport.gameObject.AddComponent<Image>();
            background.color = BodyBg;
            background.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Group(viewport, "Content", 0, 0, ContentW, height);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, height);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 60f;
            page.SetActive(false);
            return content;
        }

        private static Text Body(Transform parent, string name, float x, float y, float w, float h,
            int size = 17, Color? color = null)
        {
            var text = Label(parent, name, "", x, y, w, h, size, color ?? Ink);
            text.lineSpacing = 1.15f;
            return text;
        }

        // A plain 0-100 slider. The Slider component owns fill/handle anchors, so those rects only
        // need a size; the visible colours mirror the rest of the console.
        private static Slider SliderBar(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = Group(parent, name, x, y, w, h);
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            var background = Solid(rt, "Background", 0, 0, w, h, ButtonBg, true);
            background.raycastTarget = true;
            slider.targetGraphic = background;

            var fill = Solid(rt, "Fill", 0, 0, w, h, Gold, false);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0.3f);
            fillRect.anchorMax = new Vector2(0f, 0.7f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handle = Solid(rt, "Handle", 0, 0, 24, h, Ink, true);
            var handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0.15f);
            handleRect.anchorMax = new Vector2(0f, 0.85f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(24, 0);
            slider.handleRect = handleRect;
            slider.value = 50f;
            return slider;
        }

        // A single-line numeric field. The console reads/writes it directly; the input field only
        // needs a text child, a placeholder and a raycastable background.
        private static InputField InputBox(Transform parent, string name, string value,
            float x, float y, float w, float h)
        {
            var background = Solid(parent, name, x, y, w, h, ButtonBg, true);
            var field = background.gameObject.AddComponent<InputField>();
            var text = Label(background.transform, "Text", value, 10, 0, w - 20, h, 17, Ink,
                TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var placeholder = Label(background.transform, "Placeholder", "0-100", 10, 0, w - 20, h,
                17, Muted, TextAnchor.MiddleLeft);
            field.textComponent = text;
            field.placeholder = placeholder;
            field.text = value;
            field.contentType = InputField.ContentType.DecimalNumber;
            field.characterLimit = 6;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            return field;
        }

        private static RectTransform Group(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static Image Solid(Transform parent, string name, float x, float y, float w, float h,
            Color color, bool raycast = false)
        {
            var image = Group(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = null;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static Text Label(Transform parent, string name, string text, float x, float y,
            float w, float h, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = Group(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            label.text = text ?? "";
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static Button Btn(Transform parent, string name, string caption,
            float x, float y, float w, float h, out Text label, int size = 17)
        {
            var image = Solid(parent, name, x, y, w, h, ButtonBg, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.86f, 0.62f);
            colors.pressedColor = new Color(0.75f, 0.60f, 0.37f);
            colors.disabledColor = new Color(0.30f, 0.32f, 0.33f, 0.85f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            label = Label(image.transform, "Caption", caption, 8, 0, w - 16, h, size, Ink, TextAnchor.MiddleCenter);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return button;
        }
    }
}
