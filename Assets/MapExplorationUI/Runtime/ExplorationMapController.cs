using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameMake.ExplorationUI
{
    [Serializable] public sealed class ExplorationPartySlot
    {
        public Button Hit;
        public Image Portrait, Health, Sanity, Selection;
        public SilhouetteGraphic Placeholder;
        public Text Name, Vitals;
    }
    [Serializable] public sealed class ExplorationRouteSlot
    {
        public RectTransform Root;
        public Image Diamond;
        public Text Number, Name;
    }
    [Serializable] public sealed class ExplorationSelectionEvent : UnityEvent<int> { }

    public sealed class ExplorationMapController : MonoBehaviour
    {
        public StageOneApp Source;
        public GameObject Page;
        public ExplorationCounter Counter;
        public Text Heading, Progress, ChoiceHeading, Note, PartyDetail, Stock;
        public RectTransform Choices;
        public CanvasGroup ChoiceFade;
        public ExplorationRoomOption[] Options;
        public ExplorationPartySlot[] Party;
        public ExplorationRouteSlot[] RouteSlots;
        public ExplorationRouteGraphic Connections;
        public ExplorationSelectionEvent OnCharacterSelected = new ExplorationSelectionEvent();
        public int SelectedCharacter;
        public MapPageEntrance Entrance;
        bool wasVisible;
        StageOneApp bound;
        bool hooked;
        StepState shownStep;
        Coroutine popup;
        readonly Color cream = new Color32(224, 207, 176, 255);
        readonly Color green = new Color32(104, 192, 142, 255);
        readonly Color dim = new Color32(64, 88, 72, 255);

        void OnEnable() { Bind(); Sync(); }
        void Start() { Bind(); Sync(); }
        void OnDisable()
        {
            wasVisible=false;
            if(Entrance) Entrance.Finish();
            if (bound) bound.Changed -= Sync;
            bound = null;
            if (popup != null) StopCoroutine(popup);
            popup = null;
            if (ChoiceFade) ChoiceFade.alpha = 1;
            if (Choices) Choices.anchoredPosition = new Vector2(60, -790);
        }
        void Bind()
        {
            if (!Source) Source = FindObjectOfType<StageOneApp>();
            if (Source != bound)
            {
                if (bound) bound.Changed -= Sync;
                bound = Source;
                if (bound) bound.Changed += Sync;
            }
            if (hooked) return;
            hooked = true;
            for (int i = 0; i < Options.Length; i++)
            {
                var option = Options[i];
                option.Hit.onClick.AddListener(() => Choose(option.CandidateIndex));
            }
            for (int i = 0; i < Party.Length; i++)
            {
                int index = i;
                Party[i].Hit.onClick.AddListener(() => SelectCharacter(index));
            }
        }
        public void SelectCharacter(int index)
        {
            if (index < 0 || index >= Party.Length) return;
            SelectedCharacter = index; UpdateParty(); OnCharacterSelected.Invoke(index);
            if (Source) Source.OpenCharacterPanel(index);
        }
        void Choose(int index)
        {
            if(Entrance && Entrance.IsRunning) return;
            if (!Source || Source.Run == null || !Page.activeSelf || Source.Busy || StageOneApp.DebugOverlayOpen) return;
            Source.SelectNode(index);
        }
        public void Sync()
        {
            if (!Source || Source.Run == null) return;
            bool visible = Source.AwaitingNode && Source.Room == RoomView.Map && !Source.Map.Finished
                && Source.Map.Current != null;
            bool reopening = (!wasVisible || !Page.activeSelf) && visible;
            wasVisible=visible;
            Page.SetActive(visible);
            if (!visible)
            {
                if(Entrance) Entrance.Finish();
                if(popup!=null) StopCoroutine(popup); popup=null;
                return;
            }
            if(reopening && Entrance && Application.isPlaying) Entrance.Enter();
            RenderMap(Source.Map, Source.Catalog.MapRules(), reopening);
            Stock.text = "矿石  " + Source.Map.Supplies.ToString("D2") + "     /     玩家理智  " + Source.Run.PlayerSanity.ToString("0");
            UpdateParty();
        }
        public void RenderMap(MapState map, MapRules rules, bool reopening = false)
        {
            var step = map.Current;
            if (step == null) { foreach (var o in Options) o.gameObject.SetActive(false); return; }
            Counter.SetLocation(step.Floor, step.Index + 1);
            int total = MapGenerator.StepsOnFloor(rules, step.Floor);
            Heading.text = "第 " + step.Floor.ToString("D2") + " 层  /  探索路线";
            Progress.text = "房间 " + (step.Index + 1).ToString("D2") + " / " + total.ToString("D2") + "     ·     已通过 " + map.ResolvedSteps + " 间";
            int count = step.Resolved ? 0 : step.Candidates.Count;
            ChoiceHeading.text = count == 0 ? "暂无可选房间" : "前方房间  /  " + count.ToString("D2") + " 条可选路线";
            Note.text = "选择一间房继续探索 · 未到达的房间保持未知";
            // Current rules offer 1..3. Grow the editable slot pool if a future rule adds more.
            while (Options.Length < count)
            {
                var clone = Instantiate(Options[0], Choices);
                clone.name = "Room Option " + (Options.Length + 1);
                clone.Hit.onClick.RemoveAllListeners();
                clone.Hit.onClick.AddListener(() => Choose(clone.CandidateIndex));
                var list = Options.ToList(); list.Add(clone); Options = list.ToArray();
            }
            for (int i = 0; i < Options.Length; i++)
            {
                var option = Options[i]; option.gameObject.SetActive(i < count);
                if (i >= count) continue;
                option.CandidateIndex = i;
                var node = step.Candidates[i];
                float width = Mathf.Min(408, (1304 - 16 * (count-1)) / count);
                var rect = (RectTransform)option.transform;
                rect.anchoredPosition = new Vector2(i * (width+16), 0); rect.sizeDelta = new Vector2(width, 166);
                option.Number.text = (i+1).ToString("D2"); option.Title.text = node.Name;
                option.Description.text = Description(node.Type);
                option.Action.text = "进入房间  →";
                option.Accent.color = MapGenerator.IsCombat(node.Type) ? new Color32(200, 91, 72, 255) : green;
                option.Hit.interactable = !step.Resolved && (Source == null || (!Source.Busy && !StageOneApp.DebugOverlayOpen));
            }
            DrawRoute(map, total);
            if (step != shownStep || reopening)
            {
                shownStep = step;
                if (popup != null) StopCoroutine(popup);
                if (Application.isPlaying && isActiveAndEnabled && Page.activeInHierarchy) popup = StartCoroutine(Popup());
                else { ChoiceFade.alpha = 1; Choices.anchoredPosition = new Vector2(60, -790); }
            }
        }
        IEnumerator Popup()
        {
            ChoiceFade.alpha=0;
            if(Entrance && Entrance.IsRunning) yield return new WaitForSecondsRealtime(.28f);
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < .24f)
            {
                float t = Time.realtimeSinceStartup - started;
                float a = 1 - Mathf.Pow(1 - t / .24f, 3);
                ChoiceFade.alpha = a; Choices.anchoredPosition = new Vector2(60, -790 - (1-a)*20);
                yield return null;
            }
            ChoiceFade.alpha = 1; Choices.anchoredPosition = new Vector2(60, -790); popup = null;
        }
        void DrawRoute(MapState map, int total)
        {
            var current = map.Current;
            var starts = new List<Vector2>(); var ends = new List<Vector2>(); var colors = new List<Color>();
            int used = 0;
            // All nine positions fit the current rules. A longer future floor uses a sliding nine-room window.
            int first = Mathf.Clamp(current.Index - 4, 0, Mathf.Max(0, total-9));
            int last = Mathf.Min(total, first+9);
            float spacing = 1180f / Mathf.Max(1, last-first-1);
            if (last-first == 1) spacing = 0;
            var previous = new List<Vector2>();
            for (int index = first; index < last; index++)
            {
                float x = 120 + (index-first)*spacing;
                var known = map.OnFloor(current.Floor).FirstOrDefault(s => s.Index == index);
                bool active = index == current.Index;
                int branches = active ? Mathf.Max(1, current.Candidates.Count) : 1;
                var column = new List<Vector2>();
                for (int branch = 0; branch < branches && used < RouteSlots.Length; branch++)
                {
                    float y = 395 + (branch - (branches-1)*.5f)*136;
                    var slot = RouteSlots[used++]; slot.Root.gameObject.SetActive(true);
                    slot.Root.anchoredPosition = new Vector2(x-60, -y+23);
                    slot.Number.text = active ? (branch+1).ToString("D2") : (index+1).ToString("D2");
                    slot.Name.text = active && branch < current.Candidates.Count ? current.Candidates[branch].Name
                        : known != null && known.Resolved && known.Chosen != null ? known.Chosen.Name : "未探索";
                    slot.Diamond.color = active ? cream : known != null && known.Resolved ? green : dim;
                    slot.Name.color = slot.Number.color = active ? cream : known != null && known.Resolved ? green : dim;
                    column.Add(new Vector2(x, y));
                    foreach (var from in previous)
                    {
                        starts.Add(from + new Vector2(24, 0)); ends.Add(new Vector2(x-24, y)); colors.Add(index <= current.Index ? green : dim);
                    }
                }
                previous = column;
            }
            for (int i = used; i < RouteSlots.Length; i++) RouteSlots[i].Root.gameObject.SetActive(false);
            Connections.Starts = starts.ToArray(); Connections.Ends = ends.ToArray(); Connections.Tints = colors.ToArray(); Connections.SetVerticesDirty();
        }
        void UpdateParty()
        {
            for (int i = 0; i < Party.Length; i++)
            {
                var slot = Party[i]; slot.Selection.enabled = SelectedCharacter == i;
                if (!Source || Source.Run == null || i >= Source.Run.Heroes.Count) continue;
                var actor = Source.Run.Heroes[i];
                slot.Name.text = actor.Name;
                slot.Health.fillAmount = Mathf.Clamp01((float)actor.Hp / Mathf.Max(1, actor.MaxHp));
                slot.Sanity.fillAmount = Mathf.Clamp01((float)actor.Sanity / 100);
                slot.Vitals.text = "生命 " + actor.Hp + "/" + actor.MaxHp + "   理智 " + actor.Sanity.ToString("0");
                slot.Placeholder.Silhouette = actor.Definition.Silhouette;
                // Same as the battle party column: draw the authored portrait through the silhouette
                // graphic, and leave the separate portrait Image available for hand-assigned art.
                var portrait = ActorArt.Portrait(actor.Definition);
                slot.Placeholder.SetPortrait(portrait);
                slot.Placeholder.enabled = !slot.Portrait.sprite;
                slot.Portrait.enabled = slot.Portrait.sprite;
                slot.Placeholder.color = portrait
                    ? actor.Dead ? new Color32(120,120,120,255) : Color.white
                    : actor.Dead ? new Color32(55,55,55,255) : cream;
                if (SelectedCharacter == i) PartyDetail.text = actor.Name + "  ·  " + (actor.Dead ? "已阵亡" : "探索队员");
            }
        }
        public static string Description(NodeType type)
        {
            switch (type)
            {
                case NodeType.Battle: return "遭遇敌人，战胜后取得矿石。";
                case NodeType.EliteBattle: return "强敌盘踞，危险与收益并存。";
                case NodeType.Event: return "调查异象，决定下一步行动。";
                case NodeType.Altar: return "接近祭坛，回应未知的呼唤。";
                case NodeType.Treasure: return "搜寻遗留物资，开启宝箱。";
                case NodeType.Opening: return "选择一项全队探索增益。";
                case NodeType.Supply: return "补充状态，为下一段路做准备。";
                case NodeType.Elevator: return "乘坐升降机，前往下一层。";
                case NodeType.Boss: return "最终的守卫就在前方。";
                default: return "前往该房间继续探索。";
            }
        }
    }
}
