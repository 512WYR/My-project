using Ruin.Core;

namespace Ruin.Runtime
{
    public sealed partial class StageOneApp
    {
        public bool SupplyShopOpen { get; private set; }
        public string SupplyShopNote { get; private set; }
        public bool SupplyShopAvailable=>Run!=null && !Run.Map.Finished && AwaitingNode && Room==RoomView.Supply
            && !Busy && !DebugOverlayOpen && !CharacterPanelOpen;

        public void OpenSupplyShop()
        {
            if(!SupplyShopAvailable || SupplyShopOpen) return;
            SupplyShopOpen=true;
            SupplyShopNote="选择商品查看详情。当前矿石 " + Run.Map.Supplies + "。";
            ClearPending(); Refresh();
        }
        public void CloseSupplyShop()
        {
            SupplyShopOpen=false; Refresh();
        }
        public bool BuySupplyShopItem(string id)
        {
            if(!SupplyShopOpen || !SupplyShopAvailable) return false;
            bool bought=SupplyShop.Buy(Run,Catalog.Rules().Limbs,id,out string note);
            // Keep the result in the page body after the modal closes; the shop notice is transient.
            SupplyShopNote=note; RoomNote=note; Hint=note; Refresh(); return bought;
        }
    }
}
