using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    [CreateAssetMenu(menuName = "GameMake/Event")]
    public sealed class EventDefinition : ScriptableObject { public EventSpec Spec = new EventSpec(); }
}
