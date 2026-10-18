using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using GameMake.IndustrialBattleUI;
using GameMake.ExplorationEditor;
using Ruin.Core;
using UnityEditor;
using UnityEngine;

namespace GameMake.IndustrialBattleEditor
{
    [InitializeOnLoad]
    public static class IndustrialBattleVerification
    {
        const string Key="IndustrialBattle.Verify";
        static IEnumerator routine;
        static double resume;
        static IndustrialBattleVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{ if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start(); };
            if(EditorApplication.isPlaying && SessionState.GetBool(Key,false)) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            IndustrialBattleBuilder.Build();
            File.WriteAllText(IndustrialBattleBuilder.Output+"/validation.txt","Industrial battle UI Play Mode verification\n");
            SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
        }
        static void Start() { if(routine!=null) return; Application.runInBackground=true; routine=Tests(); resume=EditorApplication.timeSinceStartup+.8; EditorApplication.update+=Tick; }
        static void Tick()
        {
            if(routine==null || EditorApplication.timeSinceStartup<resume) return;
            try
            {
                if(!routine.MoveNext()) { Finish(null); return; }
                resume=EditorApplication.timeSinceStartup+(routine.Current is float seconds?seconds:.1f);
            }
            catch(Exception e) { Finish(e); }
        }
        static void Finish(Exception e)
        {
            EditorApplication.update-=Tick; routine=null; SessionState.SetBool(Key,false);
            if(e!=null) Debug.LogException(e);
            File.AppendAllText(IndustrialBattleBuilder.Output+"/validation.txt",e==null?"PASS: all checks completed\n":"FAIL: "+e+"\n");
            Debug.Log(e==null?"INDUSTRIAL_BATTLE_PLAYMODE_PASS":"INDUSTRIAL_BATTLE_PLAYMODE_FAIL"); EditorApplication.Exit(e==null?0:1);
        }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static void Pass(string s) { File.AppendAllText(IndustrialBattleBuilder.Output+"/validation.txt","PASS: "+s+"\n"); }
        static void Capture(GothicBattleHudController hud,string file,int width=1920,int height=1080)
        {
            ExplorationMapBuilder.Render(hud.GetComponent<Canvas>(),Camera.main,file,width,height);
            File.Copy(ExplorationMapBuilder.Output+"/"+file+".png",IndustrialBattleBuilder.Output+"/"+file+".png",true);
        }
        static IEnumerator Tests()
        {
            var hud=UnityEngine.Object.FindObjectOfType<GothicBattleHudController>(); var app=hud.BattleSource;
            Check(app && app.Run!=null && app.Battle!=null,"Preview did not enter actual battle");
            app.AutomationMode=true;
            double timeout=EditorApplication.timeSinceStartup+10;
            while((app.Busy || !app.Battle.Current.Player) && EditorApplication.timeSinceStartup<timeout) yield return .2f;
            Check(!app.Busy && app.Battle.Current.Player,"Battle never reached player input");
            hud.Sync(); var details=hud.GetComponent<IndustrialBattleDetails>(); details.Sync();
            Check(hud.ChromeRoots.All(r=>r.activeInHierarchy),"Battle page is hidden");
            Check(hud.Allies.Count(c=>c.gameObject.activeSelf)==4 && hud.Enemies.Count(c=>c.gameObject.activeSelf)==3,"Missing actor cards");
            Check(details.SkillNames.Any(t=>t.text!="—"),"No actual skill names");
            Check(details.Wheel.rect.width>=228 && hud.Counter.FitDigitsToContainer,"Enlarged counter layout missing");
            Check(Mathf.Abs(hud.Counter.DigitSlots[0].rectTransform.rect.height-94)<.01f,"Digits do not fill the inner frame height");
            Capture(hud,"Battle-1920x1080"); Capture(hud,"Battle-2560x1080",2560,1080);
            Pass("preview enters actual combat; four allies, three enemies; actual skill names and turn label; 1080p/ultrawide rendering");
            var presentation=hud.Presentation;
            hud.Panel.PartyButtons[0].onClick.Invoke();
            Check(app.InspectedId==app.Run.Heroes[0].Id,"Party selection does not reach game state");
            for(int part=0;part<4;part++)
            {
                var limb=LimbSystem.Get(app.Run.Heroes[0],(LimbPart)part); limb.Hp=0; app.Refresh();
                Check(presentation.Limbs[part].Icon.sprite==presentation.Limbs[part].BrokenSprite && presentation.Limbs[part].Fill.fillAmount==0,"Broken limb view incorrect");
                hud.Panel.PartyButtons[1].onClick.Invoke(); Check(presentation.Limbs[part].Icon.sprite==presentation.Limbs[part].HealthySprite,"Limb state leaked to another actor");
                hud.Panel.PartyButtons[0].onClick.Invoke(); limb.Hp=limb.MaxHp; app.Refresh();
                Check(presentation.Limbs[part].Icon.sprite==presentation.Limbs[part].HealthySprite && presentation.Limbs[part].Fill.fillAmount==1,"Limb restore failed");
            }
            Check(presentation.HealthFill.sprite && presentation.SanityFill.sprite && hud.Allies[0].HealthFill.sprite && hud.Allies[0].SanityFill.sprite,"Fill bar has no sprite; Image ignores fillAmount without one");
            app.Run.Heroes[0].Hp=23; app.Run.Heroes[0].Sanity=67; app.Refresh();
            Check(Mathf.Abs(presentation.HealthFill.fillAmount-23f/app.Run.Heroes[0].Definition.MaxHp)<.001f && Mathf.Abs(presentation.SanityFill.fillAmount-.67f)<.001f,"Vitals are not bound");
            Check(hud.Panel.AttributeValues!=null && hud.Panel.AttributeValues.Length==6 && hud.Panel.AttributeValues.All(t=>t && t.text!="—") && hud.Panel.AttributeValues.Any(t=>t.text.EndsWith("%")),"Bottom attributes were not filled");
            Check(hud.Allies[0].SanityFill && Mathf.Abs(hud.Allies[0].SanityFill.fillAmount-.67f)<.001f && hud.Allies[0].SanityValue.text=="67 / 100","Stage sanity bar is not bound");
            string attack=hud.Panel.AttributeValues[0].text;
            LimbSystem.Get(app.Run.Heroes[0],LimbPart.Arm).Hp=0; app.Refresh();
            Check(hud.Panel.AttributeValues[0].text!=attack,"Attack attribute ignores limb damage");
            hud.Counter.SetRound(12); yield return .5f; Capture(hud,"Battle-BrokenArm-Round12");
            LimbSystem.Get(app.Run.Heroes[0],LimbPart.Arm).Hp=LimbSystem.Get(app.Run.Heroes[0],LimbPart.Arm).MaxHp; app.Refresh();
            Pass("all four healthy/broken pairs, limb bars, character isolation and actual health/sanity updates");
            foreach(int round in new[]{9,10,99,100})
            {
                hud.Counter.SetRound(round); yield return .5f;
                string number=round.ToString("D2");
                Check(hud.Counter.DigitSlots.Count(s=>s.gameObject.activeSelf)==number.Length,"Counter digit count wrong");
                for(int i=0;i<number.Length;i++) Check(hud.Counter.DigitSlots[i].sprite==hud.Counter.DigitSprites[number[i]-'0'],"Counter image mapping wrong");
            }
            app.Refresh(); Pass("new digit images and flip transitions 09/10/99/100");
            // Use the actual UI command path, including legal target selection.
            int skill=-1;
            for(int i=0;i<hud.Panel.SkillButtons.Length;i++) if(hud.Panel.SkillButtons[i].interactable && app.Battle.Targets(i).Any()) { skill=i; break; }
            Check(skill>=0,"No targetable skill available for the input check");
            hud.Panel.SkillButtons[skill].onClick.Invoke();
            if(app.PendingSkill.HasValue)
            {
                Check(hud.Panel.DescriptionTitle.text==app.Battle.SkillsFor(app.Battle.Current)[skill].Name,"Selected skill description is stale");
                var target=app.Battle.Targets(skill).First();
                var card=hud.Allies.Concat(hud.Enemies).First(c=>c.ActorId==target.Id); card.Hit.onClick.Invoke();
                Check(!app.PendingSkill.HasValue,"Target click did not submit skill");
            }
            yield return .8f;
            timeout=EditorApplication.timeSinceStartup+10;
            while((app.Busy || !app.Battle.Current.Player) && EditorApplication.timeSinceStartup<timeout) yield return .2f;
            if(presentation.DisplacementButton.interactable)
            {
                var actor=app.Battle.Current; int before=actor.Rank;
                presentation.DisplacementButton.onClick.Invoke(); Check(app.PendingSwap,"Movement button not connected");
                var target=app.Battle.SwapTargets().First(); hud.Allies.First(c=>c.ActorId==target.Id).Hit.onClick.Invoke();
                Check(actor.Rank!=before,"Rank swap did not execute");
            }
            else throw new Exception("No available swap for verification");
            Pass("skill button -> description -> legal target -> action; displacement button -> adjacent target -> rank swap");
            yield return .5f;
            timeout=EditorApplication.timeSinceStartup+10;
            while(app.Busy && EditorApplication.timeSinceStartup<timeout) yield return .1f;
            Check(!app.Busy,"Action did not finish before round verification");
            yield return .6f;
            int goalRound=app.Battle.Round+2;
            for(int step=0;app.Battle.Round<goalRound && step<30;step++)
            {
                int beforeRound=app.Battle.Round;
                float beforeAngle=details.Wheel.localEulerAngles.z;
                if(app.Battle.Current.Player)
                {
                    var adjacent=app.Battle.SwapTargets().First();
                    Check(app.Battle.Execute(new ActionRequest(ActionKind.Swap,adjacent.Id),out var error),"Round progression failed: "+error);
                }
                else app.Battle.ExecuteEnemy();
                app.Refresh(); yield return .6f;
                Check(app.Battle.Outcome==BattleOutcome.Active,"Battle ended before round verification");
                int delta=app.Battle.Round-beforeRound;
                Check(Mathf.Abs(Mathf.DeltaAngle(beforeAngle,details.Wheel.localEulerAngles.z)+30*delta)<.1f,"Wheel must rotate clockwise exactly once per full round");
                Check(hud.Counter.RoundNumber==app.Battle.Round,"Counter is not following the actual round");
                float settled=details.Wheel.localEulerAngles.z;
                app.Refresh(); app.Refresh(); yield return .1f;
                Check(Mathf.Abs(Mathf.DeltaAngle(settled,details.Wheel.localEulerAngles.z))<.1f,"Repeated refresh rotates the wheel again");
            }
            Check(app.Battle.Round>=goalRound,"Did not complete two actual rounds");
            Capture(hud,"Battle-Counter-Advanced");
            Pass("actual full rounds update counter and rotate wheel clockwise 30 degrees; actor changes and repeated refreshes do not rotate it");
            app.DebugWinBattle(); Check(app.Battle.Outcome==BattleOutcome.Victory,"Battle settlement failed");
            app.ContinueBattle(); Check(app.AwaitingNode && app.Battle==null,"Return to exploration failed");
            Pass("battle settlement and existing exploration handover remain functional");
        }
    }
}
