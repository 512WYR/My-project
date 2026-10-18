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
            var monsters = BuildMonsters();
            var none = Asset<MentalDefinition>(Data + "/MentalStates/Neutral.asset", x => x.Spec = new MentalSpec { State = MentalState.None, Name = "平稳" });
            var composed = Asset<MentalDefinition>(Data + "/MentalStates/Composed.asset", x => x.Spec = new MentalSpec { State = MentalState.Composed, Name = "凝神", TechnicalMultiplier = 1.25 });
            var agitated = Asset<MentalDefinition>(Data + "/MentalStates/Agitated.asset", x => x.Spec = new MentalSpec { State = MentalState.Agitated, Name = "躁动", PrimalMultiplier = 1.25, AccuracyModifier = -0.05 });
            var catalog = Asset<GameCatalog>(Data + "/GameCatalog.asset", x => {
                // Rank follows array order: front to back, so the shield stands at position 1.
                x.Heroes = new[] { shield, warrior, medic, gunner };
                x.Enemies = new[] { cleaver, whisperer, hunter };
                x.MentalStates = new[] { none, composed, agitated };
            });
            Migrate(catalog);
            EnsureEnemyContent(catalog, monsters);
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
            var cleave = Skill("w_cleave", "斩劈", "●●●○/●●○○\n前移1位，造成攻击力100%的伤害，预计10点", 7, 3, s => { s.Multiplier = 1.0; s.SelfMove = -1; });
            var bash = Skill("w_bash", "猛击", "●●○○/●●●○\n造成攻击力70%的伤害，预计7点；命中后失衡：本回合无法行动、位移必中", 3, 7, s => { s.Multiplier = 0.7; s.Imbalance = true; });
            var sweep = Skill("w_sweep", "扩散斩击", "○●●○/●●●○\n主目标受到攻击力80%的伤害，预计8点；左右相邻各受攻击力40%，预计4点", 6, 7, s => { s.Multiplier = 0.8; s.Area = SkillArea.Adjacent; s.AreaRatio = 0.5; });
            var gambit = Skill("w_gambit", "带伤强攻", "●●●○/●●●○\n忽略肢体减益，造成攻击力100%的伤害，预计10点；自身损血与肢体损伤增伤，用后受伤+25%，每场1次", 7, 7, s => {
                s.Multiplier = 1.0; s.Uses = 1; s.IgnoreLimbPenalty = true;
                s.HpMissingDamageScale = 0.5; s.LimbBonusPerStack = 2; s.SelfVulnerable = 0.25;
            });
            var primalSweep = Skill("w_primal_sweep", "扩散斩击·暴戾", "○●●○/●●●○\n主目标受到攻击力100%的伤害，预计10点；1名副目标受攻击力75%，预计8点；无副目标时30%误伤队友", 6, 7, s => {
                s.Multiplier = 1.0; s.Area = SkillArea.Adjacent; s.AreaMax = 1; s.AreaRatio = 0.75; s.FriendlyFireChance = 0.3;
            });
            var pursuit = Skill("w_pursuit", "追击", "●●●○/●●●●\n1回合内全队造成伤害后追加攻击力50%，预计5点；追击不能连锁", 7, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.Followup; s.FollowupPercent = 50; s.Multiplier = 0;
            });
            return Character("warrior", "战士", "以伤换伤，向前撕开缺口", "warrior", 36, 6, 0,
                new[] { cleave, bash, sweep, gambit }, new[] { primalSweep }, pursuit);
        }

        private static CharacterDefinition Shield()
        {
            var strike = Skill("s_strike", "盾击", "●●○○/●●○○\n造成攻击力80%的伤害，预计8点", 3, 3, s => s.Multiplier = 0.8);
            var guard = Skill("s_guard", "守护", "●●●●/●●●●\n守护其他1名队友（不能选自己），1回合内代替其承受攻击", 15, 15, s => {
                s.Effect = SkillEffect.Protect; s.Target = TargetSide.Ally; s.Multiplier = 0;
            });
            var bulwark = Skill("s_bulwark", "壁垒", "●●○○/●●●●\n全队防御+20%（本回合），不叠加", 3, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.DefenseUp; s.GrantDefense = 0.2; s.Multiplier = 0;
            });
            var block = Skill("s_block", "格挡", "●○○○/●●●●\n赋予1次格挡：完全免除下次攻击的伤害与减益", 1, 15, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Grant = AllyBuff.Block; s.Multiplier = 0;
            });
            var primalGuard = Skill("s_primal_guard", "守护·血怒", "●●○○/●●●●\n守护其他1名队友（不能选自己）1回合；自身受击或代伤后反击攻击力100%，预计10点", 3, 15, s => {
                s.Effect = SkillEffect.Protect; s.Target = TargetSide.Ally; s.SelfGrant = AllyBuff.Counter; s.Multiplier = 0;
            });
            var phalanx = Skill("s_phalanx", "全队格挡反击", "●●○○/●●●●\n全队各1次格挡：免除该次攻击并反击攻击力100%，预计10点；每场1次", 3, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Grant = AllyBuff.Block | AllyBuff.Counter; s.Uses = 1; s.Multiplier = 0;
            });
            return Character("shield", "盾卫", "站稳，然后让别人站得也稳", "shield", 44, 4, 0,
                new[] { strike, guard, bulwark, block }, new[] { primalGuard }, phalanx);
        }

        private static CharacterDefinition Gunner()
        {
            var shot = Skill("g_shot", "指定射击", "○○●●/○●●●\n造成攻击力100%的伤害，预计10点", 12, 14, s => s.Multiplier = 1.0);
            var blockShot = Skill("g_block_shot", "阻断射击", "○○●●/●●●●\n造成攻击力70%的伤害，预计7点，并打断目标本回合行动；每场2次", 12, 15, s => {
                s.Multiplier = 0.7; s.Interrupt = true; s.Uses = 2;
            });
            var resupply = Skill("g_resupply", "军备补充", "○○○●/●●●●\n全队命中+20%（本回合）；所选角色恢复10理智", 8, 15, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Grant = AllyBuff.AccuracyUp; s.GrantAccuracy = 0.2;
                // Still asks for a target (that is who regains sanity), but the accuracy boost lands on the
                // whole party: the sweep is Party-wide while the rider stays on the chosen teammate.
                s.Area = SkillArea.Party; s.Multiplier = 0;
                s.Rider = new SkillRider { Kind = RiderKind.SanityRestore, Area = RiderArea.Chosen, Amount = 10 };
            });
            var grapple = Skill("g_grapple", "拖拽射击", "○○●●/○○●●\n造成攻击力75%的伤害，预计8点；命中后30%把目标拖到敌方1号位", 12, 12, s => {
                s.Multiplier = 0.75; s.PullToFront = true; s.PullChance = 0.3;
            });
            var primalShot = Skill("g_primal_shot", "指定射击·散乱", "○○●●/○●●●\n主目标受攻击力125%的伤害，预计13点；2名溅射各受主目标伤害的50%，预计7点；命中降至75%", 12, 14, s => {
                s.Multiplier = 1.25; s.Area = SkillArea.Splash; s.AreaMax = 2; s.AreaRatio = 0.5; s.Accuracy = 0.75;
            });
            var pierce = Skill("g_pierce", "贯穿弹", "○○○●/●●●●\n主目标受攻击力100%的伤害，预计10点并被打断；后方2人依次受攻击力75%/50%，预计8/5点；每场3次", 8, 15, s => {
                s.Multiplier = 1.0; s.Area = SkillArea.Pierce; s.AreaMax = 2; s.AreaDecay = 0.25; s.Interrupt = true; s.Uses = 3;
            });
            return Character("gunner", "枪手", "距离、准星与一次呼吸", "gunner", 30, 7, 0,
                new[] { shot, blockShot, resupply, grapple }, new[] { primalShot }, pierce);
        }

        private static CharacterDefinition Medic()
        {
            var bloodlet = Skill("m_bloodlet", "放血", "●●○○/●●○○\n造成攻击力60%的伤害，预计6点；施加2次流血（每次2点）", 3, 3, s => {
                s.Multiplier = 0.6; s.Bleed = 2; s.BleedTurns = 2;
            });
            var bloodHeal = Skill("m_blood_heal", "流血奶", "○●●○/●●●●\n恢复攻击力140%的生命，预计14点；50%概率附加流血（每次3点，2次）", 6, 15, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.Multiplier = 1.4;
                s.Bleed = 3; s.BleedTurns = 2; s.BleedChance = 0.5;
            });
            var mist = Skill("m_mist", "群体药雾", "○○●●/●●●●\n全队各恢复攻击力50%的生命，预计5点", 12, 0, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Multiplier = 0.5;
            });
            var cleanse = Skill("m_cleanse", "驱散疗法", "○○○●/●●●●\n清除全队每人的1种减益（当前为流血）", 8, 0, s => {
                s.Effect = SkillEffect.Buff; s.Target = TargetSide.Ally; s.Targeting = SkillTargeting.All;
                s.Area = SkillArea.Party; s.Cleanse = true; s.Multiplier = 0;
            });
            var primalHeal = Skill("m_primal_heal", "流血奶·渴求", "○●●○/●●●●\n随机恢复攻击力160%的生命，预计16点；50%概率附加流血（每次4点，2次）", 6, 15, s => {
                s.Effect = SkillEffect.Heal; s.Target = TargetSide.Ally; s.RandomTarget = true; s.Multiplier = 1.6;
                s.Bleed = 4; s.BleedTurns = 2; s.BleedChance = 0.5;
            });
            var drain = Skill("m_drain", "吸血扩散", "○●●●/●●●○\n造成攻击力100%的伤害，预计10点；按实际伤害治疗自身，左右相邻各获治疗量50%；每场3次", 14, 7, s => {
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

        // Asset<T> never overwrites, so a catalog created before these fields existed keeps its old YAML
        // and reads every one of them back as zero. This backfills the blanks in place and leaves anything
        // a designer has already set alone. MapRules() repairs these too; this makes the fix visible in
        // the inspector instead of hiding it.
        private static void Migrate(GameCatalog catalog)
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
            if (!dirty) return;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("STAGE_ONE_MIGRATED catalog fields backfilled");
        }

        // ---------------- designed monsters ----------------
        //
        // Damage is carried by Attack × Multiplier so the existing resolver (defense, guard, block,
        // counter) stays in charge. All designed monsters have CritChance 0 and a 0.9 base accuracy:
        // SkillSpec.Accuracy then reads as the literal hit rate, and the provisional numbers follow the
        // 怪物设计记录 document's "我方普通单体攻击 8～12 点" scale.

        private sealed class MonsterSet
        {
            public CharacterDefinition Minion1, Minion2, Sanity, EliteSanity, Bleed, EliteBleed;
            public CharacterDefinition High, EliteHigh, Tank, EliteTank, Healer;
            public CharacterDefinition CopyBoss, GiantBoss, SteamMachine, Operator, RandomSummoner;
            public CharacterDefinition[] All;
        }

        private static MonsterSet BuildMonsters()
        {
            var minion1 = Monster("minion1", "小怪1", "低生命、低命中的入门敌人", 12, 3, 3, 0.75, EnemyBehavior.Simple, EnemyPattern.Cleaver,
                new[] { ESkill("e_minion1_strike", "普通攻击", 15, 3, s => s.Multiplier = 1.0) });
            var minion2 = Monster("minion2", "小怪2", "能碰到我方前排与中排的杂兵", 10, 4, 2, 0.75, EnemyBehavior.Simple, EnemyPattern.Cleaver,
                new[] { ESkill("e_minion2_strike", "普通攻击", 15, 7, s => s.Multiplier = 1.0) });
            var sanity = Monster("sanity", "理智怪", "从后排侵蚀我方理智", 20, 5, 3, 0.9, EnemyBehavior.Sanity, EnemyPattern.Whisperer,
                new[]
                {
                    ESkill("e_sanity_drain", "降低理智", 14, 15, s => { s.Effect = SkillEffect.Sanity; s.SanityDamage = 8; s.Multiplier = 0; }),
                    ESkill("e_sanity_melee", "近身攻击", 1, 3, s => s.Multiplier = 1.0)
                });
            var eliteSanity = Monster("elite_sanity", "精英理智怪", "理智侵蚀会波及相邻角色", 38, 5, 5, 0.9, EnemyBehavior.Sanity, EnemyPattern.Whisperer,
                new[]
                {
                    ESkill("e_elite_sanity_drain", "降低理智", 14, 15, s => { s.Effect = SkillEffect.Sanity; s.SanityDamage = 12; s.SanityAdjacent = 4; s.Multiplier = 0; }),
                    ESkill("e_elite_sanity_melee", "近身攻击", 1, 3, s => s.Multiplier = 1.0)
                });
            var bleed = Monster("bleed", "流血怪", "优先攻击流血最多的角色", 22, 5, 3, 0.9, EnemyBehavior.Bleed, EnemyPattern.Hunter,
                new[]
                {
                    ESkill("e_bleed_strike", "流血攻击", 15, 7, s => { s.Multiplier = 1.0; s.Bleed = 2; s.BleedTurns = 2; s.TargetRule = EnemyTargetRule.MostBleed; })
                });
            var eliteBleed = Monster("elite_bleed", "精英流血怪", "更重的流血压力", 40, 5, 5, 0.9, EnemyBehavior.Bleed, EnemyPattern.Hunter,
                new[]
                {
                    ESkill("e_elite_bleed_strike", "流血攻击", 15, 7, s => { s.Multiplier = 1.0; s.Bleed = 3; s.BleedTurns = 2; s.TargetRule = EnemyTargetRule.MostBleed; })
                });
            var high = Monster("high", "高伤怪", "站前排时伤害极高，后排则明显减弱", 26, 3, 14, 0.9, EnemyBehavior.HighDamage, EnemyPattern.Cleaver,
                new[]
                {
                    ESkill("e_high_close", "近距离攻击", 3, 3, s => { s.Multiplier = 1.0; s.Accuracy = 0.9; }),
                    ESkill("e_high_far", "远距离攻击", 12, 3, s => { s.Multiplier = 0.5; s.Accuracy = 0.65; })
                });
            var eliteHigh = Monster("elite_high", "精英高伤怪", "前排一击足以致命", 46, 4, 20, 0.9, EnemyBehavior.HighDamage, EnemyPattern.Cleaver,
                new[]
                {
                    ESkill("e_elite_high_close", "近距离攻击", 3, 3, s => { s.Multiplier = 1.0; s.Accuracy = 0.9; }),
                    ESkill("e_elite_high_far", "远距离攻击", 12, 3, s => { s.Multiplier = 0.5; s.Accuracy = 0.65; })
                });
            var tank = Monster("tank", "肉盾怪", "按功能优先级守护同伴", 45, 3, 5, 0.85, EnemyBehavior.Tank, EnemyPattern.Cleaver,
                new[]
                {
                    ESkill("e_tank_guard", "守护", 15, 15, s => { s.Effect = SkillEffect.Protect; s.Multiplier = 0; }),
                    ESkill("e_tank_strike", "普通攻击", 3, 3, s => s.Multiplier = 1.0)
                });
            var eliteTank = Monster("elite_tank", "精英肉盾怪", "守护时额外减免四分之一的伤害", 75, 3, 7, 0.85, EnemyBehavior.Tank, EnemyPattern.Cleaver,
                new[]
                {
                    ESkill("e_elite_tank_guard", "强化守护", 15, 15, s => { s.Effect = SkillEffect.Protect; s.Multiplier = 0; s.GuardReduction = 0.25; }),
                    ESkill("e_elite_tank_strike", "普通攻击", 3, 3, s => s.Multiplier = 1.0)
                });
            var healer = Monster("healer", "回血怪", "精英战专属支援单位，没有攻击能力", 30, 6, 10, 0.9, EnemyBehavior.Healer, EnemyPattern.Whisperer,
                new[]
                {
                    ESkill("e_healer_heal", "治疗", 14, 15, s => { s.Effect = SkillEffect.Heal; s.Multiplier = 1.0; })
                });
            var copyBoss = Monster("copy_boss", "首领·复制者", "复制我方队员，死亡后补召", 90, 5, 10, 0.9, EnemyBehavior.CopyBoss, EnemyPattern.Whisperer,
                new[] { ESkill("e_copy_boss_strike", "复制打击", 15, 15, s => s.Multiplier = 1.0) });
            var giantBoss = Monster("giant_boss", "首领·巨型聚合体", "占据1～3号位，按1→2→1→2→3循环", 180, 4, 16, 0.9, EnemyBehavior.GiantBoss, EnemyPattern.Cleaver,
                new[]
                {
                    ESkill("e_giant_all", "全体低伤攻击", 15, 15, s => { s.Multiplier = 0.25; s.Accuracy = 0.9; s.TargetCount = 4; }),
                    ESkill("e_giant_three", "三人中伤攻击", 15, 15, s => { s.Multiplier = 0.5; s.Accuracy = 0.85; s.TargetCount = 3; }),
                    ESkill("e_giant_one", "单体高伤攻击", 15, 15, s => { s.Multiplier = 1.0; s.Accuracy = 0.8; s.TargetCount = 1; })
                },
                x => { x.RankWidth = 3; x.Immobile = true; x.BossHealOnHeroDeath = 15; });
            var steamMachine = Monster("steam_machine", "蒸汽机器", "占据2～3号位，累计操作2次后爆发", 100, 2, 12, 0.9, EnemyBehavior.SteamMachine, EnemyPattern.Cleaver,
                new[] { ESkill("e_steam_blast", "蒸汽爆发", 15, 15, s => { s.Multiplier = 1.0; s.TargetCount = 4; }) },
                x => { x.RankWidth = 2; x.Immobile = true; });
            var op = Monster("operator", "操作怪", "只在4号位操作机器，死亡后下回合补召", 30, 8, 0, 0.9, EnemyBehavior.Operator, EnemyPattern.Hunter,
                new SkillSpec[0], x => x.ActionInterval = 2);
            var summoner = Monster("random_summoner", "随机召唤怪", "每两回合行动一次，会变成随机普通怪", 1, 4, 0, 0.9, EnemyBehavior.RandomSummoner, EnemyPattern.Whisperer,
                new SkillSpec[0], x =>
                {
                    x.ActionInterval = 2;
                    x.SummonPool = new[] { minion1, minion2, bleed, sanity, tank };
                });
            return new MonsterSet
            {
                Minion1 = minion1, Minion2 = minion2, Sanity = sanity, EliteSanity = eliteSanity,
                Bleed = bleed, EliteBleed = eliteBleed, High = high, EliteHigh = eliteHigh,
                Tank = tank, EliteTank = eliteTank, Healer = healer, CopyBoss = copyBoss,
                GiantBoss = giantBoss, SteamMachine = steamMachine, Operator = op, RandomSummoner = summoner,
                All = new[] { minion1, minion2, sanity, eliteSanity, bleed, eliteBleed, high, eliteHigh,
                    tank, eliteTank, healer, copyBoss, giantBoss, steamMachine, op, summoner }
            };
        }

        private static CharacterDefinition Monster(string id, string name, string subtitle, int hp, int speed, int attack,
            double accuracy, EnemyBehavior behavior, EnemyPattern pattern, SkillSpec[] skills, Action<CharacterDefinition> extra = null)
        {
            return Asset<CharacterDefinition>(Data + "/Characters/" + id + ".asset", x =>
            {
                x.Id = id; x.DisplayName = name; x.Subtitle = subtitle; x.Silhouette = "enemy";
                x.MaxHp = hp; x.Speed = speed; x.Attack = attack; x.Accuracy = accuracy;
                x.CritChance = 0; x.Dodge = 0; x.Defense = 0;
                x.EnemyPattern = pattern; x.Behavior = behavior; x.EnemySkills = skills;
                if (extra != null) extra(x);
            });
        }

        private static SkillSpec ESkill(string id, string name, int actors, int targets, Action<SkillSpec> setup)
        {
            var spec = new SkillSpec { Id = id, Name = name, Description = name, ActorRanks = actors, TargetRanks = targets };
            setup(spec);
            return spec;
        }

        private static void EnsureEnemyContent(GameCatalog catalog, MonsterSet monsters)
        {
            bool dirty = false;
            if (catalog.Monsters == null || catalog.Monsters.Length == 0) { catalog.Monsters = monsters.All; dirty = true; }
            if (catalog.NormalEncounters == null || catalog.NormalEncounters.Length == 0)
            { catalog.NormalEncounters = NormalLineups(monsters); dirty = true; }
            if (catalog.EliteEncounters == null || catalog.EliteEncounters.Length == 0)
            { catalog.EliteEncounters = EliteLineups(monsters); dirty = true; }
            if (catalog.BossEncounters == null || catalog.BossEncounters.Length == 0)
            { catalog.BossEncounters = BossLineups(monsters); dirty = true; }
            if (!dirty) return;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("STAGE_ONE_MONSTERS catalog formations backfilled");
        }

        private static EnemyLineup Lineup(string id, params CharacterDefinition[] slots)
        {
            var line = new EnemyLineup { Id = id, Slots = new CharacterDefinition[4] };
            for (int i = 0; i < slots.Length && i < 4; i++) line.Slots[i] = slots[i];
            return line;
        }

        // 普通战 01～08，按 1 号位到 4 号位排列；null 表示空位。
        private static EnemyLineup[] NormalLineups(MonsterSet m)
        {
            return new[]
            {
                Lineup("普通01", m.Minion1, m.Minion2, m.Minion2),
                Lineup("普通02", m.Minion1, m.Minion2, m.Sanity),
                Lineup("普通03", m.Minion1, m.Bleed, m.Minion2),
                Lineup("普通04", m.High, m.Minion1, m.Minion2),
                Lineup("普通05", m.Tank, m.Minion1, m.Minion2, m.Sanity),
                Lineup("普通06", m.High, m.Minion1, m.Bleed, m.Minion2),
                Lineup("普通07", m.Tank, m.Minion2, m.Bleed, m.Sanity),
                Lineup("普通08", m.Minion1, m.Bleed, m.High, m.Sanity)
            };
        }

        // 精英战 01～06，不使用普通怪填充空位。
        private static EnemyLineup[] EliteLineups(MonsterSet m)
        {
            return new[]
            {
                Lineup("精英01", m.EliteTank, m.EliteBleed, m.EliteSanity),
                Lineup("精英02", m.EliteHigh, m.EliteBleed, m.Healer),
                Lineup("精英03", m.EliteTank, m.EliteHigh, m.EliteBleed, m.EliteSanity),
                Lineup("精英04", m.EliteTank, m.EliteBleed, m.EliteSanity, m.Healer),
                Lineup("精英05", m.EliteTank, m.EliteHigh, m.EliteBleed, m.Healer),
                Lineup("精英06", m.EliteBleed, m.EliteSanity, m.EliteHigh, m.Healer)
            };
        }

        // 三个首领各一套。组合首领按 1 号位随机召唤怪、2～3 号位蒸汽机器、4 号位操作怪摆放。
        private static EnemyLineup[] BossLineups(MonsterSet m)
        {
            return new[]
            {
                Lineup("Boss01-复制", m.CopyBoss),
                Lineup("Boss02-巨型", m.GiantBoss),
                Lineup("Boss03-组合", m.RandomSummoner, m.SteamMachine, null, m.Operator)
            };
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
