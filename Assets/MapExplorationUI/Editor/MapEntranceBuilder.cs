using GameMake.ExplorationUI;
using UnityEditor;
using UnityEngine;

namespace GameMake.ExplorationEditor
{
    public static class MapEntranceBuilder
    {
        public static void Attach(GameObject root)
        {
            var view=root.GetComponent<ExplorationMapController>();
            var entrance=root.GetComponent<MapPageEntrance>(); if(!entrance) entrance=root.AddComponent<MapPageEntrance>();
            entrance.MapPanel=(RectTransform)view.Page.transform.Find("Map Panel");
            entrance.CounterPanel=(RectTransform)view.Counter.transform;
            entrance.MapHome=entrance.MapPanel.anchoredPosition; entrance.CounterHome=entrance.CounterPanel.anchoredPosition;
            entrance.MapFade=entrance.MapPanel.GetComponent<CanvasGroup>(); if(!entrance.MapFade) entrance.MapFade=entrance.MapPanel.gameObject.AddComponent<CanvasGroup>();
            entrance.CounterFade=entrance.CounterPanel.GetComponent<CanvasGroup>(); if(!entrance.CounterFade) entrance.CounterFade=entrance.CounterPanel.gameObject.AddComponent<CanvasGroup>();
            view.Entrance=entrance;
        }
        [MenuItem("GameMake/Map Exploration/Apply Page Entrance")]
        public static void Apply()
        {
            var root=PrefabUtility.LoadPrefabContents(ExplorationMapBuilder.PrefabPath);
            try { Attach(root); PrefabUtility.SaveAsPrefabAsset(root,ExplorationMapBuilder.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); ExplorationSceneIntegration.Apply();
        }
    }
}
