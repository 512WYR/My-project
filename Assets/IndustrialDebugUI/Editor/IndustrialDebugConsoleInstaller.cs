using System.IO;
using GameMake.IndustrialDebugUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameMake.IndustrialDebugUI.EditorTools
{
    /// <summary>
    /// Puts the authored console prefab into the entry scene. Installation is idempotent and runs once
    /// after a domain reload, so a fresh checkout only needs to open the project; the menus below are
    /// for re-running it by hand.
    /// </summary>
    public static class IndustrialDebugConsoleInstaller
    {
        private const string MainScenePath = "Assets/Scenes/mainScene.unity";
        private const string HudPrefabPath = "Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab";

        [MenuItem("GameMake/Debug Console/重建并安装到 mainScene")]
        public static void RebuildAndInstall()
        {
            IndustrialDebugConsoleBuilder.Build();
            InstallInScene(MainScenePath, false);
        }

        [MenuItem("GameMake/Debug Console/安装到 mainScene")]
        public static void InstallMainScene()
        {
            InstallInScene(MainScenePath, false);
        }

        [MenuItem("GameMake/Debug Console/修复 HUD 根缩放")]
        public static void FixHudScaleMenu() { FixHudScale(true); }

        [InitializeOnLoadMethod]
        private static void ScheduleAutoInstall()
        {
            EditorApplication.delayCall += TryAutoInstall;
        }

        private static void TryAutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // The one delayed call may land while the game is running; finish the job after play mode
                // instead of silently skipping until the next domain reload.
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(IndustrialDebugConsoleBuilder.PrefabPath)
                || EditorPrefs.GetInt(IndustrialDebugConsoleBuilder.BuildVersionKey, 0)
                    != IndustrialDebugConsoleBuilder.Version)
                IndustrialDebugConsoleBuilder.Build();
            InstallInScene(MainScenePath, true);
            FixHudScale(false);
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.delayCall += TryAutoInstall;
        }

        // Adds the console prefab as a root object of the scene when it is not there yet. A scene that
        // is not open is opened additively, saved and closed, so the user's current scene is untouched.
        private static void InstallInScene(string scenePath, bool quiet)
        {
            if (!File.Exists(scenePath))
            {
                if (!quiet) Debug.LogWarning("DEBUG_CONSOLE_INSTALL_SKIP missing scene " + scenePath);
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IndustrialDebugConsoleBuilder.PrefabPath);
            if (!prefab)
            {
                if (!quiet) Debug.LogWarning("DEBUG_CONSOLE_INSTALL_SKIP missing prefab. Run GameMake/Debug Console/重建调试台 Prefab.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            bool openedHere = false;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    if (!quiet) Debug.LogWarning("DEBUG_CONSOLE_INSTALL_SKIP cannot open " + scenePath);
                    return;
                }
                openedHere = true;
            }

            try
            {
                // A scene that was clean before this call can be saved safely: the only change is the
                // console. A scene that already had unsaved edits is left dirty and the log says so, so
                // installing never quietly writes somebody else's work in progress.
                bool wasDirty = scene.isDirty;
                if (HasConsole(scene))
                {
                    // The console was added in a previous editor session but the scene was never saved.
                    // Persist it now; the save carries the whole open scene, so no in-memory edit is lost.
                    if (!SceneFileHasConsole(scenePath))
                    {
                        EditorSceneManager.SaveScene(scene);
                        Debug.Log("DEBUG_CONSOLE_SCENE_SAVED " + scenePath);
                    }
                    else if (!quiet)
                    {
                        Debug.Log("DEBUG_CONSOLE_INSTALL_OK already present in " + scenePath);
                    }
                    return;
                }
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance != null)
                {
                    instance.name = "IndustrialDebugConsole";
                    // A stale instance override once wrote a zero scale, which hid the whole canvas; the
                    // console is also kept at the scene root so it is listed in SceneRoots.
                    instance.transform.SetParent(null);
                    SceneManager.MoveGameObjectToScene(instance, scene);
                    instance.transform.localScale = Vector3.one;
                }
                if (openedHere) EditorSceneManager.SaveScene(scene);
                else if (!wasDirty)
                {
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("DEBUG_CONSOLE_INSTALL_SAVED " + scenePath);
                }
                else
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.LogWarning("DEBUG_CONSOLE_INSTALL_DIRTY " + scenePath
                        + " - the scene had unsaved edits, so the console was only added in memory. Press Ctrl+S to save.");
                }
                Debug.Log("DEBUG_CONSOLE_INSTALL_OK " + scenePath);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool HasConsole(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<IndustrialDebugConsole>(true)) return true;
            // An older generated scene held the console without listing it in SceneRoots, so the root scan
            // above could miss the very instance that was already serialized.
            foreach (var console in Object.FindObjectsOfType<IndustrialDebugConsole>(true))
                if (console && console.gameObject.scene == scene) return true;
            return false;
        }

        // Checks the saved scene text for the prefab's GUID. The open scene can contain an unsaved
        // instance, which is exactly the case the install log needs to distinguish.
        private static bool SceneFileHasConsole(string scenePath)
        {
            if (!File.Exists(scenePath)) return false;
            string guid = AssetDatabase.AssetPathToGUID(IndustrialDebugConsoleBuilder.PrefabPath);
            if (string.IsNullOrEmpty(guid)) return false;
            return File.ReadAllText(scenePath).Contains(guid);
        }

        // The generated HUD prefab shipped with a zero scale on its root at one point, which makes the
        // whole battle page invisible. Repair it when found; the console is a separate canvas and is
        // unaffected either way.
        private static void FixHudScale(bool verbose)
        {
            if (!File.Exists(HudPrefabPath)) return;
            var contents = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            if (contents == null) return;
            bool changed = false;
            if (contents.transform.localScale == Vector3.zero)
            {
                contents.transform.localScale = Vector3.one;
                changed = true;
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, HudPrefabPath);
                Debug.Log("HUD_ROOT_SCALE_FIXED " + HudPrefabPath);
            }
            else if (verbose) Debug.Log("HUD_ROOT_SCALE_OK " + HudPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
