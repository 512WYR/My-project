using System;
using System.Linq;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.CharacterUI
{
    public sealed class CharacterPanelView : MonoBehaviour
    {
        public StageOneApp Source;
        public Font Font;
        public Sprite PanelSprite, ItemSprite;
        public Sprite[] Healthy, Damaged;
        public GameObject Page;
        public Text Heading, Vitals, Notice, PickerTitle;
        public Image[] LimbImages;
        public Text[] LimbLabels;
        public Button[] LimbButtons, PotionButtons;
        public Text[] PotionDetails;
        public RectTransform Accessories, PickerItems;
        public ScrollRect AccessoryScroll, PickerScroll;
        public Button Close;
        public Button Backdrop;
        [Tooltip("按队伍顺序：盾卫、战士、奶妈、枪手。拖入完整立绘后自动保持比例显示。")]
        public Sprite[] FullBodyArt=new Sprite[4];
        public Image FullBodyImage;
        public Text FullBodyName, FullBodyPlaceholder;
        public RectTransform WindowReveal, WindowContent;
        public CanvasGroup WindowFade;
        public CharacterLinkGraphic Connector, LowerConnector;
        public bool EntranceComplete { get; private set; }
        float entranceStarted;
        StageOneApp bound;
        bool hooked;
        int mode=-1; // -1 instruction; 0..3 limb; 4 accessory stock; 5 equipped item.
        string inspectedItem, lastHero;
        int previousAccessoryCount=-1;
        float pickerHeight;
        readonly Color cream=new Color32(224,207,176,255), muted=new Color32(157,163,151,255);

        void OnEnable() { Canvas.willRenderCanvases+=BeforeRender; Bind(); Sync(); }
        void Start() { Bind(); Sync(); }
        void OnDisable() { Canvas.willRenderCanvases-=BeforeRender; if(bound) bound.Changed-=Sync; bound=null; }
        void Bind()
        {
            if(!Page) return;
            if(!Source) Source=FindObjectOfType<StageOneApp>();
            if(bound!=Source) { if(bound) bound.Changed-=Sync; bound=Source; if(bound) bound.Changed+=Sync; }
            if(hooked) return; hooked=true;
            Close.onClick.AddListener(()=>Source.CloseCharacterPanel()); Backdrop.onClick.AddListener(()=>Source.CloseCharacterPanel());
            for(int i=0;i<4;i++) { int part=i; LimbButtons[i].onClick.AddListener(()=>ShowLimb(part)); }
            PotionButtons[0].onClick.AddListener(()=>Source.CharacterUsePotion(ConsumableKind.HpRestore));
            PotionButtons[1].onClick.AddListener(()=>Source.CharacterUsePotion(ConsumableKind.SanityLock));
        }
        public void ShowLimb(int part) { mode=part; inspectedItem=null; Sync(); PickerScroll.verticalNormalizedPosition=1; }
        public void ShowAccessories() { mode=4; inspectedItem=null; Sync(); PickerScroll.verticalNormalizedPosition=1; }
        public void ShowEquipped(string id) { mode=5; inspectedItem=id; Sync(); PickerScroll.verticalNormalizedPosition=1; }
        public void Sync()
        {
            if(!Page || !Source) return;
            bool open=Source.CharacterPanelOpen && Source.CharacterPageAvailable && Source.CharacterHero!=null;
            bool opening=open && (!Page.activeSelf || lastHero!=Source.CharacterHero.Id);
            Page.SetActive(open);
            if(!open) { lastHero=null; EntranceComplete=false; if(Source.CharacterPanelOpen) Source.CloseCharacterPanel(); return; }
            var hero=Source.CharacterHero;
            if(lastHero!=hero.Id) { mode=-1; inspectedItem=null; lastHero=hero.Id; previousAccessoryCount=-1; AccessoryScroll.verticalNormalizedPosition=1; }
            Heading.text=hero.Name+"  /  角色整备";
            int heroIndex=Source.Run.Heroes.IndexOf(hero);
            FullBodyImage.sprite=heroIndex>=0 && heroIndex<FullBodyArt.Length?FullBodyArt[heroIndex]:null;
            FullBodyImage.enabled=FullBodyImage.sprite; FullBodyPlaceholder.gameObject.SetActive(!FullBodyImage.sprite);
            FullBodyName.text=hero.Name;
            if(opening) BeginEntrance();
            Vitals.text="生命  "+hero.Hp+" / "+hero.MaxHp+"       理智  "+hero.Sanity.ToString("0")+" / 100"+(hero.Dead?"       已阵亡":"");
            Notice.text=Source.CanManageCharacter ? (Source.AwaitingNode?"消耗手术包可安装义体 · 点击药品使用 · 点击空框装备饰品":"我方行动中 · 使用药品或手术包装载消耗本次行动") : "当前仅可查看，等待我方行动后可操作";
            for(int i=0;i<4;i++)
            {
                var limb=LimbSystem.Get(hero,(LimbPart)i);
                var condition=LimbSystem.ConditionOf(limb);
                LimbImages[i].sprite=condition==LimbCondition.Intact?Healthy[i]:Damaged[i];
                string state=limb.ProstheticFitted?"义体已装载":condition==LimbCondition.Severed?"断肢":condition==LimbCondition.Injured?"重伤":"完好";
                LimbLabels[i].text=LimbSystem.PartName((LimbPart)i)+" · "+state+"\n"+limb.Hp+" / "+limb.MaxHp;
                LimbLabels[i].color=condition==LimbCondition.Intact?muted:new Color32(215,115,92,255);
                var hover=LimbButtons[i].GetComponent<LimbInstallHover>();
                if(hover) hover.enabled=!limb.ProstheticFitted;
            }
            for(int i=0;i<2;i++)
            {
                var kind=i==0?ConsumableKind.HpRestore:ConsumableKind.SanityLock;
                string blocked=Source.CharacterPotionBlockedReason(kind);
                PotionButtons[i].interactable=blocked==null;
                PotionDetails[i].text="库存 ×"+ConsumableSystem.Count(Source.Run,kind)+"\n"+(blocked ?? (i==0?"点击恢复 50% 最大生命":"点击锁定本场／下场理智"));
            }
            RefreshAccessories(); RefreshPicker();
        }
        void BeginEntrance()
        {
            entranceStarted=Time.unscaledTime; EntranceComplete=false;
            WindowFade.blocksRaycasts=false; WindowFade.interactable=false;
            UpdateEntrance();
        }
        void LateUpdate() { if(Page && Page.activeSelf && WindowReveal) UpdateEntrance(); }
        void BeforeRender() { if(Page && Page.activeSelf && WindowReveal) UpdateEntrance(); }
        void UpdateEntrance()
        {
            float elapsed=Time.unscaledTime-entranceStarted;
            float rail=Mathf.Clamp01(elapsed/.12f);
            float reveal=Mathf.Clamp01((elapsed-.1f)/.3f);
            float eased=1-Mathf.Pow(1-reveal,3);
            WindowReveal.sizeDelta=new Vector2(1440*eased,860);
            WindowContent.anchoredPosition=new Vector2(-36*(1-eased),0);
            WindowFade.alpha=Mathf.Clamp01(reveal*4);
            EntranceComplete=reveal>=1;
            WindowFade.blocksRaycasts=WindowFade.interactable=EntranceComplete;
            var page=(RectTransform)Page.transform;
            float centerY=-page.rect.height*.5f;
            float left=page.rect.xMin, dock=WindowReveal.anchoredPosition.x;
            Connector.SetConnection(new Vector2(left,centerY+370),new Vector2(dock,centerY+370),rail);
            LowerConnector.SetConnection(new Vector2(left,centerY-370),new Vector2(dock,centerY-370),rail);
        }
        void Clear(Transform root)
        {
            foreach(Transform child in root) { child.gameObject.SetActive(false); if(Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        }
        void RefreshAccessories()
        {
            float scroll=AccessoryScroll.verticalNormalizedPosition;
            Clear(Accessories); var worn=Source.CharacterHero.EquippedAccessories.Where(a=>a!=null).ToArray();
            int count=worn.Length+1;
            Accessories.sizeDelta=new Vector2(714,Mathf.Max(188,Mathf.Ceil(count/4f)*94));
            for(int i=0;i<count;i++)
            {
                var box=Art(Accessories,"Accessory "+i,ItemSprite,(i%4)*180,(i/4)*94,166,86);
                var button=Click(box);
                if(i==worn.Length)
                {
                    Label(box.transform,"Add","＋",10,5,146,37,32,cream,TextAnchor.MiddleCenter);
                    Label(box.transform,"Caption","装载饰品",10,48,146,27,18,muted,TextAnchor.MiddleCenter);
                    button.onClick.AddListener(ShowAccessories);
                }
                else
                {
                    string id=worn[i].Id;
                    Label(box.transform,"Name",worn[i].Name,12,8,142,40,20,cream,TextAnchor.MiddleCenter);
                    Label(box.transform,"Caption","已装备 · 点击查看",10,55,146,22,14,muted,TextAnchor.MiddleCenter);
                    button.onClick.AddListener(()=>ShowEquipped(id));
                }
            }
            AccessoryScroll.verticalNormalizedPosition=previousAccessoryCount>=0 && worn.Length>previousAccessoryCount?0:scroll;
            previousAccessoryCount=worn.Length;
        }
        void RefreshPicker()
        {
            float scroll=PickerScroll.verticalNormalizedPosition; Clear(PickerItems); pickerHeight=0;
            var hero=Source.CharacterHero; int rows=0;
            if(mode<0)
            {
                PickerTitle.text="装备仓库";
                Label(PickerItems,"Help","悬停肢体图标显示加号\n点击选择对应义体\n\n装载义体需要手术包\n（补给站安装除外）\n随行手术包 ×"+ConsumableSystem.Count(Source.Run,ConsumableKind.SurgeryKit)+"\n\n下方药品点击即可使用\n\n点击空饰品框打开仓库\n装载后会追加新的空框",8,8,242,356,19,muted,TextAnchor.UpperLeft);
            }
            else if(mode<4)
            {
                var part=(LimbPart)mode; PickerTitle.text=LimbSystem.PartName(part)+" / 义体";
                var limb=LimbSystem.Get(hero,part);
                if(limb!=null && limb.ProstheticFitted)
                {
                    // The fitted piece has left the warehouse stock, so show that one piece alone.
                    var equipped=LimbSystem.FindProsthetic(limb.Prosthetic);
                    if(equipped!=null)
                    {
                        string restriction=ProstheticSystem.RestrictionText(equipped);
                        EquippedRow(rows++,equipped.Name,equipped.Description+(string.IsNullOrEmpty(restriction)?"":"\n"+restriction));
                    }
                    else Empty("该部位已装载义体。");
                }
                else
                {
                    foreach(var item in Source.Run.Prosthetics.Where(p=>p!=null && p.Part==part).GroupBy(p=>p.Id).Select(g=>g.First()))
                    {
                        string id=item.Id;
                        string restriction=ProstheticSystem.RestrictionText(item);
                        string description=item.Description+(string.IsNullOrEmpty(restriction)?"":"\n"+restriction);
                        string blocked=Source.CharacterInstallBlockedReason(item);
                        PickerRow(rows++,item.Name,description,blocked??"消耗手术包装载",blocked==null,()=>Source.CharacterInstallProsthetic(id));
                    }
                    if(rows==0) Empty("没有该部位的义体库存\n\n取得对应义体后可在这里装载。\n只有断肢可安装义体。\n装载消耗一个手术包；补给站可直接安装。");
                }
            }
            else if(mode==4)
            {
                PickerTitle.text="饰品 / 队伍仓库";
                foreach(var item in Source.Run.AccessoryStock.Where(a=>a!=null).GroupBy(a=>a.Id).Select(g=>g.First()))
                {
                    string id=item.Id;
                    string blocked=!Source.CanManageCharacter?"等待我方行动":AccessorySystem.EquipBlockedReason(hero,item);
                    PickerRow(rows++,item.Name,item.Description,blocked??"装载",blocked==null,()=>Source.CharacterEquipAccessory(id));
                }
                if(rows==0) Empty("暂无可装载的饰品\n\n从宝箱或事件获得饰品后，会出现在这里。");
            }
            else
            {
                var item=hero.EquippedAccessories.FirstOrDefault(a=>a!=null && a.Id==inspectedItem);
                PickerTitle.text="已装备饰品";
                if(item!=null) PickerRow(rows++,item.Name,item.Description,"卸下并放回仓库",Source.CanManageCharacter && !hero.Dead,()=>{ Source.CharacterUnequipAccessory(inspectedItem); mode=4; Sync(); });
                else { mode=4; RefreshPicker(); return; }
            }
            PickerItems.sizeDelta=new Vector2(260,Mathf.Max(560,pickerHeight)); PickerScroll.verticalNormalizedPosition=scroll;
        }
        void Empty(string message) { Label(PickerItems,"Empty",message,8,8,242,300,19,muted,TextAnchor.UpperLeft); }
        void PickerRow(int index,string name,string description,string action,bool enabled,UnityEngine.Events.UnityAction handler)
        {
            var row=Group(PickerItems,"Stock Row "+index,0,pickerHeight,260,170);
            Label(row,"Name",name,4,4,250,30,22,cream);
            var detail=Label(row,"Description",description+(enabled?"":"\n"+action),4,40,250,66,16,muted,TextAnchor.UpperLeft);
            float height=Mathf.Max(66,detail.preferredHeight+20); detail.rectTransform.sizeDelta=new Vector2(250,height);
            var art=Art(row,"Equip",ItemSprite,4,48+height,250,50); var button=Click(art); button.interactable=enabled; button.onClick.AddListener(handler);
            Label(art.transform,"Label",enabled?action:"不可操作",10,4,230,42,15,cream,TextAnchor.MiddleCenter);
            pickerHeight+=height+116; row.sizeDelta=new Vector2(260,height+106);
        }
        void EquippedRow(int index,string name,string description)
        {
            var row=Group(PickerItems,"Equipped Row "+index,0,pickerHeight,260,170);
            Label(row,"Name",name,4,4,250,30,22,cream);
            var detail=Label(row,"Description",description,4,40,250,66,16,muted,TextAnchor.UpperLeft);
            float height=Mathf.Max(66,detail.preferredHeight+20); detail.rectTransform.sizeDelta=new Vector2(250,height);
            var plate=Art(row,"Equipped",ItemSprite,4,48+height,250,50);
            Label(plate.transform,"Label","已装备",10,4,230,42,15,cream,TextAnchor.MiddleCenter);
            pickerHeight+=height+116; row.sizeDelta=new Vector2(260,height+106);
        }

        public void BuildLayout()
        {
            var page=Group(transform,"Character Popup",0,0,1920,1080); page.anchorMin=Vector2.zero; page.anchorMax=Vector2.one; page.offsetMin=page.offsetMax=Vector2.zero; Page=page.gameObject;
            var shade=Solid(page,"Click Outside To Close",0,0,1920,1080,new Color(0,0,0,.68f));
            shade.rectTransform.anchorMin=Vector2.zero; shade.rectTransform.anchorMax=Vector2.one; shade.rectTransform.offsetMin=shade.rectTransform.offsetMax=Vector2.zero; Backdrop=Click(shade);
            var link=Group(page,"Upper Screen Edge Connection",0,0,1920,1080);
            link.anchorMin=Vector2.zero; link.anchorMax=Vector2.one; link.offsetMin=link.offsetMax=Vector2.zero;
            Connector=link.gameObject.AddComponent<CharacterLinkGraphic>(); Connector.raycastTarget=false;
            var lowerLink=Group(page,"Lower Screen Edge Connection",0,0,1920,1080);
            lowerLink.anchorMin=Vector2.zero; lowerLink.anchorMax=Vector2.one; lowerLink.offsetMin=lowerLink.offsetMax=Vector2.zero;
            LowerConnector=lowerLink.gameObject.AddComponent<CharacterLinkGraphic>(); LowerConnector.raycastTarget=false;
            WindowReveal=Group(page,"Window Reveal - 从左侧展开",320,0,1440,860);
            WindowReveal.anchorMin=WindowReveal.anchorMax=new Vector2(0,.5f); WindowReveal.pivot=new Vector2(0,.5f); WindowReveal.anchoredPosition=new Vector2(320,0);
            WindowReveal.gameObject.AddComponent<RectMask2D>(); WindowFade=WindowReveal.gameObject.AddComponent<CanvasGroup>();
            WindowContent=Group(WindowReveal,"Character Equipment Window",0,0,1440,860);
            Art(WindowContent,"Skill Panel Frame",PanelSprite,0,0,1440,860).raycastTarget=true;
            Art(WindowContent,"Full Body Art Frame",ItemSprite,40,122,264,662);
            Label(WindowContent,"Full Body Heading","角色立绘",52,42,240,38,26,cream);
            var portrait=Group(WindowContent,"Full Body Art - 完整立绘",56,146,232,550);
            FullBodyImage=Solid(portrait,"Full Body Image",0,0,232,550,Color.white); FullBodyImage.preserveAspect=true; FullBodyImage.enabled=false;
            FullBodyPlaceholder=Label(portrait,"Art Space","立绘预留",0,0,232,550,23,new Color32(94,97,86,255),TextAnchor.MiddleCenter);
            FullBodyName=Label(WindowContent,"Full Body Name","",56,719,232,37,27,cream,TextAnchor.MiddleCenter);
            var window=Group(WindowContent,"Equipment Content",280,0,1160,860);
            Heading=Label(window,"Hero Name","角色整备",52,32,800,42,30,cream);
            Vitals=Label(window,"Vitals","",54,82,740,28,20,muted);
            Close=Click(Art(window,"Close",ItemSprite,1068,32,48,48)); Label(Close.transform,"X","×",0,0,48,48,30,cream,TextAnchor.MiddleCenter);
            Label(window,"Limb Heading","肢体状态 / 义体装载",52,122,720,30,21,cream);
            LimbImages=new Image[4]; LimbLabels=new Text[4]; LimbButtons=new Button[4];
            for(int i=0;i<4;i++)
            {
                var area=Group(window,"Limb "+i,52+i*184,165,156,194);
                LimbImages[i]=Art(area,"Limb Icon",Healthy[i],0,0,156,150); LimbImages[i].type=Image.Type.Simple; LimbImages[i].preserveAspect=true; LimbButtons[i]=Click(LimbImages[i]);
                LimbLabels[i]=Label(area,"Condition","",0,154,156,46,17,muted,TextAnchor.MiddleCenter);
                var plus=Solid(area,"Install Plus",0,0,156,150,new Color(0,0,0,.55f)); Label(plus.transform,"Plus","＋",0,18,156,90,64,cream,TextAnchor.MiddleCenter);
                Label(plus.transform,"Caption","装载义体",0,107,156,30,19,cream,TextAnchor.MiddleCenter);
                plus.gameObject.SetActive(false); var hover=LimbImages[i].gameObject.AddComponent<LimbInstallHover>(); hover.Plus=plus.gameObject;
            }
            Label(window,"Potion Heading","随身药品",52,377,720,28,21,cream);
            PotionButtons=new Button[2]; PotionDetails=new Text[2];
            for(int i=0;i<2;i++)
            {
                var box=Art(window,"Potion "+i,ItemSprite,52+i*368,416,340,112); PotionButtons[i]=Click(box);
                var tint=i==0?new Color32(177,65,54,255):new Color32(205,205,205,255);
                Solid(box.transform,"Bottle Neck",35,22,20,12,tint); Solid(box.transform,"Bottle Body",26,35,38,49,tint);
                Label(box.transform,"Name",i==0?"血药":"理智药 · 锁定",86,13,238,30,22,cream);
                PotionDetails[i]=Label(box.transform,"Stock And Effect","",86,47,238,53,16,muted,TextAnchor.UpperLeft);
            }
            Label(window,"Accessories Heading","饰品 / 装载后自动增加空位",52,551,720,30,21,cream);
            AccessoryScroll=Scroll(window,"Accessory Scroll",52,596,714,188,out var accessories); Accessories=accessories;
            Art(window,"Picker Frame",ItemSprite,810,122,306,662);
            PickerTitle=Label(window,"Picker Heading","装备仓库",832,141,260,32,22,cream);
            PickerScroll=Scroll(window,"Picker Scroll",832,195,260,560,out var picker); PickerItems=picker;
            Notice=Label(window,"Notice","",52,804,1050,27,17,muted);
            Page.SetActive(false);
        }
        ScrollRect Scroll(Transform parent,string name,float x,float y,float w,float h,out RectTransform content)
        {
            var root=Group(parent,name,x,y,w,h); var scroll=root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
            var viewport=Group(root,"Viewport",0,0,w,h); viewport.gameObject.AddComponent<RectMask2D>(); var hit=viewport.gameObject.AddComponent<Image>(); hit.color=Color.clear; hit.raycastTarget=true;
            content=Group(viewport,"Content",0,0,w,h); scroll.viewport=viewport; scroll.content=content;
            var track=Solid(root,"Scrollbar",w+4,0,5,h,new Color32(41,43,39,255)); track.raycastTarget=true;
            var scrollbar=track.gameObject.AddComponent<Scrollbar>(); scrollbar.direction=Scrollbar.Direction.BottomToTop;
            var handle=Solid(track.transform,"Handle",0,0,5,h,muted); handle.raycastTarget=true;
            handle.rectTransform.anchorMin=Vector2.zero; handle.rectTransform.anchorMax=Vector2.one; handle.rectTransform.offsetMin=handle.rectTransform.offsetMax=Vector2.zero;
            scrollbar.handleRect=handle.rectTransform; scrollbar.targetGraphic=handle; scroll.verticalScrollbar=scrollbar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide; return scroll;
        }
        public static RectTransform Group(Transform parent,string name,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false); r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h); return r;
        }
        Image Solid(Transform parent,string name,float x,float y,float w,float h,Color color)
        { var a=Group(parent,name,x,y,w,h).gameObject.AddComponent<Image>(); a.color=color; a.raycastTarget=false; return a; }
        Image Art(Transform parent,string name,Sprite sprite,float x,float y,float w,float h)
        { var a=Solid(parent,name,x,y,w,h,Color.white); a.sprite=sprite; a.type=Image.Type.Sliced; a.pixelsPerUnitMultiplier=1.8f; return a; }
        Text Label(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft)
        { var t=Group(parent,name,x,y,w,h).gameObject.AddComponent<Text>(); t.font=Font; t.text=value; t.fontSize=size; t.color=color; t.alignment=align; t.raycastTarget=false; t.horizontalOverflow=HorizontalWrapMode.Wrap; t.verticalOverflow=VerticalWrapMode.Truncate; return t; }
        Button Click(Image image)
        { image.raycastTarget=true; var b=image.gameObject.AddComponent<Button>(); b.targetGraphic=image; var c=b.colors; c.highlightedColor=new Color(1.15f,1.12f,1.08f); c.disabledColor=new Color(.55f,.55f,.55f,1); b.colors=c; b.navigation=new Navigation {mode=Navigation.Mode.None}; return b; }
    }
}
