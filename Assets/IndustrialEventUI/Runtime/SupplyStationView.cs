using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    // Deprecated compatibility layer. New prefabs use the event template and do not add this
    // component; the guards keep older scenes from throwing while they are rebuilt.
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
            if(Heal) Heal.onClick.AddListener(()=>{if(Owner) Owner.UseSupply(false);});
            if(Calm) Calm.onClick.AddListener(()=>{if(Owner) Owner.UseSupply(true);});
        }

        public void Prepare(bool supply, StageOneApp app,bool animate=true)
        {
            if(!Owner) return;
            if(Layout) Layout.SetActive(supply);
            if(OriginalMonitor) OriginalMonitor.SetActive(!supply);
            if(OriginalStory) OriginalStory.SetActive(!supply);
            if(Owner.ChoiceColumn) Owner.ChoiceColumn.anchoredPosition=supply?new Vector2(66,-164):DefaultChoicePosition;
            if(Owner.ChoiceHeading) Owner.ChoiceHeading.rectTransform.anchoredPosition=supply?new Vector2(66,-102):DefaultHeadingPosition;
            if(!supply) return;
            if(Mascot) Mascot.enabled=Mascot.sprite;
            if(MascotPlaceholder) MascotPlaceholder.gameObject.SetActive(Mascot && !Mascot.sprite);
            if(Location) Location.text="第 "+app.Map.Floor.ToString("D2")+" 层  /  补给站";
            if(Status) Status.text="生命补给  ·  "+(app.Map.SupplyHealed?"已使用":"可用")
                +"     /     理智补给  ·  "+(app.Map.SupplyCalmed?"已使用":"可用");
            if(HealDetail) HealDetail.text=app.Map.SupplyHealed?"本次补给已使用":"全队恢复 "+(app.Catalog.MapRules().SupplyHealRatio*100).ToString("0")+"% 生命 · 每站一次";
            if(CalmDetail) CalmDetail.text=app.Map.SupplyCalmed?"本次补给已使用":"玩家理智 +"+Exploration.CalmSanity.ToString("0.#")+" · 每站一次";
            SetProgress(animate?0:1);
        }

        public void Lock()
        {
            if(Actions) Actions.blocksRaycasts=false;
            if(Heal) Heal.interactable=false;
            if(Calm) Calm.interactable=false;
        }

        public void SetProgress(float progress)
        {
            if(Actions)
            {
                Actions.alpha=progress;
                var rect=(RectTransform)Actions.transform;
                rect.anchoredPosition=new Vector2(796,-804-12*(1-progress));
                Actions.blocksRaycasts=progress>=1;
            }
            bool ready=progress>=1 && Owner && Owner.Source;
            if(Heal) Heal.interactable=ready && !Owner.Source.Map.SupplyHealed;
            if(Calm) Calm.interactable=ready && !Owner.Source.Map.SupplyCalmed;
        }
    }
}
