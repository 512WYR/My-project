using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.EventUI;
using GameMake.ExplorationEditor;
using Ruin.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.EventEditor
{
    [InitializeOnLoad]
    public static class SupplyStationVerification
    {
        const string Key="SupplyStation.Verify";
        static IEnumerator routine;
        static double resume;
        static string Report=>SupplyStationBuilder.Output+"/validation.txt";
        static SupplyStationVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start();};
            if(EditorApplication.isPlaying && SessionState.GetBool(Key,false)) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            // Verify in place: rebuilding the prefab would overwrite hand-configured art. The choice
            // scroll is added by IndustrialEventPage.EnsureChoiceScroll when the page binds.
            Directory.CreateDirectory(SupplyStationBuilder.Output);
            File.WriteAllText(Report,"Supply station Play Mode verification (event template)\n");
            foreach(var path in ExplorationSceneIntegration.EntryScenes)
            {
                EditorSceneManager.OpenScene(path);
                var view=UnityEngine.Object.FindObjectOfType<IndustrialEventPage>();
                Check(view && view.Source,"Missing event page in "+path);
                Check(view.ChoiceColumn || view.ChoiceScroll,"Missing choice column in "+path);
                Check(view.Page.transform.Find("Event Story - 可滚动文本"),"Event story missing in "+path);
                Pass("Found event page in "+path);
            }
            EditorSceneManager.OpenScene(ExplorationSceneIntegration.MainScene);
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
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick; routine=null; SessionState.SetBool(Key,false);
            File.AppendAllText(Report,error==null?"PASS: all checks completed\n":"FAIL: "+error+"\n");
            if(error!=null) Debug.LogException(error);
            EditorApplication.Exit(error==null?0:1);
        }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static void Pass(string s)=>File.AppendAllText(Report,"PASS: "+s+"\n");
        static IEnumerator Ready(IndustrialEventPage view)
        {
            double until=EditorApplication.timeSinceStartup+10;
            while(!view.ActionsReady && EditorApplication.timeSinceStartup<until) yield return .1f;
            Check(view.ActionsReady,"Page animation did not complete");
            yield return .15f;
        }
        static void Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            var pointer=new PointerEventData(EventSystem.current) {position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center))};
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"Button occluded: "+button.name);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        static EventChoiceView Choice(IndustrialEventPage view,string label)
        {
            return view.Choices.FirstOrDefault(c=>c.gameObject.activeSelf && c.Title.text.StartsWith(label));
        }
        static void ClickChoice(IndustrialEventPage view,string label)
        {
            var choice=Choice(view,label);
            Check(choice!=null,"Choice missing: "+label);
            Click(choice.Hit);
        }
        static void Capture(IndustrialEventPage view,string name,int width=1920,int height=1080)
        {
            ExplorationMapBuilder.Render(view.GetComponent<Canvas>(),Camera.main,name,width,height);
            File.Copy(ExplorationMapBuilder.Output+"/"+name+".png",SupplyStationBuilder.Output+"/"+name+".png",true);
        }
        static IEnumerator Tests()
        {
            var view=UnityEngine.Object.FindObjectOfType<IndustrialEventPage>(); var app=view.Source;
            Check(view.ChoiceScroll && view.ChoiceViewport && view.ChoiceColumn.parent==view.ChoiceViewport,
                "Runtime choice scroll was not created");
            app.DebugForceRoom(NodeType.Supply);
            var hero=app.Run.Heroes[0]; hero.Hp=Mathf.Max(1,hero.MaxHp/4); app.Run.PlayerSanity=20;
            // Third floor charges for both recovery actions, so the result text must show a deduction.
            app.Map.Floor=3; app.Map.Supplies=10; app.Refresh();
            var wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;

            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==4,"Supply main page does not show four standard choices");
            Check(Choice(view,"治疗")!=null && Choice(view,"静养")!=null
                && Choice(view,"部位与义肢")!=null && Choice(view,"商店")!=null,"Supply choices missing");
            Check(Choice(view,"治疗").Detail.text.Contains("矿石"),"Heal price missing before selection");
            Check(Choice(view,"静养").Detail.text.Contains("矿石"),"Calm price missing before selection");
            Check(view.Body.text.Contains("当前矿石"),"Ore balance missing from the page body");
            Capture(view,"Supply-Station-1920x1080"); Capture(view,"Supply-Station-2560x1080",2560,1080);

            int before=hero.Hp, money=app.Map.Supplies;
            ClickChoice(view,"治疗");
            CheckStable(view,"heal");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(hero.Hp>before && app.Map.SupplyHealed,"Heal action failed");
            Check(app.Map.Supplies==money-SupplyShop.StationHealPrice,"Heal did not charge the third-floor price");
            Check(app.RoomNote.Contains("消耗") && app.RoomNote.Contains("剩余"),"Heal result does not mention cost and balance");
            Check(view.Body.text.Contains("剩余"),"Heal result missing from the body");

            before=(int)app.Run.PlayerSanity; money=app.Map.Supplies;
            ClickChoice(view,"静养");
            CheckStable(view,"sanity");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(app.Run.PlayerSanity==30 && app.Map.SupplyCalmed,"Sanity recovery failed");
            Check(app.Map.Supplies==money-SupplyShop.StationCalmPrice,"Calm did not charge the third-floor price");
            Check(app.RoomNote.Contains("消耗") && app.RoomNote.Contains("剩余"),"Calm result does not mention cost and balance");
            Capture(view,"Supply-Station-Used");

            ClickChoice(view,"部位与义肢");
            CheckStable(view,"open hero list");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==app.Run.Heroes.Count+1,"Hero list filtered incorrectly");
            Click(view.Choices[0].Hit); wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==5,"Limb page lost options");
            Canvas.ForceUpdateCanvases();
            foreach(var card in view.Choices.Where(c=>c.gameObject.activeSelf))
                Check(card.Detail.rectTransform.rect.height+.1f>=card.Detail.preferredHeight,"Choice detail clipped: "+card.Title.text);
            Check(view.ChoiceColumn.sizeDelta.y>view.ChoiceViewport.rect.height+1,"Long choice list does not scroll");
            Check(view.ChoiceScroll.vertical && view.ChoiceScroll.content==view.ChoiceColumn,"Choice scroll not wired");
            Capture(view,"Supply-Station-Limbs");
            view.ChoiceScroll.verticalNormalizedPosition=0; Canvas.ForceUpdateCanvases(); yield return .1f;
            Click(view.Choices.Last(c=>c.gameObject.activeSelf).Hit);
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            ClickChoice(view,"返回");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            ClickChoice(view,"返回");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;

            var shop=view.GetComponent<SupplyShopView>();
            app.Map.Supplies=40; app.Refresh();
            ClickChoice(view,"商店");
            CheckStable(view,"open shop");
            yield return .6f;
            Check(shop.Page.activeInHierarchy && shop.EntranceComplete && shop.Tabs.Length==5,"Shop modal did not open");
            Check(shop.Cards.Count==LimbSystem.DefaultProsthetics.Length,"Missing prosthetics in shop");
            int stock=app.Run.Prosthetics.Count, shopMoney=app.Map.Supplies;
            Click(shop.Cards[0].Buy);
            Check(app.Run.Prosthetics.Count==stock+1 && app.Map.Supplies==shopMoney-SupplyShop.ProstheticPrice,"Prosthetic purchase did not reach inventory");
            Check(shop.Notice.text.Contains("剩余"),"Shop purchase notice does not mention the balance");
            Check(app.RoomNote.Contains("购买") && app.RoomNote.Contains("剩余"),"Purchase result not kept in the page body");
            Check(shop.EntranceCount==1 && shop.Fade.alpha==1 && shop.EntranceComplete,"Purchase restarted shop entrance");
            Capture(view,"Supply-Shop-Prosthetics"); yield return .2f;
            Click(shop.Close); CheckStable(view,"close shop");
            Check(!app.SupplyShopOpen && !shop.Page.activeSelf,"Shop did not close");
            Check(app.RoomNote.Contains("购买"),"Closing the shop discarded the purchase result");

            yield return .2f;
            Click(view.Continue); Check(!view.Page.activeSelf,"Leave supply failed");
            Pass("Event-template supply page: prices, ore balance, cost text, auto-height, scrolling and shop results");

            app.DebugForceEvent("EVT-003"); view.CharactersPerSecond=10000;
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Page.transform.Find("Event Story - 可滚动文本").gameObject.activeSelf,"Event story not restored");
            Check(view.Monitor.gameObject.activeInHierarchy,"Event monitor not restored");
            Check(Vector2.Distance(((RectTransform)view.ChoiceScroll.transform).anchoredPosition,new Vector2(1160,-164))<.1f,"Choice column moved by supply layout");
            Check(view.Choices.Any(c=>c.gameObject.activeSelf),"Event choices missing");
            Capture(view,"Event-Layout-Preserved"); Pass("Ordinary event layout preserved");
            app.NewRun(); Check(!app.SupplyShopOpen,"New run did not clear shop state");
        }
        static void CheckStable(IndustrialEventPage view,string operation)
        {
            Check(view.ActionsReady,"Page was not ready after "+operation);
            foreach(var card in view.Choices.Where(c=>c.gameObject.activeSelf))
                Check(Mathf.Abs(((RectTransform)card.transform).anchoredPosition.x)<.01f && card.Fade.alpha>0,"Existing option moved off-screen after "+operation);
            Check(Vector2.Distance(((RectTransform)view.Continue.transform).anchoredPosition,view.ContinuePosition)<.05f,"Leave button moved after "+operation);
        }
    }
}
