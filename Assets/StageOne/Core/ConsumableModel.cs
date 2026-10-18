using System;

namespace Ruin.Core
{
    // Single-use items are the run's third stock item, next to prosthetics and accessories. They are spent
    // on use instead of being equipped, so they never live on an actor. The enum is append-only because
    // Unity may serialize its values in a save.
    public enum ConsumableKind
    {
        SanityLock, // 理智锁定药剂
        HpRestore,  // 血量恢复药
        SurgeryKit  // 手术包（功能待定）
    }

    [Serializable]
    public sealed class ConsumableSpec
    {
        public string Id;
        public string Name;
        public string Description;
        public ConsumableKind Kind;
        // Catalogued but not usable yet. The surgery kit is grantable and visible, but every use is
        // refused, so it can never be spent by accident.
        public bool Implemented = true;
    }
}
