using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // Boss only ever appears on the final floor; it is never part of the sampled candidate table.
    public enum NodeType { Battle, EliteBattle, Event, Merchant, Camp, Workshop, Boss }

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

    [Serializable]
    public sealed class NodeState
    {
        public int Floor;
        public int Index;
        public NodeType Type;
        public bool Placeholder;
        public string Name { get { return MapGenerator.Name(Type); } }
    }

    [Serializable]
    public sealed class FloorState
    {
        public int Floor;
        public List<NodeState> Nodes = new List<NodeState>();
        public bool Resolved;
        public int ChosenIndex = -1;
    }

    [Serializable]
    public sealed class MapState
    {
        public int Floor = 1; // The floor currently awaiting a choice.
        public List<FloorState> Floors = new List<FloorState>();
        public bool Finished; // Reached the boss floor, or the party was wiped out.

        public FloorState FloorAt(int floor)
        {
            for (int i = 0; i < Floors.Count; i++) if (Floors[i].Floor == floor) return Floors[i];
            return null;
        }

        public FloorState Current { get { return FloorAt(Floor); } }

        public int ResolvedFloors
        {
            get { int n = 0; for (int i = 0; i < Floors.Count; i++) if (Floors[i].Resolved) n++; return n; }
        }
    }

    // Same layer as CombatRules: the weight table is a rule, not content. MapGenerator holds the defaults.
    [Serializable]
    public sealed class MapRules
    {
        public int Floors = 10;
        public double ProbabilityStrength = 1.0; // Lower than the combat 1.5 so exploration shifts stay moderate.
        public int OpeningBranchCount = 3;       // Opening floor offers three rooms.
        public int GuaranteedBattleIndex = 0;    // Every floor keeps one plain battle as the reachable spine.
        public double BranchAffinity = 1.0;      // One branch uses -a, three branches use +a.
        public double[] BranchWeights = { 0.15, 0.60, 0.25 };
        public NodeOddsSpec[] NodeOdds;          // Null falls back to MapGenerator.DefaultOdds.
    }
}
