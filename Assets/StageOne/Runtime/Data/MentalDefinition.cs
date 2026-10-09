using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    [CreateAssetMenu(menuName = "GameMake/Mental State")]
    public sealed class MentalDefinition : ScriptableObject { public MentalSpec Spec = new MentalSpec(); }
}
