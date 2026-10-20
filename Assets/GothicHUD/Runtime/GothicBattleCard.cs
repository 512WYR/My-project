using System.Collections.Generic;
using System.Text;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// One position on the battle stage. The controller writes into it; it never rebuilds its own hierarchy,
    /// because a battle refreshes many times a second and the authored objects have to survive that.
    /// </summary>
    public sealed class GothicBattleCard : MonoBehaviour
    {
        [Header("Wiring / 引用")]
        public Button Hit;
        public Image Background;
        public Outline Highlight;
        public SilhouetteGraphic Art;
        public Text Name;
        public Image HealthFill;
        public Text HealthValue;
        public Image SanityFill;
        public Text SanityValue;
        public Text Intent;
        public Text Status;
        public GameObject ActiveBadge;
        public GameObject TargetBadge;
        public GameObject DeadCross;

        [Header("Colors / 配色")]
        public Color AliveBody = new Color32(8, 14, 19, 255);
        public Color DeadBody = new Color32(65, 72, 75, 255);
        public Color AllyAccent = new Color32(129, 193, 200, 255);
        public Color EnemyAccent = new Color32(206, 136, 117, 255);
        public Color DeadAccent = new Color32(90, 94, 93, 255);
        // The card itself has no painted body any more: the portrait/silhouette stands directly on the
        // stage. These tints only appear while the position is actionable or inspected, so idle cards
        // never show a rectangle around the art.
        public Color PossibleTint = new Color32(52, 76, 82, 70);
        public Color ActiveTint = new Color32(45, 54, 58, 90);
        public Color InspectedTint = new Color32(70, 82, 86, 55);

        // Filled by the controller so a click can be routed back without the card knowing about the battle.
        [HideInInspector] public string ActorId;

        // The feet strip is created lazily under the art, never as a direct child of the card: the span
        // code caches direct-child positions and would treat it as a badge. It shows the concrete buff
        // and injury names, while the authored Status slot keeps the older limb/form summary.
        private RectTransform buffStrip;
        private Text buffStripText;
        private BattleStatusHover buffHover;
        private readonly List<BattleStatusEntry> statusEntries = new List<BattleStatusEntry>();
        private readonly StringBuilder statusBuilder = new StringBuilder();
        private string buffStripCached;

        public void Render(ActorState actor, bool active, bool possible, bool inspected, string intent, bool heartTarget = false)
        {
            if (actor == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            ActorId = actor.Id;

            Color accent = actor.Player ? AllyAccent : EnemyAccent;
            if (actor.Dead) accent = DeadAccent;

            // Idle positions are fully transparent: the art is the card. Only interaction states add a
            // soft backplate, so there is no permanent frame around the portrait.
            if (Background)
                Background.color = actor.Dead || (!possible && !active && !inspected)
                    ? Color.clear
                    : possible ? PossibleTint : active ? ActiveTint : InspectedTint;

            // The old gold Outline drew a rectangle around the whole card. Selection is already carried
            // by the tint and the 行动 / 可选 badges, so the outline stays off even in older prefabs.
            if (Highlight) Highlight.enabled = false;

            if (Art)
            {
                Art.Silhouette = actor.Definition.Silhouette;
                Art.Enemy = actor.Definition.Pattern;
                Art.IsEnemy = !actor.Player;
                Art.Accent = accent;
                // Heroes and monsters may both ship a portrait under Resources/StageOne/<HeroArt|EnemyArt>/<Id>.
                // Anything without a file keeps the procedural silhouette.
                Art.SetPortrait(ActorArt.Portrait(actor.Definition));
                // A portrait carries its own colours, so only the dead state tints it. The vector figure
                // still uses the stage's near-black body colour.
                Art.color = Art.Portrait != null
                    ? (actor.Dead ? new Color32(120, 120, 120, 255) : Color.white)
                    : (actor.Dead ? DeadBody : AliveBody);
                Art.raycastTarget = false;
                // The silhouette fields are plain fields on a mesh that is only rebuilt when the graphic is
                // marked dirty, and the colour alone may be unchanged when a swap puts a different hero on
                // this rank. Without this the previous occupant's silhouette would linger.
                Art.SetVerticesDirty();
            }

            // The heart's target is marked on the name line: the status slot is reserved for the enemy's
            // intent, and a suffix after the name cannot push the name itself out of the card.
            if (Name) Name.text = heartTarget ? actor.Name + " <color=#D05A5A>⊕</color>" : actor.Name;

            if (HealthFill) HealthFill.fillAmount = Mathf.Clamp01((float)actor.Hp / Mathf.Max(1, actor.MaxHp));
            if (HealthValue)
                HealthValue.text = actor.Dead ? "已死亡"
                    : actor.DeathDoor ? "濒死 · 需要治疗"
                    : actor.Hp + " / " + actor.MaxHp;

            // Sanity sits directly under the health bar so a hero's other vital is visible on the stage,
            // not only on the selected-character panel. Monsters have no sanity at all, so their bar is
            // hidden instead of showing a meaningless 0 / 100.
            bool showSanity = actor.Player;
            if (SanityFill)
            {
                var sanityRoot = SanityFill.transform.parent;
                if (sanityRoot && sanityRoot != transform) sanityRoot.gameObject.SetActive(showSanity);
                else SanityFill.gameObject.SetActive(showSanity);
            }
            if (SanityValue) SanityValue.gameObject.SetActive(showSanity);
            if (showSanity)
            {
                if (SanityFill) SanityFill.fillAmount = Mathf.Clamp01((float)actor.Sanity / 100f);
                if (SanityValue)
                    SanityValue.text = Mathf.Clamp((float)actor.Sanity, 0f, 100f).ToString("0") + " / 100";
            }

            // A card carries one status line: the intent for enemies, the limb/state summary for allies.
            // They share a slot because a position card only has room for one.
            if (Status) Status.text = actor.Player ? StatusLine(actor) : "";
            if (Intent) Intent.text = intent ?? "";

            if (ActiveBadge) ActiveBadge.SetActive(active);
            if (TargetBadge) TargetBadge.SetActive(possible);
            if (DeadCross) DeadCross.SetActive(actor.Dead);

            RenderBuffStrip(actor);
        }

        private void RenderBuffStrip(ActorState actor)
        {
            if (actor == null || actor.Dead)
            {
                HideBuffStrip();
                return;
            }

            EnsureBuffStrip();
            if (!buffStrip) return;

            BattleStatus.Build(actor, statusEntries);
            if (statusEntries.Count == 0)
            {
                HideBuffStrip();
                return;
            }

            statusBuilder.Length = 0;
            BattleStatus.AppendCompact(statusEntries, statusBuilder, 4);
            string text = statusBuilder.ToString();
            // Render is called on every HUD refresh; only touch the Text when the summary really changed.
            if (text != buffStripCached)
            {
                buffStripCached = text;
                buffStripText.text = text;
            }
            if (buffHover) buffHover.Bind(actor);
            buffStrip.gameObject.SetActive(true);
        }

        private void HideBuffStrip()
        {
            if (buffStrip) buffStrip.gameObject.SetActive(false);
            if (buffHover) buffHover.Bind(null);
            buffStripCached = null;
        }

        private void EnsureBuffStrip()
        {
            if (buffStrip || !Art) return;

            var go = new GameObject("Status - 状态", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            buffStrip = go.GetComponent<RectTransform>();
            buffStrip.SetParent(Art.transform, false);
            buffStrip.anchorMin = buffStrip.anchorMax = new Vector2(0.5f, 0f);
            buffStrip.pivot = new Vector2(0.5f, 0f);
            buffStrip.anchoredPosition = new Vector2(0f, 2f);
            buffStrip.sizeDelta = new Vector2(144f, 32f);

            var plate = go.GetComponent<Image>();
            plate.color = new Color32(5, 7, 8, 190);
            plate.raycastTarget = true;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(buffStrip, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(3f, 2f);
            textRect.offsetMax = new Vector2(-3f, -2f);

            buffStripText = textGo.GetComponent<Text>();
            buffStripText.font = Name ? Name.font : Status ? Status.font : null;
            buffStripText.fontSize = 11;
            buffStripText.color = new Color32(230, 224, 212, 255);
            buffStripText.alignment = TextAnchor.MiddleCenter;
            buffStripText.horizontalOverflow = HorizontalWrapMode.Wrap;
            buffStripText.verticalOverflow = VerticalWrapMode.Truncate;
            buffStripText.raycastTarget = false;

            buffHover = go.AddComponent<BattleStatusHover>();
            buffHover.ForwardClick = Hit;
            buffStrip.gameObject.SetActive(false);
        }

        // ---- multi-rank span ----
        // A monster with RankWidth > 1 covers several positions. The stage still has one card per rank,
        // so the owner's card is widened over the neighbours and the covered cards are hidden. The base
        // layout is recorded once because the controller resets the span on every refresh: without that,
        // a card that stopped spanning would keep the wider size and the shifted contents.
        private struct ChildLayout
        {
            public RectTransform Rect;
            public Vector2 Position;
            public Vector2 Size;
        }

        private readonly List<ChildLayout> children = new List<ChildLayout>();
        private RectTransform cardRect;
        private RectTransform backgroundRect;
        private Vector2 cardBaseSize;
        private Vector2 backgroundBaseSize;
        // The health group is widened with the card instead of being centred: a multi-rank card with a
        // 148px bar floating in the middle reads as a mismatched width. The track/fill/value children
        // are widened too so the filled portion still covers the whole bar.
        private RectTransform healthRect;
        private Vector2 healthBaseSize;
        private readonly List<ChildLayout> healthChildren = new List<ChildLayout>();
        // The portrait is widened with the span as well: a multi-rank monster stretched a card wider
        // but left the art at its single-rank width, so the boss looked small in the middle of its body.
        private RectTransform artRect;
        private Vector2 artBaseSize;
        private Vector2 artBasePosition;
        private bool layoutCached;

        /// <summary>
        /// Stretch this card across <paramref name="units"/> positions. <paramref name="step"/> is the
        /// distance between two adjacent cards, so units=1 collapses back to the authored layout.
        /// </summary>
        public void SetSpan(int units, float step)
        {
            if (units <= 1 || step <= 0f) { ResetSpan(); return; }
            CacheLayout();
            float extra = (units - 1) * step;
            if (cardRect) cardRect.sizeDelta = new Vector2(cardBaseSize.x + extra, cardBaseSize.y);
            // The body is also the click target, so widening it makes the whole span selectable.
            if (backgroundRect) backgroundRect.sizeDelta = new Vector2(backgroundBaseSize.x + extra, backgroundBaseSize.y);
            // Keep the same 6px inset on both sides: the bar starts where it did and gains the span width.
            if (healthRect)
            {
                healthRect.sizeDelta = new Vector2(healthBaseSize.x + extra, healthBaseSize.y);
                foreach (var child in healthChildren)
                    if (child.Rect) child.Rect.sizeDelta = new Vector2(child.Size.x + extra, child.Size.y);
            }
            foreach (var child in children)
            {
                var rect = child.Rect;
                if (!rect) continue;
                // The art is widened below instead of being slid right: every rect here is anchored to
                // the card's top-left corner, so keeping its left edge and adding the span width makes
                // it grow symmetrically inside the card.
                if (artRect && rect == artRect) continue;
                var position = child.Position;
                // Badges are corner-anchored labels: keep "行动" on the left edge and push "可选" to the
                // new right edge. Everything else slides to the middle of the widened card.
                if (ActiveBadge && rect.gameObject == ActiveBadge) position.x = child.Position.x;
                else if (TargetBadge && rect.gameObject == TargetBadge) position.x += extra;
                else position.x += extra * 0.5f;
                rect.anchoredPosition = position;
            }
            // The portrait / silhouette fills the widened card. Without this a 2- or 3-rank boss keeps
            // its art at the width of one position, leaving most of the span empty around it.
            if (artRect)
            {
                artRect.sizeDelta = new Vector2(artBaseSize.x + extra, artBaseSize.y);
                artRect.anchoredPosition = artBasePosition;
            }
        }

        /// <summary>Restore the authored layout. Safe to call on cards that never spanned.</summary>
        public void ResetSpan()
        {
            CacheLayout();
            if (cardRect) cardRect.sizeDelta = cardBaseSize;
            if (backgroundRect) backgroundRect.sizeDelta = backgroundBaseSize;
            if (healthRect) healthRect.sizeDelta = healthBaseSize;
            foreach (var child in healthChildren)
            {
                if (!child.Rect) continue;
                child.Rect.anchoredPosition = child.Position;
                child.Rect.sizeDelta = child.Size;
            }
            foreach (var child in children)
            {
                if (!child.Rect) continue;
                child.Rect.anchoredPosition = child.Position;
                child.Rect.sizeDelta = child.Size;
            }
            if (artRect)
            {
                artRect.anchoredPosition = artBasePosition;
                artRect.sizeDelta = artBaseSize;
            }
        }

        private void CacheLayout()
        {
            if (layoutCached) return;
            layoutCached = true;
            cardRect = transform as RectTransform;
            if (cardRect) cardBaseSize = cardRect.sizeDelta;
            backgroundRect = Background ? Background.transform as RectTransform : null;
            if (backgroundRect) backgroundBaseSize = backgroundRect.sizeDelta;
            healthRect = HealthFill ? HealthFill.transform.parent as RectTransform : null;
            if (healthRect == transform) healthRect = null;
            if (healthRect)
            {
                healthBaseSize = healthRect.sizeDelta;
                for (int i = 0; i < healthRect.childCount; i++)
                {
                    var rect = healthRect.GetChild(i) as RectTransform;
                    if (rect)
                        healthChildren.Add(new ChildLayout { Rect = rect, Position = rect.anchoredPosition, Size = rect.sizeDelta });
                }
            }
            artRect = Art ? Art.transform as RectTransform : null;
            if (artRect)
            {
                artBaseSize = artRect.sizeDelta;
                artBasePosition = artRect.anchoredPosition;
            }
            children.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var rect = transform.GetChild(i) as RectTransform;
                if (!rect || rect == backgroundRect || rect == healthRect) continue;
                children.Add(new ChildLayout { Rect = rect, Position = rect.anchoredPosition, Size = rect.sizeDelta });
            }
        }

        // Only allies reach this; an enemy card shows its intent in the same slot instead. The wording
        // mirrors the built-in battle cards, so a hero reads the same on either page.
        private static string StatusLine(ActorState actor)
        {
            string limb = LimbSystem.Summary(actor);
            string state = actor.BleedTurns > 0 ? "流血 " + actor.BleedDamage + "×" + actor.BleedTurns
                : actor.Form == SkillForm.Technical ? "高理智技能"
                : actor.Form == SkillForm.Primal ? "低理智变化" : "";
            if (limb.Length == 0 && state.Length == 0)
                return AccessorySystem.ShortSummary(actor);
            if (limb.Length == 0) return state;
            return state.Length == 0 ? limb : limb + " · " + state;
        }
    }
}
