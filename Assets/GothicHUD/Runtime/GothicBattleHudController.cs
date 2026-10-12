using System;
using System.Collections.Generic;
using System.Linq;
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

        [Header("Debug sheet / 调试页")]
        [Tooltip("Readouts and commands that have no place on the authored layout yet. Off by default.")]
        public GameObject DebugRoot;
        public Button DebugButton;
        public Button DebugCloseButton;
        public Button HelpButton;
        public Button LabButton;
        public Button NewRunButton;
        public Text PlayerSanityText;
        public Text FloorText;
        public Text TurnText;
        public Text HintText;
        public Text DebugIntentText;

        private StageOneApp bound;
        private bool hooked;
        private bool debugOpen;

        private void OnEnable() { Bind(); Sync(); }
        private void Start() { Bind(); Sync(); }
        private void OnDisable()
        {
            if (bound) bound.Changed -= Sync;
            bound = null;
        }

        private void Bind()
        {
            if (!BattleSource) BattleSource = FindObjectOfType<StageOneApp>();
            if (!Panel) Panel = GetComponent<GothicHudPanel>();
            if (!Presentation) Presentation = GetComponent<ModularHudController>();
            if (!Counter) Counter = GetComponentInChildren<MechanicalRoundCounter>(true);
            if (bound != BattleSource)
            {
                if (bound) bound.Changed -= Sync;
                bound = BattleSource;
                if (bound) bound.Changed += Sync;
            }
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
            // StageOneApp yet, so only a runtime reference can reach one.
            if (DebugButton) DebugButton.onClick.AddListener(() => SetDebugOpen(!debugOpen));
            if (DebugCloseButton) DebugCloseButton.onClick.AddListener(() => SetDebugOpen(false));
            if (HelpButton) HelpButton.onClick.AddListener(() => { if (BattleSource) BattleSource.ToggleHelp(); });
            if (LabButton) LabButton.onClick.AddListener(() => { if (BattleSource) BattleSource.ToggleLab(); });
            if (NewRunButton) NewRunButton.onClick.AddListener(NewRun);
            foreach (var card in Cards())
                if (card && card.Hit) card.Hit.onClick.AddListener(() => SelectCard(card));
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
        private void NewRun() { SetDebugOpen(false); if (BattleSource) BattleSource.NewRun(); }

        private void SetDebugOpen(bool open)
        {
            debugOpen = open;
            if (DebugRoot) DebugRoot.SetActive(open);
            Sync();
        }

        private void SelectParty(int index)
        {
            var app = BattleSource;
            if (!app || app.Run == null || index < 0 || index >= app.Run.Heroes.Count) return;
            app.SelectActor(app.Run.Heroes[index].Id);
        }

        // ---- the single sync path, driven by StageOneApp.Changed ----
        public void Sync()
        {
            var app = BattleSource;
            if (!app || app.Run == null) return;
            var battle = app.Battle;
            bool live = battle != null && !app.AwaitingNode && battle.Outcome == BattleOutcome.Active
                && !app.HelpOpen && !app.LabOpen;
            foreach (var chrome in ChromeRoots) if (chrome) chrome.SetActive(live);
            if (DebugRoot) DebugRoot.SetActive(live && debugOpen);
            if (!live) return;

            SyncPresentation(app);
            if (Counter) Counter.SetRound(battle.Round);
            SyncSkills(app, battle);
            SyncStage(app, battle);
            if (debugOpen) SyncDebug(app, battle);
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
                Panel.DescriptionBody.text = BlockedReason(app, battle, skills) ?? (app.Hint ?? "点击技能槽选择技能，再点击高亮目标。");
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
            return value + skill.Description + count;
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
                // Hand the card body over so the damage flash still has something to tint: the built-in page
                // fills StageOneView's card map, and nothing else would on this page.
                if (hero != null && app.View) app.View.RegisterCardImage(hero.Id, card.Background);
            }
            for (int rank = 0; rank < Enemies.Length; rank++)
            {
                var card = Enemies[rank];
                if (!card) continue;
                var enemy = battle.Enemies.FirstOrDefault(e => e.Rank == rank);
                card.Render(enemy, enemy != null && battle.Current == enemy, enemy != null && possible.Contains(enemy.Id),
                    enemy != null && app.InspectedId == enemy.Id, enemy == null ? null : battle.Intent(enemy));
                if (enemy != null && app.View) app.View.RegisterCardImage(enemy.Id, card.Background);
            }
        }

        // The readouts and commands that have nowhere to live on the authored layout yet. Kept on one sheet
        // so the battle page only shows positions, vitals and skills.
        private void SyncDebug(StageOneApp app, BattleEngine battle)
        {
            if (PlayerSanityText) PlayerSanityText.text = "玩家理智  " + app.Run.PlayerSanity.ToString("0.0") + " / 100";
            if (FloorText)
                FloorText.text = "第 " + app.Map.Floor + " 层  ·  第 " + app.Run.Battles + " 场  ·  第 " + battle.Round + " 回合";
            if (TurnText)
                TurnText.text = battle.Current == null ? "本场已结束"
                    : (battle.Current.Player ? "我方行动  ·  " : "敌方行动  ·  ") + battle.Current.Name;
            if (HintText) HintText.text = app.Hint ?? "";
            if (DebugIntentText)
                DebugIntentText.text = string.Join("\n",
                    battle.Enemies.Where(e => !e.Dead).Select(e => e.Name + "  ·  " + battle.Intent(e)));
        }
    }
}
