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
            SupplyStationBuilder.Apply();
            File.WriteAllText(Report,"Supply station Play Mode verification\n");
            foreach(var path in ExplorationSceneIntegration.EntryScenes)
            {
                EditorSceneManager.OpenScene(path);
                var view=UnityEngine.Object.FindObjectOfType<IndustrialEventPage>();
                Check(view && view.SupplyView && view.Source,"Missing supply layout in "+path);
                Pass("Applied to "+path);
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
            Check(view.ActionsReady,"Supply animation did not complete");
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
        static void Capture(IndustrialEventPage view,string name,int width=1920,int height=1080)
        {
            ExplorationMapBuilder.Render(view.GetComponent<Canvas>(),Camera.main,name,width,height);
            File.Copy(ExplorationMapBuilder.Output+"/"+name+".png",SupplyStationBuilder.Output+"/"+name+".png",true);
        }
        static IEnumerator Tests()
        {
            var view=UnityEngine.Object.FindObjectOfType<IndustrialEventPage>(); var app=view.Source; var supply=view.SupplyView;
            app.DebugForceRoom(NodeType.Supply);
            var hero=app.Run.Heroes[0]; hero.Hp=Math.Max(1,hero.Definition.MaxHp/4); app.Run.PlayerSanity=20; app.Refresh();
            int before=hero.Hp; view.UseSupply(false); Check(hero.Hp==before,"Recovery accepted before entrance completed");
            var wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(supply.Layout.activeInHierarchy && !supply.OriginalMonitor.activeSelf && !supply.OriginalStory.activeSelf,"Original narrative still visible");
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==4 && view.ChoiceColumn.anchoredPosition.x==66,"Other supply options and shop not retained on the left");
            Check(supply.Monitor.rect.height>600,"Screen was not enlarged");
            var actions=(RectTransform)supply.Actions.transform;
            Check(actions.rect.width<=supply.Monitor.rect.width && Mathf.Abs(actions.anchoredPosition.x-supply.Monitor.anchoredPosition.x)<.1f,"Recovery row exceeds screen width");
            Capture(view,"Supply-Station-1920x1080"); Capture(view,"Supply-Station-2560x1080",2560,1080);
            yield return .2f;
            Click(supply.Heal);
            CheckStationStable(view,"heal");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(hero.Hp>before && app.Map.SupplyHealed && !supply.Heal.interactable,"Heal action failed");
            before=hero.Hp; view.UseSupply(false); Check(hero.Hp==before,"Heal used twice");
            Click(supply.Calm);
            CheckStationStable(view,"sanity");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(app.Run.PlayerSanity==30 && app.Map.SupplyCalmed && !supply.Calm.interactable,"Sanity recovery failed");
            view.UseSupply(true); Check(app.Run.PlayerSanity==30,"Sanity recovered twice");
            Capture(view,"Supply-Station-Used");
            Pass("Screen, hidden narrative, horizontal bounded buttons, actual pointer clicks, heal/sanity results and once-per-station limits");
            Click(view.Choices.First(c=>c.gameObject.activeSelf && c.Title.text=="部位与义肢").Hit);
            CheckStationStable(view,"select hero subpage");
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==app.Run.Heroes.Count+1,"Supply hero subpage filtered incorrectly");
            Click(view.Choices[0].Hit); wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(view.Choices.Count(c=>c.gameObject.activeSelf)==5,"Supply limb subpage lost options");
            Capture(view,"Supply-Station-Limbs");
            Click(view.Choices.First(c=>c.gameObject.activeSelf && c.Title.text=="返回").Hit);
            yield return .2f;
            Click(view.Choices.First(c=>c.gameObject.activeSelf && c.Title.text=="返回").Hit);
            yield return .2f;
            var shop=view.GetComponent<SupplyShopView>();
            app.Map.Supplies=40; app.Refresh();
            Click(view.Choices.First(c=>c.gameObject.activeSelf && c.Title.text=="商店").Hit);
            CheckStationStable(view,"open shop");
            yield return .6f;
            Check(shop.Page.activeInHierarchy && shop.EntranceComplete && shop.Tabs.Length==5,"Shop modal did not open");
            Check(shop.Cards.Count==4,"Missing prosthetics in shop");
            Capture(view,"Supply-Shop-Prosthetics"); yield return .2f;
            int stock=app.Run.Prosthetics.Count, money=app.Map.Supplies;
            Click(shop.Cards[0].Buy);
            Check(app.Run.Prosthetics.Count==stock+1 && app.Map.Supplies==money-SupplyShop.ProstheticPrice,"Prosthetic purchase did not reach inventory");
            Check(shop.EntranceCount==1 && shop.Fade.alpha==1 && shop.EntranceComplete,"Purchase restarted shop entrance");
            CheckStationStable(view,"purchase");
            view.Continue.onClick.Invoke(); app.LeaveRoom();
            Check(shop.Page.activeInHierarchy && view.Page.activeSelf,"Shop allowed background commands");
            Click(shop.Tabs[1]); yield return .2f;
            Check(shop.Cards.Count==AccessorySystem.Defaults.Length,"Missing accessory inventory");
            stock=app.Run.AccessoryStock.Count; money=app.Map.Supplies; Click(shop.Cards[0].Buy);
            Check(app.Run.AccessoryStock.Count==stock+1 && app.Map.Supplies==money-SupplyShop.AccessoryPrice,"Accessory purchase failed");
            Capture(view,"Supply-Shop-Accessories"); yield return .2f;
            shop.Scroll.verticalNormalizedPosition=0; yield return .2f;
            Click(shop.Cards.Last().Buy); Check(app.Run.AccessoryStock.Last().Id==shop.Cards.Last().ProductId,"Cannot buy final scrolled accessory");
            for(int category=2;category<5;category++)
            {
                Click(shop.Tabs[category]); yield return .2f;
                Check(shop.Cards.Count==1,"Consumable category does not match");
                stock=app.Run.Consumables.Count; money=app.Map.Supplies;
                var product=SupplyShop.Products(app.Catalog.Rules().Limbs).First(p=>p.Id==shop.Cards[0].ProductId);
                Click(shop.Cards[0].Buy);
                Check(app.Run.Consumables.Count==stock+1 && app.Run.Consumables.Last().Id==product.Id && app.Map.Supplies==money-product.Price,"Consumable purchase failed: "+product.Name);
            }
            Check(shop.ProductDescription.text.Contains("暂时不能使用"),"Surgery limitation missing from product description");
            Capture(view,"Supply-Shop-Surgery"); yield return .2f;
            app.Map.Supplies=0; app.Refresh();
            Check(!shop.Cards[0].Buy.interactable && shop.Cards[0].BuyLabel.text=="补给不足","Insufficient funds button not disabled");
            stock=app.Run.Consumables.Count;
            Check(!app.BuySupplyShopItem(shop.Cards[0].ProductId) && app.Map.Supplies==0 && app.Run.Consumables.Count==stock,"Insufficient funds changed inventory");
            Check(!app.BuySupplyShopItem("not-a-product") && app.Map.Supplies==0,"Unknown item accepted");
            Click(shop.Close); CheckStationStable(view,"close shop");
            Check(!app.SupplyShopOpen && !shop.Page.activeSelf,"Shop did not close");
            Check(!app.BuySupplyShopItem("con_hp_restore"),"Closed shop accepts purchase");
            Pass("Shop modal, five categories, purchases and prices, inventory delivery, scrolling, no repeated entrance, insufficient funds, background input and close guards");
            yield return .2f;
            Click(view.Continue); Check(!view.Page.activeSelf,"Leave supply failed");
            Pass("Left-side accessory, consumable, hero and limb menus; leave returns to map");
            app.DebugForceEvent("EVT-003"); view.CharactersPerSecond=10000;
            wait=Ready(view); while(wait.MoveNext()) yield return wait.Current;
            Check(!supply.Layout.activeSelf && supply.OriginalStory.activeSelf && supply.OriginalMonitor.activeSelf,"Event layout not restored");
            Check(view.ChoiceColumn.anchoredPosition==supply.DefaultChoicePosition,"Event choices not restored");
            Capture(view,"Event-Layout-Preserved"); Pass("Other room layout and event narrative preserved");
            app.NewRun(); Check(!app.SupplyShopOpen,"New run did not clear shop state");
        }
        static void CheckStationStable(IndustrialEventPage view,string operation)
        {
            Check(view.ActionsReady && view.SupplyView.Actions.alpha==1 && view.ContinueFade.alpha==1,"Station replayed entrance after "+operation);
            foreach(var card in view.Choices.Where(c=>c.gameObject.activeSelf))
                Check(Mathf.Abs(((RectTransform)card.transform).anchoredPosition.x)<.01f && card.Fade.alpha>0,"Existing option moved off-screen after "+operation);
            Check(Vector2.Distance(((RectTransform)view.Continue.transform).anchoredPosition,view.ContinuePosition)<.05f,"Leave button moved after "+operation);
        }
    }
}
