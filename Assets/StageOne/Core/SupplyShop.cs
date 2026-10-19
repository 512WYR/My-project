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
        // Initial balance, in the run-wide ore currency (Map.Supplies, displayed as 矿石).
        public const int ProstheticPrice=4, AccessoryPrice=6, HealthPrice=3, SanityPrice=3, SurgeryPrice=3;
        // Supply-station services. Installing a prosthetic always costs one ore. The first severe-limb
        // repair at each station is free and every later repair costs one ore. Healing the party and
        // calming are free on the second floor and cost two ore on later floors.
        public const int InstallPrice=1, RepairPrice=1;
        public const int StationHealPrice=2, StationCalmPrice=2, FreeStationFloor=2;

        public static List<ShopProduct> Products(LimbRules rules)
        {
            var products=new List<ShopProduct>();
            foreach(var item in LimbSystem.Resolve(rules).Prosthetics)
                products.Add(new ShopProduct {Id=item.Id,Name=item.Name,
                    Description=item.Description+Restriction(item)+" 仅断肢可安装；在补给站安装另需 "+InstallPrice+" 矿石。",
                    Category=ShopCategory.Prosthetic,Price=ProstheticPrice,Prosthetic=item});
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
        // Shared wording with LimbSystem.InstallBlockedReason, so the shop and the install button agree.
        static string Restriction(ProstheticSpec item)
        {
            var text=ProstheticSystem.RestrictionText(item);
            return string.IsNullOrEmpty(text)?"":" "+text;
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
            if(run.Map.Supplies<product.Price) { note="矿石不足。"; return false; }
            if(product.Prosthetic!=null)
            {
                run.Prosthetics.Add(LimbSystem.Clone(product.Prosthetic));
            }
            else if(product.Accessory!=null) run.AccessoryStock.Add(AccessorySystem.Clone(product.Accessory));
            else run.Consumables.Add(ConsumableSystem.Clone(product.Consumable));
            run.Map.Supplies-=product.Price;
            note="已购买「"+product.Name+"」，放入库存。";
            return true;
        }
    }
}
