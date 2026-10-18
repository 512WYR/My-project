using System;
using System.Linq;
using GameMake.ExplorationUI;
using GameMake.GothicHUD;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameMake.ExplorationEditor
{
    public static class ExplorationSceneIntegration
    {
        public const string MainScene = "Assets/Scenes/mainScene.unity";
        public static readonly string[] EntryScenes = {
            MainScene, "Assets/StageOne/Scenes/StageOne.unity",
            ExplorationMapBuilder.ScenePath, "Assets/GothicHUD/Scenes/GothicBattleHUD.unity"
        };

        [MenuItem("GameMake/Map Exploration/Apply To Game Entry Scenes")]
        public static void Apply()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var paths=EntryScenes.AsEnumerable();
            const string eventScene="Assets/IndustrialEventUI/Scenes/IndustrialEvent.unity";
            if(System.IO.File.Exists(eventScene)) paths=paths.Concat(new[]{eventScene});
            foreach(var path in paths)
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                AttachToCurrentScene();
                EditorSceneManager.SaveScene(scene);
            }
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(MainScene,true)}
                .Concat(EditorBuildSettings.scenes.Where(s=>s.path!=MainScene)).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MainScene,OpenSceneMode.Single);
            Debug.Log("EXPLORATION_ENTRY_INTEGRATION_OK");
        }

        public static void AttachToCurrentScene()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var apps=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<StageOneApp>(true)).ToArray();
            if(apps.Length!=1) throw new InvalidOperationException("Expected one game state in "+scene.path);
            var app=apps[0];
            var hud=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GothicBattleHudController>(true)).SingleOrDefault();
            if(!hud)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab");
                hud=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<GothicBattleHudController>();
            }
            var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ExplorationMapController>(true)).SingleOrDefault();
            if(!view)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ExplorationMapBuilder.PrefabPath);
                view=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<ExplorationMapController>();
            }
            app.ExternalBattleUi=true; Save(app);
            view.Source=app; Save(view);
            hud.BattleSource=app; Save(hud);
            var eventPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IndustrialEventUI/Prefabs/IndustrialEventPage.prefab");
            if(eventPrefab)
            {
                var eventView=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameMake.EventUI.IndustrialEventPage>(true)).SingleOrDefault();
                if(!eventView) eventView=((GameObject)PrefabUtility.InstantiatePrefab(eventPrefab,scene)).GetComponent<GameMake.EventUI.IndustrialEventPage>();
                eventView.Source=app; Save(eventView);
            }
            var presentation=hud.GetComponent<ModularHudController>();
            var characterPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterPanelUI/Prefabs/CharacterPanel.prefab");
            if(characterPrefab)
            {
                var characterView=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GameMake.CharacterUI.CharacterPanelView>(true)).SingleOrDefault();
                if(!characterView) characterView=((GameObject)PrefabUtility.InstantiatePrefab(characterPrefab,scene)).GetComponent<GameMake.CharacterUI.CharacterPanelView>();
                characterView.Source=app; Save(characterView);
            }
            presentation.BattleSource=app; presentation.FollowStageOneBattle=false; Save(presentation);
            foreach(var counter in hud.GetComponentsInChildren<MechanicalRoundCounter>(true))
            { counter.BattleSource=app; counter.FollowStageOneBattle=false; Save(counter); }
            if(!scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventSystem>(true)).Any())
                new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void Save(UnityEngine.Object component)
        {
            EditorUtility.SetDirty(component);
            if(PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
