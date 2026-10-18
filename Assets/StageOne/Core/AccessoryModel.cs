using System;
using System.Collections.Generic;

namespace Ruin.Core
{
    // Accessories are the run's second stock item, next to prosthetics. They are equipped on a hero and
    // change battle rules; prosthetics only replace a severed limb. The enum is append-only because Unity
    // may serialize these values in a save.
    public enum AccessoryKind
    {
        Gutter,      // 穿肠弹
        SleepPotion, // 假寐的药剂
        Addiction,   // 成瘾物
        GoldenApple, // 金丝苹果
        Heart,       // 跃动心脏
        Syringe,     // 献血针筒
        RustMedal,   // 锈血勋章
        MartyrChain  // 殉道锁链
    }

    [Serializable]
    public sealed class AccessorySpec
    {
        public string Id;
        public string Name;
        public string Description;
        public AccessoryKind Kind;
        // Empty means any hero. Otherwise "warrior" / "shield" / "gunner" / "medic", matching
        // ActorSpec.Silhouette.
        public string RestrictSilhouette;
    }

    // Per-battle accessory bookkeeping. Run-long equipment lives on ActorState.EquippedAccessories; every
    // field here is cleared when the next battle starts.
    [Serializable]
    public sealed class AccessoryBattleState
    {
        public bool AddictionActive;
        public int AddictionStacks;
        public bool SleepTriggered;
        public int SleepTurns;
        public bool HeartActive;
        public bool HeartResolved;
        public bool HeartSuccess;
        public string HeartTargetId;
        public int RustDamageAccum;
    }
}
