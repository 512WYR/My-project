using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    [CreateAssetMenu(menuName = "GameMake/Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public string Subtitle;
        public HeroRole Role;
        public int MaxHp = 30;
        public int Speed = 5;
        public double SanityOffset;
        public EnemyPattern EnemyPattern;
        public SkillDefinition Primary, Support, Technical, Primal;
        public ActorSpec ToSpec()
        {
            return new ActorSpec { Id = Id, Name = DisplayName, Subtitle = Subtitle, Role = Role, MaxHp = MaxHp, Speed = Speed,
                SanityOffset = SanityOffset, Pattern = EnemyPattern, Primary = Primary ? Primary.Spec : null,
                Support = Support ? Support.Spec : null, Technical = Technical ? Technical.Spec : null, Primal = Primal ? Primal.Spec : null };
        }
    }
}
