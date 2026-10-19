using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // The rule in the architecture doc is that pages are prefabs. The menu is the exception: it is a
    // global overlay with no artwork or per-page state, so building it in code keeps mainScene from
    // needing another rebuild whenever a label or a button moves. The primitives below mirror the
    // editor builders so the menu still looks like the rest of the game.
    internal static class MenuUi
    {
        public static readonly Color Gold = new Color32(219, 187, 131, 255);
        public static readonly Color Ink = new Color32(230, 224, 212, 255);
        public static readonly Color Muted = new Color32(162, 144, 111, 255);
        public static readonly Color Panel = new Color32(14, 17, 18, 246);
        public static readonly Color ButtonFill = new Color32(22, 26, 29, 245);
        public static readonly Color Line = new Color32(111, 96, 70, 255);

        private static Font font;

        public static Font Font
        {
            get
            {
                if (!font) font = ResolveFont();
                return font;
            }
        }

        public static Canvas MakeCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            // An authored prefab keeps its root scale; a code-created one must set it explicitly or the
            // canvas inherits whatever the parent had and can end up invisible.
            go.GetComponent<RectTransform>().localScale = Vector3.one;
            return canvas;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            return rt;
        }

        // Top-left layout, the convention every editor builder uses for fixed 1920x1080 coordinates.
        public static RectTransform Group(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // Screen-centre layout: menus stay centred on ultrawide and 4:3 windows because the anchors,
        // not hard-coded 1920x1080 offsets, decide where the block sits.
        public static RectTransform Center(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Solid(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Group(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image CenterSolid(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Center(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var text = Group(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            Style(text, value, size, color, align);
            return text;
        }

        public static Text CenterLabel(Transform parent, string name, string value, float x, float y, float w, float h,
            int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var text = Center(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            Style(text, value, size, color, align);
            return text;
        }

        private static void Style(Text text, string value, int size, Color color, TextAnchor align)
        {
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        public static Button Click(Image image)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.12f, 1.04f);
            colors.pressedColor = new Color(.78f, .72f, .58f);
            colors.disabledColor = new Color(.5f, .5f, .5f, 1);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        // Centred menu button: a dark plate with a gold edge, matching the HUD button frames without
        // pulling in the 9-slice art (which is not under Resources).
        public static Button MenuButton(Transform parent, string name, string caption, float y, float w, float h,
            out Text label)
        {
            var plate = CenterSolid(parent, name, 0, y, w, h, ButtonFill);
            // A fixed gold edge rather than a sprite: the authored button frames are outside Resources.
            CenterSolid(plate.transform, "Accent", -w / 2f + 2f, 0, 3, h - 8, Gold);
            label = CenterLabel(plate.transform, "Caption", caption, 0, 0, w - 24, h, 25, Ink);
            return Click(plate);
        }

        public static Slider SliderBar(Transform parent, string name, float y, float w, float h)
        {
            var rt = Center(parent, name, 0, y, w, h);
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            var background = Solid(rt, "Background", 0, 0, w, h, new Color32(31, 35, 37, 255));
            background.raycastTarget = true;
            slider.targetGraphic = background;

            var fill = Solid(rt, "Fill", 0, 0, w, h, Gold);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, .25f);
            fillRect.anchorMax = new Vector2(0f, .75f);
            fillRect.pivot = new Vector2(0f, .5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;
            slider.fillRect = fillRect;

            var handle = Solid(rt, "Handle", 0, 0, 18, h + 10, Ink);
            var handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, .5f);
            handleRect.anchorMax = new Vector2(0f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            // Slider drives the y anchors to 0..1, so a 10px delta makes the handle 10px taller than
            // the bar; the x anchors pin to one point and sizeDelta.x is the true width.
            handleRect.sizeDelta = new Vector2(18, 10);
            slider.handleRect = handleRect;
            return slider;
        }

        private static Font ResolveFont()
        {
            // The bundled HudChinese font is not under Resources, but mainScene's prefab instances do
            // reference it, so it is already loaded by the time StageOneApp.Start runs.
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            for (int i = 0; i < fonts.Length; i++)
                if (fonts[i] && fonts[i].name == "HudChinese") return fonts[i];

            var texts = Resources.FindObjectsOfTypeAll<Text>();
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] && texts[i].font) return texts[i].font;

            // Last resort for a scene with no authored UI: pick an OS font that covers Chinese, the
            // same list BattleLab already uses for its IMGUI overlay.
            string match = null;
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                var preferred = new[]
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Noto Sans CJK SC",
                    "Source Han Sans SC", "PingFang SC", "Arial Unicode MS"
                };
                if (installed != null)
                {
                    foreach (var wanted in preferred)
                    {
                        for (int i = 0; i < installed.Length; i++)
                        {
                            if (!string.Equals(installed[i], wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
                            match = installed[i];
                            break;
                        }
                        if (match != null) break;
                    }
                }
            }
            catch { match = null; }

            try { return Font.CreateDynamicFontFromOSFont(match ?? "Arial", 16); }
            catch { return null; }
        }
    }
}
