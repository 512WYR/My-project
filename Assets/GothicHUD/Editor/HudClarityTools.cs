using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUDEditor
{
    [InitializeOnLoad]
    public static class HudClarityTools
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static readonly string Request = Path.GetFullPath("Library/GothicHUD-Clarity.request");
        static readonly string Report = Path.GetFullPath("../交付/哥特HUD/新版/clarity-report.txt");
        static HudClarityTools()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += ApplyRequested;
        }
        static void ApplyRequested()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ApplyRequested; return;
            }
            try
            {
                Apply(); File.Delete(Request);
            }
            catch (Exception e) { File.WriteAllText(Report, "FAILED: " + e); Debug.LogException(e); File.Delete(Request); }
        }
        [MenuItem("GameMake/Gothic HUD/Clear Preview - 1080p")]
        public static void Apply()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            var log = new System.Text.StringBuilder();
            string prefabPath = ModularHudBuilder.PrefabPath;
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var canvas = contents.GetComponent<Canvas>(); canvas.pixelPerfect = true;
                int changed = 0;
                foreach (var text in contents.GetComponentsInChildren<Text>(true))
                {
                    if (text.fontSize >= 14) continue;
                    text.fontSize = 14; changed++;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                log.AppendLine("Canvas pixel-perfect: enabled; minimum font size: 14; adjusted labels: " + changed);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            // Preserve other open scenes and unsaved work; only update HUD presentation properties.
            foreach (var hud in UnityEngine.Object.FindObjectsOfType<GameMake.GothicHUD.ModularHudController>(true))
            {
                Undo.RecordObject(hud.GetComponent<Canvas>(), "HUD pixel alignment");
                hud.GetComponent<Canvas>().pixelPerfect = true;
                foreach (var text in hud.GetComponentsInChildren<Text>(true))
                    if (text.fontSize < 14) { Undo.RecordObject(text,"HUD readable type size"); text.fontSize=14; }
            }
            AssetDatabase.SaveAssets();
            ConfigureGameView(log);
            File.WriteAllText(Report, log.ToString());
            Debug.Log("HUD_CLARITY_APPLIED\n" + log);
        }
        static void ConfigureGameView(System.Text.StringBuilder log)
        {
            var assembly = typeof(EditorWindow).Assembly;
            var gameViewType = assembly.GetType("UnityEditor.GameView", true);
            var view = EditorWindow.GetWindow(gameViewType);
            var lowResolution = gameViewType.GetProperty("lowResolutionForAspectRatios", Flags);
            if (lowResolution == null || !lowResolution.CanWrite) throw new MissingMemberException("GameView.lowResolutionForAspectRatios");
            log.AppendLine("Previous low-resolution preview: " + lowResolution.GetValue(view));
            lowResolution.SetValue(view, false);

            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", Flags).GetValue(null);
            var group = sizesType.GetProperty("currentGroup", Flags).GetValue(sizes);
            var groupType = group.GetType();
            int builtin = (int)groupType.GetMethod("GetBuiltinCount", Flags).Invoke(group,null);
            int custom = (int)groupType.GetMethod("GetCustomCount", Flags).Invoke(group,null);
            var get = groupType.GetMethod("GetGameViewSize", Flags);
            int selected = -1;
            for(int i=0;i<builtin+custom;i++)
            {
                var size = get.Invoke(group,new object[]{i}); var type=size.GetType();
                if((int)type.GetProperty("width",Flags).GetValue(size)==1920 && (int)type.GetProperty("height",Flags).GetValue(size)==1080) { selected=i; break; }
            }
            if(selected<0)
            {
                var sizeType=assembly.GetType("UnityEditor.GameViewSize",true);
                var kind=assembly.GetType("UnityEditor.GameViewSizeType",true);
                var size=Activator.CreateInstance(sizeType,Flags,null,new object[]{Enum.Parse(kind,"FixedResolution"),1920,1080,"HUD 1920 x 1080"},null);
                groupType.GetMethod("AddCustomSize",Flags).Invoke(group,new[]{size}); selected=builtin+custom;
            }
            gameViewType.GetProperty("selectedSizeIndex",Flags).SetValue(view,selected);
            var zoom=gameViewType.GetField("m_ZoomArea",Flags).GetValue(view);
            var scale=zoom.GetType().GetProperty("scale",Flags);
            log.AppendLine("Previous zoom: " + scale.GetValue(zoom));
            scale.SetValue(zoom,Vector2.one);
            view.Repaint();
            log.AppendLine("Game View: fixed 1920 x 1080; low-resolution disabled; zoom reset to 1.");
            log.AppendLine("For judging clarity, inspect at 1:1; a small docked window may only show part of the frame.");
        }
    }
}
