using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruin.Core
{
    // One button on the current page. Run is the "option listener": it is the whole behaviour of the
    // choice, written inline at the point the choice is declared, so changing an option means changing
    // one lambda instead of chasing a table of effect fields.
    public sealed class EventChoice
    {
        public string Label;
        public string Detail;
        public bool Shown = true;
        public bool Enabled = true;
        public Action<EventSession> Run;
        public EventChoice(string label, string detail, Action<EventSession> run)
        { Label = label; Detail = detail; Run = run; }
    }

    // What the current step of an event looks like. Page() is a pure read: it never draws from the random
    // sequence and never writes to the node, so re-rendering the room is always safe (rule 5 of the plan:
    // shown options stay fixed and a reload never re-rolls them).
    public sealed class EventPage
    {
        public string Title;
        public string Body;
        public string Hint;
        public int Step;      // 1-based; 0 hides the "step x/y" indicator.
        public int StepCount;
        public bool CanLeave;
        public readonly List<EventChoice> Choices = new List<EventChoice>();
    }

    // The context one choice runs in. It carries the run state and the handful of helpers the event text
    // needs, plus the two things a step can ask the room to do afterwards: end, or start a fight.
    public sealed class EventSession
    {
        public readonly NodeState Node;
        public readonly RunState Run;
        public readonly MapRules Rules;

        public string Note;
        public bool WantsBattle;
        public NodeType BattleKind = NodeType.Battle;

        public EventSession(NodeState node, RunState run, MapRules rules) { Node = node; Run = run; Rules = rules; }

        // The document leaves "how high sanity decides" open; 50 is the provisional split, applied to the
        // shared player sanity the way the map already reads it.
        public bool HighSanity { get { return Run != null && Run.PlayerSanity >= EventScripts.SanitySplit; } }

        // Finishes the whole event. The room then shows the closing text and lets the party walk on.
        public void End(string note) { Note = note; Node.EventStep = EventScripts.DoneStep; Node.OptionChosen = -1; }

        // Moves to another step of the same script. The per-step "already answered" marker is cleared so the
        // new step starts unanswered.
        public void GoTo(int step, string note) { Note = note; Node.EventStep = step; Node.OptionChosen = -1; }

        // Starts a fight on the way out. A terminal fight ends the event; only EVT-H001 asks for a page
        // after the fight, so its handler calls RequestBattle without End.
        public void RequestBattle(NodeType kind, string note) { WantsBattle = true; BattleKind = kind; Note = note; }

        public List<ActorState> Living()
        {
            var list = new List<ActorState>();
            for (int i = 0; i < Run.Heroes.Count; i++) if (!Run.Heroes[i].Dead) list.Add(Run.Heroes[i]);
            return list;
        }

        // The document ties every event-level sanity change to the whole party, never to the shared player
        // sanity on its own. Dead members are left alone.
        public double TeamSanity(double delta)
        {
            double moved = 0;
            for (int i = 0; i < Run.Heroes.Count; i++)
            {
                var hero = Run.Heroes[i];
                if (hero.Dead) continue;
                double before = hero.Sanity;
                hero.Sanity = SanityProbability.Clamp(before + delta, 0, 100);
                moved += hero.Sanity - before;
            }
            return moved;
        }

        public void GainSupplies(int amount, string what)
        {
            Run.Map.Supplies = Math.Max(0, Run.Map.Supplies + amount);
            Note = (Note == null ? "" : Note + " ") + (what ?? "矿石") + (amount >= 0 ? " +" : " ") + amount + "。";
        }

        public int HealHero(ActorState hero, int percent) { return Exploration.Heal(hero, percent); }

        // Prosthetics are the run's stock item, handed out as a fresh copy of the shared table so the table
        // itself is never aliased. Returns the item so the caller can name it in the text. The catalog's
        // LimbRules carries no prosthetic table, so null and the catalog resolve to the same default list.
        public ProstheticSpec GrantProsthetic()
        {
            var item = Encounter.DropProsthetic(null, Run.Random);
            if (item != null) Run.Prosthetics.Add(item);
            return item;
        }

        // The three event rewards whose text says 饰品 now hand out an accessory instead of a prosthetic.
        // The item is a fresh copy from the code table, so the run stock never aliases it.
        public AccessorySpec GrantAccessory()
        {
            var item = Encounter.DropAccessory(Run.Random);
            if (item != null) Run.AccessoryStock.Add(item);
            return item;
        }

        // Outside a fight nothing performs the death check, so an event wound is never lethal: health stops
        // at 1. The document leaves the exact number and the "what if it runs out" case open.
        public int DamageHero(ActorState hero, int percent)
        {
            if (hero == null || hero.Dead || percent <= 0) return 0;
            int amount = Math.Max(1, (int)Math.Ceiling(hero.MaxHp * percent / 100.0 - 1e-9));
            int before = hero.Hp;
            hero.Hp = Math.Max(1, hero.Hp - amount);
            return before - hero.Hp;
        }

        // The large-enemy branch of the rest event scrambles the formation; ranks are the only thing that
        // moves, so health, sanity, parts and statuses all survive.
        public void ShuffleRanks()
        {
            var ranks = new List<int>();
            for (int i = 0; i < Run.Heroes.Count; i++) ranks.Add(Run.Heroes[i].Rank);
            for (int i = ranks.Count - 1; i > 0; i--)
            {
                int j = (int)(Run.Random.NextUnit() * (i + 1));
                if (j > i) j = i;
                int tmp = ranks[i]; ranks[i] = ranks[j]; ranks[j] = tmp;
            }
            for (int i = 0; i < Run.Heroes.Count; i++) Run.Heroes[i].Rank = ranks[i];
        }
    }

    // All ten documented events, written as code rather than as data assets: each one is a small set of
    // steps and, per step, a switch over the node's EventStep with the option handlers declared inline.
    public static class EventScripts
    {
        public const double SanitySplit = 50;  // Provisional high/low boundary.
        public const int DoneStep = 99;        // The step value that means "this event is over".
        public const double AllDrinkSuccess = 0.30; // EVT-004 全部喝下: the user-specified 30/70.

        // EVT-008 exchange rates. Supplies are the run's only currency, and the supply shop prices a
        // prosthetic at 4 supplies, so 16 sanity is its matching value. HP trades one for one.
        public const int AltarSanityPerSupply = 4;
        public const int AltarSanityPerHealth = 1;
        public const int AltarSanityPerProsthetic = 16;

        private enum Gate { Always, High, Low }

        private sealed class Script
        {
            public string Id;
            public string Title;
            public NodeType Kind;
            public Gate Gate = Gate.Always;
            public int Weight = 1;
            // Recovery is not offered until the party has fought at least once: a heal room in the
            // first position of the route would hand out health before any battle could take it away.
            public bool Healing;
        }

        // EVT-008 is the altar: the workshop serves whoever walks in, it does not happen to them.
        private static readonly Script[] Catalog =
        {
            new Script { Id = "EVT-001", Title = "废弃矿道", Kind = NodeType.Event },
            new Script { Id = "EVT-002", Title = "失控的机器", Kind = NodeType.Event },
            new Script { Id = "EVT-003", Title = "铁门后的敲击声", Kind = NodeType.Event },
            new Script { Id = "EVT-004", Title = "破损的药剂箱", Kind = NodeType.Event },
            new Script { Id = "EVT-005", Title = "应急医疗室", Kind = NodeType.Event, Healing = true },
            new Script { Id = "EVT-006", Title = "安全角落休整", Kind = NodeType.Event, Healing = true },
            new Script { Id = "EVT-007", Title = "剧情事件", Kind = NodeType.Event },
            new Script { Id = "EVT-008", Title = "废弃义肢工坊", Kind = NodeType.Altar },
            new Script { Id = "EVT-H001", Title = "隐藏的岔道", Kind = NodeType.Event, Gate = Gate.High },
            new Script { Id = "EVT-L001", Title = "第五个人", Kind = NodeType.Event, Gate = Gate.Low }
        };

        private static Script Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Catalog.Length; i++) if (Catalog[i].Id == id) return Catalog[i];
            return null;
        }

        private static bool Allows(Gate gate, RunState run)
        {
            double sanity = run == null ? SanitySplit : run.PlayerSanity;
            if (gate == Gate.High) return sanity >= SanitySplit;
            if (gate == Gate.Low) return sanity < SanitySplit;
            return true;
        }

        // A script is eligible when its sanity gate matches and, for the two recovery rooms, the
        // party has already fought once. Battles is incremented by BattleEngine's constructor, so
        // this includes event-triggered fights.
        private static bool Eligible(Script script, RunState run)
        {
            if (!Allows(script.Gate, run)) return false;
            if (script.Healing && (run == null || run.Battles < 1)) return false;
            return true;
        }

        public static List<string> Pool(NodeType kind, RunState run)
        {
            var ids = new List<string>();
            for (int i = 0; i < Catalog.Length; i++)
                if (Catalog[i].Kind == kind && Eligible(Catalog[i], run)) ids.Add(Catalog[i].Id);
            return ids;
        }

        public static string Title(string id) { var s = Find(id); return s == null ? "未知事件" : s.Title; }

        // Rolls which event the room holds. Called once, when the party walks in; the id and the step
        // counter are frozen on the node so nothing downstream re-rolls. EVT-001's dig cap is rolled here
        // for the same reason.
        public static void Roll(NodeState node, RunState run, MapRules rules) { Roll(node, run, rules, null); }

        // forceId is the acceptance tool's way in: it pins the room to one script regardless of the gate,
        // which is the only way to reach a low-sanity event without walking the whole route at low sanity.
        public static void Roll(NodeState node, RunState run, MapRules rules, string forceId)
        {
            if (node == null || run == null) return;

            var forced = Find(forceId);
            // Re-entering a room that already holds a script must not re-roll it, pay EVT-001's entry
            // haul twice, or add a second history entry. A valid forceId is still allowed to replace it.
            if (forced == null && !string.IsNullOrEmpty(node.EventId)) return;

            node.OptionChosen = -1;
            node.EventId = null;
            node.EventStep = 0;
            node.EventCount = 0;
            node.EventLimit = 0;
            node.EventTarget = null;
            node.EventFlag = 0;
            node.EventJump = false;

            if (forced != null)
            {
                // The acceptance tool bypasses both the sanity gate and the first-battle rule, and it
                // stays out of the history so a forced room cannot make the next normal room skip.
                node.EventId = forced.Id;
            }
            else
            {
                var pool = Pool(node.Type, run);
                if (pool.Count == 0) return;
                node.EventId = Select(pool, run);
                if (run.Map.EventHistory == null) run.Map.EventHistory = new List<string>();
                run.Map.EventHistory.Add(node.EventId);
            }

            if (node.EventId == "EVT-001")
            {
                // The document fixes the cap at "2 to 5 digs" without saying how it is chosen; a uniform
                // draw in that range is the provisional reading. The entry haul lands immediately.
                node.EventLimit = 2 + (int)(run.Random.NextUnit() * 4);
                if (node.EventLimit > 5) node.EventLimit = 5;
                run.Map.Supplies += 3;
            }
            // The altar's two trades are frozen here, once, for the same reason as the mine's cap: the
            // page must show the same numbers every time it is rebuilt.
            if (node.EventId == "EVT-008") RollAltar(node, run);
        }

        public static bool CanLeave(NodeState node)
        {
            return node == null || node.EventStep >= DoneStep || Find(node.EventId) == null;
        }

        // Pure read: builds the page for whichever step the node currently sits on.
        public static EventPage Page(NodeState node, RunState run, MapRules rules)
        {
            var page = new EventPage();
            if (node == null) return page;
            var script = Find(node.EventId);
            if (script == null) { page.Title = "空房间"; page.Body = "这里什么也没有留下。"; page.CanLeave = true; return page; }
            page.Title = script.Title;
            page.CanLeave = node.EventStep >= DoneStep;
            if (page.CanLeave) { page.Body = "这一处已经结束，可以继续前进。"; return page; }

            switch (script.Id)
            {
                case "EVT-001": Mine(node, run, page); break;
                case "EVT-002": Machine(node, run, page); break;
                case "EVT-003": IronDoor(node, run, page); break;
                case "EVT-004": PotionChest(node, run, page); break;
                case "EVT-005": Infirmary(node, run, page); break;
                case "EVT-006": Rest(node, run, page); break;
                case "EVT-007": Story(page); break;
                case "EVT-008": Workshop(node, run, page); break;
                case "EVT-H001": HiddenFork(node, run, page); break;
                case "EVT-L001": FifthPerson(node, run, page); break;
            }
            return page;
        }

        // Runs the chosen option. Refused without touching anything when the index is out of range, the
        // option is hidden, or the current step has already been answered.
        public static bool Choose(NodeState node, int index, RunState run, MapRules rules, out EventSession session)
        {
            session = null;
            if (node == null || run == null) return false;
            if (node.OptionChosen >= 0) return false;
            var page = Page(node, run, rules);
            if (index < 0 || index >= page.Choices.Count) return false;
            var choice = page.Choices[index];
            if (!choice.Shown || !choice.Enabled || choice.Run == null) return false;

            session = new EventSession(node, run, rules);
            node.OptionChosen = index;
            choice.Run(session);
            return true;
        }

        // Hands the outcome of an event fight back to the event. Rewards are paid here, once, so a retreat
        // pays nothing and a re-render can never pay twice.
        public static string ResolveBattleReturn(NodeState node, RunState run, MapRules rules, bool won)
        {
            if (node == null || run == null) return "战斗结束。";
            node.EventFlag = won ? 1 : 2;
            if (!won) return "你们退了下来，这一处再没有别的东西可拿。";

            switch (node.EventId)
            {
                case "EVT-001":
                    run.Map.Supplies += 2;
                    return "矿道深处的响动终于停了。你在碎石里又挑出了一些矿石：矿石 +2。";
                case "EVT-003":
                    return "门后确实有人，只是已经不说话了。你们取下了他身上的饰品：" + NameAccessory(run);
                case "EVT-006":
                    run.Map.Supplies += 3;
                    return "休整被打断了，但敌人留下了能用的东西：矿石 +3。";
                case "EVT-H001":
                    return "岔道尽头的东西安静下来。你们得到一件饰品：" + NameAccessory(run)
                        + "　岔道后面有一部还能用的电梯井。";
                case "EVT-L001":
                    run.Map.BattleNodeBonus = true;
                    return "第五个人已经不在了，只留下一件饰品：" + NameAccessory(run)
                        + "　（占位：后续战斗节点概率增加尚未实现。）";
                default:
                    return "战斗结束。";
            }
        }

        private static string NameProsthetic(RunState run)
        {
            var item = Encounter.DropProsthetic(null, run.Random);
            if (item == null) return "什么都没有。";
            run.Prosthetics.Add(item);
            return item.Name;
        }

        private static string NameAccessory(RunState run)
        {
            var item = Encounter.DropAccessory(run.Random);
            if (item == null) return "什么都没有。";
            run.AccessoryStock.Add(item);
            return item.Name;
        }

        // ---------------- EVT-001 废弃矿道 ----------------

        private static void Mine(NodeState node, RunState run, EventPage page)
        {
            page.Step = 1; page.StepCount = 2;
            int left = Math.Max(0, node.EventLimit - node.EventCount);
            page.Body = "矿道口塌了一半，往里几步就是成片的矿脉。还能再挖 " + left + " 次。";
            page.Hint = "继续挖矿会消耗全队理智，次数用尽时会引来战斗。";
            page.Choices.Add(new EventChoice("继续挖矿", "全队理智 -3 · 矿石 +4 · 剩余 " + (left - 1) + " 次", s =>
            {
                s.TeamSanity(-3);
                s.GainSupplies(4, "矿石");
                s.Node.EventCount++;
                if (s.Node.EventCount >= s.Node.EventLimit)
                {
                    s.RequestBattle(NodeType.Battle, "最后一次挖穿了一面空腔，里面的东西醒了。");
                    s.End("次数用尽，矿道深处有东西朝这边过来。");
                }
                else s.GoTo(0, "全队理智 -3，矿石 +4。");
            }));
            page.Choices.Add(new EventChoice("离开矿道", "保留已经到手的全部矿石", s =>
                s.End("你带着已经到手的矿石退出了矿道。")));
        }

        // ---------------- EVT-002 失控的机器 ----------------

        private static void Machine(NodeState node, RunState run, EventPage page)
        {
            page.Step = 1; page.StepCount = 1;
            page.Body = "一台失控的机器还在原地抽搐，齿轮咬着一截不知道是谁的袖子。";
            page.Hint = "修理会按当前理智分成两种手法，两种都能拿到义体。";
            page.Choices.Add(new EventChoice("修理", "理智判定：专业修理或暴力拆解", s =>
            {
                if (s.HighSanity)
                {
                    s.GrantProsthetic();
                    s.End("你按顺序卸下零件，机器安静地停了。获得一件义体。");
                    return;
                }
                var victims = s.Living();
                if (victims.Count == 0) { s.End("机器停了，没有人受伤。"); return; }
                int pick = (int)(s.Run.Random.NextUnit() * victims.Count);
                if (pick >= victims.Count) pick = victims.Count - 1;
                var victim = victims[pick];
                int hurt = s.DamageHero(victim, 30);
                s.GrantProsthetic();
                s.End("你直接撬开了外壳，弹开的零件划伤了" + victim.Name + "（生命 -" + hurt + "）。获得一件义体。");
            }));
            page.Choices.Add(new EventChoice("远离", "无事发生", s => s.End("你们绕开了那台还在抽搐的机器。")));
        }

        // ---------------- EVT-003 铁门后的敲击声 ----------------

        private static void IronDoor(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                page.Step = 1; page.StepCount = 2;
                page.Body = "一扇紧闭的铁门后面，有节奏的敲击声停了一下，又继续。";
                page.Hint = "靠近之后还要再选一次。";
                page.Choices.Add(new EventChoice("靠近对话", "进入第二次选择", s => s.GoTo(1, "你走到门边，敲击声停了。")));
                page.Choices.Add(new EventChoice("远离", "无事发生", s => s.End("你们从铁门边绕了过去。")));
                return;
            }
            page.Step = 2; page.StepCount = 2;
            page.Body = "门缝里透出一点光。敲击声又响了一次。";
            page.Hint = "开门会按理智分出两种结果，交易需要付出矿石。";
            page.Choices.Add(new EventChoice("打开门", "理智判定：发现无人，或进入战斗", s =>
            {
                if (s.HighSanity)
                {
                    s.GrantAccessory();
                    s.End("门后没有人，只有一具早就没了的尸体和它身上的饰品。获得一件饰品。");
                    return;
                }
                s.RequestBattle(NodeType.Battle, "门后不是人。");
                s.End("门开的一瞬间，里面的东西扑了上来。");
            }));
            page.Choices.Add(new EventChoice("隔墙交易", "付出 2 矿石 · 换回 6 矿石", s =>
            {
                s.Run.Map.Supplies = Math.Max(0, s.Run.Map.Supplies - 2);
                s.GainSupplies(6, "矿石");
                s.End("你隔着门把矿石推过去，对面递回更大的一堆。");
            }));
        }

        // ---------------- EVT-004 破损的药剂箱 ----------------

        private static void PotionChest(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                page.Step = 1; page.StepCount = 2;
                page.Body = "药剂箱摔开了，一支蓝的，一支红的，都还没漏。";
                page.Hint = "药剂作用于选中的一名队员。";
                page.Choices.Add(new EventChoice("喝下蓝色药剂", "选中队员理智清零", s => PickPotion(s, 1)));
                page.Choices.Add(new EventChoice("喝下红色药剂", "选中队员理智拉满至 100", s => PickPotion(s, 2)));
                page.Choices.Add(new EventChoice("全部喝下", "仅低理智可用 · 30% 成功", s => PickPotion(s, 3))
                { Shown = run != null && run.PlayerSanity < SanitySplit });
                page.Choices.Add(new EventChoice("离开", "无事发生", s => s.End("你们没有碰那两只药剂。")));
                return;
            }
            ChooserPage(page, 2, 2, "谁喝下这些药剂？", "选定之后立刻结算。");
            for (int i = 0; i < run.Heroes.Count; i++)
            {
                var hero = run.Heroes[i];
                if (hero == null || hero.Dead) continue;
                var picked = hero;
                page.Choices.Add(new EventChoice(picked.Name, "生命 " + picked.Hp + "/" + picked.MaxHp
                    + " · 理智 " + picked.Sanity.ToString("0.0"), s => Drink(s, picked)));
            }
        }

        private static void PickPotion(EventSession s, int flag)
        {
            s.Node.EventFlag = flag;
            s.GoTo(1, "选择一名队员。");
        }

        private static void Drink(EventSession s, ActorState hero)
        {
            if (s.Node.EventFlag == 1)
            {
                hero.Sanity = 0;
                s.End(hero.Name + " 的理智被清空。");
                return;
            }
            if (s.Node.EventFlag == 2)
            {
                hero.Sanity = 100;
                s.End(hero.Name + " 的理智被拉到了满值。");
                return;
            }
            // 全部喝下: the user fixed this at 30% and said it takes no sanity correction. Nothing else
            // happens on a miss, and the medicine chest's permanent rewire is the only reward.
            if (s.Run.Random.NextUnit() < AllDrinkSuccess)
            {
                hero.SanitySkillsOnly = true;
                s.End("两瓶一起灌下去，喉咙里像过了电。" + hero.Name + " 永久解锁高理智技能，从此只能用高理智与低理智技能。");
            }
            else s.End("两瓶一起灌下去。" + hero.Name + " 干呕了一阵，什么也没有发生。");
        }

        // ---------------- EVT-005 应急医疗室 ----------------

        private static void Infirmary(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                ChooserPage(page, 1, 2, "应急医疗室", "先选一名队员接受治疗：恢复 40% 生命，然后按理智发现不同的东西。");
                for (int i = 0; i < run.Heroes.Count; i++)
                {
                    var hero = run.Heroes[i];
                    if (hero == null || hero.Dead) continue;
                    var picked = hero;
                    page.Choices.Add(new EventChoice(picked.Name, "生命 " + picked.Hp + "/" + picked.MaxHp, s =>
                    {
                        int healed = s.HealHero(picked, 40);
                        s.GoTo(1, picked.Name + " 恢复了 " + healed + " 点生命。");
                    }));
                }
                return;
            }
            page.Step = 2; page.StepCount = 2;
            if (run.PlayerSanity >= SanitySplit)
            {
                page.Body = "药柜后面卡着一副义体，型号不对，但零件是全的。";
                page.Hint = "义体只是取走；安装需要消耗手术包，或在补给站的「部位与义肢」里进行。";
                page.Choices.Add(new EventChoice("取走义体", "获得一件义肢", s =>
                {
                    var item = s.GrantProsthetic();
                    s.End("你把义体拆了下来。获得 " + (item == null ? "一件义肢" : item.Name) + "。");
                }));
                page.Choices.Add(new EventChoice("不动它", "什么也不拿", s => s.End("你把药柜推了回去。")));
                return;
            }
            page.Body = "柜子里有一支理智稳定剂，标签已经泡烂了。";
            page.Hint = "稳定剂的具体效果文档仍标待定，这里只按一次理智恢复处理。";
            page.Choices.Add(new EventChoice("使用稳定剂", "玩家理智 +8", s =>
            {
                double before = s.Run.PlayerSanity;
                s.Run.PlayerSanity = SanityProbability.Clamp(before + 8, 0, 100);
                s.End("玩家理智 +" + (s.Run.PlayerSanity - before).ToString("0.0") + "。");
            }));
            page.Choices.Add(new EventChoice("不动它", "什么也不拿", s => s.End("你把柜门关上了。")));
        }

        // ---------------- EVT-006 安全角落休整 ----------------

        private static void Rest(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                ChooserPage(page, 1, 2, "安全角落休整", "先选一名队员守夜：其余队员恢复 30% 生命，守夜的人不享受这次休整。");
                for (int i = 0; i < run.Heroes.Count; i++)
                {
                    var hero = run.Heroes[i];
                    if (hero == null || hero.Dead) continue;
                    var watch = hero;
                    page.Choices.Add(new EventChoice("由 " + watch.Name + " 守夜", "其余队员恢复 30% 生命", s =>
                    {
                        int healed = 0;
                        for (int h = 0; h < s.Run.Heroes.Count; h++)
                        {
                            var member = s.Run.Heroes[h];
                            if (member == null || member.Dead || member == watch) continue;
                            healed += s.HealHero(member, 30);
                        }
                        string note = "其余队员共恢复 " + healed + " 点生命。" + watch.Name + " 整夜没有合眼。";
                        if (s.HighSanity)
                        {
                            s.RequestBattle(NodeType.EliteBattle, "天快亮时，一支精英小队摸了过来。");
                            s.End(note + "敌人是精英，队伍站位没有被冲乱。");
                        }
                        else
                        {
                            s.ShuffleRanks();
                            s.RequestBattle(NodeType.EliteBattle, "大型敌人撞开了掩体，队伍被冲散。");
                            s.End(note + "敌人很大，队伍站位被打乱。");
                        }
                    }));
                }
                return;
            }
        }

        // ---------------- EVT-007 剧情事件 ----------------

        private static void Story(EventPage page)
        {
            page.Step = 1; page.StepCount = 1;
            page.Title = "剧情事件";
            page.Body = "（剧情文案待补充。此处只描述剧情，不改变任何状态。）";
            page.Hint = "阅读完成即可继续。";
            page.Choices.Add(new EventChoice("继续", "结束阅读", s => s.End("你继续向前。")));
        }

        // ---------------- EVT-008 废弃义肢工坊（祭坛） ----------------

        private static void Workshop(NodeState node, RunState run, EventPage page)
        {
            page.Step = 1; page.StepCount = 1;
            page.Body = "工坊里堆着拆下来的义肢，墙中央却立着一座不该出现在这里的黑色祭坛。"
                + "投料口亮着暗红色的灯，像一只不肯闭上的眼睛。台面上刻着一行小字：「拿来，或者从这里拿走。」";
            page.Hint = "祭坛只做等价交换：1 矿石 = 4 理智，1 生命 = 1 理智，1 件义体 = 16 理智。"
                + "理智结算给玩家或全队：若结算给队员，则每名存活队员各按报价变化一次；"
                + "生命增减始终作用于全队，资源只收或只给一份。每次只能选择其中一项交易。";

            var payResource = node.AltarResourceForSanity;
            var paySanity = node.AltarSanityForResource;
            page.Choices.Add(new EventChoice("献上资源", DescribeResourceTrade(payResource, run),
                s => TradeResourceForSanity(s, payResource))
            { Enabled = payResource != null && payResource.Valid });
            page.Choices.Add(new EventChoice("献上理智", DescribeSanityTrade(paySanity, run),
                s => TradeSanityForResource(s, paySanity))
            { Enabled = paySanity != null && paySanity.Valid });
        }

        // ---------------- EVT-008 祭坛报价 ----------------

        private static void RollAltar(NodeState node, RunState run)
        {
            node.AltarResourceForSanity = RollResourceForSanity(run);
            node.AltarSanityForResource = RollSanityForResource(run);
        }

        // Resources -> sanity. The sanity side is the player alone or the whole party. A party-wide quote
        // must fit every living hero, because each of them gains the same amount; the resource side is
        // still paid once. Health on either side is always a party-wide effect.
        private static AltarOffer RollResourceForSanity(RunState run)
        {
            var heroes = LivingHeroes(run);
            bool playerRoomy = 100 - run.PlayerSanity >= 1;
            // The quote must fit the smallest pool, since every living hero receives it in full.
            double teamRoom = 100;
            for (int i = 0; i < heroes.Count; i++) teamRoom = Math.Min(teamRoom, 100 - heroes[i].Sanity);
            bool teamRoomy = heroes.Count > 0 && teamRoom >= 1;
            if (!playerRoomy && !teamRoomy)
                return new AltarOffer { Valid = false, Refusal = "所有人的理智都已满，祭坛不收这笔交易。" };

            var offer = new AltarOffer();
            offer.PlayerSanity = playerRoomy && (!teamRoomy || run.Random.NextUnit() < 0.5);
            int headroom = (int)Math.Floor(offer.PlayerSanity ? 100 - run.PlayerSanity : teamRoom);

            var stock = run.Prosthetics == null ? new List<ProstheticSpec>()
                : run.Prosthetics.Where(p => p != null).ToList();
            var kinds = new List<AltarResource>();
            if (run.Map != null && run.Map.Supplies >= 2 && headroom >= AltarSanityPerSupply * 2)
                kinds.Add(AltarResource.Supplies);
            if (stock.Count > 0 && headroom >= AltarSanityPerProsthetic)
                kinds.Add(AltarResource.Prosthetic);
            // The whole party pays the same bite, so every living hero must be above the death door.
            if (headroom >= AltarSanityPerHealth && heroes.Count > 0 && heroes.All(h => h.Hp > 1))
                kinds.Add(AltarResource.Health);
            if (kinds.Count == 0)
                return new AltarOffer { Valid = false, Refusal = "祭坛嫌这笔交易太小，什么也没收。" };

            offer.Resource = kinds[PickIndex(kinds, run.Random)];
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                {
                    // Never charge more than the purse holds, and never buy more sanity than fits.
                    int most = Math.Min(4, Math.Min(run.Map.Supplies, headroom / AltarSanityPerSupply));
                    offer.ResourceAmount = Range(run.Random, 2, Math.Max(2, most));
                    offer.SanityAmount = offer.ResourceAmount * AltarSanityPerSupply;
                    break;
                }
                case AltarResource.Health:
                {
                    // 10..25% of the average max HP, but never past the death door of any hero and never
                    // more sanity than the smallest pool can take.
                    int maxHpSum = 0;
                    int floor = int.MaxValue;
                    for (int i = 0; i < heroes.Count; i++)
                    {
                        maxHpSum += heroes[i].MaxHp;
                        floor = Math.Min(floor, heroes[i].Hp - 1);
                    }
                    int wanted = (int)Math.Ceiling(maxHpSum / (double)heroes.Count * Range(run.Random, 10, 25) / 100.0 - 1e-9);
                    offer.ResourceAmount = Math.Max(1, Math.Min(wanted, Math.Min(floor, headroom)));
                    offer.SanityAmount = offer.ResourceAmount * AltarSanityPerHealth;
                    break;
                }
                case AltarResource.Prosthetic:
                {
                    var item = stock[PickIndex(stock, run.Random)];
                    offer.Prosthetic = LimbSystem.Clone(item);
                    offer.ResourceAmount = 1;
                    offer.SanityAmount = AltarSanityPerProsthetic;
                    break;
                }
            }
            return offer;
        }

        // Sanity -> resources. A party-wide quote must be payable by every living hero, because each of
        // them pays it; the resource side is handed over once. Health is always healed party-wide.
        private static AltarOffer RollSanityForResource(RunState run)
        {
            var heroes = LivingHeroes(run);
            // The team side is limited by its poorest pool; the player side by the player's own sanity.
            double teamSanity = heroes.Count == 0 ? 0 : double.MaxValue;
            for (int i = 0; i < heroes.Count; i++) teamSanity = Math.Min(teamSanity, heroes[i].Sanity);
            double maxSanity = Math.Max(run.PlayerSanity, teamSanity);

            var kinds = new List<AltarResource>();
            if (maxSanity >= AltarSanityPerSupply * 2) kinds.Add(AltarResource.Supplies);
            if (maxSanity >= AltarSanityPerProsthetic) kinds.Add(AltarResource.Prosthetic);
            var wounded = heroes.Where(h => h.Hp < h.MaxHp).ToList();
            if (wounded.Count > 0 && maxSanity >= AltarSanityPerHealth) kinds.Add(AltarResource.Health);
            if (kinds.Count == 0)
                return new AltarOffer { Valid = false, Refusal = "你们的理智不足以支付任何东西。" };

            var offer = new AltarOffer { Resource = kinds[PickIndex(kinds, run.Random)] };
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                {
                    int most = Math.Min(4, (int)(maxSanity / AltarSanityPerSupply));
                    offer.ResourceAmount = Range(run.Random, 2, Math.Max(2, most));
                    offer.SanityAmount = offer.ResourceAmount * AltarSanityPerSupply;
                    break;
                }
                case AltarResource.Health:
                {
                    int maxHpSum = 0;
                    int floor = int.MaxValue;
                    for (int i = 0; i < wounded.Count; i++)
                    {
                        maxHpSum += wounded[i].MaxHp;
                        floor = Math.Min(floor, wounded[i].MaxHp - wounded[i].Hp);
                    }
                    int wanted = (int)Math.Ceiling(maxHpSum / (double)wounded.Count * Range(run.Random, 10, 25) / 100.0 - 1e-9);
                    // Every wounded hero is healed by the same amount, so the smallest gap caps the quote.
                    int amount = Math.Max(1, Math.Min(wanted, floor));
                    if (amount > maxSanity) amount = Math.Max(1, (int)Math.Floor(maxSanity));
                    offer.ResourceAmount = amount;
                    offer.SanityAmount = amount * AltarSanityPerHealth;
                    break;
                }
                case AltarResource.Prosthetic:
                    offer.Prosthetic = Encounter.DropProsthetic(null, run.Random);
                    if (offer.Prosthetic == null)
                        return new AltarOffer { Valid = false, Refusal = "祭坛拿不出一件完整的义体。" };
                    offer.ResourceAmount = 1;
                    offer.SanityAmount = AltarSanityPerProsthetic;
                    break;
            }
            ChooseSanityPayer(offer, run, heroes);
            return offer;
        }

        private static void ChooseSanityPayer(AltarOffer offer, RunState run, List<ActorState> heroes)
        {
            bool playerCan = run.PlayerSanity >= offer.SanityAmount;
            bool teamCan = heroes.Count > 0 && heroes.All(h => h.Sanity >= offer.SanityAmount);
            if (playerCan && (!teamCan || run.Random.NextUnit() < 0.5))
            {
                offer.PlayerSanity = true;
                return;
            }
            if (teamCan)
            {
                offer.PlayerSanity = false;
                return;
            }
            // Defensive only: RollSanityForResource filtered by the richest side, so one side can pay.
            offer.Valid = false;
            offer.Refusal = "理智不足，祭坛收回了它的报价。";
        }

        private static string DescribeResourceTrade(AltarOffer offer, RunState run)
        {
            if (offer == null) return "祭坛没有回应。";
            if (!offer.Valid) return offer.Refusal;
            string gainer = offer.PlayerSanity ? "玩家理智" : "全队理智各";
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                    return "支付 " + offer.ResourceAmount + " 矿石 · " + gainer + " +" + offer.SanityAmount;
                case AltarResource.Health:
                    return "全队各支付 " + offer.ResourceAmount + " 生命 · " + gainer + " +" + offer.SanityAmount;
                default:
                    return "交付「" + (offer.Prosthetic == null ? "义体" : offer.Prosthetic.Name)
                        + "」 · " + gainer + " +" + offer.SanityAmount;
            }
        }

        private static string DescribeSanityTrade(AltarOffer offer, RunState run)
        {
            if (offer == null) return "祭坛没有回应。";
            if (!offer.Valid) return offer.Refusal;
            string payer = offer.PlayerSanity ? "玩家理智" : "全队理智各";
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                    return payer + " -" + offer.SanityAmount + " · 获得 " + offer.ResourceAmount + " 矿石";
                case AltarResource.Health:
                    return payer + " -" + offer.SanityAmount + " · 全队各恢复 " + offer.ResourceAmount + " 生命";
                default:
                    return payer + " -" + offer.SanityAmount + " · 获得「"
                        + (offer.Prosthetic == null ? "义体" : offer.Prosthetic.Name) + "」";
            }
        }

        private static void TradeResourceForSanity(EventSession s, AltarOffer offer)
        {
            if (s == null || offer == null || !offer.Valid) { if (s != null) s.End(offer == null ? "祭坛没有回应。" : offer.Refusal); return; }
            var run = s.Run;
            var heroes = LivingHeroes(run);
            string paid;
            int sanityPerHero;
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                {
                    int amount = Math.Min(offer.ResourceAmount, run.Map.Supplies);
                    run.Map.Supplies -= amount;
                    paid = amount + " 点矿石";
                    sanityPerHero = amount * AltarSanityPerSupply;
                    break;
                }
                case AltarResource.Health:
                {
                    // The same bite is taken from every living hero; nobody is pushed through the death door.
                    int amount = heroes.Count == 0 ? 0 : offer.ResourceAmount;
                    for (int i = 0; i < heroes.Count; i++)
                        amount = Math.Min(amount, heroes[i].Hp - 1);
                    amount = Math.Max(0, amount);
                    for (int i = 0; i < heroes.Count; i++) heroes[i].Hp -= amount;
                    paid = "全队各 " + amount + " 点生命";
                    sanityPerHero = amount * AltarSanityPerHealth;
                    break;
                }
                default:
                {
                    int index = FindProsthetic(run, offer.Prosthetic);
                    if (index >= 0) run.Prosthetics.RemoveAt(index);
                    paid = "「" + (offer.Prosthetic == null ? "义体" : offer.Prosthetic.Name) + "」";
                    sanityPerHero = index >= 0 ? AltarSanityPerProsthetic : 0;
                    break;
                }
            }
            if (sanityPerHero <= 0) { s.End("祭坛没有收下任何东西。"); return; }

            if (offer.PlayerSanity)
            {
                double before = run.PlayerSanity;
                run.PlayerSanity = SanityProbability.Clamp(before + sanityPerHero, 0, 100);
                sanityPerHero = (int)Math.Round(run.PlayerSanity - before);
                if (sanityPerHero <= 0) { s.End("祭坛收下了" + paid + "，但什么也没有回给你。"); return; }
                s.End("你把" + paid + "放上祭坛。投料口合拢了一次，玩家理智 +" + sanityPerHero + "。祭坛没有道谢。");
                return;
            }

            // The quote was fitted to the smallest pool, so every living hero normally gains it in full.
            int least = int.MaxValue;
            for (int i = 0; i < heroes.Count; i++)
            {
                double before = heroes[i].Sanity;
                heroes[i].Sanity = SanityProbability.Clamp(before + sanityPerHero, 0, 100);
                least = Math.Min(least, (int)Math.Round(heroes[i].Sanity - before));
            }
            if (heroes.Count == 0 || least <= 0) { s.End("祭坛收下了" + paid + "，但什么也没有回给你。"); return; }
            s.End("你把" + paid + "放上祭坛。投料口合拢了一次，全队理智各 +" + least + "。祭坛没有道谢。");
        }

        private static void TradeSanityForResource(EventSession s, AltarOffer offer)
        {
            if (s == null || offer == null || !offer.Valid) { if (s != null) s.End(offer == null ? "祭坛没有回应。" : offer.Refusal); return; }
            var run = s.Run;
            var heroes = LivingHeroes(run);
            int spend;
            string payer;
            if (offer.PlayerSanity)
            {
                spend = Math.Min(offer.SanityAmount, (int)Math.Floor(run.PlayerSanity));
                run.PlayerSanity = SanityProbability.Clamp(run.PlayerSanity - spend, 0, 100);
                payer = "玩家理智";
            }
            else
            {
                // The whole party pays the same quote. The roll only offered team payment when every
                // living hero could cover it; the minimum below is a defensive clamp.
                spend = offer.SanityAmount;
                for (int i = 0; i < heroes.Count; i++)
                    spend = Math.Min(spend, (int)Math.Floor(heroes[i].Sanity));
                spend = Math.Max(0, spend);
                for (int i = 0; i < heroes.Count; i++)
                    heroes[i].Sanity = SanityProbability.Clamp(heroes[i].Sanity - spend, 0, 100);
                payer = "全队理智各";
            }
            if (spend <= 0) { s.End("祭坛什么也没有取走。"); return; }

            string bought;
            switch (offer.Resource)
            {
                case AltarResource.Supplies:
                {
                    int amount = Math.Max(1, spend / AltarSanityPerSupply);
                    run.Map.Supplies += amount;
                    bought = " " + amount + " 矿石";
                    break;
                }
                case AltarResource.Health:
                {
                    // One payment heals every living hero by the same amount.
                    int amount = Math.Max(1, spend);
                    int healed = 0;
                    for (int i = 0; i < heroes.Count; i++)
                    {
                        healed += ProstheticSystem.RestoreHp(heroes[i], amount);
                    }
                    bought = "全队共 " + healed + " 点生命";
                    break;
                }
                default:
                {
                    if (offer.Prosthetic != null)
                        run.Prosthetics.Add(LimbSystem.Clone(offer.Prosthetic));
                    bought = "「" + (offer.Prosthetic == null ? "义体" : offer.Prosthetic.Name) + "」";
                    break;
                }
            }
            s.End(payer + " -" + spend + "。投料口吐出了" + bought + "，像早就等在那里。");
        }

        // ---------------- EVT-H001 隐藏的岔道（高理智） ----------------

        private static void HiddenFork(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                page.Step = 1; page.StepCount = 2;
                page.Body = "主路旁边多出一条岔道，昨天走的时候那里还是墙。";
                page.Hint = "探索会先扣全队理智，然后进入战斗。";
                page.Choices.Add(new EventChoice("无视", "全队理智 +6", s =>
                {
                    s.TeamSanity(6);
                    s.End("你记住了岔道的位置，然后继续走主路：全队理智 +6。");
                }));
                page.Choices.Add(new EventChoice("探索", "全队理智 -6 · 进入战斗", s =>
                {
                    s.TeamSanity(-6);
                    s.RequestBattle(NodeType.EliteBattle, "岔道深处的守备醒了。");
                    s.GoTo(1, "全队理智 -6。岔道深处的东西站起来了。");
                }));
                return;
            }
            page.Step = 2; page.StepCount = 2;
            if (node.EventFlag == 1)
            {
                page.Body = "岔道尽头空了。墙后有一部还在供电的电梯井，直通本层末端。";
                page.Hint = "饰品已经到手，进不进通道与饰品无关。";
                page.Choices.Add(new EventChoice("进入通道", "直接前往电梯井", s =>
                {
                    s.Node.EventJump = true;
                    s.End("你们走进了通道，电梯井的灯亮了起来。");
                }));
                page.Choices.Add(new EventChoice("不进入", "继续走主路", s => s.End("你们记下了通道的位置，继续走主路。")));
                return;
            }
            page.Body = "守备把你们逼回了岔道口，通道口在它身后，够不着。";
            page.Hint = "撤退没有得到饰品，通道也用不上。";
            page.Choices.Add(new EventChoice("离开", "回到主路", s => s.End("你们退回了岔道口。")));
        }

        // ---------------- EVT-L001 第五个人（低理智） ----------------

        private static void FifthPerson(NodeState node, RunState run, EventPage page)
        {
            if (node.EventStep == 0)
            {
                page.Step = 1; page.StepCount = 2;
                page.Body = "队伍里多了一个人。没有人记得他是谁，也没有人记得他是什么时候跟上的。";
                page.Hint = "两个选项都会先改变全队理智。";
                page.Choices.Add(new EventChoice("无视", "全队理智 -6", s =>
                {
                    s.TeamSanity(-6);
                    s.Run.Map.NextBattleBonusActions = true;
                    s.End("你决定不去看他的脸。全队理智 -6。（占位：下次战斗每回合行动数 +1 尚未实现。）");
                }));
                page.Choices.Add(new EventChoice("查清状况", "全队理智 +6 · 进入战斗", s =>
                {
                    s.TeamSanity(6);
                    s.RequestBattle(NodeType.Battle, "你转过身，那个人的脸不属于队伍里的任何一个人。");
                    s.GoTo(1, "全队理智 +6。你问出了声，那个人的动作停住了。");
                }));
                return;
            }
            page.Step = 2; page.StepCount = 2;
            if (node.EventFlag == 1)
            {
                page.Body = "那个人已经不在了，地上留着一件不属于你们的东西。";
                page.Hint = "（占位：后续战斗节点出现概率增加尚未实现。）";
                page.Choices.Add(new EventChoice("继续前进", "离开这一处", s => s.End("队伍重新变回四个人，继续向前。")));
                return;
            }
            page.Body = "它退进了黑暗里，还跟着。";
            page.Choices.Add(new EventChoice("继续前进", "离开这一处", s => s.End("你们没能弄清那是什么，继续向前。")));
        }

        // ---------------- 共用 ----------------

        // A step whose options are the living members. Only the heading differs between the three events
        // that use one, so the shape lives here.
        private static void ChooserPage(EventPage page, int step, int stepCount, string body, string hint)
        {
            page.Step = step; page.StepCount = stepCount;
            page.Body = body;
            page.Hint = hint;
        }

        private static int Pick(IList<string> pool, RunRandom random)
        {
            if (pool.Count <= 1) return 0;
            int index = (int)(random.NextUnit() * pool.Count);
            return index >= pool.Count ? pool.Count - 1 : index;
        }

        private static int PickIndex<T>(IList<T> pool, RunRandom random)
        {
            if (pool == null || pool.Count == 0) return -1;
            if (pool.Count <= 1) return 0;
            int index = (int)(random.NextUnit() * pool.Count);
            return index >= pool.Count ? pool.Count - 1 : index;
        }

        // Inclusive on both ends; the altar uses it for small integer ranges.
        private static int Range(RunRandom random, int min, int max)
        {
            int value = min + (int)(random.NextUnit() * (max - min + 1));
            return value > max ? max : value;
        }

        private static List<ActorState> LivingHeroes(RunState run)
        {
            var list = new List<ActorState>();
            if (run == null || run.Heroes == null) return list;
            for (int i = 0; i < run.Heroes.Count; i++)
                if (run.Heroes[i] != null && !run.Heroes[i].Dead) list.Add(run.Heroes[i]);
            return list;
        }

        private static int FindProsthetic(RunState run, ProstheticSpec item)
        {
            if (run == null || run.Prosthetics == null || item == null) return -1;
            for (int i = 0; i < run.Prosthetics.Count; i++)
                if (run.Prosthetics[i] != null && run.Prosthetics[i].Id == item.Id) return i;
            return -1;
        }

        // Prefers content the run has not shown yet. Once every eligible script has appeared, the one
        // whose last appearance is furthest back is picked, so repeats are spread as widely as possible.
        private static string Select(List<string> pool, RunState run)
        {
            var history = run.Map.EventHistory;
            var unseen = new List<string>();
            for (int i = 0; i < pool.Count; i++)
                if (LastSeen(history, pool[i]) < 0) unseen.Add(pool[i]);
            if (unseen.Count > 0) return unseen[Pick(unseen, run.Random)];

            string oldest = pool[0];
            int oldestAt = LastSeen(history, oldest);
            for (int i = 1; i < pool.Count; i++)
            {
                int at = LastSeen(history, pool[i]);
                if (at < oldestAt) { oldest = pool[i]; oldestAt = at; }
            }
            return oldest;
        }

        private static int LastSeen(IList<string> history, string id)
        {
            if (history == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i] == id) return i;
            return -1;
        }
    }
}
