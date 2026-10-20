using System;
using System.Collections.Generic;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Hero and monster portraits are looked up by the same Id the character asset uses, so adding art is
    // just dropping <Id>.png into the matching Resources folder: no ScriptableObject needs a sprite
    // reference and no catalog rebuild is required. A missing file returns null and the card keeps its
    // vector silhouette.
    public static class ActorArt
    {
        // Heroes are checked first, then monsters. Ids are unique per side, so one cache covers both.
        private static readonly string[] Roots = { "StageOne/HeroArt/", "StageOne/EnemyArt/" };
        // A boss copy wears the hero's portrait in black and white, so the duplicate reads as a copy at
        // a glance. The card and the cut-in read these same two brightness values: alive copies keep
        // full brightness, dead ones dim.
        public static readonly Color CopyTint = Color.white;
        public static readonly Color CopyDeadTint = new Color(0.58f, 0.58f, 0.58f, 1f);

        // Graphic.color multiplies the texture and cannot desaturate it, so the copy treatment needs a
        // shader that converts the sampled portrait to luminance. It is authored under a Resources
        // folder so the build includes it even though no material asset references it.
        private static Material copyMaterial;
        // A failed lookup is remembered too: the battle HUD refreshes several times a second and would
        // otherwise retry the Resources load for every copy on every refresh.
        private static bool copyMaterialLookup;

        public static Material CopyMaterial
        {
            get
            {
                if (copyMaterial) return copyMaterial;
                if (copyMaterialLookup) return null;
                copyMaterialLookup = true;
                var shader = Resources.Load<Shader>("CopyGrey");
                if (shader == null) shader = Shader.Find("Ruin/UI Copy Grey");
                if (shader == null) return null;
                copyMaterial = new Material(shader)
                {
                    name = "Copy Grey (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                copyMaterial.SetFloat("_Saturation", 0f);
                copyMaterial.SetFloat("_Brightness", 1.12f);
                return copyMaterial;
            }
        }

        // The vertex colour a copy should use. If the shader is missing (for example it was stripped
        // from a build), fall back to the old multiplicative grey so a copy still looks different.
        public static Color CopyColor(bool dead)
        {
            if (CopyMaterial != null)
                return dead ? CopyDeadTint : CopyTint;
            return dead ? new Color32(138, 138, 138, 255) : new Color32(216, 216, 216, 255);
        }
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        // Image-based HUDs (the authored Gothic panel) take a Sprite instead of a Texture2D, so the
        // sprite form is cached alongside the texture and built once per Id.
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Texture2D Portrait(ActorSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id)) return null;
            if (cache.TryGetValue(spec.Id, out var cached)) return cached;
            Texture2D texture = LoadBody(spec.Id);
            if (texture == null)
            {
                var source = CopySourceId(spec.Id);
                if (source != null) texture = LoadBody(source);
            }
            // Misses are cached too. The battle HUD renders several times a second, and a repeated
            // Resources lookup for the characters that have no art would otherwise hit the asset system.
            cache[spec.Id] = texture;
            return texture;
        }

        // Sprite form of the same file, for Image slots. A single-sprite import is returned as-is; a
        // Texture2D-only import is wrapped in a Sprite so either import mode works.
        public static Sprite PortraitSprite(ActorSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id)) return null;
            if (spriteCache.TryGetValue(spec.Id, out var cached)) return cached;
            Sprite sprite = LoadBodySprite(spec.Id);
            if (sprite == null)
            {
                var source = CopySourceId(spec.Id);
                if (source != null) sprite = LoadBodySprite(source);
            }
            spriteCache[spec.Id] = sprite;
            return sprite;
        }

        // Square bust icons for buttons that open the character panel. They live in their own folder
        // because the full-body portrait above uses the same Id under HeroArt.
        private static readonly string IconRoot = "StageOne/HeroPortrait/";
        private static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();

        public static Sprite PortraitIcon(ActorSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id)) return null;
            if (iconCache.TryGetValue(spec.Id, out var cached)) return cached;
            Sprite sprite = Resources.Load<Sprite>(IconRoot + spec.Id);
            if (sprite == null)
            {
                var texture = Load(IconRoot + spec.Id);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            iconCache[spec.Id] = sprite;
            return sprite;
        }

        // ---- show portraits ----
        // The cut-in swaps the base portrait for the pose that matches the beat: a skill's own art, a
        // monster's attack swing, or a hurt frame on whoever just took the hit. All of them live under
        // Resources/StageOne/Show/<Kind>/<key>. Missing files fall back to the base portrait, so a
        // character only needs the frames that were drawn for it.
        private static readonly string SkillRoot = "StageOne/Show/Skill/";
        private static readonly string AttackRoot = "StageOne/Show/Attack/";
        private static readonly string HurtRoot = "StageOne/Show/Hurt/";
        private static readonly Dictionary<string, Texture2D> showCache = new Dictionary<string, Texture2D>();

        // Prosthetic replacement skills are new Ids that reuse the art of the skill they replace until
        // their own frame is drawn. A direct hit always wins, so dropping <newId>.png in later needs no
        // code change; this table only covers the fallback.
        private static readonly Dictionary<string, string> SkillArtAliases = new Dictionary<string, string>
        {
            { "g_whaling_anchor", "g_grapple" },
            { "s_double_guard", "s_guard" },
            { "s_double_guard_low", "s_primal_guard" },
            { "w_boiler_shock", "w_bash" },
            { "m_boiling_mist", "m_mist" }
        };

        public static Texture2D SkillPortrait(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            var texture = Show(SkillRoot + skillId);
            if (texture != null) return texture;
            string alias;
            return SkillArtAliases.TryGetValue(skillId, out alias) ? Show(SkillRoot + alias) : null;
        }

        public static Texture2D AttackPortrait(ActorSpec spec)
        {
            return spec == null || string.IsNullOrEmpty(spec.Id) ? null : Show(AttackRoot + spec.Id);
        }

        public static Texture2D HurtPortrait(ActorSpec spec)
        {
            return spec == null || string.IsNullOrEmpty(spec.Id) ? null : Show(HurtRoot + spec.Id);
        }

        private static Texture2D Show(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (showCache.TryGetValue(path, out var cached)) return cached;
            showCache[path] = Load(path);
            return showCache[path];
        }

        // A copy boss summons units whose spec id is "copy_<heroId>"; they ship no art of their own
        // and borrow the hero file when the full id has none. The copy boss itself is also called
        // copy_boss, so its own portrait always wins before the prefix is stripped.
        private static string CopySourceId(string id)
        {
            return id != null && id.Length > 5 && id.StartsWith("copy_", StringComparison.Ordinal)
                ? id.Substring(5) : null;
        }

        private static Texture2D LoadBody(string id)
        {
            for (int i = 0; i < Roots.Length; i++)
            {
                var texture = Load(Roots[i] + id);
                if (texture != null) return texture;
            }
            return null;
        }

        private static Sprite LoadBodySprite(string id)
        {
            for (int i = 0; i < Roots.Length; i++)
            {
                var sprite = Resources.Load<Sprite>(Roots[i] + id);
                if (sprite != null) return sprite;
                var texture = Load(Roots[i] + id);
                if (texture != null)
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
            }
            return null;
        }

        private static Texture2D Load(string path)
        {
            var texture = Resources.Load<Texture2D>(path);
            // A 2D project imports a dropped PNG as a Sprite, and depending on the Unity version the
            // Texture2D main object is not always reachable through the typed load. Fall back to the
            // sprite's source texture; a single-sprite import uses the whole image, so 0..1 UVs match.
            if (texture == null)
            {
                var sprite = Resources.Load<Sprite>(path);
                if (sprite != null) texture = sprite.texture;
            }
            return texture;
        }
    }
}
