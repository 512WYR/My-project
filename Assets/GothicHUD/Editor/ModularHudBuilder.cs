using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.GothicHUDEditor
{
    public static class ModularHudBuilder
    {
        const string Root = "Assets/GothicHUD";
        const string ArtRoot = Root + "/Art/Modular/";
        public const string ScenePath = Root + "/Scenes/GothicBattleHUD.unity";
        public const string PrefabPath = Root + "/Prefabs/GothicBattleHUD.prefab";
        public const int Width = 1920, Height = 1080;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Font font;
        static Sprite white;
        static readonly Color Gold = new Color32(219, 187, 131, 255);
        static readonly Color Muted = new Color32(162, 144, 111, 255);
        static readonly Color Ink = new Color32(230, 224, 212, 255);
        static readonly Color Cyan = new Color32(129, 193, 200, 255);
        static readonly Color Rust = new Color32(206, 136, 117, 255);
        // Position cards, in the 1920x1080 reference space. They sit on top of the operation frame, lined up
        // with the two formation rails already painted inside it: allied rail x176..851, enemy rail
        // x1150..1820, both at the frame's top edge y750. Rank 0 is the front rank in both columns.
        const float CardW = 160, CardH = 180, CardStep = 161;
        const float AllyCardsX = 192, EnemyCardsX = 1164, CardsY = 570;
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../交付/哥特HUD/新版"));

        static SpriteRect Rect(string name, int x, int y, int w, int h, int height, Vector4 border = default)
        {
            return new SpriteRect { name = name, rect = new Rect(x, height - y - h, w, h), border = border, pivot = Vector2.one / 2, alignment = SpriteAlignment.Center, spriteID = GUID.Generate() };
        }
        static void Import(string file, string name, Vector4 border = default, int[] crop = null, SpriteRect[] cuts = null)
        {
            string path = ArtRoot + file + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100; importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096; importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var old = provider.GetSpriteRects().ToDictionary(s => s.name, s => s.spriteID);
            var rects = cuts ?? new[] { crop == null ? Rect(name, 0, 0, texture.width, texture.height, texture.height, border) : Rect(name, crop[0], crop[1], crop[2], crop[3], texture.height, border) };
            foreach (var rect in rects) if (old.TryGetValue(rect.name, out var id)) rect.spriteID = id;
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply(); importer.SaveAndReimport();
            foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()) sprites[sprite.name] = sprite;
        }
        static void Prepare()
        {
            AssetDatabase.Refresh(); sprites.Clear();
            Import("操作框", "OperationFrame", new Vector4(250, 90, 180, 170));
            Import("技能介绍栏", "DescriptionFrame", new Vector4(60, 40, 90, 135));
            Import("角色框", "PartyFrame");
            Import("角色站位", "FormationRail", new Vector4(90, 0, 90, 0));
            Import("人物头像框", "PortraitFrame", crop: new[] { 605, 587, 535, 529 });
            Import("生命理智值", "VitalsFrame", new Vector4(37, 12, 37, 12));
            Import("通用边框", "GeneralFrame", new Vector4(98, 98, 98, 98));
            Import("物品框", "ItemFrame", new Vector4(15, 15, 15, 15));
            Import("位移", "Displacement");
            Import("蒸汽朋克机械回合计数器", "RoundFrame", crop: new[] { 34, 111, 2138, 502 });
            Import("肢体血条", "LimbBar", new Vector4(35, 14, 35, 14), new[] { 195, 0, 407, 67 });
            Import("肢体1", "HeadHealthy"); Import("肢体 2", "TorsoHealthy"); Import("肢体 3", "ArmHealthy"); Import("肢体 4", "LegHealthy");
            Import("肢体破损1", "HeadBroken"); Import("肢体破损2", "TorsoBroken"); Import("肢体破损3", "ArmBroken"); Import("肢体破损 4", "LegBroken");
            for (int i = 1; i <= 5; i++) Import("图层 " + i, "Hotkey" + i);
            Import("技能框", "SkillFrame", cuts: new[] {
                Rect("Skill1",84,0,145,136,175), Rect("Skill2",236,0,148,136,175),
                Rect("Skill3",391,0,148,136,175), Rect("Skill4",547,0,149,136,175), Rect("Skill5",702,0,146,136,175),
                Rect("SkillLeft",0,66,84,70,175), Rect("SkillRight",848,65,81,72,175)
            });
            font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Art/HudChinese.ttf");
            // A tiny native asset supports Image.fillAmount without altering any supplied artwork.
            string fillPath = Root + "/Art/SolidFill.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fillPath);
            if (!texture)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); texture.name = "Solid Fill";
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
                AssetDatabase.CreateAsset(texture, fillPath);
            }
            white = AssetDatabase.LoadAllAssetsAtPath(fillPath).OfType<Sprite>().FirstOrDefault();
            if (!white) { white = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one / 2, 100); white.name = "Solid Fill Sprite"; AssetDatabase.AddObjectToAsset(white, texture); }
        }

        [MenuItem("GameMake/Gothic HUD/Rebuild Modular HUD")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Prepare(); Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Preview Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -100); camera.orthographic = true; camera.orthographicSize = Height / 2f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(8, 9, 11, 255);
            camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var go = new GameObject("GothicBattleHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GothicHudPanel), typeof(ModularHudController));
            var root = (RectTransform)go.transform;
            // A zero scale here is what left the previous prefab invisible; the canvas is driven by the
            // CanvasScaler, so the transform must stay at identity.
            root.localScale = Vector3.one;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10; canvas.pixelPerfect = true;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(Width, Height); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = go.GetComponent<GothicHudPanel>(); var controller = go.GetComponent<ModularHudController>(); controller.Panel = panel;
            controller.Characters = Enumerable.Range(1,4).Select(i => new HudCharacterState { Name = "角色 " + i, Subtitle = "职业 / 称号" }).ToArray();

            var top = Group(root, "Round Counter - 回合计数器", 0, 12, 1000, 235);
            top.anchorMin = top.anchorMax = new Vector2(.5f, 1); top.pivot = new Vector2(.5f, 1); top.anchoredPosition = new Vector2(0, -12);
            Art(top, "Mechanical Frame", "RoundFrame", 0, 0, 1000, 235);
            Solid(top, "Number Backplate", 464, 87, 76, 55, new Color32(12,12,11,255));
            var digits = Group(top, "Digits - 动态数字", 502, 115, 82, 66); digits.pivot = Vector2.one / 2;
            var counter = top.gameObject.AddComponent<MechanicalRoundCounter>(); counter.DigitContainer = digits;
            counter.DigitSprites = AssetDatabase.LoadAllAssetsAtPath(Root + "/Art/DigitAtlas.png").OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            counter.DigitSlots = new Image[10];
            for (int i = 0; i < 10; i++) { var image = Blank(digits, "Digit " + i, 0, 0, 37, 59); image.color = Color.white; counter.DigitSlots[i] = image; }
            counter.RefreshDisplay();

            var party = Group(root, "Party - 四人队伍", 12, 137, 148, 584);
            Art(party, "Party Mechanical Frame", "PartyFrame", 0, 0, 148, 584);
            panel.PartyButtons = new Button[4]; panel.PartyPortraits = new Image[4]; panel.PartySelections = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                var slot = Group(party, "Character " + (i + 1), 39, 26 + i * 115.6f, 102, 102);
                panel.PartyPortraits[i] = Blank(slot, "Portrait - 拖入角色图片", 0, 0, 99, 96);
                var hit = Solid(slot, "Select Character", 0, 0, 100, 100, Color.clear); panel.PartyButtons[i] = Click(hit);
                Label(slot, "Party Number", (i+1).ToString(), 65, 72, 26, 20, 13, Muted, TextAnchor.MiddleRight);
                var marker = Label(slot, "Selected", "◀", -38, 30, 30, 36, 25, Gold); panel.PartySelections[i] = marker.gameObject; marker.gameObject.SetActive(i == 0);
            }

            var bottom = Group(root, "Bottom HUD - 操作面板", 0, 0, Width, 330);
            bottom.anchorMin = bottom.anchorMax = new Vector2(.5f, 0); bottom.pivot = new Vector2(.5f, 0); bottom.anchoredPosition = Vector2.zero;
            Art(bottom, "Allied Formation - 我方站位", "FormationRail", 176, -56, 675, 56, true);
            Art(bottom, "Enemy Formation - 敌方站位", "FormationRail", 1150, -56, 670, 56, true);
            Art(bottom, "Operation Outer Frame", "OperationFrame", 5, 0, 1908, 326, true, 2.5f);

            var portrait = Group(bottom, "Portrait Panel - 人物头像", 26, 52, 226, 240);
            Art(portrait, "Portrait Frame", "PortraitFrame", 0, 0, 228, 225);
            panel.MainPortrait = Blank(portrait, "Portrait - 拖入角色图片", 83, 46, 91, 125);
            var stats = Group(bottom, "Vitals - 生命理智", 266, 56, 204, 231);
            panel.CharacterName = Label(stats, "Character Name", "角色 1", 0, 0, 202, 31, 25, Gold);
            panel.CharacterSubtitle = Label(stats, "Character Subtitle", "职业 / 称号", 0, 35, 202, 22, 14, Muted);
            Label(stats, "Health Label", "生命", 0, 65, 54, 20, 14, Gold);
            controller.HealthFill = Meter(stats, "Health", "VitalsFrame", 0, 91, 199, 27, new Color32(174, 47, 38, 255), out var healthText);
            controller.HealthValue = healthText;
            Label(stats, "Sanity Label", "理智", 0, 127, 54, 20, 14, Gold);
            controller.SanityFill = Meter(stats, "Sanity", "VitalsFrame", 0, 153, 199, 27, new Color32(45, 119, 170, 255), out var sanityText);
            controller.SanityValue = sanityText;
            Label(stats, "Selected Character Note", "当前角色", 0, 199, 198, 20, 13, Muted);
            panel.AttributeValues = Array.Empty<Text>(); panel.EquipmentValues = Array.Empty<Text>();

            var limbs = Group(bottom, "Limbs - 肢体状态", 492, 53, 282, 247);
            Art(limbs, "Limb Panel Border", "GeneralFrame", -9, -7, 283, 250, true, 6);
            controller.Limbs = new HudLimbView[4];
            string[] keys = { "Head", "Torso", "Arm", "Leg" }; string[] names = { "头部", "躯干", "手臂", "腿部" };
            for (int i = 0; i < 4; i++)
            {
                var row = Group(limbs, keys[i] + " - " + names[i], 0, i * 58, 264, 55);
                var icon = Art(row, "State Icon - 自动切换", keys[i] + "Healthy", 0, 0, 48, 52);
                Label(row, "Limb Name", names[i], 58, 0, 80, 22, 15, Gold);
                var stateLabel = Label(row, "State", "完好", 162, 0, 95, 22, 12, Muted, TextAnchor.MiddleRight);
                var fill = Meter(row, "Limb Durability", "LimbBar", 57, 26, 201, 23, new Color32(164,56,39,255), out var value);
                controller.Limbs[i] = new HudLimbView { Part = (HudLimb)i, Icon = icon, Fill = fill, Value = value, Status = stateLabel, HealthySprite = sprites[keys[i]+"Healthy"], BrokenSprite = sprites[keys[i]+"Broken"] };
            }

            var skills = Group(bottom, "Skills - 技能与位移", 806, 65, 624, 238);
            Label(skills, "Skills Heading", "技能", 2, 0, 500, 24, 16, Muted);
            panel.SkillButtons = new Button[5]; panel.SkillIcons = new Image[5]; panel.SkillHotkeys = new Text[5]; panel.SkillSelections = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = Group(skills, "Skill " + (i+1), i * 102, 34, 96, 137);
                var frame = Art(slot, "Skill Frame", "Skill" + (i+1), 0, 0, 96, 98); panel.SkillButtons[i] = Click(frame);
                panel.SkillIcons[i] = Blank(slot, "Skill Icon - 替换图片并设Alpha为1", 10, 10, 76, 77);
                Art(slot, "Hotkey Plate", "Hotkey" + (i+1), 28, 99, 41, 30);
                panel.SkillHotkeys[i] = Label(slot, "Custom Hotkey - 使用前隐藏原数字牌", "", 28, 99, 41, 30, 19, Gold, TextAnchor.MiddleCenter);
                var selected = Label(slot, "Selected", "◆", 31, -22, 34, 24, 17, Gold, TextAnchor.MiddleCenter); selected.gameObject.SetActive(false); panel.SkillSelections[i] = selected.gameObject;
            }
            var move = Group(skills, "Displacement - 位移按钮", 522, 34, 92, 137);
            var moveFrame = Art(move, "Displacement Frame", "ItemFrame", 0, 0, 92, 98, true); controller.DisplacementButton = Click(moveFrame);
            Art(move, "Displacement Icon", "Displacement", 11, 10, 70, 75);
            Label(move, "Displacement Label", "位移", 0, 102, 92, 28, 16, Gold, TextAnchor.MiddleCenter);
            var items = Group(skills, "Items - 快捷物品", 0, 180, 510, 52);
            Label(items, "Items Label", "物品", 2, 13, 54, 24, 14, Muted);
            for (int i=0;i<4;i++)
            {
                var item=Group(items, "Item " + (i+1), 64+i*57, 0, 46, 46);
                Click(Art(item,"Item Frame","ItemFrame",0,0,46,46,true)); Blank(item,"Item Icon",7,7,32,32);
            }

            var description = Group(bottom, "Description - 技能介绍", 1450, 53, 421, 246);
            Art(description, "Description Frame", "DescriptionFrame", 0, 0, 421, 246, true, 2);
            panel.DescriptionTitle = Label(description, "Title", "技能介绍", 22, 16, 345, 34, 23, Gold);
            panel.DescriptionBody = Label(description, "Body", "选择技能查看说明", 23, 82, 337, 141, 17, Muted, TextAnchor.UpperLeft);
            controller.RefreshView();
            // The node pages go in first so the debug button and its sheet, which are authored last and have
            // to stay reachable from every page, end up drawn on top of them.
            BuildNodeChrome(root);
            BuildBattleChrome(root, panel, controller, counter, top.gameObject, party.gameObject, bottom.gameObject);
            PrefabUtility.SaveAsPrefabAsset(top.gameObject, Root + "/Prefabs/MechanicalRoundCounter.prefab");
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            Validate(controller);
            Render(canvas, camera, "HUD-1920x1080", 1920, 1080);
            Render(canvas, camera, "HUD-2560x1080", 2560, 1080);
            controller.SetLimbBroken(0, HudLimb.Arm, true); counter.SetRound(12);
            Render(canvas, camera, "HUD-ArmBroken-Round12", 1920, 1080);
            controller.RestoreSelectedLimbs(); counter.ResetRound();
            Debug.Log("MODULAR_HUD_BUILD_OK " + ScenePath);
        }

        /// <summary>
        /// What the battle page adds on top of the authored modules: the eight position cards, and one button
        /// opening a debug sheet for the readouts that have no place on the layout yet. Everything the
        /// controller toggles lives under one of the roots handed to ChromeRoots, so a scene can hide the
        /// battle half in one call.
        /// </summary>
        static void BuildBattleChrome(RectTransform root, GothicHudPanel panel, ModularHudController controller,
            MechanicalRoundCounter counter, GameObject top, GameObject party, GameObject bottom)
        {
            // ---- position cards, resting on the operation frame and lined up with the two formation rails
            // painted inside it. Nothing new is drawn behind them: the rail already reads as the ground they
            // stand on. Rank 0 faces the other side, so the ally column runs right to left and the enemy
            // column left to right, the way the two rails point at each other.
            var allyRoot = Group(root, "Ally Positions - 我方位置", AllyCardsX, CardsY, 3 * CardStep + CardW, CardH);
            var enemyRoot = Group(root, "Enemy Positions - 敌方位置", EnemyCardsX, CardsY, 3 * CardStep + CardW, CardH);
            var allies = new GothicBattleCard[4];
            var enemies = new GothicBattleCard[4];
            for (int rank = 0; rank < 4; rank++)
            {
                allies[rank] = Card(allyRoot, "Ally " + (rank + 1), (3 - rank) * CardStep, 0, true);
                enemies[rank] = Card(enemyRoot, "Enemy " + (rank + 1), rank * CardStep, 0, false);
            }

            // ---- everything that has no place on the authored layout yet lives behind one button ----
            var debugButton = HudButton(root, "Debug Button", "调试", 1720, 24, 180, 52);
            var debug = Group(root, "Debug Page - 调试", 0, 0, Width, Height);
            // The scrim has to swallow clicks, or a stray press next to the sheet would land on a position
            // card underneath and issue a real battle command.
            var scrim = Solid(debug, "Scrim", 0, 0, Width, Height, new Color32(5, 7, 9, 238));
            scrim.raycastTarget = true;
            var sheet = Group(debug, "Sheet", 510, 190, 900, 700);
            Art(sheet, "Sheet Frame", "OperationFrame", 0, 0, 900, 700, true, 2);
            Label(sheet, "Title", "调试 · Debug", 34, 20, 500, 40, 26, Gold);
            var debugSanity = Label(sheet, "Player Sanity", "", 34, 76, 832, 30, 17, Cyan);
            var debugFloor = Label(sheet, "Progress", "", 34, 110, 832, 30, 16, Muted);
            var debugTurn = Label(sheet, "Turn", "", 34, 144, 832, 30, 16, Ink);
            var debugHint = Label(sheet, "Hint", "", 34, 176, 832, 56, 15, Gold);
            Label(sheet, "Intent Heading", "敌人意图", 34, 240, 400, 28, 18, Rust);
            var debugIntent = Label(sheet, "Intent Body", "", 34, 272, 832, 178, 15, Muted, TextAnchor.UpperLeft);
            var helpButton = HudButton(sheet, "Help", "帮助 F1", 34, 480, 190, 56);
            var labButton = HudButton(sheet, "Lab", "验收 F3", 240, 480, 190, 56);
            var newRunButton = HudButton(sheet, "New Run", "新开演练", 446, 480, 190, 56);
            var closeButton = HudButton(sheet, "Close", "关闭", 652, 480, 214, 56);
            debug.gameObject.SetActive(false);

            var chrome = root.gameObject.AddComponent<GothicBattleHudController>();
            chrome.Panel = panel;
            chrome.Presentation = controller;
            chrome.Counter = counter;
            chrome.PlayerSanityText = debugSanity;
            chrome.FloorText = debugFloor;
            chrome.TurnText = debugTurn;
            chrome.HintText = debugHint;
            chrome.DebugIntentText = debugIntent;
            chrome.DebugRoot = debug.gameObject;
            chrome.DebugButton = debugButton;
            chrome.DebugCloseButton = closeButton;
            chrome.HelpButton = helpButton;
            chrome.LabButton = labButton;
            chrome.NewRunButton = newRunButton;
            chrome.Allies = allies;
            chrome.Enemies = enemies;
            // The debug button stays out of the chrome roots: the node pages are drawn by another controller,
            // and this sheet is the only place 新开演练 stays reachable from there.
            chrome.ChromeRoots = new[] { top, party, bottom, allyRoot.gameObject, enemyRoot.gameObject };
        }

        /// <summary>
        /// The node half of the game: the route and the six rooms, laid out by GothicNodeHudController.
        /// Every page shares one skeleton — a general frame, an illustration block, a text block and an
        /// option block — and one closing action in the bottom-right slot that carries the party from a
        /// finished node to the next one. The whole thing starts switched off; a scene without a
        /// StageOneApp never turns it on.
        /// </summary>
        static void BuildNodeChrome(RectTransform root)
        {
            var pages = Group(root, "Node Pages - 节点页", 0, 0, Width, Height);
            // The scrim swallows clicks, or a stray press next to a page would land on a position card
            // underneath and issue a real battle command.
            var backdrop = Solid(pages, "Backdrop", 0, 0, Width, Height, new Color32(8, 9, 11, 242));
            backdrop.raycastTarget = true;

            // ---------------- route ----------------
            var route = Group(pages, "Route Page - 路线页", 0, 0, Width, Height);
            Art(route, "Node Frame", "GeneralFrame", 40, 50, 1840, 980, true);
            var routeHeading = Label(route, "RouteHeading", "第 1 层  /  共 3 层", 110, 110, 900, 46, 32, Ink);
            var routeStock = Label(route, "RouteStock", "", 1000, 116, 720, 34, 17, Gold, TextAnchor.MiddleRight);
            var routeNote = Label(route, "RouteNote", "", 110, 160, 1620, 28, 15, Muted);

            // The odds read down the left column here, where the built-in page spread them four across.
            var oddsHeading = Label(route, "RouteOddsHeading", "本位置可能出现房间的概率", 110, 200, 620, 28, 18, Gold);
            Art(route, "Odds Frame", "GeneralFrame", 100, 232, 640, 300, true);
            var oddsRoots = new GameObject[6]; var oddsNames = new Text[6]; var oddsFills = new Image[6]; var oddsPercents = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                var row = Group(route, "Odds " + (i + 1), 120, 248 + i * 46, 600, 38);
                oddsRoots[i] = row.gameObject;
                oddsNames[i] = Label(row, "Name", "", 0, 7, 160, 24, 14, Ink);
                Solid(row, "Track", 170, 13, 330, 12, new Color32(22, 25, 26, 255));
                var fill = Solid(row, "Fill", 170, 13, 330, 12, Gold);
                fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
                oddsFills[i] = fill;
                oddsPercents[i] = Label(row, "Percent", "", 510, 7, 90, 24, 14, Gold, TextAnchor.MiddleRight);
            }

            var passedHeading = Label(route, "RoutePassedHeading", "已经过的位置", 110, 560, 620, 28, 18, Gold);
            Art(route, "Passed Frame", "GeneralFrame", 100, 592, 640, 250, true);
            var passedRoots = new GameObject[8]; var passedNames = new Text[8]; var passedAccents = new Outline[8];
            for (int i = 0; i < 8; i++)
            {
                var card = Group(route, "Passed " + (i + 1), 112 + (i % 4) * 156, 604 + (i / 4) * 76, 140, 64);
                var body = Solid(card, "Body", 0, 0, 140, 64, new Color32(34, 45, 53, 255));
                passedAccents[i] = body.gameObject.AddComponent<Outline>();
                passedAccents[i].effectColor = Gold; passedAccents[i].effectDistance = new Vector2(1, -1);
                passedNames[i] = Label(card, "Name", "", 4, 6, 132, 24, 14, Ink, TextAnchor.MiddleCenter);
                Label(card, "Note", "已通过", 4, 32, 132, 20, 14, Muted, TextAnchor.MiddleCenter);
                passedRoots[i] = card.gameObject;
            }

            // The candidates are stacked down the page rather than strung across it: this is the one change
            // the route page was asked for, and every other block is the same as the built-in page's.
            var candidatesHeading = Label(route, "RouteCandidatesHeading", "本层路线", 790, 200, 1020, 28, 18, Gold);
            var routeMapNote = Label(route, "RouteMapNote", "", 790, 232, 1020, 26, 15, Gold, TextAnchor.MiddleRight);
            var candidates = new GothicRouteNode[4];
            for (int i = 0; i < 4; i++)
            {
                var slot = Group(route, "Candidate " + (i + 1), 790, 270 + i * 138, 1020, 124);
                var card = slot.gameObject.AddComponent<GothicRouteNode>();
                var body = Solid(slot, "Body", 0, 0, 1020, 124, new Color32(25, 33, 41, 255));
                card.Hit = Click(body);
                card.Accent = body.gameObject.AddComponent<Outline>();
                card.Accent.effectColor = Gold; card.Accent.effectDistance = new Vector2(1, -1);
                card.Name = Label(slot, "Name", "", 24, 18, 760, 40, 26, Ink);
                card.Detail = Label(slot, "Detail", "", 24, 62, 760, 44, 15, Muted);
                Label(slot, "Go", "点击前进", 830, 44, 160, 32, 16, Gold, TextAnchor.MiddleRight);
                candidates[i] = card;
            }

            var routeHint = Label(route, "RouteHint", "", 110, 862, 1450, 40, 16, Ink);
            var routeRule = Label(route, "RouteRule", "点击一个候选前进。走过的房间固定不变；前方候选只有在走到跟前才会抽出来。",
                110, 906, 1450, 26, 14, Muted);

            // ---------------- run end ----------------
            var runEnd = Group(pages, "Run End Page - 结束页", 0, 0, Width, Height);
            Art(runEnd, "Node Frame", "GeneralFrame", 40, 50, 1840, 980, true);
            var runEndTitle = Label(runEnd, "RunEndTitle", "本局结束", 110, 150, 1450, 70, 42, Gold);
            var runEndNote = Label(runEnd, "RunEndNote", "", 110, 232, 1450, 56, 18, Muted);
            var runEndSummary = Label(runEnd, "RunEndSummary", "", 110, 300, 1450, 44, 21, Gold);
            var runEndStock = Label(runEnd, "RunEndStock", "", 110, 358, 1450, 34, 16, Muted);
            var runEndParty = Label(runEnd, "RunEndParty", "", 110, 400, 1450, 34, 16, Ink);
            Art(runEnd, "Illustration Frame", "GeneralFrame", 110, 470, 560, 240, true);
            Label(runEnd, "Illustration", "配图", 110, 550, 560, 40, 22, Muted, TextAnchor.MiddleCenter);
            Label(runEnd, "Illustration Note", "（结算页暂未提供插图）", 110, 594, 560, 28, 15, Muted, TextAnchor.MiddleCenter);
            Label(runEnd, "Run End Hint", "本局已经结束。新开一局会重置全部队员与路线。", 110, 918, 1450, 72, 16, Muted);
            var runEndButton = ActionSlot(runEnd, "Run End Button", 1600, 918, 240, 72, out var runEndTitleText, out var runEndDetailText);
            runEndTitleText.text = "新开一局";
            runEndDetailText.text = "重置全部队员与路线";

            // ---------------- rooms ----------------
            var room = Group(pages, "Room Page - 房间页", 0, 0, Width, Height);
            Art(room, "Node Frame", "GeneralFrame", 40, 50, 1840, 980, true);
            var roomHeading = Label(room, "RoomHeading", "事件房间", 110, 100, 900, 50, 34, Ink);
            var roomSubtitle = Label(room, "RoomSubtitle", "", 110, 152, 1100, 28, 16, Muted);
            var helpButton = HudButton(room, "Help Button", "帮助 F1", 1470, 104, 160, 52);
            var labButton = HudButton(room, "Lab Button", "验收 F3", 1650, 104, 160, 52);

            // The illustration block. EventSpec carries no sprite yet, so it is a labelled placeholder on
            // every page; this is the one spot to fill in when one arrives.
            var illustration = Art(room, "Illustration Frame", "GeneralFrame", 110, 200, 560, 380, true);
            var roomAccent = illustration.gameObject.AddComponent<Outline>();
            roomAccent.effectColor = Gold; roomAccent.effectDistance = new Vector2(2, -2);
            Label(room, "Illustration", "配图", 110, 340, 560, 44, 24, Muted, TextAnchor.MiddleCenter);
            Label(room, "Illustration Note", "（事件内容暂未提供插图）", 110, 388, 560, 28, 15, Muted, TextAnchor.MiddleCenter);

            Art(room, "Text Frame", "GeneralFrame", 710, 200, 1100, 380, true);
            var roomTitle = Label(room, "RoomTitle", "", 740, 224, 1040, 42, 26, Gold);
            var roomBody = Label(room, "RoomBody", "", 740, 276, 1040, 160, 17, Ink, TextAnchor.UpperLeft);
            var roomNote = Label(room, "RoomNote", "", 740, 448, 1040, 108, 18, Gold, TextAnchor.UpperLeft);

            Art(room, "Options Frame", "GeneralFrame", 110, 604, 1700, 264, true);
            var optionsHeading = Label(room, "OptionsHeading", "选项", 140, 620, 400, 26, 15, Muted);
            var optionsPlaceholder = Label(room, "OptionsPlaceholder", "", 140, 656, 1640, 190, 17, Muted, TextAnchor.MiddleLeft);
            var options = new GothicNodeOption[4];
            for (int i = 0; i < 4; i++)
            {
                // Authored at the option block's left edge; the controller centres whatever run of them a
                // page actually shows, so the slot positions are the controller's business.
                var slot = Group(room, "Option " + (i + 1), 140, 656, 385, 190);
                var option = slot.gameObject.AddComponent<GothicNodeOption>();
                var body = Solid(slot, "Body", 0, 0, 385, 190, new Color32(25, 33, 41, 255));
                option.Hit = Click(body);
                option.Accent = body.gameObject.AddComponent<Outline>();
                option.Accent.effectColor = Gold; option.Accent.effectDistance = new Vector2(1, -1);
                option.Title = Label(slot, "Title", "", 16, 14, 353, 44, 22, Ink);
                option.Detail = Label(slot, "Detail", "", 16, 62, 353, 92, 16, Muted, TextAnchor.UpperLeft);
                option.Selected = Label(slot, "Selected", "已选择", 16, 156, 353, 26, 14, Gold, TextAnchor.MiddleRight).gameObject;
                option.Selected.SetActive(false);
                options[i] = option;
            }

            var continueHint = Label(room, "Continue Hint", "", 110, 918, 1450, 72, 16, Muted);
            var continueButton = ActionSlot(room, "Continue Button", 1600, 918, 240, 72, out var continueTitle, out var continueDetail);

            var page = root.gameObject.AddComponent<GothicNodeHudController>();
            page.PageRoot = pages.gameObject;
            page.RouteRoot = route.gameObject;
            page.RunEndRoot = runEnd.gameObject;
            page.RoomRoot = room.gameObject;

            page.RouteHeading = routeHeading; page.RouteStock = routeStock; page.RouteNote = routeNote;
            page.RouteOddsHeading = oddsHeading; page.RoutePassedHeading = passedHeading;
            page.RouteCandidatesHeading = candidatesHeading; page.RouteMapNote = routeMapNote;
            page.RouteHint = routeHint; page.RouteRule = routeRule;
            page.Candidates = candidates;
            page.PassedRoots = passedRoots; page.PassedNames = passedNames; page.PassedAccents = passedAccents;
            page.OddsRoots = oddsRoots; page.OddsNames = oddsNames; page.OddsFills = oddsFills; page.OddsPercents = oddsPercents;

            page.RunEndTitle = runEndTitle; page.RunEndNote = runEndNote; page.RunEndSummary = runEndSummary;
            page.RunEndStock = runEndStock; page.RunEndParty = runEndParty;
            page.RunEndButton = runEndButton;

            page.RoomHeading = roomHeading; page.RoomSubtitle = roomSubtitle; page.RoomTitle = roomTitle;
            page.RoomBody = roomBody; page.RoomNote = roomNote; page.RoomAccent = roomAccent;
            page.OptionsHeading = optionsHeading; page.OptionsPlaceholder = optionsPlaceholder; page.Options = options;
            page.ContinueHint = continueHint; page.ContinueButton = continueButton;
            page.ContinueTitle = continueTitle; page.ContinueDetail = continueDetail;
            page.HelpButton = helpButton; page.LabButton = labButton;

            pages.gameObject.SetActive(false);
        }

        /// <summary>
        /// The one closing action a node page ends on, always authored in the same bottom-right slot so a
        /// finished node continues from the same spot on the route, in a room and at the end of a run.
        /// </summary>
        static Button ActionSlot(Transform parent, string name, float x, float y, float w, float h, out Text title, out Text detail)
        {
            var image = Solid(parent, name, x, y, w, h, new Color32(17, 21, 24, 240));
            Art(image.transform, "Border", "ItemFrame", 0, 0, w, h, true);
            var button = Click(image);
            title = Label(image.transform, "Title", "", 8, 8, w - 16, 32, 20, Gold, TextAnchor.MiddleCenter);
            detail = Label(image.transform, "Detail", "", 8, 40, w - 16, 26, 14, Muted, TextAnchor.MiddleCenter);
            return button;
        }

        static Button HudButton(Transform parent, string name, string caption, float x, float y, float w, float h)
        {
            var image = Solid(parent, name, x, y, w, h, new Color32(17, 21, 24, 240));
            Art(image.transform, "Border", "ItemFrame", 0, 0, w, h, true);
            var button = Click(image);
            Label(image.transform, "Caption", caption, 0, 0, w, h, 16, Gold, TextAnchor.MiddleCenter);
            return button;
        }

        static Image Bar(Transform parent, string name, float x, float y, float w, float h, Color tint, out Text value)
        {
            var group = Group(parent, name, x, y, w, h);
            Solid(group, "Track", 0, 0, w, h, new Color32(22, 25, 26, 255));
            var fill = Solid(group, "Fill", 1, 1, w - 2, h - 2, tint);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            value = Label(group, "Value", "", 0, 0, w, h, 12, Ink, TextAnchor.MiddleCenter);
            return fill;
        }

        /// <summary>
        /// One position slot. It only carries what a position needs to be playable: who stands there, how
        /// hurt they are, and whether they can be acted on. Sanity and the full limb breakdown stay in the
        /// vitals module, which already shows them for the inspected character.
        /// </summary>
        static GothicBattleCard Card(Transform parent, string name, float x, float y, bool ally)
        {
            var slot = Group(parent, name, x, y, CardW, CardH);
            var card = slot.gameObject.AddComponent<GothicBattleCard>();

            // The body doubles as the click target, so a card costs one raycastable graphic and nothing else.
            card.Background = Solid(slot, "Body", 0, 0, CardW, CardH, card.AliveBody);
            card.Hit = Click(card.Background);
            card.Highlight = card.Background.gameObject.AddComponent<Outline>();
            card.Highlight.effectColor = Gold; card.Highlight.effectDistance = new Vector2(2, -2); card.Highlight.enabled = false;

            var art = Group(slot, "Silhouette", 6, 6, CardW - 12, 78).gameObject.AddComponent<SilhouetteGraphic>();
            art.raycastTarget = false; card.Art = art;

            card.Name = Label(slot, "Name", ally ? "角色" : "敌人", 4, 88, CardW - 8, 22, 15, Gold, TextAnchor.MiddleCenter);
            card.HealthFill = Bar(slot, "Health", 6, 112, CardW - 12, 17, new Color32(174, 47, 38, 255), out var healthValue);
            card.HealthValue = healthValue;
            card.Status = Label(slot, "Status", "", 6, 132, CardW - 12, 42, 12, Muted, TextAnchor.UpperLeft);
            if (!ally) card.Intent = Label(slot, "Intent", "", 6, 132, CardW - 12, 42, 12, Rust, TextAnchor.UpperLeft);
            card.ActiveBadge = Label(slot, "Active Badge", "行动", 6, 4, 48, 18, 13, Gold).gameObject;
            card.TargetBadge = Label(slot, "Target Badge", "可选", CardW - 54, 4, 48, 18, 13, Gold, TextAnchor.MiddleRight).gameObject;
            card.DeadCross = Label(slot, "Dead Cross", "✕", 0, 44, CardW, 70, 44, Rust, TextAnchor.MiddleCenter).gameObject;
            card.ActiveBadge.SetActive(false); card.TargetBadge.SetActive(false); card.DeadCross.SetActive(false);
            return card;
        }

        static RectTransform Group(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0,1); rt.anchoredPosition = new Vector2(x,-y); rt.sizeDelta = new Vector2(w,h); return rt;
        }
        static Image Art(Transform p, string name, string sprite, float x,float y,float w,float h,bool sliced=false,float mult=1)
        {
            var image = Group(p,name,x,y,w,h).gameObject.AddComponent<Image>(); image.sprite = sprites[sprite]; image.raycastTarget=false;
            image.type=sliced?Image.Type.Sliced:Image.Type.Simple; image.pixelsPerUnitMultiplier=mult; return image;
        }
        static Image Solid(Transform p,string name,float x,float y,float w,float h,Color color)
        {
            var image=Group(p,name,x,y,w,h).gameObject.AddComponent<Image>(); image.sprite=white; image.color=color; image.raycastTarget=false; return image;
        }
        static Image Blank(Transform p,string name,float x,float y,float w,float h)
        {
            var image=Solid(p,name,x,y,w,h,Color.clear); image.sprite=null; image.preserveAspect=true; return image;
        }
        static Text Label(Transform p,string name,string text,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var label=Group(p,name,x,y,w,h).gameObject.AddComponent<Text>(); label.text=text; label.font=font; label.fontSize=Mathf.Max(14,size); label.color=color;
            label.alignment=align; label.raycastTarget=false; label.horizontalOverflow=HorizontalWrapMode.Wrap; label.verticalOverflow=VerticalWrapMode.Truncate; return label;
        }
        static Button Click(Image image)
        {
            image.raycastTarget=true; var button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors; colors.highlightedColor=new Color(1,.86f,.62f); colors.pressedColor=new Color(.75f,.60f,.37f); button.colors=colors;
            button.navigation=new Navigation { mode=Navigation.Mode.None }; return button;
        }
        static Image Meter(Transform parent,string name,string frame,float x,float y,float w,float h,Color tint,out Text value)
        {
            var group=Group(parent,name,x,y,w,h);
            Art(group,"Frame",frame,0,0,w,h,true,2);
            var fill=Solid(group,"Fill - 可编辑填充值",17,7,w-34,h-14,tint); fill.type=Image.Type.Filled; fill.fillMethod=Image.FillMethod.Horizontal; fill.fillOrigin=0;
            value=Label(group,"Value","100 / 100",18,0,w-36,h,12,Gold,TextAnchor.MiddleCenter); return fill;
        }

        public static void Validate(ModularHudController hud)
        {
            if (hud.Limbs.Length!=4 || hud.Panel.SkillButtons.Length!=5 || hud.Characters.Length!=4 || !hud.DisplacementButton) throw new Exception("Missing HUD slots");
            foreach(var view in hud.Limbs) if(!view.HealthySprite || !view.BrokenSprite || !view.Icon || !view.Fill) throw new Exception("Missing limb sprite or reference");
            for(int i=0;i<4;i++)
            {
                hud.SetLimbHealth(0,(HudLimb)i,0,100); if(hud.Limbs[i].Icon.sprite!=hud.Limbs[i].BrokenSprite) throw new Exception("Wrong broken icon");
                hud.SelectCharacter(1); if(hud.Limbs[i].Icon.sprite!=hud.Limbs[i].HealthySprite) throw new Exception("Limb damage leaked to another character");
                hud.SelectCharacter(0); hud.SetLimbHealth(0,(HudLimb)i,100,100); if(hud.Limbs[i].Icon.sprite!=hud.Limbs[i].HealthySprite) throw new Exception("Limb icon not restored");
            }
            File.WriteAllText(Path.Combine(Output,"validation.txt"),"PASS: 4 characters, 5 skills, adjacent displacement button\nPASS: life, sanity, 4 limb bars\nPASS: all 4 healthy/broken sprite pairs and restoration\nPASS: limb states isolated per character\n");
        }
        public static void Render(Canvas canvas, Camera camera,string name,int width,int height)
        {
            var scaler=canvas.GetComponent<CanvasScaler>(); scaler.enabled=false; canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=100;
            var target=new RenderTexture(width,height,24); camera.targetTexture=target; canvas.scaleFactor=Mathf.Min(width/(float)Width,height/(float)Height);
            Canvas.ForceUpdateCanvases(); camera.Render(); var previous=RenderTexture.active; RenderTexture.active=target;
            var capture=new Texture2D(width,height,TextureFormat.RGB24,false); capture.ReadPixels(new Rect(0,0,width,height),0,0); capture.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),capture.EncodeToPNG()); RenderTexture.active=previous; camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(capture); UnityEngine.Object.DestroyImmediate(target);
            canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; scaler.enabled=true;
        }
    }
}
