using System.Collections.Generic;
using UnityEngine;

namespace Ruin.Runtime
{
    // How a family of skills performs in the cut-in. The style only changes timing and effects; the
    // numbers and target list still come from BattleShowResult, so presentation never affects combat.
    public enum SkillShowStyle
    {
        Default,
        Ranged,
        Pierce,
        Scatter,
        Grapple,
        Melee,
        Heavy,
        Sweep,
        Guard,
        Buff,
        Heal,
        Cleanse,
        Drain
    }

    public sealed class SkillShowProfile
    {
        public SkillShowStyle Style = SkillShowStyle.Default;
        // Lands when the numbers appear. SfxDirector walks its own fallback chain when the file is
        // missing, so this table can be written before every clip exists.
        public SfxCue Hit = SfxCue.AttackHit;
        public SfxCue? Extra;
        public float ExtraGain = 0.7f;
        public float Pitch = 1f;
        // Negative numbers pull the actor away from the target before the strike.
        public float Windup = -36f;
        // Ranged only: how far the actor leans towards the target instead of dashing all the way.
        public float HitOffset = 42f;
        public float Recoil = 1f;
        public float ExtraHold;
        public bool Shake;
        public Color Trail = new Color(1f, 0.9f, 0.72f, 0.95f);

        public bool Ranged
        {
            get
            {
                return Style == SkillShowStyle.Ranged || Style == SkillShowStyle.Pierce
                    || Style == SkillShowStyle.Scatter || Style == SkillShowStyle.Grapple
                    || Style == SkillShowStyle.Drain;
            }
        }

        public bool Slash
        {
            get
            {
                return Style == SkillShowStyle.Melee || Style == SkillShowStyle.Heavy
                    || Style == SkillShowStyle.Sweep;
            }
        }

        public bool Support
        {
            get
            {
                return Style == SkillShowStyle.Guard || Style == SkillShowStyle.Buff
                    || Style == SkillShowStyle.Heal || Style == SkillShowStyle.Cleanse;
            }
        }
    }

    // The single table described in Docs/技能演出与音效映射表.md. Ids that are not listed (enemy skills,
    // counters, follow-ups) use the Default profile, which is the old charge-and-dash behaviour.
    public static class SkillPresentation
    {
        private static readonly SkillShowProfile DefaultProfile = new SkillShowProfile();
        private static readonly Dictionary<string, SkillShowProfile> Table = Build();

        public static SkillShowProfile For(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return DefaultProfile;
            SkillShowProfile profile;
            return Table.TryGetValue(skillId, out profile) ? profile : DefaultProfile;
        }

