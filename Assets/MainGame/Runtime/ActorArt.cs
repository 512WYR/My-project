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
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        // Image-based HUDs (the authored Gothic panel) take a Sprite instead of a Texture2D, so the
        // sprite form is cached alongside the texture and built once per Id.
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        public static Texture2D Portrait(ActorSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Id)) return null;
            if (cache.TryGetValue(spec.Id, out var cached)) return cached;
            Texture2D texture = null;
            for (int i = 0; i < Roots.Length && texture == null; i++)
                texture = Load(Roots[i] + spec.Id);
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
            Sprite sprite = null;
            for (int i = 0; i < Roots.Length && sprite == null; i++)
            {
                sprite = Resources.Load<Sprite>(Roots[i] + spec.Id);
                if (sprite != null) continue;
                var texture = Load(Roots[i] + spec.Id);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
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

        public static Texture2D SkillPortrait(string skillId)
        {
            return string.IsNullOrEmpty(skillId) ? null : Show(SkillRoot + skillId);
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
