using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using GameMake.IndustrialBattleUI;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.IndustrialBattleEditor
{
    public static class IndustrialBattleBuilder
    {
        const string Root="Assets/IndustrialBattleUI";
        public const string Prefab="Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";
        public const string Scene="Assets/GothicHUD/Scenes/GothicBattleHUD.unity";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/战斗UI重建"));
        static readonly Dictionary<string,Sprite> art=new Dictionary<string,Sprite>();
        static Font font;
        static Sprite white;
        static readonly Color Cream=new Color32(224,207,176,255), Muted=new Color32(168,163,146,255);
        static void Import(string name,Vector4 border=default)
        {
            string path=Root+"/Art/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100; importer.spriteBorder=border; importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096; importer.npotScale=TextureImporterNPOTScale.None; importer.filterMode=FilterMode.Bilinear;
            importer.SaveAndReimport(); art[name]=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        [MenuItem("GameMake/Industrial Battle/Rebuild Battle Page")]
        public static void Build()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Output); AssetDatabase.Refresh(); art.Clear();
            foreach(string path in Directory.GetFiles(Root+"/Art","*.png")) Import(Path.GetFileNameWithoutExtension(path));
            Import("操作框",new Vector4(70,65,70,65)); Import("技能放置栏",new Vector4(65,55,65,55));
            Import("技能介绍栏",new Vector4(190,90,190,90)); Import("物品框",new Vector4(60,60,60,60));
            SlicePanel("CompactPortrait",0,0,350,367);
            SlicePanel("CompactVitalFrame",495,65,660,50,new Vector4(14,10,14,10));
            SlicePanel("CompactLimbMeter",404,365,158,41,new Vector4(12,10,12,10));
            SlicePanel("HealthSymbol",430,68,51,45);
            SlicePanel("SanitySymbol",430,132,51,48);
            SlicePanel("CounterLeftRail",0,0,810,217,new Vector4(150,0,50,0),"蒸汽朋克机械回合计数器");
            SlicePanel("CounterCenterFrame",810,0,472,217,default,"蒸汽朋克机械回合计数器");
            SlicePanel("CounterRightRail",1282,0,810,217,new Vector4(50,0,150,0),"蒸汽朋克机械回合计数器");
            font=AssetDatabase.LoadAssetAtPath<Font>("Assets/GothicHUD/Art/HudChinese.ttf");
            white=AssetDatabase.LoadAllAssetsAtPath("Assets/GothicHUD/Art/SolidFill.asset").OfType<Sprite>().First();
            var go=PrefabUtility.LoadPrefabContents(Prefab);
            try { Restyle(go); PrefabUtility.SaveAsPrefabAsset(go,Prefab); }
            finally { PrefabUtility.UnloadPrefabContents(go); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera"; camera.orthographic=true;
            camera.orthographicSize=540; camera.transform.position=new Vector3(0,0,-100); camera.farClipPlane=1000;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color32(12,14,14,255);
            new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var app=new GameObject("Battle Game State").AddComponent<StageOneApp>();
            app.Catalog=AssetDatabase.LoadAssetAtPath<GameCatalog>("Assets/StageOne/Resources/StageOne/GameCatalog.asset"); app.ExternalBattleUi=true;
            var hud=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            var chrome=hud.GetComponent<GothicBattleHudController>(); chrome.BattleSource=app; PrefabUtility.RecordPrefabInstancePropertyModifications(chrome);
            var nodes=hud.GetComponent<GothicNodeHudController>(); nodes.Source=app; PrefabUtility.RecordPrefabInstancePropertyModifications(nodes);
            var presentation=hud.GetComponent<ModularHudController>(); presentation.BattleSource=app; presentation.FollowStageOneBattle=false; PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            var counter=hud.GetComponentInChildren<MechanicalRoundCounter>(true); counter.BattleSource=app; counter.FollowStageOneBattle=false; PrefabUtility.RecordPrefabInstancePropertyModifications(counter);
            var preview=new GameObject("Battle Preview Entry").AddComponent<BattlePreviewStart>(); preview.Source=app;
            GameMake.ExplorationEditor.ExplorationSceneIntegration.AttachToCurrentScene();
            EditorSceneManager.SaveScene(scene,Scene); AssetDatabase.SaveAssets();
            Debug.Log("INDUSTRIAL_BATTLE_BUILD_OK");
        }
        static void Restyle(GameObject go)
        {
            var root=go.transform; var panel=go.GetComponent<GothicHudPanel>(); var hud=go.GetComponent<ModularHudController>();
            var chrome=go.GetComponent<GothicBattleHudController>();
            var details=go.GetComponent<IndustrialBattleDetails>(); if(!details) details=go.AddComponent<IndustrialBattleDetails>(); details.BattleHud=chrome;
            go.GetComponent<Canvas>().pixelPerfect=true; hud.FollowStageOneBattle=false;
            var bottom=Find(root,"Bottom HUD - 操作面板");
            var bottomRect=(RectTransform)bottom;
            bottomRect.sizeDelta=new Vector2(1920,360);
            bottomRect.anchorMin=bottomRect.anchorMax=bottomRect.pivot=new Vector2(.5f,0);
            bottomRect.anchoredPosition=Vector2.zero;
            Skin(Find(bottom,"Operation Outer Frame"),"操作框",true,2);
            SetRect(Find(bottom,"Operation Outer Frame"),5,0,1908,356);
            // Replace only battle widgets; keep the room pages and every game-command binding.
            var oldPortrait=Find(bottom,"Portrait Panel - 人物头像"); var stats=Find(bottom,"Vitals - 生命理智"); var limbs=Find(bottom,"Limbs - 肢体状态");
            SetRect(oldPortrait,18,28,780,280); SetRect(stats,18,28,780,280); SetRect(limbs,18,28,780,280);
            foreach(var tr in new[]{oldPortrait,stats,limbs}) foreach(var g in tr.GetComponentsInChildren<Graphic>(true)) g.gameObject.SetActive(false);
            var healthPanel=Image(oldPortrait,"Industrial Character Panel","操作框",0,0,780,280,true,3);
            healthPanel.transform.SetAsFirstSibling();
            Image(oldPortrait,"Compact Portrait Frame","CompactPortrait",8,9,216,218).transform.SetSiblingIndex(1);
            Place(panel.MainPortrait,oldPortrait,32,36,160,155); panel.MainPortrait.preserveAspect=true;
            var main=GetOrFigure(oldPortrait,"Main Figure",67,36,90,155); details.MainFigure=main;
            Place(panel.CharacterName,oldPortrait,22,222,184,28); Type(panel.CharacterName,24,Cream,TextAnchor.MiddleCenter);
            Place(panel.CharacterSubtitle,oldPortrait,20,250,190,20); Type(panel.CharacterSubtitle,15,Muted,TextAnchor.MiddleCenter);
            Image(stats,"Compact Health Frame","CompactVitalFrame",280,43,306,31,true).transform.SetAsFirstSibling();
            Image(stats,"Compact Sanity Frame","CompactVitalFrame",280,84,306,31,true).transform.SetAsFirstSibling();
            Image(stats,"Compact Health Symbol","HealthSymbol",244,45,28,25);
            Image(stats,"Compact Sanity Symbol","SanitySymbol",244,86,28,26);
            Place(hud.HealthFill,stats,287,49,292,19); ConfigureFill(hud.HealthFill,new Color32(167,53,43,255));
            Place(hud.HealthValue,stats,287,47,292,23); Type(hud.HealthValue,17,Cream,TextAnchor.MiddleCenter);
            Place(hud.SanityFill,stats,287,90,292,19); ConfigureFill(hud.SanityFill,new Color32(205,205,205,255));
            Place(hud.SanityValue,stats,287,88,292,23); Type(hud.SanityValue,17,Cream,TextAnchor.MiddleCenter);
            var sanityOutline=hud.SanityValue.GetComponent<Outline>();
            if(!sanityOutline) sanityOutline=hud.SanityValue.gameObject.AddComponent<Outline>();
            sanityOutline.effectColor=new Color(0,0,0,.95f); sanityOutline.effectDistance=new Vector2(1,-1);
            var attributes=Group(stats,"Attributes - 属性预留",614,29,142,225);
            Label(attributes,"Heading","角色属性",0,0,142,27,19,Cream);
            string[] attributeNames={"攻击","防御","速度","闪避","命中","暴击"};
            for(int i=0;i<attributeNames.Length;i++)
            {
                Label(attributes,"Attribute Label "+i,attributeNames[i],0,35+i*29,76,25,17,Muted);
                Label(attributes,"Attribute Value "+i,"—",82,35+i*29,60,25,17,Cream,TextAnchor.MiddleRight);
            }
            string[] healthy={"肢体1","肢体 2","肢体 3","肢体 4"}, broken={"肢体破损1","肢体破损2","肢体破损3","肢体破损 4"};
            for(int i=0;i<4;i++)
            {
                var limb=hud.Limbs[i]; float x=240+i*88;
                limb.HealthySprite=art[healthy[i]]; limb.BrokenSprite=art[broken[i]];
                Place(limb.Icon,limbs,x,146,82,84); limb.Icon.preserveAspect=false;
                Image(limbs,"Compact Limb Meter "+i,"CompactLimbMeter",x,231,82,24,true).transform.SetAsFirstSibling();
                Place(limb.Fill,limbs,x+5,237,72,12); ConfigureFill(limb.Fill,new Color32(164,56,39,255));
                Place(limb.Value,limbs,x,233,82,20); Type(limb.Value,15,Cream,TextAnchor.MiddleCenter);
                Place(limb.Status,limbs,x,254,82,21); Type(limb.Status,15,Muted,TextAnchor.MiddleCenter);
                // The supplied panel has no clear text row below the bars. The icon and durability
                // already convey damage; keep the bound status label available for later layout edits.
                limb.Status.gameObject.SetActive(false);
            }
            var skills=Find(bottom,"Skills - 技能与位移"); SetRect(skills,812,28,568,280);
            Image(skills,"Industrial Skill Tray","技能放置栏",0,0,568,280,true,3).transform.SetAsFirstSibling();
            SetRect(Find(skills,"Skills Heading"),24,18,400,26);
            details.SkillNames=new Text[5];
            for(int i=0;i<5;i++)
            {
                var slot=panel.SkillButtons[i].transform.parent; SetRect(slot,22+i*87,57,82,137);
                Skin(panel.SkillButtons[i].transform,"物品框",true,5); SetRect(panel.SkillButtons[i].transform,0,0,80,90);
                SetRect(panel.SkillIcons[i].transform,8,8,64,70);
                Skin(Find(slot,"Hotkey Plate"),"图层 "+(i+1)); SetRect(Find(slot,"Hotkey Plate"),25,96,30,30);
                SetRect(panel.SkillHotkeys[i].transform,25,96,30,30);
                SetRect(panel.SkillSelections[i].transform,26,-19,30,22);
                details.SkillNames[i]=Label(slot,"Skill Name","技能 "+(i+1),4,16,72,60,17,Cream,TextAnchor.MiddleCenter);
            }
            var move=hud.DisplacementButton.transform.parent; SetRect(move,467,57,78,137);
            Skin(hud.DisplacementButton.transform,"物品框",true,5); SetRect(hud.DisplacementButton.transform,0,0,76,90);
            Skin(Find(move,"Displacement Icon"),"位移"); SetRect(Find(move,"Displacement Icon"),9,13,58,58);
            SetRect(Find(move,"Displacement Label"),0,98,76,26);
            var items=Find(skills,"Items - 快捷物品"); SetRect(items,24,211,470,46);
            foreach(var image in items.GetComponentsInChildren<Image>(true)) if(image.name=="Item Frame") { image.sprite=art["物品框"]; image.type=UnityEngine.UI.Image.Type.Sliced; image.pixelsPerUnitMultiplier=5; }
            var description=Find(bottom,"Description - 技能介绍"); SetRect(description,1400,28,500,280);
            var screen=Find(description,"Description Frame"); Skin(screen,"技能介绍栏",true,3); SetRect(screen,0,0,500,280);
            SetRect(panel.DescriptionTitle.transform,68,27,365,32); Type(panel.DescriptionTitle,23,Cream);
            SetRect(panel.DescriptionBody.transform,68,77,365,170); Type(panel.DescriptionBody,18,new Color32(166,194,163,255),TextAnchor.UpperLeft);

            var party=Find(root,"Party - 四人队伍"); SetRect(party,14,183,172,536);
            party.localScale=new Vector3(.88f,.88f,1);
            Skin(Find(party,"Party Mechanical Frame"),"角色框"); SetRect(Find(party,"Party Mechanical Frame"),0,0,172,536);
            details.PartyNames=new Text[4]; details.PartyFigures=new SilhouetteGraphic[4];
            var catalog=AssetDatabase.LoadAssetAtPath<GameCatalog>("Assets/StageOne/Resources/StageOne/GameCatalog.asset");
            for(int i=0;i<4;i++)
            {
                var slot=panel.PartyButtons[i].transform.parent; SetRect(slot,22,14+i*134,118,112);
                SetRect(panel.PartyButtons[i].transform,0,0,118,112); SetRect(panel.PartyPortraits[i].transform,12,24,94,84);
                Find(slot,"Party Number").gameObject.SetActive(false);
                SetRect(panel.PartySelections[i].transform,125,36,22,32);
                details.PartyNames[i]=Label(slot,"Industrial Party Name",catalog.Heroes[i].DisplayName,0,0,118,24,18,Cream,TextAnchor.MiddleCenter);
                details.PartyFigures[i]=GetOrFigure(slot,"Party Figure",36,26,48,80); details.PartyFigures[i].Silhouette=catalog.Heroes[i].Silhouette;
            }

            var counter=chrome.Counter; counter.FollowStageOneBattle=false;
            var top=counter.transform; SetRect(top,0,18,1100,180); var topRect=(RectTransform)top;
            topRect.anchorMin=topRect.anchorMax=new Vector2(.5f,1); topRect.pivot=new Vector2(.5f,1); topRect.anchoredPosition=new Vector2(0,-18);
            topRect.localScale=Vector3.one*.78f;
            // Stretch only the straight rail sections; retain the center, connectors and arrowhead proportions.
            const float frameScale=1100f/2092f, railExtension=160f;
            float railWidth=810*frameScale, centerWidth=472*frameScale;
            var mechanical=Find(top,"Mechanical Frame"); Skin(mechanical,"CounterCenterFrame"); SetRect(mechanical,railWidth,47,centerWidth,114);
            Image(top,"Extended Left Rail","CounterLeftRail",-railExtension,47,railWidth+railExtension,114,true,1/frameScale);
            Image(top,"Extended Right Rail","CounterRightRail",railWidth+centerWidth,47,railWidth+railExtension,114,true,1/frameScale);
            Find(top,"Number Backplate").gameObject.SetActive(false);
            var wheel=Image(top,"Round Wheel","旋转轮盘",436,0,228,242); details.Wheel=wheel.rectTransform;
            wheel.rectTransform.pivot=Vector2.one*.5f; wheel.rectTransform.anchoredPosition=new Vector2(550,-113); wheel.transform.SetAsFirstSibling();
            Image(top,"Round Heading Plate","回合计数",492,0,118,47);
            var digits=counter.DigitContainer; SetRect(digits,550,105,120,94); digits.pivot=Vector2.one*.5f;
            counter.FitDigitsToContainer=true;
            digits.SetAsLastSibling(); counter.DigitSprites=Enumerable.Range(0,10).Select(i=>art["计数标"+i]).ToArray();
            foreach(var digit in counter.DigitSlots) digit.preserveAspect=true; counter.RefreshDisplay();
            details.TurnLabel=Label(top,"Current Turn","等待行动",300,244,500,38,28,Cream,TextAnchor.MiddleCenter);
            // Keep the new portrait frame at card scale, and leave enough space for readable target data.
            foreach(var card in chrome.Allies.Concat(chrome.Enemies))
            {
                var rt=(RectTransform)card.transform; rt.sizeDelta=new Vector2(160,270);
                SetRect(card.Background.transform,0,0,160,270);
                Image(card.transform,"Industrial Card Frame","人物头像框",0,0,160,166).transform.SetSiblingIndex(1);
                if(!card.Art.GetComponent<CanvasRenderer>()) card.Art.gameObject.AddComponent<CanvasRenderer>();
                SetRect(card.Art.transform,37,18,86,128);
                SetRect(card.Name.transform,3,172,154,28); Type(card.Name,20,Cream,TextAnchor.MiddleCenter);
                SetRect(card.HealthFill.transform.parent,6,206,148,20);
                foreach(var g in card.HealthFill.transform.parent.GetComponentsInChildren<RectTransform>()) if(g!=card.HealthFill.transform.parent) g.sizeDelta=new Vector2(148,20);
                Type(card.HealthValue,16,Cream,TextAnchor.MiddleCenter);
                SetRect(card.Status.transform,6,231,148,38); Type(card.Status,15,Muted,TextAnchor.UpperLeft);
                if(card.Intent) { SetRect(card.Intent.transform,6,231,148,38); Type(card.Intent,15,Muted,TextAnchor.UpperLeft); }
                SetRect(card.ActiveBadge.transform,7,-29,52,23); SetRect(card.TargetBadge.transform,102,-29,52,23);
                Type(card.ActiveBadge.GetComponent<Text>(),16,Cream); Type(card.TargetBadge.GetComponent<Text>(),16,Cream,TextAnchor.MiddleRight);
                card.Art.color=Cream;
            }
            SetRect(Find(root,"Ally Positions - 我方位置"),213,405,643,270);
            SetRect(Find(root,"Enemy Positions - 敌方位置"),1164,405,643,270);
            // Inset the content on every side so it cannot cover the outer frame decoration.
            foreach(var section in new[]{oldPortrait,stats,limbs,skills,description})
            {
                var rect=(RectTransform)section;
                rect.anchoredPosition=new Vector2(960+(rect.anchoredPosition.x-960)*.97f,-44);
                rect.localScale=Vector3.one*.97f;
            }
            // Existing gold formation rails are supplied again; retain their clean imported slices.
            SetRect(Find(bottom,"Allied Formation - 我方站位"),193,-44,680,44);
            SetRect(Find(bottom,"Enemy Formation - 敌方站位"),1144,-44,680,44);
            foreach(var label in bottom.GetComponentsInChildren<Text>(true)) if(label.fontSize<15) label.fontSize=15;
            Skin(Find(chrome.DebugButton.transform,"Border"),"物品框",true,5);
            hud.RefreshView();
            GameMake.SharedUI.Editor.RetroScreenStyling.ApplyBattle(go);
            PrefabUtility.SaveAsPrefabAsset(counter.gameObject,"Assets/GothicHUD/Prefabs/MechanicalRoundCounter.prefab");
        }
        static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
        // Reuse regions of the supplied panel as independent Unity sprites; the source PNG stays intact.
        static void SlicePanel(string name,int x,int top,int width,int height,Vector4 border=default,string source="角色血条框")
        {
            string folder=Root+"/Generated";
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root,"Generated");
            var texture=art[source].texture;
            var sprite=Sprite.Create(texture,new Rect(x,texture.height-top-height,width,height),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,border);
            sprite.name=name;
            string path=folder+"/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if(existing) { EditorUtility.CopySerialized(sprite,existing); UnityEngine.Object.DestroyImmediate(sprite); EditorUtility.SetDirty(existing); art[name]=existing; }
            else { AssetDatabase.CreateAsset(sprite,path); art[name]=sprite; }
        }
        static void SetRect(Transform tr,float x,float y,float w,float h)
        {
            var rt=(RectTransform)tr; rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1); rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(w,h); rt.localScale=Vector3.one;
        }
        static RectTransform Group(Transform p,string name,float x,float y,float w,float h)
        {
            var tr=p.Find(name) as RectTransform;
            if(!tr) { tr=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); tr.SetParent(p,false); }
            tr.gameObject.SetActive(true); SetRect(tr,x,y,w,h); return tr;
        }
        static void Place(Graphic g,Transform p,float x,float y,float w,float h) { g.transform.SetParent(p,false); g.gameObject.SetActive(true); SetRect(g.transform,x,y,w,h); }
        static void Skin(Transform tr,string key,bool sliced=false,float multiplier=1)
        {
            var image=tr.GetComponent<Image>(); image.sprite=art[key]; image.type=sliced?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple;
            image.pixelsPerUnitMultiplier=multiplier; image.color=Color.white;
        }
        static Image Image(Transform p,string name,string key,float x,float y,float w,float h,bool sliced=false,float multiplier=1)
        {
            var rt=Group(p,name,x,y,w,h); var image=rt.GetComponent<Image>(); if(!image) image=rt.gameObject.AddComponent<Image>();
            Skin(rt,key,sliced,multiplier); image.raycastTarget=false; return image;
        }
        static Text Label(Transform p,string name,string value,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var rt=Group(p,name,x,y,w,h); var text=rt.GetComponent<Text>(); if(!text) text=rt.gameObject.AddComponent<Text>(); text.font=font; text.text=value; text.raycastTarget=false; Type(text,size,color,align); return text;
        }
        static void Type(Text t,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft) { t.fontSize=size; t.color=color; t.alignment=align; }
        static void ConfigureFill(Image image,Color tint) { image.sprite=white; image.type=UnityEngine.UI.Image.Type.Filled; image.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal; image.fillOrigin=0; image.color=tint; }
        static SilhouetteGraphic GetOrFigure(Transform p,string name,float x,float y,float w,float h)
        {
            var rt=Group(p,name,x,y,w,h); if(!rt.GetComponent<CanvasRenderer>()) rt.gameObject.AddComponent<CanvasRenderer>();
            var figure=rt.GetComponent<SilhouetteGraphic>(); if(!figure) figure=rt.gameObject.AddComponent<SilhouetteGraphic>();
            figure.color=Cream; figure.Accent=new Color32(143,101,68,255); figure.raycastTarget=false; return figure;
        }
    }
}
