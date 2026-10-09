using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    public enum HeroRole { Guard, Scout, Arbalist, Medic, Enemy }
    public enum SkillForm { Technical, Basic, Primal }
    public enum MentalState { None, Composed, Agitated }
    public enum SkillEffect { Attack, Heal, Protect }
    public enum TargetSide { Enemy, Ally }
    public enum ActionKind { Primary, Support, Technical, Defend, Swap, Focus, Embrace, Retreat }
    public enum BattleOutcome { Active, Victory, Retreat, Defeat }
    public enum EnemyPattern { Cleaver, Whisperer, Hunter }

    [Serializable]
    public sealed class SkillSpec
    {
        public string Id;
        public string Name;
        public string Description;
        public SkillEffect Effect;
        public TargetSide Target;
        public double Multiplier = 1.0; // Damage or healing is BaseAttack * Multiplier.
        public int ActorRanks = 15;
        public int TargetRanks = 15;
        public int Uses; // 0 means unlimited
        public int Bleed;
        public bool Interrupt;
        public bool Cleanse;
        public double Accuracy = 0.9;
    }

    [Serializable]
    public sealed class ActorSpec
    {
        public string Id;
        public string Name;
        public string Subtitle;
        public HeroRole Role;
        public int MaxHp;
        public int Speed;
        // Character combat attributes. They are static per character and never derived from sanity.
        public int Attack = 10;
        public double Accuracy = 0.9;
        public double CritChance = 0.1;
        public double Dodge;
        public double Defense;
        public double SanityOffset;
        public EnemyPattern Pattern;
        public SkillSpec Primary;
        public SkillSpec Support;
        public SkillSpec Technical;
        public SkillSpec Primal;
    }

    [Serializable]
    public sealed class MentalSpec
    {
        public MentalState State;
        public string Name;
        public double TechnicalMultiplier = 1;
        public double PrimalMultiplier = 1;
        public double AccuracyModifier;
    }

    [Serializable]
    public sealed class CombatRules
    {
        public double ProbabilityStrength = 1.5;
        public double FeedbackFactor = 0.5;
        public double DeathChance = 0.25;
        public int MindStep = 15;
        public double TechnicalWeight = 0.2;
        public double BasicWeight = 0.6;
        public double PrimalWeight = 0.2;
        public double CritMultiplier = 1.5;
        public double SkillAccuracyBaseline = 0.9; // Skill accuracy is expressed relative to this baseline.
        public MentalSpec[] MentalStates = {
            new MentalSpec { State = MentalState.None, Name = "平稳" },
            new MentalSpec { State = MentalState.Composed, Name = "凝神", TechnicalMultiplier = 1.25 },
            new MentalSpec { State = MentalState.Agitated, Name = "躁动", PrimalMultiplier = 1.25, AccuracyModifier = -0.05 }
        };
        public MentalSpec Mental(MentalState state) { return MentalStates.First(x => x.State == state); }
    }

    [Serializable]
    public sealed class ActorState
    {
        public string Id;
        public ActorSpec Definition;
        public bool Player;
        public int Rank;
        public int Hp;
        public bool Dead;
        public double Sanity;
        public MentalState Mental;
        public SkillForm Form = SkillForm.Basic;
        public int BleedDamage;
        public int BleedTurns;
        public int Interrupted;
        public bool Defending;
        public string ProtectorId;
        public int FocusUses = 2;
        public int EmbraceUses = 2;
        public int SupportUses;
        public int Speed { get { return Definition.Speed; } }
        public string Name { get { return Definition.Name; } }
        public bool DeathDoor { get { return Player && !Dead && Hp <= 0; } }
    }

    [Serializable]
    public sealed class RunState
    {
        public double PlayerSanity = 50;
        public uint Seed;
        public RunRandom Random;
        public int Battles;
        public List<ActorState> Heroes = new List<ActorState>();
        public MapState Map = new MapState();

        public static RunState Create(ActorSpec[] heroes, uint seed, double? sanityPreset = null)
        {
            var run = new RunState { Seed = seed, Random = new RunRandom(seed), Map = new MapState() };
            for (int i = 0; i < heroes.Length; i++)
            {
                var spec = heroes[i];
                run.Heroes.Add(new ActorState { Id = spec.Id, Definition = spec, Player = true, Rank = i,
                    Hp = spec.MaxHp, Sanity = SanityProbability.Clamp(sanityPreset ?? (run.PlayerSanity + spec.SanityOffset), 0, 100) });
            }
            return run;
        }
    }

    public sealed class FeedbackEntry
    {
        public string Id;
        public string Name;
        public double Start;
        public double End;
        public bool Dead;
        public double Delta { get { return End - Start; } }
    }

    public sealed class BattleSettlement
    {
        public BattleOutcome Outcome;
        public readonly List<FeedbackEntry> Entries = new List<FeedbackEntry>();
        public double Before;
        public double RawDelta;
        public double After;
        public double AppliedDelta { get { return After - Before; } }
    }

    public sealed class BattleEvent
    {
        public string Message;
        public string ActorId;
        public string TargetId;
        public int Amount;
        public string Kind;
    }

    public sealed class ActionRequest
    {
        public ActionKind Kind;
        public string TargetId;
        public ActionRequest(ActionKind kind, string targetId = null) { Kind = kind; TargetId = targetId; }
    }
}
