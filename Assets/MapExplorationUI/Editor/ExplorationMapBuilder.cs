using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Core;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.ExplorationEditor
{
    public static class ExplorationMapBuilder
    {
        public const string Root = "Assets/MapExplorationUI";
        public const string ScenePath = Root + "/Scenes/MapExploration.unity";
        public const string PrefabPath = Root + "/Prefabs/MapExploration.prefab";
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../交付/地图探索UI"));
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static Font font;
        static Sprite white;
        static readonly Color Cream = new Color32(224,207,176,255), Muted = new Color32(150,160,143,255), Green = new Color32(104,192,142,255);

        static SpriteRect Cut(string name, int x, int top, int w, int h, int height, Vector4 border = default)
        {
            return new SpriteRect { name=name, rect=new Rect(x,height-top-h,w,h), border=border,
                pivot=Vector2.one*.5f, alignment=SpriteAlignment.Center, spriteID=GUID.Generate() };
        }
        static void Import(string file, string name, Vector4 border = default, SpriteRect[] cuts = null)
        {
            string path = Root + "/Art/" + file + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit=100; importer.alphaIsTransparency=true; importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=4096;
            importer.npotScale=TextureImporterNPOTScale.None; importer.filterMode=FilterMode.Bilinear; importer.wrapMode=TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects().ToDictionary(r=>r.name,r=>r.spriteID);
            var rects = cuts ?? new[]{Cut(name,0,0,texture.width,texture.height,texture.height,border)};
            foreach (var rect in rects) if(previous.TryGetValue(rect.name,out var id)) rect.spriteID=id;
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));
            provider.Apply(); importer.SaveAndReimport();
            foreach(var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()) sprites[sprite.name]=sprite;
        }
        static void Prepare()
        {
            AssetDatabase.Refresh(); sprites.Clear();
            Import("地图","Map",cuts:new[]{ Cut("Map",0,0,1024,1530,1530,new Vector4(72,72,72,72)), Cut("Grid",92,126,36,36,1530) });
            Import("楼层计数器","Counter"); Import("角色框","Party");
            Import("通用边框","Option",new Vector4(72,72,72,100));
            for(int i=0;i<10;i++) Import("计数标"+i,"Digit"+i);
            font=AssetDatabase.LoadAssetAtPath<Font>("Assets/IndustrialBattleUI/Art/HudChinese.ttf");
            // Shared solid sprite; Image needs an assigned sprite before fillAmount changes its mesh.
            white=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/IndustrialBattleUI/Art/SolidFill.png");
            Directory.CreateDirectory(Output);
        }
        [MenuItem("GameMake/Map Exploration/Build New Scene")]
        public static void Build()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Prepare();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.orthographic=true; camera.orthographicSize=540; camera.transform.position=new Vector3(0,0,-100);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color32(10,12,12,255); camera.farClipPlane=1000;
            new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var go=new GameObject("Map Exploration UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(ExplorationMapController));
            var canvas=go.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=20; canvas.pixelPerfect=true;
            var scaler=go.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var root=(RectTransform)go.transform; root.localScale=Vector3.one;
            var view=go.GetComponent<ExplorationMapController>();
            var page=Group(root,"Exploration Page",0,0,1920,1080); Stretch(page,Vector2.zero,Vector2.zero); view.Page=page.gameObject;
            var background=Solid(page,"Background",0,0,1920,1080,new Color32(10,12,12,255));
            Stretch(background.rectTransform,Vector2.zero,Vector2.zero); background.raycastTarget=true;
            Text(page,"Terminal Title","探索终端",24,16,300,32,24,Cream);
            Text(page,"Terminal Index","EXPLORATION  /  01",24,149,380,22,16,Muted);
            var counterRoot=Group(page,"Floor and Room Counter",16,58,448,82);
            Art(counterRoot,"Counter Frame","Counter",0,0,448,82);
            // Replace the small baked English labels with separate readable Chinese text.
            Solid(counterRoot,"Floor Label Back",24,27,54,18,Color.black);
            Text(counterRoot,"Floor Label","楼层",24,24,54,24,18,Cream,TextAnchor.MiddleCenter);
            Solid(counterRoot,"Room Label Back",254,27,50,18,Color.black);
            Text(counterRoot,"Room Label","房间",252,24,56,24,18,Cream,TextAnchor.MiddleCenter);
            var counter=counterRoot.gameObject.AddComponent<ExplorationCounter>(); view.Counter=counter;
            counter.Digits=Enumerable.Range(0,10).Select(i=>sprites["Digit"+i]).ToArray();
            counter.FloorWindow=Group(counterRoot,"Floor Digits",87,17,104,46);
            counter.RoomWindow=Group(counterRoot,"Room Digits",314,17,104,46);
            counter.FloorSlots=DigitSlots(counter.FloorWindow); counter.RoomSlots=DigitSlots(counter.RoomWindow); counter.Paint();

            var party=Group(page,"Party Column",8,178,270,842); Art(party,"Four Character Frames","Party",0,0,270,842);
            var catalog=AssetDatabase.LoadAssetAtPath<GameCatalog>("Assets/StageOne/Resources/StageOne/GameCatalog.asset");
            view.Party=new ExplorationPartySlot[4];
            for(int i=0;i<4;i++)
            {
                var slot=new ExplorationPartySlot(); view.Party[i]=slot;
                var area=Group(party,"Character "+(i+1),35,22+i*210,184,172);
                var hit=Solid(area,"Character Button",0,0,184,172,Color.clear); slot.Hit=Click(hit);
                slot.Selection=Solid(area,"Selected Character",0,0,184,3,Green); slot.Selection.enabled=i==0;
                slot.Name=Text(area,"Name",catalog.Heroes[i].DisplayName,0,8,184,27,22,Cream,TextAnchor.MiddleCenter);
                var silhouette=Group(area,"Replaceable Character Silhouette",58,39,68,85);
                silhouette.gameObject.AddComponent<CanvasRenderer>();
                slot.Placeholder=silhouette.gameObject.AddComponent<SilhouetteGraphic>();
                slot.Placeholder.Silhouette=catalog.Heroes[i].Silhouette; slot.Placeholder.color=Cream; slot.Placeholder.Accent=new Color32(124,90,61,255); slot.Placeholder.raycastTarget=false;
                slot.Portrait=Solid(area,"Portrait - Drop Sprite Here",35,37,114,91,Color.white); slot.Portrait.sprite=null; slot.Portrait.enabled=false; slot.Portrait.preserveAspect=true;
                Solid(area,"Health Track",0,131,184,6,new Color32(44,29,27,255)); slot.Health=Solid(area,"Health Fill",0,131,184,6,new Color32(174,71,58,255)); Fill(slot.Health);
                Solid(area,"Sanity Track",0,141,184,6,new Color32(25,40,40,255)); slot.Sanity=Solid(area,"Sanity Fill",0,141,184,6,new Color32(205,205,205,255)); Fill(slot.Sanity); slot.Sanity.fillAmount=.5f;
                slot.Vitals=Text(area,"Health and Sanity","生命 "+catalog.Heroes[i].MaxHp+"/"+catalog.Heroes[i].MaxHp+"   理智 50",-8,151,200,21,16,Muted,TextAnchor.MiddleCenter);
            }
            view.PartyDetail=Text(page,"Selected Character Summary",catalog.Heroes[0].DisplayName+"  ·  探索队员",24,1034,420,28,18,Muted,TextAnchor.MiddleCenter);

            var map=Group(page,"Map Panel",480,16,1424,1048); Stretch(map,new Vector2(480,16),new Vector2(-16,-16));
            var grid=Art(map,"Native Grid Tiles","Grid",58,58,1308,932); grid.type=Image.Type.Tiled;
            Stretch(grid.rectTransform,new Vector2(58,58),new Vector2(-58,-58));
            var frame=Art(map,"Map Border","Map",0,0,1424,1048,true,1.2f); frame.fillCenter=false; Stretch(frame.rectTransform,Vector2.zero,Vector2.zero);
            view.Heading=Text(map,"Floor Heading","第 01 层  /  探索路线",78,67,800,46,32,Cream);
            view.Progress=Text(map,"Room Progress","房间 01 / 07",80,121,800,29,20,Muted);
            view.Stock=Text(map,"Supplies and Sanity","矿石  00     /     玩家理智  50",80,167,900,27,18,Green);
            Text(map,"Route Legend","◆ 可选     ◆ 已通过     ◇ 未探索",80,646,900,29,18,Muted);
            var route=Group(map,"Route Connections",0,0,1424,700).gameObject.AddComponent<ExplorationRouteGraphic>(); route.raycastTarget=false; view.Connections=route;
            view.RouteSlots=new ExplorationRouteSlot[11];
            for(int i=0;i<view.RouteSlots.Length;i++)
            {
                var slot=new ExplorationRouteSlot(); view.RouteSlots[i]=slot;
                slot.Root=Group(map,"Route Node "+(i+1),60+i*130,372,120,100);
                slot.Diamond=Solid(slot.Root,"Node Diamond",44,6,32,32,Cream); slot.Diamond.rectTransform.localEulerAngles=new Vector3(0,0,45);
                slot.Diamond.rectTransform.pivot=Vector2.one*.5f; slot.Diamond.rectTransform.anchoredPosition=new Vector2(60,-22);
                var core=Solid(slot.Root,"Node Center",48,10,24,24,new Color32(8,22,15,255)); core.rectTransform.localEulerAngles=new Vector3(0,0,45);
                core.rectTransform.pivot=Vector2.one*.5f; core.rectTransform.anchoredPosition=new Vector2(60,-22);
                slot.Number=Text(slot.Root,"Number","01",35,6,50,32,17,Cream,TextAnchor.MiddleCenter);
                slot.Name=Text(slot.Root,"Room Name","未探索",-10,63,140,28,18,Muted,TextAnchor.MiddleCenter);
            }
            view.ChoiceHeading=Text(map,"Choices Heading","前方房间",60,728,1100,36,24,Cream);
            view.Choices=Group(map,"Room Options - Pop Up",60,790,1304,166);
            view.ChoiceFade=view.Choices.gameObject.AddComponent<CanvasGroup>();
            view.Options=new ExplorationRoomOption[3];
            for(int i=0;i<3;i++) view.Options[i]=MakeOption(view.Choices,i);
            view.Note=Text(map,"Exploration Hint","选择一间房继续探索",60,974,1280,25,18,Muted);
            var sample=new MapState(); MapGenerator.Advance(sample,catalog.MapRules(),new RunRandom(44972),50); view.RenderMap(sample,catalog.MapRules());
            GameMake.SharedUI.Editor.RetroScreenStyling.ApplyMap(go);
            MapEntranceBuilder.Attach(go);
            PrefabUtility.SaveAsPrefabAssetAndConnect(go,PrefabPath,InteractionMode.AutomatedAction);

            // Keep existing rooms, combat and settlement playable in this new scene. The debug console is
            // a separate prefab installed into mainScene by IndustrialDebugConsoleInstaller.
            var app=new GameObject("Exploration Game State").AddComponent<StageOneApp>(); app.Catalog=catalog;
            view.Source=app;
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            var legacy=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab");
            var fallback=(GameObject)PrefabUtility.InstantiatePrefab(legacy); fallback.name="Existing Room and Battle Pages";
            var battle=fallback.GetComponent<GothicBattleHudController>(); if(battle) { battle.BattleSource=app; PrefabUtility.RecordPrefabInstancePropertyModifications(battle); }
            var presentation=fallback.GetComponent<ModularHudController>();
            if(presentation) { presentation.BattleSource=app; presentation.FollowStageOneBattle=false; PrefabUtility.RecordPrefabInstancePropertyModifications(presentation); }
            foreach(var oldCounter in fallback.GetComponentsInChildren<MechanicalRoundCounter>(true))
            { oldCounter.BattleSource=app; oldCounter.FollowStageOneBattle=false; PrefabUtility.RecordPrefabInstancePropertyModifications(oldCounter); }
            ExplorationSceneIntegration.AttachToCurrentScene();
            EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
            // Append only; retain the user's startup scene and existing build entries.
            if(!EditorBuildSettings.scenes.Any(s=>s.path==ScenePath))
                EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(ScenePath,true)}).ToArray();
            Render(canvas,camera,"MapExploration-1920x1080",1920,1080);
            Debug.Log("MAP_EXPLORATION_BUILD_OK "+ScenePath);
        }
        static ExplorationRoomOption MakeOption(Transform parent,int index)
        {
            var root=Group(parent,"Room Option "+(index+1),index*424,0,408,166);
            var option=root.gameObject.AddComponent<ExplorationRoomOption>(); option.CandidateIndex=index;
            var frame=Art(root,"Event Option Frame","Option",0,0,408,166,true,4); Stretch(frame.rectTransform,Vector2.zero,Vector2.zero);
            option.Hit=Click(frame);
            option.Accent=Solid(root,"Room Type Accent",20,37,3,89,Green);
            option.Number=Text(root,"Option Number",(index+1).ToString("D2"),32,27,40,28,20,Muted);
            option.Title=Text(root,"Room Title","事件房间",78,27,302,29,24,Cream);
            option.Description=Text(root,"Room Description","调查异象，决定下一步行动。",32,68,344,46,18,Muted);
            option.Action=Text(root,"Enter Room","进入房间  →",32,123,342,27,18,Cream);
            return option;
        }
        static Image[] DigitSlots(Transform parent)
        {
            var slots=new Image[10];
            for(int i=0;i<slots.Length;i++)
            {
                slots[i]=Art(parent,"Digit "+i,"Digit0",0,0,29,46);
                slots[i].rectTransform.pivot=Vector2.one*.5f;
            }
            return slots;
        }
        public static void Render(Canvas canvas,Camera camera,string name,int width,int height)
        {
            var scaler=canvas.GetComponent<CanvasScaler>(); var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
            var target=new RenderTexture(width,height,24); var capture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                scaler.enabled=false; canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=100;
                camera.targetTexture=target; canvas.scaleFactor=Mathf.Min(width/1920f,height/1080f);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
                capture.ReadPixels(new Rect(0,0,width,height),0,0); capture.Apply();
                File.WriteAllBytes(Path.Combine(Output,name+".png"),capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=oldActive; camera.targetTexture=oldTarget;
                UnityEngine.Object.DestroyImmediate(capture); UnityEngine.Object.DestroyImmediate(target);
                canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; scaler.enabled=true;
            }
        }
        static RectTransform Group(Transform parent,string name,float x,float y,float w,float h)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1); rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(w,h); return rt;
        }
        static void Stretch(RectTransform rt,Vector2 min,Vector2 max) { rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=min; rt.offsetMax=max; }
        static Image Art(Transform parent,string name,string key,float x,float y,float w,float h,bool sliced=false,float multiplier=1)
        {
            var image=Group(parent,name,x,y,w,h).gameObject.AddComponent<Image>(); image.sprite=sprites[key]; image.raycastTarget=false;
            image.type=sliced?Image.Type.Sliced:Image.Type.Simple; image.pixelsPerUnitMultiplier=multiplier; return image;
        }
        static Image Solid(Transform parent,string name,float x,float y,float w,float h,Color color)
        {
            var image=Group(parent,name,x,y,w,h).gameObject.AddComponent<Image>(); image.sprite=white; image.color=color; image.raycastTarget=false; return image;
        }
        static Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var label=Group(parent,name,x,y,w,h).gameObject.AddComponent<Text>(); label.font=font; label.text=value; label.fontSize=size;
            label.color=color; label.alignment=align; label.raycastTarget=false; label.horizontalOverflow=HorizontalWrapMode.Wrap; label.verticalOverflow=VerticalWrapMode.Truncate; return label;
        }
        static Button Click(Image image)
        {
            image.raycastTarget=true; var button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors; colors.highlightedColor=new Color(1.12f,1.12f,1.04f); colors.pressedColor=new Color(.72f,.83f,.72f); colors.fadeDuration=.1f; button.colors=colors;
            button.navigation=new Navigation{mode=Navigation.Mode.None}; return button;
        }
        static void Fill(Image image) { image.type=Image.Type.Filled; image.fillMethod=Image.FillMethod.Horizontal; image.fillOrigin=0; }
    }
}
