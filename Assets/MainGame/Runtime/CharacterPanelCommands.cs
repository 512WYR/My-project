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
        public void CharacterInstallProsthetic(string itemId)
        {
            if (!CanManageCharacter || CharacterHero == null || CharacterHero.Dead) return;
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (item == null) { Hint = "没有对应部位的义体库存。"; Refresh(); return; }
            string blocked = LimbSystem.InstallBlockedReason(CharacterHero, item);
            if (blocked != null) { Hint = blocked; Refresh(); return; }
            if (!LimbSystem.Install(CharacterHero, item)) { Hint = "只有断肢才能安装义体。"; Refresh(); return; }
            Run.Prosthetics.Remove(item); Hint = CharacterHero.Name + " 装上了" + item.Name + "。";
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
