using System.Linq;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using UnityEditor;
using UnityEngine;

namespace GameMake.SharedUI.Editor
{
    public static class RetroScreenStyling
    {
        public const string MapPrefab="Assets/MapExplorationUI/Prefabs/MapExploration.prefab";
        public const string BattlePrefab="Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";
        [MenuItem("GameMake/UI/Apply Retro Screen Effects")]
        public static void Apply()
        {
            foreach(var path in new[]{MapPrefab,BattlePrefab})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(path==MapPrefab) ApplyMap(root); else ApplyBattle(root);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        public static void ApplyMap(GameObject root)
        {
            var view=root.GetComponent<ExplorationMapController>();
            foreach(var party in view.Party) party.Sanity.color=new Color32(205,205,205,255);
            var grid=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Native Grid Tiles");
            var effect=Effect(grid.parent,1,new Vector2(.6f,1.2f),new Vector2(260,420),new Vector2(2,4));
            var rect=effect.rectTransform;
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(58,58); rect.offsetMax=new Vector2(-58,-58);
            effect.transform.SetAsLastSibling();
        }
        public static void ApplyBattle(GameObject root)
        {
            var frame=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Description Frame");
            var effect=Effect(frame.parent,1,new Vector2(.6f,1f),new Vector2(110,180),new Vector2(1.5f,3));
            var rect=effect.rectTransform;
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(67,-25); rect.sizeDelta=new Vector2(370,230);
            effect.transform.SetAsLastSibling();
        }
        static RetroScreenNoise Effect(Transform parent,int count,Vector2 thickness,Vector2 speed,Vector2 displacement)
        {
            var child=parent.Find("Retro Signal Noise - 屏幕干扰");
            if(!child)
            {
                child=new GameObject("Retro Signal Noise - 屏幕干扰",typeof(RectTransform),typeof(CanvasRenderer),typeof(RetroScreenNoise)).transform;
                child.SetParent(parent,false);
            }
            var effect=child.GetComponent<RetroScreenNoise>();
            effect.raycastTarget=false; effect.color=Color.white;
            effect.BandCount=count; effect.ThicknessRange=thickness; effect.SpeedRange=speed;
            effect.DisplacementRange=displacement; effect.ReappearDelayRange=new Vector2(3,5); effect.Animate=true;
            const string path="Assets/SharedUI/Runtime/RetroScreenDistortion.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat)
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/SharedUI/Runtime/RetroScreenDistortion.shader");
                mat=new Material(shader); AssetDatabase.CreateAsset(mat,path);
            }
            effect.DistortionMaterial=mat; effect.material=mat; effect.transform.localScale=Vector3.one;
            return effect;
        }
    }
}
