using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // Characters are defined directly. There is no profession layer any more: a hero is just its own
    // Id/Name/Silhouette plus three skill lists. The three forms survive from the sanity system, they
    // simply changed what they switch: the whole skill list, not a single slot.
    public enum SkillForm { Technical, Basic, Primal }
    // Sanity sits next to Attack/Heal as its own effect: 理智怪 has no hit roll and deals no HP damage.
    public enum SkillEffect { Attack, Heal, Protect, Buff, Sanity }
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
    // Legacy pattern enemies (the three stage-one dummies) keep their scripted behaviour. Designed
    // monsters carry SkillSpec data and run through the enemy skill AI instead. The enum is append-only:
    // Unity assets may serialize these values.
    public enum EnemyPattern { Cleaver, Whisperer, Hunter }
    public enum EnemyBehavior
    {
        Simple,          // Random target inside the skill's TargetRanks.
        Sanity,          // 理智怪: sanity drain from the back, plain melee in the front rank.
        Bleed,           // 流血怪: prefer the target already carrying the most bleed.
        HighDamage,      // 高伤怪: front skill and back skill differ only by rank.
        Tank,            // 肉盾怪: guard, then guard/attack alternation.
        Healer,          // 回血怪: heal the most wounded ally, retreat from the front.
        CopyBoss,        // Boss1: copies party members and refills dead copies.
        GiantBoss,       // Boss2: occupies three ranks, cycles 1-2-1-2-3, heals on hero death.
        RandomSummoner,  // Boss3 part: is replaced by a random normal monster at battle start.
        SteamMachine,    // Boss3 part: immobile, fires when operated twice.
        Operator         // Boss3 part: operates the machine from rank 4 every other round.
    }
    // How an enemy skill picks among its legal targets. Random is the default and is the only rule the
    // plain monsters use; the other two serve the bleed monster and the healer's "most wounded" rule.
    public enum EnemyTargetRule { Random, LowestHp, MostBleed }

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

        // ---- designed-monster fields ----
        // Sanity is a separate effect, not a rider: the drain lands without an accuracy roll and without
        // touching HP. SanityAdjacent is the elite 理智怪's bleed-out onto both neighbours.
        public int SanityDamage;
        public int SanityAdjacent;
        // How many separate targets the skill takes. 0 keeps the older Targeting/Area behaviour; anything
        // above zero makes the enemy AI roll the hit independently for each drawn target.
        public int TargetCount;
        public EnemyTargetRule TargetRule = EnemyTargetRule.Random;
        // When set, an attack that is intercepted by a guard loses this share of its damage.
        public double GuardReduction;

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
        // Base speed. Still the authoring fallback: when the range below is unset, or when a debug/test
        // caller moves Speed outside its authored span, this value is used as a fixed speed.
        public int Speed;
        // Inclusive roll range for the speed drawn at the start of every round. 0 means "no span" and
        // keeps Speed fixed. SpeedMin/SpeedMax are authored as the actual endpoints (e.g. 5 and 7).
        public int SpeedMin;
        public int SpeedMax;
        // Effective endpoints, including the runtime-override guard. If Speed has been moved outside the
        // authored span, the caller deliberately overrode it (debug tools and tests do this), so the span
        // collapses to that fixed value instead of silently ignoring the override.
        public int MinSpeed { get { return SpeedRangeValid ? SpeedMin : Math.Max(1, Speed); } }
        public int MaxSpeed
        {
            get
            {
                if (!SpeedRangeValid) return Math.Max(1, Speed);
                return Math.Max(MinSpeed, SpeedMax);
            }
        }
        private bool SpeedRangeValid
        {
            get
            {
                return SpeedMin > 0 && SpeedMax >= SpeedMin && Speed >= SpeedMin && Speed <= SpeedMax;
            }
        }
        // Uniform integer in [MinSpeed, MaxSpeed]. A fixed-speed actor returns immediately without drawing,
        // so adding this rule never shifts the random sequence for specs that have no real span.
        public int RollSpeed(RunRandom random)
        {
            int min = MinSpeed, max = MaxSpeed;
            if (max <= min || random == null) return min;
            return min + (int)(random.NextUnit() * (max - min + 1));
        }
        // Starting enemy rank. -1 means "use the array index", which keeps the old constructor contract
        // for heroes and tests; a formation instead names the rank explicitly so empty slots stay empty.
        public int StartRank = -1;
        // Character combat attributes. They are static per character and never derived from sanity.
        public int Attack = 10;
        public double Accuracy = 0.9;
        public double CritChance = 0.1;
        public double Dodge;
        public double Defense;
        public double SanityOffset;
        public EnemyPattern Pattern;
        // ---- designed-monster fields (ignored by legacy pattern enemies) ----
        public EnemyBehavior Behavior = EnemyBehavior.Simple;
        // Ranks the actor occupies: the giant boss covers ranks 1-3, the steam machine ranks 2-3.
        public int RankWidth = 1;
        public bool Immobile;              // Never moved by dragging or by a back-step.
        // A monster that acts once every N rounds. ActionPhase shifts which rounds count.
        public int ActionInterval = 1;
        public int ActionPhase;
        // Giant boss: HP restored whenever a party member dies.
        public int BossHealOnHeroDeath;
        // Share of an intercepted hit absorbed by this actor when it guards somebody.
        public double GuardReduction;
        // RandomSummoner: the pool it turns into at battle start (normal monsters only).
        public ActorSpec[] SummonPool = new ActorSpec[0];
        public SkillSpec[] BasicSkills = EmptySkills;
        public SkillSpec[] PrimalSkills = EmptySkills;
        public SkillSpec[] TechnicalSkills = EmptySkills;

        public static readonly SkillSpec[] EmptySkills = new SkillSpec[0];

        public int Width { get { return Math.Max(1, RankWidth); } }
        public bool ActsOn(int round)
        {
            if (ActionInterval <= 1) return true;
            int offset = round - 1 - ActionPhase;
            return offset >= 0 && offset % ActionInterval == 0;
        }

        public SkillSpec[] SkillsFor(SkillForm form)
        {
            if (form == SkillForm.Primal) return PrimalSkills ?? EmptySkills;
            if (form == SkillForm.Technical) return TechnicalSkills ?? EmptySkills;
            return BasicSkills ?? EmptySkills;
        }
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
        // Displacement skills auto-hit for this many rounds after imbalance lands. The skipped action is
        // still a separate, one-shot effect controlled by ActorState.Imbalance.
        public int ImbalanceAutoHitRounds = 2;
        public LimbRules Limbs = new LimbRules();
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
        // Hero-only resource. Enemies are constructed with 0 and never read or write it; the field stays
        // on the shared state type because the battle engine does not branch on side.
        public double Sanity;
        public SkillForm Form = SkillForm.Basic;
        public int BleedDamage;
        public int BleedTurns;
        public int Interrupted;
        public bool Defending;
        public string ProtectorId;
        // Damage share absorbed while this actor is guarding somebody (elite tank: 0.25).
        public double GuardReduction;
        // ---- designed-monster runtime state ----
        // Counts the enemy's own completed actions: the tank alternates guard/attack and the giant boss
        // walks its 1-2-1-2-3 cycle off this counter.
        public int EnemyActionCount;
        public int Operations;      // Steam machine: successful operations accumulated.
        public int BlastRound;      // Steam machine: round on which the charged blast fires; 0 = not charged.
        public int NextSpawnRound;  // Operator: round on which it may be summoned again.
        public bool IsCopy;         // Boss1 copy, so refills only top up the copies it owns.
        public string SummonedBy;   // Boss1 body id for a copy.
        // Per-skill use counters. Counted by skill Id, so a skill keeps its spent uses across forms and
        // is only refilled when the next battle resets the actor.
        public List<SkillUse> Uses = new List<SkillUse>();
        public int BlockCharges;      // Attacks fully negated before the charge runs out.
        public bool Counter;          // One counter-attack, spent on trigger.
        public bool Followup;         // Follow-up buff granted by the warrior.
        public int FollowupAttack;    // The warrior's base attack when the buff was handed out.
        public int Imbalance;         // > 0 skips the actor's next turn.
        public int ImbalanceImmuneRound = -1; // The round on which imbalance is refused.
        // Last round on which a displacement skill auto-hits this actor. Independent of Imbalance, so the
        // guaranteed hit survives the skipped turn and lasts the full configured window.
        public int ImbalanceAutoHitUntilRound = -1;
        public double DefenseBuff;    // Temporary team defence bump.
        public double AccuracyBuff;   // Temporary team accuracy bump.
        public double SelfVulnerable; // Damage taken is raised by this share for the round.
        // Set by the medicine-chest event (EVT-004) when the gamble succeeds: this hero may only ever use
        // the high-sanity and low-sanity lists, never the plain four. It is a run-long trait, so it is
        // deliberately not cleared by ResetBattleEffects the way the per-battle flags above are.
        public bool SanitySkillsOnly;
        // Four parts, hero side only. Read the effective attributes through BattleEngine so the limb
        // penalties are applied; Speed below stays the raw character value.
        public List<LimbState> Limbs = new List<LimbState>();
        // Run-long equipment. Unequipping moves the item back to RunState.AccessoryStock.
        public List<AccessorySpec> EquippedAccessories = new List<AccessorySpec>();
        // Per-battle accessory flags and stacks, reset at the start of every battle.
        public AccessoryBattleState AccessoryBattle = new AccessoryBattleState();
        // 理智锁定药剂. SanityLocked is the live battle lock; SanityLockPending is a lock bought outside
        // battle that activates when the next one starts. Both are cleared when a battle settles.
        public bool SanityLocked;
        public bool SanityLockPending;
        // Speed drawn for the current round. 0 means "not rolled yet"; read EffectiveSpeed on the engine
        // instead of this field when applying limb penalties.
        public int SpeedRoll;
        public int Speed { get { return Definition.Speed; } }
        public string Name { get { return Definition.Name; } }
        public bool DeathDoor { get { return Player && !Dead && Hp <= 0; } }
        public int Width { get { return Definition == null ? 1 : Definition.Width; } }
        public bool OccupiesRank(int rank) { return rank >= Rank && rank < Rank + Width; }
        public bool OccupiesAnyRank(int rankMask)
        {
            for (int rank = Rank; rank < Rank + Width; rank++)
                if (rank >= 0 && rank < 32 && (rankMask & (1 << rank)) != 0) return true;
            return false;
        }

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
        // Accessories the party owns but has not equipped yet.
        public List<AccessorySpec> AccessoryStock = new List<AccessorySpec>();
        // Single-use items the party owns. A successful use removes one entry.
        public List<ConsumableSpec> Consumables = new List<ConsumableSpec>();

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
        // Structured payload for effects that cannot be reconstructed from the number alone: the limb
        // part name, "locked" for a sanity lock, or the concrete buff list. Value carries the actual
        // sanity delta and the severed flag (1 = severed, 0 = only injured).
        public string Detail;
        public double Value;
        // True when a status crossed a visible threshold: a limb turned injured (half health) or was
        // severed. The engine still logs every guaranteed hit for the combat log; only milestones get
        // a line in the cut-in.
        public bool Milestone;
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
