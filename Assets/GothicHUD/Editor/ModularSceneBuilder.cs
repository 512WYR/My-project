using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using Ruin.Core;
using Ruin.Editor;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameMake.GothicHUDEditor
{
    /// <summary>
    /// Builds the scene where the Gothic HUD owns every page. The built-in pages stay exactly as they are
    /// in StageOne.unity; this one turns StageOneApp.ExternalBattleUi on so StageOneView keeps only the
    /// settlement, help and lab modals, while the HUD draws the battle page, the route and the six rooms.
    /// </summary>
    public static class ModularSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/mainScene.unity";
        const string CatalogPath = "Assets/StageOne/Resources/StageOne/GameCatalog.asset";

        [MenuItem("GameMake/Gothic HUD/Build mainScene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Order matters: Prepare() rewrites EditorBuildSettings.scenes wholesale, so it has to run before
            // mainScene is appended. Running it afterwards would drop the new scene from the build list again.
            StageOneBuild.Prepare();

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            if (!catalog) { Debug.LogError("GameCatalog missing after Prepare: " + CatalogPath); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModularHudBuilder.PrefabPath);
            if (!prefab) { Debug.LogError("Build the HUD first: GameMake/Gothic HUD/Rebuild Modular HUD"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.03f, .04f, .05f, 1);
            cameraObject.tag = "MainCamera"; cameraObject.transform.position = new Vector3(0, 0, -10);
            cameraObject.AddComponent<AudioListener>();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var app = new GameObject("StageOne").AddComponent<StageOneApp>();
            app.Catalog = catalog;
            app.ExternalBattleUi = true;

            var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            hud.name = "GothicBattleHUD";
            // The instance carries its own transform overrides, so a stale prefab would leave the whole HUD
            // scaled to zero and invisible in the scene even after the prefab itself is fixed.
            hud.transform.localScale = Vector3.one;

            // One writer per widget. Every controller that could also follow the battle has that path turned
            // off, so nothing fights GothicBattleHudController over the same fields, and each is pointed at
            // this scene's app instead of relying on its FindObjectOfType fallback.
            var presentation = hud.GetComponent<ModularHudController>();
            if (presentation) { presentation.FollowStageOneBattle = false; presentation.BattleSource = app; }
            var counter = hud.GetComponentInChildren<MechanicalRoundCounter>(true);
            if (counter) { counter.FollowStageOneBattle = false; counter.BattleSource = app; }
            var chrome = hud.GetComponent<GothicBattleHudController>();
            if (chrome) chrome.BattleSource = app;
            else Debug.LogError("The HUD prefab has no GothicBattleHudController, so nothing will feed the " +
                "battle into it. Run GameMake/Gothic HUD/Rebuild Modular HUD first, then build this scene again.");

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            // Append rather than replace: StageOne.unity has to stay in the build list.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("GOTHIC_MAIN_SCENE_OK " + ScenePath);
        }
    }
}
