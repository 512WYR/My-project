using Ruin.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// One candidate position on the route page. The controller writes into it and toggles it on and off;
    /// it never rebuilds its own hierarchy, because the route is redrawn on every StageOneApp refresh.
    /// </summary>
    public sealed class GothicRouteNode : MonoBehaviour
    {
        [Header("Wiring / 引用")]
        public Button Hit;
        public Text Name;
        public Text Detail;
        public Outline Accent;

        // Filled by the controller so a click can be routed back without the card knowing about the route.
        [HideInInspector] public int Index;

        public void Render(NodeState node, string detail, Color accent, bool interactable)
        {
            if (node == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            if (Name) Name.text = node.Name;
            if (Detail) Detail.text = detail;
            if (Accent) { Accent.effectColor = accent; Accent.enabled = true; }
            if (Hit) Hit.interactable = interactable;
        }
    }
}
