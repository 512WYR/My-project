using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using GameMake.IndustrialDebugUI;
#endif
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// Drives the authored battle page from a live StageOne run. It only ever writes into objects that already
    /// exist: the battle refreshes several times per second and the scene hierarchy has to survive that.
    /// Health, sanity, limbs and the party portraits are handed to ModularHudController, which already owns
    /// those widgets and is covered by ModularHudVerification. Turn its FollowStageOneBattle off in the scene
    /// so this component is the single writer.
    /// The page itself carries positions, vitals and skills; the readouts and commands that have no place on
    /// the layout yet sit behind one button in the debug sheet.
    /// </summary>
    public sealed class GothicBattleHudController : MonoBehaviour
    {
        [Header("Battle / 战斗来源")]
        public StageOneApp BattleSource;
        [Header("Existing HUD modules / 复用现有模块")]
        public GothicHudPanel Panel;
        public ModularHudController Presentation;
        public MechanicalRoundCounter Counter;
        [Tooltip("Everything that only belongs to the battle page is switched off outside a battle.")]
        public GameObject[] ChromeRoots = new GameObject[0];

        [Header("Positions / 位置")]
        public GothicBattleCard[] Allies = new GothicBattleCard[4];
        public GothicBattleCard[] Enemies = new GothicBattleCard[4];

        [Header("Quick items / 快捷物品")]
        [Tooltip("The four authored Item slots. Resolved at runtime when left empty.")]
        public Button[] QuickItems = new Button[4];

        [Header("Debug sheet / 调试页")]
        [Tooltip("Readouts and commands that have no place on the authored layout yet. Off by default.")]
        public GameObject DebugRoot;
        public Button DebugButton;
        public Button DebugCloseButton;
        public Text PlayerSanityText;
        public Text FloorText;
        public Text TurnText;
        public Text HintText;
        public Text DebugIntentText;

        private StageOneApp bound;
        private bool hooked;
        private GameObject quickItemRoot;
        // Owned here now that the code-generated page is gone: the app asks for a flash and the cards
        // answer with their own background images.
        private readonly Dictionary<string, Image> cardImages = new Dictionary<string, Image>();

        private void OnEnable() { Bind(); Sync(); }
        private void Start() { Bind(); Sync(); }
        private void OnDisable()
        {
            if (bound)
            {
                bound.Changed -= Sync;
                bound.BattleEventAnimation = null;
            }
            cardImages.Clear();
            bound = null;
        }

        private void Bind()
        {
            if (!BattleSource) BattleSource = FindObjectOfType<StageOneApp>();
            if (!Panel) Panel = GetComponent<GothicHudPanel>();
            if (!Presentation) Presentation = GetComponent<ModularHudController>();
            if (!Counter) Counter = GetComponentInChildren<MechanicalRoundCounter>(true);
            if (QuickItems == null || QuickItems.Length < 4 || QuickItems.All(b => !b)) ResolveQuickItems();
            // The quick-item slots led to the deleted item panel. The page keeps its authored shape, but
            // the group is switched off rather than rewired to another screen.
            if (quickItemRoot) quickItemRoot.SetActive(false);
            if (bound != BattleSource)
            {
                if (bound) { bound.Changed -= Sync; bound.BattleEventAnimation = null; }
                bound = BattleSource;
                if (bound) bound.Changed += Sync;
            }
            if (bound) bound.BattleEventAnimation = AnimateEvents;
            if (hooked) return;
            hooked = true;
            if (Panel)
            {
                Panel.OnSkillSelected.AddListener(SelectSkill);
                Panel.OnPartySelected.AddListener(SelectParty);
            }
            if (Presentation && Presentation.DisplacementButton)
                Presentation.DisplacementButton.onClick.AddListener(SelectSwap);
            // Wired here rather than as persistent listeners: the prefab is built in a scene that has no
            // StageOneApp yet, so only a runtime reference can reach one. The old in-prefab sheet is kept
            // only as serialized history; the authored console owns every readout and command now.
            if (DebugButton)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DebugButton.onClick.AddListener(IndustrialDebugConsole.ToggleFromHud);
#else
                DebugButton.gameObject.SetActive(false);
#endif
                var caption = DebugButton.GetComponentInChildren<Text>(true);
                if (caption) caption.text = "调试台";
            }
            foreach (var card in Cards())
                if (card && card.Hit) card.Hit.onClick.AddListener(() => SelectCard(card));
        }

        // The authored quick-item slots carry a Button on their "Item Frame" child. They are found by name
        // rather than referenced from the prefab so the prefab keeps its existing shape.
        private void ResolveQuickItems()
        {
            QuickItems = new Button[4];
            var group = GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Items - 快捷物品");
            if (group == null) return;
            quickItemRoot = group.gameObject;
            for (int i = 0; i < 4; i++)
            {
                var slot = group.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "Item " + (i + 1));
                if (slot != null) QuickItems[i] = slot.GetComponentInChildren<Button>(true);
            }
        }

        private IEnumerable<GothicBattleCard> Cards()
        {
            foreach (var card in Allies) yield return card;
            foreach (var card in Enemies) yield return card;
        }

        // ---- input, routed straight into the existing app commands ----
        private void SelectSkill(int index) { if (BattleSource) BattleSource.SelectSkill(index); }
        private void SelectSwap() { if (BattleSource) BattleSource.SelectSwap(); }
        private void SelectCard(GothicBattleCard card)
        {
            if (BattleSource && card && !string.IsNullOrEmpty(card.ActorId)) BattleSource.SelectActor(card.ActorId);
        }
        private void SelectParty(int index)
        {
            var app = BattleSource;
            if (!app || app.Run == null || index < 0 || index >= app.Run.Heroes.Count) return;
            app.OpenCharacterPanel(index);
        }

        // ---- the single sync path, driven by StageOneApp.Changed ----
        public void Sync()
        {
            var app = BattleSource;
            if (!app || app.Run == null) return;
            var battle = app.Battle;
            bool live = battle != null && !app.AwaitingNode && battle.Outcome == BattleOutcome.Active;
            foreach (var chrome in ChromeRoots) if (chrome) chrome.SetActive(live);
            // The old sheet is never shown: the authored console replaced it. Hiding it here also keeps
            // a stale prefab from covering the laid-out page if the scene was saved while it was open.
            if (DebugRoot) DebugRoot.SetActive(false);
            if (!live) return;

            SyncPresentation(app);
            if (Counter) Counter.SetRound(battle.Round);
            SyncSkills(app, battle);
            SyncStage(app, battle);
        }

        private void SyncPresentation(StageOneApp app)
        {
            if (!Presentation) return;
            for (int i = 0; i < Presentation.Characters.Length && i < app.Run.Heroes.Count; i++)
            {
                var source = app.Run.Heroes[i];
                var target = Presentation.Characters[i];
                if (target == null || source == null) continue;
                target.Name = source.Name;
                target.Subtitle = source.Definition.Subtitle;
                target.Health = source.Hp;
                target.MaxHealth = Mathf.Max(1, source.Definition.MaxHp);
                target.Sanity = (float)source.Sanity;
                if (target.Limbs == null) continue;
                for (int p = 0; p < target.Limbs.Length && p < 4; p++)
                {
                    var part = (LimbPart)p;
                    var limb = LimbSystem.Get(source, part);
                    var slot = target.Limbs[p];
                    if (limb == null || slot == null) continue;
                    slot.Maximum = Mathf.Max(1, limb.MaxHp);
                    slot.Current = Mathf.Clamp(limb.Hp, 0, slot.Maximum);
                    slot.Broken = LimbSystem.ConditionOf(source, part) == LimbCondition.Severed;
                }
            }
            int selected = app.Run.Heroes.FindIndex(h => h.Id == app.InspectedId);
            if (selected >= 0) Presentation.SelectedCharacter = selected;
            Presentation.RefreshView();
        }

        private void SyncSkills(StageOneApp app, BattleEngine battle)
        {
            if (!Panel) return;
            // Only the acting hero's own form is on offer, exactly like the built-in page. Technicians end up
            // with four basic skills plus their special, which is the five the authored slots hold.
            var skills = battle.Current != null && battle.Current.Player
                ? battle.SkillsFor(battle.Current)
                : Array.Empty<SkillSpec>();

            for (int i = 0; i < Panel.SkillButtons.Length; i++)
            {
                bool has = i < skills.Length;
                string reason = has ? battle.SkillDisabledReason(i) : null;
                var button = Panel.SkillButtons[i];
                if (button) button.interactable = has && reason == null && !app.Busy;
                if (Panel.SkillSelections != null && i < Panel.SkillSelections.Length && Panel.SkillSelections[i])
                    Panel.SkillSelections[i].SetActive(app.PendingSkill == i);
            }

            var swap = Presentation ? Presentation.DisplacementButton : null;
            if (swap) swap.interactable = battle.SwapDisabledReason() == null && !app.Busy;

            if (!Panel.DescriptionTitle || !Panel.DescriptionBody) return;
            if (app.PendingSkill.HasValue && app.PendingSkill.Value < skills.Length)
            {
                var skill = skills[app.PendingSkill.Value];
                Panel.DescriptionTitle.text = skill.Name;
                Panel.DescriptionBody.text = Detail(app, battle, battle.Current, skill) + "\n\n" + (app.Hint ?? "");
            }
            else if (app.PendingSwap)
            {
                Panel.DescriptionTitle.text = "换位";
                Panel.DescriptionBody.text = "与相邻队员交换位置，消耗本次行动。\n\n" + (app.Hint ?? "");
            }
            else
            {
                // This page has no action title, so the description panel is the only place a blocked turn
                // can say why. Without it an all-grey skill row tells the player nothing.
                Panel.DescriptionTitle.text = "技能介绍";
                Panel.DescriptionBody.text = BlockedReason(app, battle, skills)
                    ?? (app.Hint ?? "点击技能槽选择技能，再点击高亮目标。\n\n图例：●可选 ○不可；斜杠前为己方站位，斜杠后为目标位置（阵营见效果说明），1→4号位。");
            }
        }

        // Null when at least one skill can be used right now.
        private static string BlockedReason(StageOneApp app, BattleEngine battle, SkillSpec[] skills)
        {
            if (battle.Current == null) return "本场战斗已结束。";
            if (!battle.Current.Player) return "等待敌方行动  ·  " + battle.Current.Name;
            if (app.Busy) return "行动结算中…";
            for (int i = 0; i < skills.Length; i++)
                if (battle.SkillDisabledReason(i) == null) return null;
            return skills.Length == 0 ? "本回合没有可用技能，可以尝试换位。" : battle.SkillDisabledReason(0);
        }

        // Same wording as the built-in battle page, so a skill reads identically on either UI.
        private static string Detail(StageOneApp app, BattleEngine battle, ActorState actor, SkillSpec skill)
        {
            string count = skill.Uses > 0 ? " · 剩" + actor.UsesLeft(skill) + "次" : "";
            string value = skill.Effect == SkillEffect.Attack ? "伤害 " + battle.SkillValue(actor, skill) + " · "
                : skill.Effect == SkillEffect.Heal ? "恢复 " + battle.SkillValue(actor, skill) + " · " : "";
            // The first description line holds the position/target symbols and has to stay alone.
            // Put the live value and remaining uses on the effect line instead.
            int split = skill.Description.IndexOf('\n');
            if (split < 0) return value + skill.Description + count;
            return skill.Description.Substring(0, split) + "\n"
                + value + skill.Description.Substring(split + 1) + count;
        }

        private void SyncStage(StageOneApp app, BattleEngine battle)
        {
            var possible = new HashSet<string>();
            if (app.PendingSwap) foreach (var target in battle.SwapTargets()) possible.Add(target.Id);
            else if (app.PendingSkill.HasValue) foreach (var target in battle.Targets(app.PendingSkill.Value)) possible.Add(target.Id);

            for (int rank = 0; rank < Allies.Length; rank++)
            {
                var card = Allies[rank];
                if (!card) continue;
                var hero = app.Run.Heroes.FirstOrDefault(h => h.Rank == rank);
                card.Render(hero, hero != null && battle.Current == hero, hero != null && possible.Contains(hero.Id),
                    hero != null && app.InspectedId == hero.Id, null);
                // Keep the card body so the damage flash has something to tint; this page owns that map now.
                if (hero != null && card.Background) cardImages[hero.Id] = card.Background;
            }
            if (Enemies == null || Enemies.Length == 0) return;
            // A living enemy owns as many consecutive cards as its Width. The owner's card is stretched
            // over them and the covered cards are switched off, so a two- or three-rank boss reads as one
            // body on the stage. Matching by rank first keeps the formation readable; anything that does
            // not fit (a collision left by debug tools, or a rank outside 1-4) takes the first consecutive
            // run of free cards instead of disappearing. Corpses own no slot.
            float step = EnemyCardStep();
            var living = battle.Enemies.Where(e => !e.Dead)
                .OrderBy(e => e.Rank).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
            var owners = new ActorState[Enemies.Length];
            var claimed = new bool[Enemies.Length];
            foreach (var enemy in living)
            {
                int width = Mathf.Clamp(enemy.Width, 1, Enemies.Length);
                int start = FindSpan(claimed, enemy.Rank, width);
                // A wide monster can only fail to fit when every remaining run is split by narrower units.
                // Falling back to one card is better than hiding the unit entirely.
                if (start < 0 && width > 1) { width = 1; start = FindSpan(claimed, enemy.Rank, width); }
                if (start < 0) continue;
                owners[start] = enemy;
                for (int i = 0; i < width; i++) claimed[start + i] = true;
            }
            for (int rank = 0; rank < Enemies.Length; rank++)
            {
                var card = Enemies[rank];
                if (!card) continue;
                card.ResetSpan();
                var enemy = owners[rank];
                if (enemy == null)
                {
                    // A card covered by a wider neighbour must be invisible; a genuinely empty rank just
                    // renders blank. Render(null) covers both, but SetActive keeps the covered card from
                    // ever flashing its old contents while the wide card is being rebuilt.
                    if (claimed[rank]) card.gameObject.SetActive(false);
                    else card.Render(null, false, false, false, null);
                    continue;
                }
                card.Render(enemy, battle.Current == enemy, possible.Contains(enemy.Id),
                    app.InspectedId == enemy.Id, battle.Intent(enemy));
                card.SetSpan(Mathf.Clamp(enemy.Width, 1, Enemies.Length), step);
                if (card.Background) cardImages[enemy.Id] = card.Background;
            }
        }

        // The authored layout places enemy cards one step apart. Reading that step from the cards keeps
        // the span correct if the layout is re-tuned; 161 is ModularHudBuilder's current CardStep.
        private float EnemyCardStep()
        {
            if (Enemies != null && Enemies.Length > 1 && Enemies[0] && Enemies[1])
            {
                var first = Enemies[0].transform as RectTransform;
                var second = Enemies[1].transform as RectTransform;
                if (first && second)
                {
                    float delta = Mathf.Abs(second.anchoredPosition.x - first.anchoredPosition.x);
                    if (delta > 1f) return delta;
                }
            }
            return 161f;
        }

        // Prefer the enemy's actual rank, then the first free consecutive run. A wide unit therefore keeps
        // its authored position unless another unit already blocks part of its span.
        private static int FindSpan(bool[] claimed, int preferred, int width)
        {
            if (preferred >= 0 && preferred + width <= claimed.Length && IsFree(claimed, preferred, width))
                return preferred;
            for (int start = 0; start + width <= claimed.Length; start++)
                if (IsFree(claimed, start, width)) return start;
            return -1;
        }

        private static bool IsFree(bool[] claimed, int start, int width)
        {
            for (int i = 0; i < width; i++)
                if (claimed[start + i]) return false;
            return true;
        }

        // StageOneApp asks the bound page to flash the cards between two events. The old code-generated
        // view used to do this; the HUD now owns both the cards and the animation.
        private IEnumerator AnimateEvents(BattleEvent[] events, bool fast)
        {
            var hits = events.Where(e => e.Kind == "damage" || e.Kind == "heal").ToArray();
            if (hits.Length == 0) { yield return new WaitForSeconds(fast ? 0.01f : 0.12f); yield break; }
            foreach (var e in hits)
            {
                Image img;
                if (e.TargetId == null || !cardImages.TryGetValue(e.TargetId, out img) || !img) continue;
                Color saved = img.color;
                img.color = e.Kind == "heal" ? new Color32(0x39, 0x72, 0x74, 0xFF) : new Color32(0x80, 0x3F, 0x39, 0xFF);
                yield return new WaitForSeconds(fast ? 0.01f : 0.18f);
                if (img) img.color = saved;
            }
        }
    }
}
