using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    [CreateAssetMenu(menuName = "GameMake/Skill")]
    public sealed class SkillDefinition : ScriptableObject { public SkillSpec Spec = new SkillSpec(); }
}
