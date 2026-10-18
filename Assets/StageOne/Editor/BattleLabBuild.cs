using System.IO;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ruin.Editor
{
    // Battle Lab is a debugging scene. It is deliberately never added to EditorBuildSettings: the normal
    // release entry stays StageOne.unity, and this menu only opens / validates / regenerates the lab.
    public static class BattleLabBuild
    {
        public const string ScenePath = "Assets/StageOne/Scenes/BattleLab.unity";
        public const string AppScriptPath = "Assets/StageOne/Runtime/BattleLab/BattleLabApp.cs";
        private const string CatalogPath = "Assets/StageOne/Resources/StageOne/GameCatalog.asset";

        [MenuItem("GameMake/Battle Lab/Open Battle Lab", false, 40)]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog("Battle Lab", "场景不存在，是否立即生成？", "生成", "取消")) return;
                Regenerate();
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Battle Lab: 已打开 " + ScenePath + "（调试场景，未加入 Build Settings）。");
        }

        [MenuItem("GameMake/Battle Lab/Validate Setup", false, 41)]
        public static void Validate()
        {
            bool ok = true;
            ok &= Require(File.Exists(ScenePath), "存在场景 " + ScenePath);
            ok &= Require(File.Exists(AppScriptPath), "存在入口脚本 " + AppScriptPath);
            ok &= Require(File.Exists(CatalogPath), "存在 GameCatalog " + CatalogPath);
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            ok &= Require(catalog != null, "GameCatalog 能作为 GameCatalog 加载");
            if (catalog != null)
            {
                ok &= Require(catalog.Heroes != null && catalog.Heroes.Length > 0, "GameCatalog 至少有一名英雄");
                ok &= Require(catalog.Monsters != null && catalog.Monsters.Length > 0, "GameCatalog 有怪物表");
            }
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            ok &= Require(sceneAsset != null, "Unity 能导入场景资产");
            if (ok) Debug.Log("Battle Lab: 校验通过。打开 " + ScenePath + " 后点击 Play。");
            else Debug.LogError("Battle Lab: 校验失败，见上面的缺项。");
        }

        [MenuItem("GameMake/Battle Lab/Regenerate Scene", false, 42)]
        public static void Regenerate()
        {
            Directory.CreateDirectory("Assets/StageOne/Scenes");
            AssetDatabase.Refresh();

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.1f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            var labObject = new GameObject("BattleLab");
            var app = labObject.AddComponent<BattleLabApp>();
            // Serializing the catalog is a convenience; BattleLabApp.Awake also falls back to Resources.Load.
            app.Catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Battle Lab: 保存场景失败 " + ScenePath);
                return;
            }
            AssetDatabase.Refresh();
            Debug.Log("Battle Lab: 已生成 " + ScenePath + "（未加入 Build Settings）。");
        }

        private static bool Require(bool condition, string message)
        {
            if (condition) Debug.Log("Battle Lab: OK  " + message);
            else Debug.LogError("Battle Lab: 缺少 " + message);
            return condition;
        }
    }
}
