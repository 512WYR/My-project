using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameMake.CharacterUI;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMake.CharacterEditor
{
    [InitializeOnLoad]
    public static class CharacterPanelVerification
    {
        const string Key="CharacterPanel.Verify";
        static IEnumerator routine; static double resume;
        static string Report=>CharacterPanelBuilder.Output+"/validation.txt";
        static CharacterPanelVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start();};
        }
        public static void Run()
        {
            CharacterPanelBuilder.Build(); File.WriteAllText(Report,"Character management Play Mode verification\n");
            SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
        }
        static void Start() { Application.runInBackground=true; routine=Tests(); resume=EditorApplication.timeSinceStartup+1; EditorApplication.update+=Tick; }
        static void Tick()
        {
            if(routine==null || EditorApplication.timeSinceStartup<resume) return;
            try { if(!routine.MoveNext()) { Finish(null); return; } resume=EditorApplication.timeSinceStartup+(routine.Current is float f?f:.1); }
            catch(Exception e) { Finish(e); }
        }
        static void Finish(Exception e)
        {
            routine=null; EditorApplication.update-=Tick; SessionState.SetBool(Key,false);
            File.AppendAllText(Report,e==null?"PASS: all checks completed\n":"FAIL: "+e+"\n");
            if(e!=null) { Debug.LogException(e); try { Capture("Failure"); } catch {} } EditorApplication.Exit(e==null?0:1);
        }
        static void Check(bool value,string note) { if(!value) throw new Exception(note); }
        static void Pass(string text)=>File.AppendAllText(Report,"PASS: "+text+"\n");
        static Button ActiveButton(Transform parent,string label)=>parent.GetComponentsInChildren<Button>().First(b=>b.GetComponentsInChildren<Text>().Any(t=>t.text==label));
        static int ActiveChildren(Transform root)=>root.Cast<Transform>().Count(t=>t.gameObject.activeSelf);
        static void RaycastClick(Button button)
        {
            Canvas.ForceUpdateCanvases(); var rect=(RectTransform)button.transform;
            Vector2 point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            if(button.name=="Click Outside To Close") point=new Vector2(5,5);
            var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(data,hits);
            Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Button>()==button,"Pointer is blocked above "+button.name+" at "+point+": "+string.Join(", ",hits.Take(4).Select(h=>h.gameObject.name)));
            ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
        }
        static IEnumerator Tests()
        {
            var view=UnityEngine.Object.FindObjectOfType<CharacterPanelView>(); var app=view.Source; app.AutomationMode=true;
            var map=UnityEngine.Object.FindObjectOfType<ExplorationMapController>(); var hud=UnityEngine.Object.FindObjectOfType<GothicBattleHudController>();
            Check(app.Run!=null && !view.Page.activeSelf,"Popup opens without a character click");
            Check(Mathf.Abs(((RectTransform)map.transform.Find("Exploration Page/Party Column")).anchoredPosition.x-8)<.01f,"Map portraits not moved left");
            Check(Mathf.Abs(hud.transform.Find("Party - 四人队伍").localScale.x-.88f)<.01f,"Battle portraits not reduced");
            for(int i=0;i<4;i++)
            {
                RaycastClick(map.Party[i].Hit);
                Check(!view.EntranceComplete && !view.WindowFade.blocksRaycasts,"Popup skipped connected entrance animation");
                if(i==0)
                {
                    yield return .18f;
                    Check(view.WindowReveal.rect.width>0 && view.WindowReveal.rect.width<1440,"Window does not reveal from the left");
                    Capture("Map-Connected-Opening");
                }
                yield return .5f;
                Check(view.Page.activeSelf && app.CharacterHero==app.Run.Heroes[i] && view.Heading.text.Contains(app.Run.Heroes[i].Name),"Map character identity mismatch");
                Check(view.EntranceComplete && view.WindowFade.blocksRaycasts && view.Connector.Progress==1,"Connected window did not finish opening");
                Check(view.Connector.GetComponent<CanvasRenderer>()!=null,"Mechanical connection has no renderer");
                CheckScreenEdgeRails(view);
                Check(view.FullBodyName.text==app.Run.Heroes[i].Name && view.FullBodyImage.preserveAspect,"Full body area does not follow selected hero");
                if(i==0) { Capture("Map-Connected-Portrait"); Capture("Map-Connected-Wide",2560,1080); }
                RaycastClick(view.Close);
                yield return .1f;
            }
            RaycastClick(map.Party[1].Hit); yield return .5f; var hero=app.CharacterHero;
            var savedArt=view.FullBodyArt[1]; view.FullBodyArt[1]=view.Healthy[1]; app.Refresh();
            Check(view.FullBodyImage.sprite==view.Healthy[1] && view.FullBodyImage.enabled && !view.FullBodyPlaceholder.gameObject.activeSelf && view.EntranceComplete,"Portrait assignment or refresh restarts entrance");
            view.FullBodyArt[1]=savedArt; app.Refresh();
            Pass("left full-body portrait area, two fixed screen-left mechanical connections, timed left-to-right reveal, click gating and ultrawide layout");
            var step=app.Map.Current; app.SelectNode(0); Check(app.Map.Current==step && app.Room==Ruin.Runtime.RoomView.Map,"Map input leaks through popup");
            app.Run.Consumables.Clear(); app.Run.Prosthetics.Clear(); app.Run.AccessoryStock.Clear(); hero.EquippedAccessories.Clear(); app.Refresh();
            Check(!view.PotionButtons[0].interactable && !view.PotionButtons[1].interactable,"Empty potion stock is usable");
            app.CharacterUsePotion(ConsumableKind.HpRestore); Check(app.Run.Consumables.Count==0,"Empty potion created stock");
            Capture("Map-Character-Empty");
            Pass("four map portraits open matching hero; map anchored left; battle portraits reduced 12%; modal blocks map input and empty inventory");

            foreach(var p in LimbSystem.DefaultProsthetics) app.Run.Prosthetics.Add(LimbSystem.Clone(p));
            for(int i=0;i<4;i++)
            {
                var limb=LimbSystem.Get(hero,(LimbPart)i); limb.Hp=0;
            }
            app.Refresh(); Check(Enumerable.Range(0,4).All(i=>view.LimbImages[i].sprite==view.Damaged[i]),"Broken limbs do not use supplied damaged icons");
            var hover=view.LimbButtons[2].GetComponent<LimbInstallHover>();
            ExecuteEvents.Execute(hover.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
            Check(hover.Plus.activeSelf,"Hover does not show install plus");
            RaycastClick(view.LimbButtons[2]); Capture("Map-Character-Limb-Hover");
            ExecuteEvents.Execute(hover.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);
            int beforeInstall=app.Run.Prosthetics.Count;
            RaycastClick(ActiveButton(view.PickerItems,"装载"));
            Check(LimbSystem.Get(hero,LimbPart.Arm).ProstheticFitted && app.Run.Prosthetics.Count==beforeInstall-1 && view.LimbImages[2].sprite==view.Healthy[2],"Prosthetic does not consume stock or restore arm/icon");
            int stock=app.Run.Prosthetics.Count; app.CharacterInstallProsthetic("prosthetic_arm"); Check(stock==app.Run.Prosthetics.Count,"Repeated installation consumes another item");
            var leg=LimbSystem.Get(hero,LimbPart.Leg); leg.Hp=Mathf.Max(1,leg.MaxHp/2); app.Refresh();
            app.CharacterInstallProsthetic("prosthetic_leg"); Check(app.Run.Prosthetics.Count==stock && !leg.ProstheticFitted,"Injured flesh accepted prosthetic");
            Pass("all four damage sprites; pointer hover plus; matching prosthetic consumed once; limb restored; intact/injured install rejected");

            hero.Hp=1; app.Run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.ForKind(ConsumableKind.HpRestore)));
            app.Run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.ForKind(ConsumableKind.SanityLock))); app.Refresh();
            int other=app.Run.Heroes[0].Hp; RaycastClick(view.PotionButtons[0]);
            Check(hero.Hp>1 && app.Run.Heroes[0].Hp==other && ConsumableSystem.Count(app.Run,ConsumableKind.HpRestore)==0,"Health potion affects wrong hero or stock");
            RaycastClick(view.PotionButtons[1]); Check(hero.SanityLockPending && ConsumableSystem.Count(app.Run,ConsumableKind.SanityLock)==0,"Sanity potion did not apply pending lock");
            app.Run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.ForKind(ConsumableKind.HpRestore))); hero.Hp=hero.Definition.MaxHp; app.Refresh();
            app.CharacterUsePotion(ConsumableKind.HpRestore); Check(ConsumableSystem.Count(app.Run,ConsumableKind.HpRestore)==1,"Full health wastes potion");
            Pass("potions affect clicked hero and decrement stock; full HP and missing stock do not consume; sanity uses existing lock effect");

            app.Run.AccessoryStock.AddRange(AccessorySystem.Defaults.Select(AccessorySystem.Clone)); app.Refresh();
            yield return .1f;
            RaycastClick(ActiveButton(view.Accessories,"装载饰品"));
            var apple=app.Run.AccessoryStock.First(a=>a.Kind==AccessoryKind.GoldenApple); int max=hero.Definition.MaxHp;
            app.CharacterEquipAccessory(apple.Id);
            Check(hero.EquippedAccessories.Count==1 && ActiveChildren(view.Accessories)==2 && hero.Definition.MaxHp>max,"Accessory equip does not add slot / real stats");
            app.Run.AccessoryStock.Add(AccessorySystem.Clone(apple)); int count=app.Run.AccessoryStock.Count;
            app.CharacterEquipAccessory(apple.Id); Check(hero.EquippedAccessories.Count==1 && app.Run.AccessoryStock.Count==count,"Duplicate accessory consumed");
            var forbidden=app.Run.AccessoryStock.First(a=>!string.IsNullOrEmpty(a.RestrictSilhouette) && a.RestrictSilhouette!=hero.Definition.Silhouette);
            app.CharacterEquipAccessory(forbidden.Id); Check(hero.EquippedAccessories.Count==1,"Class restriction bypassed");
            foreach(var a in app.Run.AccessoryStock.ToArray().Where(a=>AccessorySystem.CanEquip(hero,a))) app.CharacterEquipAccessory(a.Id);
            Check(ActiveChildren(view.Accessories)==hero.EquippedAccessories.Count+1,"Accessory slots do not grow");
            Capture("Map-Character-Accessories");
            for(int i=0;i<4;i++)
            {
                var extra=AccessorySystem.Clone(AccessorySystem.Find("acc_sleep")); extra.Id="verify_extra_"+i; extra.Name="扩展饰品 "+i;
                app.Run.AccessoryStock.Add(extra); app.CharacterEquipAccessory(extra.Id);
            }
            Check(view.Accessories.rect.height>188 && ActiveChildren(view.Accessories)==hero.EquippedAccessories.Count+1,"Expanded accessories do not scroll / retain trailing slot");
            view.ShowEquipped(apple.Id); yield return .1f; RaycastClick(ActiveButton(view.PickerItems,"卸下并放回仓库"));
            Check(!hero.EquippedAccessories.Any(a=>a.Id==apple.Id) && hero.Definition.MaxHp==max,"Unequip fails to restore item or max HP");
            Pass("equipping adds one trailing empty slot; unlimited grid scrolls; live accessory stats, duplicate/class restrictions and unequip preserved");
            hero.Dead=true; app.Refresh(); Check(!view.PotionButtons[0].interactable,"Dead hero can use potion"); hero.Dead=false;
            RaycastClick(view.Backdrop); Check(!app.CharacterPanelOpen,"Outside click does not close");

            app.NewRun(); app.DebugForceRoom(NodeType.Battle);
            yield return .2f;
            double until=EditorApplication.timeSinceStartup+20;
            while(app.Busy && EditorApplication.timeSinceStartup<until) yield return .1f;
            Check(!app.Busy && app.Battle.Current.Player,"Battle did not reach player turn");
            var battle=app.Battle; var actor=battle.Current; int marker=battle.Events.Count;
            app.SelectSkill(0); RaycastClick(hud.Panel.PartyButtons[2]);
            yield return .5f;
            Check(app.CharacterHero==app.Run.Heroes[2] && !app.Choosing && battle.Events.Count==marker,"Portrait click submits pending attack or targets wrong hero");
            CheckScreenEdgeRails(view);
            hero=app.CharacterHero; hero.Hp=1;
            app.Run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.ForKind(ConsumableKind.HpRestore)));
            LimbSystem.Get(hero,LimbPart.Head).Hp=0;
            app.Run.Prosthetics.Add(LimbSystem.DefaultProsthetics[0]);
            app.Run.AccessoryStock.Add(AccessorySystem.Clone(AccessorySystem.Find("acc_apple"))); app.Refresh();
            view.ShowLimb(0); yield return .1f; RaycastClick(ActiveButton(view.PickerItems,"装载"));
            Check(LimbSystem.Get(hero,LimbPart.Head).ProstheticFitted,"Combat prosthetic install fails");
            app.CharacterEquipAccessory("acc_apple"); Check(hero.EquippedAccessories.Any(a=>a.Id=="acc_apple"),"Combat accessory install fails");
            Capture("Battle-Character-Panel");
            int stockBefore=ConsumableSystem.Count(app.Run,ConsumableKind.HpRestore); RaycastClick(view.PotionButtons[0]);
            Check(!app.CharacterPanelOpen && hero.Hp>1 && ConsumableSystem.Count(app.Run,ConsumableKind.HpRestore)==stockBefore-1 && battle.Events.Count>marker,"Battle potion does not execute through combat engine");
            Check(app.Busy || battle.Current!=actor,"Battle potion did not consume action");
            app.CharacterUsePotion(ConsumableKind.HpRestore); Check(ConsumableSystem.Count(app.Run,ConsumableKind.HpRestore)==stockBefore-1,"Closed popup accepted stale second click");
            while(app.Busy && EditorApplication.timeSinceStartup<until) yield return .1f;
            RaycastClick(hud.Panel.PartyButtons[0]); Check(app.CharacterHero==app.Run.Heroes[0],"Battle popup cannot reopen for another hero");
            app.NewRun(); Check(!view.Page.activeSelf && !app.CharacterPanelOpen,"Popup retains previous run hero");
            Pass("combat portrait opens without firing pending skill; combat equipment works; potion uses battle action and closes; stale clicks rejected; new run clears popup");
        }
        static void CheckScreenEdgeRails(CharacterPanelView view)
        {
            var rails=view.Page.GetComponentsInChildren<CharacterLinkGraphic>();
            Check(rails.Length==2,"Expected exactly two screen-edge connections");
            foreach(var rail in rails)
                Check(Mathf.Abs(rail.StartPoint.x)<.01f && Mathf.Abs(rail.EndPoint.x-320)<.01f && rail.StartPoint.y==rail.EndPoint.y && rail.Progress==1 && rail.GetComponent<CanvasRenderer>(),"Connection must run horizontally from screen left to window");
            Check(Mathf.Abs(view.Connector.StartPoint.y-view.LowerConnector.StartPoint.y-740)<.01f,"Connections do not dock at upper and lower window positions");
        }
        public static void Capture(string name,int width=1920,int height=1080)
        {
            var camera=Camera.main; var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
            var canvases=UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
            var modes=canvases.Select(c=>c.renderMode).ToArray(); var scales=canvases.Select(c=>c.scaleFactor).ToArray();
            var scalers=canvases.Select(c=>c.GetComponent<CanvasScaler>()).ToArray(); var enabled=scalers.Select(s=>s && s.enabled).ToArray();
            var target=new RenderTexture(width,height,24); var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;
                for(int i=0;i<canvases.Length;i++)
                {
                    if(scalers[i]) scalers[i].enabled=false;
                    canvases[i].renderMode=RenderMode.ScreenSpaceCamera; canvases[i].worldCamera=camera; canvases[i].planeDistance=100;
                    canvases[i].scaleFactor=Mathf.Min(width/1920f,height/1080f);
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
                tex.ReadPixels(new Rect(0,0,width,height),0,0); tex.Apply(); File.WriteAllBytes(CharacterPanelBuilder.Output+"/"+name+".png",tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=oldActive; camera.targetTexture=oldTarget;
                for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=null; canvases[i].scaleFactor=scales[i]; if(scalers[i]) scalers[i].enabled=enabled[i]; }
                UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(target); Canvas.ForceUpdateCanvases();
            }
        }
    }
}
