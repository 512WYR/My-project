using UnityEngine;

namespace GameMake.ExplorationUI
{
    public sealed class MapPageEntrance : MonoBehaviour
    {
        public RectTransform MapPanel, CounterPanel;
        public CanvasGroup MapFade, CounterFade;
        public Vector2 MapHome, CounterHome;
        public bool IsRunning { get; private set; }
        public int EntranceCount { get; private set; }
        float started;
        public void Enter()
        {
            if(!MapPanel || !CounterPanel) return;
            EntranceCount++; started=Time.unscaledTime; IsRunning=true;
            MapFade.interactable=false; MapFade.blocksRaycasts=false; Paint();
        }
        void LateUpdate() { if(IsRunning) Paint(); }
        void OnDisable() { Finish(); }
        void Paint()
        {
            float elapsed=Time.unscaledTime-started;
            float counter=Mathf.Clamp01(elapsed/.44f);
            float map=Mathf.Clamp01((elapsed-.06f)/.56f);
            // Ease-out quint keeps the long travel quick, with a soft final approach and no overshoot.
            float mapOffset=Mathf.Pow(1-map,5), counterOffset=Mathf.Pow(1-counter,4);
            MapPanel.anchoredPosition=MapHome+Vector2.right*(MapPanel.rect.width+64)*mapOffset;
            CounterPanel.anchoredPosition=CounterHome+Vector2.up*(CounterPanel.rect.height+Mathf.Abs(CounterHome.y)+24)*counterOffset;
            MapFade.alpha=Mathf.Clamp01(map*5); CounterFade.alpha=Mathf.Clamp01(counter*5);
            if(map>=1 && counter>=1) Finish();
        }
        public void Finish()
        {
            IsRunning=false;
            if(MapPanel) MapPanel.anchoredPosition=MapHome;
            if(CounterPanel) CounterPanel.anchoredPosition=CounterHome;
            if(MapFade) { MapFade.alpha=1; MapFade.interactable=true; MapFade.blocksRaycasts=true; }
            if(CounterFade) CounterFade.alpha=1;
        }
    }
}
