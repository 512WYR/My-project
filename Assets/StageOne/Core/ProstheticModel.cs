using System;

namespace Ruin.Core
{
    // Every prosthetic the party can fit. Conventional is the four original repair pieces; all other
    // values are the precision/heterogeneous pieces from the steampunk design. The enum is append-only
    // because Unity assets may serialize these values.
    public enum ProstheticKind
    {
        Conventional = 0,
        CastIronBellHead, SiegeHydraulicArm, PileAnchorLeg, ArmoredPressureTorso,
        PainAmplifierCrown, DetonationStrokeArm, SteamJetLeg, RedlineSteamHeart,
        ClockworkCompoundEye, GyroChargeArm, SteelWireLeg, HollowWingFrame,
        CondensationHead, LeechReturnArm, BedframeLeg, DoublePumpTorso,
        GunnerMonocle, WhalingHarpoonArm,
        VengeanceFlywheelArm, DoubleValveBoilerTorso,
        RatchetLeg, SteamCityHammerArm,
        MadSurgeonLens, PressureDrugArm
    }

    // Per-battle prosthetic state. It is recreated by ResetBattleEffects, so every flag here is scoped to
    // one fight and never leaks into the next one.
    [Serializable]
    public sealed class ProstheticBattleState
    {
        public bool FreeSwapUsed;          // SteamJet/SteelWire: one free swap per action.
        public bool GyroCharged;           // GyroChargeArm: consumed by the next active attack.
        public bool SteamSwapAttackBonus;  // SteamJetLeg: +50% on this action's attack after swapping.
        public bool SteamSwapVulnerable;   // SteamJetLeg: +40% damage taken until the next action starts.
        public bool SteelWireVulnerable;   // SteelWireLeg: +25% damage taken until the user's next action.
        public string SteelWireSourceId;   // Which hero used the wire, so the debuff can be cleared.
        public bool BoilerVulnerable;      // SteamCityHammerArm: +50% damage taken after Boiler Shock.
        public bool DoubleGuardActive;     // DoubleValveBoilerTorso: +25% damage when intercepting.
        public bool RankChangedThisAction; // BedframeLeg: whether the current action changed rank.
    }
}
