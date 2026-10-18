using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    public sealed class SupplyStationView : MonoBehaviour
    {
        public IndustrialEventPage Owner;
        public GameObject Layout, OriginalMonitor, OriginalStory;
        public RectTransform Monitor;
        public Image Mascot;
        public Text MascotPlaceholder, Location, Status, HealDetail, CalmDetail;
        public Button Heal, Calm;
        public CanvasGroup Actions;
        public Vector2 DefaultChoicePosition, DefaultHeadingPosition;

        void Awake()
        {
            Heal.onClick.AddListener(()=>Owner.UseSupply(false));
            Calm.onClick.AddListener(()=>Owner.UseSupply(true));
        }

        public void Prepare(bool supply, StageOneApp app,bool animate=true)
        {
            Layout.SetActive(supply);
            OriginalMonitor.SetActive(!supply);
            OriginalStory.SetActive(!supply);
            Owner.ChoiceColumn.anchoredPosition=supply?new Vector2(66,-164):DefaultChoicePosition;
            Owner.ChoiceHeading.rectTransform.anchoredPosition=supply?new Vector2(66,-102):DefaultHeadingPosition;
            if(!supply) return;
            Mascot.enabled=Mascot.sprite;
            MascotPlaceholder.gameObject.SetActive(!Mascot.sprite);
            Location.text="第 "+app.Map.Floor.ToString("D2")+" 层  /  补给站";
            Status.text="生命补给  ·  "+(app.Map.SupplyHealed?"已使用":"可用")
                +"     /     理智补给  ·  "+(app.Map.SupplyCalmed?"已使用":"可用");
            HealDetail.text=app.Map.SupplyHealed?"本次补给已使用":"全队恢复 "+(app.Catalog.MapRules().SupplyHealRatio*100).ToString("0")+"% 生命 · 每站一次";
            CalmDetail.text=app.Map.SupplyCalmed?"本次补给已使用":"玩家理智 +"+Exploration.CalmSanity.ToString("0.#")+" · 每站一次";
            SetProgress(animate?0:1);
        }

        public void Lock()
        {
            Actions.blocksRaycasts=false;
            Heal.interactable=Calm.interactable=false;
        }

        public void SetProgress(float progress)
        {
            Actions.alpha=progress;
            var rect=(RectTransform)Actions.transform;
            rect.anchoredPosition=new Vector2(796,-804-12*(1-progress));
            bool ready=progress>=1;
            Actions.blocksRaycasts=ready;
            Heal.interactable=ready && !Owner.Source.Map.SupplyHealed;
            Calm.interactable=ready && !Owner.Source.Map.SupplyCalmed;
        }
    }
}
