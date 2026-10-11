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
        public bool ProstheticFitted { get { return !string.IsNullOrEmpty(Prosthetic); } }
    }

    [Serializable]
    public sealed class ProstheticSpec
    {
        public string Id;
        public string Name;
        public LimbPart Part;
        public string Description;
        public ProstheticSpec() { }
        public ProstheticSpec(string id, string name, LimbPart part, string description)
        { Id = id; Name = name; Part = part; Description = description; }
    }

    // Which part an enemy action mauls, and how often. Only the enemies that physically strike carry one.
    [Serializable]
    public sealed class LimbAttackSpec
    {
        public LimbPart Part = LimbPart.Arm;
        public double Chance = 0.5;
        public bool HeavyRoundsOnly; // The cleaver mauls only on its heavy swing, which lands on even rounds.
        public LimbAttackSpec() { }
        public LimbAttackSpec(LimbPart part, double chance, bool heavyRoundsOnly)
        { Part = part; Chance = chance; HeavyRoundsOnly = heavyRoundsOnly; }
    }

    [Serializable]
    public sealed class LimbRules
    {
        // One limb hit removes ceil(part max * HitRatio). At the default 0.5 that reads as: one hit is an
        // injury, two hits sever the part.
        public double HitRatio = 0.5;
        public LimbPartRule[] Parts;          // Null falls back to LimbSystem.DefaultParts.
        public ProstheticSpec[] Prosthetics;  // Null falls back to LimbSystem.DefaultProsthetics.
    }
}
