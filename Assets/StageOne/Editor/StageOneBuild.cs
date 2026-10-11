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
            Directory.CreateDirectory(Data + "/Events");
            Directory.CreateDirectory("Assets/StageOne/Scenes");
            // Every folder above has to exist before this call: CreateAsset cannot write into a directory
            // the AssetDatabase has not seen yet.
            AssetDatabase.Refresh();
            var warrior = Warrior();
            var shield = Shield();
            var gunner = Gunner();
            var medic = Medic();
            var cleaver = Enemy("cleaver", "执斧者", "重劈前排的废墟守卫", 32, 3, EnemyPattern.Cleaver);
            var whisperer = Enemy("whisperer", "低语者", "侵蚀最清醒者的心智", 24, 2, EnemyPattern.Whisperer);
            var hunter = Enemy("hunter", "追猎者", "追击虚弱者并造成流血", 24, 7, EnemyPattern.Hunter);
            var none = Asset<MentalDefinition>(Data + "/MentalStates/Neutral.asset", x => x.Spec = new MentalSpec { State = MentalState.None, Name = "平稳" });
            var composed = Asset<MentalDefinition>(Data + "/MentalStates/Composed.asset", x => x.Spec = new MentalSpec { State = MentalState.Composed, Name = "凝神", TechnicalMultiplier = 1.25 });
            var agitated = Asset<MentalDefinition>(Data + "/MentalStates/Agitated.asset", x => x.Spec = new MentalSpec { State = MentalState.Agitated, Name = "躁动", PrimalMultiplier = 1.25, AccuracyModifier = -0.05 });
            var events = ExplorationEvents();
            var catalog = Asset<GameCatalog>(Data + "/GameCatalog.asset", x => {
                // Rank follows array order: front to back, so the shield stands at position 1.
                x.Heroes = new[] { shield, warrior, medic, gunner };
                x.Enemies = new[] { cleaver, whisperer, hunter };
                x.MentalStates = new[] { none, composed, agitated };
                x.Events = events;
            });
            Migrate(catalog, events);
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

        // ---------------- characters ----------------
        //
        // Rank bitmasks: bit 0 is position 1 (the front rank, closest to the enemy). 1<<rank, so 3 is the
        // two front positions, 12 the two back positions, 14 positions 2/3/4, 15 all four.
        // Skill numbers and placeholders follow the four <name>技能记录.md documents. Any number the
        // document left "待定" is provisional and marked as such in the document.

        private static CharacterDefinition Warrior()
        {
            // 战士 · 位置 2 / 3 / 4 都能站，前排技能要求 1/2/3。
            var cleave = Skill("w_cleave", "斩劈", "向前移动一位并攻击敌方1、2号位", 7, 3, s => { s.Multiplier = 1.0; s.SelfMove = -1; });
            var bash = Skill("w_bash", "猛击", "较低伤害 · 命中后造成失衡", 3, 7, s => { s.Multiplier = 0.7; s.Imbalance = true; });
            var sweep = Skill("w_sweep", "扩散斩击", "攻击主目标，并波及左右相邻敌人", 6, 7, s => { s.Multiplier = 0.8; s.Area = SkillArea.Adjacent; s.AreaRatio = 0.5; });
            var gambit = Skill("w_gambit", "带伤强攻", "忽略肢体减益 · 每场仅1次", 7, 7, s => {
                s.Multiplier = 1.0; s.Uses = 1; s.IgnoreLimbPenalty = true;
                s.HpMissingDamageScale = 0.5; s.LimbBonusPerStack = 2; s.SelfVulnerable = 0.25;
            });
            var primalSweep = Skill("w_primal_sweep", "扩散斩击·暴戾", "伤害提高，波及一名敌人或误伤队友", 6, 7, s => {
                s.Multiplier = 1.0; s.Area = SkillArea.Adjacent; s.AreaMax = 1; s.AreaRatio = 0.75; s.FriendlyFireChance = 0.3;
            });
            var pursuit = Skill("w_pursuit", "追击", "为全队赋予追击：造成伤害时追加50%攻击", 7, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.Followup; s.FollowupPercent = 50; s.Multiplier = 0;
            });
            return Character("warrior", "战士", "以伤换伤，向前撕开缺口", "warrior", 36, 6, 0,
                new[] { cleave, bash, sweep, gambit }, new[] { primalSweep }, pursuit);
        }

        private static CharacterDefinition Shield()
        {
            var strike = Skill("s_strike", "盾击", "攻击敌方1、2号位", 3, 3, s => s.Multiplier = 0.8);
            var guard = Skill("s_guard", "守护", "守护一名队友，本回合代为承受攻击", 15, 15, s => {
                s.Effect = SkillEffect.Protect; s.Target = TargetSide.Ally; s.Multiplier = 0;
            });
            var bulwark = Skill("s_bulwark", "壁垒", "提高全队防御力", 3, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.DefenseUp; s.GrantDefense = 0.2; s.Multiplier = 0;
            });
            var block = Skill("s_block", "格挡", "为一名己方角色赋予1次格挡（1号位施放）", 1, 15, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Grant = AllyBuff.Block; s.Multiplier = 0;
            });
            var primalGuard = Skill("s_primal_guard", "守护·血怒", "守护一名队友，自身获得反击", 3, 15, s => {
                s.Effect = SkillEffect.Protect; s.Target = TargetSide.Ally; s.SelfGrant = AllyBuff.Counter; s.Multiplier = 0;
            });
            var phalanx = Skill("s_phalanx", "全队格挡反击", "为全队各赋予格挡与反击 · 每场1次", 3, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.Block | AllyBuff.Counter; s.Uses = 1; s.Multiplier = 0;
            });
            return Character("shield", "盾卫", "站稳，然后让别人站得也稳", "shield", 44, 4, 0,
                new[] { strike, guard, bulwark, block }, new[] { primalGuard }, phalanx);
        }

        private static CharacterDefinition Gunner()
        {
            var shot = Skill("g_shot", "指定射击", "攻击敌方2、3、4号位（3/4号位施放）", 12, 14, s => s.Multiplier = 1.0);
            var blockShot = Skill("g_block_shot", "阻断射击", "攻击并打断目标 · 每场2次", 12, 15, s => {
                s.Multiplier = 0.7; s.Interrupt = true; s.Uses = 2;
            });
            var resupply = Skill("g_resupply", "军备补充", "全队精准提升 · 为一名角色恢复10理智（4号位施放）", 8, 15, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Grant = AllyBuff.AccuracyUp; s.GrantAccuracy = 0.2;
                // Still asks for a target (that is who regains sanity), but the accuracy boost lands on the
                // whole party: the sweep is Party-wide while the rider stays on the chosen teammate.
                s.Area = SkillArea.Party; s.Multiplier = 0;
                s.Rider = new SkillRider { Kind = RiderKind.SanityRestore, Area = RiderArea.Chosen, Amount = 10 };
            });
            var grapple = Skill("g_grapple", "拖拽射击", "75%伤害 · 30%概率把敌人拖到1号位", 12, 12, s => {
                s.Multiplier = 0.75; s.PullToFront = true; s.PullChance = 0.3;
            });
            var primalShot = Skill("g_primal_shot", "指定射击·散乱", "命中降低，主目标受125%伤害并溅射周边", 12, 14, s => {
                s.Multiplier = 1.25; s.Area = SkillArea.Splash; s.AreaMax = 2; s.AreaRatio = 0.5; s.Accuracy = 0.75;
            });
            var pierce = Skill("g_pierce", "贯穿弹", "贯穿主目标后方最多两名敌人 · 打断 · 每场3次", 8, 15, s => {
                s.Multiplier = 1.0; s.Area = SkillArea.Pierce; s.AreaMax = 2; s.AreaDecay = 0.25; s.Interrupt = true; s.Uses = 3;
            });
            return Character("gunner", "枪手", "距离、准星与一次呼吸", "gunner", 30, 7, 0,
                new[] { shot, blockShot, resupply, grapple }, new[] { primalShot }, pierce);
        }

        private static CharacterDefinition Medic()
        {
            var bloodlet = Skill("m_bloodlet", "放血", "造成伤害并施加两次流血", 3, 3, s => {
                s.Multiplier = 0.6; s.Bleed = 2; s.BleedTurns = 2;
            });
            var bloodHeal = Skill("m_blood_heal", "流血奶", "治疗一名角色，50%概率施加3层流血", 6, 15, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.Multiplier = 1.4;
                s.Bleed = 3; s.BleedTurns = 2; s.BleedChance = 0.5;
            });
            var mist = Skill("m_mist", "群体药雾", "为己方全体恢复少量生命", 12, 0, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Multiplier = 0.5;
            });
            var cleanse = Skill("m_cleanse", "驱散疗法", "为己方每名角色移除一处减益（4号位施放）", 8, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Cleanse = true; s.Multiplier = 0;
            });
            var primalHeal = Skill("m_primal_heal", "流血奶·渴求", "治疗量提高，随机一名角色；流血更重", 6, 15, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.RandomTarget = true; s.Multiplier = 1.6;
                s.Bleed = 4; s.BleedTurns = 2; s.BleedChance = 0.5;
            });
            var drain = Skill("m_drain", "吸血扩散", "攻击敌人，治疗自身并扩散给相邻队友 · 每场3次", 14, 7, s => {
                s.Multiplier = 1.0; s.Uses = 3;
                s.Rider = new SkillRider { Kind = RiderKind.HealFromDamage, Area = RiderArea.SelfAdjacent, Ratio = 1.0, SpreadRatio = 0.5 };
            });
            return Character("medic", "奶妈", "让同伴撑过下一次心跳", "medic", 28, 5, 0,
                new[] { bloodlet, bloodHeal, mist, cleanse }, new[] { primalHeal }, drain);
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

        // One asset per default event, so the content is editable in the inspector. An existing asset is
        // returned untouched, which keeps a designer's edits.
        private static EventDefinition[] ExplorationEvents()
        {
            var list = new System.Collections.Generic.List<EventDefinition>();
            foreach (var spec in Exploration.DefaultEvents)
            {
                var captured = spec;
                list.Add(Asset<EventDefinition>(Data + "/Events/" + captured.Id + ".asset", x => x.Spec = captured));
            }
            return list.ToArray();
        }

        // Asset<T> never overwrites, so a catalog created before these fields existed keeps its old YAML
        // and reads every one of them back as zero. This backfills the blanks in place and leaves anything
        // a designer has already set alone. MapRules() repairs these too; this makes the fix visible in
        // the inspector instead of hiding it.
        private static void Migrate(GameCatalog catalog, EventDefinition[] events)
        {
            bool dirty = false;
            if (catalog.MapFloors <= 0) { catalog.MapFloors = 3; dirty = true; }
            if (catalog.MapStepsPerFloor == null || catalog.MapStepsPerFloor.Length == 0) { catalog.MapStepsPerFloor = new[] { 5, 6, 7 }; dirty = true; }
            if (catalog.MapProbabilityStrength <= 0) { catalog.MapProbabilityStrength = 1f; dirty = true; }
            if (catalog.MapBranchAffinity <= 0) { catalog.MapBranchAffinity = 1.5f; dirty = true; }
            // Backfill the two weight tables so they can be tuned in the inspector instead of in code.
            if (catalog.MapNodeOdds == null || catalog.MapNodeOdds.Length == 0)
            {
                catalog.MapNodeOdds = MapGenerator.DefaultOdds
                    .Select(o => new NodeOddsSpec(o.Type, o.Weight, o.Affinity)).ToArray();
                dirty = true;
            }
            if (catalog.MapBranchWeights == null || catalog.MapBranchWeights.Length != 3) { catalog.MapBranchWeights = new[] { 0.31, 0.38, 0.31 }; dirty = true; }
            if (catalog.SupplyHealRatio <= 0) { catalog.SupplyHealRatio = 0.35f; dirty = true; }
            if (catalog.SuppliesPerBattle <= 0) { catalog.SuppliesPerBattle = 2; dirty = true; }
            if (catalog.SuppliesPerElite <= 0) { catalog.SuppliesPerElite = 4; dirty = true; }
            if (catalog.SuppliesPerBoss <= 0) { catalog.SuppliesPerBoss = 8; dirty = true; }
            if (catalog.EliteHealthBonus <= 0) { catalog.EliteHealthBonus = 20; dirty = true; }
            if (catalog.EliteAttackScale <= 0) { catalog.EliteAttackScale = 1.25f; dirty = true; }
            if (catalog.BossHealthBonus <= 0) { catalog.BossHealthBonus = 45; dirty = true; }
            if (catalog.BossAttackScale <= 0) { catalog.BossAttackScale = 1.5f; dirty = true; }
            if (catalog.Events == null || catalog.Events.Length == 0) { catalog.Events = events; dirty = true; }
            if (!dirty) return;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("STAGE_ONE_MIGRATED catalog fields backfilled");
        }

        private static T Asset<T>(string path, Action<T> setup) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing; // Preserve subsequent designer edits.
            var item = ScriptableObject.CreateInstance<T>(); setup(item); AssetDatabase.CreateAsset(item, path); return item;
        }

        // ActorRanks/TargetRanks are the legal cast positions and legal target positions as bitmasks.
        private static SkillDefinition Skill(string id, string name, string description, int actors, int targets, Action<SkillSpec> setup)
        {
            return Asset<SkillDefinition>(Data + "/Skills/" + id + ".asset", x => {
                var spec = new SkillSpec { Id = id, Name = name, Description = description, ActorRanks = actors, TargetRanks = targets };
                setup(spec);
                x.Spec = spec;
            });
        }

        private static CharacterDefinition Character(string id, string name, string subtitle, string silhouette, int hp, int speed, double sanity,
            SkillDefinition[] basic, SkillDefinition[] primal, SkillDefinition technicalSpecial)
        {
            return Asset<CharacterDefinition>(Data + "/Characters/" + id + ".asset", x => {
                x.Id = id; x.DisplayName = name; x.Subtitle = subtitle; x.Silhouette = silhouette;
                x.MaxHp = hp; x.Speed = speed; x.SanityOffset = sanity;
                x.Attack = 10; x.Accuracy = 0.9; x.CritChance = 0.1; x.Dodge = 0; x.Defense = 0;
                x.BasicSkills = basic; x.PrimalSkills = primal; x.TechnicalSpecial = technicalSpecial;
            });
        }

        private static CharacterDefinition Enemy(string id, string name, string subtitle, int hp, int speed, EnemyPattern pattern)
        {
            return Asset<CharacterDefinition>(Data + "/Characters/" + id + ".asset", x => {
                x.Id = id; x.DisplayName = name; x.Subtitle = subtitle; x.Silhouette = "enemy"; x.MaxHp = hp; x.Speed = speed; x.EnemyPattern = pattern;
                x.Attack = 10; x.Accuracy = 0.86; x.CritChance = 0.1; x.Dodge = 0; x.Defense = 0;
            });
        }
    }
}
