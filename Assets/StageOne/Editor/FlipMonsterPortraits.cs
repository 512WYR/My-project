using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ruin.Runtime;
using UnityEditor;
using UnityEngine;

namespace Ruin.Editor
{
    // One-off art pass: mirrors the source PNG of every monster portrait except the boss units. Writing
    // the same path keeps the .meta file, so GUIDs, importer settings and Resources lookups stay intact.
    // Running the menu again flips the same files back.
    public static class FlipMonsterPortraits
    {
        const string PortraitFolder = "Assets/StageOne/Resources/StageOne/EnemyArt";
        const string CatalogPath = "StageOne/GameCatalog";

        [MenuItem("GameMake/Art/Flip Monster Portraits (Except Bosses)")]
        public static void Flip()
        {
            var catalog = Resources.Load<GameCatalog>(CatalogPath);
            if (!catalog)
            {
                Debug.LogError("Flip Monster Portraits: 找不到 " + CatalogPath + "，无法确定首领列表。");
                return;
            }
            var bosses = BossIds(catalog);
            if (bosses.Count == 0)
            {
                Debug.LogError("Flip Monster Portraits: GameCatalog.BossEncounters 为空，已中止以免翻转首领立绘。");
                return;
            }
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { PortraitFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            int count = paths.Count(path => !IsBoss(path, bosses));
            if (count == 0)
            {
                Debug.LogWarning("Flip Monster Portraits: " + PortraitFolder + " 里没有可翻转的怪物立绘。");
                return;
            }
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("翻转怪物立绘",
                "将水平翻转 " + count + " 张怪物立绘，首领立绘保持不变。\n\n直接改写 PNG 源文件；再执行一次可翻回。",
                "翻转", "取消")) return;

            int flipped = 0;
            var skipped = new List<string>();
            foreach (var path in paths)
            {
                if (IsBoss(path, bosses)) { skipped.Add(Path.GetFileNameWithoutExtension(path)); continue; }
                if (FlipPng(path)) flipped++;
            }
            AssetDatabase.Refresh();
            Debug.Log("FLIP_MONSTER_PORTRAITS flipped=" + flipped
                + " skippedBosses=" + string.Join(",", skipped)
                + " folder=" + PortraitFolder);
        }

        static HashSet<string> BossIds(GameCatalog catalog)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (catalog.BossEncounters == null) return ids;
            foreach (var lineup in catalog.BossEncounters)
            {
                if (lineup == null || lineup.Slots == null) continue;
                foreach (var slot in lineup.Slots)
                    if (slot && !string.IsNullOrEmpty(slot.Id)) ids.Add(slot.Id);
            }
            return ids;
        }

        static bool IsBoss(string path, HashSet<string> bosses)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            return bosses.Contains(id) || id.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool FlipPng(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    Debug.LogError("Flip Monster Portraits: 无法读取 " + path);
                    return false;
                }
                int width = texture.width, height = texture.height;
                var pixels = texture.GetPixels32();
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width / 2; x++)
                    {
                        int left = row + x, right = row + width - 1 - x;
                        var swap = pixels[left];
                        pixels[left] = pixels[right];
                        pixels[right] = swap;
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                return true;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
