using Ruin.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // Attached to the raycast plate under a character. It owns no UI of its own: it only tells the
    // shared tooltip which actor is hovered, so the cut-in and the persistent card show identical data.
    public sealed class BattleStatusHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public ActorState Actor { get; private set; }

        // The card's own selection button. A status plate is a separate raycast target hovering over
        // the lower part of the portrait, so without forwarding, clicking the feet would silently stop
        // selecting the character.
        public Button ForwardClick;

        public void Bind(ActorState actor)
        {
            if (ReferenceEquals(Actor, actor))
            {
                if (actor != null && BattleStatusTooltip.IsShowing(actor)) BattleStatusTooltip.Refresh(actor);
                return;
            }

            var previous = Actor;
            Actor = actor;
            if (previous != null && BattleStatusTooltip.IsShowing(previous)) BattleStatusTooltip.Hide(previous);
            if (actor != null && BattleStatusTooltip.IsShowing(actor)) BattleStatusTooltip.Refresh(actor);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Actor != null && eventData != null)
                BattleStatusTooltip.Show(Actor, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Actor != null) BattleStatusTooltip.Hide(Actor);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (ForwardClick == null || !ForwardClick.IsActive() || !ForwardClick.interactable) return;
            ForwardClick.onClick.Invoke();
        }

        private void OnDisable()
        {
            if (Actor != null) BattleStatusTooltip.Hide(Actor);
        }
    }
}
