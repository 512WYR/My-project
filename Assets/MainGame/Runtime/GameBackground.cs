using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // The scene is authored from code and every page is a prefab, so the backdrop is built the same way:
    // one overlay canvas underneath all page canvases (every authored page sorts at 10 or above), filled
    // with the background art from Resources. Building it at run time means a replaced image is picked up
    // without re-baking the scene, and a missing image simply leaves the old solid-colour camera clear.
    public sealed class GameBackground : MonoBehaviour
    {
        public const string ResourcePath = "StageOne/Background/main";
        // Below every page canvas, above nothing: the camera clear colour shows through if art is missing.
        private const int SortingOrder = -1000;
        // The page plates are transparent so the art shows through; this keeps the old contrast for text.
        private const float ScrimAlpha = .5f;

        private RectTransform rect;
        private Sprite art;
        private Vector2 lastScreen;

        // Used by StageOneApp, the only game-state owner, so module preview scenes without it keep their
        // own look. Returns null when no background art has been imported yet.
        public static GameBackground Ensure()
        {
            var existing = FindObjectOfType<GameBackground>();
            if (existing)
            {
                existing.Fit();
                return existing;
            }

            var sprite = LoadSprite(ResourcePath);
            if (!sprite) return null;

            var root = new GameObject("GameBackground");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var background = root.AddComponent<GameBackground>();
            background.Build(sprite);
            return background;
        }

        private void Build(Sprite sprite)
        {
            art = sprite;
            var imageObject = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(transform, false);
            var image = imageObject.GetComponent<Image>();
            image.sprite = art;
            // The backdrop never owns input; every click belongs to the page above it.
            image.raycastTarget = false;
            rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;

            // Uniform scrim between the art and every page. Done here rather than per page so a new
            // page automatically inherits the same contrast instead of remembering to tint its plate.
            var scrimObject = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            scrimObject.transform.SetParent(transform, false);
            var scrim = scrimObject.GetComponent<Image>();
            scrim.color = new Color(.02f, .025f, .03f, ScrimAlpha);
            scrim.raycastTarget = false;
            var scrimRect = scrim.rectTransform;
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = scrimRect.offsetMax = Vector2.zero;
            Fit();
        }

        // Cover, not contain: the frame always fills the screen and the overflow is cropped evenly on both
        // sides. Letterboxing would expose the camera clear colour and break the full-screen look.
        private void Fit()
        {
            if (!rect || !art) return;
            lastScreen = new Vector2(Screen.width, Screen.height);
            var native = art.rect.size;
            if (native.x <= 0 || native.y <= 0) return;
            var cover = Mathf.Max(lastScreen.x / native.x, lastScreen.y / native.y);
            rect.sizeDelta = native * cover;
        }

        private void Update()
        {
            // Windowed play and device rotation both resize the screen after the canvas was built.
            if (Screen.width != (int)lastScreen.x || Screen.height != (int)lastScreen.y) Fit();
        }

        private static Sprite LoadSprite(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite) return sprite;
            // Same fallback ActorArt uses: depending on the importer the texture main object is not always
            // reachable as a Sprite, so build one from the Texture2D instead of dropping the backdrop.
            var texture = Resources.Load<Texture2D>(path);
            if (!texture) return null;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        }
    }
}
