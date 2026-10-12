using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// One option slot inside a node page's option block. The event options, the opening buffs and the
    /// supply actions all share it, so a page only has to write text and a state into slots that already
    /// exist. The option block is redrawn on every StageOneApp refresh, so this never rebuilds anything.
    /// </summary>
    public sealed class GothicNodeOption : MonoBehaviour
    {
        [Header("Wiring / 引用")]
        public Button Hit;
        public Text Title;
        public Text Detail;
        public Outline Accent;
        public GameObject Selected;

        // Filled by the controller so a click can be routed back without the slot knowing which page it
        // is standing on.
        [HideInInspector] public int Index;

        public void Render(string title, string detail, Color accent, bool selected, bool interactable)
        {
            gameObject.SetActive(true);
            if (Title) Title.text = title;
            if (Detail) Detail.text = detail;
            if (Accent) { Accent.effectColor = accent; Accent.enabled = true; }
            if (Selected) Selected.SetActive(selected);
            if (Hit) Hit.interactable = interactable;
        }

        public void Hide() { gameObject.SetActive(false); }
    }
}
