using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Ruin.Runtime
{
    // Small IMGUI toolkit: one dynamic CJK font, styles, and text buffers that do not fight the user's
    // cursor. Every keyed field needs a stable unique key; pages build theirs from slot + field names.
    public static class BattleLabUi
    {
        public static GUIStyle Title;
        public static GUIStyle Section;
        public static GUIStyle SubHeader;
        public static GUIStyle Hint;
        public static GUIStyle Error;
        public static GUIStyle Body;
        public static GUIStyle FoldoutStyle;

        // Card tints multiply the box background, so they stay subtle in both the runtime and editor skins.
        public static readonly Color HeroTint = new Color(0.74f, 0.84f, 0.98f, 1f);
        public static readonly Color EnemyTint = new Color(0.98f, 0.79f, 0.74f, 1f);
        public static readonly Color NeutralTint = new Color(0.88f, 0.9f, 0.94f, 1f);

        private static Font font;
        private static bool fontAttempted;
        private static readonly Dictionary<string, string> Buffers = new Dictionary<string, string>();

        public static void EnsureSkin()
        {
            if (!fontAttempted)
            {
                fontAttempted = true;
                font = LoadCjkFont();
            }
            if (font != null) GUI.skin.font = font;
            if (Title == null)
            {
                Title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                Section = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                Section.normal.textColor = new Color(0.78f, 0.88f, 1f);
                SubHeader = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                SubHeader.normal.textColor = new Color(0.95f, 0.82f, 0.55f);
                Body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                Hint = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                Hint.normal.textColor = new Color(0.72f, 0.72f, 0.72f);
                Error = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                Error.normal.textColor = new Color(1f, 0.55f, 0.45f);
                // Runtime GUILayout has no Foldout, so the header is a content-sized button.
                FoldoutStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(8, 6, 3, 3),
                    richText = false
                };
                FoldoutStyle.normal.textColor = new Color(0.86f, 0.92f, 1f);
                FoldoutStyle.hover.textColor = new Color(1f, 0.92f, 0.7f);
                FoldoutStyle.active.textColor = new Color(1f, 0.92f, 0.7f);
                FoldoutStyle.onNormal.textColor = FoldoutStyle.hover.textColor;
                FoldoutStyle.onHover.textColor = FoldoutStyle.hover.textColor;
                FoldoutStyle.onActive.textColor = FoldoutStyle.hover.textColor;
            }
        }

        private static Font LoadCjkFont()
        {
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
                    foreach (var name in preferred)
                    {
                        match = installed.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                        if (match != null) break;
                    }
                    if (match == null)
                        match = installed.FirstOrDefault(x =>
                            x.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0
                            || x.IndexOf("CJK", StringComparison.OrdinalIgnoreCase) >= 0
                            || x.IndexOf("Hei", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            catch { match = null; }
            try
            {
                return Font.CreateDynamicFontFromOSFont(match ?? "Arial", 16);
            }
            catch
            {
                return null;
            }
        }

        public static void Label(string text)
        {
            GUILayout.Label(text ?? "", Body);
        }

        public static void Label(string text, GUIStyle style)
        {
            GUILayout.Label(text ?? "", style);
        }

        public static void Small(string text)
        {
            GUILayout.Label(text ?? "", Hint);
        }

        public static void ErrorLabel(string text)
        {
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, Error);
        }

        public static void BoxTitle(string text)
        {
            GUILayout.Label(text ?? "", Section);
        }

        public static void SubTitle(string text)
        {
            GUILayout.Space(2);
            GUILayout.Label(text ?? "", SubHeader);
        }

        // Foldout row with an always-visible summary, so a collapsed group still tells you what is inside.
        public static bool Foldout(bool open, string title, string summary = null)
        {
            GUILayout.BeginHorizontal();
            string label = (open ? "[-] " : "[+] ") + (title ?? "");
            if (GUILayout.Button(label, FoldoutStyle, GUILayout.ExpandWidth(false))) open = !open;
            if (!string.IsNullOrEmpty(summary))
                GUILayout.Label(summary, Hint, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return open;
        }

        // Tinted card: only the surrounding box is tinted; fields inside keep the normal skin colors.
        public static void BeginCard(Color tint)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = previous;
        }

        public static void EndCard()
        {
            GUILayout.EndVertical();
        }

        // Indents a whole block by nesting it in a horizontal row; useful for group contents inside a card.
        public static void BeginIndent(float width = 12f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(width);
            GUILayout.BeginVertical();
        }

        public static void EndIndent()
        {
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        public static bool Button(string text, bool enabled = true, params GUILayoutOption[] options)
        {
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            bool clicked = GUILayout.Button(text, options);
            GUI.enabled = was;
            return clicked;
        }

        public static bool MiniButton(string text, bool enabled = true)
        {
            return Button(text, enabled, GUILayout.Width(Mathf.Max(48, text.Length * 14)));
        }

        public static bool Toggle(string text, ref bool value, bool enabled = true)
        {
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            bool next = GUILayout.Toggle(value, text);
            GUI.enabled = was;
            if (next != value) { value = next; return true; }
            return false;
        }

        public static string TextField(string key, string value, params GUILayoutOption[] options)
        {
            value = value ?? "";
            GUI.SetNextControlName(key);
            if (!Buffers.TryGetValue(key, out string cached)) cached = value;
            else if (GUI.GetNameOfFocusedControl() != key && cached != value) cached = value;
            string next = GUILayout.TextField(cached, options);
            Buffers[key] = next;
            return next;
        }

        public static string TextArea(string key, string value, params GUILayoutOption[] options)
        {
            value = value ?? "";
            GUI.SetNextControlName(key);
            if (!Buffers.TryGetValue(key, out string cached)) cached = value;
            else if (GUI.GetNameOfFocusedControl() != key && cached != value) cached = value;
            string next = GUILayout.TextArea(cached, options);
            Buffers[key] = next;
            return next;
        }

        public static int IntField(string key, int value, params GUILayoutOption[] options)
        {
            string text = TextField(key, value.ToString(CultureInfo.InvariantCulture), options);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : value;
        }

        public static long LongField(string key, long value, params GUILayoutOption[] options)
        {
            string text = TextField(key, value.ToString(CultureInfo.InvariantCulture), options);
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : value;
        }

        public static double DoubleField(string key, double value, params GUILayoutOption[] options)
        {
            string text = TextField(key, value.ToString("0.###", CultureInfo.InvariantCulture), options);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : value;
        }

        public static float FloatField(string key, float value, params GUILayoutOption[] options)
        {
            string text = TextField(key, value.ToString("0.###", CultureInfo.InvariantCulture), options);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : value;
        }

        public static int SelectionGrid(int selected, string[] labels, int columns)
        {
            return GUILayout.SelectionGrid(selected, labels, columns);
        }

        public static int ValueGrid(int selectedValue, int[] values, string[] labels, int columns)
        {
            int selected = Array.IndexOf(values, selectedValue);
            int next = GUILayout.SelectionGrid(selected, labels, columns);
            return next >= 0 && next < values.Length ? values[next] : selectedValue;
        }

        public static void Separator()
        {
            GUILayout.Space(4);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);
        }

        public static void CopyToClipboard(string text)
        {
            try { GUIUtility.systemCopyBuffer = text ?? ""; }
            catch { }
        }

        public static string Clipboard()
        {
            try { return GUIUtility.systemCopyBuffer ?? ""; }
            catch { return ""; }
        }

        // Shrinks a long list of errors to something that fits in a page.
        public static string FirstLines(IEnumerable<string> lines, int count)
        {
            if (lines == null) return "";
            var list = lines.Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (list.Count == 0) return "";
            string text = string.Join("\n", list.Take(count));
            if (list.Count > count) text += "\n…还有 " + (list.Count - count) + " 条。";
            return text;
        }
    }
}
