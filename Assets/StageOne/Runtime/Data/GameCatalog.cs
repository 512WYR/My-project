using System.Linq;
using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    [CreateAssetMenu(menuName = "GameMake/Stage One Catalog")]
    public sealed class GameCatalog : ScriptableObject
    {
        public CharacterDefinition[] Heroes;
        public CharacterDefinition[] Enemies;
        public MentalDefinition[] MentalStates;
        [Range(0, 3)] public float ProbabilityStrength = 1.5f;
        [Range(0, 1)] public float FeedbackFactor = 0.5f;
        [Range(0, 1)] public float DeathChance = 0.25f;
        [Range(1, 3)] public float CritMultiplier = 1.5f;
        public int MindStep = 15;
        [Header("地图生成")]
        public int MapFloors = 10;
        [Range(0, 3)] public float MapProbabilityStrength = 1f;
        public float MapBranchAffinity = 1f;
        [Range(1, 3)] public int MapOpeningBranches = 3;

        public CombatRules Rules()
        {
            return new CombatRules { ProbabilityStrength = ProbabilityStrength, FeedbackFactor = FeedbackFactor,
                DeathChance = DeathChance, CritMultiplier = CritMultiplier, MindStep = MindStep, MentalStates = MentalStates.Select(m => m.Spec).ToArray() };
        }

        // The per-type weight table stays in MapGenerator.DefaultOdds; only the knobs live here, the same
        // way TechnicalWeight/BasicWeight/PrimalWeight stay in CombatRules.
        public MapRules MapRules()
        {
            return new MapRules { Floors = MapFloors, ProbabilityStrength = MapProbabilityStrength,
                OpeningBranchCount = MapOpeningBranches, BranchAffinity = MapBranchAffinity };
        }
    }
}
