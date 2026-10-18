using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Core;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameMake.ExplorationEditor
{
    [InitializeOnLoad]
    public static class ExplorationEntryVerification
    {
        const string Key="Exploration.EntryVerification";
        static IEnumerator routine;
        static double resume;
        static string Report=>Path.Combine(ExplorationMapBuilder.Output,"entry-validation.txt");
        static ExplorationEntryVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{
                if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start();
            };
            if(EditorApplication.isPlaying && SessionState.GetBool(Key,false)) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            MapEntranceBuilder.Apply();
            Directory.CreateDirectory(ExplorationMapBuilder.Output);
            File.WriteAllText(Report,"Game entry map integration verification\n");
            Check(EditorBuildSettings.scenes.First(s=>s.enabled).path==ExplorationSceneIntegration.MainScene,"Wrong default startup scene");
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
            File.AppendAllText(Report,e==null?"PASS: all entry scenes and map/room/battle return flows\n":"FAIL: "+e+"\n");
            if(e!=null) Debug.LogException(e);
            EditorApplication.Exit(e==null?0:1);
        }
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        static IEnumerator Tests()
        {
            foreach(var path in ExplorationSceneIntegration.EntryScenes)
            {
                if(SceneManager.GetActiveScene().path!=path)
                {
                    var load=EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Single));
                    while(!load.isDone) yield return .1f;
                    yield return .8f;
                }
                var apps=UnityEngine.Object.FindObjectsOfType<StageOneApp>();
                var maps=UnityEngine.Object.FindObjectsOfType<ExplorationMapController>();
                var hud=UnityEngine.Object.FindObjectOfType<GothicBattleHudController>();
                Check(apps.Length==1 && maps.Length==1 && hud,"Missing or duplicate UI in "+path);
                var app=apps[0]; var map=maps[0];
                Check(map.Source==app && hud.BattleSource==app,"Mismatched game state in "+path);
                Check(map.Entrance && map.Entrance.MapPanel && map.Entrance.CounterPanel,"Missing map entrance in "+path);
                if(app.Battle==null)
                {
                    Check(map.Page.activeInHierarchy,"Map is not visible in "+path);
                    yield return .75f;
                    if(path==ExplorationSceneIntegration.MainScene)
                    {
                        var entrance=map.Entrance;
                        app.Refresh();
                        Check(entrance.IsRunning && !entrance.MapFade.blocksRaycasts,"Entry has no animation/input guard");
                        Check(entrance.MapPanel.anchoredPosition.x>entrance.MapHome.x+entrance.MapPanel.rect.width,"Map did not begin outside the right edge");
                        Check(entrance.CounterPanel.anchoredPosition.y>entrance.CounterPanel.rect.height,"Counter did not begin above the screen");
                        int entries=entrance.EntranceCount; var step=app.Map.Current;
                        map.Options[0].Hit.onClick.Invoke(); Check(app.Map.Current==step && app.Room==RoomView.Map,"Early click entered a room during slide");
                        app.Refresh(); Check(entrance.EntranceCount==entries,"Refresh restarted entrance");
                        float startX=entrance.MapPanel.anchoredPosition.x;
                        Time.timeScale=0; yield return .2f;
                        Check(entrance.IsRunning && entrance.MapPanel.anchoredPosition.x<startX && entrance.MapPanel.anchoredPosition.x>entrance.MapHome.x,"Unscaled slide does not progress");
                        ExplorationMapBuilder.Render(map.GetComponent<Canvas>(),Camera.main,"Map-Entrance-Moving",1920,1080);
                        double deadline=EditorApplication.timeSinceStartup+3;
                        while(entrance.IsRunning && EditorApplication.timeSinceStartup<deadline) yield return .1f;
                        Time.timeScale=1;
                        Check(!entrance.IsRunning && entrance.MapFade.blocksRaycasts && Vector2.Distance(entrance.MapPanel.anchoredPosition,entrance.MapHome)<.05f && Vector2.Distance(entrance.CounterPanel.anchoredPosition,entrance.CounterHome)<.05f,"Entrance does not finish at authored positions: running="+entrance.IsRunning+" map="+entrance.MapPanel.anchoredPosition+" home="+entrance.MapHome+" counter="+entrance.CounterPanel.anchoredPosition+" home="+entrance.CounterHome);
                        app.OpenCharacterPanel(0); app.CloseCharacterPanel(); app.Refresh();
                        Check(entrance.EntranceCount==entries,"Character popup/state refresh restarted map slide");
                        File.AppendAllText(Report,"PASS: right-edge map + top-edge counter, unscaled easing, delayed options, input guard, no refresh replay\n");
                        ExplorationMapBuilder.Render(map.GetComponent<Canvas>(),Camera.main,"MainScene-NewMap",1920,1080);
                    }
                    map.Options[0].Hit.onClick.Invoke();
                    Check(app.Room==RoomView.Buff && !map.Page.activeSelf,"Opening room handover failed");
                    app.ChooseBuff(app.Catalog.MapRules().OpeningBuffs[0]); app.LeaveRoom(); yield return .75f;
                    Check(app.AwaitingNode && app.Room==RoomView.Map && map.Page.activeInHierarchy,"Room did not return to new map");
                    app.Map.Current.Candidates.Clear();
                    app.Map.Current.Candidates.Add(new NodeState{Floor=app.Map.Current.Floor,Index=app.Map.Current.Index,Type=NodeType.Battle});
                    app.Refresh(); map.Options[0].Hit.onClick.Invoke(); yield return .3f;
                }
                Check(app.Battle!=null && !map.Page.activeSelf && hud.ChromeRoots.All(r=>r.activeInHierarchy),"New battle page not active in "+path);
                app.DebugWinBattle(); app.ContinueBattle();
                Check(map.Entrance.IsRunning,"Returning from battle did not start map entrance");
                yield return .75f;
                Check(app.Battle==null && map.Page.activeInHierarchy,"Battle did not return to the map in "+path);
                Check(hud.ChromeRoots.All(r=>!r.activeSelf),"Battle UI overlaps returned map");
                if(path==ExplorationSceneIntegration.MainScene)
                {
                    yield return .75f;
                    ExplorationMapBuilder.Render(map.GetComponent<Canvas>(),Camera.main,"MainScene-MapAfterBattle",1920,1080);
                }
                File.AppendAllText(Report,"PASS: "+path+" -> new map / room / battle / new map; shared state; old route hidden\n");
            }
        }
    }
}

