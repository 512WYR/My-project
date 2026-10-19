using System.Collections.Generic;
using System.Text;
using Ruin.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // One shared tooltip for every feet plate. It lives on its own canvas above the presentation so a
    // hover opened during the cut-in is not hidden behind it, and it never blocks raycasts: the plate
    // underneath keeps receiving the pointer events that decide when to close it.
    public sealed class BattleStatusTooltip : MonoBehaviour
    {
        private const int SortingOrder = 70;
        private const float PanelWidth = 460f;
        private const float MaxBodyHeight = 560f;

        private static BattleStatusTooltip instance;

        private ActorState actor;
        private RectTransform panel;
        private Text title;
        private Text body;
        private bool showing;
        private Vector2 pointer;

        public static bool IsShowing(ActorState target)
        {
            return instance != null && instance.showing && target != null
                && ReferenceEquals(instance.actor, target);
        }

        public static void Show(ActorState target, Vector2 screenPosition)
        {
            if (target == null || Application.isBatchMode) return;
            var entries = new List<BattleStatusEntry>();
            BattleStatus.Build(target, entries);
            if (entries.Count == 0)
            {
                if (instance != null) instance.HideInternal();
                return;
            }

            EnsureInstance();
            if (instance == null) return;
            instance.actor = target;
            instance.pointer = screenPosition;
            instance.showing = true;
            instance.panel.gameObject.SetActive(true);
            instance.Render(entries);
            instance.Place();
        }

        public static void Refresh(ActorState target)
        {
            if (instance == null || !instance.showing || target == null
                || !ReferenceEquals(instance.actor, target)) return;
            var entries = new List<BattleStatusEntry>();
            BattleStatus.Build(target, entries);
            if (entries.Count == 0)
            {
                instance.HideInternal();
                return;
            }
            instance.Render(entries);
            instance.Place();
        }

        public static void Hide(ActorState target)
        {
            if (instance == null || !instance.showing || target == null) return;
            if (!ReferenceEquals(instance.actor, target)) return;
            instance.HideInternal();
        }

        public static void HideAll()
        {
            if (instance != null) instance.HideInternal();
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;

            var canvas = MenuUi.MakeCanvas("BattleStatusTooltipCanvas", SortingOrder);
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            instance = canvas.gameObject.AddComponent<BattleStatusTooltip>();
            instance.Build();
        }

        private void Build()
        {
            panel = MenuUi.Center(transform, "Panel", 0f, 0f, PanelWidth, 160f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);

            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color32(10, 12, 14, 244);
            background.raycastTarget = false;

            var edge = MenuUi.CenterSolid(panel, "Edge", 0f, 0f, PanelWidth, 2f, MenuUi.Line);
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.anchoredPosition = Vector2.zero;
            edgeRect.sizeDelta = new Vector2(0f, 2f);

            title = MenuUi.CenterLabel(panel, "Title", "", 0f, 0f, PanelWidth - 32f, 28f,
                20, MenuUi.Gold, TextAnchor.MiddleLeft);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(16f, -12f);

            body = MenuUi.CenterLabel(panel, "Body", "", 0f, 0f, PanelWidth - 32f, 80f,
                17, MenuUi.Ink, TextAnchor.UpperLeft);
            var bodyRect = body.rectTransform;
            bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(0f, 1f);
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.anchoredPosition = new Vector2(16f, -44f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.supportRichText = true;

            panel.gameObject.SetActive(false);
        }

        private void Render(List<BattleStatusEntry> entries)
        {
            title.text = (actor.Name ?? "角色") + " · 状态";

            var text = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (i > 0) text.Append("\n\n");
                text.Append("<b><color=").Append(ToneColor(entry.Tone)).Append('>');
                text.Append(entry.Name ?? "");
                if (!string.IsNullOrEmpty(entry.Value)) text.Append(entry.Value);
                text.Append("</color></b>");
                if (!string.IsNullOrEmpty(entry.Detail))
                    text.Append("\n<color=#B9B4A8>").Append(entry.Detail).Append("</color>");
            }
            body.text = text.ToString();

            body.rectTransform.sizeDelta = new Vector2(PanelWidth - 32f, 80f);
            float bodyHeight = Mathf.Min(MaxBodyHeight, Mathf.Ceil(body.preferredHeight));
            body.rectTransform.sizeDelta = new Vector2(PanelWidth - 32f, bodyHeight);
            panel.sizeDelta = new Vector2(PanelWidth, 44f + bodyHeight + 16f);
        }

        private void Place()
        {
            if (!showing || panel == null) return;
            var root = transform as RectTransform;
            if (root == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pointer, null, out local)) return;

            float width = panel.sizeDelta.x;
            float height = panel.sizeDelta.y;
            Rect bounds = root.rect;

            float x = local.x + 18f;
            if (x + width > bounds.xMax - 8f) x = local.x - 18f - width;
            float y = local.y - 18f;
            if (y - height < bounds.yMin + 8f) y = local.y + 18f + height;

            float minX = bounds.xMin + 8f;
            float maxX = bounds.xMax - width - 8f;
            x = maxX < minX ? (bounds.xMin + bounds.xMax) * 0.5f : Mathf.Clamp(x, minX, maxX);

            float minY = bounds.yMin + height + 8f;
            float maxY = bounds.yMax - 8f;
            y = minY > maxY ? (bounds.yMin + bounds.yMax) * 0.5f : Mathf.Clamp(y, minY, maxY);

            panel.anchoredPosition = new Vector2(x, y);
        }

        private static string ToneColor(BattleStatusTone tone)
        {
            switch (tone)
            {
                case BattleStatusTone.Positive: return "#E2C889";
                case BattleStatusTone.Negative: return "#D98D7E";
                case BattleStatusTone.Injury: return "#D9775E";
                default: return "#9FB8D8";
            }
        }

        private void Update()
        {
            if (!showing || panel == null || !panel.gameObject.activeSelf) return;
            pointer = Input.mousePosition;
            Place();
        }

        private void HideInternal()
        {
            showing = false;
            actor = null;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // MenuOverlay hides every gameplay canvas and later restores them. Without clearing the
            // owner here the restored canvas would bring an old tooltip back after leaving a battle.
            HideInternal();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
