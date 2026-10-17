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
        // ---- designed-monster data ----
        // Enemy skills are plain serializable SkillSpecs rather than the hero-side SkillDefinition assets:
        // each one is bound to one monster's AI rule, and keeping the pair on the same asset avoids a
        // second lookup table. When this list is non-empty it replaces BasicSkills for the enemy.
        public EnemyBehavior Behavior;
        public SkillSpec[] EnemySkills;
        public int RankWidth = 1;
        public bool Immobile;
        public int ActionInterval = 1;
        public int ActionPhase;
        public int BossHealOnHeroDeath;
        public double GuardReduction;
        // RandomSummoner only: the normal monsters it may turn into at battle start.
        public CharacterDefinition[] SummonPool;
        public ActorSpec ToSpec()
        {
            var enemySkills = EnemySkills != null && EnemySkills.Length > 0 ? EnemySkills : Specs(BasicSkills);
            return new ActorSpec { Id = Id, Name = DisplayName, Subtitle = Subtitle, Silhouette = Silhouette, MaxHp = MaxHp, Speed = Speed,
                Attack = Attack, Accuracy = Accuracy, CritChance = CritChance, Dodge = Dodge, Defense = Defense,
                SanityOffset = SanityOffset, Pattern = EnemyPattern,
                Behavior = Behavior, RankWidth = Mathf.Max(1, RankWidth), Immobile = Immobile,
                ActionInterval = Mathf.Max(1, ActionInterval), ActionPhase = ActionPhase,
                BossHealOnHeroDeath = BossHealOnHeroDeath, GuardReduction = GuardReduction,
                SummonPool = Specs(SummonPool),
                BasicSkills = enemySkills, PrimalSkills = Specs(PrimalSkills), TechnicalSkills = Technical(BasicSkills, TechnicalSpecial) };
        }

        private static ActorSpec[] Specs(CharacterDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0) return new ActorSpec[0];
            var list = new List<ActorSpec>();
            foreach (var definition in definitions)
                if (definition) list.Add(definition.ToSpec());
            return list.ToArray();
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
