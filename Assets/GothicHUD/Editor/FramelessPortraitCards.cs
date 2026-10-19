using GameMake.GothicHUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUDEditor
{
    /// <summary>
    /// Patches the compiled battle HUD directly instead of rebuilding it. The authored cards still carry
    /// the old "Industrial Card Frame" image and a small 86x128 portrait; this tool applies only two
    /// changes to those cards: hide the frame and let the portrait fill the framed area.
    /// It does not touch the old Assets/GothicHUD/Art assets, which the prefab no longer references.
    /// </summary>
    public static class FramelessPortraitCards
    {
        private const string PrefabPath = "Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";
        private const string FrameName = "Industrial Card Frame";
        // The old frame occupied 0..166, so this is the largest portrait that stays clear of the name.
        private const float MaxArtHeight = 166f;

        [MenuItem("GameMake/Gothic HUD/Apply Frameless Portrait Cards")]
        public static void Apply()
        {
            int seen = PatchPrefab(out int changed);
            if (seen == 0) seen = PatchOpenScene(out changed);
            AssetDatabase.SaveAssets();
            Debug.Log(seen == 0
                ? "FRAMELESS_CARDS: 没有找到 GothicBattleCard，请先打开战斗 HUD 场景。"
                : "FRAMELESS_CARDS_OK cards=" + seen + " changed=" + changed);
        }

        private static int PatchPrefab(out int changed)
        {
            changed = 0;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                return 0;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            int seen = 0;
            foreach (var card in root.GetComponentsInChildren<GothicBattleCard>(true))
            {
                seen++;
                if (Patch(card)) changed++;
            }
            // Save only when something changed, so running the menu twice is harmless.
            if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return seen;
        }

        private static int PatchOpenScene(out int changed)
        {
            changed = 0;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int seen = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var card in root.GetComponentsInChildren<GothicBattleCard>(true))
                {
                    // Prefab instances already inherit the patched prefab; touching them would just
                    // create needless overrides in the scene.
                    if (PrefabUtility.IsPartOfPrefabInstance(card)) continue;
                    seen++;
                    Undo.RecordObject(card, "Apply frameless portrait cards");
                    if (Patch(card)) changed++;
                }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            return seen;
        }

        private static bool Patch(GothicBattleCard card)
        {
            if (card == null) return false;
            bool changed = false;
            var cardRect = (RectTransform)card.transform;
            float width = cardRect.sizeDelta.x;

            // 1. No frame around the portrait. The frame is an independent child image, not the
            //    GothicBattleCard.Background, so the runtime colour changes could never hide it.
            var frame = card.transform.Find(FrameName);
            if (frame && frame.gameObject.activeSelf)
            {
                frame.gameObject.SetActive(false);
                changed = true;
            }

            // 2. Enlarge the portrait to the old framed area. Keep a 6px gap above the name so a
            //    rebuilt card with a shorter body still lays out correctly.
            float artHeight = MaxArtHeight;
            if (card.Name && card.Name.transform.parent == card.transform)
                artHeight = Mathf.Min(artHeight, -card.Name.rectTransform.anchoredPosition.y - 6f);
            artHeight = Mathf.Max(96f, artHeight);
            if (card.Art)
            {
                changed |= SetRect(card.Art.rectTransform, 2, 0, width - 4, artHeight);
                card.Art.SetVerticesDirty();
            }

            // The runtime already keeps idle bodies transparent. The serialized tints still held the old
            // opaque values, which would paint a rectangle whenever a position became actionable.
            if (card.Background)
            {
                if (card.Background.color.a != 0f)
                {
                    card.Background.color = Color.clear;
                    changed = true;
                }
                // Keep the click target the size of the whole card.
                changed |= SetSize(card.Background.rectTransform, width, cardRect.sizeDelta.y);
            }
            if (card.PossibleTint != (Color)new Color32(52, 76, 82, 70))
            {
                card.PossibleTint = new Color32(52, 76, 82, 70);
                changed = true;
            }
            if (card.ActiveTint != (Color)new Color32(45, 54, 58, 90))
            {
                card.ActiveTint = new Color32(45, 54, 58, 90);
                changed = true;
            }
            if (card.InspectedTint != (Color)new Color32(70, 82, 86, 55))
            {
                card.InspectedTint = new Color32(70, 82, 86, 55);
                changed = true;
            }
            if (card.Highlight && card.Highlight.enabled)
            {
                card.Highlight.enabled = false;
                changed = true;
            }

            return changed;
        }

        private static bool SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            if (!rect) return false;
            bool changed = SetPosition(rect, x, y);
            changed |= SetSize(rect, width, height);
            return changed;
        }

        // The card hierarchy uses a top-left pivot, so a larger y means lower on screen.
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
