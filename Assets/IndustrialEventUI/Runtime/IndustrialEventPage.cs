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
        bool IsSupply=>Source && Source.Room==RoomView.Supply && !Source.Map.Finished;
        public ScrollRect StoryScroll, ChoiceScroll;
        public RectTransform StoryContent, ChoiceColumn, ChoiceViewport, Monitor;
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
        string signature, fullText, rewardText;
        int[] characterStarts;
        // Gold used by the stage for emphasis. Names and amounts both wear it so a glance finds every
        // resource without reading the sentence again.
        const string GainColor = "#D6B677";
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
            // The reward block is rich text even though the builder leaves the body plain. Setting it
            // here keeps existing prefabs working without a rebuild.
            if(Body) Body.supportRichText=true;
            if(!Source) Source=FindObjectOfType<StageOneApp>();
            if(bound!=Source)
            {
                if(bound) bound.Changed-=Sync;
                bound=Source;
                if(bound) bound.Changed+=Sync;
            }
            if(hooked) return;
            hooked=true;
            EnsureChoiceScroll();
            foreach(var choice in Choices) Hook(choice);
            Continue.onClick.AddListener(Advance);
        }
        // Older prefabs keep the choice column directly under the page and have no viewport. Rebuilding
        // the prefab would overwrite hand-configured art, so add the scroll hierarchy in place instead.
        public void EnsureChoiceScroll()
        {
            if(ChoiceScroll || !Page || !ChoiceColumn) return;
            var page=Page.transform;
            var scrollRect=(RectTransform)new GameObject("Choice Scroll",typeof(RectTransform)).transform;
            scrollRect.SetParent(page,false);
            scrollRect.anchorMin=scrollRect.anchorMax=scrollRect.pivot=new Vector2(0,1);
            scrollRect.anchoredPosition=ChoiceColumn.anchoredPosition;
            scrollRect.sizeDelta=new Vector2(680,680);
            var scroll=scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;

            var viewport=(RectTransform)new GameObject("Choice Viewport",typeof(RectTransform)).transform;
            viewport.SetParent(scrollRect,false);
            viewport.anchorMin=viewport.anchorMax=viewport.pivot=new Vector2(0,1);
            viewport.anchoredPosition=Vector2.zero; viewport.sizeDelta=new Vector2(680,680);
            var hit=viewport.gameObject.AddComponent<Image>(); hit.color=Color.clear; hit.raycastTarget=true;
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport=viewport;

            ChoiceColumn.SetParent(viewport,false);
            ChoiceColumn.anchorMin=ChoiceColumn.anchorMax=ChoiceColumn.pivot=new Vector2(0,1);
            ChoiceColumn.anchoredPosition=Vector2.zero;
            ChoiceColumn.sizeDelta=new Vector2(680,680);
            scroll.content=ChoiceColumn;

            var track=(RectTransform)new GameObject("Choice Scrollbar",typeof(RectTransform)).transform;
            track.SetParent(page,false);
            track.anchorMin=track.anchorMax=track.pivot=new Vector2(0,1);
            track.anchoredPosition=scrollRect.anchoredPosition+new Vector2(692,0);
            track.sizeDelta=new Vector2(8,680);
            var trackImage=track.gameObject.AddComponent<Image>();
            trackImage.color=new Color32(42,49,43,255); trackImage.raycastTarget=true;
            var bar=track.gameObject.AddComponent<Scrollbar>();
            bar.direction=Scrollbar.Direction.BottomToTop;
            var slide=(RectTransform)new GameObject("Sliding Area",typeof(RectTransform)).transform;
            slide.SetParent(track,false);
            slide.anchorMin=slide.anchorMax=slide.pivot=new Vector2(0,1);
            slide.anchoredPosition=Vector2.zero; slide.sizeDelta=new Vector2(8,680);
            var handle=(RectTransform)new GameObject("Handle",typeof(RectTransform)).transform;
            handle.SetParent(slide,false);
            var handleImage=handle.gameObject.AddComponent<Image>();
            handleImage.color=new Color32(148,165,143,255); handleImage.raycastTarget=true;
            handle.anchorMin=Vector2.zero; handle.anchorMax=Vector2.one;
            handle.offsetMin=handle.offsetMax=Vector2.zero;
            bar.handleRect=handle; bar.targetGraphic=handleImage; bar.size=.3f;
            scroll.verticalScrollbar=bar;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;

            ChoiceScroll=scroll; ChoiceViewport=viewport;
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
            // The resource report is its own block rather than a sentence inside the story, so the
            // typewriter reveals the narrative while the report is already legible.
            string rewards=RewardBlock(content.Rewards);
            var key=new StringBuilder(content.Header).Append('|').Append(content.Title).Append('|')
                .Append(content.Code).Append('|').Append(content.Location).Append('|')
                .Append(rewards).Append('|')
                .Append(story).Append('|').Append(content.CanLeave).Append('|').Append(content.AdvanceLabel);
            foreach(var c in content.Choices) key.Append('|').Append(c.Label).Append(c.Detail).Append(c.Shown).Append(c.Enabled);
            string next=key.ToString();
            bool changed=shownNode!=node || roomChanged || signature!=next;
            bool reopening=!Page.activeSelf;
            Page.SetActive(true);
            if(!changed && !reopening && (reveal!=null || ActionsReady)) return;
            current=content;
            Cancel();
            Header.text=content.Header??"事件终端";
            ScreenTitle.text=content.Title??"";
            ScreenCode.text=content.Code??"";
            ScreenLocation.text=content.Location??"";
            EventIllustration.enabled=EventIllustration.sprite;
            if(changed)
            {
                shownNode=node; signature=next; fullText=story; rewardText=rewards;
                characterStarts=StringInfo.ParseCombiningCharacters(fullText);
                // Settlement is a result screen, so it appears in full instead of typing itself out;
                // Reveal still runs for the 0.32s action fade.
                revealed=content.Instant?characterStarts.Length:0;
                Body.text=WithRewards(fullText); Canvas.ForceUpdateCanvases();
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
            string current=Body.text; Body.text=WithRewards(fullText);
            float height=Mathf.Max(StoryScroll.viewport.rect.height,Body.preferredHeight+24);
            Body.text=current;
            StoryContent.sizeDelta=new Vector2(StoryContent.sizeDelta.x,height);
            StoryScroll.verticalNormalizedPosition=position;
        }
        void PrepareActions(RoomPageView content,bool animate)
        {
            // Every shown choice is a standard right-column option now, including the supply station's
            // recovery commands. The dedicated supply layout used to filter the first two indices out.
            var visible=content.Choices.Select((c,i)=>new { Choice=c,Index=i })
                .Where(x=>x.Choice.Shown).ToArray();
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
            for(int i=0;i<Choices.Length;i++)
            {
                var item=Choices[i]; bool shown=i<count;
                item.gameObject.SetActive(shown);
                if(!shown) continue;
                item.ChoiceIndex=visible[i].Index; item.Available=visible[i].Choice.Enabled;
                item.Number.text=(i+1).ToString("D2"); item.Title.text=visible[i].Choice.Label;
                item.Detail.text=visible[i].Choice.Detail??"";
            }
            // Preferred heights are only meaningful after this page's texts have been assigned.
            Canvas.ForceUpdateCanvases();
            float viewportHeight=ChoiceViewport && ChoiceViewport.rect.height>1?ChoiceViewport.rect.height:680f;
            float slide=ChoiceSlideDistance(), y=0;
            for(int i=0;i<count;i++)
            {
                var item=Choices[i];
                float titleHeight=Mathf.Max(38,item.Title.preferredHeight);
                float detailTop=24+titleHeight+5;
                float detailHeight=Mathf.Max(42,item.Detail.preferredHeight);
                float cardHeight=Mathf.Max(132,detailTop+detailHeight+23);
                var titleRect=item.Title.rectTransform;
                titleRect.sizeDelta=new Vector2(titleRect.sizeDelta.x,titleHeight);
                titleRect.anchoredPosition=new Vector2(titleRect.anchoredPosition.x,-24);
                var detailRect=item.Detail.rectTransform;
                detailRect.sizeDelta=new Vector2(detailRect.sizeDelta.x,detailHeight);
                detailRect.anchoredPosition=new Vector2(detailRect.anchoredPosition.x,-detailTop);
                var rect=(RectTransform)item.transform;
                rect.sizeDelta=new Vector2(680,cardHeight); rect.anchoredPosition=new Vector2(animate?slide:0,-y);
                item.Fade.alpha=animate?0:(item.Available?1:.42f); item.Fade.blocksRaycasts=false; item.Hit.interactable=false;
                y+=cardHeight+18;
            }
            if(ChoiceColumn)
            {
                ChoiceColumn.sizeDelta=new Vector2(ChoiceColumn.sizeDelta.x,Mathf.Max(viewportHeight,y-18));
                // Content and viewport both pivot at the top-left, so y=0 is the top of the list. This
                // is independent of ScrollRect bounds, which may still be cached from before the resize.
                ChoiceColumn.anchoredPosition=new Vector2(ChoiceColumn.anchoredPosition.x,0);
            }
            if(ChoiceScroll) ChoiceScroll.StopMovement();
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
        // Cards live inside a RectMask2D viewport, so they must start beyond the viewport itself rather
        // than beyond the whole screen. 720 also clears the 680-wide card.
        float ChoiceSlideDistance()
        {
            return (ChoiceViewport?ChoiceViewport.rect.width:680f)+720f;
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
                Body.text=WithRewards(fullText.Substring(0,end));
                yield return null;
            }
            TextComplete=true; Body.text=WithRewards(fullText);
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
                yield return null;
            }
            FinishActions();
        }
        void FinishActions()
        {
            TextComplete=true;
            // Supply commands refresh in place without running Reveal, which normally restores the body
            // after Sync clears it for the typewriter. Put the new result back before unlocking.
            if(Body && fullText!=null) Body.text=WithRewards(fullText);
            if(ReadingStatus && StoryContent && StoryScroll && Body)
                ReadingStatus.text=StoryContent.rect.height>StoryScroll.viewport.rect.height+2?"滚动阅读事件记录":"记录读取完毕";
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
            }
        }
        // The report sits above the narrative, separated by a blank line. It is deliberately outside
        // fullText: the typewriter counts only the story's characters, so a rich-text tag can never be
        // split in half and shown as literal markup.
        string WithRewards(string story)
        {
            return string.IsNullOrEmpty(rewardText)?story:rewardText+"\n\n"+story;
        }
        static string RewardBlock(IReadOnlyList<ResourceGain> gains)
        {
            if(gains==null || gains.Count==0) return "";
            var text=new StringBuilder();
            text.Append("<color=#8A9A8C>本节点获得</color>");
            foreach(var gain in gains)
            {
                if(gain==null) continue;
                text.Append("\n获得 <color=").Append(GainColor).Append('>').Append(gain.Name)
                    .Append("</color> ×<color=").Append(GainColor).Append('>').Append(gain.Amount).Append("</color>");
            }
            return text.ToString();
        }
        void AnimateChoice(EventChoiceView choice,float progress)
        {
            float eased=1-Mathf.Pow(1-progress,3);
            var rect=(RectTransform)choice.transform;
            rect.anchoredPosition=new Vector2((1-eased)*ChoiceSlideDistance(),rect.anchoredPosition.y);
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
