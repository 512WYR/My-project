using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    public sealed partial class StageOneApp
    {
        public bool CharacterPanelOpen { get; private set; }
        public string CharacterHeroId { get; private set; }
        public bool CharacterPageAvailable => Run != null && !DebugOverlayOpen
            && (AwaitingNode ? Room == RoomView.Map && !Run.Map.Finished
                : Battle != null && Battle.Outcome == BattleOutcome.Active);
        public bool CanManageCharacter => CharacterPanelOpen && CharacterPageAvailable && !Busy
            && (AwaitingNode || (Battle.Current != null && Battle.Current.Player));
        public ActorState CharacterHero => Run?.Heroes.FirstOrDefault(h => h.Id == CharacterHeroId);

        public void OpenCharacterPanel(int index)
        {
            if (!CharacterPageAvailable || index < 0 || index >= Run.Heroes.Count) return;
            CharacterHeroId = Run.Heroes[index].Id; CharacterPanelOpen = true;
            InspectedId = CharacterHeroId; ClearPending(); Refresh();
        }
        public void CloseCharacterPanel()
        {
            CharacterPanelOpen = false; CharacterHeroId = null; ClearPending(); Refresh();
        }
        public string CharacterPotionBlockedReason(ConsumableKind kind)
        {
            if (!CanManageCharacter) return "等待我方行动后可操作";
            var item = Run.Consumables.FirstOrDefault(c => c != null && c.Kind == kind);
            return item == null ? "库存不足" : ConsumableSystem.UseBlockedReason(Run, CharacterHero, item, !AwaitingNode);
        }
        public void CharacterUsePotion(ConsumableKind kind)
        {
            string blocked = CharacterPotionBlockedReason(kind);
            if (blocked != null) { Hint = blocked; Refresh(); return; }
            int index = Run.Consumables.FindIndex(c => c != null && c.Kind == kind);
            var hero = CharacterHero;
            if (!AwaitingNode)
            {
                int from = Battle.Events.Count;
                if (!Battle.UseConsumable(index, hero.Id, out string battleNote))
                { Hint = battleNote; Refresh(); return; }
                CharacterPanelOpen = false; CharacterHeroId = null; ClearPending(); Hint = battleNote;
                StartCoroutine(ContinueEnemies(from));
            }
            else
            {
                ConsumableSystem.Use(Run, hero, index, false, out string note);
                // Out of battle there is no cut-in to carry the cue, so the potion plays it directly.
                if (kind == ConsumableKind.HpRestore) SfxDirector.Play(SfxCue.Heal);
                Hint = note; Refresh();
            }
        }
        // The character panel fits a prosthetic only with a surgery kit; the supply station's own install
        // option is the other route. The view asks the same method, so the visible reason and the command
        // can never disagree.
        public string CharacterInstallBlockedReason(ProstheticSpec item)
        {
            if (CharacterHero == null) return "请选择一名队员";
            if (CharacterHero.Dead) return "角色已阵亡";
            if (!CanManageCharacter) return "等待我方行动";
            return ConsumableSystem.SurgeryKitBlockedReason(Run, CharacterHero, item);
        }
        public void CharacterInstallProsthetic(string itemId)
        {
            if (!CanManageCharacter || CharacterHero == null || CharacterHero.Dead) return;
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (item == null) { Hint = "没有对应部位的义体库存。"; Refresh(); return; }
            string blocked = ConsumableSystem.SurgeryKitBlockedReason(Run, CharacterHero, item);
            if (blocked != null) { Hint = blocked; Refresh(); return; }
            var hero = CharacterHero;
            bool inBattle = !AwaitingNode;
            int from = inBattle ? Battle.Events.Count : 0;
            if (!ConsumableSystem.UseSurgeryKit(Run, hero, item, out string note))
            { Hint = note; Refresh(); return; }
            Hint = note;
            if (inBattle)
            {
                // A field fitting is an item use: it spends the current hero's action and lets the enemy
                // turns play out, exactly like a potion.
                Battle.EndTurnAfterSurgery(note);
                CharacterPanelOpen = false; CharacterHeroId = null; ClearPending();
                StartCoroutine(ContinueEnemies(from));
                return;
            }
            Refresh();
        }
        public void CharacterEquipAccessory(string itemId)
        {
            if (!CanManageCharacter) return;
            int index = Run.AccessoryStock.FindIndex(a => a != null && a.Id == itemId);
            AccessorySystem.Equip(Run, CharacterHero, index, out string note); Hint = note; Refresh();
        }
        public void CharacterUnequipAccessory(string itemId)
        {
            if (!CanManageCharacter || CharacterHero == null || CharacterHero.Dead) return;
            AccessorySystem.Unequip(Run, CharacterHero, itemId, out string note); Hint = note; Refresh();
        }
    }
}
