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
        public CombatRules Rules()
        {
            return new CombatRules { ProbabilityStrength = ProbabilityStrength, FeedbackFactor = FeedbackFactor,
                DeathChance = DeathChance, CritMultiplier = CritMultiplier, MindStep = MindStep, MentalStates = MentalStates.Select(m => m.Spec).ToArray() };
        }
    }
}
