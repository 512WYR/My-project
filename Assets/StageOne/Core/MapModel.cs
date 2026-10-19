using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // Boss only ever appears at the end of the last floor; it is never part of the sampled candidate table.
    // Merchant, camp and workshop were deleted rather than parked at weight 0: the supply room took over
    // what they used to do. Nothing persists a NodeType yet, so the renumbering is free. From stage three
    // on these values live inside save data, and then they can only ever be appended to.
    public enum NodeType { Battle, EliteBattle, Event, Boss, Supply, Altar, Elevator, Opening, Treasure }

    [Serializable]
    public sealed class NodeOddsSpec
    {
        public NodeType Type;
        public double Weight;
        // Positive leans toward lucid supply and negotiation; negative leans toward danger and anomaly.
        public double Affinity;
        public NodeOddsSpec() { }
        public NodeOddsSpec(NodeType type, double weight, double affinity) { Type = type; Weight = weight; Affinity = affinity; }
    }

    // EVT-008 trades one kind of resource against sanity. The side that is not sanity is named here;
    // the two hero ids split the HP side (ResourceHeroId) from the sanity side (HeroId), because a single
    // trade can bleed one hero and steady another.
    public enum AltarResource { Supplies, Health, Prosthetic }

    // Both altar trades are rolled once on entry and stored on the node, so re-rendering the room can
    // rebuild the same two offers without touching the random sequence. Valid=false means the party has
    // nothing to pay with; the page still shows the option, disabled, with Refusal as its explanation.
    [Serializable]
    public sealed class AltarOffer
    {
        public bool Valid = true;
        public string Refusal;
        public AltarResource Resource;
        public int ResourceAmount;       // supplies / HP points / prosthetic count (always 1)
        public int SanityAmount;
        public bool PlayerSanity = true; // sanity side is Run.PlayerSanity
        public string HeroId;            // sanity-side hero when PlayerSanity is false
        public string ResourceHeroId;    // HP-side hero
        public ProstheticSpec Prosthetic; // the exact part paid or handed over
    }

    [Serializable]
    public sealed class NodeState
    {
        public int Floor;
        public int Index;         // Position inside the floor.
        public NodeType Type;
        public int OptionIndex;   // Which candidate of the step this node came from.
        public string EventId;    // Set on an event or altar once its content has been rolled.
        public int OptionChosen = -1; // Which option of the current step was taken; -1 while unanswered.
        // Scripted-event progress. A documented event is a short run of steps; the node remembers which
        // step is on screen and the few numbers a step needs, so a re-render never has to re-roll or guess.
        // Option visibility is not stored: EventScripts.Page rebuilds it from the step alone, which is what
        // keeps a re-render from re-rolling anything.
        public int EventStep;      // Which step of the script is showing; EventScripts.DoneStep means over.
        public int EventCount;     // How many times a repeatable option has already been taken.
        public int EventLimit;     // The cap rolled once on entry (the abandoned mine's 2..5 digs).
        public string EventTarget; // The hero a "choose a member" step settled on.
        public int EventFlag;      // Free slot for one-off outcomes: potion colour, a 30/70 roll, a retreat.
        public bool EventJump;     // EVT-H001: leaving this room should go straight to the floor's elevator.
        // EVT-008's two frozen trades: resources -> sanity, and sanity -> resources.
        public AltarOffer AltarResourceForSanity;
        public AltarOffer AltarSanityForResource;
        public string Name { get { return MapGenerator.Name(Type); } }
    }

    // One position on the route. Its candidate set is drawn when the player walks up to it, so a step that
    // has been shown stays frozen and a step that has not been reached yet does not exist.
    [Serializable]
    public sealed class StepState
    {
        public int Floor;                                        // 1..Floors
        public int Index;                                        // Position inside the floor, 0 based.
        public List<NodeState> Candidates = new List<NodeState>(); // Fixed steps hold exactly one.
        public bool Shown;                                       // Drawn and frozen; never re-rolled.
        public bool Resolved;                                    // The party walked through it.
        public int ChosenIndex = -1;
        public bool Fixed;                                       // Opening / supply / elevator / boss: no draws.

        public NodeState Chosen
        {
            get { return ChosenIndex >= 0 && ChosenIndex < Candidates.Count ? Candidates[ChosenIndex] : null; }
        }
    }

    [Serializable]
    public sealed class MapState
    {
        public int Floor = 1;                      // The floor the current step belongs to.
        public List<StepState> Steps = new List<StepState>(); // The whole route so far, in walked order.
        public int StepIndex;                      // Where the party stands on that route.
        public bool Finished;                      // The run ended, either by clearing the boss or by dying.
        public bool Cleared;                       // The boss was beaten.
        public int Supplies;                       // Run-wide ore stock (displayed as 矿石), spent inside the supply room.
        // Event ids in the order their rooms were entered. Duplicates are allowed once the pool is
        // exhausted, so this is a history rather than a set. List<string> keeps JsonUtility happy.
        public List<string> EventHistory = new List<string>();
        public GrowthBuff Buff;                    // Opening team buff, chosen once.
        public bool BuffChosen;
        public bool SupplyHealed;                  // This supply room already healed the team.
        public bool SupplyCalmed;                  // This supply room already steadied the team.
        // Placeholders raised by EVT-L001. The document asks for +1 action in the next fight and a higher
        // battle-node rate afterwards; neither rule exists in the engine yet, so the event only records the
        // request and the notes say so. Nothing reads these two fields today.
        public bool NextBattleBonusActions;
        public bool BattleNodeBonus;

        public StepState Current
        {
            get { return StepIndex >= 0 && StepIndex < Steps.Count ? Steps[StepIndex] : null; }
        }

        public int ResolvedSteps
        {
            get { int n = 0; for (int i = 0; i < Steps.Count; i++) if (Steps[i].Resolved) n++; return n; }
        }

        public List<StepState> OnFloor(int floor)
        {
            var list = new List<StepState>();
            for (int i = 0; i < Steps.Count; i++) if (Steps[i].Floor == floor) list.Add(Steps[i]);
            return list;
        }

        // True once the party has walked through a room of that type; the route view uses it to draw the
        // opening buff room and the supply rooms as settled rather than pending.
        public bool HasPassed(NodeType type)
        {
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i].Resolved && Steps[i].Chosen != null && Steps[i].Chosen.Type == type) return true;
            return false;
        }
    }

    // Same layer as CombatRules: the weight table is a rule, not content. MapGenerator holds the defaults.
    [Serializable]
    public sealed class MapRules
    {
        public int Floors = 3;
        public int[] StepsPerFloor = { 5, 6, 7 }; // Sampled positions per floor; the two fixed ends are extra.
        public double ProbabilityStrength = 1.0;  // Lower than the combat 1.5 so exploration shifts stay moderate.
        // Effective branch sensitivity is ProbabilityStrength * BranchAffinity = 1.5, the same as combat.
        // At 1.0 the expectation can only reach 1.5/2.5 by making one candidate and three candidates both
        // common and two candidates rare; 1.5 keeps the middle option the most likely one instead.
        public double BranchAffinity = 1.5;       // One candidate uses -a, three candidates use +a.
        public double[] BranchWeights = { 0.31, 0.38, 0.31 };
        public NodeOddsSpec[] NodeOdds;           // Null falls back to MapGenerator.DefaultOdds.
        public GrowthBuff[] OpeningBuffs = { GrowthBuff.Toughness, GrowthBuff.Sharpness, GrowthBuff.Clarity };
        public double SupplyHealRatio = 0.35;     // Share of a hero's max health restored by one supply heal.
        public int SuppliesPerBattle = 2;
        public int SuppliesPerElite = 4;
        public int SuppliesPerBoss = 8;
        public int EliteHealthBonus = 20;
        public double EliteAttackScale = 1.25;
        // Open balance question: the boss is the same three enemies every battle uses, so these two knobs
        // take the room from 80 health / 10 attack each to 215 health / 15 attack each against a party of
        // 132 (164 with Toughness). By the arithmetic the fight is very likely unwinnable, and no test
        // asserts a win because of it. Tune here (or in the catalog asset) before claiming stage three's
        // "clear the run" acceptance step.
        public int BossHealthBonus = 45;
        public double BossAttackScale = 1.5;
    }
}
