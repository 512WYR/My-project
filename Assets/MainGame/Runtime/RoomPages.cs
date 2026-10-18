using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;

namespace Ruin.Runtime
{
    // The event page is the only non-battle page left in the new UI, so every room is written in the same
    // shape: a title, a story, a list of choices and one advance action. This context is view state only —
    // it lets the supply room walk main menu -> hero -> limb without teaching StageOneApp about subpages.
    public sealed class RoomPageContext
    {
        public int Section;
        public int Hero = -1;
        public void Reset() { Section = 0; Hero = -1; }
    }

    public sealed class RoomPageChoice
    {
        public string Label;
        public string Detail;
        public bool Shown = true;
        public bool Enabled = true;
        public RoomPageChoice(string label, string detail, bool enabled = true)
        { Label = label; Detail = detail; Enabled = enabled; }
    }

    // What the event terminal draws for the current room. Choose and Advance are the only two ways back
    // into the game state, so the page itself never has to know which room it is showing.
    public sealed class RoomPageView
    {
        public string Header, Title, Code, Location, Body, Hint, AdvanceLabel;
        public bool CanLeave;
        // Settlement is a result screen rather than a record to read, so it skips the typewriter.
        public bool Instant;
        public readonly List<RoomPageChoice> Choices = new List<RoomPageChoice>();
        public Action<int> Choose;
        public Action Advance;
    }

    public static class RoomPages
    {
        static readonly LimbPart[] PartOrder = { LimbPart.Head, LimbPart.Torso, LimbPart.Arm, LimbPart.Leg };

        // The map page owns the route; every other node, plus the finished run, is drawn as an event page.
        public static bool Visible(StageOneApp app)
        {
            if (!app || app.Run == null || app.Map == null) return false;
            // A settled battle is a result page in its own right; the route only takes over once
            // ContinueBattle resolves the position.
            if (app.Battle != null && app.Battle.Outcome != BattleOutcome.Active) return true;
            if (!app.AwaitingNode) return false;
            return app.Room != RoomView.Map || app.Map.Finished;
        }

        public static RoomPageView Build(StageOneApp app, RoomPageContext context)
        {
            if (app.Battle != null && app.Battle.Outcome != BattleOutcome.Active) return Settlement(app);
            if (app.Map.Finished) return RunEnd(app);
            switch (app.Room)
            {
                case RoomView.Buff: return Buff(app);
                case RoomView.Supply: return Supply(app, context);
                case RoomView.Elevator: return Elevator(app);
                case RoomView.Treasure: return Treasure(app);
                case RoomView.Event: return Event(app);
                default: return RunEnd(app);
            }
        }

        // ---- opening buff ----

