using GameMake.GothicHUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUDEditor
{
    /// <summary>
    /// One-click patch for the eight battle position cards:
    ///   1. hides the decorative "Industrial Card Frame" around the portrait;
    ///   2. enlarges the portrait (Silhouette), widening the cards and re-spacing the formations so the
    ///      image can grow while still contain-fitting (never cropped).
    /// The name/health/sanity/status rows move under the taller portrait; font sizes and runtime logic
    /// stay untouched. Re-running is harmless. "Revert" restores the original authored layout.
    /// </summary>
    public static class FramelessPortraitCards
    {
        private const string PrefabPath = "Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";
        private const string FrameName = "Industrial Card Frame";

        // ---- tuning: change these numbers and run the menu again if the result is not what you want ----
        // Without cropping, a 2:3 portrait is limited by card width. Widening the card from 160 to 200
        // therefore grows the visible image from 156x234 to 196x294. The card still grows upward and its
        // bottom edge stays on the formation rail.
        private const float CardWidth = 200f;
        private const float CardGap = 1f;
        private const float CardStep = CardWidth + CardGap;
        private const float CardHeight = 418f;      // 294 portrait + 122 of information rows

        private const float ArtX = 2f;
        private const float ArtY = 0f;
        private const float ArtWidthInset = 4f;     // 200 - 4 = 196
        private const float ArtHeight = 294f;       // 196 / 2 * 3, no crop

        // The four rows are packed under the portrait. Their heights and font sizes stay as authored.
        private const float NameX = 3f;
        private const float NameY = 300f;
        private const float NameWidthInset = 6f;    // 200 - 6 = 194
        private const float NameHeight = 28f;
        private const float HealthX = 6f;
        private const float HealthY = 332f;
        private const float BarWidthInset = 12f;    // 200 - 12 = 188
        private const float BarHeight = 20f;
        private const float SanityY = 356f;
        private const float StatusX = 6f;
        private const float StatusY = 380f;
        private const float StatusWidthInset = 12f;
        private const float StatusHeight = 36f;
        private const float DeadCrossY = 112f;      // (294 - 70) / 2, centred over the portrait

        // Four cards must still fit on screen. The enemy formation shifts left by the overflow.
        private const float ScreenWidth = 1920f;
        private const float GroupRightMargin = 8f;

        // Original authored values, used by Revert.
        private const float OriginalCardWidth = 160f;
        private const float OriginalCardGap = 1f;
        private const float OriginalCardStep = OriginalCardWidth + OriginalCardGap;
        private const float OriginalCardHeight = 292f;
        private const float OriginalArtX = 37f;
        private const float OriginalArtY = 18f;
        private const float OriginalArtWidth = 86f;
        private const float OriginalArtHeight = 128f;
        private const float OriginalNameY = 172f;
        private const float OriginalHealthY = 206f;
        private const float OriginalSanityY = 230f;
        private const float OriginalStatusY = 254f;
        private const float OriginalDeadCrossY = 44f;
        private const float OriginalAllyGroupX = 213f;
        private const float OriginalEnemyGroupX = 1164f;
        private static readonly Color OriginalBody = new Color32(8, 14, 19, 255);
        private static readonly Color OriginalPossibleTint = new Color32(52, 76, 82, 255);
        private static readonly Color OriginalActiveTint = new Color32(45, 54, 58, 255);
        private static readonly Color OriginalInspectedTint = new Color32(70, 82, 86, 255);

        [MenuItem("GameMake/Gothic HUD/Apply Frameless Portrait Cards")]
        public static void Apply()
        {
            bool changed;
            int seen = PatchAll(false, out changed);
            AssetDatabase.SaveAssets();
            Debug.Log(seen == 0
                ? "FRAMELESS_CARDS: 没有找到 GothicBattleCard，请先打开战斗 HUD 场景。"
                : "FRAMELESS_CARDS_OK cards=" + seen + (changed ? " changed" : " already applied"));
        }

        [MenuItem("GameMake/Gothic HUD/Revert Frameless Portrait Cards")]
        public static void Revert()
        {
            bool changed;
            int seen = PatchAll(true, out changed);
            AssetDatabase.SaveAssets();
            Debug.Log(seen == 0
                ? "FRAMELESS_CARDS: 没有找到 GothicBattleCard，请先打开战斗 HUD 场景。"
                : "FRAMELESS_CARDS_REVERTED cards=" + seen + (changed ? " changed" : " already original"));
        }

        private static int PatchAll(bool revert, out bool changed)
        {
            int seen = PatchPrefab(revert, out changed);
            if (seen > 0) return seen;
            return PatchOpenScene(revert, out changed);
        }

        private static int PatchPrefab(bool revert, out bool changed)
        {
            changed = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return 0;

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            int seen = 0;
            foreach (var card in root.GetComponentsInChildren<GothicBattleCard>(true))
            {
                seen++;
                if (revert ? Restore(card) : Patch(card)) changed = true;
            }
            // Save only if anything actually moved, so a second run does not dirty the prefab.
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return seen;
        }

        private static int PatchOpenScene(bool revert, out bool changed)
        {
            changed = false;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int seen = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var card in root.GetComponentsInChildren<GothicBattleCard>(true))
                {
                    // Prefab instances inherit the patched prefab; editing them would only create overrides.
                    if (PrefabUtility.IsPartOfPrefabInstance(card)) continue;
                    seen++;
                    Undo.RecordObject(card, revert ? "Revert frameless portrait cards" : "Apply frameless portrait cards");
                    if (revert ? Restore(card) : Patch(card)) changed = true;
                }
            if (changed) EditorSceneManager.MarkSceneDirty(scene);
            return seen;
        }

        private static bool Patch(GothicBattleCard card)
        {
            if (card == null) return false;
            bool changed = false;
            var cardRect = (RectTransform)card.transform;

            // 1. Hide the decorative frame. It is an independent child image, not card.Background,
            //    so runtime tint changes could never remove it.
            var frame = card.transform.Find(FrameName);
            if (frame && frame.gameObject.activeSelf)
            {
                frame.gameObject.SetActive(false);
                changed = true;
            }

            // 2. Widen and grow the card upward, keeping its bottom edge exactly where it was. The extra
            //    width is what allows the 2:3 portrait to grow without cropping; the extra height follows
            //    from that width. Cards are re-spaced from their left-to-right order.
            int index = CardIndex(card);
            float bottom = -cardRect.anchoredPosition.y + cardRect.sizeDelta.y;
            changed |= SetSize(cardRect, CardWidth, CardHeight);
            changed |= SetPosition(cardRect, index * CardStep, bottom - CardHeight);
            changed |= LayoutFormation(card, false);

            // 3. The portrait fills the card width: 196x294 visible, contain-fit, never cropped.
            if (card.Art)
            {
                changed |= SetRect(card.Art.rectTransform, ArtX, ArtY, CardWidth - ArtWidthInset, ArtHeight);
                card.Art.SetVerticesDirty();
            }

            // 4. Move the four information rows under the portrait. Only positions and widths change; the
            //    authored font sizes and row heights are kept so text does not clip.
            if (card.Name)
                changed |= SetRect(card.Name.rectTransform, NameX, NameY, CardWidth - NameWidthInset, NameHeight);
            changed |= MoveBar(card.HealthFill, card.transform, HealthX, HealthY, CardWidth - BarWidthInset, BarHeight);
            changed |= MoveBar(card.SanityFill, card.transform, HealthX, SanityY, CardWidth - BarWidthInset, BarHeight);
            if (card.Status)
                changed |= SetRect(card.Status.rectTransform, StatusX, StatusY, CardWidth - StatusWidthInset, StatusHeight);
            if (card.Intent)
                changed |= SetRect(card.Intent.rectTransform, StatusX, StatusY, CardWidth - StatusWidthInset, StatusHeight);
            if (card.DeadCross)
                changed |= SetRect(card.DeadCross.transform as RectTransform, 0, DeadCrossY, CardWidth, 70f);

            // The badges are corner labels: 行动 stays on the left, 可选 tracks the new right edge.
            if (card.ActiveBadge)
                changed |= SetRect(card.ActiveBadge.transform as RectTransform, 7f, 29f, 52f, 23f);
            if (card.TargetBadge)
                changed |= SetRect(card.TargetBadge.transform as RectTransform, CardWidth - 58f, 29f, 52f, 23f);

            // The body is also the click target, so it must cover the taller, wider card.
            if (card.Background)
            {
                if (card.Background.color.a != 0f)
                {
                    card.Background.color = Color.clear;
                    changed = true;
                }
                changed |= SetSize(card.Background.rectTransform, CardWidth, CardHeight);
            }

            // No painted rectangle at rest. The runtime writes these colours every frame; the serialized
            // values must be translucent too or an actionable card gets a hard box.
            changed |= SetColor(ref card.PossibleTint, new Color32(52, 76, 82, 70));
            changed |= SetColor(ref card.ActiveTint, new Color32(45, 54, 58, 90));
            changed |= SetColor(ref card.InspectedTint, new Color32(70, 82, 86, 55));
            if (card.Highlight && card.Highlight.enabled)
            {
                card.Highlight.enabled = false;
                changed = true;
            }

            return changed;
        }

        private static bool Restore(GothicBattleCard card)
        {
            if (card == null) return false;
            bool changed = false;
            var cardRect = (RectTransform)card.transform;

            var frame = card.transform.Find(FrameName);
            if (frame && !frame.gameObject.activeSelf)
            {
                frame.gameObject.SetActive(true);
                changed = true;
            }

            int index = CardIndex(card);
            float bottom = -cardRect.anchoredPosition.y + cardRect.sizeDelta.y;
            changed |= SetSize(cardRect, OriginalCardWidth, OriginalCardHeight);
            changed |= SetPosition(cardRect, index * OriginalCardStep, bottom - OriginalCardHeight);
            changed |= LayoutFormation(card, true);

            if (card.Art)
            {
                changed |= SetRect(card.Art.rectTransform, OriginalArtX, OriginalArtY, OriginalArtWidth, OriginalArtHeight);
                card.Art.SetVerticesDirty();
            }

            if (card.Name)
                changed |= SetRect(card.Name.rectTransform, NameX, OriginalNameY, OriginalCardWidth - NameWidthInset, NameHeight);
            changed |= MoveBar(card.HealthFill, card.transform, HealthX, OriginalHealthY, OriginalCardWidth - BarWidthInset, BarHeight);
            changed |= MoveBar(card.SanityFill, card.transform, HealthX, OriginalSanityY, OriginalCardWidth - BarWidthInset, BarHeight);
            if (card.Status)
                changed |= SetRect(card.Status.rectTransform, StatusX, OriginalStatusY, OriginalCardWidth - StatusWidthInset, StatusHeight);
            if (card.Intent)
                changed |= SetRect(card.Intent.rectTransform, StatusX, OriginalStatusY, OriginalCardWidth - StatusWidthInset, StatusHeight);
            if (card.DeadCross)
                changed |= SetRect(card.DeadCross.transform as RectTransform, 0, OriginalDeadCrossY, OriginalCardWidth, 70f);

            if (card.ActiveBadge)
                changed |= SetRect(card.ActiveBadge.transform as RectTransform, 7f, 29f, 52f, 23f);
            if (card.TargetBadge)
                changed |= SetRect(card.TargetBadge.transform as RectTransform, 102f, 29f, 52f, 23f);

            if (card.Background)
            {
                if (card.Background.color != OriginalBody)
                {
                    card.Background.color = OriginalBody;
                    changed = true;
                }
                changed |= SetSize(card.Background.rectTransform, OriginalCardWidth, OriginalCardHeight);
            }
            changed |= SetColor(ref card.PossibleTint, OriginalPossibleTint);
            changed |= SetColor(ref card.ActiveTint, OriginalActiveTint);
            changed |= SetColor(ref card.InspectedTint, OriginalInspectedTint);

            if (card.Highlight && !card.Highlight.enabled)
            {
                card.Highlight.enabled = true;
                changed = true;
            }

            return changed;
        }

        // Cards are ordered left-to-right inside a formation. Because the layout keeps that order, the
        // same index comes back on a second run and the operation stays idempotent.
        private static int CardIndex(GothicBattleCard card)
        {
            var parent = card.transform.parent;
            var rect = (RectTransform)card.transform;
            if (!parent) return 0;
            int index = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var other = parent.GetChild(i).GetComponent<GothicBattleCard>();
                if (!other || other == card) continue;
                if (((RectTransform)other.transform).anchoredPosition.x < rect.anchoredPosition.x) index++;
            }
            return index;
        }

        // Widens the formation container and, on the enemy side, slides it left by any overflow so the
        // fourth card stays inside the 1920px canvas. Ally cards have the party panel on their left, so
        // they grow to the right into the central gap instead.
        private static bool LayoutFormation(GothicBattleCard card, bool revert)
        {
            var group = card.transform.parent as RectTransform;
            if (!group) return false;
            bool enemy = group.name.StartsWith("Enemy Positions");
            if (!enemy && !group.name.StartsWith("Ally Positions")) return false;

            int count = 0;
            for (int i = 0; i < group.childCount; i++)
                if (group.GetChild(i).GetComponent<GothicBattleCard>()) count++;
            if (count == 0) return false;

            float cardWidth = revert ? OriginalCardWidth : CardWidth;
            float gap = revert ? OriginalCardGap : CardGap;
            float width = count * cardWidth + Mathf.Max(0, count - 1) * gap;
            bool changed = SetSize(group, width, group.sizeDelta.y);

            float x = revert
                ? (enemy ? OriginalEnemyGroupX : OriginalAllyGroupX)
                : (enemy ? Mathf.Min(group.anchoredPosition.x, ScreenWidth - GroupRightMargin - width)
                         : group.anchoredPosition.x);
            if (!Mathf.Approximately(x, group.anchoredPosition.x))
            {
                group.anchoredPosition = new Vector2(x, group.anchoredPosition.y);
                changed = true;
            }
            return changed;
        }

        // HealthFill/SanityFill are the "Fill" images; their parent group owns Track/Fill/Value. Moving the
        // group moves the whole row. Track/Fill/Value are authored as wide as the group (the fill sits one
        // pixel right), so they have to grow with it or the bar would not span the widened card.
        private static bool MoveBar(Image fill, Transform card, float x, float y, float width, float height)
        {
            var group = fill ? fill.transform.parent as RectTransform : null;
            if (!group || group == card) return false;
            bool changed = SetRect(group, x, y, width, height);
            for (int i = 0; i < group.childCount; i++)
            {
                var child = group.GetChild(i) as RectTransform;
                if (child) changed |= SetSize(child, width, child.sizeDelta.y);
            }
            return changed;
        }

        private static bool SetColor(ref Color field, Color target)
        {
            if (field == target) return false;
            field = target;
            return true;
        }

        private static bool SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            if (!rect) return false;
            bool changed = SetPosition(rect, x, y);
            changed |= SetSize(rect, width, height);
            return changed;
        }

        // The card hierarchy uses a top-left pivot, so y is stored negated: screen-down = -y.
        private static bool SetPosition(RectTransform rect, float x, float y)
        {
            var target = new Vector2(x, -y);
            if ((rect.anchoredPosition - target).sqrMagnitude < 0.01f) return false;
            rect.anchoredPosition = target;
            return true;
        }

        private static bool SetSize(RectTransform rect, float width, float height)
        {
            var target = new Vector2(width, height);
            if ((rect.sizeDelta - target).sqrMagnitude < 0.01f) return false;
            rect.sizeDelta = target;
            return true;
        }
    }
}
