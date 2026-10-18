using System;
using System.Collections;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    // Which page the node half of the game is showing. A battle is not a room: the battle page is driven
    // by AwaitingNode being false, exactly as it was before the route rework.
    public enum RoomView { Map, Buff, Supply, Event, Elevator, Treasure }

    public sealed class StageOneApp : MonoBehaviour
    {
        public GameCatalog Catalog;
        public RunState Run { get; private set; }
        public BattleEngine Battle { get; private set; }
        public StageOneView View { get; private set; }
        // 本回合已经点开、正等着点目标的技能下标。换位走一个单独的开关，因为它没有下标。
        public int? PendingSkill { get; private set; }
        public bool PendingSwap { get; private set; }
        // 任意一种「已选好行动、等待点目标」的状态。
        public bool Choosing { get { return PendingSkill.HasValue || PendingSwap; } }
        public string InspectedId { get; private set; }
        public bool Busy { get; private set; }
        public bool HelpOpen { get; private set; }
        public bool LabOpen { get; private set; }
        // The accessory manager is a modal that both UI systems share: StageOneView draws it above the
        // external HUD, and the external HUD only routes clicks into these commands.
        public bool AccessoryEditorOpen { get; private set; }
        public string AccessoryHeroId { get; private set; }
        // The consumable panel follows the same rule: StageOneView draws it above either UI system, and
        // both the built-in pages and the external HUD only route clicks into these commands.
        public bool ConsumableEditorOpen { get; private set; }
        public string ConsumableHeroId { get; private set; }
        // Which page of the acceptance tool is up: the general one, or the per-event list.
        public bool LabEvents { get; private set; }
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
        // Off by default, so StageOne.unity, the smoke run and the play mode tests keep the built-in pages.
        // A scene that supplies its own UI turns this on, and StageOneView then stops drawing every page —
        // battle and nodes alike — keeping only the settlement, help and lab modals, which still have to
        // cover the external UI (that is what the canvas sorting order is for).
        public bool ExternalBattleUi;
        public const uint DefaultSeed = 44972;

        // A room page is open, so the debug tool stays shut the way it did around the workshop.
        public bool InRoom { get { return AwaitingNode && Room != RoomView.Map; } }

        private MapRules Rules() { return Catalog.MapRules(); }

        // Debug only: an event id the next room roll must take instead of the weighted draw.
        private string ForcedEvent;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Catalog = Catalog ? Catalog : Resources.Load<GameCatalog>("StageOne/GameCatalog");
            if (!Catalog) { Debug.LogError("StageOne catalog missing. Run GameMake/Prepare Stage One from the editor."); return; }
            View = gameObject.AddComponent<StageOneView>();
            View.Initialize(this);
            NewRun();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Environment.GetCommandLineArgs().Contains("--stage1-smoke")) gameObject.AddComponent<StageOneSmoke>();
#endif
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) ToggleHelp();
            if (Input.GetKeyDown(KeyCode.I)) ToggleConsumableEditor();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F3)) ToggleLab();
