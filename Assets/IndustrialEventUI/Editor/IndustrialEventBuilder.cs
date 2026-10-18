using System.IO;
using System.Linq;
using GameMake.EventUI;
using GameMake.ExplorationEditor;
using GameMake.SharedUI;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventEditor
{
    public static class IndustrialEventBuilder
    {
        public const string Root="Assets/IndustrialEventUI";
        public const string Prefab=Root+"/Prefabs/IndustrialEventPage.prefab";
        public const string Scene=Root+"/Scenes/IndustrialEvent.unity";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/事件页面"));
        static Font font;
        static Sprite frame,screen,grid;
        static readonly Color Cream=new Color32(224,207,176,255), Muted=new Color32(151,163,145,255), Green=new Color32(146,187,149,255);
        static Sprite Import(string name,Vector4 border)
        {
            string path=Root+"/Art/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.spriteBorder=border; importer.spritePixelsPerUnit=100; importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=4096;
            importer.npotScale=TextureImporterNPOTScale.None; importer.alphaIsTransparency=true;
            importer.filterMode=FilterMode.Bilinear; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        [MenuItem("GameMake/Event UI/Build And Apply Event Page")]
        public static void Build()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root+"/Prefabs"); Directory.CreateDirectory(Root+"/Scenes"); Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            frame=Import("通用边框",new Vector4(72,72,72,100));
            screen=Import("地图",new Vector4(72,72,72,72));
            font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GothicHUD/Art/HudChinese.ttf");
            string gridPath=Root+"/Art/ScreenGrid.asset";
            var tile=Sprite.Create(screen.texture,new Rect(92,screen.texture.height-162,36,36),Vector2.one*.5f,100,0,SpriteMeshType.FullRect);
            tile.name="ScreenGrid"; grid=AssetDatabase.LoadAssetAtPath<Sprite>(gridPath);
            if(grid) { EditorUtility.CopySerialized(tile,grid); Object.DestroyImmediate(tile); EditorUtility.SetDirty(grid); }
            else { grid=tile; AssetDatabase.CreateAsset(grid,gridPath); }

            var root=new GameObject("Industrial Event UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(IndustrialEventPage));
            try
            {
                var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=25; canvas.pixelPerfect=true;
                var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                var view=root.GetComponent<IndustrialEventPage>();
                var page=Group(root.transform,"Event Page",0,0,1920,1080); view.Page=page.gameObject;
                page.anchorMin=page.anchorMax=page.pivot=new Vector2(.5f,.5f); page.anchoredPosition=Vector2.zero;
                var background=Solid(page,"Page Background",0,0,1920,1080,new Color32(10,12,12,255)); background.raycastTarget=true;
                Art(page,"Event Outer Frame",frame,20,20,1880,1040,2.5f);
                view.Header=Label(page,"Header","事件终端",66,38,700,36,25,Cream);
                Label(page,"Terminal Index","MORROW  /  FIELD TERMINAL",1380,42,460,27,17,Muted,TextAnchor.MiddleRight);

                var monitor=Group(page,"Event Monitor - 显示屏",66,92,1030,422);
                var tiles=Art(monitor,"Monitor Grid",grid,35,35,960,352); tiles.type=Image.Type.Tiled;
                var border=Art(monitor,"Monitor Frame",screen,0,0,1030,422,1.8f); border.fillCenter=false;
                view.EventIllustration=Solid(monitor,"Event Illustration - 拖入事件图片",45,45,940,332,Color.white);
                view.EventIllustration.preserveAspect=true; view.EventIllustration.enabled=false;
                Label(monitor,"Display Heading","现场记录",55,45,700,30,20,Green);
                view.ScreenTitle=Label(monitor,"Event Title","铁门后的敲击声",65,132,900,72,40,Cream,TextAnchor.MiddleCenter);
                view.ScreenCode=Label(monitor,"Event Code","EVT-003  /  阶段 1 / 2",65,213,900,40,20,Green,TextAnchor.MiddleCenter);
                view.ScreenLocation=Label(monitor,"Location","第 01 层  ·  房间 01",55,340,900,27,18,Muted);
                var noise=Group(monitor,"Retro Signal Noise - 屏幕干扰",40,38,950,346).gameObject.AddComponent<RetroScreenNoise>();
                noise.BandCount=1; noise.ThicknessRange=new Vector2(.6f,1); noise.SpeedRange=new Vector2(160,240);
                noise.DisplacementRange=new Vector2(1.5f,3); noise.ReappearDelayRange=new Vector2(3,5);
                noise.DistortionMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/SharedUI/Runtime/RetroScreenDistortion.mat"); noise.material=noise.DistortionMaterial;
                noise.raycastTarget=false;

                var story=Group(page,"Event Story - 可滚动文本",66,548,1030,430);
                Art(story,"Story Frame",frame,0,0,1030,430,3);
                Label(story,"Story Heading","事件记录",30,21,700,32,23,Cream);
                var scroll=Group(story,"Story Scroll",30,70,970,310).gameObject.AddComponent<ScrollRect>();
                scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
                view.StoryScroll=scroll;
                var viewport=Group(scroll.transform,"Viewport",0,0,938,310);
                Solid(viewport,"Scroll Hit Area",0,0,938,310,Color.clear).raycastTarget=true;
                viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport=viewport;
                view.Body=Label(viewport,"Event Text","",4,4,914,302,25,Cream,TextAnchor.UpperLeft);
                view.Body.supportRichText=false; view.Body.lineSpacing=1.3f;
                view.Body.verticalOverflow=VerticalWrapMode.Overflow;
                view.StoryContent=view.Body.rectTransform; scroll.content=view.StoryContent;
                var track=Solid(scroll.transform,"Scrollbar",954,0,8,310,new Color32(42,49,43,255));
                track.raycastTarget=true;
                var slider=track.gameObject.AddComponent<Scrollbar>(); slider.direction=Scrollbar.Direction.BottomToTop;
                var slideArea=Group(track.transform,"Sliding Area",0,0,8,310);
                var handle=Solid(slideArea,"Handle",0,0,8,60,new Color32(148,165,143,255));
                handle.raycastTarget=true;
                handle.rectTransform.anchorMin=Vector2.zero; handle.rectTransform.anchorMax=Vector2.one;
                handle.rectTransform.offsetMin=handle.rectTransform.offsetMax=Vector2.zero;
                slider.handleRect=handle.rectTransform; slider.targetGraphic=handle; slider.size=.3f;
                scroll.verticalScrollbar=slider; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
                view.ReadingStatus=Label(story,"Reading Status","正在读取事件记录…",30,390,970,22,15,Muted);

                view.ChoiceHeading=Label(page,"Choices Heading","行动选择",1160,102,680,35,25,Cream);
                view.ChoiceColumn=Group(page,"Choice Column",1160,164,680,680);
                view.Choices=new EventChoiceView[4];
                for(int i=0;i<4;i++)
                {
                    var option=Group(view.ChoiceColumn,"Choice "+(i+1),0,i*150,680,132);
                    var card=option.gameObject.AddComponent<EventChoiceView>(); view.Choices[i]=card;
                    card.Fade=option.gameObject.AddComponent<CanvasGroup>();
                    var box=Art(option,"Option Frame",frame,0,0,680,132,3);
                    box.rectTransform.anchorMin=Vector2.zero; box.rectTransform.anchorMax=Vector2.one; box.rectTransform.offsetMin=box.rectTransform.offsetMax=Vector2.zero;
                    card.Hit=Button(box); card.Number=Label(option,"Number",(i+1).ToString("D2"),28,25,50,36,23,Muted);
                    card.Title=Label(option,"Title","选择",88,24,560,38,26,Cream);
                    card.Detail=Label(option,"Detail","",88,67,550,42,19,Muted,TextAnchor.UpperLeft);
                }
                var advance=Art(page,"Continue Or Leave - 继续或离开",frame,1528,932,312,94,3);
                view.Continue=Button(advance); view.ContinueFade=advance.gameObject.AddComponent<CanvasGroup>();
                view.ContinueLabel=Label(advance.transform,"Label","继续  →",24,19,264,54,27,Cream,TextAnchor.MiddleCenter);
                view.ContinuePosition=advance.rectTransform.anchoredPosition;
                SupplyStationBuilder.Attach(view);
                var existing=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                if(existing && existing.GetComponent<IndustrialEventPage>().SupplyView)
                    view.SupplyView.Mascot.sprite=existing.GetComponent<IndustrialEventPage>().SupplyView.Mascot.sprite;
                view.Page.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            ExplorationSceneIntegration.Apply();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var app=Object.FindObjectOfType<StageOneApp>();
            var preview=new GameObject("Event Preview Entry").AddComponent<EventPreviewStart>(); preview.Source=app;
            EditorSceneManager.SaveScene(scene,Scene);
            EditorSceneManager.OpenScene(ExplorationSceneIntegration.MainScene,OpenSceneMode.Single);
            Debug.Log("INDUSTRIAL_EVENT_BUILD_OK");
        }
        static RectTransform Group(Transform parent,string name,float x,float y,float width,float height)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(width,height); return rect;
        }
        static Image Solid(Transform parent,string name,float x,float y,float width,float height,Color color)
        {
            var image=Group(parent,name,x,y,width,height).gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image;
        }
        static Image Art(Transform parent,string name,Sprite sprite,float x,float y,float width,float height,float multiplier=1)
        {
            var image=Solid(parent,name,x,y,width,height,Color.white); image.sprite=sprite; image.type=Image.Type.Sliced; image.pixelsPerUnitMultiplier=multiplier; return image;
        }
        static Text Label(Transform parent,string name,string value,float x,float y,float width,float height,int size,Color tint,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var text=Group(parent,name,x,y,width,height).gameObject.AddComponent<Text>(); text.font=font; text.text=value; text.fontSize=size; text.color=tint;
            text.alignment=align; text.raycastTarget=false; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Truncate; return text;
        }
        static Button Button(Image image)
        {
            image.raycastTarget=true; var button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors; colors.highlightedColor=new Color(1.15f,1.12f,1.05f); colors.pressedColor=new Color(.7f,.78f,.7f); colors.fadeDuration=.1f; button.colors=colors;
            button.navigation=new Navigation {mode=Navigation.Mode.None}; return button;
        }
    }
}
