using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameMake.ExplorationUI;
using Ruin.Core;
using Ruin.Runtime;
using UnityEditor;
using UnityEngine;

namespace GameMake.ExplorationEditor
{
    [InitializeOnLoad]
    public static class ExplorationMapVerification
    {
        const string Key="MapExploration.Verification";
        static IEnumerator routine;
        static double resume;
        static ExplorationMapVerification()
        {
            EditorApplication.playModeStateChanged += state =>
            { if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start(); };
            if(SessionState.GetBool(Key,false) && EditorApplication.isPlaying) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            ExplorationMapBuilder.Build();
            File.WriteAllText(Path.Combine(ExplorationMapBuilder.Output,"validation.txt"),"Unity map exploration verification\n");
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
                if(!routine.MoveNext()) { Finish(true,null); return; }
                resume=EditorApplication.timeSinceStartup+(routine.Current is float seconds?seconds:.1f);
            }
            catch(Exception ex) { Finish(false,ex); }
        }
        static void Finish(bool success,Exception ex)
        {
            EditorApplication.update-=Tick; routine=null; SessionState.SetBool(Key,false);
            File.AppendAllText(Path.Combine(ExplorationMapBuilder.Output,"validation.txt"),success?"PASS: all Unity Play Mode checks completed\n":"FAIL: "+ex+"\n");
            if(ex!=null) Debug.LogException(ex);
            Debug.Log(success?"MAP_EXPLORATION_PLAYMODE_PASS":"MAP_EXPLORATION_PLAYMODE_FAIL");
            EditorApplication.Exit(success?0:1);
        }
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        static void Digits(ExplorationCounter counter,string floor,string room)
        {
            foreach(var pair in new[]{Tuple.Create(counter.FloorSlots,floor),Tuple.Create(counter.RoomSlots,room)})
            {
                Check(pair.Item1.Count(i=>i.gameObject.activeSelf)==pair.Item2.Length,"Wrong digit count "+pair.Item2);
                for(int i=0;i<pair.Item2.Length;i++) Check(pair.Item1[i].sprite==counter.Digits[pair.Item2[i]-'0'],"Wrong digit sprite "+pair.Item2);
            }
        }
        static void Pass(string text) { File.AppendAllText(Path.Combine(ExplorationMapBuilder.Output,"validation.txt"),"PASS: "+text+"\n"); }
        static IEnumerator Tests()
        {
            var view=UnityEngine.Object.FindObjectOfType<ExplorationMapController>(); var app=view.Source;
            Check(app && app.Run!=null && view.Page.activeInHierarchy,"New scene did not start on live map");
            Digits(view.Counter,"01","01");
            Check(view.Options.Count(o=>o.gameObject.activeSelf)==1,"Opening room should have exactly one choice");
            view.Options[0].Hit.onClick.Invoke();
            Check(app.Room==RoomView.Buff && !view.Page.activeSelf,"Choice did not enter the existing buff room");
            app.ChooseBuff(app.Catalog.MapRules().OpeningBuffs[0]); app.LeaveRoom(); yield return .4f;
            Check(app.Room==RoomView.Map && view.Page.activeInHierarchy,"Could not return from room to new map");
            Digits(view.Counter,"01","02");
            var frozen=app.Map.Current; var first=frozen.Candidates[0]; app.Refresh(); app.Refresh();
            Check(ReferenceEquals(frozen,app.Map.Current) && ReferenceEquals(first,app.Map.Current.Candidates[0]),"UI refresh rerolled candidates");
            Pass("live opening choice -> buff room -> next room; floor/room binding; refresh does not reroll");

            int selected=-1; view.OnCharacterSelected.AddListener(i=>selected=i); view.Party[2].Hit.onClick.Invoke();
            Check(selected==2 && view.Party[2].Selection.enabled && !view.Party[0].Selection.enabled,"Party button selection failed");
            app.Run.Heroes[2].Hp=11; app.Run.Heroes[2].Sanity=72; app.Refresh();
            Check(Mathf.Abs(view.Party[2].Health.fillAmount-11f/app.Run.Heroes[2].Definition.MaxHp)<.001f && Mathf.Abs(view.Party[2].Sanity.fillAmount-.72f)<.001f,"Live party vitals failed");
            Pass("four character slots, selection event and actual health/sanity updates");

            for(int count=1;count<=3;count++)
            {
                var map=new MapState(); var step=new StepState{Floor=1,Index=3,Shown=true};
                for(int i=0;i<count;i++) step.Candidates.Add(new NodeState{Floor=1,Index=3,OptionIndex=i,Type=(NodeType)i});
                for(int i=0;i<3;i++) map.Steps.Add(new StepState{Floor=1,Index=i,Resolved=true,ChosenIndex=0,Candidates={new NodeState{Type=i==0?NodeType.Opening:NodeType.Battle}}});
                map.Steps.Add(step); map.StepIndex=3;
                view.RenderMap(map,app.Catalog.MapRules());
                double timeout=EditorApplication.timeSinceStartup+3;
                do { yield return .1f; } while(view.ChoiceFade.alpha<1 && EditorApplication.timeSinceStartup<timeout);
                Check(view.Options.Count(o=>o.gameObject.activeSelf)==count,"Incorrect popup count "+count);
                Check(view.ChoiceFade.alpha==1 && Vector2.Distance(view.Choices.anchoredPosition,new Vector2(60,-790))<.01f,"Popup animation did not finish; alpha="+view.ChoiceFade.alpha+", position="+view.Choices.anchoredPosition);
                for(int i=0;i<count;i++) Check(view.Options[i].CandidateIndex==i && view.Options[i].Title.text==step.Candidates[i].Name,"Wrong room mapping");
                ExplorationMapBuilder.Render(view.GetComponent<Canvas>(),Camera.main,"MapExploration-"+count+"-Choices",1920,1080);
                if(count==3) ExplorationMapBuilder.Render(view.GetComponent<Canvas>(),Camera.main,"MapExploration-2560x1080",2560,1080);
                step.Resolved=true; view.RenderMap(map,app.Catalog.MapRules());
                Check(view.Options.All(o=>!o.gameObject.activeSelf),"Resolved room retained stale choices");
            }
            Pass("one/two/three popup cards, zero after resolution, correct titles, completed animation; native 1080p and ultrawide captures");
            foreach(int number in new[]{9,10,99,100})
            { view.Counter.SetLocation(number,number); yield return .3f; Digits(view.Counter,number.ToString("D2"),number.ToString("D2")); }
            view.Counter.SetLocation(2,9); yield return .03f; view.Counter.SetLocation(3,10); yield return .3f; Digits(view.Counter,"03","10");
            view.Counter.SetLocation(4,11); yield return .03f; view.Counter.gameObject.SetActive(false); view.Counter.SetLocation(5,12); view.Counter.gameObject.SetActive(true); yield return .3f; Digits(view.Counter,"05","12");
            Check(view.Counter.FloorWindow.localScale==Vector3.one && view.Counter.RoomWindow.localScale==Vector3.one,"Counter left collapsed after reenable");
            Pass("digit images 01/09/10/99/100, rapid changes and disable/enable");

            app.Map.StepIndex=MapGenerator.StepsOnFloor(app.Catalog.MapRules(),1)-1;
            MapGenerator.Advance(app.Map,app.Catalog.MapRules(),app.Run.Random,app.Run.PlayerSanity); app.Refresh(); yield return .3f;
            Check(app.Map.Current.Candidates[0].Type==NodeType.Elevator,"Missing floor elevator");
            view.Options[0].Hit.onClick.Invoke(); Check(app.Room==RoomView.Elevator,"Could not enter elevator");
            app.AdvanceFloor(); yield return .4f; Digits(view.Counter,"02","01");
            Check(view.Page.activeSelf && app.Map.Current.Floor==2 && app.Map.Current.Index==0,"Floor transition failed");
            view.Options[0].Hit.onClick.Invoke(); Check(app.Room==RoomView.Supply,"Floor two supply room not connected");
            app.LeaveRoom(); yield return .3f;
            app.Map.Current.Candidates.Clear(); app.Map.Current.Candidates.Add(new NodeState{Floor=2,Index=1,Type=NodeType.Battle}); app.Refresh();
            view.Options[0].Hit.onClick.Invoke();
            Check(app.Battle!=null && !app.AwaitingNode && !view.Page.activeSelf,"Battle option did not hand over to combat UI");
            Pass("real elevator -> floor 02 / room 01; supply room return; battle handover to existing HUD");

            foreach(string path in Directory.GetFiles(ExplorationMapBuilder.Root+"/Art","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                Check(!importer.mipmapEnabled && importer.textureCompression==TextureImporterCompression.Uncompressed,"Texture quality configuration wrong: "+path);
            }
            Check(view.GetComponent<Canvas>().pixelPerfect,"Canvas pixel alignment disabled");
            Pass("original PNG assets imported without compression or mipmaps; pixel-perfect UI canvas");
        }
    }
}
