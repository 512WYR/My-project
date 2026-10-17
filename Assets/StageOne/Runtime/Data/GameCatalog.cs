using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    // One battle formation. Slot index is the starting rank (0 = enemy rank 1), so a null slot is an empty
    // position rather than a compaction: the combination boss keeps the machine at slots 1-2 and the
    // operator at slot 3. A multi-rank unit is listed once at its front slot.
    [Serializable]
    public sealed class EnemyLineup
    {
        public string Id;
        public CharacterDefinition[] Slots = new CharacterDefinition[4];
    }

    [CreateAssetMenu(menuName = "GameMake/Stage One Catalog")]
    public sealed class GameCatalog : ScriptableObject
    {
        public CharacterDefinition[] Heroes;
        // Legacy scripted enemies. Kept for the stage-one rule tests and as the fallback when no
        // formation table has been generated yet.
        public CharacterDefinition[] Enemies;
        // The 11 designed non-boss monsters plus the three bosses, listed for inspection and for the
        // random-summoner pool. Formations below point at these assets.
        public CharacterDefinition[] Monsters;
        [Header("战斗搭配")]
        public EnemyLineup[] NormalEncounters;
        public EnemyLineup[] EliteEncounters;
        public EnemyLineup[] BossEncounters;
        public MentalDefinition[] MentalStates;
        [Range(0, 3)] public float ProbabilityStrength = 1.5f;
        [Range(0, 1)] public float FeedbackFactor = 0.5f;
        [Range(0, 1)] public float DeathChance = 0.25f;
        [Range(1, 3)] public float CritMultiplier = 1.5f;
        public int MindStep = 15;
        [Header("地图生成")]
        public int MapFloors = 3;
        // Sampled positions per floor. Every floor also carries a fixed opening room and a fixed last room.
        public int[] MapStepsPerFloor = { 5, 6, 7 };
        [Range(0, 3)] public float MapProbabilityStrength = 1f;
        // Effective branch sensitivity is MapProbabilityStrength * MapBranchAffinity. At 1.5 the candidate
        // count runs 1.50 / 2.00 / 2.50 at sanity 20 / 50 / 80; at 1.0 the two ends only reach 1.65 / 2.35.
        public float MapBranchAffinity = 1.5f;
        // Room types offered at a sampled position. Weight is relative within this table; Affinity is the
        // sanity lean (negative shows up more at low sanity, positive at high, 0 ignores sanity). A type
        // with weight 0 is listed on the route page but never drawn. Empty falls back to
        // MapGenerator.DefaultOdds, which is what an asset made before this field existed reads back as.
        public NodeOddsSpec[] MapNodeOdds;
        // Relative weights for drawing 1, 2 or 3 candidates. The leans are fixed at -a / 0 / +a.
        public double[] MapBranchWeights = { 0.31, 0.38, 0.31 };
        [Header("探索内容")]
        // Event and altar content lives in EventScripts as code, not here: the documented events need steps,
        // a chosen member and fights, which a flat array of ScriptableObjects cannot carry. Everything below
        // is the supply room's numbers.
        [Range(0.05f, 1)] public float SupplyHealRatio = 0.35f;
        public int SuppliesPerBattle = 2;
        public int SuppliesPerElite = 4;
        public int SuppliesPerBoss = 8;
        [Header("敌人缩放")]
        public int EliteHealthBonus = 20;
        public float EliteAttackScale = 1.25f;
        public int BossHealthBonus = 45;
        public float BossAttackScale = 1.5f;
        [Header("肢体与义肢")]
        // One crippling blow always lands exactly half of a part's maximum, so a part is injured on the
        // first hit and severed on the second, for every hero and every part.
        [Range(0.05f, 1)] public float LimbHitRatio = 0.5f;
        [Range(0.05f, 1)] public float LimbHeadRatio = 0.3f;
        [Range(0.05f, 1)] public float LimbTorsoRatio = 0.45f;
        [Range(0.05f, 1)] public float LimbArmRatio = 0.35f;
        [Range(0.05f, 1)] public float LimbLegRatio = 0.35f;
        [Range(0, 1)] public float LimbHeadInjured = 0.1f;
        [Range(0, 1)] public float LimbHeadSevered = 0.25f;
        [Range(0, 1)] public float LimbTorsoInjured = 0.2f;
        [Range(0, 1)] public float LimbTorsoSevered = 0.4f;
        [Range(0, 1)] public float LimbArmInjured = 0.2f;
        [Range(0, 1)] public float LimbArmSevered = 0.45f;
        [Range(0, 1)] public float LimbLegInjured = 0.2f;
        [Range(0, 1)] public float LimbLegSevered = 0.45f;

        // Picks one formation for the room type and hands back fresh ActorSpecs with explicit start ranks.
        // The draw is part of the run's own random sequence, so a save/restore reproduces the same fight.
        // An empty table means the catalog has not been regenerated yet; the caller falls back to the
        // legacy three enemies scaled by Encounter.Scale.
        public ActorSpec[] BuildEnemyLineup(NodeType type, RunRandom random)
        {
            EnemyLineup[] table = type == NodeType.EliteBattle ? EliteEncounters
                : type == NodeType.Boss ? BossEncounters : NormalEncounters;
            if (table == null || table.Length == 0) return new ActorSpec[0];
            int seed = random == null ? 0 : (int)Math.Min(int.MaxValue, random.NextUnit() * table.Length);
            var lineup = table[Math.Max(0, Math.Min(table.Length - 1, seed))];
            if (lineup == null || lineup.Slots == null) return new ActorSpec[0];
            var specs = new List<ActorSpec>();
            for (int rank = 0; rank < lineup.Slots.Length && rank < 4; rank++)
            {
                var slot = lineup.Slots[rank];
                if (!slot) continue;
                var spec = slot.ToSpec();
                spec.StartRank = rank;
                // 首领房的每个单位都沿用旧缩放路径的命名，验收里用“首领”前缀识别首领战。
                if (type == NodeType.Boss && !spec.Name.StartsWith("首领"))
                    spec.Name = "首领·" + spec.Name;
                specs.Add(spec);
            }
            return specs.ToArray();
        }

        public CombatRules Rules()
        {
            return new CombatRules { ProbabilityStrength = ProbabilityStrength, FeedbackFactor = FeedbackFactor,
                DeathChance = DeathChance, CritMultiplier = CritMultiplier, MindStep = MindStep,
                MentalStates = MentalStates.Select(m => m.Spec).ToArray(), Limbs = LimbRules() };
        }

        // The prosthetic table stays in LimbSystem.DefaultProsthetics for the same reason the node weight
        // table stays in MapGenerator.DefaultOdds: an existing .asset never receives newly added array
        // fields, so keeping content here would silently produce empty references.
        public LimbRules LimbRules()
        {
            return new LimbRules { HitRatio = LimbHitRatio, Parts = new[]
            {
                new LimbPartRule(LimbPart.Head, LimbHeadRatio, LimbHeadInjured, LimbHeadSevered),
                new LimbPartRule(LimbPart.Torso, LimbTorsoRatio, LimbTorsoInjured, LimbTorsoSevered),
                new LimbPartRule(LimbPart.Arm, LimbArmRatio, LimbArmInjured, LimbArmSevered),
                new LimbPartRule(LimbPart.Leg, LimbLegRatio, LimbLegInjured, LimbLegSevered)
            } };
        }

        // The per-type weight table stays in MapGenerator.DefaultOdds; only the knobs live here, the same
        // way TechnicalWeight/BasicWeight/PrimalWeight stay in CombatRules.
        //
        // This goes through Resolve on purpose. Every key below is missing from an asset made before the
        // field existed, and a missing key reads back as zero, so without Resolve a catalog would hand the
        // run zero floors and it would stall on floor 0.
        public MapRules MapRules()
        {
            return MapGenerator.Resolve(new MapRules
            {
                Floors = MapFloors,
                StepsPerFloor = MapStepsPerFloor,
                ProbabilityStrength = MapProbabilityStrength,
                BranchAffinity = MapBranchAffinity,
                // Resolve clones both tables before using them, so handing it the asset's own arrays cannot
                // let a room page write back into the catalog.
                BranchWeights = MapBranchWeights,
                NodeOdds = MapNodeOdds,
                SupplyHealRatio = SupplyHealRatio,
                SuppliesPerBattle = SuppliesPerBattle,
                SuppliesPerElite = SuppliesPerElite,
                SuppliesPerBoss = SuppliesPerBoss,
                EliteHealthBonus = EliteHealthBonus,
                EliteAttackScale = EliteAttackScale,
                BossHealthBonus = BossHealthBonus,
                BossAttackScale = BossAttackScale
            });
        }
    }
}
