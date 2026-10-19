using Ruin.Core;
using Ruin.Runtime;
using UnityEditor;
using UnityEngine;

namespace Ruin.Editor
{
    /// <summary>
    /// Heroes and monsters share one CharacterDefinition asset, but they use disjoint halves of it.
    /// Drawing every field for both types made it far too easy to fill in a monster's sanity or a hero's
    /// rank width, so this inspector shows only the common block plus the selected kind's block.
    /// The asset is still one type and the runtime still reads one ActorSpec; this is authoring sugar.
    /// </summary>
    [CustomEditor(typeof(CharacterDefinition))]
    public sealed class CharacterDefinitionEditor : UnityEditor.Editor
    {
        private static readonly string[] Common =
        {
            "Id", "DisplayName", "Subtitle", "Silhouette", "MaxHp", "Speed",
            "Attack", "Accuracy", "CritChance", "Dodge", "Defense"
        };

        private static readonly string[] HeroOnly =
        {
            "SanityOffset", "BasicSkills", "PrimalSkills", "TechnicalSpecial"
        };

        private static readonly string[] MonsterOnly =
        {
            "EnemyPattern", "Behavior", "EnemySkills", "RankWidth", "Immobile",
            "ActionInterval", "ActionPhase", "BossHealOnHeroDeath", "GuardReduction", "SummonPool"
        };

        public override void OnInspectorGUI()
        {
            var definition = (CharacterDefinition)target;
            serializedObject.Update();

            var kindProperty = serializedObject.FindProperty("Kind");
            EditorGUILayout.PropertyField(kindProperty, new GUIContent("类型 / Kind"));
            var kind = (ActorKind)kindProperty.enumValueIndex;

            if (kind == ActorKind.Unset)
            {
                EditorGUILayout.HelpBox(
                    "类型尚未指定。请运行 GameMake/Assign Character Kinds（或 GameMake/Prepare Stage One）完成迁移。"
                    + "\nUnset 会保留旧的共用模板行为，暂时仍显示全部字段。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            Section("通用 / Common");
            DrawAll(Common);

            if (kind == ActorKind.Hero || kind == ActorKind.Unset)
            {
                EditorGUILayout.Space();
                Section("英雄专用 / Hero only");
                DrawAll(HeroOnly);
            }

            if (kind == ActorKind.Monster || kind == ActorKind.Unset)
            {
                EditorGUILayout.Space();
                Section("怪物专用 / Monster only");
                DrawAll(MonsterOnly);
            }

            serializedObject.ApplyModifiedProperties();

            if (kind != ActorKind.Unset) DrawWarnings(definition, kind);
            if (kind != ActorKind.Unset) DrawCleanup(definition, kind);
        }

        private void DrawAll(string[] names)
        {
            foreach (var name in names)
            {
                var property = serializedObject.FindProperty(name);
                if (property != null) EditorGUILayout.PropertyField(property, true);
            }
        }

        private static void Section(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        // Warnings are advisory only. ToSpec already coerces these fields, so a stray value cannot change
        // runtime behaviour — it only means the asset is lying to whoever reads it next.
        private static void DrawWarnings(CharacterDefinition definition, ActorKind kind)
        {
            if (definition.RankWidth < 1 || definition.RankWidth > 4)
                EditorGUILayout.HelpBox("RankWidth 应在 1～4 之间。", MessageType.Error);

            if (kind == ActorKind.Hero && HasMonsterData(definition))
                EditorGUILayout.HelpBox(
                    "英雄资产包含怪物字段（RankWidth / Immobile / Behavior / EnemySkills / ActionInterval / SummonPool 等）。"
                    + "ToSpec 会忽略它们；可点下面的按钮清空。",
                    MessageType.Warning);

            if (kind == ActorKind.Monster && HasHeroData(definition))
                EditorGUILayout.HelpBox(
                    "怪物资产包含英雄字段（SanityOffset / BasicSkills / PrimalSkills / TechnicalSpecial）。"
                    + "ToSpec 会忽略它们；可点下面的按钮清空。",
                    MessageType.Warning);
        }

        private static bool HasMonsterData(CharacterDefinition definition)
        {
            return definition.RankWidth != 1
                || definition.Immobile
                || definition.Behavior != EnemyBehavior.Simple
                || (definition.EnemySkills != null && definition.EnemySkills.Length > 0)
                || definition.EnemyPattern != EnemyPattern.Cleaver
                || definition.ActionInterval != 1
                || definition.ActionPhase != 0
                || definition.BossHealOnHeroDeath != 0
                || definition.GuardReduction != 0
                || (definition.SummonPool != null && definition.SummonPool.Length > 0);
        }

        private static bool HasHeroData(CharacterDefinition definition)
        {
            return definition.SanityOffset != 0
                || (definition.BasicSkills != null && definition.BasicSkills.Length > 0)
                || (definition.PrimalSkills != null && definition.PrimalSkills.Length > 0)
                || definition.TechnicalSpecial != null;
        }

        private static void DrawCleanup(CharacterDefinition definition, ActorKind kind)
        {
            EditorGUILayout.Space();
            string label = kind == ActorKind.Hero ? "清空怪物字段" : "清空英雄字段";
            if (!GUILayout.Button(label)) return;

            string action = kind == ActorKind.Hero ? "Clear monster fields" : "Clear hero fields";
            Undo.RecordObject(definition, action);
            if (kind == ActorKind.Hero)
            {
                definition.EnemyPattern = EnemyPattern.Cleaver;
                definition.Behavior = EnemyBehavior.Simple;
                definition.EnemySkills = new SkillSpec[0];
                definition.RankWidth = 1;
                definition.Immobile = false;
                definition.ActionInterval = 1;
                definition.ActionPhase = 0;
                definition.BossHealOnHeroDeath = 0;
                definition.GuardReduction = 0;
                definition.SummonPool = new CharacterDefinition[0];
            }
            else
            {
                definition.SanityOffset = 0;
                definition.BasicSkills = new SkillDefinition[0];
                definition.PrimalSkills = new SkillDefinition[0];
                definition.TechnicalSpecial = null;
            }
            EditorUtility.SetDirty(definition);
        }
    }
}
