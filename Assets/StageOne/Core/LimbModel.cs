using System;

namespace Ruin.Core
{
    // Only the player side has limbs. Left and right are merged into one part each, so a hero carries
    // exactly four: head, torso, arm and leg.
    public enum LimbPart { Head, Torso, Arm, Leg }

    // Intact above half health, injured at or below half, severed at zero.
    public enum LimbCondition { Intact, Injured, Severed }

    [Serializable]
    public sealed class LimbPartRule
    {
        public LimbPart Part;
        public double HpRatio = 0.35;        // Part max health is ceil(character MaxHp * HpRatio).
        public double InjuredPenalty = 0.20; // Share of the mapped attribute lost while injured.
        public double SeveredPenalty = 0.45; // Share lost while severed and not yet replaced.
        public LimbPartRule() { }
        public LimbPartRule(LimbPart part, double hpRatio, double injuredPenalty, double severedPenalty)
        {
            Part = part; HpRatio = hpRatio; InjuredPenalty = injuredPenalty; SeveredPenalty = severedPenalty;
        }
    }

    [Serializable]
    public sealed class LimbState
    {
        public LimbPart Part;
        public int Hp;
        public int MaxHp;
        public string Prosthetic; // Installed prosthetic id, null while the part is still flesh.
        // What kind of prosthetic is fitted. Conventional carries no combat effect; every other value is
        // read by ProstheticSystem. Kept next to the id so an item can be destroyed and the limb can fall
        // back to flesh without looking anything up.
        public ProstheticKind ProstheticKind;
        public bool ProstheticFitted { get { return !string.IsNullOrEmpty(Prosthetic); } }
    }

    [Serializable]
    public sealed class ProstheticSpec
    {
        public string Id;
        public string Name;
        public LimbPart Part;
        public string Description;
        public ProstheticKind Kind;
        public string RestrictSilhouette; // "gunner"/"shield"/"warrior"/"medic", or null for everyone.
        public ProstheticSpec() { }
        public ProstheticSpec(string id, string name, LimbPart part, string description)
        { Id = id; Name = name; Part = part; Description = description; }
        public ProstheticSpec(string id, string name, LimbPart part, string description,
            ProstheticKind kind, string restrictSilhouette = null)
        {
            Id = id; Name = name; Part = part; Description = description;
            Kind = kind; RestrictSilhouette = restrictSilhouette;
        }
    }

    [Serializable]
    public sealed class LimbRules
    {
        // One limb hit removes ceil(part max * HitRatio). At the default 0.5 that reads as: one hit is an
        // injury, two hits sever the part. Used by the debug tools and by accessories that spend a limb.
        public double HitRatio = 0.5;
        // Every landed enemy attack also removes ceil(actual HP damage * DamageRatio) from one random
        // intact part. At the default 0.5 a 14-damage elite takes 7 off a part, while a 3-damage minion
        // only chips it. Zero or invalid falls back to the default.
        public double DamageRatio = 0.5;
        public LimbPartRule[] Parts;          // Null falls back to LimbSystem.DefaultParts.
        public ProstheticSpec[] Prosthetics;  // Null falls back to LimbSystem.DefaultProsthetics.
    }
}
