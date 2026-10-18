using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    public sealed class IndustrialEventPage : MonoBehaviour
    {
        public StageOneApp Source;
        public GameObject Page;
        public Text Header, ScreenTitle, ScreenCode, ScreenLocation, Body, ReadingStatus, ChoiceHeading;
        public Image EventIllustration;
        public SupplyStationView SupplyView;
        bool IsSupply=>SupplyView && Source && Source.Room==RoomView.Supply && !Source.Map.Finished;
        public ScrollRect StoryScroll;
        public RectTransform StoryContent, ChoiceColumn;
        public EventChoiceView[] Choices=new EventChoiceView[0];
        public Button Continue;
        public Text ContinueLabel;
        public CanvasGroup ContinueFade;
        public Vector2 ContinuePosition;
        [Min(1)] public float CharactersPerSecond=38;
        public bool TextComplete { get; private set; }
        public bool ActionsReady { get; private set; }
        StageOneApp bound;
        NodeState shownNode;
        RoomView shownRoom;
        bool shownFinished;
        RoomPageView current;
        readonly RoomPageContext context = new RoomPageContext();
        string signature, fullText;
        int[] characterStarts;
        int revealed, continueChoice=-1;
        bool leave, hooked;
        Coroutine reveal;
        float measuredScale=-1, measuredWidth=-1;
        bool supplyPresented;
        NodeState supplyNode;
        RunState supplyRun;

        void OnEnable() { Canvas.willRenderCanvases+=ResizeStory; Bind(); Sync(); }
        void Start() { Bind(); Sync(); }
        void OnDisable()
        {
            Canvas.willRenderCanvases-=ResizeStory;
            if(bound) bound.Changed-=Sync; bound=null;
            Cancel();
        }
        void Bind()
        {
            if(!Page || !Continue) return;
            if(!Source) Source=FindObjectOfType<StageOneApp>();
            if(bound!=Source)
            {
                if(bound) bound.Changed-=Sync;
                bound=Source;
                if(bound) bound.Changed+=Sync;
            }
            if(hooked) return;
            hooked=true;
            foreach(var choice in Choices) Hook(choice);
            Continue.onClick.AddListener(Advance);
        }
        void Hook(EventChoiceView choice)
        {
            choice.Hit.onClick.AddListener(()=>{
                if(!ActionsReady || !choice.Available || !Live() || Source.SupplyShopOpen) return;
                ActionsReady=false; Lock();
                // The route, the buff, the supply station and the chest all speak through their own
                // page object; only the event branch falls back to the scripted event command.
                if(current!=null && current.Choose!=null) current.Choose(choice.ChoiceIndex);
                else Source.ChooseEventOption(choice.ChoiceIndex);
                Sync();
            });
        }
        bool Live()=>RoomPages.Visible(Source);
        void Advance()
        {
            if(!ActionsReady || !Live() || Source.SupplyShopOpen) return;
            ActionsReady=false; Lock();
            if(leave && current!=null && current.Advance!=null) current.Advance();
            else if(leave) Source.LeaveRoom();
            else if(continueChoice>=0) Source.ChooseEventOption(continueChoice);
            Sync();
        }
        void Lock()
        {
            if(SupplyView) SupplyView.Lock();
            foreach(var choice in Choices) if(choice && choice.Hit) choice.Hit.interactable=false;
            if(Continue) Continue.interactable=false;
        }
        public void UseSupply(bool sanity)
        {
            if(!ActionsReady || !Live() || !IsSupply || Source.SupplyShopOpen) return;
            if(sanity?Source.Map.SupplyCalmed:Source.Map.SupplyHealed) return;
            ActionsReady=false; Lock();
            if(sanity) Source.SupplyCalm(); else Source.SupplyHeal();
            Sync();
        }
        void Cancel()
        {
            if(reveal!=null) StopCoroutine(reveal); reveal=null;
            ActionsReady=false; Lock();
        }
        public void Sync()
        {
            if(!Page || !Body) return;
            if(!Live())
            {
                Cancel(); Page.SetActive(false);
                if(!IsSupply) { context.Reset(); shownNode=null; supplyPresented=false; }
                return;
            }
            var node=Source.ActiveNode;
            bool keepSupply=IsSupply && supplyPresented && supplyNode==node && supplyRun==Source.Run;

            // Every non-battle node is drawn as an event page now, so the header, location and action set
            // come from RoomPages instead of from the event script alone. The supply subpages (hero, limb)
            // are view state and reset when the room changes.
            bool roomChanged=shownRoom!=Source.Room || shownFinished!=Source.Run.Map.Finished;
            if(roomChanged) context.Reset();
            shownRoom=Source.Room; shownFinished=Source.Run.Map.Finished;

            var content=RoomPages.Build(Source,context);
            string story=(content.Body??"");
            if(!string.IsNullOrEmpty(Source.RoomNote)) story+="\n\n"+Source.RoomNote;
            if(!string.IsNullOrEmpty(content.Hint)) story+="\n\n"+content.Hint;
            var key=new StringBuilder(content.Header).Append('|').Append(content.Title).Append('|')
                .Append(content.Code).Append('|').Append(content.Location).Append('|')
                .Append(story).Append('|').Append(content.CanLeave).Append('|').Append(content.AdvanceLabel);
            foreach(var c in content.Choices) key.Append('|').Append(c.Label).Append(c.Detail).Append(c.Shown).Append(c.Enabled);
            string next=key.ToString();
            bool changed=shownNode!=node || roomChanged || signature!=next;
            bool reopening=!Page.activeSelf;
            Page.SetActive(true);
            if(!changed && !reopening && (reveal!=null || ActionsReady)) return;
            current=content;
            Cancel();
            if(SupplyView) SupplyView.Prepare(IsSupply,Source,!keepSupply);
            Header.text=content.Header??"事件终端";
            ScreenTitle.text=content.Title??"";
            ScreenCode.text=content.Code??"";
            ScreenLocation.text=content.Location??"";
            EventIllustration.enabled=EventIllustration.sprite;
            if(changed)
            {
                shownNode=node; signature=next; fullText=story;
                characterStarts=StringInfo.ParseCombiningCharacters(fullText); revealed=0;
                Body.text=fullText; Canvas.ForceUpdateCanvases();
                measuredScale=-1; ResizeStory();
                Body.text=""; StoryScroll.StopMovement(); StoryScroll.verticalNormalizedPosition=1;
            }
            PrepareActions(content,!keepSupply);
            if(keepSupply) FinishActions();
            else reveal=StartCoroutine(Reveal());
        }
        void ResizeStory()
        {
            if(!Page || !Page.activeInHierarchy || !Body || fullText==null || !StoryContent || !StoryScroll) return;
            float scale=Body.canvas.scaleFactor, width=StoryContent.rect.width;
            if(Mathf.Approximately(scale,measuredScale) && Mathf.Approximately(width,measuredWidth)) return;
            measuredScale=scale; measuredWidth=width;
            float position=StoryScroll.verticalNormalizedPosition;
            string current=Body.text; Body.text=fullText;
            float height=Mathf.Max(StoryScroll.viewport.rect.height,Body.preferredHeight+24);
            Body.text=current;
            StoryContent.sizeDelta=new Vector2(StoryContent.sizeDelta.x,height);
            StoryScroll.verticalNormalizedPosition=position;
        }
        void PrepareActions(RoomPageView content,bool animate)
        {
            // Supply recovery actions live under the mascot display; keep their original command indices
            // out of the left menu, without removing hero/limb choices on supply subpages.
            var visible=content.Choices.Select((c,i)=>new { Choice=c,Index=i })
                .Where(x=>x.Choice.Shown && !(IsSupply && context.Section==0 && x.Index<2)).ToArray();
            leave=content.CanLeave; continueChoice=-1;
            // The event scripts use a single closing choice where the other rooms use an advance button.
            if(!leave && Source.Room==RoomView.Event && visible.Length==1
                && new[]{"继续","继续前进","离开"}.Contains(visible[0].Choice.Label))
                continueChoice=visible[0].Index;
            // Rooms keep their choices while the advance button is available; an event's closing page has
            // no choices at all, so it naturally shows only the button. A single "继续" choice folds into
            // the button instead of taking a slot.
            int count=continueChoice>=0?0:visible.Length;
            while(Choices.Length<count)
            {
                var clone=Instantiate(Choices[0],ChoiceColumn); clone.name="Choice "+(Choices.Length+1);
                clone.Hit.onClick.RemoveAllListeners(); Hook(clone);
                var list=Choices.ToList(); list.Add(clone); Choices=list.ToArray();
            }
            float height=Mathf.Min(132,(680-Mathf.Max(0,count-1)*18)/Mathf.Max(1,count));
            for(int i=0;i<Choices.Length;i++)
            {
                var item=Choices[i]; item.gameObject.SetActive(i<count);
                if(i>=count) continue;
                item.ChoiceIndex=visible[i].Index; item.Available=visible[i].Choice.Enabled;
                item.Number.text=(i+1).ToString("D2"); item.Title.text=visible[i].Choice.Label;
                item.Detail.text=visible[i].Choice.Detail??"";
                var rect=(RectTransform)item.transform;
                rect.sizeDelta=new Vector2(680,height); rect.anchoredPosition=new Vector2(animate?SlideDistance():0,-i*(height+18));
                item.Fade.alpha=animate?0:(item.Available?1:.42f); item.Fade.blocksRaycasts=false; item.Hit.interactable=false;
            }
            ChoiceHeading.text=count>0?"行动选择":"";
            ChoiceHeading.gameObject.SetActive(!animate && count>0);
            Continue.gameObject.SetActive(!animate && (leave || continueChoice>=0)); ContinueFade.alpha=animate?0:1; ContinueFade.blocksRaycasts=false;
            ContinueLabel.text=leave?(content.AdvanceLabel??"离开  →"):"继续  →";
            Continue.interactable=false;
        }
        float SlideDistance()
        {
            var canvas=GetComponent<Canvas>();
            return Mathf.Max(2100,canvas.pixelRect.width/Mathf.Max(.01f,canvas.scaleFactor)+800);
        }
        IEnumerator Reveal()
        {
            // The supply station has no narrative box to wait for.
            if(IsSupply) revealed=characterStarts.Length;
            TextComplete=false; float progress=revealed;
            ReadingStatus.text="正在读取事件记录…";
            while(revealed<characterStarts.Length)
            {
                progress+=Time.unscaledDeltaTime*Mathf.Max(1,CharactersPerSecond);
                revealed=Mathf.Min(characterStarts.Length,(int)progress);
                int end=revealed==characterStarts.Length?fullText.Length:characterStarts[revealed];
                Body.text=fullText.Substring(0,end);
                yield return null;
            }
            TextComplete=true; Body.text=fullText;
            ReadingStatus.text=StoryContent.rect.height>StoryScroll.viewport.rect.height+2?"滚动阅读事件记录":"记录读取完毕";
            var shown=Choices.Where(c=>c.gameObject.activeSelf).ToArray();
            ChoiceHeading.gameObject.SetActive(shown.Length>0);
            bool showContinue=leave || continueChoice>=0;
            Continue.gameObject.SetActive(showContinue);
            float started=Time.unscaledTime;
            float duration=.32f+Mathf.Max(0,shown.Length-1)*.07f;
            while(Time.unscaledTime-started<duration)
            {
                float elapsed=Time.unscaledTime-started;
                for(int i=0;i<shown.Length;i++) AnimateChoice(shown[i],Mathf.Clamp01((elapsed-i*.07f)/.32f));
                if(showContinue) AnimateContinue(Mathf.Clamp01(elapsed/.32f));
                if(IsSupply) SupplyView.SetProgress(1-Mathf.Pow(1-Mathf.Clamp01(elapsed/.32f),3));
                yield return null;
            }
            FinishActions();
        }
        void FinishActions()
        {
            TextComplete=true;
            ChoiceHeading.gameObject.SetActive(Choices.Any(c=>c.gameObject.activeSelf));
            bool showContinue=leave || continueChoice>=0;
            Continue.gameObject.SetActive(showContinue);
            foreach(var choice in Choices.Where(c=>c.gameObject.activeSelf))
            { AnimateChoice(choice,1); choice.Fade.blocksRaycasts=choice.Available; choice.Hit.interactable=choice.Available; }
            if(showContinue)
            {
                AnimateContinue(1); ContinueFade.blocksRaycasts=true;
                Continue.interactable=leave || (continueChoice>=0 && continueChoice<current.Choices.Count
                    && current.Choices[continueChoice].Enabled);
            }
            ActionsReady=true; reveal=null;
            if(IsSupply)
            {
                supplyPresented=true; supplyNode=Source.ActiveNode; supplyRun=Source.Run;
                SupplyView.SetProgress(1);
            }
        }
        void AnimateChoice(EventChoiceView choice,float progress)
        {
            float eased=1-Mathf.Pow(1-progress,3);
            var rect=(RectTransform)choice.transform;
            rect.anchoredPosition=new Vector2((1-eased)*SlideDistance(),rect.anchoredPosition.y);
            choice.Fade.alpha=eased*(choice.Available?1:.42f);
        }
        void AnimateContinue(float progress)
        {
            float eased=1-Mathf.Pow(1-progress,3);
            ((RectTransform)Continue.transform).anchoredPosition=ContinuePosition+new Vector2((1-eased)*SlideDistance(),0);
            ContinueFade.alpha=eased;
        }
    }
}