        static RoomPageView Buff(StageOneApp app)
        {
            var view = Base(app, "增益终端", "初始增益", "OPENING / 全队增益");
            view.Body = "出发之前，队伍只来得及带走一样东西。\n"
                + "增益直接改写队员的基础属性，战斗引擎读的就是这些数值，所以它整局有效；"
                + "选定之后本次冒险不再更改。";

            var buffs = app.Catalog.MapRules().OpeningBuffs;
            bool taken = app.Map.BuffChosen;
            for (int i = 0; i < buffs.Length; i++)
            {
                var buff = buffs[i];
                bool mine = taken && app.Map.Buff == buff;
                view.Choices.Add(new RoomPageChoice(
                    OpeningBuffs.Name(buff) + (mine ? " · 已选择" : ""),
                    OpeningBuffs.Describe(buff),
                    !taken));
            }
            view.Choose = index =>
            {
                if (index >= 0 && index < buffs.Length) app.ChooseBuff(buffs[index]);
            };
            view.CanLeave = taken;
            view.AdvanceLabel = "离开  →";
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        // ---- supply: main menu -> hero -> limb ----

        static RoomPageView Supply(StageOneApp app, RoomPageContext context)
        {
            switch (context.Section)
            {
                case 1: return SupplyHeroes(app, context);
                case 2: return SupplyLimbs(app, context);
                default: return SupplyMain(app, context);
            }
        }

        static RoomPageView SupplyMain(StageOneApp app, RoomPageContext context)
        {
            var view = Base(app, "补给终端", "补给站", "SUPPLY / 补给站");
            string ratio = (app.Catalog.MapRules().SupplyHealRatio * 100).ToString("0");
            view.Body = "治疗为每名队员恢复 " + ratio + "% 生命，阵亡成员不会复活。\n"
                + "静养为玩家理智 +" + Exploration.CalmSanity.ToString("0.#") + "。\n"
                + "重伤部位可以修复，断肢需要安装对应部位的义肢。";
            view.Hint = "治疗 " + (app.Map.SupplyHealed ? "已使用" : "可用")
                + "   ·   静养 " + (app.Map.SupplyCalmed ? "已使用" : "可用")
                + "\n" + StockText(app);

            view.Choices.Add(new RoomPageChoice(
                app.Map.SupplyHealed ? "治疗已使用" : "治疗全队",
                "每名队员恢复 " + ratio + "% 生命",
                !app.Map.SupplyHealed));
            view.Choices.Add(new RoomPageChoice(
                app.Map.SupplyCalmed ? "静养已使用" : "静养",
                "玩家理智 +" + Exploration.CalmSanity.ToString("0.#"),
                !app.Map.SupplyCalmed));
            view.Choices.Add(new RoomPageChoice(
                "部位与义肢",
                "查看每名队员的四个部位，修复重伤或安装义肢"));
            view.Choices.Add(new RoomPageChoice("商店", "使用补给购买义体、饰品与药品"));

            view.Choose = index =>
            {
                switch (index)
                {
                    case 0: app.SupplyHeal(); break;
                    case 1: app.SupplyCalm(); break;
                    case 2: context.Reset(); context.Section = 1; break;
                    case 3: app.OpenSupplyShop(); break;
                }
            };
            view.CanLeave = true;
            view.AdvanceLabel = "离开补给站  →";
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        static RoomPageView SupplyHeroes(StageOneApp app, RoomPageContext context)
        {
            var view = Base(app, "补给终端", "部位与义肢", "SUPPLY / 选择队员");
            view.Body = "选择一名队员，处理他的四个部位。\n"
                + "只有重伤部位能在这里修复；断肢只能安装对应部位的义肢。";
            view.Hint = StockText(app);

            for (int i = 0; i < app.Run.Heroes.Count; i++)
            {
                var hero = app.Run.Heroes[i];
                string limbs = LimbSystem.Summary(hero);
                view.Choices.Add(new RoomPageChoice(
                    hero.Name,
                    hero.Dead ? "已死亡" : limbs.Length == 0 ? "部位全部完好" : "最严重 " + limbs,
                    !hero.Dead));
            }
            int back = view.Choices.Count;
            view.Choices.Add(new RoomPageChoice("返回", "回到补给站操作"));

            view.Choose = index =>
            {
                if (index == back) { context.Section = 0; context.Hero = -1; return; }
                if (index < 0 || index >= app.Run.Heroes.Count) return;
                context.Section = 2; context.Hero = index;
            };
            view.CanLeave = true;
            view.AdvanceLabel = "离开补给站  →";
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        static RoomPageView SupplyLimbs(StageOneApp app, RoomPageContext context)
        {
            int heroIndex = context.Hero;
            if (heroIndex < 0 || heroIndex >= app.Run.Heroes.Count) { context.Section = 1; return SupplyHeroes(app, context); }
            var hero = app.Run.Heroes[heroIndex];

            var view = Base(app, "补给终端", hero.Name + " · 部位", "SUPPLY / 部位与义肢");
            view.Body = "重伤部位可以修复；断肢需要对应部位的义肢。\n"
                + "修复与安装都会立刻生效，返回后可以继续处理其他队员。";
            view.Hint = "生命 " + hero.Hp + " / " + hero.Definition.MaxHp + "\n" + StockText(app);

            for (int i = 0; i < PartOrder.Length; i++)
            {
                var part = PartOrder[i];
                var limb = LimbSystem.Get(hero, part);
                var condition = LimbSystem.ConditionOf(limb);
                bool fitted = limb != null && limb.ProstheticFitted;
                string name = LimbSystem.PartName(part);

                if (hero.Dead || limb == null)
                {
                    view.Choices.Add(new RoomPageChoice(name, hero.Dead ? "已死亡" : "无法处理", false));
                    continue;
                }
                if (fitted)
                {
                    view.Choices.Add(new RoomPageChoice(name + " · 义肢", "已经安装，功能正常", false));
                    continue;
                }
                if (condition == LimbCondition.Injured)
                {
                    view.Choices.Add(new RoomPageChoice(
                        name + " · 修复", "重伤 " + limb.Hp + "/" + limb.MaxHp + "，恢复到 " + limb.MaxHp));
                    continue;
                }
                if (condition == LimbCondition.Severed)
                {
                    var item = app.Run.Prosthetics.FirstOrDefault(p => p != null && p.Part == part);
                    view.Choices.Add(item == null
                        ? new RoomPageChoice(name + " · 断肢", "缺少该部位的义肢", false)
                        : new RoomPageChoice(name + " · 安装 " + item.Name, item.Description));
                    continue;
                }
                view.Choices.Add(new RoomPageChoice(name + " · 完好", limb.Hp + "/" + limb.MaxHp, false));
            }
            int back = view.Choices.Count;
            view.Choices.Add(new RoomPageChoice("返回", "回到队员选择"));

            view.Choose = index =>
            {
                if (index == back) { context.Section = 1; return; }
                if (index < 0 || index >= PartOrder.Length) return;
                var part = PartOrder[index];
                if (LimbSystem.ConditionOf(hero, part) == LimbCondition.Injured)
                {
                    app.RepairLimb(hero.Id, part);
                    return;
                }
                var item = app.Run.Prosthetics.FirstOrDefault(p => p != null && p.Part == part);
                if (item != null) app.InstallProsthetic(hero.Id, item.Id);
            };
            view.CanLeave = true;
            view.AdvanceLabel = "离开补给站  →";
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        // ---- elevator ----

        static RoomPageView Elevator(StageOneApp app)
        {
            var view = Base(app, "层级终端", "电梯井", "ELEVATOR / 第 " + app.Map.Floor + " 层");
            view.Body = "第 " + app.Map.Floor + " 层到此为止。\n"
                + "下行之后不会自动整备生命，个人理智保留。\n\n"
                + string.Join("\n", app.Run.Heroes.Select(h =>
                {
                    string limbs = LimbSystem.Summary(h);
                    return h.Name + (h.Dead ? "   阵亡" : "   生命 " + h.Hp + " / " + h.Definition.MaxHp)
                        + "   " + (limbs.Length == 0 ? "部位全部完好" : "最严重 " + limbs);
                }));
            view.Hint = StockText(app) + "\n补给 " + app.Map.Supplies
                + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0");
            view.CanLeave = true;
            view.AdvanceLabel = "下到第 " + (app.Map.Floor + 1) + " 层  →";
            view.Advance = () => app.AdvanceFloor();
            return view;
        }

        // ---- treasure ----

        static RoomPageView Treasure(StageOneApp app)
        {
            var view = Base(app, "宝箱终端", "宝箱房", "TREASURE / 宝箱");
            view.Body = "宝箱里装着的是饰品。每个宝箱房只发一次，重新打开页面不会重复抽取。\n"
                + "已经拿到的饰品会在离开后进入队伍仓库。";
            view.Hint = StockText(app);
            view.CanLeave = true;
            view.AdvanceLabel = "离开宝箱房  →";
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        // ---- events and altars, unchanged ----

        static RoomPageView Event(StageOneApp app)
        {
            var node = app.ActiveNode;
            var page = EventScripts.Page(node, app.Run, app.Catalog.MapRules());
            var view = Base(app, "事件终端", page.Title ?? "", node?.EventId ?? "EVENT");
            view.Code = (node?.EventId ?? "EVENT") + "  /  "
                + (page.CanLeave ? "记录结束" : page.StepCount > 0 ? "阶段 " + page.Step + " / " + page.StepCount : "事件记录");
            view.Body = page.Body ?? "";
            view.Hint = page.Hint ?? "";
            view.CanLeave = page.CanLeave && app.EventCanLeave;
            view.AdvanceLabel = "离开  →";
            foreach (var choice in page.Choices)
                view.Choices.Add(new RoomPageChoice(choice.Label, choice.Detail, choice.Enabled) { Shown = choice.Shown });
            view.Choose = index => app.ChooseEventOption(index);
            view.Advance = () => app.LeaveRoom();
            return view;
        }

        // ---- battle settlement ----

        static RoomPageView Settlement(StageOneApp app)
        {
            var battle = app.Battle;
            var result = battle.Settlement;
            string heading = result.Outcome == BattleOutcome.Victory ? "战斗胜利"
                : result.Outcome == BattleOutcome.Retreat ? "队伍已撤离" : "本次冒险结束";
            var view = Base(app, "战斗终端", heading, "SETTLEMENT / 战斗结算");
            view.Location = "第 " + (app.ActiveNode?.Floor ?? app.Map.Floor).ToString("D2") + " 层";
            var lines = new List<string>();
            foreach (var entry in result.Entries)
                lines.Add(entry.Name + (entry.Dead ? "  ·  死亡" : "")
                    + "    入战 " + entry.Start.ToString("0.0")
                    + "  →  结束 " + entry.End.ToString("0.0")
                    + "    （" + entry.Delta.ToString("+0.0;-0.0;0.0") + "）");
            double average = result.Entries.Count == 0 ? 0 : result.Entries.Average(e => e.Delta);
            bool wiped = app.Run.Heroes.All(a => a.Dead);
            bool boss = app.ActiveNode != null && app.ActiveNode.Type == NodeType.Boss;
            bool won = result.Outcome == BattleOutcome.Victory;
            view.Body = "战斗中的经历，已经成为下一次出发的起点。\n\n"
                + string.Join("\n", lines)
                + "\n\n平均净变化 " + average.ToString("+0.0;-0.0;0.0")
                + "  ×  " + battle.Rules.FeedbackFactor.ToString("0.0")
                + "  =  " + result.RawDelta.ToString("+0.0;-0.0;0.0")
                + "\n玩家理智    " + result.Before.ToString("0.0")
                + "  →  " + result.After.ToString("0.0")
                + "\n\n" + (boss
                    ? "首领是这条路的尽头：胜利即通关，全队阵亡则本局结束。"
                    : won
                        ? "胜利的补给已经入账。治疗与义肢只在补给站提供，路线上不会自动整备。"
                        : "战斗失利，没有收获。治疗与义肢只在补给站提供，路线上不会自动整备。");
            view.Hint = "查看结果后继续。";
            view.CanLeave = false;
            view.Instant = true;
            view.Choices.Add(new RoomPageChoice(
                wiped || (boss && !won) ? "结束本局" : boss ? "通关结算" : "继续前进",
                wiped ? "查看本局结果" : boss ? "查看本局结果" : "选择下一个位置"));
            view.Choices.Add(new RoomPageChoice("新开演练", "重置全部队员"));
            view.Choose = index =>
            {
                if (index == 0) app.ContinueBattle();
                else if (index == 1) app.NewRun();
            };
            return view;
        }

        // ---- run end ----

        static RoomPageView RunEnd(StageOneApp app)
        {
            var view = Base(app, "记录终端",
                app.Map.Cleared ? "本局通关" : "本局结束",
                "RUN END  /  " + (app.Map.Cleared ? "通关" : "结束"));
            view.Code = "RUN END  /  " + (app.Map.Cleared ? "通关" : "结束");
            view.Location = "抵达第 " + app.Map.Floor.ToString("D2") + " 层  ·  通过 " + app.Map.ResolvedSteps + " 个位置";
            view.Body = (app.RunEndNote ?? "冒险已经结束。") + "\n\n"
                + "到达第 " + app.Map.Floor + " 层   ·   通过 " + app.Map.ResolvedSteps + " 个位置"
                + "   ·   玩家理智 " + app.Run.PlayerSanity.ToString("0.0") + "\n"
                + string.Join("   ·   ", app.Run.Heroes.Select(h =>
                    h.Name + (h.Dead ? " 阵亡" : " " + h.Hp + "/" + h.Definition.MaxHp)));
            view.Hint = StockText(app);
            view.CanLeave = true;
            view.AdvanceLabel = "新开一局  →";
            view.Advance = () => app.NewRun();
            return view;
        }

        // ---- shared furniture ----

        static RoomPageView Base(StageOneApp app, string header, string title, string code)
        {
            var node = app.ActiveNode;
            return new RoomPageView
            {
                Header = header,
                Title = title,
                Code = code,
                Location = "第 " + (node?.Floor ?? app.Map.Floor).ToString("D2")
                    + " 层  ·  房间 " + (((node?.Index) ?? 0) + 1).ToString("D2"),
                AdvanceLabel = "离开  →",
                CanLeave = true,
                Advance = () => app.LeaveRoom(),
            };
        }

        static string StockText(StageOneApp app)
        {
            string accessories = "持有饰品 " + app.Run.AccessoryStock.Count + " 件";
            string consumables = "持有道具 " + app.Run.Consumables.Count + " 件";
            var items = app.Run.Prosthetics;
            if (items.Count == 0) return "持有义肢：无   ·   " + accessories + "   ·   " + consumables;
            var groups = items.GroupBy(p => p.Id).Select(g => g.First().Name + (g.Count() > 1 ? " ×" + g.Count() : ""));
            return "持有义肢：" + string.Join("   ·   ", groups) + "   ·   " + accessories + "   ·   " + consumables;
        }
    }
}
