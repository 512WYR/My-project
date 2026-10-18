using UnityEngine;
using UnityEngine.EventSystems;

namespace GameMake.CharacterUI
{
    public sealed class LimbInstallHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Plus;
        public void OnPointerEnter(PointerEventData data) { if(Plus) Plus.SetActive(true); }
        public void OnPointerExit(PointerEventData data) { if(Plus) Plus.SetActive(false); }
        void OnDisable() { if(Plus) Plus.SetActive(false); }
    }
}