#endif
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (ConsumableEditorOpen) CloseConsumableEditor();
                else if (AccessoryEditorOpen) CloseAccessoryEditor();
                else if (HelpOpen) ToggleHelp();
                // Back out of the event list first, so one Esc does not lose the whole tool.
                else if (LabOpen && LabEvents) ToggleLabEvents();
                else if (LabOpen) ToggleLab();
                else { ClearPending(); Refresh(); }
            }
        }

        public void NewRun(double? preset = null)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); HelpOpen = false; LabOpen = false;
            AccessoryEditorOpen = false; AccessoryHeroId = null;
            ConsumableEditorOpen = false; ConsumableHeroId = null;
            Room = RoomView.Map; ActiveNode = null; EventBattle = null; RoomNote = null; MapNote = null;
            RunEndNote = null;
            // The limb rules travel with the rest of the combat rules, so the catalog's part ratios and
            // penalties seed every hero's limb table.
            Run = RunState.Create(Catalog.Heroes.Select(a => a.ToSpec()).ToArray(), DefaultSeed, preset, Catalog.Rules());
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
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false;
            AccessoryEditorOpen = false; AccessoryHeroId = null;
            ConsumableEditorOpen = false; ConsumableHeroId = null; Battle = null;
            Room = RoomView.Map; ActiveNode = null; EventBattle = null; RoomNote = null;
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
            if (Busy || HelpOpen || LabOpen || AccessoryEditorOpen || ConsumableEditorOpen
                || Run == null || Run.Map.Finished) return;
            if (Room != RoomView.Map) return;
            var step = Run.Map.Current;
            if (step == null || step.Resolved || index < 0 || index >= step.Candidates.Count) return;
            var node = step.Candidates[index];
            step.ChosenIndex = index;
            ActiveNode = node;
            MapNote = null;

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

        // ---------------- openings, elevators, and the room pages ----------------

        // The opening room hands out one team buff. It is applied to the heroes' own definitions: each of
        // those is the fresh ActorSpec the catalog handed out, so nothing here writes into the asset.
        private void EnterBuffRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
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
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Supply; AwaitingNode = true; RoomNote = null;
            Run.Map.SupplyHealed = false; Run.Map.SupplyCalmed = false;
            Hint = "补给站：治疗全队、修复重伤部位、为断肢安装义肢、静养恢复理智。离开后继续前进。";
            Refresh();
        }

        public void SupplyHeal()
        {
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyHealed) return;
            int healed = Exploration.SupplyHealParty(Rules(), Run);
            Run.Map.SupplyHealed = true;
            Hint = "补给站为全队恢复 " + healed + " 点生命。阵亡成员不会复活。";
            Refresh();
        }

        public void SupplyCalm()
        {
            if (Run == null || Room != RoomView.Supply || Run.Map.SupplyCalmed) return;
            double gained = Exploration.SupplyCalm(Run);
            Run.Map.SupplyCalmed = true;
            Hint = "全队静养，玩家理智 +" + gained.ToString("0.0") + "。";
            Refresh();
        }

        public void RepairLimb(string heroId, LimbPart part)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            if (hero == null) return;
            if (!LimbSystem.Repair(hero, part)) { Hint = "只有重伤部位能在这里修复；断肢需要安装义肢。"; Refresh(); return; }
            Hint = hero.Name + " 的" + LimbSystem.PartName(part) + "已经修复。";
            Refresh();
        }

        public void InstallProsthetic(string heroId, string itemId)
        {
            if (Run == null || Room != RoomView.Supply) return;
            var hero = Run.Heroes.FirstOrDefault(a => a.Id == heroId);
            var item = Run.Prosthetics.FirstOrDefault(p => p != null && p.Id == itemId);
            if (hero == null || item == null) return;
            if (!LimbSystem.Install(hero, item)) { Hint = "只有断肢才能安装义肢。"; Refresh(); return; }
            Run.Prosthetics.Remove(item);
            Hint = hero.Name + " 装上了" + item.Name + "，" + LimbSystem.PartName(item.Part) + "恢复基础功能。";
            Refresh();
        }

        // ---------------- accessories ----------------

        // The manager is reachable from the supply room and from the acceptance tool. It only needs a run
        // that is standing on a node, so it works on the route page and inside a room alike.
        public void OpenAccessoryEditor(string heroId = null)
        {
            if (Run == null || Busy || !AwaitingNode || ConsumableEditorOpen) return;
            var hero = !string.IsNullOrEmpty(heroId) ? Run.Heroes.FirstOrDefault(h => h.Id == heroId) : null;
            if (hero == null) hero = Run.Heroes.FirstOrDefault(h => h.Id == AccessoryHeroId)
                ?? Run.Heroes.FirstOrDefault(h => !h.Dead) ?? Run.Heroes.FirstOrDefault();
            AccessoryHeroId = hero == null ? null : hero.Id;
            AccessoryEditorOpen = true; HelpOpen = false; LabOpen = false; LabEvents = false;
            ClearPending(); Refresh();
        }

        public void CloseAccessoryEditor()
        {
            if (!AccessoryEditorOpen) return;
            AccessoryEditorOpen = false; ClearPending(); Refresh();
        }

        public void SelectAccessoryHero(string heroId)
        {
            if (!AccessoryEditorOpen || Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            AccessoryHeroId = hero.Id; Refresh();
        }

        public void EquipAccessory(string heroId, int stockIndex)
        {
            if (!AccessoryEditorOpen || Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            string note;
            if (AccessorySystem.Equip(Run, hero, stockIndex, out note))
            {
                AccessoryHeroId = hero.Id;
                Hint = note;
            }
            else Hint = note;
            Refresh();
        }

        public void UnequipAccessory(string heroId, string itemId)
        {
            if (!AccessoryEditorOpen || Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            string note;
            if (AccessorySystem.Unequip(Run, hero, itemId, out note))
            {
                AccessoryHeroId = hero.Id;
                Hint = note;
            }
            else Hint = note;
            Refresh();
        }

        // ---------------- consumables ----------------

        // Reachable everywhere a battle is not mid-enemy-turn: the route, every room, and our own battle
        // turns. The accessory manager and this panel never stand open together.
        public void ToggleConsumableEditor()
        {
            if (ConsumableEditorOpen) CloseConsumableEditor();
            else OpenConsumableEditor();
        }

        public void OpenConsumableEditor(string heroId = null)
        {
            if (Run == null || Busy || HelpOpen || LabOpen) return;
            if (!AwaitingNode && (Battle == null || Battle.Outcome != BattleOutcome.Active
                || Battle.Current == null || !Battle.Current.Player)) return;
            var hero = !string.IsNullOrEmpty(heroId) ? Run.Heroes.FirstOrDefault(h => h.Id == heroId) : null;
            if (hero == null) hero = Run.Heroes.FirstOrDefault(h => h.Id == ConsumableHeroId);
            if (hero == null && !AwaitingNode && Battle != null && Battle.Current != null && Battle.Current.Player)
                hero = Battle.Current;
            if (hero == null) hero = Run.Heroes.FirstOrDefault(h => !h.Dead) ?? Run.Heroes.FirstOrDefault();
            ConsumableHeroId = hero == null ? null : hero.Id;
            ConsumableEditorOpen = true;
            // One modal at a time: the two managers share the same canvas and the same Escape key.
            AccessoryEditorOpen = false; AccessoryHeroId = null;
            HelpOpen = false; LabOpen = false; LabEvents = false;
            ClearPending(); Refresh();
        }

        public void CloseConsumableEditor()
        {
            if (!ConsumableEditorOpen) return;
            ConsumableEditorOpen = false; ClearPending(); Refresh();
        }

        public void SelectConsumableHero(string heroId)
        {
            if (!ConsumableEditorOpen || Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;
            ConsumableHeroId = hero.Id; Refresh();
        }

        public void UseConsumable(string heroId, int stockIndex)
        {
            if (!ConsumableEditorOpen || Run == null) return;
            var hero = Run.Heroes.FirstOrDefault(h => h.Id == heroId);
            if (hero == null) return;

            if (!AwaitingNode && Battle != null)
            {
                int from = Battle.Events.Count;
                string battleNote;
                if (!Battle.UseConsumable(stockIndex, heroId, out battleNote))
                { Hint = battleNote; Refresh(); return; }
                ConsumableHeroId = hero.Id;
                ConsumableEditorOpen = false; ClearPending();
                Hint = battleNote;
                StartCoroutine(ContinueEnemies(from));
                return;
            }

            string note;
            if (ConsumableSystem.Use(Run, hero, stockIndex, false, out note))
                ConsumableHeroId = hero.Id;
            Hint = note;
            // The panel stays open outside battle so a second item can be used without reopening it.
            Refresh();
        }

        // Events and altars share one page. Which event the room holds, and everything that event rolled on
        // entry, is frozen here; EventScripts.Page then rebuilds the current step on every render without
        // drawing anything, so re-rendering never re-rolls the room.
        private void EnterEventRoom(NodeState node)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
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
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            Room = RoomView.Elevator; AwaitingNode = true; RoomNote = null;
            Hint = "电梯井：本层到此为止。下行之后不会自动整备生命。";
            Refresh();
        }

        // The chest room is the accessory source. EventFlag doubles as the "already opened" marker for
        // this node, so reopening the page never draws a second item.
        private void EnterTreasureRoom()
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
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
            if (Run == null || !InRoom || AccessoryEditorOpen || ConsumableEditorOpen) return;
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
                // Written to MapNote rather than RoomNote: the walk forward right below clears the room
                // notes, and a reward nobody can read is the same as no reward.
                if (won)
                {
                    int supplies = Encounter.SuppliesFor(Rules(), node.Type);
                    Run.Map.Supplies += supplies;
                    MapNote = "战斗胜利，获得 " + supplies + " 点补给。";
                    if (node.Type == NodeType.EliteBattle)
                    {
                        var drop = Encounter.DropProsthetic(Catalog.Rules().Limbs, Run.Random);
                        if (drop != null) { Run.Prosthetics.Add(drop); MapNote += "  掉落一件「" + drop.Name + "」，已放进队伍仓库。"; }
                    }
                }
                else MapNote = "撤退：没有获得这个房间的补给。";
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
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false;
            ConsumableEditorOpen = false; ConsumableHeroId = null;
            Battle = null; EventBattle = null;
            AwaitingNode = true; Room = RoomView.Event; ActiveNode = node;
            RoomNote = EventScripts.ResolveBattleReturn(node, Run, Rules(), won);
            Hint = RoomNote;
            Refresh();
        }

        private void FinishRun(string note, bool cleared)
        {
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false; Battle = null;
            ConsumableEditorOpen = false; ConsumableHeroId = null;
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
            StopAllCoroutines(); Busy = false; ClearPending(); LabOpen = false;
            ConsumableEditorOpen = false; ConsumableHeroId = null;
            AwaitingNode = false;
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
            if (Busy || HelpOpen || LabOpen || ConsumableEditorOpen || AwaitingNode || Battle == null) return;
            string reason = Battle.SkillDisabledReason(index);
            if (reason != null) { Hint = reason; Refresh(); return; }
            PendingSwap = false; PendingSkill = index;
            Hint = "点击高亮目标以执行「" + Battle.SkillFor(Battle.Current, index).Name + "」。Esc取消选择。";
            Refresh();
        }

        public void SelectSwap()
        {
            if (Busy || HelpOpen || LabOpen || ConsumableEditorOpen || AwaitingNode || Battle == null) return;
            string reason = Battle.SwapDisabledReason();
            if (reason != null) { Hint = reason; Refresh(); return; }
            PendingSkill = null; PendingSwap = true;
            Hint = "点击相邻的高亮队员，交换站位并消耗本次行动。Esc取消选择。";
            Refresh();
        }

        private void ClearPending() { PendingSkill = null; PendingSwap = false; }

        public void SelectActor(string id)
        {
            if (AwaitingNode || Battle == null || ConsumableEditorOpen) return;
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
            yield return View.AnimateEvents(Battle.Events.Skip(from).ToArray(), AutomationMode);
            while (Battle.Outcome == BattleOutcome.Active && Battle.Current != null && !Battle.Current.Player)
            {
                Hint = Battle.Current.Name + " 即将行动。";
                Refresh();
                yield return new WaitForSeconds(AutomationMode ? 0.01f : 0.65f);
                int marker = Battle.Events.Count;
                Battle.ExecuteEnemy();
                Refresh();
                yield return View.AnimateEvents(Battle.Events.Skip(marker).ToArray(), AutomationMode);
            }
            Busy = false;
            if (Battle.Current != null && Battle.Current.Player) InspectedId = Battle.Current.Id;
            Hint = "选择技能，再点击高亮目标。F1查看说明，I使用道具，F3打开验收工具。";
            Refresh();
        }

        public void ToggleHelp()
        {
            if (AccessoryEditorOpen || ConsumableEditorOpen) return;
            HelpOpen = !HelpOpen; LabOpen = false; ClearPending(); Refresh();
        }
        public void ToggleLab()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Closing always wins: a forced room can open a page while the tool is up, and without this the
            // tool would be stuck open because the room page blocks the reopen path.
            if (LabOpen) { LabOpen = false; LabEvents = false; ClearPending(); Refresh(); return; }
            if (AccessoryEditorOpen || ConsumableEditorOpen || Busy || InRoom) return;
            LabOpen = true; HelpOpen = false; LabEvents = false; ClearPending(); Refresh();
#endif
        }

        // The acceptance tool's second page: one button per event script, so a branch that hides behind a
        // sanity gate or a 30/70 does not have to be rolled for.
        public void ToggleLabEvents()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LabEvents = !LabEvents; ClearPending(); Refresh();
#endif
        }
        // Everything that changes state funnels through Refresh(), so this is the one place a second UI can
        // listen for changes instead of polling. Subscribers must only read: calling back into Refresh()
        // would loop.
        public event Action Changed;
        public void Refresh() { if (View) View.Render(); Changed?.Invoke(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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

        public void DebugMental(MentalState mental)
        {
            if (Battle == null) return;
            var actor = Battle.Find(InspectedId);
            if (actor == null || !actor.Player || actor.Dead) actor = Run.Heroes.FirstOrDefault(a => !a.Dead);
            if (actor == null) return;
            actor.Mental = mental; Refresh();
        }

        public void DebugSettle()
        {
            if (Battle == null) return;
            StopAllCoroutines(); Busy = false; LabOpen = false; Battle.Finish(BattleOutcome.Retreat); Refresh();
        }

        // Forcing a win matters for one flow above all: an event fight has to come back to the event and
        // pay its reward, and that path is unreachable without a victory.
        public void DebugWinBattle()
        {
            if (Battle == null) return;
            StopAllCoroutines(); Busy = false; LabOpen = false; Battle.Finish(BattleOutcome.Victory); Refresh();
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
            Hint = "验收工具已整备全队生命；部位、个人理智、精神状态和死亡记录沿用。";
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
            StopAllCoroutines(); Busy = false; ClearPending(); HelpOpen = false; LabOpen = false; Battle = null;
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
            // SelectNode refuses while a modal is up, and this button is only ever pressed *from* the tool,
            // so the tool has to stand down first or the forced room silently never opens.
            LabOpen = false;
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
            // Same reason as DebugForceEvent: the button lives inside the tool, so the tool has to close.
            LabOpen = false;
            var step = MapGenerator.Advance(Run.Map, Rules(), Run.Random, Run.PlayerSanity);
            if (step == null) return;
            step.Candidates.Clear();
            step.Candidates.Add(new NodeState { Floor = step.Floor, Index = step.Index, Type = type, OptionIndex = 0 });
            SelectNode(0);
        }
#endif
    }
}
