using System.IO;
using System.Linq;
using GameMake.CharacterUI;
using GameMake.ExplorationEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.CharacterEditor
{
    public static class CharacterPanelBuilder
    {
        public const string Root="Assets/CharacterPanelUI";
        public const string Prefab=Root+"/Prefabs/CharacterPanel.prefab";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/角色管理弹窗"));
        static Sprite Import(string name,Vector4 border)
        {
            string path=Root+"/Art/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.spriteBorder=border; importer.spritePixelsPerUnit=100; importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=2048;
            importer.npotScale=TextureImporterNPOTScale.None; importer.alphaIsTransparency=true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        [MenuItem("GameMake/Character Panel/Build And Apply")]
        public static void Build()
        {
            if(!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root+"/Prefabs"); Directory.CreateDirectory(Output); AssetDatabase.Refresh();
            var previous=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var preservedArt=previous?previous.GetComponent<CharacterPanelView>().FullBodyArt:null;
            var go=new GameObject("Character Management",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(CharacterPanelView));
            try
            {
                var canvas=go.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=45;
                var scaler=go.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                var view=go.GetComponent<CharacterPanelView>(); view.Font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GothicHUD/Art/HudChinese.ttf");
                if(preservedArt!=null) view.FullBodyArt=preservedArt.ToArray();
                view.PanelSprite=Import("技能放置栏",new Vector4(68,48,68,48)); view.ItemSprite=Import("物品框",new Vector4(48,48,48,48));
                view.Healthy=new[]{"肢体1","肢体 2","肢体 3","肢体 4"}.Select(n=>Import(n,Vector4.zero)).ToArray();
                view.Damaged=new[]{"肢体破损1","肢体破损2","肢体破损3","肢体破损 4"}.Select(n=>Import(n,Vector4.zero)).ToArray();
                view.BuildLayout(); PrefabUtility.SaveAsPrefabAsset(go,Prefab);
            }
            finally { Object.DestroyImmediate(go); }
            AdjustExistingLayouts(); AssetDatabase.SaveAssets(); ExplorationSceneIntegration.Apply();
            Debug.Log("CHARACTER_PANEL_BUILD_OK");
        }
        static void AdjustExistingLayouts()
        {
            string battle="Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";
            var root=PrefabUtility.LoadPrefabContents(battle);
            try
            {
                var party=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Party - 四人队伍");
                party.localScale=new Vector3(.88f,.88f,1); PrefabUtility.SaveAsPrefabAsset(root,battle);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            string map=ExplorationMapBuilder.PrefabPath; root=PrefabUtility.LoadPrefabContents(map);
            try
            {
                var party=(RectTransform)root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Party Column");
                party.anchoredPosition=new Vector2(8,party.anchoredPosition.y); PrefabUtility.SaveAsPrefabAsset(root,map);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
