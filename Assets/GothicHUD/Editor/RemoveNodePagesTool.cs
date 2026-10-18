using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameMake.GothicHUDEditor
{
    /// <summary>
    /// One-off cleanup for prefabs built before the node pages were retired. The generator no longer creates
    /// them, but an already-authored GothicBattleHUD still carries the Node Pages subtree and, once
    /// GothicNodeHudController is deleted, a missing script on its root. This rewrites the prefab in place
    /// with PrefabUtility, so it is safe to run while the editor is open.
    /// </summary>
    public static class RemoveNodePagesTool
    {
        public const string NodePagesName = "Node Pages - 节点页";

        [MenuItem("GameMake/Gothic HUD/Remove Node Pages - 节点页")]
        public static void Remove()
        {
            var contents = PrefabUtility.LoadPrefabContents(ModularHudBuilder.PrefabPath);
            if (!contents)
            {
                Debug.LogError("Cannot load " + ModularHudBuilder.PrefabPath);
                return;
            }

            int pages = 0, components = 0;
            try
            {
                // Only the outer-most match is removed; its children go with it.
                var page = contents.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.StartsWith("Node Pages"));
                if (page)
                {
                    Object.DestroyImmediate(page.gameObject);
                    pages++;
                }
                foreach (var transform in contents.GetComponentsInChildren<Transform>(true))
                    components += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                if (pages + components > 0) PrefabUtility.SaveAsPrefabAsset(contents, ModularHudBuilder.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("REMOVE_NODE_PAGES_DONE pages=" + pages + " missingComponents=" + components
                + (pages == 0 && components == 0 ? " (already clean)" : ""));
        }
    }
}
