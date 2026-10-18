using System.IO;
using GameMake.EventUI;
using GameMake.ExplorationEditor;
using GameMake.SharedUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventEditor
{
    public static class SupplyStationBuilder
    {
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/补给站UI"));

        [MenuItem("GameMake/Event UI/Apply Supply Station Layout")]
        public static void Apply()
        {
            Directory.CreateDirectory(Output);
            var root=PrefabUtility.LoadPrefabContents(IndustrialEventBuilder.Prefab);
            try
            {
                Attach(root.GetComponent<IndustrialEventPage>());
                PrefabUtility.SaveAsPrefabAsset(root,IndustrialEventBuilder.Prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            ExplorationSceneIntegration.Apply();
        }

        public static void Attach(IndustrialEventPage owner)
        {
            if(owner.SupplyView) { SupplyShopBuilder.Attach(owner); return; }
            var view=owner.gameObject.AddComponent<SupplyStationView>(); owner.SupplyView=view; view.Owner=owner;
            var page=owner.Page.transform;
            view.OriginalMonitor=page.Find("Event Monitor - 显示屏").gameObject;
            view.OriginalStory=page.Find("Event Story - 可滚动文本").gameObject;
            view.DefaultChoicePosition=owner.ChoiceColumn.anchoredPosition;
            view.DefaultHeadingPosition=owner.ChoiceHeading.rectTransform.anchoredPosition;
            var layout=Group(page,"Supply Station - 补给站",0,0,1920,1080); view.Layout=layout.gameObject;
            var monitor=Group(layout,"Mascot Monitor - 吉祥物显示屏",796,100,1044,680); view.Monitor=monitor;
            var original=view.OriginalMonitor.transform;
            var grid=original.Find("Monitor Grid").GetComponent<Image>();
            var tiles=Art(monitor,"Monitor Grid",grid.sprite,35,35,974,610,1); tiles.type=Image.Type.Tiled;
            var border=original.Find("Monitor Frame").GetComponent<Image>();
            Art(monitor,"Monitor Frame",border.sprite,0,0,1044,680,1.8f).fillCenter=false;
            Label(owner,monitor,"Display Heading","补给站",52,42,700,34,26,owner.ScreenTitle.color);
            view.Location=Label(owner,monitor,"Location","",52,88,900,24,16,owner.ScreenLocation.color);
            var viewport=Group(monitor,"Mascot Viewport",44,126,956,452);
            viewport.gameObject.AddComponent<RectMask2D>();
            view.Mascot=Art(viewport,"Mascot - 拖入吉祥物图片",null,0,0,956,452,1);
            view.Mascot.type=Image.Type.Simple; view.Mascot.preserveAspect=true; view.Mascot.enabled=false;
            view.MascotPlaceholder=Label(owner,viewport,"Mascot Placeholder","吉祥物展示区",0,0,956,452,22,owner.ScreenLocation.color);
            view.MascotPlaceholder.alignment=TextAnchor.MiddleCenter;
            view.Status=Label(owner,monitor,"Supply Status","",52,609,940,24,18,owner.ScreenCode.color);
            var noise=Object.Instantiate(original.Find("Retro Signal Noise - 屏幕干扰").gameObject,monitor).GetComponent<RetroScreenNoise>();
            noise.name="Retro Signal Noise - 屏幕干扰";
            var noiseRect=(RectTransform)noise.transform; noiseRect.anchoredPosition=new Vector2(40,-38); noiseRect.sizeDelta=new Vector2(964,604);

            var actions=Group(layout,"Recovery Actions - 恢复操作",796,804,1044,112);
            view.Actions=actions.gameObject.AddComponent<CanvasGroup>();
            var frame=owner.Continue.GetComponent<Image>().sprite;
            view.Heal=Action(owner,actions,frame,"Heal - 回血","回血",0,out view.HealDetail);
            view.Calm=Action(owner,actions,frame,"Calm - 回理智","回理智",534,out view.CalmDetail);
            view.Layout.SetActive(false);
            SupplyShopBuilder.Attach(owner);
        }
        static Button Action(IndustrialEventPage owner,Transform parent,Sprite frame,string name,string title,float x,out Text detail)
        {
            var image=Art(parent,name,frame,x,0,510,112,3); image.raycastTarget=true;
            var button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            button.colors=owner.Continue.colors; button.navigation=new Navigation {mode=Navigation.Mode.None};
            var heading=Label(owner,image.transform,"Title",title,24,19,462,35,28,owner.ScreenTitle.color);
            heading.alignment=TextAnchor.MiddleCenter;
            detail=Label(owner,image.transform,"Detail","",24,61,462,27,18,owner.ScreenLocation.color);
            detail.alignment=TextAnchor.MiddleCenter; return button;
        }
        static RectTransform Group(Transform parent,string name,float x,float y,float w,float h)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h); return rect;
        }
        static Image Art(Transform parent,string name,Sprite sprite,float x,float y,float w,float h,float multiplier)
        {
            var image=Group(parent,name,x,y,w,h).gameObject.AddComponent<Image>(); image.sprite=sprite; image.type=Image.Type.Sliced;
            image.pixelsPerUnitMultiplier=multiplier; image.raycastTarget=false; return image;
        }
        static Text Label(IndustrialEventPage owner,Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color)
        {
            var text=Group(parent,name,x,y,w,h).gameObject.AddComponent<Text>(); text.font=owner.Header.font;
            text.text=value; text.fontSize=size; text.color=color; text.alignment=TextAnchor.MiddleLeft; text.raycastTarget=false; return text;
        }
    }
}
