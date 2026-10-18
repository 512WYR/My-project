using Ruin.Core;

namespace Ruin.Runtime
{
    public sealed partial class StageOneApp
    {
        public bool SupplyShopOpen { get; private set; }
        public string SupplyShopNote { get; private set; }
        public bool SupplyShopAvailable=>Run!=null && !Run.Map.Finished && AwaitingNode && Room==RoomView.Supply
            && !Busy && !HelpOpen && !LabOpen && !CharacterPanelOpen && !AccessoryEditorOpen && !ConsumableEditorOpen;

        public void OpenSupplyShop()
        {
            if(!SupplyShopAvailable || SupplyShopOpen) return;
            SupplyShopOpen=true; SupplyShopNote="选择商品查看详情，使用补给购买。"; ClearPending(); Refresh();
        }
        public void CloseSupplyShop()
        {
            SupplyShopOpen=false; Refresh();
        }
        public bool BuySupplyShopItem(string id)
        {
            if(!SupplyShopOpen || !SupplyShopAvailable) return false;
            bool bought=SupplyShop.Buy(Run,Catalog.Rules().Limbs,id,out string note);
            SupplyShopNote=note; Refresh(); return bought;
        }
    }
}
