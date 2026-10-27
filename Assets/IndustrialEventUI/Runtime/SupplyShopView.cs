using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    public sealed class SupplyShopView : MonoBehaviour
    {
        public IndustrialEventPage Owner;
        public GameObject Page;
        public RectTransform Window, Content;
        public CanvasGroup Fade;
        public Button Close, Backdrop;
        public Button[] Tabs;
        public Text Balance, Notice, ProductName, ProductDescription, ProductCategory, ProductOwned;
        public Image Mascot;
        public ScrollRect Scroll;
        public SupplyShopCard CardTemplate;
        public List<SupplyShopCard> Cards {get;}=new List<SupplyShopCard>();
        public bool EntranceComplete {get; private set;}
        public int EntranceCount {get; private set;}
        static readonly string[] CategoryNames={"义体","饰品","血药","理智药","手术包"};
        List<ShopProduct> products;
        StageOneApp bound;
        bool wasOpen, hooked;
        float enteredAt;
        ShopCategory category;
        string selected;

        void OnEnable() { Bind(); Sync(); }
        void Start() { Bind(); Sync(); }
        void OnDisable() { if(bound) bound.Changed-=Sync; bound=null; wasOpen=false; }
        void Bind()
        {
            if(!Owner || !Owner.Source) return;
            if(bound!=Owner.Source)
            {
                if(bound) bound.Changed-=Sync;
                bound=Owner.Source; bound.Changed+=Sync;
            }
            if(hooked) return;
            hooked=true;
            Close.onClick.AddListener(()=>bound.CloseSupplyShop());
            Backdrop.onClick.AddListener(()=>bound.CloseSupplyShop());
            for(int i=0;i<Tabs.Length;i++)
            {
                int index=i;
                Tabs[i].onClick.AddListener(()=>{if(EntranceComplete) ShowCategory((ShopCategory)index);});
            }
        }
        void Update()
        {
            if(!wasOpen || EntranceComplete) return;
            float t=Mathf.Clamp01((Time.unscaledTime-enteredAt)/.24f);
            float ease=1-Mathf.Pow(1-t,3);
            Window.anchoredPosition=new Vector2(0,-24*(1-ease)); Fade.alpha=ease;
            if(t>=1) { EntranceComplete=true; Fade.interactable=true; }
        }
        void Sync()
        {
            if(!bound || !Page) return;
            bool open=bound.SupplyShopOpen && bound.SupplyShopAvailable;
            Page.SetActive(open);
            if(!open) { wasOpen=false; return; }
            if(!wasOpen)
            {
                wasOpen=true; enteredAt=Time.unscaledTime; EntranceComplete=false; EntranceCount++;
                Fade.alpha=0; Fade.interactable=false;
                products=SupplyShop.Products(bound.Catalog.Rules().Limbs);
                ShowCategory(ShopCategory.Prosthetic);
            }
            RefreshStock();
        }
        void ShowCategory(ShopCategory next)
        {
            category=next;
            foreach(var card in Cards) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
            Cards.Clear();
            var shown=products.Where(p=>p.Category==category).ToArray();
            selected=shown.FirstOrDefault()?.Id;
            for(int i=0;i<shown.Length;i++)
            {
                var product=shown[i];
                var card=Instantiate(CardTemplate,Content); card.name="Product - "+product.Id; card.gameObject.SetActive(true);
                card.ProductId=product.Id; card.Title.text=product.Name; card.Description.text=product.Description;
                card.Price.text=product.Price+" 矿石";
                ((RectTransform)card.transform).anchoredPosition=new Vector2((i%2)*488,-(i/2)*238);
                card.Select.onClick.AddListener(()=>{selected=product.Id; RefreshStock();});
                card.Buy.onClick.AddListener(()=>
                {
                    if(!EntranceComplete) return;
                    selected=product.Id; bound.BuySupplyShopItem(product.Id);
                });
                Cards.Add(card);
            }
            Content.sizeDelta=new Vector2(956,Mathf.Max(Scroll.viewport.rect.height,Mathf.Ceil(shown.Length/2f)*238-16));
            Scroll.StopMovement(); Scroll.verticalNormalizedPosition=1;
            for(int i=0;i<Tabs.Length;i++) Tabs[i].targetGraphic.color=i==(int)category?new Color(1,.89f,.70f):new Color(.65f,.67f,.63f);
            RefreshStock();
        }
        void RefreshStock()
        {
            Balance.text="持有矿石  "+bound.Map.Supplies;
            Notice.text=bound.SupplyShopNote;
            foreach(var card in Cards)
            {
                var product=products.First(p=>p.Id==card.ProductId);
                bool afford=bound.Map.Supplies>=product.Price;
                card.Owned.text="库存 "+SupplyShop.Owned(bound.Run,product);
                card.Buy.interactable=afford;
                card.BuyLabel.text=afford?"购买":"矿石不足";
                card.Frame.color=selected==product.Id?Color.white:new Color(.75f,.77f,.72f);
            }
            var chosen=products.FirstOrDefault(p=>p.Id==selected);
            ProductCategory.text=CategoryNames[(int)category]+"  /  商品资料";
            ProductName.text=chosen?.Name??"选择商品";
            ProductDescription.text=chosen?.Description??"";
            ProductOwned.text=chosen==null?"":"库存 "+SupplyShop.Owned(bound.Run,chosen)+"    /    单价 "+chosen.Price+" 矿石";
            // The supply station no longer has a mascot display; keep the reserved slot blank.
            if(Mascot) Mascot.enabled=false;
        }
    }
}
