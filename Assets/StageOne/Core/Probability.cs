using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    public enum SanityGate { None, High, Low }

    [Serializable]
    public struct WeightedOutcome
    {
        public double Weight;
        public double Affinity;
        public SanityGate Gate;
        public WeightedOutcome(double weight, double affinity, SanityGate gate = SanityGate.None)
        { Weight = weight; Affinity = affinity; Gate = gate; }
    }

    public static class SanityProbability
    {
        public static double Clamp(double v, double min, double max) { return Math.Max(min, Math.Min(max, v)); }
        public static double Activation(double sanity, SanityGate gate)
        {
            if (gate == SanityGate.High) return Clamp((sanity - 70.0) / 30.0, 0, 1);
            if (gate == SanityGate.Low) return Clamp((30.0 - sanity) / 30.0, 0, 1);
            return 1;
        }

        public static double[] Distribution(double sanity, IList<WeightedOutcome> outcomes, double strength = 1.5)
        {
            if (double.IsNaN(sanity) || double.IsInfinity(sanity)) throw new ArgumentException("Invalid sanity");
            if (double.IsNaN(strength) || double.IsInfinity(strength) || strength < 0) throw new ArgumentException("Invalid strength");
            if (outcomes == null || outcomes.Count == 0) throw new ArgumentException("No outcomes");
            double x = (Clamp(sanity, 0, 100) - 50) / 50;
            var logs = new double[outcomes.Count];
            double max = double.NegativeInfinity;
            for (int i = 0; i < outcomes.Count; i++)
            {
                var item = outcomes[i];
                if (item.Weight < 0 || double.IsNaN(item.Weight) || double.IsInfinity(item.Weight) || double.IsNaN(item.Affinity) || double.IsInfinity(item.Affinity))
                    throw new ArgumentException("Invalid outcome weights");
                double gate = Activation(sanity, item.Gate);
                logs[i] = item.Weight == 0 || gate == 0 ? double.NegativeInfinity : Math.Log(item.Weight) + Math.Log(gate) + strength * item.Affinity * x;
                max = Math.Max(max, logs[i]);
            }
            if (double.IsNegativeInfinity(max)) throw new ArgumentException("All outcomes are disabled");
            double sum = 0;
            for (int i = 0; i < logs.Length; i++) { logs[i] = Math.Exp(logs[i] - max); sum += logs[i]; }
            for (int i = 0; i < logs.Length; i++) logs[i] /= sum;
            return logs;
        }

        public static int Sample(double[] probabilities, RunRandom random)
        {
            if (probabilities == null || probabilities.Length == 0) throw new ArgumentException("No probabilities");
            double pick = random.NextUnit();
            double cumulative = 0;
            int lastPositive = -1;
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (probabilities[i] > 0) lastPositive = i;
                cumulative += probabilities[i];
                if (pick < cumulative) return i;
            }
            if (lastPositive < 0) throw new ArgumentException("No positive probabilities");
            return lastPositive;
        }

        public static bool Check(double baseline, double affinity, double sanity, RunRandom random, double strength = 1.5)
        {
            baseline = Clamp(baseline, 0, 1);
            if (baseline <= 0) return false;
            if (baseline >= 1) return true;
            return Sample(Distribution(sanity, new[] { new WeightedOutcome(baseline, affinity), new WeightedOutcome(1 - baseline, 0) }, strength), random) == 0;
        }
    }

    // All gameplay draws share this serializable state; no UnityEngine.Random calls.
    [Serializable]
    public sealed class RunRandom
    {
        public uint State;

        // Debug-only draw overrides. [NonSerialized] keeps them out of Unity serialization, so a saved
        // run can never come back with a forced draw still armed. Off by default: debugForcedDraws == 0
        // means NextUnit behaves exactly as the plain xorshift below.
        [NonSerialized] private int debugForcedDraws;
        [NonSerialized] private double debugForcedValue;
        [NonSerialized] private bool debugConsumeState = true;

        public RunRandom(uint seed) { State = seed == 0 ? 0x9e3779b9u : seed; }

        public bool DebugOverrideActive { get { return debugForcedDraws > 0; } }
        public int DebugForcedDrawsRemaining { get { return debugForcedDraws; } }

        // Forces the next N draws to return value. By default the xorshift state still advances, so the
        // draws after the override line up with an unforced run; pass false to peek at a result without
        // consuming the sequence. The debug tool records which mode was used when it replays.
        public void ForceNextDraw(double value, int times = 1, bool consumeState = true)
        {
            debugForcedValue = ClampUnit(value);
            debugForcedDraws = Math.Max(1, times);
            debugConsumeState = consumeState;
        }

        // Keeps returning value until ClearDebugOverrides is called.
        public void LockDraw(double value, bool consumeState = true)
        {
            debugForcedValue = ClampUnit(value);
            debugForcedDraws = int.MaxValue;
            debugConsumeState = consumeState;
        }

        public void ClearDebugOverrides()
        {
            debugForcedDraws = 0;
            debugForcedValue = 0;
            debugConsumeState = true;
        }

        public double NextUnit()
        {
            if (debugForcedDraws > 0)
            {
                debugForcedDraws--;
                if (debugConsumeState) AdvanceState();
                return debugForcedValue;
            }
            return AdvanceState() / 4294967296.0;
        }

        private uint AdvanceState()
        {
            uint x = State == 0 ? 0x9e3779b9u : State;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            State = x;
            return x;
        }

        // NextUnit has always returned [0, 1); keep a forced value inside that range.
        private static double ClampUnit(double value)
        {
            if (double.IsNaN(value) || value <= 0) return 0;
            if (value >= 1) return 0.999999999;
            return value;
        }
    }
}
