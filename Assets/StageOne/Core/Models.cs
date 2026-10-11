using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // Characters are defined directly. There is no profession layer any more: a hero is just its own
    // Id/Name/Silhouette plus three skill lists. The three forms survive from the sanity system, they
    // simply changed what they switch: the whole skill list, not a single slot.
    public enum SkillForm { Technical, Basic, Primal }
    public enum MentalState { None, Composed, Agitated }
    public enum SkillEffect { Attack, Heal, Protect, Buff }
    public enum TargetSide { Enemy, Ally }
    // Choose = the player picks one main target. All = the skill sweeps the whole side and needs no pick.
    public enum SkillTargeting { Choose, All }
    // How the main target's own side is swept around it. Party is used together with Targeting.All.
    public enum SkillArea { Single, Adjacent, Splash, Pierce, Party }
    // Buffs a skill can hand to its own side. Kept as flags so one skill can grant several.
    [Flags] public enum AllyBuff { None = 0, Block = 1, Counter = 2, Followup = 4, DefenseUp = 8, AccuracyUp = 16 }
    // A rider is the optional second half of an attack: drain-heal or a sanity top-up.
    public enum RiderKind { None, HealFromDamage, SanityRestore }
    public enum RiderArea { Self, SelfAdjacent, Chosen }
    public enum ActionKind { Skill, Swap }
    public enum BattleOutcome { Active, Victory, Retreat, Defeat }
    public enum EnemyPattern { Cleaver, Whisperer, Hunter }

    [Serializable]
    public sealed class SkillRider
    {
        public RiderKind Kind;
        public RiderArea Area;
        public double Ratio = 1.0;        // HealFromDamage: healing = damage dealt * Ratio.
        public double SpreadRatio = 0.5;  // SelfAdjacent: neighbours heal for the main amount * SpreadRatio.
        public int Amount;                // SanityRestore: flat sanity.
    }

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

        // ---- sweep shape ----
        public SkillTargeting Targeting = SkillTargeting.Choose;
        public SkillArea Area = SkillArea.Single;
        public int AreaMax = 2;          // Splash/Pierce reach beyond the main target.
        public double AreaRatio = 0.5;   // Splash/Adjacent damage as a share of the main hit.
        public double AreaDecay;         // Pierce drops this share of base damage per step back.

        // ---- movement ----
        public int SelfMove;             // Ranks the caster moves by; -1 is one step towards the enemy.
        public bool PullToFront;
        public double PullChance;

        // ---- status applied to the main target ----
        public bool Imbalance;
        public int BleedTurns = 2;       // "Two bleeds" is read as two bleed ticks.
        public double BleedChance;       // 0 means always apply the bleed.

        // ---- wound gambit ----
        public bool IgnoreLimbPenalty;
        public double HpMissingDamageScale; // Extra multiplier for every missing HP fraction.
        public int LimbBonusPerStack;       // Flat damage per injured part.
        public int MaxLimbBonusStacks = 2;
        public double SelfVulnerable;       // Damage the caster takes is raised by this share this round.

        // ---- buffs handed to the caster's side ----
        public AllyBuff Grant;           // Applied to every target of the sweep.
        public AllyBuff SelfGrant;       // Applied to the caster once, regardless of the sweep.
        public double GrantDefense;
        public double GrantAccuracy;
        public int FollowupPercent = 50;

        // ---- randomness ----
        public bool RandomTarget;        // The target is drawn instead of picked.
        public double FriendlyFireChance; // When the sweep finds no second enemy, this may hit a neighbour.

        public SkillRider Rider;
    }

    [Serializable]
    public sealed class ActorSpec
    {
        public string Id;
        public string Name;
        public string Subtitle;
        public string Silhouette; // Replaces HeroRole: "warrior" / "shield" / "gunner" / "medic", or null for enemies.
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
        public SkillSpec[] BasicSkills = EmptySkills;
        public SkillSpec[] PrimalSkills = EmptySkills;
        public SkillSpec[] TechnicalSkills = EmptySkills;

        public static readonly SkillSpec[] EmptySkills = new SkillSpec[0];

        public SkillSpec[] SkillsFor(SkillForm form)
        {
            if (form == SkillForm.Primal) return PrimalSkills ?? EmptySkills;
            if (form == SkillForm.Technical) return TechnicalSkills ?? EmptySkills;
            return BasicSkills ?? EmptySkills;
        }
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
        // Follow-up strikes, counters and guard lifetimes. All provisional.
        public double FollowupRatio = 0.5;
        public double CounterRatio = 1.0;
        public int ImbalanceImmuneRounds = 1;
        public MentalSpec[] MentalStates = {
            new MentalSpec { State = MentalState.None, Name = "平稳" },
            new MentalSpec { State = MentalState.Composed, Name = "凝神", TechnicalMultiplier = 1.25 },
            new MentalSpec { State = MentalState.Agitated, Name = "躁动", PrimalMultiplier = 1.25, AccuracyModifier = -0.05 }
        };
        public LimbRules Limbs = new LimbRules();
        public MentalSpec Mental(MentalState state) { return MentalStates.First(x => x.State == state); }
    }

    [Serializable]
    public sealed class SkillUse { public string Id; public int Left; }

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
        // Per-skill use counters. Counted by skill Id, so a skill keeps its spent uses across forms and
        // is only refilled when the next battle resets the actor.
        public List<SkillUse> Uses = new List<SkillUse>();
        public int BlockCharges;      // Attacks fully negated before the charge runs out.
        public bool Counter;          // One counter-attack, spent on trigger.
        public bool Followup;         // Follow-up buff granted by the warrior.
        public int FollowupAttack;    // The warrior's base attack when the buff was handed out.
        public int Imbalance;         // > 0 skips the actor's next turn.
        public int ImbalanceImmuneRound = -1; // The round on which imbalance is refused.
        public double DefenseBuff;    // Temporary team defence bump.
        public double AccuracyBuff;   // Temporary team accuracy bump.
        public double SelfVulnerable; // Damage taken is raised by this share for the round.
        // Four parts, hero side only. Read the effective attributes through BattleEngine so the limb
        // penalties are applied; Speed below stays the raw character value.
        public List<LimbState> Limbs = new List<LimbState>();
        public int Speed { get { return Definition.Speed; } }
        public string Name { get { return Definition.Name; } }
        public bool DeathDoor { get { return Player && !Dead && Hp <= 0; } }

        public int UsesLeft(SkillSpec skill)
        {
            if (skill == null || skill.Uses <= 0) return int.MaxValue;
            var use = Uses.FirstOrDefault(x => x.Id == skill.Id);
            return use == null ? skill.Uses : use.Left;
        }

        public void SpendUse(SkillSpec skill)
        {
            if (skill == null || skill.Uses <= 0) return;
            var use = Uses.FirstOrDefault(x => x.Id == skill.Id);
            if (use == null) Uses.Add(new SkillUse { Id = skill.Id, Left = Math.Max(0, skill.Uses - 1) });
            else use.Left = Math.Max(0, use.Left - 1);
        }
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
        // Prosthetics the party owns but has not fitted yet.
        public List<ProstheticSpec> Prosthetics = new List<ProstheticSpec>();

        public static RunState Create(ActorSpec[] heroes, uint seed, double? sanityPreset = null, CombatRules rules = null)
        {
            var limbRules = (rules ?? new CombatRules()).Limbs;
            var run = new RunState { Seed = seed, Random = new RunRandom(seed), Map = new MapState() };
            for (int i = 0; i < heroes.Length; i++)
            {
                var spec = heroes[i];
                run.Heroes.Add(new ActorState { Id = spec.Id, Definition = spec, Player = true, Rank = i,
                    Hp = spec.MaxHp, Limbs = LimbSystem.CreateLimbs(spec.MaxHp, limbRules),
                    Sanity = SanityProbability.Clamp(sanityPreset ?? (run.PlayerSanity + spec.SanityOffset), 0, 100) });
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
        public int SkillIndex;
        public string TargetId;
        public ActionRequest(ActionKind kind, string targetId = null, int skillIndex = 0)
        { Kind = kind; TargetId = targetId; SkillIndex = skillIndex; }
    }
}
