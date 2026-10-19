using System.Collections.Generic;
using Ruin.Core;
using UnityEngine;
namespace Ruin.Runtime
{
    // Authoring-only split. The runtime still uses one ActorSpec for both sides, so this enum never reaches
    // BattleEngine; it only decides which half of the shared asset the inspector shows and which fields
    // ToSpec is allowed to copy. Unset keeps the pre-split behaviour for assets that have not been migrated.
    public enum ActorKind { Unset = 0, Hero = 1, Monster = 2 }

    [CreateAssetMenu(menuName = "GameMake/Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [Tooltip("Hero or Monster. Run GameMake/Assign Character Kinds once to backfill existing assets.")]
        public ActorKind Kind;

        [Header("Common / 通用")]
        public string Id;
        public string DisplayName;
        public string Subtitle;
        // Replaces the old profession: the silhouette key the view draws, e.g. "warrior"/"shield"/"gunner"/"medic".
        public string Silhouette;
        public int MaxHp = 30;
        public int Speed = 5;
        // Character combat attributes. Static per character; never derived from sanity.
        public int Attack = 10;
        public double Accuracy = 0.9;
        public double CritChance = 0.1;
        public double Dodge;
        public double Defense;

        [Header("Hero only / 英雄专用")]
        public double SanityOffset;
        // Three forms' skill lists. Basic is the normal kit; Primal is the low-sanity reshuffle; Technical
        // is the high-sanity kit, stored here as just the extra skill so ToSpec can union it with Basic.
        public SkillDefinition[] BasicSkills;
        public SkillDefinition[] PrimalSkills;
        public SkillDefinition TechnicalSpecial;

        [Header("Monster only / 怪物专用")]
        // Monsters have no sanity and no limb track. There are deliberately no limb fields here: the
        // battle only builds Limbs for heroes, and LimbSystem.Damage refuses non-Player actors outright,
        // so every monster is immune to dismemberment.
        // Legacy pattern enemies (the three stage-one dummies) keep their scripted behaviour.
        public EnemyPattern EnemyPattern;
        // ---- designed-monster data ----
        // Enemy skills are plain serializable SkillSpecs rather than the hero-side SkillDefinition assets:
        // each one is bound to one monster's AI rule, and keeping the pair on the same asset avoids a
        // second lookup table. When this list is non-empty it replaces BasicSkills for the enemy.
        public EnemyBehavior Behavior;
        public SkillSpec[] EnemySkills;
        // Ranks the actor occupies. 1 is a normal unit; 2-4 lets a boss cover several positions. The
        // battle engine already reads this through ActorSpec.Width.
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
            bool isHero = Kind == ActorKind.Hero;
            bool isMonster = Kind == ActorKind.Monster;
            // Unset keeps the original shared-template behaviour, so an asset that has not been migrated
            // still produces exactly the same spec as before.
            var enemySkills = EnemySkills != null && EnemySkills.Length > 0 ? EnemySkills : Specs(BasicSkills);
            var basicSkills = isHero ? Specs(BasicSkills) : enemySkills;
            return new ActorSpec
            {
                Id = Id, Name = DisplayName, Subtitle = Subtitle, Silhouette = Silhouette,
                MaxHp = MaxHp, Speed = Speed,
                Attack = Attack, Accuracy = Accuracy, CritChance = CritChance, Dodge = Dodge, Defense = Defense,
                // Monsters have no sanity at all; the offset is a hero-only field.
                SanityOffset = isMonster ? 0 : SanityOffset,
                Pattern = EnemyPattern,
                Behavior = isHero ? EnemyBehavior.Simple : Behavior,
                RankWidth = isHero ? 1 : Mathf.Max(1, RankWidth),
                Immobile = !isHero && Immobile,
                ActionInterval = isHero ? 1 : Mathf.Max(1, ActionInterval),
                ActionPhase = isHero ? 0 : ActionPhase,
                BossHealOnHeroDeath = isHero ? 0 : BossHealOnHeroDeath,
                GuardReduction = isHero ? 0 : GuardReduction,
                SummonPool = isHero ? new ActorSpec[0] : Specs(SummonPool),
                BasicSkills = basicSkills,
                // Hero skill forms are meaningless for a monster and vice versa.
                PrimalSkills = isMonster ? ActorSpec.EmptySkills : Specs(PrimalSkills),
                TechnicalSkills = isMonster ? ActorSpec.EmptySkills : Technical(BasicSkills, TechnicalSpecial)
            };
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
