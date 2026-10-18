using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    public enum ShopCategory { Prosthetic, Accessory, Health, Sanity, Surgery }

    public sealed class ShopProduct
    {
        public string Id, Name, Description;
        public ShopCategory Category;
        public int Price;
        public ProstheticSpec Prosthetic;
        public AccessorySpec Accessory;
        public ConsumableSpec Consumable;
    }

    public static class SupplyShop
    {
        // Initial balance, in the existing run-wide supply currency.
        public const int ProstheticPrice=4, AccessoryPrice=6, HealthPrice=2, SanityPrice=3, SurgeryPrice=3;

        public static List<ShopProduct> Products(LimbRules rules)
        {
            var products=new List<ShopProduct>();
            foreach(var item in LimbSystem.Resolve(rules).Prosthetics)
                products.Add(new ShopProduct {Id=item.Id,Name=item.Name,Description=item.Description+" 仅断肢可安装。",Category=ShopCategory.Prosthetic,Price=ProstheticPrice,Prosthetic=item});
            foreach(var item in AccessorySystem.Defaults)
                products.Add(new ShopProduct {Id=item.Id,Name=item.Name,Description=item.Description+Restriction(item),Category=ShopCategory.Accessory,Price=AccessoryPrice,Accessory=item});
            foreach(var item in ConsumableSystem.Defaults)
            {
                var category=item.Kind==ConsumableKind.HpRestore?ShopCategory.Health:item.Kind==ConsumableKind.SanityLock?ShopCategory.Sanity:ShopCategory.Surgery;
                products.Add(new ShopProduct {Id=item.Id,Name=item.Name,Description=item.Description,Category=category,
                    Price=category==ShopCategory.Health?HealthPrice:category==ShopCategory.Sanity?SanityPrice:SurgeryPrice,Consumable=item});
            }
            return products;
        }
        static string Restriction(AccessorySpec item)
        {
            switch(item.RestrictSilhouette)
            {
                case "gunner": return " 仅枪手可装备。";
                case "medic": return " 仅医师可装备。";
                case "warrior": return " 仅战士可装备。";
                case "shield": return " 仅盾卫可装备。";
                default: return "";
            }
        }
        public static int Owned(RunState run, ShopProduct product)
        {
            if(product.Prosthetic!=null) return run.Prosthetics.Count(p=>p.Id==product.Id);
            if(product.Accessory!=null) return run.AccessoryStock.Count(p=>p.Id==product.Id);
            return run.Consumables.Count(p=>p.Id==product.Id);
        }
        public static bool Buy(RunState run,LimbRules rules,string id,out string note)
        {
            note="当前无法购买。";
            if(run==null || run.Map==null || run.Map.Finished) return false;
            var product=Products(rules).FirstOrDefault(p=>p.Id==id);
            if(product==null) { note="商品不存在。"; return false; }
            if(run.Map.Supplies<product.Price) { note="补给不足。"; return false; }
            if(product.Prosthetic!=null)
            {
                var p=product.Prosthetic;
                run.Prosthetics.Add(new ProstheticSpec(p.Id,p.Name,p.Part,p.Description));
            }
            else if(product.Accessory!=null) run.AccessoryStock.Add(AccessorySystem.Clone(product.Accessory));
            else run.Consumables.Add(ConsumableSystem.Clone(product.Consumable));
            run.Map.Supplies-=product.Price;
            note="已购买「"+product.Name+"」，放入库存。";
            return true;
        }
    }
}
