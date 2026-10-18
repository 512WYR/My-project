using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    public sealed class SupplyShopCard : MonoBehaviour
    {
        public Button Select, Buy;
        public Image Frame;
        public Text Title, Description, Price, Owned, BuyLabel;
        public string ProductId;
    }
}
