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
        string signature, fullText;
        int[] characterStarts;
        int revealed, continueChoice=-1;
        bool leave, hooked;
        Coroutine reveal;
        float measuredScale=-1, measuredWidth=-1;

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
                if(!ActionsReady || !choice.Available || !Live()) return;
                ActionsReady=false; Lock(); Source.ChooseEventOption(choice.ChoiceIndex);
            });
        }
        bool Live()=>Source && Source.Run!=null && Source.AwaitingNode && Source.Room==RoomView.Event
            && !Source.HelpOpen && !Source.LabOpen;
        void Advance()
        {
            if(!ActionsReady || !Live()) return;
            ActionsReady=false; Lock();
            if(leave) Source.LeaveRoom(); else if(continueChoice>=0) Source.ChooseEventOption(continueChoice);
        }
        void Lock()
        {
            foreach(var choice in Choices) if(choice && choice.Hit) choice.Hit.interactable=false;
            if(Continue) Continue.interactable=false;
        }
        void Cancel()
        {
            if(reveal!=null) StopCoroutine(reveal); reveal=null;
            ActionsReady=false; Lock();
        }
        public void Sync()
        {
            if(!Page || !Body) return;
            if(!Live()) { Cancel(); Page.SetActive(false); return; }
            var node=Source.ActiveNode;
            var content=EventScripts.Page(node,Source.Run,Source.Catalog.MapRules());
            string story=(content.Body??"");
            if(!string.IsNullOrEmpty(Source.RoomNote)) story+="\n\n"+Source.RoomNote;
            if(!string.IsNullOrEmpty(content.Hint)) story+="\n\n"+content.Hint;
            var key=new StringBuilder(content.Title).Append('|').Append(story).Append('|').Append(content.CanLeave);
            foreach(var c in content.Choices) key.Append('|').Append(c.Label).Append(c.Detail).Append(c.Shown).Append(c.Enabled);
            string next=key.ToString();
            bool changed=shownNode!=node || signature!=next;
            bool reopening=!Page.activeSelf;
            Page.SetActive(true);
            if(!changed && !reopening && (reveal!=null || ActionsReady)) return;
            Cancel();
            Header.text="事件终端";
            ScreenTitle.text=content.Title;
            ScreenCode.text=(node?.EventId??"EVENT")+"  /  "+(content.CanLeave?"记录结束":content.StepCount>0?"阶段 "+content.Step+" / "+content.StepCount:"事件记录");
            ScreenLocation.text="第 "+(node?.Floor??1).ToString("D2")+" 层  ·  房间 "+((node?.Index??0)+1).ToString("D2");
            EventIllustration.enabled=EventIllustration.sprite;
            if(changed)
            {
                shownNode=node; signature=next; fullText=story;
                characterStarts=StringInfo.ParseCombiningCharacters(fullText); revealed=0;
                Body.text=fullText; Canvas.ForceUpdateCanvases();
                measuredScale=-1; ResizeStory();
                Body.text=""; StoryScroll.StopMovement(); StoryScroll.verticalNormalizedPosition=1;
            }
            PrepareActions(content);
            reveal=StartCoroutine(Reveal());
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
        void PrepareActions(EventPage content)
        {
            var visible=content.Choices.Select((c,i)=>new { Choice=c,Index=i }).Where(x=>x.Choice.Shown).ToArray();
            leave=content.CanLeave && Source.EventCanLeave; continueChoice=-1;
            if(!leave && visible.Length==1 && new[]{"继续","继续前进","离开"}.Contains(visible[0].Choice.Label))
                continueChoice=visible[0].Index;
            int count=leave || continueChoice>=0?0:visible.Length;
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
                rect.sizeDelta=new Vector2(680,height); rect.anchoredPosition=new Vector2(SlideDistance(),-i*(height+18));
                item.Fade.alpha=0; item.Fade.blocksRaycasts=false; item.Hit.interactable=false;
            }
            ChoiceHeading.text=count>0?"行动选择":"";
            ChoiceHeading.gameObject.SetActive(false);
            Continue.gameObject.SetActive(false); ContinueFade.alpha=0; ContinueFade.blocksRaycasts=false;
            ContinueLabel.text=leave?"离开  →":"继续  →";
            Continue.interactable=false;
        }
        float SlideDistance()
        {
            var canvas=GetComponent<Canvas>();
            return Mathf.Max(2100,canvas.pixelRect.width/Mathf.Max(.01f,canvas.scaleFactor)+800);
        }
        IEnumerator Reveal()
        {
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
                yield return null;
            }
            foreach(var choice in shown)
            { AnimateChoice(choice,1); choice.Fade.blocksRaycasts=choice.Available; choice.Hit.interactable=choice.Available; }
            if(showContinue)
            {
                AnimateContinue(1); ContinueFade.blocksRaycasts=true;
                Continue.interactable=leave || EventScripts.Page(Source.ActiveNode,Source.Run,Source.Catalog.MapRules()).Choices[continueChoice].Enabled;
            }
            ActionsReady=true; reveal=null;
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