        private static Dictionary<string, SkillShowProfile> Build()
        {
            var t = new Dictionary<string, SkillShowProfile>();

            // ---- gunner ----
            Add(t, "g_shot", SkillShowStyle.Ranged, SfxCue.Gunshot, 1.10f, -14f, 40f);
            Add(t, "g_block_shot", SkillShowStyle.Ranged, SfxCue.Gunshot, 0.95f, -16f, 34f, 1f, 0.04f);
            Add(t, "g_resupply", SkillShowStyle.Buff, SfxCue.Buff);
            Add(t, "g_grapple", SkillShowStyle.Grapple, SfxCue.Gunshot, 0.95f, -18f, 38f, 1f, 0.02f,
                false, null, SfxCue.GrappleChain, 0.65f);
            Add(t, "g_primal_shot", SkillShowStyle.Scatter, SfxCue.GunshotScatter, 1f, -18f, 50f, 1.10f, 0.06f);
            Add(t, "g_pierce", SkillShowStyle.Pierce, SfxCue.Gunshot, 0.85f, -20f, 46f, 1.15f, 0.08f);

            // ---- shield ----
            Add(t, "s_strike", SkillShowStyle.Melee, SfxCue.ShieldBash, 1f, -42f, 0f);
            Add(t, "s_guard", SkillShowStyle.Guard, SfxCue.DefendUp);
            Add(t, "s_bulwark", SkillShowStyle.Buff, SfxCue.Buff);
            Add(t, "s_block", SkillShowStyle.Guard, SfxCue.Block);
            Add(t, "s_primal_guard", SkillShowStyle.Guard, SfxCue.DefendUp, 0.9f);
            Add(t, "s_phalanx", SkillShowStyle.Buff, SfxCue.Buff, 0.95f, -36f, 42f, 1f, 0.04f,
                false, null, SfxCue.Block, 0.6f);

            // ---- warrior ----
            Add(t, "w_cleave", SkillShowStyle.Melee, SfxCue.Slash, 1f, -42f, 0f);
            Add(t, "w_bash", SkillShowStyle.Heavy, SfxCue.HeavyHit, 0.95f, -48f, 0f, 1.05f, 0.04f, true);
            Add(t, "w_sweep", SkillShowStyle.Sweep, SfxCue.Slash, 1f, -44f, 0f, 1.05f, 0.03f);
            Add(t, "w_gambit", SkillShowStyle.Heavy, SfxCue.HeavyHit, 0.88f, -52f, 0f, 1.10f, 0.06f, true);
            Add(t, "w_primal_sweep", SkillShowStyle.Sweep, SfxCue.HeavyHit, 0.92f, -46f, 0f, 1.15f, 0.06f, true);
            Add(t, "w_pursuit", SkillShowStyle.Buff, SfxCue.Buff);

            // ---- medic ----
            Add(t, "m_bloodlet", SkillShowStyle.Melee, SfxCue.Slash, 0.95f, -40f, 0f, 0.9f, 0f, false,
                null, SfxCue.Bleed, 0.55f);
            Add(t, "m_blood_heal", SkillShowStyle.Heal, SfxCue.Heal);
            Add(t, "m_mist", SkillShowStyle.Heal, SfxCue.Heal, 0.98f);
            Add(t, "m_cleanse", SkillShowStyle.Cleanse, SfxCue.Cleanse);
            Add(t, "m_primal_heal", SkillShowStyle.Heal, SfxCue.Heal, 0.94f);
            Add(t, "m_drain", SkillShowStyle.Drain, SfxCue.AttackHit, 0.95f, -20f, 44f, 1f, 0.04f,
                false, null, SfxCue.Heal, 0.6f);

            // ---- prosthetic replacements ----
            Add(t, "g_whaling_anchor", SkillShowStyle.Grapple, SfxCue.GunshotHeavy, 0.82f, -20f, 42f, 1.25f,
                0.06f, false, null, SfxCue.GrappleChain, 0.7f);
            Add(t, "s_double_guard", SkillShowStyle.Guard, SfxCue.DefendUp);
            Add(t, "s_double_guard_low", SkillShowStyle.Guard, SfxCue.DefendUp, 0.9f);
            Add(t, "w_boiler_shock", SkillShowStyle.Heavy, SfxCue.HeavyHit, 0.92f, -46f, 0f, 1.10f, 0.05f,
                true, null, SfxCue.SteamBurst, 0.7f);
            Add(t, "m_boiling_mist", SkillShowStyle.Heal, SfxCue.Heal, 0.96f, -36f, 42f, 1f, 0.04f,
                false, null, SfxCue.Bleed, 0.55f);

            return t;
        }

        private static void Add(Dictionary<string, SkillShowProfile> table, string id, SkillShowStyle style,
            SfxCue hit, float pitch = 1f, float windup = -36f, float hitOffset = 42f, float recoil = 1f,
            float hold = 0f, bool shake = false, Color? trail = null,
            SfxCue? extra = null, float extraGain = 0.7f)
        {
            table[id] = new SkillShowProfile
            {
                Style = style,
                Hit = hit,
                Extra = extra,
                ExtraGain = extraGain,
                Pitch = pitch,
                Windup = windup,
                HitOffset = hitOffset,
                Recoil = recoil,
                ExtraHold = hold,
                Shake = shake,
                Trail = trail ?? TrailFor(style)
            };
        }

        private static Color TrailFor(SkillShowStyle style)
        {
            switch (style)
            {
                case SkillShowStyle.Pierce: return new Color(0.58f, 0.86f, 1f, 0.95f);
                case SkillShowStyle.Scatter: return new Color(1f, 0.72f, 0.32f, 0.95f);
                case SkillShowStyle.Grapple: return new Color(0.82f, 0.72f, 0.48f, 0.95f);
                case SkillShowStyle.Melee: return new Color(1f, 0.96f, 0.86f, 0.95f);
                case SkillShowStyle.Heavy: return new Color(1f, 0.7f, 0.38f, 0.95f);
                case SkillShowStyle.Sweep: return new Color(0.78f, 0.92f, 1f, 0.95f);
                case SkillShowStyle.Guard: return new Color(0.58f, 0.76f, 0.98f, 0.9f);
                case SkillShowStyle.Buff: return new Color(0.92f, 0.78f, 0.42f, 0.9f);
                case SkillShowStyle.Heal: return new Color(0.48f, 0.88f, 0.58f, 0.9f);
                case SkillShowStyle.Cleanse: return new Color(0.94f, 0.96f, 1f, 0.9f);
                case SkillShowStyle.Drain: return new Color(0.9f, 0.22f, 0.24f, 0.95f);
                default: return new Color(1f, 0.9f, 0.72f, 0.95f);
            }
        }
    }
}
