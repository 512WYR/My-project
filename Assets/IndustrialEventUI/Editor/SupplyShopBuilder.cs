using GameMake.EventUI;
using GameMake.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventEditor
{
    public static class SupplyShopBuilder
    {
        static Font font;
        static Sprite frame;
        static readonly Color Cream=new Color32(224,207,176,255), Muted=new Color32(155,168,151,255);
        public static void Attach(IndustrialEventPage owner)
        {
            if(owner.GetComponent<SupplyShopView>()) return;
            font=owner.Header.font; frame=owner.Continue.GetComponent<Image>().sprite;
            var shop=owner.gameObject.AddComponent<SupplyShopView>(); shop.Owner=owner;
            var page=Group(owner.transform,"Supply Shop - 商店弹窗",0,0,1920,1080); shop.Page=page.gameObject;
            page.anchorMin=Vector2.zero; page.anchorMax=Vector2.one; page.offsetMin=page.offsetMax=Vector2.zero;
            var shade=Solid(page,"Backdrop",0,0,1920,1080,new Color(0,0,0,.78f));
            shade.rectTransform.anchorMin=Vector2.zero; shade.rectTransform.anchorMax=Vector2.one;
            shade.rectTransform.offsetMin=shade.rectTransform.offsetMax=Vector2.zero; shop.Backdrop=Button(shade);
            var window=Group(page,"Shop Window",0,0,1600,900); shop.Window=window;
            window.anchorMin=window.anchorMax=window.pivot=Vector2.one*.5f;
            shop.Fade=window.gameObject.AddComponent<CanvasGroup>();
            var backing=Art(window,"Shop Frame",frame,0,0,1600,900); backing.raycastTarget=true;
            Label(window,"Title","补给商店",44,28,700,46,30,Cream);
            shop.Balance=Label(window,"Balance","",1110,35,360,38,25,Cream,TextAnchor.MiddleRight);
            shop.Close=Button(Art(window,"Close",frame,1494,28,66,52));
            Label(shop.Close.transform,"Label","×",0,0,66,52,32,Cream,TextAnchor.MiddleCenter);

            var monitor=Group(window,"Shop Monitor - 商品电子屏幕",44,100,480,688);
            var source=owner.SupplyView.Monitor;
            var sourceGrid=source.Find("Monitor Grid").GetComponent<Image>();
            var tiles=Art(monitor,"Grid",sourceGrid.sprite,28,28,424,632); tiles.type=Image.Type.Tiled;
            var rim=Art(monitor,"Monitor Frame",source.Find("Monitor Frame").GetComponent<Image>().sprite,0,0,480,688);
            rim.pixelsPerUnitMultiplier=2; rim.fillCenter=false;
            shop.ProductCategory=Label(monitor,"Category","",44,42,392,34,20,Muted);
            shop.Mascot=Solid(monitor,"Mascot - 吉祥物",48,98,384,220,Color.white);
            shop.Mascot.preserveAspect=true; shop.Mascot.enabled=false;
            Label(monitor,"Terminal Code","MORROW  /  SUPPLY",44,330,392,26,16,Muted);
            shop.ProductName=Label(monitor,"Product Name","",44,372,392,64,30,Cream);
            shop.ProductDescription=Label(monitor,"Description","",44,450,392,138,21,Muted,TextAnchor.UpperLeft);
            shop.ProductOwned=Label(monitor,"Stock","",44,616,392,30,18,Cream);
            var noise=Object.Instantiate(source.GetComponentInChildren<RetroScreenNoise>(true),monitor);
            var noiseRect=(RectTransform)noise.transform; noiseRect.anchoredPosition=new Vector2(30,-30); noiseRect.sizeDelta=new Vector2(420,628);

            var names=new[]{"义体","饰品","血药","理智药","手术包"}; shop.Tabs=new Button[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                var tab=Art(window,"Category - "+names[i],frame,568+i*196,100,180,54); shop.Tabs[i]=Button(tab);
                Label(tab.transform,"Label",names[i],8,6,164,42,22,Cream,TextAnchor.MiddleCenter);
            }
            var scroll=Group(window,"Products Scroll",568,178,976,610).gameObject.AddComponent<ScrollRect>(); shop.Scroll=scroll;
            scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=38;
            var viewport=Group(scroll.transform,"Viewport",0,0,956,610); viewport.gameObject.AddComponent<RectMask2D>();
            var hit=Solid(viewport,"Scroll Hit Area",0,0,956,610,Color.clear); hit.raycastTarget=true;
            scroll.viewport=viewport;
            shop.Content=Group(viewport,"Products",0,0,956,610); scroll.content=shop.Content;
            var track=Solid(scroll.transform,"Scrollbar",968,0,8,610,new Color(.12f,.15f,.13f));
            var bar=track.gameObject.AddComponent<Scrollbar>(); track.raycastTarget=true;
            bar.direction=Scrollbar.Direction.BottomToTop;
            var handle=Solid(track.transform,"Handle",0,0,8,120,Muted); handle.raycastTarget=true;
            handle.rectTransform.anchorMin=Vector2.zero; handle.rectTransform.anchorMax=Vector2.one;
            handle.rectTransform.offsetMin=handle.rectTransform.offsetMax=Vector2.zero;
            bar.handleRect=handle.rectTransform; bar.targetGraphic=handle;
            scroll.verticalScrollbar=bar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;

            var cardFrame=Art(shop.Content,"Product Template",frame,0,0,468,222);
            var card=cardFrame.gameObject.AddComponent<SupplyShopCard>(); shop.CardTemplate=card;
            card.Frame=cardFrame; card.Select=Button(cardFrame);
            card.Title=Label(card.transform,"Title","",22,17,314,36,25,Cream);
            card.Owned=Label(card.transform,"Owned","",338,22,108,26,17,Muted,TextAnchor.MiddleRight);
            card.Description=Label(card.transform,"Description","",22,62,424,92,18,Muted,TextAnchor.UpperLeft);
            card.Price=Label(card.transform,"Price","",24,174,230,30,23,Cream);
            card.Buy=Button(Art(card.transform,"Buy",frame,288,166,156,42));
            card.BuyLabel=Label(card.Buy.transform,"Label","购买",8,4,140,34,20,Cream,TextAnchor.MiddleCenter);
            card.gameObject.SetActive(false);
            shop.Notice=Label(window,"Purchase Notice","选择商品查看详情，使用矿石购买。",52,820,1484,34,22,Muted);
            page.gameObject.SetActive(false);
        }
        static RectTransform Group(Transform parent,string name,float x,float y,float w,float h)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h); return rect;
        }
        static Image Solid(Transform parent,string name,float x,float y,float w,float h,Color color)
        {
            var image=Group(parent,name,x,y,w,h).gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image;
        }
        static Image Art(Transform parent,string name,Sprite sprite,float x,float y,float w,float h)
        {
            var image=Solid(parent,name,x,y,w,h,Color.white); image.sprite=sprite; image.type=Image.Type.Sliced; image.pixelsPerUnitMultiplier=3; return image;
        }
        static Text Label(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var text=Group(parent,name,x,y,w,h).gameObject.AddComponent<Text>(); text.font=font; text.text=value;
            text.fontSize=size; text.color=color; text.alignment=align; text.raycastTarget=false; return text;
        }
        static Button Button(Image image)
        {
            image.raycastTarget=true; var button=image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors; colors.highlightedColor=new Color(1.15f,1.12f,1.05f); colors.pressedColor=new Color(.65f,.75f,.65f);
            colors.disabledColor=new Color(.42f,.42f,.42f,.8f); colors.fadeDuration=.1f; button.colors=colors;
            button.navigation=new Navigation {mode=Navigation.Mode.None}; return button;
        }
    }
}
