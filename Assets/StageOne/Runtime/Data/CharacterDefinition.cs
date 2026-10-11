using System.Collections.Generic;
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
        // Replaces the old profession: the silhouette key the view draws, e.g. "warrior"/"shield"/"gunner"/"medic".
        public string Silhouette;
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
        // Three forms' skill lists. Basic is the normal kit; Primal is the low-sanity reshuffle; Technical
        // is the high-sanity kit, stored here as just the extra skill so ToSpec can union it with Basic.
        public SkillDefinition[] BasicSkills;
        public SkillDefinition[] PrimalSkills;
        public SkillDefinition TechnicalSpecial;
        public ActorSpec ToSpec()
        {
            return new ActorSpec { Id = Id, Name = DisplayName, Subtitle = Subtitle, Silhouette = Silhouette, MaxHp = MaxHp, Speed = Speed,
                Attack = Attack, Accuracy = Accuracy, CritChance = CritChance, Dodge = Dodge, Defense = Defense,
                SanityOffset = SanityOffset, Pattern = EnemyPattern,
                BasicSkills = Specs(BasicSkills), PrimalSkills = Specs(PrimalSkills), TechnicalSkills = Technical(BasicSkills, TechnicalSpecial) };
        }

        private static SkillSpec[] Specs(SkillDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0) return ActorSpec.EmptySkills;
            var list = new List<SkillSpec>();
            foreach (var definition in definitions)
                if (definition && definition.Spec != null) list.Add(definition.Spec);
            return list.ToArray();
        }

        // TechnicalSkills is deliberately stored as "Basic ∪ {special}" so the high-sanity kit can never
        // accidentally drop a normal skill.
        private static SkillSpec[] Technical(SkillDefinition[] basics, SkillDefinition special)
        {
            var list = new List<SkillSpec>();
            if (basics != null)
                foreach (var definition in basics)
                    if (definition && definition.Spec != null) list.Add(definition.Spec);
            if (special && special.Spec != null) list.Add(special.Spec);
            return list.ToArray();
        }
    }
}
