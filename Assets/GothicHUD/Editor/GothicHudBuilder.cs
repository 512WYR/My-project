using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.GothicHUDEditor
{
    public static class GothicHudBuilder
    {
        const string Root = "Assets/GothicHUD";
        public const string ScenePath = Root + "/Scenes/GothicBattleHUD.unity";
        public const string PrefabPath = Root + "/Prefabs/GothicBattleHUD.prefab";
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        static Material keyed;
        static Font font;
        static readonly Color Gold = new Color32(218, 182, 119, 255);
        static readonly Color Muted = new Color32(143, 126, 101, 255);
        const float Width = 1851, Height = 850;

        // Coordinates are top-left source pixels, making the atlas easy to re-slice.
        static SpriteMetaData Slice(string name, float x, float y, float w, float h, int textureHeight, Vector4 border = default)
        {
            return new SpriteMetaData { name = name, rect = new Rect(x, textureHeight - y - h, w, h), pivot = new Vector2(.5f, .5f), alignment = 0, border = border };
        }

        static void Import(string file, SpriteMetaData[] slices)
        {
            string path = Root + "/Art/" + file + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = slices.Select(s => new SpriteRect {
                name = s.name, rect = s.rect, pivot = s.pivot, border = s.border,
                alignment = (SpriteAlignment)s.alignment,
                spriteID = previous.TryGetValue(s.name, out var id) ? id : GUID.Generate()
            }).ToArray();
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()) Sprites[sprite.name] = sprite;
        }

        static void PrepareArt()
        {
            AssetDatabase.Refresh();
            Sprites.Clear();
            Import("ModuleAtlas", new[] {
                Slice("Lantern", 484, 17, 174, 240, 887),
                Slice("OrnamentRail", 1078, 92, 641, 78, 887, new Vector4(88, 0, 88, 0)),
                Slice("PortraitFrame", 40, 233, 300, 292, 887),
                Slice("SlotFrame", 721, 765, 113, 106, 887, new Vector4(17, 17, 17, 17)),
                Slice("SmallPanel", 71, 773, 253, 91, 887, new Vector4(31, 25, 31, 25)),
                Slice("DetailPanel", 1024, 521, 702, 236, 887, new Vector4(60, 45, 105, 44)),
                Slice("FineDivider", 1063, 628, 571, 22, 887, new Vector4(17, 0, 17, 0)),
                Slice("HeadIcon", 892, 276, 70, 51, 887),
                Slice("ArmIcon", 894, 333, 76, 53, 887),
                Slice("CoatIcon", 891, 393, 73, 55, 887),
                Slice("BootIcon", 899, 453, 58, 51, 887),
                Slice("EquipmentField", 984, 279, 214, 44, 887, new Vector4(14, 12, 14, 12))
            });
            Import("LayoutReference", new[] {
                Slice("PortraitSilhouette", 55, 119, 68, 79, 850),
                Slice("MainSilhouette", 90, 615, 112, 127, 850),
                Slice("PartyPipe", 16, 102, 23, 400, 850),
                Slice("VerticalDivider", 472, 589, 19, 215, 850),
                Slice("AttackIcon", 255, 695, 29, 23, 850),
                Slice("AccuracyIcon", 255, 719, 29, 22, 850),
                Slice("DefenseIcon", 255, 743, 29, 25, 850),
                Slice("SpeedIcon", 255, 774, 29, 22, 850)
            });
            Import("PanelFrame", new[] { Slice("OuterFrame", 0, 0, 1983, 793, 793, new Vector4(300, 185, 250, 245)) });
            Import("RoundCounter", new[] { Slice("RoundMechanism", 0, 100, 2172, 540, 724) });
            var digits = new SpriteMetaData[10];
            for (int i = 0; i < 10; i++) digits[i] = Slice("Digit" + i, 37 + i * 213, 244, 177, 230, 724);
            Import("DigitAtlas", digits);
            string materialPath = Root + "/Materials/CheckerboardRemoval.mat";
            keyed = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!keyed) { keyed = new Material(Shader.Find("GothicHUD/AtlasTransparency")); AssetDatabase.CreateAsset(keyed, materialPath); }
            font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Art/HudChinese.ttf");
        }

        [MenuItem("GameMake/Gothic HUD/Create Editable HUD Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Application.isBatchMode && File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("重建 HUD", "这会重建生成的 HUD 场景和预制体。请先保存自行修改的副本。", "重建", "取消")) return;
            PrepareArt();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = Height / 2;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(7, 8, 10, 255);
            camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var go = new GameObject("GothicBattleHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GothicHudPanel));
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = go.GetComponent<RectTransform>();
            var hud = go.GetComponent<GothicHudPanel>();

            var top = Group(root, "Round Counter - 机械回合计数器", 0, 0, 1000, 260);
            top.anchorMin = top.anchorMax = new Vector2(.5f, 1); top.pivot = new Vector2(.5f, 1); top.anchoredPosition = Vector2.zero;
            Art(top, "Mechanical Counter Frame", "RoundMechanism", 0, 0, 1000, 250);
            var cover = Group(top, "Digit Backplate", 459, 87, 82, 66).gameObject.AddComponent<Image>();
            cover.color = new Color32(12, 12, 11, 255); cover.raycastTarget = false;
            var digitsContainer = Group(top, "Editable Digits - 可编辑数字", 459, 87, 82, 66);
            digitsContainer.pivot = new Vector2(.5f, .5f); digitsContainer.anchoredPosition = new Vector2(500, -120);
            var counter = top.gameObject.AddComponent<MechanicalRoundCounter>();
            counter.DigitContainer = digitsContainer;
            counter.DigitSprites = Enumerable.Range(0, 10).Select(i => Sprites["Digit" + i]).ToArray();
            counter.DigitSlots = new Image[10];
            for (int i = 0; i < 10; i++) counter.DigitSlots[i] = Art(digitsContainer, "Digit " + i, "Digit0", 0, 0, 37, 59, false, false);
            counter.RefreshDisplay();

            var party = Group(root, "Party Portraits - 队伍头像", 14, 99, 138, 410);
            Art(party, "Mechanical Spine", "PartyPipe", 0, 0, 23, 400, false, false);
            hud.PartyButtons = new Button[4]; hud.PartyPortraits = new Image[4]; hud.PartySelections = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                var slot = Group(party, "Party Slot " + (i + 1), 17, 2 + i * 99, 112, 101);
                var background = Art(slot, "Portrait Frame", "SlotFrame", 0, 0, 112, 101, true);
                hud.PartyButtons[i] = Clickable(background);
                hud.PartyPortraits[i] = Art(slot, "Portrait - 替换此图片", "PortraitSilhouette", 22, 14, 68, 78, false, false);
                var selection = Label(slot, "Selected Marker", "◀", -20, 29, 29, 40, 27, Gold);
                hud.PartySelections[i] = selection.gameObject; selection.gameObject.SetActive(i == 0);
            }

            var lower = Group(root, "Bottom HUD - 底部面板", 0, 0, Width, 305);
            lower.anchorMin = lower.anchorMax = new Vector2(.5f, 0); lower.pivot = new Vector2(.5f, 0); lower.anchoredPosition = Vector2.zero;
            Art(lower, "Allied Formation Ornament", "OrnamentRail", 126, -37, 795, 51, true);
            Art(lower, "Enemy Formation Ornament", "OrnamentRail", 1155, -37, 640, 51, true);
            var outer = Art(lower, "Outer Mechanical Frame - 九宫格外框", "OuterFrame", 0, 9, Width, 297, true);
            outer.pixelsPerUnitMultiplier = 2.65f;

            var character = Group(lower, "Character Panel - 角色信息", 20, 41, 464, 234);
            Art(character, "Portrait Frame and Lantern", "PortraitFrame", -4, -6, 234, 230);
            hud.MainPortrait = Art(character, "Portrait - 替换此图片", "MainSilhouette", 71, 47, 90, 103, false, false);
            hud.CharacterName = Label(character, "Character Name", "角色名称", 233, 32, 197, 35, 24, Gold);
            hud.CharacterSubtitle = Label(character, "Character Subtitle", "职业 / 称号", 235, 67, 197, 24, 14, Muted);
            hud.AttributeValues = new Text[4];
            string[] attributes = { "Attack", "Accuracy", "Defense", "Speed" };
            string[] attributeLabels = { "攻击", "命中", "防御", "速度" };
            for (int i = 0; i < 4; i++)
            {
                var row = Group(character, attributes[i] + " - " + attributeLabels[i], 237, 97 + i * 30, 191, 27);
                Art(row, "Icon", attributes[i] + "Icon", 0, 1, 27, 25, false, false);
                Label(row, "Label", attributeLabels[i], 32, 0, 67, 26, 15, Muted);
                Art(row, "Value Frame", "EquipmentField", 101, 0, 83, 26, true);
                hud.AttributeValues[i] = Label(row, "Value", "—", 110, 0, 64, 26, 17, Gold, TextAnchor.MiddleCenter);
            }

            Art(lower, "Divider Character Equipment", "VerticalDivider", 477, 43, 15, 221, false, false);
            var equipment = Group(lower, "Equipment Panel - 装备栏", 501, 66, 211, 208);
            hud.EquipmentValues = new Text[4];
            string[] equipmentNames = { "Head", "Arm", "Coat", "Boot" };
            for (int i = 0; i < 4; i++)
            {
                var row = Group(equipment, equipmentNames[i] + " Slot", 0, i * 49, 206, 44);
                Art(row, "Equipment Icon", equipmentNames[i] + "Icon", 0, 1, 40, 39);
                Art(row, "Equipment Field", "EquipmentField", 49, 5, 155, 34, true);
                hud.EquipmentValues[i] = Label(row, "Equipment Name", "", 59, 5, 135, 34, 16, Gold);
            }

            Art(lower, "Divider Equipment Skills", "VerticalDivider", 716, 43, 15, 221, false, false);
            var skills = Group(lower, "Skills Panel - 五个技能槽", 741, 61, 575, 195);
            Art(skills, "Upper Divider", "FineDivider", 0, 0, 575, 13, true);
            Art(skills, "Lower Divider", "FineDivider", 0, 173, 575, 13, true);
            hud.SkillButtons = new Button[5]; hud.SkillIcons = new Image[5]; hud.SkillHotkeys = new Text[5]; hud.SkillSelections = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                var slot = Group(skills, "Skill Slot " + (i + 1), i * 117, 18, 107, 149);
                var frame = Art(slot, "Skill Frame", "SlotFrame", 0, 0, 107, 113, true);
                hud.SkillButtons[i] = Clickable(frame);
                var icon = Group(slot, "Skill Icon - 替换Sprite并设Alpha为1", 12, 13, 83, 87).gameObject.AddComponent<Image>();
                icon.color = new Color(1, 1, 1, 0); icon.preserveAspect = true; icon.raycastTarget = false; hud.SkillIcons[i] = icon;
                Art(slot, "Hotkey Plate", "SmallPanel", 26, 113, 55, 33, true).pixelsPerUnitMultiplier = 2;
                hud.SkillHotkeys[i] = Label(slot, "Hotkey - 可编辑数字", (i + 1).ToString(), 28, 115, 51, 27, 23, Gold, TextAnchor.MiddleCenter);
                var selected = Label(slot, "Selected Marker", "◆", 36, -17, 35, 24, 20, Gold, TextAnchor.MiddleCenter);
                hud.SkillSelections[i] = selected.gameObject; selected.gameObject.SetActive(false);
            }

            Art(lower, "Divider Skills Description", "VerticalDivider", 1331, 43, 15, 221, false, false);
            var detail = Group(lower, "Description Panel - 技能说明", 1355, 53, 450, 206);
            hud.DescriptionTitle = Label(detail, "Description Title", "", 20, 5, 401, 37, 23, Gold);
            Art(detail, "Title Divider", "FineDivider", 10, 55, 427, 13, true);
            hud.DescriptionBody = Label(detail, "Description Body", "", 22, 77, 397, 117, 18, Muted, TextAnchor.UpperLeft);

            PrefabUtility.SaveAsPrefabAsset(top.gameObject, Root + "/Prefabs/MechanicalRoundCounter.prefab");
            PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Validate(hud);
            RenderPreviews(canvas, camera);
            Selection.activeGameObject = go;
            Debug.Log("GOTHIC_HUD_CREATED " + ScenePath);
        }

        static RectTransform Group(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }

        static Image Art(Transform parent, string name, string sprite, float x, float y, float w, float h, bool sliced = false, bool transparency = true)
        {
            var image = Group(parent, name, x, y, w, h).gameObject.AddComponent<Image>(); image.sprite = Sprites[sprite];
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false; if (transparency) image.material = keyed; return image;
        }

        static Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var label = Group(parent, name, x, y, w, h).gameObject.AddComponent<Text>(); label.font = font; label.text = value;
            label.fontSize = size; label.color = color; label.alignment = align; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }

        static Button Clickable(Image image)
        {
            image.raycastTarget = true; var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .9f, .7f);
            colors.pressedColor = new Color(.8f, .65f, .4f); button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; return button;
        }

        static void Validate(GothicHudPanel hud)
        {
            if (hud.PartyButtons.Length != 4 || hud.SkillButtons.Length != 5 || hud.EquipmentValues.Length != 4) throw new Exception("HUD slot count mismatch");
            var graphics = hud.GetComponentsInChildren<Graphic>(true);
            if (graphics.OfType<Image>().Any(i => !i.sprite && !hud.SkillIcons.Contains(i) && i.name != "Digit Backplate")) throw new Exception("Missing sprite");
            if (graphics.OfType<Text>().Any(t => !t.font)) throw new Exception("Missing font");
            if (hud.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("Health") || t.name.Contains("Sanity"))) throw new Exception("Excluded meter present");
            // Exercise selection without changing the saved authored defaults.
            hud.SelectParty(3); if (!hud.PartySelections[3].activeSelf || hud.PartySelections[0].activeSelf) throw new Exception("Party selection failed");
            hud.SelectParty(0); hud.SelectSkill(4); if (!hud.SkillSelections[4].activeSelf) throw new Exception("Skill selection failed");
            foreach (var selection in hud.SkillSelections) selection.SetActive(false);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../交付/哥特HUD")); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "validation.txt"), "PASS: Unity scene and prefab saved\nPASS: 4 party buttons, 5 skill buttons, 4 equipment fields\nPASS: all sprite/font references resolved\nPASS: no health/sanity meters\nPASS: selection interactions\nUI objects: " + hud.GetComponentsInChildren<RectTransform>(true).Length);
        }

        static void RenderPreviews(Canvas canvas, Camera camera)
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../交付/哥特HUD"));
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 100;
            foreach (var size in new[] { new Vector2Int(1851, 850), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) })
            {
                var target = new RenderTexture(size.x, size.y, 24); camera.targetTexture = target;
                canvas.scaleFactor = Mathf.Min(size.x / Width, size.y / Height);
                Canvas.ForceUpdateCanvases(); camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); capture.Apply();
                File.WriteAllBytes(Path.Combine(output, "HUD-" + size.x + "x" + size.y + ".png"), capture.EncodeToPNG());
                RenderTexture.active = previous; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(capture); UnityEngine.Object.DestroyImmediate(target);
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; scaler.enabled = true;
            // Preview changes are intentionally not saved into the reusable scene/prefab.
        }
    }
}
