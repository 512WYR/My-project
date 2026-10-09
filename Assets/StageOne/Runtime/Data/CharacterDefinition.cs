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
        // Character combat attributes. Static per character; never derived from sanity.
        public int Attack = 10;
        public double Accuracy = 0.9;
        public double CritChance = 0.1;
        public double Dodge;
        public double Defense;
        public SkillDefinition Primary, Support, Technical, Primal;
        public ActorSpec ToSpec()
        {
            return new ActorSpec { Id = Id, Name = DisplayName, Subtitle = Subtitle, Role = Role, MaxHp = MaxHp, Speed = Speed,
                Attack = Attack, Accuracy = Accuracy, CritChance = CritChance, Dodge = Dodge, Defense = Defense,
                SanityOffset = SanityOffset, Pattern = EnemyPattern, Primary = Primary ? Primary.Spec : null,
                Support = Support ? Support.Spec : null, Technical = Technical ? Technical.Spec : null, Primal = Primal ? Primal.Spec : null };
        }
    }
}
