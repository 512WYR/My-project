using System;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    public enum HudLimb { Head, Torso, Arm, Leg }

    [Serializable]
    public sealed class HudLimbState
    {
        [Min(0)] public float Current = 100;
        [Min(1)] public float Maximum = 100;
        public bool Broken;
        public bool IsBroken => Broken || Current <= 0;
    }

    [Serializable]
    public sealed class HudCharacterState
    {
        public string Name = "角色名称";
        public string Subtitle = "职业 / 称号";
        public Sprite Portrait;
        [Min(0)] public float Health = 100;
        [Min(1)] public float MaxHealth = 100;
        [Range(0, 100)] public float Sanity = 75;
        public HudLimbState[] Limbs = { new HudLimbState(), new HudLimbState(), new HudLimbState(), new HudLimbState() };
    }

    [Serializable]
    public sealed class HudLimbView
    {
        public HudLimb Part;
        public Sprite HealthySprite;
        public Sprite BrokenSprite;
        public Image Icon;
        public Image Fill;
        public Text Value;
        public Text Status;
    }

    /// <summary>Updates existing authored objects; never rebuilds the editable scene hierarchy.</summary>
    public sealed class ModularHudController : MonoBehaviour
    {
        public GothicHudPanel Panel;
        [Header("角色数据 / 可在 Inspector 预览")]
        public HudCharacterState[] Characters = { new HudCharacterState(), new HudCharacterState(), new HudCharacterState(), new HudCharacterState() };
        [Range(0, 3)] public int SelectedCharacter;
        [Header("生命与理智")]
        public Image HealthFill;
        public Image SanityFill;
        public Text HealthValue;
        public Text SanityValue;
        [Header("按头部、躯干、手臂、腿部绑定")]
        public HudLimbView[] Limbs = new HudLimbView[4];
        [Header("位移 / 接入现有换位或自定义事件")]
        public Button DisplacementButton;
        public UnityEvent OnDisplacementRequested = new UnityEvent();
        [Header("自动读取现有战斗的角色生命、理智与名称")]
        public bool FollowStageOneBattle = true;
        public StageOneApp BattleSource;
        private float nextSync;
        private RunState lastRun;

        private void Awake()
        {
            if (Panel) Panel.OnPartySelected.AddListener(SelectCharacter);
            if (DisplacementButton) DisplacementButton.onClick.AddListener(RequestDisplacement);
        }
        private void OnEnable() { RefreshView(); }
        private void OnValidate() { RefreshView(); }
        private void OnDestroy()
        {
            if (Panel) Panel.OnPartySelected.RemoveListener(SelectCharacter);
            if (DisplacementButton) DisplacementButton.onClick.RemoveListener(RequestDisplacement);
        }
        private void Update()
        {
            if (!FollowStageOneBattle || Time.unscaledTime < nextSync) return;
            nextSync = Time.unscaledTime + .1f;
            if (!BattleSource) BattleSource = FindObjectOfType<StageOneApp>();
            if (!BattleSource || BattleSource.Run == null) return;
            if (lastRun != BattleSource.Run)
            {
                if (lastRun != null)
                    foreach (var character in Characters)
                        foreach (var limb in character.Limbs) { limb.Current = limb.Maximum; limb.Broken = false; }
                lastRun = BattleSource.Run;
            }
            for (int i = 0; i < Characters.Length && i < lastRun.Heroes.Count; i++)
            {
                var source = lastRun.Heroes[i]; var target = Characters[i];
                target.Name = source.Name; target.Subtitle = source.Definition.Subtitle;
                // Fill the authored portrait slot from Resources/StageOne/HeroArt/<Id> when the slot has
                // not been given custom art by hand.
                if (!target.Portrait) target.Portrait = ActorArt.PortraitSprite(source.Definition);
                target.Health = source.Hp; target.MaxHealth = source.MaxHp; target.Sanity = (float)source.Sanity;
                if (source.Id == BattleSource.InspectedId) SelectedCharacter = i;
            }
            RefreshView();
        }

        public void SelectCharacter(int index)
        {
            if (Characters == null || index < 0 || index >= Characters.Length) return;
            SelectedCharacter = index;
            if (FollowStageOneBattle && BattleSource && BattleSource.Run != null && index < BattleSource.Run.Heroes.Count)
                BattleSource.SelectActor(BattleSource.Run.Heroes[index].Id);
            RefreshView();
        }

        public void RequestDisplacement()
        {
            if (FollowStageOneBattle && BattleSource) BattleSource.RequestConfirmOrSwap();
            OnDisplacementRequested.Invoke();
        }

        public void SetVitals(int character, float health, float maximum, float sanity)
        {
            if (!ValidCharacter(character)) return;
            var data = Characters[character]; data.MaxHealth = Mathf.Max(1, maximum);
            data.Health = Mathf.Clamp(health, 0, data.MaxHealth); data.Sanity = Mathf.Clamp(sanity, 0, 100);
            if (character == SelectedCharacter) RefreshView();
        }
        public void SetLimbHealth(int character, HudLimb part, float current, float maximum)
        {
            var limb = GetLimb(character, part); if (limb == null) return;
            limb.Maximum = Mathf.Max(1, maximum); limb.Current = Mathf.Clamp(current, 0, limb.Maximum);
            limb.Broken = limb.Current <= 0;
            if (character == SelectedCharacter) RefreshView();
        }
        public void SetLimbBroken(int character, HudLimb part, bool broken)
        {
            var limb = GetLimb(character, part); if (limb == null) return;
            limb.Broken = broken;
            if (broken) limb.Current = 0;
            else if (limb.Current <= 0) limb.Current = Mathf.Max(1, limb.Maximum);
            if (character == SelectedCharacter) RefreshView();
        }
        public HudLimbState GetLimb(int character, HudLimb part)
        {
            int index = (int)part;
            if (!ValidCharacter(character) || index < 0 || index >= 4) return null;
            var data = Characters[character].Limbs;
            return data != null && index < data.Length ? data[index] : null;
        }
        private bool ValidCharacter(int character) => Characters != null && character >= 0 && character < Characters.Length && Characters[character] != null;

        [ContextMenu("Preview / Break selected character arm")]
        public void PreviewBrokenArm() { SetLimbBroken(SelectedCharacter, HudLimb.Arm, true); }
        [ContextMenu("Preview / Restore selected character limbs")]
        public void RestoreSelectedLimbs() { for (int i = 0; i < 4; i++) SetLimbBroken(SelectedCharacter, (HudLimb)i, false); }

        public void RefreshView()
        {
            if (Characters == null || Characters.Length == 0) return;
            SelectedCharacter = Mathf.Clamp(SelectedCharacter, 0, Characters.Length - 1);
            var actor = Characters[SelectedCharacter]; if (actor == null) return;
            if (HealthFill) HealthFill.fillAmount = Mathf.Clamp01(actor.Health / Mathf.Max(1, actor.MaxHealth));
            if (SanityFill) SanityFill.fillAmount = Mathf.Clamp01(actor.Sanity / 100);
            if (HealthValue) HealthValue.text = Mathf.Max(0, actor.Health).ToString("0") + " / " + Mathf.Max(1, actor.MaxHealth).ToString("0");
            if (SanityValue) SanityValue.text = Mathf.Clamp(actor.Sanity, 0, 100).ToString("0") + " / 100";
            if (Panel)
            {
                if (Panel.CharacterName) Panel.CharacterName.text = actor.Name;
                if (Panel.CharacterSubtitle) Panel.CharacterSubtitle.text = actor.Subtitle;
                SetPortrait(Panel.MainPortrait, actor.Portrait);
                for (int i = 0; i < Panel.PartyPortraits.Length && i < Characters.Length; i++) SetPortrait(Panel.PartyPortraits[i], Characters[i].Portrait);
                for (int i = 0; i < Panel.PartySelections.Length; i++) if (Panel.PartySelections[i]) Panel.PartySelections[i].SetActive(i == SelectedCharacter);
            }
            if (Limbs == null) return;
            foreach (var view in Limbs)
            {
                if (view == null) continue;
                var state = GetLimb(SelectedCharacter, view.Part); if (state == null) continue;
                bool broken = state.IsBroken;
                if (view.Icon) view.Icon.sprite = broken ? view.BrokenSprite : view.HealthySprite;
                if (view.Fill) { view.Fill.fillAmount = broken ? 0 : Mathf.Clamp01(state.Current / Mathf.Max(1, state.Maximum)); view.Fill.color = new Color32(164, 56, 39, 255); }
                if (view.Value) view.Value.text = (broken ? 0 : Mathf.Max(0, state.Current)).ToString("0") + " / " + Mathf.Max(1, state.Maximum).ToString("0");
                if (view.Status) { view.Status.text = broken ? "已损坏" : state.Current < state.Maximum ? "受伤" : "完好"; view.Status.color = broken ? new Color32(226, 92, 64, 255) : new Color32(167, 145, 108, 255); }
            }
        }
        static void SetPortrait(Image image, Sprite sprite)
        {
            if (!image) return;
            image.sprite = sprite; image.color = sprite ? Color.white : Color.clear;
        }
    }
}
