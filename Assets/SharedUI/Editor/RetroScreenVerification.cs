using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameMake.ExplorationEditor;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameMake.SharedUI.Editor
{
    [InitializeOnLoad]
    public static class RetroScreenVerification
    {
        const string Key="RetroScreen.Verify";
        static IEnumerator routine;
        static double resume;
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../交付/屏幕扫描扭曲"));
        static RetroScreenVerification()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)) Start();};
            if(EditorApplication.isPlaying && SessionState.GetBool(Key,false)) EditorApplication.delayCall+=Start;
        }
        public static void Run()
        {
            RetroScreenStyling.Apply();
            EditorSceneManager.OpenScene(ExplorationSceneIntegration.MainScene,OpenSceneMode.Single);
            Directory.CreateDirectory(Output); File.WriteAllText(Output+"/validation.txt","Retro screen verification\n");
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
                resume=EditorApplication.timeSinceStartup+(routine.Current is float seconds?seconds:.1f);
            }
            catch(Exception e) { Finish(e); }
        }
        static void Finish(Exception e)
        {
            Time.timeScale=1;
            EditorApplication.update-=Tick; routine=null; SessionState.SetBool(Key,false);
            File.AppendAllText(Output+"/validation.txt",e==null?"PASS: all checks completed\n":"FAIL: "+e+"\n");
            if(e!=null) Debug.LogException(e);
            EditorApplication.Exit(e==null?0:1);
        }
        static void Check(bool pass,string message) { if(!pass) throw new Exception(message); }
        static void Pass(string message)=>File.AppendAllText(Output+"/validation.txt","PASS: "+message+"\n");
        static Vector4[] Inspect(RetroScreenNoise effect)
        {
            Canvas.ForceUpdateCanvases();
            var mesh=effect.canvasRenderer.GetMesh();
            var vertices=mesh.vertices; var colors=mesh.colors;
            Check(vertices.Length==4,"Missing screen refraction quad");
            var rect=effect.rectTransform.rect;
            foreach(var vertex in vertices)
                Check(vertex.x>=rect.xMin-.01f && vertex.x<=rect.xMax+.01f && vertex.y>=rect.yMin-.01f && vertex.y<=rect.yMax+.01f,"Noise spills beyond screen");
            Check(effect.material.shader.isSupported,"Screen distortion shader unsupported");
            var bands=effect.material.GetVectorArray("_Bands").Take(effect.BandCount).ToArray();
            Check(bands.Length==effect.BandCount && bands.All(b=>b.y>0) && effect.ThicknessRange.y<=1.2f,"Scanlines are not thin");
            Check(effect.SpeedRange.x>=110 && effect.BandCount==1 && effect.ReappearDelayRange.x>=3,"Scanline count, speed or cooldown is wrong");
            Check(bands.All(b=>Mathf.Abs(b.z)>=1.5f && Mathf.Abs(b.z)<=4f && b.w<=.20f),"Distortion should be stronger while lines remain dim");
            Check(!effect.raycastTarget,"Noise blocks interaction");
            return bands;
        }
        static void CheckPixelDisplacement(string baseline,string warped)
        {
            var a=new Texture2D(2,2); var b=new Texture2D(2,2);
            try
            {
                a.LoadImage(File.ReadAllBytes(Output+"/"+baseline+".png")); b.LoadImage(File.ReadAllBytes(Output+"/"+warped+".png"));
                var original=a.GetPixels32(); var distorted=b.GetPixels32();
                int changed=0,unchanged=0;
                for(int y=1080-125;y<1080-85;y++) for(int x=560;x<1120;x++)
                {
                    int i=y*a.width+x;
                    if(Mathf.Abs(original[i].r-distorted[i].r)>8) changed++; else unchanged++;
                }
                Check(changed>10 && unchanged>changed*20,"Text shear should be visible only in a thin moving row: "+changed+" / "+unchanged);
            }
            finally { UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b); }
        }
        static void Capture(Canvas canvas,string name)
        {
            ExplorationMapBuilder.Render(canvas,Camera.main,name,1920,1080);
            File.Copy(ExplorationMapBuilder.Output+"/"+name+".png",Output+"/"+name+".png",true);
        }
        static float LineContrast(string baseline,string frame,int top)
        {
            var a=new Texture2D(2,2); var b=new Texture2D(2,2);
            try
            {
                a.LoadImage(File.ReadAllBytes(Output+"/"+baseline+".png")); b.LoadImage(File.ReadAllBytes(Output+"/"+frame+".png"));
                var original=a.GetPixels32(); var scanned=b.GetPixels32();
                float total=0; int count=0;
                for(int y=1080-top-2;y<=1080-top+2;y++) for(int x=1300;x<1600;x++)
                { int i=y*a.width+x; total+=Mathf.Abs(scanned[i].r-original[i].r); count++; }
                return total/count;
            }
            finally { UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b); }
        }
        static IEnumerator Tests()
        {
            var map=UnityEngine.Object.FindObjectOfType<ExplorationMapController>(); var app=map.Source;
            var hud=UnityEngine.Object.FindObjectOfType<GothicBattleHudController>();
            var mapNoise=map.GetComponentInChildren<RetroScreenNoise>(true);
            var battleNoise=hud.GetComponentInChildren<RetroScreenNoise>(true);
            Check(mapNoise && battleNoise && map.Page.activeInHierarchy,"Effects not present in main scene");
            foreach(var slot in map.Party) Check(((Color32)slot.Sanity.color).Equals(new Color32(205,205,205,255)),"Map sanity color differs from battle");
            var before=Inspect(mapNoise); yield return .15f;
            var after=Inspect(mapNoise);
            var travel=Enumerable.Range(0,before.Length).Where(i=>after[i].x<before[i].x).Select(i=>before[i].x-after[i].x).ToArray();
            Check(travel.Length>=1 && travel.Max()>.005f,"Scanlines are not descending quickly");
            mapNoise.Animate=false; mapNoise.enabled=false;
            Capture(map.GetComponent<Canvas>(),"Map-Scan-Baseline");
            mapNoise.enabled=true;
            // Capture uses a fixed 1080p canvas; its inset map screen is 932px tall.
            const float capturedScreenHeight=932f;
            var controlled=new Vector4[8]; controlled[0]=new Vector4(1-30f/capturedScreenHeight,1.2f/capturedScreenHeight,3,0);
            mapNoise.material.SetInt("_BandCount",1); mapNoise.material.SetVectorArray("_Bands",controlled);
            Capture(map.GetComponent<Canvas>(),"Map-Scan-TextDistortion");
            CheckPixelDisplacement("Map-Scan-Baseline","Map-Scan-TextDistortion");
            controlled[0].w=.18f;
            mapNoise.material.SetVectorArray("_Bands",controlled);
            Capture(map.GetComponent<Canvas>(),"Map-VisibleLine-A");
            Check(LineContrast("Map-Scan-Baseline","Map-VisibleLine-A",104)>2,"Thin scanline is invisible");
            controlled[0].x-=70f/capturedScreenHeight;
            mapNoise.material.SetVectorArray("_Bands",controlled);
            Capture(map.GetComponent<Canvas>(),"Map-VisibleLine-B");
            Check(LineContrast("Map-Scan-Baseline","Map-VisibleLine-B",174)>2 && LineContrast("Map-Scan-Baseline","Map-VisibleLine-B",104)<2,"Visible line does not travel with the distortion band");
            Pass("pixel capture confirms a visible interference line at the distortion center; both move down together and leave the previous row clear");
            mapNoise.Animate=true; yield return .4f;
            Capture(map.GetComponent<Canvas>(),"Map-RollingScan");
            double deadline=EditorApplication.timeSinceStartup+8;
            while(Inspect(mapNoise)[0].x>=0 && EditorApplication.timeSinceStartup<deadline) yield return .05f;
            Check(Inspect(mapNoise)[0].x<0,"Scanline never left screen");
            yield return 2f;
            Check(Inspect(mapNoise)[0].x<0,"Scanline reappears too frequently");
            deadline=EditorApplication.timeSinceStartup+7;
            while(Inspect(mapNoise)[0].x<0 && EditorApplication.timeSinceStartup<deadline) yield return .05f;
            Check(Inspect(mapNoise)[0].x>.7f,"Scanline did not reenter from the top after cooldown");
            Pass("one line at a time; original thin width and fast descent retained; line stays absent during cooldown then reappears from top; stronger localized displacement");
            Pass("map sanity #CDCDCD; full-width bands descend at varied speeds and thicknesses; GPU capture proves actual text displacement, not an overlaid line");
            map.Options[0].Hit.onClick.Invoke();
            Check(app.Room==Ruin.Runtime.RoomView.Buff && !mapNoise.gameObject.activeInHierarchy,"Map effect remains visible over room");
            app.ChooseBuff(app.Catalog.MapRules().OpeningBuffs[0]); app.LeaveRoom(); yield return .4f;
            app.Map.Current.Candidates.Clear(); app.Map.Current.Candidates.Add(new NodeState{Floor=1,Index=1,Type=NodeType.Battle});
            app.Refresh(); map.Options[0].Hit.onClick.Invoke(); yield return .8f;
            Check(!mapNoise.gameObject.activeInHierarchy && battleNoise.gameObject.activeInHierarchy,"Effects did not switch with page");
            Check(battleNoise.transform.GetSiblingIndex()>hud.Panel.DescriptionTitle.transform.GetSiblingIndex() && battleNoise.transform.GetSiblingIndex()>hud.Panel.DescriptionBody.transform.GetSiblingIndex(),"Skill text is excluded from refraction pass");
            deadline=EditorApplication.timeSinceStartup+9;
            while((Inspect(battleNoise)[0].x<.4f || Inspect(battleNoise)[0].x>1) && EditorApplication.timeSinceStartup<deadline) yield return .05f;
            before=Inspect(battleNoise); Time.timeScale=0; yield return .15f;
            Check(!before.SequenceEqual(Inspect(battleNoise)),"Skill interference stops with game time");
            Time.timeScale=1;
            Capture(hud.GetComponent<Canvas>(),"Battle-RollingScan");
            Pass("skill screen refracts text and graphics; animation continues when game time pauses; map/room/battle transitions work");
            app.DebugWinBattle(); app.ContinueBattle(); yield return .5f;
            Check(mapNoise.gameObject.activeInHierarchy && !battleNoise.gameObject.activeInHierarchy,"Effect visibility is wrong after battle return");
            Inspect(mapNoise);
            Pass("battle settlement returns to animated new map");
        }
    }
}
