using System;
using System.IO;
using System.Linq;
using Ruin.Core;
using Ruin.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ruin.Editor
{
    public static class StageOneBuild
    {
        public const string ScenePath = "Assets/StageOne/Scenes/StageOne.unity";
        private const string Data = "Assets/StageOne/Resources/StageOne";

        [MenuItem("GameMake/Prepare Stage One")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Data + "/Skills");
            Directory.CreateDirectory(Data + "/Characters");
            Directory.CreateDirectory(Data + "/MentalStates");
            Directory.CreateDirectory("Assets/StageOne/Scenes");
            AssetDatabase.Refresh();
            var guard = Character("guard", "护卫", "守住队伍的第一道防线", HeroRole.Guard, 42, 4, 0,
                Skill("shield_strike", "盾击", "近战攻击前排", 0.8, 3, 3),
                Skill("cover", "掩护", "拦截一次攻击 · 减半伤害", 0, 15, 15, SkillEffect.Protect, TargetSide.Ally),
                Skill("tactical_bash", "战术盾击", "攻击前排 · 打断下一次行动", 0.8, 3, 3, interrupt: true),
                Skill("body_slam", "扑撞", "重击前排 · 命中较低", 1.3, 15, 3, accuracy: 0.82));
            var scout = Character("scout", "侦察者", "在危险之前找到破绽", HeroRole.Scout, 32, 8, -10,
                Skill("dagger", "短刃突刺", "攻击前三排", 0.7, 7, 7),
                Skill("cut", "割裂", "攻击前三排 · 附加流血", 0.4, 7, 7, bleed: 2),
                Skill("anatomy", "解剖打击", "攻击前三排 · 流血更强", 1.0, 7, 15, bleed: 3),
                Skill("rend", "撕扯", "重击前排 · 命中较低", 1.2, 15, 3, accuracy: 0.82));
            var arbalist = Character("arbalist", "弩手", "距离、准星与一次呼吸", HeroRole.Arbalist, 30, 6, 10,
                Skill("bolt", "射击", "攻击中后排", 0.9, 14, 14),
                Skill("suppress", "压制射击", "打断 · 伤害较低", 0.5, 15, 15, uses: 3, interrupt: true),
                Skill("heavy_bolt", "强弩射击", "打断 · 攻击任意敌人", 1.2, 14, 15, interrupt: true),
                Skill("throw_bolt", "投掷弩矢", "重击前排 · 命中较低", 1.5, 15, 3, accuracy: 0.80));
            var medic = Character("medic", "医师", "让同伴撑过下一次心跳", HeroRole.Medic, 28, 5, 15,
                Skill("scalpel", "手术刀投掷", "攻击任意敌人", 0.5, 14, 15),
                Skill("bandage", "战地包扎", "恢复生命 · 可救治濒死", 0.9, 15, 15, SkillEffect.Heal, TargetSide.Ally, uses: 3),
                Skill("precise_treatment", "精密救治", "恢复更高生命 · 清除流血", 1.3, 15, 15, SkillEffect.Heal, TargetSide.Ally, cleanse: true),
                Skill("desperate_cut", "本能切割", "重击前排 · 命中较低", 0.9, 15, 3, accuracy: 0.84));
            var cleaver = Enemy("cleaver", "执斧者", "重劈前排的废墟守卫", 32, 3, EnemyPattern.Cleaver);
            var whisperer = Enemy("whisperer", "低语者", "侵蚀最清醒者的心智", 24, 2, EnemyPattern.Whisperer);
            var hunter = Enemy("hunter", "追猎者", "追击虚弱者并造成流血", 24, 7, EnemyPattern.Hunter);
            var none = Asset<MentalDefinition>(Data + "/MentalStates/Neutral.asset", x => x.Spec = new MentalSpec { State = MentalState.None, Name = "平稳" });
            var composed = Asset<MentalDefinition>(Data + "/MentalStates/Composed.asset", x => x.Spec = new MentalSpec { State = MentalState.Composed, Name = "凝神", TechnicalMultiplier = 1.25 });
            var agitated = Asset<MentalDefinition>(Data + "/MentalStates/Agitated.asset", x => x.Spec = new MentalSpec { State = MentalState.Agitated, Name = "躁动", PrimalMultiplier = 1.25, AccuracyModifier = -0.05 });
            var catalog = Asset<GameCatalog>(Data + "/GameCatalog.asset", x => {
                x.Heroes = new[] { guard, scout, arbalist, medic };
                x.Enemies = new[] { cleaver, whisperer, hunter };
                x.MentalStates = new[] { none, composed, agitated };
            });
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cameraObject = new GameObject("Main Camera");
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.06f, 0.08f, 0.1f);
                cameraObject.tag = "MainCamera"; cameraObject.transform.position = new Vector3(0, 0, -10);
                cameraObject.AddComponent<AudioListener>();
                var app = new GameObject("StageOne").AddComponent<StageOneApp>(); app.Catalog = catalog;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Between the Veils - Stage One";
            PlayerSettings.companyName = "GameMake";
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            Debug.Log("STAGE_ONE_PREPARED " + ScenePath);
        }

        [MenuItem("GameMake/Build Stage One Windows")]
        public static void BuildWindows()
        {
            Prepare();
            string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
            string target = Path.Combine(workspace, "交付", "阶段一", "Windows试玩版", "BetweenTheVeils.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = target, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log("STAGE_ONE_BUILD_OK " + target + " size=" + report.summary.totalSize);
        }

        private static T Asset<T>(string path, Action<T> setup) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing; // Preserve subsequent designer edits.
            var item = ScriptableObject.CreateInstance<T>(); setup(item); AssetDatabase.CreateAsset(item, path); return item;
        }
        private static SkillDefinition Skill(string id, string name, string description, double multiplier, int actors, int targets,
            SkillEffect effect = SkillEffect.Attack, TargetSide target = TargetSide.Enemy, int uses = 0, int bleed = 0, bool interrupt = false, bool cleanse = false, double accuracy = 0.9)
        {
            return Asset<SkillDefinition>(Data + "/Skills/" + id + ".asset", x => x.Spec = new SkillSpec {
                Id = id, Name = name, Description = description, Multiplier = multiplier, ActorRanks = actors, TargetRanks = targets,
                Effect = effect, Target = target, Uses = uses, Bleed = bleed, Interrupt = interrupt, Cleanse = cleanse, Accuracy = accuracy
            });
        }
        private static CharacterDefinition Character(string id, string name, string subtitle, HeroRole role, int hp, int speed, double sanity,
            SkillDefinition primary, SkillDefinition support, SkillDefinition technical, SkillDefinition primal)
        {
            return Asset<CharacterDefinition>(Data + "/Characters/" + id + ".asset", x => {
                x.Id = id; x.DisplayName = name; x.Subtitle = subtitle; x.Role = role; x.MaxHp = hp; x.Speed = speed; x.SanityOffset = sanity;
                x.Attack = 10; x.Accuracy = 0.9; x.CritChance = 0.1; x.Dodge = 0; x.Defense = 0;
                x.Primary = primary; x.Support = support; x.Technical = technical; x.Primal = primal;
            });
        }
        private static CharacterDefinition Enemy(string id, string name, string subtitle, int hp, int speed, EnemyPattern pattern)
        {
            return Asset<CharacterDefinition>(Data + "/Characters/" + id + ".asset", x => {
                x.Id = id; x.DisplayName = name; x.Subtitle = subtitle; x.Role = HeroRole.Enemy; x.MaxHp = hp; x.Speed = speed; x.EnemyPattern = pattern;
                x.Attack = 10; x.Accuracy = 0.86; x.CritChance = 0.1; x.Dodge = 0; x.Defense = 0;
            });
        }
    }
}
