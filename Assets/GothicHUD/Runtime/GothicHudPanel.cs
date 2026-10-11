using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>References to authored scene objects. Never rebuilds or replaces the UI.</summary>
    public sealed class GothicHudPanel : MonoBehaviour
    {
        [Header("Portraits / 头像")]
        public Image MainPortrait;
        public Image[] PartyPortraits;
        public Button[] PartyButtons;
        public GameObject[] PartySelections;
        [Header("Character / 角色")]
        public Text CharacterName;
        public Text CharacterSubtitle;
        public Text[] AttributeValues;
        public Text[] EquipmentValues;
        [Header("Skills / 技能")]
        public Button[] SkillButtons;
        public Image[] SkillIcons;
        public Text[] SkillHotkeys;
        public GameObject[] SkillSelections;
        [Header("Description / 说明")]
        public Text DescriptionTitle;
        public Text DescriptionBody;
        [Header("Events / 供游戏逻辑绑定")]
        public UnityEvent<int> OnPartySelected = new UnityEvent<int>();
        public UnityEvent<int> OnSkillSelected = new UnityEvent<int>();

        private void Awake()
        {
            for (int i = 0; i < PartyButtons.Length; i++)
            {
                int index = i;
                PartyButtons[i].onClick.AddListener(() => SelectParty(index));
            }
            for (int i = 0; i < SkillButtons.Length; i++)
            {
                int index = i;
                SkillButtons[i].onClick.AddListener(() => SelectSkill(index));
            }
        }

        public void SelectParty(int index)
        {
            if (index < 0 || index >= PartySelections.Length) return;
            for (int i = 0; i < PartySelections.Length; i++) PartySelections[i].SetActive(i == index);
            OnPartySelected.Invoke(index);
        }

        public void SelectSkill(int index)
        {
            if (index < 0 || index >= SkillSelections.Length) return;
            for (int i = 0; i < SkillSelections.Length; i++) SkillSelections[i].SetActive(i == index);
            OnSkillSelected.Invoke(index);
        }

        public void SetDescription(string title, string body)
        {
            DescriptionTitle.text = title;
            DescriptionBody.text = body;
        }
    }
}
