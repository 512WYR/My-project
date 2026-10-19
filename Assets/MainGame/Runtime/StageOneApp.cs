using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Which page the node half of the game is showing. A battle is not a room: the battle page is driven
    // by AwaitingNode being false, exactly as it was before the route rework.
    public enum RoomView { Map, Buff, Supply, Event, Elevator, Treasure }

    public sealed partial class StageOneApp : MonoBehaviour
    {
        public GameCatalog Catalog;
        public RunState Run { get; private set; }
        public BattleEngine Battle { get; private set; }
        // 本回合已经点开、正等着点目标的技能下标。换位走一个单独的开关，因为它没有下标。
        public int? PendingSkill { get; private set; }
        public bool PendingSwap { get; private set; }
        // 正在说明区查看的技能下标。不可用的技能也能点开，所以它可以和 PendingSkill 不同。
        public int? InspectedSkill { get; private set; }
        // 任意一种「已选好行动、等待点目标」的状态。
        public bool Choosing { get { return PendingSkill.HasValue || PendingSwap; } }
        public string InspectedId { get; private set; }
        public bool Busy { get; private set; }
        public string Hint { get; private set; }
        // True while the battle page is not in charge, which is also when Battle is null.
        public bool AwaitingNode { get; private set; }
        // The room page standing in for the battle page right now.
        public RoomView Room { get; private set; }
        // The room the party walked into, kept so a battle settlement and a room page both know which
        // node on the route they belong to.
        public NodeState ActiveNode { get; private set; }
        // The event node that started the fight currently on the battle page, if any. When ContinueBattle
        // sees it, the settlement goes back to the event instead of walking the route forward.
        public NodeState EventBattle { get; private set; }
        // A scripted event runs as a short series of steps; the room only opens up once the script itself
        // says so, and the answer is recomputed rather than stored, so a step can never un-finish itself.
        public bool EventCanLeave { get { return ActiveNode == null || EventScripts.CanLeave(ActiveNode); } }
        public string RoomNote { get; private set; }
        // A one-line report of the fight that just settled, shown on the route page. It outlives the walk
        // forward, which is why it is not RoomNote: RevealStep clears that one on every step.
        public string MapNote { get; private set; }
        public string RunEndNote { get; private set; }
        public MapState Map { get { return Run == null ? null : Run.Map; } }
        public bool AutomationMode { get; set; }
        public const uint DefaultSeed = 44972;

        // A room page is open, so the debug tool stays shut the way it did around the workshop.
        public bool InRoom { get { return AwaitingNode && Room != RoomView.Map; } }

        private MapRules Rules() { return Catalog.MapRules(); }

        // ---- per-node resource report ----
        // The report is a diff against the stock the party had when it stepped onto the node. Events,
        // the chest, the shop and debug grants all write the run stock directly, so a diff catches every
        // source without teaching each call site about the UI. It is deliberately node-scoped: walking to
        // the next position takes a new baseline and the old report disappears with it.
        private bool nodeGainsTracked;
        private int nodeGainSupplies;
        private readonly Dictionary<string, int> nodeGainAccessories = new Dictionary<string, int>();
        private readonly Dictionary<string, int> nodeGainConsumables = new Dictionary<string, int>();
        private readonly Dictionary<string, int> nodeGainProsthetics = new Dictionary<string, int>();
        // A battle's payout is moved to the moment its result page is built. ContinueBattle calls the same
        // guard, so the reward is applied once even though both the page and the button ask for it.
        private bool battleRewardsSettled;
        private string battleRewardNote;

        // Debug only: an event id the next room roll must take instead of the weighted draw.
        private string ForcedEvent;

        private void Start()
        {
            Application.targetFrameRate = 60;
            // Created here rather than authored into the scene: the game state is the only component that
            // owns the formal entry, and preview scenes without it should not inherit the backdrop.
            GameBackground.Ensure();
            Catalog = Catalog ? Catalog : Resources.Load<GameCatalog>("StageOne/GameCatalog");
            if (!Catalog) { Debug.LogError("StageOne catalog missing. Run GameMake/Prepare Stage One from the editor."); return; }
            NewRun();
        }

        // The authored debug console is outside this assembly's UI, so it publishes the two facts the
        // game loop needs: whether it currently owns the screen, and whether it already consumed Esc in
        // this frame. Without the second flag the app would also cancel a pending target whenever the
        // console closes, depending on which Update ran first.
        public static bool DebugOverlayOpen;
        public static int DebugOverlayEscapeFrame = -1;

        private void Update()
        {
            if (SupplyShopOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseSupplyShop();
                return;
            }
            if (CharacterPanelOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseCharacterPanel();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { ClearPending(); Refresh(); }
        }

        public void NewRun(double? preset = null, uint? seed = null)
        {
            SupplyShopOpen = false; SupplyShopNote = null;
            CharacterPanelOpen = false; CharacterHeroId = null;
            StopAllCoroutines(); Busy = false; ClearPending();
            Room = RoomView.Map; ActiveNode = null; EventBattle = null; RoomNote = null; MapNote = null;
            RunEndNote = null;
            // The limb rules travel with the rest of the combat rules, so the catalog's part ratios and
            // penalties seed every hero's limb table.
            Run = RunState.Create(Catalog.Heroes.Select(a => a.ToSpec()).ToArray(), seed ?? DefaultSeed, preset, Catalog.Rules());
            // The seed preset only writes each hero's own sanity. The shared player sanity is a different
            // field, and it is what the map odds and every event's high/low gate read, so the debug
            // presets have to move it too — otherwise "sanity 15" still reads 50 and every event quietly
            // takes its high-sanity branch. A normal run passes no preset and stays at 50.
            if (preset.HasValue) Run.PlayerSanity = SanityProbability.Clamp(preset.Value, 0, 100);
            Run.Map.Floor = 1;
            RevealStep();
            Hint = "路线开局是一间初始增益房，先选一项全队增益。";
            Refresh();
        }

        // Stands on the current position of the route. Already-shown positions are returned untouched, so
        // re-rendering or reloading never re-rolls them and never consumes a draw.
        private void RevealStep()
        {
            SupplyShopOpen = false;
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Map; ActiveNode = null; EventBattle = null; RoomNote = null;
            nodeGainsTracked = false; battleRewardsSettled = false; battleRewardNote = null;
            AwaitingNode = true;
            MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            Hint = "选择一条路线前进。已通过的房间固定不变，前方候选只有在走到跟前才会出现。";
            Refresh();
        }

        // Walks one position further along the route.
        private void WalkForward()
        {
            if (Run.Map.Finished) return;
            var rules = Rules();
            if (Run.Map.StepIndex + 1 >= MapGenerator.TotalSteps(rules)) return;
            Run.Map.StepIndex++;
            RevealStep();
        }

        public void SelectNode(int index)
        {
            if (SupplyShopOpen) return;
            if (CharacterPanelOpen) return;
            if (Busy || Run == null || Run.Map.Finished) return;
            if (Room != RoomView.Map) return;
            var step = Run.Map.Current;
            if (step == null || step.Resolved || index < 0 || index >= step.Candidates.Count) return;
            var node = step.Candidates[index];
            step.ChosenIndex = index;
            ActiveNode = node;
            MapNote = null;
            BeginNodeGains();

            switch (node.Type)
            {
                case NodeType.Battle:
                case NodeType.EliteBattle:
                case NodeType.Boss:
                    StartBattle(node, node.Type); return;
                case NodeType.Opening:
                    EnterBuffRoom(); return;
                case NodeType.Supply:
                    EnterSupplyRoom(); return;
                case NodeType.Event:
                case NodeType.Altar:
                    EnterEventRoom(node); return;
                case NodeType.Elevator:
                    EnterElevator(); return;
                case NodeType.Treasure:
                    EnterTreasureRoom(); return;
            }
            // Nothing in the table falls through today; the branch is left so a node type added in a later
            // stage degrades into walking on instead of dead-ending the run.
            RoomNote = "「" + node.Name + "」还没有内容，先继续前进。";
            ResolveCurrent();
        }

        // Takes the baseline the node's resource report is measured against. Called once, right after the
        // party steps onto a position and before its room hands anything out.
        private void BeginNodeGains()
        {
            nodeGainsTracked = Run != null && ActiveNode != null;
            nodeGainSupplies = Run == null || Run.Map == null ? 0 : Run.Map.Supplies;
            SnapshotStock(Run == null ? null : Run.AccessoryStock, a => a.Id, nodeGainAccessories);
            SnapshotStock(Run == null ? null : Run.Consumables, c => c.Id, nodeGainConsumables);
            SnapshotStock(Run == null ? null : Run.Prosthetics, p => p.Id, nodeGainProsthetics);
        }

        /// <summary>
        /// What the party has gained while standing on the current node. Only net increases are reported:
        /// a supply spent in the shop or a prosthetic fitted onto a stump is not a gain, and something
        /// gained then spent inside the same node nets out to nothing.
        /// </summary>
        public List<ResourceGain> NodeGains()
        {
            var gains = new List<ResourceGain>();
            if (!nodeGainsTracked || Run == null || ActiveNode == null) return gains;
            int supplies = (Run.Map == null ? 0 : Run.Map.Supplies) - nodeGainSupplies;
            if (supplies > 0)
                gains.Add(new ResourceGain { Kind = ResourceKind.Supplies, Name = "矿石", Amount = supplies });
            AddStockGains(gains, ResourceKind.Accessory, Run.AccessoryStock, nodeGainAccessories, a => a.Id, a => a.Name);
            AddStockGains(gains, ResourceKind.Consumable, Run.Consumables, nodeGainConsumables, c => c.Id, c => c.Name);
            AddStockGains(gains, ResourceKind.Prosthetic, Run.Prosthetics, nodeGainProsthetics, p => p.Id, p => p.Name);
            return gains;
        }

        private static void SnapshotStock<T>(List<T> items, Func<T, string> id, Dictionary<string, int> counts)
        {
            counts.Clear();
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                string key = id(item);
                if (string.IsNullOrEmpty(key)) continue;
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
        }

        private static void AddStockGains<T>(List<ResourceGain> gains, ResourceKind kind, List<T> items,
            Dictionary<string, int> baseline, Func<T, string> id, Func<T, string> name)
        {
            if (items == null || items.Count == 0) return;
            // Count first, then report once per id in the order the stock is held, so a stack of copies
            // becomes one "×N" line instead of N identical lines.
            var totals = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (item == null) continue;
                string key = id(item);
                if (string.IsNullOrEmpty(key)) continue;
                totals.TryGetValue(key, out int count);
                totals[key] = count + 1;
            }
            var reported = new HashSet<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
                string key = id(item);
                if (string.IsNullOrEmpty(key) || !reported.Add(key)) continue;
                baseline.TryGetValue(key, out int before);
                int delta = totals[key] - before;
                if (delta > 0)
                    gains.Add(new ResourceGain { Kind = kind, Name = name(item) ?? key, Amount = delta });
            }
        }

        // ---------------- openings, elevators, and the room pages ----------------

        // The opening room hands out one team buff. It is applied to the heroes' own definitions: each of
        // those is the fresh ActorSpec the catalog handed out, so nothing here writes into the asset.
        private void EnterBuffRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Buff; AwaitingNode = true; RoomNote = null;
            Hint = "选择一项全队增益。选定之后本次冒险不再更改。";
            Refresh();
        }

        public void ChooseBuff(GrowthBuff buff)
        {
            if (Run == null || Room != RoomView.Buff || Run.Map.BuffChosen) return;
            OpeningBuffs.Apply(buff, Run, Catalog.Rules().Limbs);
            Run.Map.Buff = buff; Run.Map.BuffChosen = true;
            RoomNote = "全队获得「" + OpeningBuffs.Name(buff) + "」：" + OpeningBuffs.Describe(buff) + "。";
            Hint = RoomNote;
            Refresh();
        }

        // The supply room took over everything the workshop and the camp used to do: it heals the party,
        // repairs injured parts, fits prosthetics onto severed ones, and steadies the team. The limb half
        // is the old workshop page unchanged, so a part still only ever goes up by being repaired here.
        private void EnterSupplyRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Supply; AwaitingNode = true; RoomNote = null;
            Run.Map.SupplyHealed = false; Run.Map.SupplyCalmed = false; Run.Map.SupplyRepairFreeUsed = false;
            Hint = "补给站：治疗全队、修复重伤部位、为断肢安装义肢、静养恢复理智。离开后继续前进。";
            Refresh();
        }

        public void SupplyHeal()
        {
            if (SupplyShopOpen) return;
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyHealed) return;
            if (!Run.Heroes.Any(h => h != null && !h.Dead && h.Hp < h.Definition.MaxHp))
            {
                Hint = "全队生命已满，不需要治疗。";
                Refresh(); return;
            }
            int price = StationPrice(SupplyShop.StationHealPrice);
            if (price > 0 && Run.Map.Supplies < price) { Hint = "矿石不足，治疗需要 " + price + " 矿石。"; Refresh(); return; }
            int healed = Exploration.SupplyHealParty(Rules(), Run);
            Run.Map.Supplies -= price;
            Run.Map.SupplyHealed = true;
            Hint = "补给站为全队恢复 " + healed + " 点生命。阵亡成员不会复活。"
                + (price > 0 ? "消耗 " + price + " 矿石。" : "本层免费。");
            Refresh();
        }

        public void SupplyCalm()
        {
            if (SupplyShopOpen) return;
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyCalmed) return;
            if (Run.PlayerSanity >= 100) { Hint = "玩家理智已满，不需要静养。"; Refresh(); return; }
            int price = StationPrice(SupplyShop.StationCalmPrice);
            if (price > 0 && Run.Map.Supplies < price) { Hint = "矿石不足，静养需要 " + price + " 矿石。"; Refresh(); return; }
            double gained = Exploration.SupplyCalm(Run);
            Run.Map.Supplies -= price;
            Run.Map.SupplyCalmed = true;
            Hint = "全队静养，玩家理智 +" + gained.ToString("0.0") + "。"
                + (price > 0 ? "消耗 " + price + " 矿石。" : "本层免费。");
            Refresh();
        }

        // The second floor's station is free; every later station charges the listed price.
        private int StationPrice(int laterFloorPrice)
        {
            return Run.Map.Floor <= SupplyShop.FreeStationFloor ? 0 : laterFloorPrice;
        }

        public void RepairLimb(string heroId, LimbPart part)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            if (hero == null) return;
            if (!LimbSystem.CanRepair(hero, part)) { Hint = "只有重伤部位能在这里修复；断肢需要安装义肢。"; Refresh(); return; }
            bool free = !Run.Map.SupplyRepairFreeUsed;
            int price = free ? 0 : SupplyShop.RepairPrice;
            if (price > 0 && Run.Map.Supplies < price) { Hint = "矿石不足，修复需要 " + price + " 矿石。"; Refresh(); return; }
            if (!LimbSystem.Repair(hero, part)) { Hint = "只有重伤部位能在这里修复；断肢需要安装义肢。"; Refresh(); return; }
            Run.Map.Supplies -= price;
            Run.Map.SupplyRepairFreeUsed = true;
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经修复。"
                + (price > 0 ? "消耗 " + price + " 矿石。" : "这是本座补给站的第一次免费修复。");
            Refresh();
        }

        public void InstallProsthetic(string heroId, string itemId)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (hero == null || item == null) return;
            if (!LimbSystem.CanInstall(hero, item)) { Hint = "只有断肢才能安装义肢。"; Refresh(); return; }
            if (Run.Map.Supplies < SupplyShop.InstallPrice)
            {
                Hint = "矿石不足，安装义肢需要 " + SupplyShop.InstallPrice + " 矿石。";
                Refresh(); return;
            }
            if (!LimbSystem.Install(hero, item)) { Hint = "只有断肢才能安装义肢。"; Refresh(); return; }
            Run.Prosthetics.Remove(item);
            Run.Map.Supplies -= SupplyShop.InstallPrice;
            Hint = hero.Name + " 装上了" + item.Name + "，" + LimbSystem.PartName(item.Part) + "恢复基础功能。"
                + "消耗 " + SupplyShop.InstallPrice + " 矿石。";
            Refresh();
        }

        // Events and altars share one page. Which event the room holds, and everything that event rolled on
        // entry, is frozen here; EventScripts.Page then rebuilds the current step on every render without
        // drawing anything, so re-rendering never re-rolls the room.
        private void EnterEventRoom(NodeState node)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Event; AwaitingNode = true; EventBattle = null; RoomNote = null;
            EventScripts.Roll(node, Run, Rules(), ForcedEvent);
            ForcedEvent = null;
            Hint = node.Type == NodeType.Altar ? "祭坛房间：选择一种处理方式。" : "事件房间：选择一种处理方式。";
            Refresh();
        }

        // One option listener call. A step that asks for a fight hands the room over to the battle page and
        // keeps EventBattle pointing at this node, so the settlement knows where to come back to.
        public void ChooseEventOption(int index)
        {
            if (Run == null || Room != RoomView.Event || ActiveNode == null) return;
            EventSession session;
            if (!EventScripts.Choose(ActiveNode, index, Run, Rules(), out session))
            { Hint = "这个选项现在不可用。"; Refresh(); return; }
            RoomNote = session.Note; Hint = session.Note;
            if (session.WantsBattle) { StartBattle(ActiveNode, session.BattleKind); return; }
            Refresh();
        }

        // A pure transition page: no fight, no draw, just the floor summary and the way down.
        private void EnterElevator()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Elevator; AwaitingNode = true; RoomNote = null;
            Hint = "电梯井：本层到此为止。下行之后不会自动整备生命。";
            Refresh();
        }

        // The chest room is the accessory source. EventFlag doubles as the "already opened" marker for
        // this node, so reopening the page never draws a second item.
        private void EnterTreasureRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            Room = RoomView.Treasure; AwaitingNode = true;
            if (ActiveNode != null && ActiveNode.EventFlag == 0)
            {
                var item = Encounter.DropAccessory(Run.Random);
                ActiveNode.EventFlag = 1;
                if (item != null)
                {
                    Run.AccessoryStock.Add(item);
                    RoomNote = "宝箱里是一件饰品：「" + item.Name + "」——" + item.Description;
                    Hint = "获得饰品「" + item.Name + "」。可在补给站或验收工具中装备。";
                }
                else
                {
                    RoomNote = "宝箱是空的。";
                    Hint = "宝箱房：空。离开会继续前进。";
                }
            }
            else
            {
                RoomNote = "宝箱已经空了，饰品已经放进了队伍仓库。";
                Hint = "宝箱房：已领取。离开会继续前进。";
            }
            Refresh();
        }

        public void AdvanceFloor()
        {
            if (Run == null || Room != RoomView.Elevator) return;
            if (Run.Map.Floor >= Rules().Floors) return;
            ResolveCurrent();
        }

        // The one button every room page ends on. A room that still owes a decision refuses to close.
        public void LeaveRoom()
        {
            if (SupplyShopOpen) return;
            if (Run == null || !InRoom) return;
            if (Room == RoomView.Buff && !Run.Map.BuffChosen) { Hint = "先选择一项增益再离开。"; Refresh(); return; }
            if (Room == RoomView.Event && !EventCanLeave) { Hint = "这个事件还没有走完。"; Refresh(); return; }
            ResolveCurrent();
        }

        // Walks through the current position. Nothing heals here: the supply room owns recovery, and the
        // next position is only drawn once the party actually arrives.
        private void ResolveCurrent()
        {
            var step = Run.Map.Current;
            if (step == null) return;
            step.Resolved = true;
            // EVT-H001's passage drops the party at this floor's elevator instead of one position along.
            if (ActiveNode != null && ActiveNode.EventJump) { JumpToFloorEnd(); return; }
            WalkForward();
        }

        // The hidden passage shortcut. Rather than writing StepIndex ahead of the party on its own, this
        // walks the same positions Advance would have built, so the draw contract and the shown steps are
        // exactly what a walked route would have produced. Onto the last floor it does nothing: there is no
        // elevator down there, only the boss.
        private void JumpToFloorEnd()
        {
            var rules = Rules();
            int floor = Math.Min(Math.Max(Run.Map.Floor, 1), rules.Floors);
            if (floor >= rules.Floors) { WalkForward(); return; }
            int start = 0;
            for (int f = 1; f < floor; f++) start += MapGenerator.StepsOnFloor(rules, f);
            int target = start + MapGenerator.StepsOnFloor(rules, floor) - 1;
            if (target <= Run.Map.StepIndex) { WalkForward(); return; }
            Run.Map.StepIndex = target;
            RevealStep();
            Hint = "通道把队伍直接送到了第 " + floor + " 层的电梯井。";
            Refresh();
        }

        // ---------------- battles ----------------

        // Applies a route battle's payout exactly once. The settlement page calls this while it is being
        // built, which is what lets the result screen list the reward; ContinueBattle calls it again before
        // walking forward. A fight the event asked for pays nothing here, because ResolveBattleReturn pays
        // that reward when the room is re-entered.
        public void EnsureBattleRewards()
        {
            if (battleRewardsSettled) return;
            if (Battle == null || ActiveNode == null) return;
            battleRewardsSettled = true;
            battleRewardNote = null;
            if (Battle.Outcome != BattleOutcome.Victory) return;
            // A wipe ends the run without a payout even if the engine also called the fight a victory.
            if (Run.Heroes.All(a => a.Dead)) return;
            var node = ActiveNode;
            if (node == EventBattle) return;
            int supplies = Encounter.SuppliesFor(Rules(), node.Type);
            Run.Map.Supplies += supplies;
            battleRewardNote = "战斗胜利，获得 " + supplies + " 点矿石。";
            if (node.Type == NodeType.EliteBattle)
            {
                var drop = Encounter.DropProsthetic(Catalog.Rules().Limbs, Run.Random);
                if (drop != null)
                {
                    Run.Prosthetics.Add(drop);
                    battleRewardNote += "  掉落一件「" + drop.Name + "」，已放进队伍仓库。";
                }
            }
        }

        public void ContinueBattle()
        {
            if (Battle == null || Battle.Outcome == BattleOutcome.Active) return;
            if (Run.Heroes.All(a => a.Dead)) { FinishRun("全队阵亡，本次冒险结束。", false); return; }

            var node = ActiveNode;
            bool won = Battle.Outcome == BattleOutcome.Victory;
            // A fight started by a scripted event goes back to that event rather than forward along the
            // route: the event still owes the party its reward text and its last step.
            if (node != null && node == EventBattle) { ReturnToEvent(node, won); return; }
            if (node != null)
            {
                // The payout itself lives in EnsureBattleRewards so the result page can list it before
                // this button is pressed. Written to MapNote rather than RoomNote: the walk forward right
                // below clears the room notes, and a reward nobody can read is the same as no reward.
                if (won)
                {
                    EnsureBattleRewards();
                    MapNote = battleRewardNote;
                }
                else MapNote = "撤退：没有获得这个房间的矿石。";
            }
            if (node != null && node.Type == NodeType.Boss)
            {
                if (won) FinishRun("击败第 " + node.Floor + " 层首领，本局通关。", true);
                else FinishRun("从首领面前撤退，本局到此结束。", false);
                return;
            }
            // A retreat pays nothing, which is why the reward above is gated on the victory.
            ResolveCurrent();
        }

        // Comes back from an event fight. The fight is torn down by hand rather than through RevealStep,
        // because that would wipe ActiveNode, the room and the event's own progress and leave no way back.
        // The engine is dropped before Refresh so the enemy coroutine cannot run against a dead battle.
        private void ReturnToEvent(NodeState node, bool won)
        {
            StopAllCoroutines(); Busy = false; ClearPending();
            Battle = null; EventBattle = null;
            AwaitingNode = true; Room = RoomView.Event; ActiveNode = node;
            RoomNote = EventScripts.ResolveBattleReturn(node, Run, Rules(), won);
            Hint = RoomNote;
            Refresh();
        }

        private void FinishRun(string note, bool cleared)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); Battle = null;
            AwaitingNode = true; Room = RoomView.Map; ActiveNode = null; EventBattle = null;
            Run.Map.Finished = true; Run.Map.Cleared = cleared;
            RunEndNote = note;
            Hint = note;
            Refresh();
        }

        // The battle kind is passed in rather than read off the node: an event node is NodeType.Event, so
        // the elite branch of a scripted fight would otherwise never scale anything.
        private void StartBattle(NodeState node, NodeType kind)
        {
            StopAllCoroutines(); Busy = false; ClearPending();
            AwaitingNode = false;
            battleRewardsSettled = false; battleRewardNote = null;
            // Designed formations name their own ranks and already carry their final numbers, so they are
            // not scaled again. The legacy three-enemy fallback is kept for a catalog that has not been
            // regenerated since the monster pass.
            var enemies = Catalog.BuildEnemyLineup(kind, Run.Random);
            if (enemies.Length == 0)
                enemies = Encounter.Scale(Catalog.Enemies.Select(a => a.ToSpec()).ToArray(), kind, Rules());
            Battle = new BattleEngine(Run, enemies, Catalog.Rules());
            // Only a fight the event asked for comes back to the event; a route battle resolves the position.
            EventBattle = Room == RoomView.Event ? node : null;
            InspectedId = Battle.Current != null && Battle.Current.Player ? Battle.Current.Id : Run.Heroes[0].Id;
            Hint = "第 " + node.Floor + " 层 · " + (kind == node.Type ? node.Name : MapGenerator.Name(kind))
                + "。选择技能，再点击高亮目标；右侧可查看理智与技能概率。";
            Refresh();
            if (Battle.Current != null && !Battle.Current.Player) StartCoroutine(ContinueEnemies(Battle.Events.Count));
        }

        public void SelectSkill(int index)
        {
            if (CharacterPanelOpen) return;
            if (Busy || AwaitingNode || Battle == null) return;
            var skill = Battle.SkillFor(Battle.Current, index);
            if (skill == null) return;
            // Every slot the player clicks owns the description panel, usable or not. A blocked skill only
            // explains itself; it never arms a cast.
            InspectedSkill = index;
            string reason = Battle.SkillDisabledReason(index);
            if (reason != null)
            {
                PendingSkill = null; PendingSwap = false;
                Hint = reason;
                Refresh();
                return;
            }
            PendingSwap = false;
            PendingSkill = index;
            // A group skill has no single main target, but every living actor on its side is a valid
            // confirmation, so the page highlights them all and any one of them releases the skill.
            Hint = skill.Targeting == SkillTargeting.All
                ? "点击任意高亮目标释放「" + skill.Name + "」。Esc取消选择。"
                : "点击高亮目标以执行「" + skill.Name + "」。Esc取消选择。";
            Refresh();
        }

        public void SelectSwap()
        {
            if (CharacterPanelOpen) return;
            if (Busy || AwaitingNode || Battle == null) return;
            string reason = Battle.SwapDisabledReason();
            if (reason != null) { Hint = reason; Refresh(); return; }
            PendingSkill = null; PendingSwap = true; InspectedSkill = null;
            Hint = "点击相邻的高亮队员，交换站位并消耗本次行动。Esc取消选择。";
            Refresh();
        }

        private void ClearPending() { PendingSkill = null; PendingSwap = false; InspectedSkill = null; }

        public void SelectActor(string id)
        {
            if (CharacterPanelOpen) return;
            if (AwaitingNode || Battle == null) return;
            InspectedId = id;
            if (!Busy && PendingSkill.HasValue && Battle.Targets(PendingSkill.Value).Any(a => a.Id == id))
            { Submit(new ActionRequest(ActionKind.Skill, id, PendingSkill.Value)); return; }
            if (!Busy && PendingSwap && Battle.SwapTargets().Any(a => a.Id == id))
            { Submit(new ActionRequest(ActionKind.Swap, id)); return; }
            Refresh();
        }

        private void Submit(ActionRequest request)
        {
            int from = Battle.Events.Count;
            string error;
            if (!Battle.Execute(request, out error)) { Hint = error; Refresh(); return; }
            ClearPending();
            StartCoroutine(ContinueEnemies(from));
        }

        private IEnumerator ContinueEnemies(int from)
        {
            Busy = true; Refresh();
            yield return AnimateEvents(Battle.Events.Skip(from).ToArray(), AutomationMode);
            while (Battle.Outcome == BattleOutcome.Active && Battle.Current != null && !Battle.Current.Player)
            {
                Hint = Battle.Current.Name + " 即将行动。";
                Refresh();
                yield return new WaitForSeconds(AutomationMode ? 0.01f : 0.65f);
                int marker = Battle.Events.Count;
                Battle.ExecuteEnemy();
                Refresh();
                yield return AnimateEvents(Battle.Events.Skip(marker).ToArray(), AutomationMode);
            }
            Busy = false;
            if (Battle.Current != null && Battle.Current.Player) InspectedId = Battle.Current.Id;
            Hint = "选择技能，再点击高亮目标。Esc 取消目标；右上角「调试台」提供验收命令与后台信息。";
            Refresh();
        }

        // The HUD owns the cards, so it also owns the flash between two events. If nothing is bound the
        // wait still keeps the pace the old view had.
        public Func<BattleEvent[], bool, IEnumerator> BattleEventAnimation;
        private IEnumerator AnimateEvents(BattleEvent[] events, bool fast)
        {
            if (BattleEventAnimation != null) { yield return BattleEventAnimation(events, fast); yield break; }
            yield return new WaitForSeconds(fast ? 0.01f : 0.12f);
        }

        // Everything that changes state funnels through Refresh(), so this is the one place a second UI can
        // listen for changes instead of polling. Subscribers must only read: calling back into Refresh()
        // would loop.
        public event Action Changed;
        public void Refresh() { Changed?.Invoke(); }

        // ---- acceptance commands ----
        // Reached only from the authored debug console (Assets/IndustrialDebugUI). The commands stay
        // compiled in every build because that console is a scene object rather than generated code;
        // the console itself hides outside the editor and development builds.
        public void DebugSetSanity(double value)
        {
            if (Battle == null) return;
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null || Battle.Outcome != BattleOutcome.Active) return;
            // ChangeSanity refuses while the potion is active, so the tool must say so instead of claiming
            // it changed anything.
            if (actor.SanityLocked) { Hint = "理智已被锁定，无法调整。"; Refresh(); return; }
            Battle.ChangeSanity(actor, value - actor.Sanity, "验收工具调整");
            Hint = "已改变实际理智；本回合形态固定，下回合才重新抽取。";
            Refresh();
        }

        public void DebugSettle()
        {
            if (Battle == null) return;
            StopAllCoroutines(); Busy = false;
            CharacterPanelOpen = false; CharacterHeroId = null;
            Battle.Finish(BattleOutcome.Retreat); Refresh();
        }

        // Forcing a win matters for one flow above all: an event fight has to come back to the event and
        // pay its reward, and that path is unreachable without a victory.
        public void DebugWinBattle()
        {
            if (Battle == null) return;
            StopAllCoroutines(); Busy = false;
            CharacterPanelOpen = false; CharacterHeroId = null;
            Battle.Finish(BattleOutcome.Victory); Refresh();
        }

        public void DebugFeedbackExample()
        {
            NewRun(50);
            DebugForceRoom(NodeType.Battle);
            if (Battle == null) return;
            int[] delta = { -20, -10, 0, 10 };
            for (int i = 0; i < Run.Heroes.Count; i++) Battle.ChangeSanity(Run.Heroes[i], delta[i], "验收样例");
            Battle.Finish(BattleOutcome.Retreat);
            Refresh();
        }

        public void DebugDeathDoor()
        {
            NewRun(50);
            DebugForceRoom(NodeType.Battle);
            if (Battle == null) return;
            Run.Heroes[0].Hp = 0;
            InspectedId = Run.Heroes[0].Id;
            Hint = "盾卫处于濒死；等奶妈行动后使用「流血奶」，可以恢复生命。";
            Refresh();
        }

        // Battles no longer heal the party on their own; the supply room owns that, so the acceptance tool
        // keeps the recovery reachable without walking to one.
        public void DebugRestParty()
        {
            if (Battle == null) return;
            foreach (var hero in Run.Heroes.Where(a => !a.Dead)) hero.Hp = hero.Definition.MaxHp;
            Hint = "验收工具已整备全队生命；部位、个人理智和死亡记录沿用。";
            Refresh();
        }

        // Prosthetics are a run-level stock rather than a battle effect, so the limb tools work on the map
        // page too. That is what makes "walk into a supply room with a severed arm" reachable without
        // waiting for a cleaver to land its swing.
        public void DebugGrantProsthetic(LimbPart part)
        {
            if (Run == null) return;
            var item = LimbSystem.Resolve(Catalog.Rules().Limbs).Prosthetics.FirstOrDefault(p => p.Part == part);
            if (item == null) return;
            Run.Prosthetics.Add(item);
            Hint = "验收工具已发放" + item.Name + "。";
            Refresh();
        }

        public void DebugInjureLimb(LimbPart part)
        {
            if (Run == null) return;
            var hero = InspectedHero();
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "受到一次部位伤害，现在是"
                + LimbSystem.ConditionName(LimbSystem.ConditionOf(hero, part)) + "。";
            Refresh();
        }

        public void DebugSeverLimb(LimbPart part)
        {
            if (Run == null) return;
            var hero = InspectedHero();
            if (hero == null) return;
            var rules = Catalog.Rules().Limbs;
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            LimbSystem.Damage(hero, part, LimbSystem.HitAmount(hero, part, rules));
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经断肢，需要义肢才能恢复。";
            Refresh();
        }

        public void DebugGrantAllProsthetics()
        {
            if (Run == null) return;
            foreach (var item in LimbSystem.Resolve(Catalog.Rules().Limbs).Prosthetics)
                if (!Run.Prosthetics.Any(p => p != null && p.Id == item.Id)) Run.Prosthetics.Add(item);
            Hint = "验收工具已发放全部义肢。";
            Refresh();
        }

        public void DebugGrantAllAccessories()
        {
            if (Run == null) return;
            int granted = 0;
            foreach (var item in AccessorySystem.Defaults)
            {
                if (item == null) continue;
                Run.AccessoryStock.Add(AccessorySystem.Clone(item));
                granted++;
            }
            Hint = "验收工具已发放全部 " + granted + " 件饰品。";
            Refresh();
        }

        public void DebugGrantAccessory(AccessoryKind kind)
        {
            if (Run == null) return;
            var item = AccessorySystem.Defaults.FirstOrDefault(a => a != null && a.Kind == kind);
            if (item == null) return;
            Run.AccessoryStock.Add(AccessorySystem.Clone(item));
            Hint = "验收工具已发放「" + item.Name + "」。";
            Refresh();
        }

        public void DebugGrantAllConsumables()
        {
            if (Run == null) return;
            int granted = 0;
            foreach (var item in ConsumableSystem.Defaults)
            {
                if (item == null) continue;
                Run.Consumables.Add(ConsumableSystem.Clone(item));
                granted++;
            }
            Hint = "验收工具已发放全部 " + granted + " 件道具。";
            Refresh();
        }

        public void DebugGrantConsumable(ConsumableKind kind)
        {
            if (Run == null) return;
            var item = ConsumableSystem.ForKind(kind);
            if (item == null) return;
            Run.Consumables.Add(ConsumableSystem.Clone(item));
            Hint = "验收工具已发放「" + item.Name + "」。";
            Refresh();
        }

        // Prefers whoever the inspector is showing, the same way every other debug tool does.
        private ActorState InspectedHero()
        {
            var actor = Run.Heroes.FirstOrDefault(a => a.Id == InspectedId && !a.Dead);
            return actor ?? Run.Heroes.FirstOrDefault(a => !a.Dead);
        }

        // Jumps to the first position of a floor. The positions in between are still built in order inside
        // Advance, so the random sequence stays the one a walked route would have produced.
        public void DebugEnterFloor(int floor)
        {
            if (Run == null) return;
            var rules = Rules();
            StopAllCoroutines(); Busy = false; ClearPending();
            CharacterPanelOpen = false; CharacterHeroId = null; Battle = null;
            int target = Math.Min(Math.Max(floor, 1), rules.Floors);
            int start = 0;
            for (int f = 1; f < target; f++) start += MapGenerator.StepsOnFloor(rules, f);
            Run.Map.StepIndex = start;
            Run.Map.Finished = false; Run.Map.Cleared = false; RunEndNote = null;
            RevealStep();
            Hint = "验收工具已跳到第 " + target + " 层。";
            Refresh();
        }

        // Jumps to the last position of the current floor, which is the elevator on floors 1 and 2 and the
        // boss on the last one. That is what lets the smoke run reach the end without playing every fight.
        public void DebugSkipToFloorEnd()
        {
            if (Run == null || Run.Map.Finished) return;
            var rules = Rules();
            int floor = Math.Min(Math.Max(Run.Map.Floor, 1), rules.Floors);
            int start = 0;
            for (int f = 1; f < floor; f++) start += MapGenerator.StepsOnFloor(rules, f);
            Run.Map.StepIndex = start + MapGenerator.StepsOnFloor(rules, floor) - 1;
            RevealStep();
            Hint = "验收工具已跳到第 " + floor + " 层的末端。";
            Refresh();
        }

        // Forces the current position to hold one named event, so a script with a hidden branch (a sanity
        // gate, a 30/70, a repeatable loop) can be reached on demand instead of rolled for.
        public void DebugForceEvent(string eventId)
        {
            if (Run == null || Run.Map.Finished) return;
            // SelectNode refuses while the character panel is up, and this button can be pressed from the
            // console while it is open, so close that page first or the forced room never opens.
            CharacterPanelOpen = false; CharacterHeroId = null;
            var step = MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            if (step == null) return;
            bool altar = eventId == "EVT-008";
            step.Candidates.Clear();
            step.Candidates.Add(new NodeState
            {
                Floor = step.Floor, Index = step.Index,
                Type = altar ? NodeType.Altar : NodeType.Event, OptionIndex = 0
            });
            ForcedEvent = eventId;
            SelectNode(0);
        }

        // Replaces the current position with one forced room type and walks into it, so the supply room,
        // an event, an altar and the elevator are all reachable on demand.
        public void DebugForceRoom(NodeType type)
        {
            if (Run == null || Run.Map.Finished) return;
            // Same reason as DebugForceEvent: the character panel has to be out of the way.
            CharacterPanelOpen = false; CharacterHeroId = null;
            var step = MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            if (step == null) return;
            step.Candidates.Clear();
            step.Candidates.Add(new NodeState { Floor = step.Floor, Index = step.Index, Type = type, OptionIndex = 0 });
            SelectNode(0);
        }
    }
}
