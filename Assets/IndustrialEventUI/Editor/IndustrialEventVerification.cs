using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using GameMake.EventUI;
using GameMake.ExplorationEditor;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Core;
using UnityEditor;
using UnityEngine;

namespace GameMake.EventEditor
{
    [InitializeOnLoad]
    public static class IndustrialEventVerification
    {
        const string Key="IndustrialEvent.Verify";
        static IEnumerator routine;
        static double resume;
        static string Report=>IndustrialEventBuilder.Output+"/validation.txt";
        static IndustrialEventVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start();};
            if(EditorApplication.isPlaying && SessionState.GetBool(Key,false)) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            IndustrialEventBuilder.Build();
            File.WriteAllText(Report,"Industrial event page Play Mode verification\n");
            SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
        }
        static void Start()
        {
            if(routine!=null) return;
            Application.runInBackground=true; routine=Tests(); resume=EditorApplication.timeSinceStartup+.8;
            EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(routine==null || EditorApplication.timeSinceStartup<resume) return;
            try
            {
                if(!routine.MoveNext()) { Finish(null); return; }
                resume=EditorApplication.timeSinceStartup+(routine.Current is float delay?delay:.1f);
            }
            catch(Exception e) { Finish(e); }
        }
        static void Finish(Exception e)
        {
            EditorApplication.update-=Tick; routine=null; SessionState.SetBool(Key,false);
            File.AppendAllText(Report,e==null?"PASS: all checks completed\n":"FAIL: "+e+"\n");
            if(e!=null) Debug.LogException(e);
            EditorApplication.Exit(e==null?0:1);
        }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static void Pass(string s)=>File.AppendAllText(Report,"PASS: "+s+"\n");
        static IEnumerator Ready(IndustrialEventPage view)
        {
            double until=EditorApplication.timeSinceStartup+18;
            while(!view.ActionsReady && EditorApplication.timeSinceStartup<until) yield return .1f;
            Check(view.ActionsReady && view.TextComplete,"Text reveal / slide animation failed to complete");
        }
        static void Capture(IndustrialEventPage view,string name,int width=1920,int height=1080)
        {
            ExplorationMapBuilder.Render(view.GetComponent<Canvas>(),Camera.main,name,width,height);
            File.Copy(ExplorationMapBuilder.Output+"/"+name+".png",IndustrialEventBuilder.Output+"/"+name+".png",true);
        }
        static IEnumerator Tests()
        {
            var view=UnityEngine.Object.FindObjectOfType<IndustrialEventPage>(); var app=view.Source;
            var old=UnityEngine.Object.FindObjectOfType<GothicNodeHudController>();
            var map=UnityEngine.Object.FindObjectOfType<ExplorationMapController>();
            Check(app && app.Run!=null && map.Page.activeSelf && !view.Page.activeSelf,"Main entry is not connected correctly");
            app.DebugForceEvent("EVT-003");
            Check(view.Page.activeInHierarchy && !old.RoomRoot.activeInHierarchy && !map.Page.activeSelf,"New event did not replace old page");
            Check(!view.TextComplete && view.Choices.All(c=>!c.Hit.interactable) && !view.Continue.gameObject.activeSelf,"Actions visible before narrative finished");
            var node=app.ActiveNode; view.Choices[0].Hit.onClick.Invoke();
            Check(node.EventStep==0,"Early click changed event");
            yield return .25f;
            int chars=view.Body.text.Length; app.Refresh(); app.Refresh();
            Check(view.Body.text.Length>=chars,"Refresh restarted typewriter");
            Capture(view,"Event-Typing");
            var wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==2 && view.Choices[0].Hit.interactable,"Event choices were not revealed");
            Check(Mathf.Abs(((RectTransform)view.Choices[0].transform).anchoredPosition.x)<.01f,"Choices did not finish sliding from right");
            Capture(view,"Event-Choices-1920x1080"); Capture(view,"Event-Choices-2560x1080",2560,1080);
            Pass("main scene replaces old event UI; grapheme typewriter; no early clicks; refresh preserves progress; right-side slide; 1080p and ultrawide layout");
            view.Choices[0].Hit.onClick.Invoke();
            Check(node.EventStep==1 && !view.ActionsReady,"First choice did not enter next narrative step");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            app.ToggleHelp(); Check(!view.Page.activeSelf,"Event overlays help");
            app.ToggleHelp(); wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            view.Choices[0].Hit.onClick.Invoke();
            Check(app.EventCanLeave && !view.Continue.gameObject.activeSelf,"Event end did not wait for closing narrative");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Continue.gameObject.activeSelf && view.ContinueLabel.text.Contains("离开") && view.Continue.interactable,"No bottom-right leave button");
            Capture(view,"Event-Leave"); view.Continue.onClick.Invoke();
            Check(map.Page.activeSelf && !view.Page.activeSelf,"Event did not return to new map");
            Pass("multi-stage choice, help reopen, closing text then bottom-right leave button, return to new map");

            app.DebugForceEvent("EVT-007");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.All(c=>!c.gameObject.activeSelf) && view.ContinueLabel.text.Contains("继续"),"Pure continuation not placed at bottom right");
            Capture(view,"Event-Continue"); view.Continue.onClick.Invoke();
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(app.EventCanLeave && view.ContinueLabel.text.Contains("离开"),"Continue did not reach final leave"); view.Continue.onClick.Invoke();
            Pass("continue-only event uses bottom-right button, then leave after closing text");

            app.DebugForceEvent("EVT-004");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==3,"Hidden sanity-gated choice leaked into UI");
            view.Choices[0].Hit.onClick.Invoke();
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==4,"Party selection choices missing");
            Capture(view,"Event-Four-Choices");
            view.Choices[0].Hit.onClick.Invoke();
            Check(app.Run.Heroes[0].Sanity==0,"Choice index mapping applied potion to wrong hero");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current; view.Continue.onClick.Invoke();
            Pass("sanity-gated choices hidden; four character options and actual potion outcome");

            app.DebugForceEvent("EVT-H001");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            view.Choices.First(c=>c.gameObject.activeSelf && c.Title.text=="探索").Hit.onClick.Invoke();
            Check(app.Battle!=null && !view.Page.activeSelf,"Event battle handover failed");
            app.DebugWinBattle(); app.ContinueBattle();
            Check(view.Page.activeSelf && app.Room==Ruin.Runtime.RoomView.Event,"Battle did not return to the event");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.ScreenTitle.text=="隐藏的岔道","Event changed on battle return");
            Pass("event -> actual battle -> settlement -> continuation of same event");

            var note=typeof(Ruin.Runtime.StageOneApp).GetProperty("RoomNote",BindingFlags.Instance|BindingFlags.Public);
            string longNote=string.Join("\n\n",Enumerable.Repeat("测试长记录：这里的文字必须完整显示，能够上下滚动阅读，不能溢出文本框或遮挡选项。",18))+"\n\n长文本结束。";
            note.SetValue(app,longNote); view.CharactersPerSecond=10000; app.Refresh();
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.StoryContent.rect.height>view.StoryScroll.viewport.rect.height*2,"Long narrative does not expand scroll content");
            view.StoryScroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases();
            yield return .2f;
            Check(view.StoryScroll.verticalNormalizedPosition<.01f,"Cannot scroll narrative to bottom");
            Check(view.StoryScroll.verticalScrollbar.value<.01f,"Scrollbar does not follow narrative position");
            Check(view.StoryScroll.verticalScrollbar.targetGraphic.raycastTarget,"Scrollbar handle cannot receive pointer input");
            Check(view.Body.cachedTextGenerator.lines.Last().startCharIdx>view.Body.text.Length-80,"Last narrative lines were truncated by text rendering");
            Capture(view,"Event-Long-Text-Scroll");
            yield return .2f; // Let the restored Game View scale rebuild its own text metrics.
            var lastLine=view.Body.cachedTextGenerator.lines.Last();
            Check((-lastLine.topY+lastLine.height)/view.Body.pixelsPerUnit<=view.StoryContent.rect.height+2,"Narrative content height does not include final rendered line");
            Pass("long narrative is fully retained in a masked, functioning scroll area");
        }
    }
}
