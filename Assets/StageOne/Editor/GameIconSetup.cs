using UnityEditor;
using UnityEngine;

namespace Ruin.Editor
{
    // The Windows exe and taskbar icon come from Player Settings, which cannot be authored as a
    // text asset. This applies the shipped Branding/GameIcon.jpg to the Standalone target once after
    // the project loads, and the build menu calls Apply explicitly so a packaged player never ships
    // with Unity's default icon even if the automatic pass ran before the texture finished importing.
    [InitializeOnLoad]
    public static class GameIconSetup
    {
        public const string IconPath = "Assets/Branding/GameIcon.jpg";

        static GameIconSetup()
        {
            // Deferred: during the static constructor the AssetDatabase may still be importing the
            // texture, and PlayerSettings writes are not safe until the editor is idle.
            EditorApplication.delayCall += ApplyIfMissing;
        }

        [MenuItem("GameMake/Apply Game Icon")]
        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (!icon)
            {
                Debug.LogWarning("Game icon missing at " + IconPath + "; the player will use Unity's default icon.");
                return;
            }

            // Standalone's slot list is fixed by the editor (8 slots, 1024 down to 16). The API
            // returns one entry per square icon slot, so the texture array must match that length
            // exactly; Unity downsamples the source into each size when the player is built.
            var sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++) icons[i] = icon;
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
            AssetDatabase.SaveAssets();
            Debug.Log("GAME_ICON_APPLIED " + IconPath + " slots=" + icons.Length);
        }

        private static void ApplyIfMissing()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (!icon) return;
            var current = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Standalone);
            if (current != null && current.Length > 0 && current[0] == icon) return;
            Apply();
        }
    }
}
